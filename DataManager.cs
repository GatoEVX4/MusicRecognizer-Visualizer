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

        public event EventHandler HistoryChanged;
        public event EventHandler RecommendationsChanged;

        private DataManager()
        {
            _db = new DatabaseManager();
            _settings = _db.GetSettings();
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

            var track = new RecognizedTrack(result);
            
            var existingCount = _db.GetRecognitionCount(result.Id);
            if (existingCount > 0)
            {
                track.RecognitionCount = existingCount + 1;
            }

            _db.AddOrUpdateHistory(track);
            OnHistoryChanged();
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

        public List<RecognizedTrack> GetHistory(int count = 100)
        {
            return _db.GetHistory(count);
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
        }

        public void UpdateSettings(Action<AppSettings> updateAction)
        {
            updateAction(_settings);
            _db.SaveSettings(_settings);
        }
    }
}

