using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace Music
{
    public class DatabaseManager
    {
        private static readonly string DatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MusicRecognizer",
            "music.db"
        );

        private readonly string _connectionString;

        public DatabaseManager()
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={DatabasePath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS History (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    Genre TEXT,
                    ReleaseYear TEXT,
                    Isrc TEXT,
                    CoverArtUrl TEXT,
                    JoeColorString TEXT,
                    RecognizedAt TEXT NOT NULL,
                    RecognitionCount INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS Recommendations (
                    Key TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    CoverArtUrl TEXT,
                    ShazamUrl TEXT,
                    SpotifySearchUri TEXT,
                    AppleMusicUri TEXT
                );

                CREATE TABLE IF NOT EXISTS TrackRecommendations (
                    TrackId TEXT NOT NULL,
                    RecommendationKey TEXT NOT NULL,
                    PRIMARY KEY (TrackId, RecommendationKey),
                    FOREIGN KEY (RecommendationKey) REFERENCES Recommendations(Key)
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_history_recognized 
                    ON History(RecognizedAt DESC);
                
                CREATE INDEX IF NOT EXISTS idx_track_recommendations 
                    ON TrackRecommendations(TrackId);
            ";
            command.ExecuteNonQuery();

            Logger.Log("Database initialized successfully", ConsoleColor.Green);
        }

        public void AddOrUpdateHistory(RecognizedTrack track)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO History (Id, Title, Artist, Genre, ReleaseYear, Isrc, CoverArtUrl, JoeColorString, RecognizedAt, RecognitionCount)
                VALUES ($id, $title, $artist, $genre, $year, $isrc, $cover, $color, $time, $count)
                ON CONFLICT(Id) DO UPDATE SET
                    RecognitionCount = RecognitionCount + 1,
                    RecognizedAt = $time;
            ";
            
            command.Parameters.AddWithValue("$id", track.Id);
            command.Parameters.AddWithValue("$title", track.Title);
            command.Parameters.AddWithValue("$artist", track.Artist);
            command.Parameters.AddWithValue("$genre", track.Genre ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$year", track.ReleaseYear ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$isrc", track.Isrc ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$cover", track.CoverArtUrl ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$color", track.JoeColorString ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$time", track.RecognizedAt.ToString("o"));
            command.Parameters.AddWithValue("$count", track.RecognitionCount);

            command.ExecuteNonQuery();
        }

        public List<RecognizedTrack> GetHistory(int limit = 500)
        {
            var history = new List<RecognizedTrack>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Title, Artist, Genre, ReleaseYear, Isrc, CoverArtUrl, JoeColorString, RecognizedAt, RecognitionCount
                FROM History
                ORDER BY RecognizedAt DESC
                LIMIT $limit;
            ";
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                history.Add(new RecognizedTrack
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Artist = reader.GetString(2),
                    Genre = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ReleaseYear = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Isrc = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CoverArtUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                    JoeColorString = reader.IsDBNull(7) ? null : reader.GetString(7),
                    RecognizedAt = DateTime.Parse(reader.GetString(8)),
                    RecognitionCount = reader.GetInt32(9)
                });
            }

            return history;
        }

        public int GetRecognitionCount(string trackId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT RecognitionCount FROM History WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", trackId);

            var result = command.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        public void SaveRecommendations(string trackId, List<RecommendedTrack> recommendations)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var rec in recommendations)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO Recommendations (Key, Title, Artist, CoverArtUrl, ShazamUrl, SpotifySearchUri, AppleMusicUri)
                        VALUES ($key, $title, $artist, $cover, $shazam, $spotify, $apple)
                        ON CONFLICT(Key) DO UPDATE SET
                            Title = $title,
                            Artist = $artist,
                            CoverArtUrl = $cover,
                            ShazamUrl = $shazam,
                            SpotifySearchUri = $spotify,
                            AppleMusicUri = $apple;
                    ";
                    cmd.Parameters.AddWithValue("$key", rec.Key);
                    cmd.Parameters.AddWithValue("$title", rec.Title);
                    cmd.Parameters.AddWithValue("$artist", rec.Artist);
                    cmd.Parameters.AddWithValue("$cover", rec.CoverArtUrl ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$shazam", rec.ShazamUrl ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$spotify", rec.SpotifySearchUri ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$apple", rec.AppleMusicUri ?? (object)DBNull.Value);
                    cmd.ExecuteNonQuery();

                    var linkCmd = connection.CreateCommand();
                    linkCmd.CommandText = @"
                        INSERT OR IGNORE INTO TrackRecommendations (TrackId, RecommendationKey)
                        VALUES ($trackId, $recKey);
                    ";
                    linkCmd.Parameters.AddWithValue("$trackId", trackId);
                    linkCmd.Parameters.AddWithValue("$recKey", rec.Key);
                    linkCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public List<RecommendedTrack> GetTopRecommendations(int limit = 50)
        {
            var recommendations = new List<RecommendedTrack>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT 
                    r.Key, 
                    r.Title, 
                    r.Artist, 
                    r.CoverArtUrl, 
                    r.ShazamUrl, 
                    r.SpotifySearchUri, 
                    r.AppleMusicUri,
                    COUNT(tr.TrackId) as OccurrenceCount
                FROM Recommendations r
                INNER JOIN TrackRecommendations tr ON r.Key = tr.RecommendationKey
                GROUP BY r.Key, r.Title, r.Artist, r.CoverArtUrl, r.ShazamUrl, r.SpotifySearchUri, r.AppleMusicUri
                ORDER BY OccurrenceCount DESC
                LIMIT $limit;
            ";
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                recommendations.Add(new RecommendedTrack
                {
                    Key = reader.GetString(0),
                    Title = reader.GetString(1),
                    Artist = reader.GetString(2),
                    CoverArtUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ShazamUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    SpotifySearchUri = reader.IsDBNull(5) ? null : reader.GetString(5),
                    AppleMusicUri = reader.IsDBNull(6) ? null : reader.GetString(6),
                    OccurrenceCount = reader.GetInt32(7)
                });
            }

            return recommendations;
        }

        public void ClearAllData()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM TrackRecommendations;
                DELETE FROM Recommendations;
                DELETE FROM History;
            ";
            command.ExecuteNonQuery();

            Logger.Log("All data cleared from database", ConsoleColor.Yellow);
        }

        public AppSettings GetSettings()
        {
            var settings = new AppSettings();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Key, Value FROM Settings;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.GetString(1);

                switch (key)
                {
                    case "RecognitionDelayMin":
                        settings.RecognitionDelayMin = int.Parse(value);
                        break;
                    case "RecognitionDelayMax":
                        settings.RecognitionDelayMax = int.Parse(value);
                        break;
                    case "VisualizerBars":
                        settings.VisualizerBars = int.Parse(value);
                        break;
                    case "VisualizerFps":
                        settings.VisualizerFps = int.Parse(value);
                        break;
                    case "EnableDiscordRichPresence":
                        settings.EnableDiscordRichPresence = bool.Parse(value);
                        break;
                    case "SaveHistory":
                        settings.SaveHistory = bool.Parse(value);
                        break;
                    case "MaxHistoryItems":
                        settings.MaxHistoryItems = int.Parse(value);
                        break;
                }
            }

            return settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();

            try
            {
                void SaveSetting(string key, string value)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO Settings (Key, Value) 
                        VALUES ($key, $value)
                        ON CONFLICT(Key) DO UPDATE SET Value = $value;
                    ";
                    cmd.Parameters.AddWithValue("$key", key);
                    cmd.Parameters.AddWithValue("$value", value);
                    cmd.ExecuteNonQuery();
                }

                SaveSetting("RecognitionDelayMin", settings.RecognitionDelayMin.ToString());
                SaveSetting("RecognitionDelayMax", settings.RecognitionDelayMax.ToString());
                SaveSetting("VisualizerBars", settings.VisualizerBars.ToString());
                SaveSetting("VisualizerFps", settings.VisualizerFps.ToString());
                SaveSetting("EnableDiscordRichPresence", settings.EnableDiscordRichPresence.ToString());
                SaveSetting("SaveHistory", settings.SaveHistory.ToString());
                SaveSetting("MaxHistoryItems", settings.MaxHistoryItems.ToString());

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}

