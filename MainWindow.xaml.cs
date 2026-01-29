using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Music
{
    public enum ViewMode
    {
        TaskBar,
        Window,
        Wallpaper
    }

    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_SHOWWINDOW = 0x0040;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);        
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        public ViewMode CurrentViewMode { get; set; } = ViewMode.TaskBar;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            KeyDown += MainWindow_KeyDown;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += (s, _) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            };            
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.L)
            {
                switch (CurrentViewMode)
                {
                    case ViewMode.TaskBar:
                        PositionateWindow();                        
                        break;

                    case ViewMode.Window:
                        PositionateTaskBar();                        
                        break;
                }
            }
            else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+H - Abrir histórico
                var historyWindow = new HistoryWindow();
                historyWindow.Show();
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+S - Abrir configurações
                var settingsWindow = new SettingsWindow();
                settingsWindow.ShowDialog();
            }
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ContextMenu.IsOpen = true;
        }

        private void HistoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var historyWindow = new HistoryWindow();
            historyWindow.Show();
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionateTaskBar();
        }

        private void PositionateWindow()
        {
            _timer.Stop();
            Height = 82;
            Left = 3;
            try
            {
                Top = SystemParameters.WorkArea.Bottom - Height - 3;
            }
            catch { }
            CurrentViewMode = ViewMode.Window;
        }

        private void PositionateTaskBar()
        {
            _timer.Start();
            Height = 42;
            Left = 3;
            try
            {
                Top = SystemParameters.WorkArea.Bottom - Height + 44.5;
            }
            catch { }
            CurrentViewMode = ViewMode.TaskBar;
        }
    }
}