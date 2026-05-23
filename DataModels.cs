using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace Music
{
    public enum DownloadStatus { Queued, Downloading, Completed, Failed }

    public class DownloadedTrack : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string TrackId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string? CoverArtUrl { get; set; }

        private DownloadStatus _status;
        public DownloadStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                Notify(nameof(Status));
                Notify(nameof(StatusText));
                Notify(nameof(IsDownloading));
                Notify(nameof(IsCompleted));
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; Notify(nameof(Progress)); Notify(nameof(StatusText)); }
        }

        public string? FilePath { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }

        public bool IsDownloading => Status == DownloadStatus.Downloading;
        public bool IsCompleted  => Status == DownloadStatus.Completed;
        public bool IsQueued     => Status == DownloadStatus.Queued;

        public string StatusText => Status switch
        {
            DownloadStatus.Queued      => "Queued",
            DownloadStatus.Downloading => $"Downloading... {Progress:F0}%",
            DownloadStatus.Completed   => "Completed",
            DownloadStatus.Failed      => "Failed",
            _                          => ""
        };
    }


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
        public bool HasRecommendations => RecognitionCount >= 3;

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

    // Unified model: merges RecognizedTrack + DownloadedTrack info
    public class MusicItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Genre { get; set; } = "";
        public string ReleaseYear { get; set; } = "";
        public string CoverArtUrl { get; set; } = "";
        public DateTime LastRecognizedAt { get; set; }
        public int RecognitionCount { get; set; }
        public string? JoeColorString { get; set; }
        public bool HasRecommendations => RecognitionCount >= 3;

        private DownloadStatus? _downloadStatus;
        public DownloadStatus? DownloadStatus
        {
            get => _downloadStatus;
            set
            {
                _downloadStatus = value;
                Notify(nameof(DownloadStatus));
                Notify(nameof(DownloadStatusText));
                Notify(nameof(HasDownload));
                Notify(nameof(IsDownloading));
                Notify(nameof(IsDownloaded));
                Notify(nameof(IsQueued));
            }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; Notify(nameof(DownloadProgress)); Notify(nameof(DownloadStatusText)); }
        }

        public string? FilePath { get; set; }
        public string? ErrorMessage { get; set; }

        public bool HasDownload => _downloadStatus.HasValue;
        public bool IsDownloading => _downloadStatus == Music.DownloadStatus.Downloading;
        public bool IsDownloaded => _downloadStatus == Music.DownloadStatus.Completed;
        public bool IsQueued => _downloadStatus == Music.DownloadStatus.Queued;
        public bool HasFile => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);

        public string DownloadStatusText => _downloadStatus switch
        {
            Music.DownloadStatus.Queued      => "Queued",
            Music.DownloadStatus.Downloading => $"Downloading {DownloadProgress:F0}%",
            Music.DownloadStatus.Completed   => "On PC",
            Music.DownloadStatus.Failed      => "Failed",
            _                                => ""
        };

        public static MusicItem FromRecognizedTrack(RecognizedTrack track, DownloadedTrack? download = null)
        {
            var item = new MusicItem
            {
                Id = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Genre = track.Genre ?? "",
                ReleaseYear = track.ReleaseYear ?? "",
                CoverArtUrl = track.CoverArtUrl ?? "",
                LastRecognizedAt = track.RecognizedAt,
                RecognitionCount = track.RecognitionCount,
                JoeColorString = track.JoeColorString
            };
            if (download != null)
            {
                item.DownloadStatus = download.Status;
                item.DownloadProgress = download.Progress;
                item.FilePath = download.FilePath;
                item.ErrorMessage = download.ErrorMessage;
            }
            return item;
        }
    }

    // Enriched recommendation: RecommendedTrack + optional history/download data
    // when the track already exists in the user's DB.
    public class ForYouItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // From recommendation
        public string Key             { get; set; } = "";
        public string Title           { get; set; } = "";
        public string Artist          { get; set; } = "";
        public string CoverArtUrl     { get; set; } = "";
        public string? ShazamUrl      { get; set; }
        public string? SpotifySearchUri { get; set; }
        public int    OccurrenceCount { get; set; }   // score: how many seeds recommended it

        // Enriched from history (populated when Key exists in DB)
        public bool InHistory        { get; set; }
        public int  RecognitionCount { get; set; }

        // Download state
        private DownloadStatus? _downloadStatus;
        public DownloadStatus? DownloadStatus
        {
            get => _downloadStatus;
            set
            {
                _downloadStatus = value;
                Notify(nameof(DownloadStatus));
                Notify(nameof(DownloadStatusText));
                Notify(nameof(IsDownloading));
                Notify(nameof(IsDownloaded));
                Notify(nameof(IsQueued));
            }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; Notify(nameof(DownloadProgress)); Notify(nameof(DownloadStatusText)); }
        }

        public string? FilePath { get; set; }

        public bool IsDownloaded  => _downloadStatus == Music.DownloadStatus.Completed;
        public bool IsDownloading => _downloadStatus == Music.DownloadStatus.Downloading;
        public bool IsQueued      => _downloadStatus == Music.DownloadStatus.Queued;
        public bool HasFile       => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);

        public string DownloadStatusText => _downloadStatus switch
        {
            Music.DownloadStatus.Queued      => "Queued",
            Music.DownloadStatus.Downloading => $"Downloading {DownloadProgress:F0}%",
            Music.DownloadStatus.Completed   => "On PC",
            Music.DownloadStatus.Failed      => "Failed",
            _                                => ""
        };
    }

    public class AppData
    {
        public List<RecognizedTrack> History { get; set; } = new List<RecognizedTrack>();
        public Dictionary<string, List<RecommendedTrack>> SimilarTracks { get; set; } = new Dictionary<string, List<RecommendedTrack>>();
        public AppSettings Settings { get; set; } = new AppSettings();
    }

    public class AppSettings
    {
        public int  RecognitionDelayMin        { get; set; } = 2200;
        public int  RecognitionDelayMax        { get; set; } = 5500;
        public int  VisualizerBars             { get; set; } = 32;
        public int  VisualizerFps              { get; set; } = 60;
        public bool EnableDiscordRichPresence  { get; set; } = true;
        public bool SaveHistory                { get; set; } = true;
        public int  MaxHistoryItems            { get; set; } = 1000;
        public int  MaxConcurrentDownloads     { get; set; } = 2;
        public string DownloadsFolder          { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MusicRecognizer");
    }
}




