using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Music
{
    public class DataManager
    {
        private static DataManager _instance;
        private static readonly object _lock = new object();

        private readonly DatabaseManager _db;
        private AppSettings _settings;

        // Tracks seen exactly once since startup — held in memory, not yet persisted.
        // Only written to the DB on the second recognition.
        private readonly HashSet<string> _pendingFirstSeen = new();

        public event EventHandler HistoryChanged;
        public event EventHandler RecommendationsChanged;
        public event EventHandler DownloadsChanged;

        private DataManager()
        {
            _db = new DatabaseManager();
            _settings = _db.GetSettings();
            _db.DeleteSingleRecognitionTracks();
            DownloadService.Instance.DownloadsListChanged += (s, e) => DownloadsChanged?.Invoke(s, e);
        }

        public static DataManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new DataManager();
                    }
                    return _instance;
                }
            }
        }

        public AppSettings Settings => _settings;

        protected virtual void OnHistoryChanged()
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnRecommendationsChanged()
        {
            RecommendationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddToHistory(API.ShazamResult result)
        {
            if (!_settings.SaveHistory)
                return;

            var existingCount = _db.GetRecognitionCount(result.Id);

            if (existingCount == 0)
            {
                // Not in DB yet — first recognition: park in memory and wait for a second hit.
                if (!_pendingFirstSeen.Contains(result.Id))
                {
                    _pendingFirstSeen.Add(result.Id);
                    return;
                }

                // Second recognition — save to DB for the first time with count = 2.
                _pendingFirstSeen.Remove(result.Id);
            }

            var track = new RecognizedTrack(result)
            {
                RecognitionCount = existingCount > 0 ? existingCount + 1 : 2
            };

            _db.AddOrUpdateHistory(track);
            OnHistoryChanged();

            if (track.RecognitionCount == 10)
                DownloadService.Instance.Enqueue(track);
        }

        public int GetRecognitionCount(string trackId)
        {
            return _db.GetRecognitionCount(trackId);
        }

        public void SaveSimilarTracks(string trackId, List<RecommendedTrack> recommendations)
        {
            _db.SaveRecommendations(trackId, recommendations);
            OnRecommendationsChanged();
        }

        public List<RecommendedTrack> GetTopRecommendations(int count = 50)
        {
            return _db.GetTopRecommendations(count);
        }

        public List<RecommendedTrack> GetRecommendationsForTrack(string trackId, int count = 50)
        {
            return _db.GetRecommendationsForTrack(trackId, count);
        }

        public List<RecognizedTrack> GetHistory(int count = 100)
        {
            return _db.GetHistory(count);
        }

        public List<DownloadedTrack> GetDownloads()      => DownloadService.Instance.GetDownloads();
        public List<DownloadedTrack> GetDownloadQueue()   => DownloadService.Instance.GetQueue(10);

        private Dictionary<string, DownloadedTrack> GetDownloadMap()
        {
            return DownloadService.Instance.GetDownloads()
                .GroupBy(d => d.TrackId)
                .ToDictionary(g => g.Key, g => g.First());
        }

        public List<MusicItem> GetMusicItems(int count = 500)
        {
            var history = _db.GetHistory(count);
            var dlMap = GetDownloadMap();
            return history.Select(t => MusicItem.FromRecognizedTrack(t,
                dlMap.TryGetValue(t.Id, out var d) ? d : null)).ToList();
        }

        // Context-aware recommendations: seeds = most recent N distinct tracks, scored by
        // how many seeds share the same recommendation.
        // Tracks in history are NOT excluded — they are enriched with recognition/download data.
        // Falls back to global top recommendations if seeds have no saved recs yet.
        public List<ForYouItem> GetContextualRecommendations(int seedCount = 5, int resultCount = 20)
        {
            var recentSeeds = _db.GetHistory(seedCount * 2)
                .DistinctBy(t => t.Id)
                .Take(seedCount)
                .ToList();

            if (!recentSeeds.Any())
                return MapToForYouItems(_db.GetTopRecommendations(resultCount));

            // score[key] = (track, how many seeds recommended it)
            var scores = new Dictionary<string, (RecommendedTrack track, int score)>();

            foreach (var seed in recentSeeds)
            {
                var recs = _db.GetRecommendationsForTrack(seed.Id, 20);
                foreach (var rec in recs)
                {
                    if (scores.TryGetValue(rec.Key, out var existing))
                        scores[rec.Key] = (existing.track, existing.score + 1);
                    else
                        scores[rec.Key] = (rec, 1);
                }
            }

            if (!scores.Any())
                return MapToForYouItems(_db.GetTopRecommendations(resultCount));

            var topRecs = scores.Values
                .OrderByDescending(x => x.score)
                .ThenByDescending(x => x.track.OccurrenceCount)
                .Take(resultCount)
                .Select(x => { x.track.OccurrenceCount = x.score; return x.track; })
                .ToList();

            return MapToForYouItems(topRecs);
        }

        private List<ForYouItem> MapToForYouItems(List<RecommendedTrack> recs)
        {
            var historyMap = _db.GetHistory(10000).ToDictionary(t => t.Id);
            var dlMap      = GetDownloadMap();

            return recs.Select(rec =>
            {
                var item = new ForYouItem
                {
                    Key              = rec.Key,
                    Title            = rec.Title,
                    Artist           = rec.Artist,
                    CoverArtUrl      = rec.CoverArtUrl ?? "",
                    ShazamUrl        = rec.ShazamUrl,
                    SpotifySearchUri = rec.SpotifySearchUri,
                    OccurrenceCount  = rec.OccurrenceCount,
                };

                if (historyMap.TryGetValue(rec.Key, out var hist))
                {
                    item.InHistory        = true;
                    item.RecognitionCount = hist.RecognitionCount;

                    if (dlMap.TryGetValue(rec.Key, out var dl))
                    {
                        item.DownloadStatus   = dl.Status;
                        item.DownloadProgress = dl.Progress;
                        item.FilePath         = dl.FilePath;
                    }
                }

                return item;
            }).ToList();
        }

        // Songs recognized recently with high count — "what you've been jamming lately"
        public List<MusicItem> GetTrendingNow(int count = 14)
        {
            var history = _db.GetHistory(1000);
            var dlMap = GetDownloadMap();
            var cutoff = DateTime.Now.AddDays(-30);
            return history
                .Where(t => t.RecognizedAt >= cutoff)
                .OrderByDescending(t => t.RecognitionCount)
                .ThenByDescending(t => t.RecognizedAt)
                .Take(count)
                .Select(t => MusicItem.FromRecognizedTrack(t, dlMap.TryGetValue(t.Id, out var d) ? d : null))
                .ToList();
        }

        // Songs you listened to a lot but haven't heard in a while
        public List<MusicItem> GetHaventHeardInAWhile(int count = 14)
        {
            var history = _db.GetHistory(1000);
            var dlMap = GetDownloadMap();
            var cutoff = DateTime.Now.AddDays(-14);
            return history
                .Where(t => t.RecognizedAt < cutoff && t.RecognitionCount >= 3)
                .OrderByDescending(t => t.RecognitionCount)
                .Take(count)
                .Select(t => MusicItem.FromRecognizedTrack(t, dlMap.TryGetValue(t.Id, out var d) ? d : null))
                .ToList();
        }

        public List<MusicItem> SearchMusicItems(string query, int count = 500)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetMusicItems(count);
            var allItems = GetMusicItems(5000);
            var q = query.ToLowerInvariant();
            return allItems
                .Where(t => t.Title.ToLowerInvariant().Contains(q) ||
                            t.Artist.ToLowerInvariant().Contains(q))
                .Take(count)
                .ToList();
        }

        public void DeleteMusicItem(string trackId)
        {
            // Grab file path before removing records
            var dl = DownloadService.Instance.GetDownloads().FirstOrDefault(d => d.TrackId == trackId);
            var filePath = dl?.FilePath;

            _db.DeleteTrack(trackId);
            DownloadService.Instance.RemoveFromList(trackId);

            if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
            {
                try { System.IO.File.Delete(filePath); }
                catch { /* ignore — file may be locked */ }
            }

            OnHistoryChanged();
            DownloadsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearHistory()
        {
            _db.ClearAllData();
            OnHistoryChanged();
            OnRecommendationsChanged();
        }

        public void SaveSettings(AppSettings settings)
        {
            _settings = settings;
            _db.SaveSettings(settings);
            // Apply concurrency setting immediately — no restart needed
            DownloadService.Instance.MaxConcurrentDownloads =
                Math.Clamp(settings.MaxConcurrentDownloads, 1, 10);
            // If the limit was raised, try to fill the new slots right away
            DownloadService.Instance.TryStartDownloadsPublic();
        }

        public void UpdateSettings(Action<AppSettings> updateAction)
        {
            updateAction(_settings);
            _db.SaveSettings(_settings);
        }
    }
}

