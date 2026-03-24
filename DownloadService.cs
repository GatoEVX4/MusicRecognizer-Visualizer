using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Music
{
    public class DownloadService
    {
        private static readonly DownloadService _instance = new();
        public static DownloadService Instance => _instance;

        private readonly DatabaseManager _db = new();
        private readonly List<DownloadedTrack> _downloads = new();
        private readonly Queue<DownloadedTrack> _queue    = new();
        private int  _activeDownloads = 0;
        private readonly object _lock = new();

        /// <summary>Maximum number of simultaneous yt-dlp processes. Updated live from settings.</summary>
        public int MaxConcurrentDownloads { get; set; } = 2;

        /// <summary>Fired only when items are added/removed. Progress updates via INotifyPropertyChanged.</summary>
        public event EventHandler? DownloadsListChanged;

        public static string GetOutputDir() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MusicRecognizer");

        public static string GenerateFileName(string artist, string title)
        {
            var raw     = $"{artist} - {title}";
            var invalid = Path.GetInvalidFileNameChars();
            var sb      = new StringBuilder(raw.Length);
            foreach (var c in raw)
                if (!invalid.Contains(c)) sb.Append(c);
            var result = sb.ToString().Trim().TrimEnd('.');
            if (result.Length > 200) result = result[..200].TrimEnd();
            return string.IsNullOrWhiteSpace(result) ? "Unknown Track" : result;
        }

        // Constructor returns immediately; all heavy work runs on a thread-pool thread.
        private DownloadService() => _ = Task.Run(Initialize);

        private void Initialize()
        {
            // Pick up the persisted concurrency setting before starting any downloads.
            MaxConcurrentDownloads = _db.GetSettings().MaxConcurrentDownloads;

            var outputDir = GetOutputDir();
            Directory.CreateDirectory(outputDir);

            // --- Load existing download records ---
            var saved = _db.GetDownloads();
            var toUpdate = new List<DownloadedTrack>();

            foreach (var d in saved)
            {
                if (string.IsNullOrEmpty(d.FilePath))
                    d.FilePath = Path.Combine(outputDir, GenerateFileName(d.Artist, d.Title) + ".mp3");

                // File existence is the source of truth
                if (File.Exists(d.FilePath))
                {
                    d.Status   = DownloadStatus.Completed;
                    d.Progress = 100;
                }
                else
                {
                    d.Status   = DownloadStatus.Queued;
                    d.Progress = 0;
                }

                toUpdate.Add(d);
            }

            // Batch DB updates outside the lock
            foreach (var d in toUpdate)
                _db.UpdateDownload(d);

            lock (_lock)
            {
                foreach (var d in toUpdate)
                {
                    _downloads.Add(d);
                    if (d.Status == DownloadStatus.Queued)
                        _queue.Enqueue(d);
                }
            }

            // --- Scan history for eligible tracks not yet registered ---
            var eligible  = _db.GetTracksWithMinRecognitions(30);
            var toSave    = new List<DownloadedTrack>();

            foreach (var track in eligible)
            {
                lock (_lock)
                {
                    if (_downloads.Any(d => d.TrackId == track.Id))
                        continue;
                }

                var filePath = Path.Combine(outputDir, GenerateFileName(track.Artist, track.Title) + ".mp3");
                var download = new DownloadedTrack
                {
                    TrackId     = track.Id,
                    Title       = track.Title,
                    Artist      = track.Artist,
                    CoverArtUrl = track.CoverArtUrl,
                    FilePath    = filePath,
                    QueuedAt    = DateTime.Now
                };

                if (File.Exists(filePath))
                {
                    download.Status      = DownloadStatus.Completed;
                    download.Progress    = 100;
                    download.CompletedAt = File.GetLastWriteTime(filePath);
                }
                else
                {
                    download.Status = DownloadStatus.Queued;
                }

                lock (_lock)
                {
                    _downloads.Add(download);
                    if (download.Status == DownloadStatus.Queued)
                        _queue.Enqueue(download);
                }

                toSave.Add(download);
            }

            foreach (var d in toSave)
                _db.SaveDownload(d);

            if (toSave.Count > 0 || saved.Count > 0)
                DownloadsListChanged?.Invoke(this, EventArgs.Empty);

            TryStartDownloads();
        }

        /// <summary>
        /// Returns items in the exact order they will be processed:
        /// the currently-downloading item first, then the queue in FIFO order.
        /// </summary>
        public List<DownloadedTrack> GetQueue(int max = 10)
        {
            lock (_lock)
            {
                var result = new List<DownloadedTrack>();
                // All actively downloading items first (may be >1 with concurrency > 1)
                result.AddRange(_downloads.Where(d => d.Status == DownloadStatus.Downloading));
                // Then the waiting queue in FIFO order
                foreach (var item in _queue)
                    if (result.Count < max) result.Add(item);
                return result.Take(max).ToList();
            }
        }

        public void RemoveFromList(string trackId)
        {
            lock (_lock)
            {
                var item = _downloads.FirstOrDefault(d => d.TrackId == trackId);
                if (item != null) _downloads.Remove(item);
            }
            DownloadsListChanged?.Invoke(this, EventArgs.Empty);
        }

        public List<DownloadedTrack> GetDownloads()
        {
            lock (_lock)
                return _downloads.ToList();
        }

        public void Enqueue(RecognizedTrack track)
        {
            var filePath = Path.Combine(GetOutputDir(), GenerateFileName(track.Artist, track.Title) + ".mp3");

            DownloadedTrack? download = null;
            lock (_lock)
            {
                if (_downloads.Any(d => d.TrackId == track.Id))
                    return;

                download = new DownloadedTrack
                {
                    TrackId     = track.Id,
                    Title       = track.Title,
                    Artist      = track.Artist,
                    CoverArtUrl = track.CoverArtUrl,
                    FilePath    = filePath,
                    QueuedAt    = DateTime.Now
                };

                if (File.Exists(filePath))
                {
                    download.Status      = DownloadStatus.Completed;
                    download.Progress    = 100;
                    download.CompletedAt = File.GetLastWriteTime(filePath);
                }
                else
                {
                    download.Status = DownloadStatus.Queued;
                    _queue.Enqueue(download);
                }

                _downloads.Insert(0, download);
            }

            _db.SaveDownload(download);
            DownloadsListChanged?.Invoke(this, EventArgs.Empty);

            TryStartDownloads();
        }

        /// <summary>Called by DataManager when the concurrency setting is raised at runtime.</summary>
        public void TryStartDownloadsPublic() => TryStartDownloads();

        /// <summary>
        /// Starts as many downloads as allowed by MaxConcurrentDownloads.
        /// Safe to call from any thread; uses the lock to avoid races.
        /// </summary>
        private void TryStartDownloads()
        {
            var toStart = new List<DownloadedTrack>();
            lock (_lock)
            {
                while (_queue.Count > 0 && _activeDownloads < MaxConcurrentDownloads)
                {
                    toStart.Add(_queue.Dequeue());
                    _activeDownloads++;
                }
            }
            foreach (var track in toStart)
                _ = RunDownloadSlot(track);
        }

        private async Task RunDownloadSlot(DownloadedTrack track)
        {
            try   { await DownloadTrackAsync(track); }
            finally
            {
                lock (_lock) _activeDownloads--;
                TryStartDownloads();   // fill the freed slot
            }
        }

        private static readonly Regex ProgressRegex =
            new(@"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);

        private async Task DownloadTrackAsync(DownloadedTrack track)
        {
            if (File.Exists(track.FilePath))
            {
                track.Status      = DownloadStatus.Completed;
                track.Progress    = 100;
                track.CompletedAt = File.GetLastWriteTime(track.FilePath!);
                _db.UpdateDownload(track);
                // INotifyPropertyChanged already notified — no list refresh needed
                return;
            }

            track.Status   = DownloadStatus.Downloading;
            track.Progress = 0;
            _db.UpdateDownload(track);
            // INotifyPropertyChanged on Status/Progress updates the UI automatically

            var ytdlpPath      = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            var basePath       = Path.ChangeExtension(track.FilePath, null);
            var outputTemplate = basePath + ".%(ext)s";
            var query          = $"ytsearch1:{track.Artist} - {track.Title}";
            var args           = $"-x --audio-format mp3 --audio-quality 0 --no-playlist -o \"{outputTemplate}\" \"{query}\"";

            Logger.Log($"[Download] {track.Artist} - {track.Title}", ConsoleColor.Cyan);

            try
            {
                var psi = new ProcessStartInfo(ytdlpPath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };

                using var process = Process.Start(psi)!;

                // Only update the in-memory Progress — INotifyPropertyChanged propagates to UI.
                // No DB write and no list-refresh event on every tick.
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    var m = ProgressRegex.Match(e.Data);
                    if (m.Success && double.TryParse(m.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    {
                        track.Progress = pct; // fires PropertyChanged → ProgressBar updates automatically
                    }
                };

                process.BeginOutputReadLine();
                await process.WaitForExitAsync();

                if (File.Exists(track.FilePath))
                {
                    track.Status      = DownloadStatus.Completed;
                    track.Progress    = 100;
                    track.CompletedAt = DateTime.Now;
                    Logger.Log($"[Download] Done: {track.FilePath}", ConsoleColor.Green);
                }
                else
                {
                    track.Status       = DownloadStatus.Failed;
                    track.ErrorMessage = process.ExitCode != 0
                        ? $"yt-dlp saiu com código {process.ExitCode}"
                        : "Arquivo não encontrado após download";
                    Logger.Log($"[Download] Failed: {track.Artist} - {track.Title}", ConsoleColor.Red);
                }
            }
            catch (Exception ex)
            {
                track.Status       = DownloadStatus.Failed;
                track.ErrorMessage = ex.Message;
                Logger.Log($"[Download] Error: {ex.Message}", ConsoleColor.Red);
            }

            _db.UpdateDownload(track);
            // Notify observers that the list state changed (status transition, not just progress)
            DownloadsListChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
