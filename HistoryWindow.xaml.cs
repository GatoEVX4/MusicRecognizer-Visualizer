using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Music
{
    public partial class HistoryWindow : Window
    {
        private List<MusicItem>       _allHistory    = new();
        private List<MusicItem>       _searchResults = new();
        private List<ForYouItem>      _forYouItems   = new();
        private int                   _searchPage    = 0;
        private const int             PageSize = 25;

        // Track subscriptions on queue items so we can unsubscribe later
        private readonly List<DownloadedTrack> _queueSubscribed = new();

        public HistoryWindow()
        {
            InitializeComponent();
            LoadAll();

            DataManager.Instance.HistoryChanged         += OnHistoryChanged;
            DataManager.Instance.RecommendationsChanged += OnRecommendationsChanged;
            DataManager.Instance.DownloadsChanged       += OnDownloadsChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            DataManager.Instance.HistoryChanged         -= OnHistoryChanged;
            DataManager.Instance.RecommendationsChanged -= OnRecommendationsChanged;
            DataManager.Instance.DownloadsChanged       -= OnDownloadsChanged;
            UnsubscribeQueueItems();
            base.OnClosed(e);
        }

        // ── Event handlers ───────────────────────────────────────────────────

        // History/recommendations changes → rebuild all sections
        private void OnHistoryChanged(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(LoadAll);

        private void OnRecommendationsChanged(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(LoadAll);

        // Download list changes → only refresh download-related sections
        private void OnDownloadsChanged(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(() => { LoadDownloadQueue(); RefreshDownloadBadgesOnMusicItems(); RefreshForYouDownloadBadges(); });

        // Individual queue item status changed (e.g. Downloading → Completed)
        private void OnQueueItemStatusChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DownloadedTrack.Status))
                Dispatcher.InvokeAsync(LoadDownloadQueue);
        }

        // ── Load ─────────────────────────────────────────────────────────────

        private void LoadAll()
        {
            _allHistory = DataManager.Instance.GetMusicItems(10000);
            LoadTrending();
            LoadForYou();
            LoadSaudade();
            RefreshRecentHistory();
            LoadDownloadQueue();
            UpdateSubtitle();

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                RunSearch(SearchBox.Text);
        }

        private void LoadTrending()
        {
            var items = DataManager.Instance.GetTrendingNow(40);
            TrendingList.ItemsSource      = items;
            TrendingSection.Visibility    = items.Any() ? Visibility.Visible   : Visibility.Collapsed;
            TrendingEmpty.Visibility      = items.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void LoadForYou()
        {
            _forYouItems             = DataManager.Instance.GetContextualRecommendations(seedCount: 5, resultCount: 40);
            ForYouList.ItemsSource   = _forYouItems;
            ForYouSection.Visibility = _forYouItems.Any() ? Visibility.Visible   : Visibility.Collapsed;
            ForYouEmpty.Visibility   = _forYouItems.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshForYouDownloadBadges()
        {
            var dlMap = DataManager.Instance.GetDownloads()
                .GroupBy(d => d.TrackId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in _forYouItems)
            {
                if (dlMap.TryGetValue(item.Key, out var dl))
                {
                    item.DownloadStatus   = dl.Status;
                    item.DownloadProgress = dl.Progress;
                    item.FilePath         = dl.FilePath;
                }
                else
                {
                    item.DownloadStatus = null;
                }
            }
        }

        private void LoadSaudade()
        {
            var items = DataManager.Instance.GetHaventHeardInAWhile(16);
            SaudadeList.ItemsSource       = items;
            SaudadeSection.Visibility     = items.Any() ? Visibility.Visible   : Visibility.Collapsed;
            SaudadeEmpty.Visibility       = items.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshRecentHistory()
        {
            RecentHistoryList.ItemsSource = _allHistory.Take(5).ToList();
            HistoryEmpty.Visibility       = _allHistory.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        // When only download statuses change, update the MusicItem badge data without
        // rebuilding the whole allHistory list (avoids cover-image flicker).
        private void RefreshDownloadBadgesOnMusicItems()
        {
            var dlMap = DataManager.Instance.GetDownloads()
                .GroupBy(d => d.TrackId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in _allHistory)
            {
                if (dlMap.TryGetValue(item.Id, out var dl))
                {
                    item.DownloadStatus   = dl.Status;
                    item.DownloadProgress = dl.Progress;
                    item.FilePath         = dl.FilePath;
                }
                else
                {
                    item.DownloadStatus = null;
                }
            }
        }

        private void UpdateSubtitle()
        {
            var n = _allHistory.Count;
            SubtitleText.Text = n > 0
                ? $"{n} música{(n != 1 ? "s" : "")} na sua coleção"
                : "Nenhuma música reconhecida ainda";
        }

        // ── Download queue ────────────────────────────────────────────────────

        private void LoadDownloadQueue()
        {
            UnsubscribeQueueItems();

            // GetDownloadQueue() returns items in true processing order:
            // [downloading item] then [queued items in FIFO order] — no re-sorting needed.
            var queue = DataManager.Instance.GetDownloadQueue();

            // Subscribe to Status changes so the list reacts immediately without
            // waiting for a DownloadsListChanged event (which only fires on add/remove).
            foreach (var item in queue)
            {
                item.PropertyChanged += OnQueueItemStatusChanged;
                _queueSubscribed.Add(item);
            }

            if (queue.Any())
            {
                DownloadQueueList.ItemsSource     = queue;
                DownloadQueueEmpty.Visibility     = Visibility.Collapsed;
                DownloadQueueCountText.Text       = queue.Count.ToString();
                DownloadQueueSection.Visibility   = Visibility.Visible;
            }
            else
            {
                DownloadQueueList.ItemsSource     = null;
                DownloadQueueEmpty.Visibility     = Visibility.Visible;
                DownloadQueueCountText.Text       = "0";
                DownloadQueueSection.Visibility   = Visibility.Collapsed;
            }
        }

        private void UnsubscribeQueueItems()
        {
            foreach (var item in _queueSubscribed)
                item.PropertyChanged -= OnQueueItemStatusChanged;
            _queueSubscribed.Clear();
        }

        // ── Full history window ───────────────────────────────────────────────

        private void ToggleHistory_Click(object sender, RoutedEventArgs e)
        {
            new AllHistoryWindow(_allHistory) { Owner = this }.Show();
        }

        // ── Search ────────────────────────────────────────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query)
                ? Visibility.Visible : Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(query))
            {
                SectionsPanel.Visibility = Visibility.Visible;
                SearchPanel.Visibility   = Visibility.Collapsed;
                return;
            }

            SectionsPanel.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility   = Visibility.Visible;
            RunSearch(query);
        }

        private void RunSearch(string query)
        {
            _searchResults = DataManager.Instance.SearchMusicItems(query.Trim(), 10000);
            _searchPage    = 0;
            UpdateSearchPage();
        }

        private void UpdateSearchPage()
        {
            var total      = _searchResults.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            if (_searchPage >= totalPages) _searchPage = totalPages - 1;

            var page = _searchResults.Skip(_searchPage * PageSize).Take(PageSize).ToList();
            SearchResultsList.ItemsSource = page;

            if (total > 0)
            {
                SearchResultsHeader.Text     = $"{total} resultado{(total != 1 ? "s" : "")} para \"{SearchBox.Text.Trim()}\"";
                SearchEmpty.Visibility       = Visibility.Collapsed;
                SearchResultsList.Visibility = Visibility.Visible;
            }
            else
            {
                SearchResultsHeader.Text     = "";
                SearchEmpty.Visibility       = Visibility.Visible;
                SearchResultsList.Visibility = Visibility.Collapsed;
            }

            SearchPageInfo.Text     = $"Página {_searchPage + 1} de {totalPages}";
            SearchPrevBtn.IsEnabled = _searchPage > 0;
            SearchNextBtn.IsEnabled = (_searchPage + 1) < totalPages;
            SearchPaginationPanel.Visibility = totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_searchPage > 0) { _searchPage--; UpdateSearchPage(); }
        }

        private void SearchNext_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (int)Math.Ceiling(_searchResults.Count / (double)PageSize);
            if (_searchPage + 1 < totalPages) { _searchPage++; UpdateSearchPage(); }
        }

        // ── Card interactions ─────────────────────────────────────────────────

        private void PlayForYouCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (sender is not FrameworkElement fe || fe.Tag is not ForYouItem item || !item.HasFile) return;
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath!) { UseShellExecute = true }); }
            catch { }
        }

        private void RecommendedCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not ForYouItem rec) return;
            if (e.ChangedButton != MouseButton.Left) return;
            e.Handled = true;

            var url = !string.IsNullOrEmpty(rec.ShazamUrl)        ? rec.ShazamUrl
                    : !string.IsNullOrEmpty(rec.SpotifySearchUri)  ? rec.SpotifySearchUri
                    : null;
            if (url != null)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
        }

        private void PlayCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (sender is not FrameworkElement fe || fe.Tag is not MusicItem item || !item.HasFile) return;
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath!) { UseShellExecute = true }); }
            catch { }
        }

        private void MusicCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not MusicItem item) return;

            if (e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                new TrackDetailWindow(item) { Owner = this }.ShowDialog();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                e.Handled = true;
                ShowItemMenu(item);
            }
        }

        private void ShowItemMenu(MusicItem item)
        {
            var menu = new ContextMenu();

            var open = new MenuItem { Header = "Ver detalhes" };
            open.Click += (_, _) => new TrackDetailWindow(item) { Owner = this }.ShowDialog();
            menu.Items.Add(open);

            menu.Items.Add(new Separator());

            var del = new MenuItem { Header = "🗑  Excluir música" };
            del.Click += (_, _) => ConfirmAndDelete(item);
            menu.Items.Add(del);

            menu.IsOpen = true;
        }

        private void ConfirmAndDelete(MusicItem item)
        {
            var msg = item.HasFile
                ? $"Excluir \"{item.Title}\"?\n\nO registro do histórico e o arquivo de áudio serão removidos permanentemente."
                : $"Excluir \"{item.Title}\" do histórico?";

            if (MessageBox.Show(msg, "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes)
                DataManager.Instance.DeleteMusicItem(item.Id);
        }
    }
}
