using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Music
{
    public partial class TrackDetailWindow : Window
    {
        private readonly MusicItem _track;

        public TrackDetailWindow(MusicItem track)
        {
            InitializeComponent();
            _track = track;
            Title  = $"{track.Title} — Music Recognizer";

            PopulateInfo();
            LoadRecommendations();

            DataManager.Instance.DownloadsChanged += OnDownloadsChanged;
        }

        private void OnDownloadsChanged(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var downloads = DataManager.Instance.GetDownloads();
                var dl = downloads.FirstOrDefault(d => d.TrackId == _track.Id);
                if (dl != null)
                {
                    _track.DownloadStatus   = dl.Status;
                    _track.DownloadProgress = dl.Progress;
                    _track.FilePath         = dl.FilePath;
                    _track.ErrorMessage     = dl.ErrorMessage;
                }
                UpdateDownloadStatus();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            DataManager.Instance.DownloadsChanged -= OnDownloadsChanged;
            base.OnClosed(e);
        }

        // ── Info ─────────────────────────────────────────────────────────────

        private void PopulateInfo()
        {
            // Cover art — use disk cache via converter
            CoverImage.Source = CoverArtConverter.Instance
                .Convert(_track.CoverArtUrl, typeof(System.Windows.Media.ImageSource), null,
                         System.Globalization.CultureInfo.InvariantCulture)
                as System.Windows.Media.ImageSource;

            TitleText.Text  = _track.Title;
            ArtistText.Text = _track.Artist;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_track.Genre))       parts.Add(_track.Genre);
            if (!string.IsNullOrWhiteSpace(_track.ReleaseYear)) parts.Add(_track.ReleaseYear);
            MetaText.Text       = string.Join("  •  ", parts);
            MetaText.Visibility = parts.Any() ? Visibility.Visible : Visibility.Collapsed;

            var n = _track.RecognitionCount;
            RecognitionText.Text = $"🎵  Recognized {n} {(n == 1 ? "time" : "times")}";
            LastSeenText.Text    = $"🕐  {_track.LastRecognizedAt:dd/MM/yyyy  HH:mm}";

            UpdateDownloadStatus();
        }

        private void UpdateDownloadStatus()
        {
            if (_track.IsDownloaded)
            {
                DownloadBadge.Background            = new SolidColorBrush(Color.FromRgb(20, 83, 45));
                DownloadIcon.Text                   = "✓";
                DownloadIcon.Foreground             = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                DownloadLabel.Text                  = _track.HasFile ? "On your PC" : "Downloaded (file not found)";
                DownloadLabel.Foreground            = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                DownloadProgressBar.Visibility      = Visibility.Collapsed;
                PlayBtn.IsEnabled                   = _track.HasFile;
                OpenFolderBtn.IsEnabled             = !string.IsNullOrEmpty(_track.FilePath);
            }
            else if (_track.IsDownloading)
            {
                DownloadBadge.Background            = new SolidColorBrush(Color.FromRgb(120, 53, 15));
                DownloadIcon.Text                   = "⬇";
                DownloadIcon.Foreground             = new SolidColorBrush(Color.FromRgb(253, 230, 138));
                DownloadLabel.Text                  = $"Downloading... {_track.DownloadProgress:F0}%";
                DownloadLabel.Foreground            = new SolidColorBrush(Color.FromRgb(253, 230, 138));
                DownloadProgressBar.Value           = _track.DownloadProgress;
                DownloadProgressBar.Visibility      = Visibility.Visible;
                PlayBtn.IsEnabled                   = false;
                OpenFolderBtn.IsEnabled             = false;
            }
            else if (_track.IsQueued)
            {
                DownloadBadge.Background            = new SolidColorBrush(Color.FromRgb(30, 58, 95));
                DownloadIcon.Text                   = "⏳";
                DownloadIcon.Foreground             = new SolidColorBrush(Color.FromRgb(147, 197, 253));
                DownloadLabel.Text                  = "Queued for download";
                DownloadLabel.Foreground            = new SolidColorBrush(Color.FromRgb(147, 197, 253));
                DownloadProgressBar.Visibility      = Visibility.Collapsed;
                PlayBtn.IsEnabled                   = false;
                OpenFolderBtn.IsEnabled             = false;
            }
            else
            {
                // Not yet queued — show recognition progress toward threshold
                DownloadBadge.Background            = new SolidColorBrush(Color.FromRgb(31, 41, 55));
                DownloadIcon.Text                   = "🎵";
                DownloadIcon.Foreground             = new SolidColorBrush(Color.FromRgb(107, 114, 128));
                var remaining = Math.Max(0, 20 - _track.RecognitionCount);
                DownloadLabel.Text                  = remaining > 0
                    ? $"Missing {remaining} recognition{(remaining == 1 ? "" : "s")} for auto download"
                    : "Waiting for download queue";
                DownloadLabel.Foreground            = new SolidColorBrush(Color.FromRgb(107, 114, 128));
                DownloadProgressBar.Visibility      = Visibility.Collapsed;
                PlayBtn.IsEnabled                   = false;
                OpenFolderBtn.IsEnabled             = false;
            }
        }

        // ── Recommendations ──────────────────────────────────────────────────

        private void LoadRecommendations()
        {
            var recs = DataManager.Instance.GetRecommendationsForTrack(_track.Id, 30);
            if (recs.Any())
            {
                RecList.ItemsSource      = recs;
                RecCount.Text            = $"({recs.Count})";
                RecEmpty.Visibility      = Visibility.Collapsed;
            }
            else
            {
                RecCount.Text       = "";
                RecEmpty.Visibility = Visibility.Visible;
            }
        }

        // ── Buttons ──────────────────────────────────────────────────────────

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_track.FilePath) && File.Exists(_track.FilePath))
            {
                try { Process.Start(new ProcessStartInfo(_track.FilePath) { UseShellExecute = true }); }
                catch { }
            }
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_track.FilePath))
            {
                try
                {
                    var arg = File.Exists(_track.FilePath)
                        ? $"/select,\"{_track.FilePath}\""
                        : $"\"{Path.GetDirectoryName(_track.FilePath)}\"";
                    Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void ShazamBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl($"https://www.shazam.com/track/{_track.Id}");
        }

        private void SpotifyBtn_Click(object sender, RoutedEventArgs e)
        {
            var q = Uri.EscapeDataString($"{_track.Title} {_track.Artist}");
            OpenUrl($"spotify:search:{q}");
        }

        private void YouTubeBtn_Click(object sender, RoutedEventArgs e)
        {
            var q = Uri.EscapeDataString($"{_track.Artist} {_track.Title}");
            OpenUrl($"https://www.youtube.com/results?search_query={q}");
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var msg = _track.HasFile
                ? $"Delete \"{_track.Title}\"?\n\nThe history record and audio file will be permanently removed."
                : $"Delete \"{_track.Title}\" from history?";

            if (MessageBox.Show(msg, "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes)
            {
                DataManager.Instance.DeleteMusicItem(_track.Id);
                Close();
            }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        // ── Recommendation card click ─────────────────────────────────────────

        private void RecCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (sender is FrameworkElement fe && fe.Tag is RecommendedTrack track)
            {
                e.Handled = true;
                var menu = new ContextMenu();

                void AddItem(string header, Action action)
                {
                    var item = new MenuItem { Header = header };
                    item.Click += (_, __) => action();
                    menu.Items.Add(item);
                }

                if (!string.IsNullOrEmpty(track.ShazamUrl))
                    AddItem("🔍  Open on Shazam",      () => OpenUrl(track.ShazamUrl));
                if (!string.IsNullOrEmpty(track.SpotifySearchUri))
                    AddItem("🎧  Open on Spotify",     () => OpenUrl(track.SpotifySearchUri));
                if (!string.IsNullOrEmpty(track.AppleMusicUri))
                    AddItem("🍎  Open on Apple Music", () => OpenUrl(track.AppleMusicUri));

                var q = Uri.EscapeDataString($"{track.Artist} {track.Title}");
                AddItem("▶  Search on YouTube", () => OpenUrl($"https://www.youtube.com/results?search_query={q}"));

                menu.IsOpen = true;
            }
        }
    }
}
