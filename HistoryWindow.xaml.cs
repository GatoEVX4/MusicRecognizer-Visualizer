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
            
            // Inscrever-se nos eventos de mudança
            DataManager.Instance.HistoryChanged += OnHistoryChanged;
            DataManager.Instance.RecommendationsChanged += OnRecommendationsChanged;
        }

        private void OnHistoryChanged(object sender, EventArgs e)
        {
            // Atualizar UI na thread principal
            Dispatcher.InvokeAsync(() =>
            {
                Logger.Log("History updated, refreshing UI...", ConsoleColor.Cyan);
                LoadHistory();
                UpdateLastUpdateText();
            });
        }

        private void OnRecommendationsChanged(object sender, EventArgs e)
        {
            // Atualizar UI na thread principal
            Dispatcher.InvokeAsync(() =>
            {
                Logger.Log("Recommendations updated, refreshing UI...", ConsoleColor.Cyan);
                LoadRecommendations();
                UpdateLastUpdateText();
            });
        }

        private void UpdateLastUpdateText()
        {
            LastUpdateText.Text = $"Atualizado: {DateTime.Now:HH:mm:ss}";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
            UpdateLastUpdateText();
            Logger.Log("Manual refresh triggered", ConsoleColor.Yellow);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Desinscrever dos eventos ao fechar a janela
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
                // Não define ItemsSource se vazio, usa Items diretamente
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
                // Não define ItemsSource se vazio, usa Items diretamente
                RecommendationsList.ItemsSource = null;
                RecommendationsList.Items.Clear();
                
                var emptyMessage = new TextBlock
                {
                    Text = "Nenhuma recomendação disponível ainda.\n\nReconheça a mesma música 3 vezes para receber recomendações!",
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
            // Animação sutil de fade para indicar atualização
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

                // Abrir no Shazam
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

                // Buscar no Spotify
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

                // Buscar no YouTube
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

                // Abrir no Shazam
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

                // Abrir no Spotify
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

                // Abrir no Apple Music
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

                // Buscar no YouTube
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

