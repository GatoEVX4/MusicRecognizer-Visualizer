using System;
using System.Windows;

namespace Music
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var s = DataManager.Instance.Settings;

            DelayMinTextBox.Text          = s.RecognitionDelayMin.ToString();
            DelayMaxTextBox.Text          = s.RecognitionDelayMax.ToString();
            VisualizerBarsTextBox.Text    = s.VisualizerBars.ToString();
            VisualizerFpsTextBox.Text     = s.VisualizerFps.ToString();
            SaveHistoryCheckBox.IsChecked = s.SaveHistory;
            MaxHistoryTextBox.Text        = s.MaxHistoryItems.ToString();
            DownloadDirTextBox.Text       = s.DownloadsFolder;
            DiscordRpcCheckBox.IsChecked  = s.EnableDiscordRichPresence;

            ConcurrentDownloadsSlider.Value = Math.Clamp(s.MaxConcurrentDownloads, 1, 10);
        }

        private void ConcurrentDownloadsSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (ConcurrentDownloadsLabel != null)
                ConcurrentDownloadsLabel.Text = ((int)ConcurrentDownloadsSlider.Value).ToString();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new AppSettings();

                if (int.TryParse(DelayMinTextBox.Text, out int delayMin))
                    settings.RecognitionDelayMin = Math.Max(1000, delayMin);

                if (int.TryParse(DelayMaxTextBox.Text, out int delayMax))
                    settings.RecognitionDelayMax = Math.Max(delayMin, delayMax);

                if (int.TryParse(VisualizerBarsTextBox.Text, out int bars))
                    settings.VisualizerBars = Math.Clamp(bars, 8, 128);

                if (int.TryParse(VisualizerFpsTextBox.Text, out int fps))
                    settings.VisualizerFps = Math.Clamp(fps, 15, 120);

                settings.SaveHistory = SaveHistoryCheckBox.IsChecked ?? true;

                if (int.TryParse(MaxHistoryTextBox.Text, out int maxHistory))
                    settings.MaxHistoryItems = Math.Max(10, maxHistory);

                settings.DownloadsFolder = DownloadDirTextBox.Text;

                settings.EnableDiscordRichPresence = DiscordRpcCheckBox.IsChecked ?? true;
                settings.MaxConcurrentDownloads    = Math.Clamp((int)ConcurrentDownloadsSlider.Value, 1, 10);

                DataManager.Instance.SaveSettings(settings);

                MessageBox.Show("Settings saved successfully!\n\nSome changes may require application restart.",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all history?\nThis action cannot be undone.",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DataManager.Instance.ClearHistory();
                MessageBox.Show("History cleared successfully!", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}



