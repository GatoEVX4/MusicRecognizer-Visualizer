using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Music
{
    public class RecognizedTrack
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Genre { get; set; }
        public string ReleaseYear { get; set; }
        public string Isrc { get; set; }
        public string CoverArtUrl { get; set; }
        public DateTime RecognizedAt { get; set; }
        public int RecognitionCount { get; set; }
        public string JoeColorString { get; set; }

        public RecognizedTrack() { }

        public RecognizedTrack(API.ShazamResult result)
        {
            Id = result.Id;
            Title = result.Title;
            Artist = result.Artist;
            Genre = result.Genre;
            ReleaseYear = result.ReleaseYear;
            Isrc = result.Isrc;
            CoverArtUrl = result.CoverArtUrl;
            RecognizedAt = DateTime.Now;
            RecognitionCount = 1;
            JoeColorString = result.JoeColor?.ToString();
        }
    }

    public class RecommendedTrack
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string CoverArtUrl { get; set; }
        public string ShazamUrl { get; set; }
        public string SpotifySearchUri { get; set; }
        public string AppleMusicUri { get; set; }
        public int OccurrenceCount { get; set; }
    }

    public class AppData
    {
        public List<RecognizedTrack> History { get; set; } = new List<RecognizedTrack>();
        public Dictionary<string, List<RecommendedTrack>> SimilarTracks { get; set; } = new Dictionary<string, List<RecommendedTrack>>();
        public AppSettings Settings { get; set; } = new AppSettings();
    }

    public class AppSettings
    {
        public int RecognitionDelayMin { get; set; } = 2200;
        public int RecognitionDelayMax { get; set; } = 5500;
        public int VisualizerBars { get; set; } = 32;
        public int VisualizerFps { get; set; } = 60;
        public bool EnableDiscordRichPresence { get; set; } = true;
        public bool SaveHistory { get; set; } = true;
        public int MaxHistoryItems { get; set; } = 1000;
    }
}



