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
            _timer.Start();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.L)
            {
                switch (CurrentViewMode)
                {
                    case ViewMode.TaskBar:
                        PositionateWindow();
                        _timer.Stop();
                        CurrentViewMode = ViewMode.Window;
                        break;

                    case ViewMode.Window:
                        PositionateTaskBar();
                        _timer.Start();
                        CurrentViewMode = ViewMode.TaskBar;
                        break;
                }
            }
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionateTaskBar();
        }

        private void PositionateWindow()
        {
            Height = 82;
            Left = 3;
            Top = SystemParameters.WorkArea.Bottom - Height - 3;
        }

        private void PositionateTaskBar()
        {
            Height = 42;
            Left = 3;
            Top = SystemParameters.WorkArea.Bottom - Height + 44.5;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            
        }
    }
}