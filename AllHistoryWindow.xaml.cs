using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Music
{
    public partial class AllHistoryWindow : Window
    {
        private enum SortField { DateDesc, DateAsc, CountDesc, CountAsc, NameAsc, NameDesc }

        private List<MusicItem> _allItems;
        private int             _page    = 0;
        private SortField       _sort    = SortField.DateDesc;
        private const int       PageSize = 25;

        public AllHistoryWindow(List<MusicItem> items)
        {
            InitializeComponent();
            _allItems = items;
            UpdateSubtitle();
            UpdatePage();

            DataManager.Instance.HistoryChanged   += OnHistoryChanged;
            DataManager.Instance.DownloadsChanged += OnDownloadsChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            DataManager.Instance.HistoryChanged   -= OnHistoryChanged;
            DataManager.Instance.DownloadsChanged -= OnDownloadsChanged;
            base.OnClosed(e);
        }

        private void OnHistoryChanged(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(() =>
            {
                _allItems = DataManager.Instance.GetMusicItems(10000);
                _page     = 0;
                UpdateSubtitle();
                UpdatePage();
            });

        private void OnDownloadsChanged(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(() =>
            {
                var dlMap = DataManager.Instance.GetDownloads()
                    .GroupBy(d => d.TrackId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var item in _allItems)
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
            });

        // ── Sorting ───────────────────────────────────────────────────────────

        private IEnumerable<MusicItem> ApplySort(IEnumerable<MusicItem> source) => _sort switch
        {
            SortField.DateDesc  => source.OrderByDescending(x => x.LastRecognizedAt),
            SortField.DateAsc   => source.OrderBy(x => x.LastRecognizedAt),
            SortField.CountDesc => source.OrderByDescending(x => x.RecognitionCount),
            SortField.CountAsc  => source.OrderBy(x => x.RecognitionCount),
            SortField.NameAsc   => source.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            SortField.NameDesc  => source.OrderByDescending(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            _                   => source
        };

        private void SetSort(SortField field)
        {
            _sort = field;
            _page = 0;
            UpdateSortButtons();
            UpdatePage();
        }

        private void UpdateSortButtons()
        {
            var inactive = (Style)Resources["SortBtn"];
            var active   = (Style)Resources["SortBtnActive"];

            SortDateDescBtn .Style = _sort == SortField.DateDesc  ? active : inactive;
            SortDateAscBtn  .Style = _sort == SortField.DateAsc   ? active : inactive;
            SortCountDescBtn.Style = _sort == SortField.CountDesc ? active : inactive;
            SortCountAscBtn .Style = _sort == SortField.CountAsc  ? active : inactive;
            SortNameAscBtn  .Style = _sort == SortField.NameAsc   ? active : inactive;
            SortNameDescBtn .Style = _sort == SortField.NameDesc  ? active : inactive;
        }

        private void SortDateDesc_Click (object sender, RoutedEventArgs e) => SetSort(SortField.DateDesc);
        private void SortDateAsc_Click  (object sender, RoutedEventArgs e) => SetSort(SortField.DateAsc);
        private void SortCountDesc_Click(object sender, RoutedEventArgs e) => SetSort(SortField.CountDesc);
        private void SortCountAsc_Click (object sender, RoutedEventArgs e) => SetSort(SortField.CountAsc);
        private void SortNameAsc_Click  (object sender, RoutedEventArgs e) => SetSort(SortField.NameAsc);
        private void SortNameDesc_Click (object sender, RoutedEventArgs e) => SetSort(SortField.NameDesc);

        // ── Pagination ────────────────────────────────────────────────────────

        private void UpdatePage()
        {
            var sorted     = ApplySort(_allItems).ToList();
            var totalPages = Math.Max(1, (int)Math.Ceiling(sorted.Count / (double)PageSize));
            if (_page >= totalPages) _page = totalPages - 1;

            var page = sorted.Skip(_page * PageSize).Take(PageSize).ToList();
            HistoryList.ItemsSource = page;

            EmptyText.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            PageInfoText.Text      = $"Page {_page + 1} of {totalPages}";
            PrevPageBtn.IsEnabled  = _page > 0;
            NextPageBtn.IsEnabled  = (_page + 1) < totalPages;
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 0) { _page--; UpdatePage(); }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (int)Math.Ceiling(_allItems.Count / (double)PageSize);
            if (_page + 1 < totalPages) { _page++; UpdatePage(); }
        }

        private void UpdateSubtitle()
        {
            var n = _allItems.Count;
            SubtitleText.Text = n > 0
                ? $"{n} song{(n != 1 ? "s" : "")} in history"
                : "No music recognized yet";
        }

        // ── Card interactions ─────────────────────────────────────────────────

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

            var open = new MenuItem { Header = "View details" };
            open.Click += (_, _) => new TrackDetailWindow(item) { Owner = this }.ShowDialog();
            menu.Items.Add(open);

            menu.Items.Add(new Separator());

            var del = new MenuItem { Header = "🗑  Delete music" };
            del.Click += (_, _) => ConfirmAndDelete(item);
            menu.Items.Add(del);

            menu.IsOpen = true;
        }

        private void ConfirmAndDelete(MusicItem item)
        {
            var msg = item.HasFile
                ? $"Delete \"{item.Title}\"?\n\nThe history record and audio file will be permanently removed."
                : $"Delete \"{item.Title}\" from history?";

            if (MessageBox.Show(msg, "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes)
                DataManager.Instance.DeleteMusicItem(item.Id);
        }
    }
}
