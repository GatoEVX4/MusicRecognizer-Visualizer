using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Music
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            LoadData();
            
            DataManager.Instance.HistoryChanged += OnHistoryChanged;
            DataManager.Instance.RecommendationsChanged += OnRecommendationsChanged;
        }

        private void OnHistoryChanged(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LoadHistory();
                UpdateLastUpdateText();
            });
        }

        private void OnRecommendationsChanged(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LoadRecommendations();
                UpdateLastUpdateText();
            });
        }

        private void UpdateLastUpdateText()
        {
            LastUpdateText.Text = $"Updated: {DateTime.Now:HH:mm:ss}";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
            UpdateLastUpdateText();
        }

        protected override void OnClosed(EventArgs e)
        {
            DataManager.Instance.HistoryChanged -= OnHistoryChanged;
            DataManager.Instance.RecommendationsChanged -= OnRecommendationsChanged;
            base.OnClosed(e);
        }

        private void LoadData()
        {
            LoadHistory();
            LoadRecommendations();
        }

        private void LoadHistory()
        {
            var history = DataManager.Instance.GetHistory(100);
            
            if (!history.Any())
            {
                HistoryList.ItemsSource = null;
                HistoryList.Items.Clear();
                
                var emptyMessage = new TextBlock
                {
                    Text = "Nenhuma música reconhecida ainda.\nComece a reproduzir música e aguarde o reconhecimento!",
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(20, 50, 20, 20),
                    TextWrapping = TextWrapping.Wrap
                };
                HistoryList.Items.Add(emptyMessage);
            }
            else
            {
                HistoryList.ItemsSource = history;
                AnimateRefresh(HistoryList);
            }
        }

        private void LoadRecommendations()
        {
            var recommendations = DataManager.Instance.GetTopRecommendations(50);
            
            if (!recommendations.Any())
            {
                RecommendationsList.ItemsSource = null;
                RecommendationsList.Items.Clear();
                
                var emptyMessage = new TextBlock
                {
                    Text = "Nenhuma recomendação disponível ainda. Tente escutar alguma coisa!",
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(20, 50, 20, 20),
                    TextWrapping = TextWrapping.Wrap
                };
                RecommendationsList.Items.Add(emptyMessage);
            }
            else
            {
                RecommendationsList.ItemsSource = recommendations;
                AnimateRefresh(RecommendationsList);
            }
        }

        private void AnimateRefresh(UIElement element)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.7,
                Duration = TimeSpan.FromMilliseconds(100)
            };
            
            var fadeIn = new DoubleAnimation
            {
                From = 0.7,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                BeginTime = TimeSpan.FromMilliseconds(100)
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(fadeOut);
            storyboard.Children.Add(fadeIn);
            
            Storyboard.SetTarget(fadeOut, element);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            Storyboard.SetTarget(fadeIn, element);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

            storyboard.Begin();
        }

        private void HistoryTabButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryTabButton.Tag = "Active";
            RecommendationsTabButton.Tag = null;
            HistoryPanel.Visibility = Visibility.Visible;
            RecommendationsPanel.Visibility = Visibility.Collapsed;
        }

        private void RecommendationsTabButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryTabButton.Tag = null;
            RecommendationsTabButton.Tag = "Active";
            HistoryPanel.Visibility = Visibility.Collapsed;
            RecommendationsPanel.Visibility = Visibility.Visible;
        }

        private void HistoryItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is RecognizedTrack track)
            {
                var contextMenu = new ContextMenu();

                var shazamMenuItem = new MenuItem { Header = "Abrir no Shazam" };
                shazamMenuItem.Click += (s, args) =>
                {
                    try
                    {
                        var url = $"https://www.shazam.com/track/{track.Id}";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                };
                contextMenu.Items.Add(shazamMenuItem);

                var spotifyMenuItem = new MenuItem { Header = "Buscar no Spotify" };
                spotifyMenuItem.Click += (s, args) =>
                {
                    try
                    {
                        var query = $"{track.Title} {track.Artist}".Replace(" ", "%20");
                        var url = $"spotify:search:{query}";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                };
                contextMenu.Items.Add(spotifyMenuItem);

                var youtubeMenuItem = new MenuItem { Header = "Buscar no YouTube" };
                youtubeMenuItem.Click += (s, args) =>
                {
                    try
                    {
                        var query = $"{track.Artist} {track.Title}".Replace(" ", "+");
                        var url = $"https://www.youtube.com/results?search_query={query}";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                };
                contextMenu.Items.Add(youtubeMenuItem);

                contextMenu.IsOpen = true;
            }
        }

        private void RecommendationItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is RecommendedTrack track)
            {
                var contextMenu = new ContextMenu();

                if (!string.IsNullOrEmpty(track.ShazamUrl))
                {
                    var shazamMenuItem = new MenuItem { Header = "Abrir no Shazam" };
                    shazamMenuItem.Click += (s, args) =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(track.ShazamUrl) { UseShellExecute = true });
                        }
                        catch { }
                    };
                    contextMenu.Items.Add(shazamMenuItem);
                }

                if (!string.IsNullOrEmpty(track.SpotifySearchUri))
                {
                    var spotifyMenuItem = new MenuItem { Header = "Abrir no Spotify" };
                    spotifyMenuItem.Click += (s, args) =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(track.SpotifySearchUri) { UseShellExecute = true });
                        }
                        catch { }
                    };
                    contextMenu.Items.Add(spotifyMenuItem);
                }

                if (!string.IsNullOrEmpty(track.AppleMusicUri))
                {
                    var appleMenuItem = new MenuItem { Header = "Abrir no Apple Music" };
                    appleMenuItem.Click += (s, args) =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(track.AppleMusicUri) { UseShellExecute = true });
                        }
                        catch { }
                    };
                    contextMenu.Items.Add(appleMenuItem);
                }

                var youtubeMenuItem = new MenuItem { Header = "Buscar no YouTube" };
                youtubeMenuItem.Click += (s, args) =>
                {
                    try
                    {
                        var query = $"{track.Artist} {track.Title}".Replace(" ", "+");
                        var url = $"https://www.youtube.com/results?search_query={query}";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                };
                contextMenu.Items.Add(youtubeMenuItem);

                contextMenu.IsOpen = true;
            }
        }
    }
}

