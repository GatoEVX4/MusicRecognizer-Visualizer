using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Music
{
    public class DataManager
    {
        private static readonly string DataFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MusicRecognizer",
            "data.json"
        );

        private static DataManager _instance;
        private static readonly object _lock = new object();

        public AppData Data { get; private set; }

        // Eventos para notificar mudanças
        public event EventHandler HistoryChanged;
        public event EventHandler RecommendationsChanged;

        private DataManager()
        {
            Data = new AppData();
            Load();
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

        protected virtual void OnHistoryChanged()
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnRecommendationsChanged()
        {
            RecommendationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Load()
        {
            try
            {
                if (File.Exists(DataFilePath))
                {
                    var json = File.ReadAllText(DataFilePath);
                    Data = JsonSerializer.Deserialize<AppData>(json) ?? new AppData();
                }
                else
                {
                    Data = new AppData();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading data: {ex.Message}", ConsoleColor.Red);
                Data = new AppData();
            }
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(DataFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(Data, options);
                File.WriteAllText(DataFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error saving data: {ex.Message}", ConsoleColor.Red);
            }
        }

        public async Task SaveAsync()
        {
            await Task.Run(() => Save());
        }

        public void AddToHistory(API.ShazamResult result)
        {
            if (!Data.Settings.SaveHistory)
                return;

            var existing = Data.History.FirstOrDefault(h => h.Id == result.Id);
            
            if (existing != null)
            {
                existing.RecognitionCount++;
                existing.RecognizedAt = DateTime.Now;
            }
            else
            {
                Data.History.Insert(0, new RecognizedTrack(result));

                // Limitar o histórico ao máximo configurado
                if (Data.History.Count > Data.Settings.MaxHistoryItems)
                {
                    Data.History.RemoveAt(Data.History.Count - 1);
                }
            }

            _ = SaveAsync();
            OnHistoryChanged(); // Notificar mudança
        }

        public int GetRecognitionCount(string trackId)
        {
            var track = Data.History.FirstOrDefault(h => h.Id == trackId);
            return track?.RecognitionCount ?? 0;
        }

        public void SaveSimilarTracks(string trackId, List<RecommendedTrack> recommendations)
        {
            Data.SimilarTracks[trackId] = recommendations;
            _ = SaveAsync();
            OnRecommendationsChanged(); // Notificar mudança
        }

        public List<RecommendedTrack> GetTopRecommendations(int count = 5)
        {
            var allRecommendations = new Dictionary<string, RecommendedTrack>();

            foreach (var trackRecommendations in Data.SimilarTracks.Values)
            {
                foreach (var track in trackRecommendations)
                {
                    if (allRecommendations.ContainsKey(track.Key))
                    {
                        allRecommendations[track.Key].OccurrenceCount++;
                    }
                    else
                    {
                        var newTrack = new RecommendedTrack
                        {
                            Key = track.Key,
                            Title = track.Title,
                            Artist = track.Artist,
                            CoverArtUrl = track.CoverArtUrl,
                            ShazamUrl = track.ShazamUrl,
                            SpotifySearchUri = track.SpotifySearchUri,
                            AppleMusicUri = track.AppleMusicUri,
                            OccurrenceCount = 1
                        };
                        allRecommendations[track.Key] = newTrack;
                    }
                }
            }

            // Filtrar músicas que já estão no histórico
            var historyIds = Data.History.Select(h => h.Id).ToHashSet();
            
            return allRecommendations.Values
                //.Where(r => !historyIds.Contains(r.Key))
                .OrderByDescending(r => r.OccurrenceCount)
                .Take(count)
                .ToList();
        }

        public List<RecognizedTrack> GetHistory(int count = 50)
        {
            return Data.History.Take(count).ToList();
        }

        public void ClearHistory()
        {
            Data.History.Clear();
            Data.SimilarTracks.Clear();
            _ = SaveAsync();
            OnHistoryChanged();
            OnRecommendationsChanged();
        }
    }
}

