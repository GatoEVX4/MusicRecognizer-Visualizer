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
            var settings = DataManager.Instance.Settings;

            DelayMinTextBox.Text = settings.RecognitionDelayMin.ToString();
            DelayMaxTextBox.Text = settings.RecognitionDelayMax.ToString();
            VisualizerBarsTextBox.Text = settings.VisualizerBars.ToString();
            VisualizerFpsTextBox.Text = settings.VisualizerFps.ToString();
            SaveHistoryCheckBox.IsChecked = settings.SaveHistory;
            MaxHistoryTextBox.Text = settings.MaxHistoryItems.ToString();
            DiscordRpcCheckBox.IsChecked = settings.EnableDiscordRichPresence;
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

                settings.EnableDiscordRichPresence = DiscordRpcCheckBox.IsChecked ?? true;

                DataManager.Instance.SaveSettings(settings);

                MessageBox.Show("Configurações salvas com sucesso!\n\nAlgumas alterações podem exigir reinicialização do aplicativo.", 
                    "Configurações", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar configurações: {ex.Message}", 
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
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
                "Tem certeza que deseja limpar todo o histórico?\nEsta ação não pode ser desfeita.",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DataManager.Instance.ClearHistory();
                MessageBox.Show("Histórico limpo com sucesso!", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}



