using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Music
{
    public partial class Visualizer : UserControl
    {
        private WasapiLoopbackCapture capture = new WasapiLoopbackCapture();
        private readonly Queue<Complex[]> smooth;
        private DateTime _lastDraw = DateTime.MinValue;
        private readonly List<Rectangle> _bars = new List<Rectangle>();
        private Complex[] fftBuffer = new Complex[4096];
        private const double FrequencyCutoff = 0.20;

        private TimeSpan _frameInterval;
        private int VerticalSmoothness;
        private int HorizontalSmoothness;
        private int BarCount;
        public Brush brush = Brushes.White;

        public Visualizer(int fps, int bars, int vsmooth, int hsmooth)
        {
            _frameInterval = TimeSpan.FromMilliseconds(1000.0 / fps);
            BarCount = bars;
            VerticalSmoothness = vsmooth;
            HorizontalSmoothness = hsmooth;
            smooth = new Queue<Complex[]>(VerticalSmoothness + 1);
            Start();
        }

        public Visualizer()
        {
            _frameInterval = TimeSpan.FromMilliseconds(1000.0 / 60);

            BarCount = 32;
            //BarCount = 64;
            VerticalSmoothness = 2;
            HorizontalSmoothness = 1;
            smooth = new Queue<Complex[]>(VerticalSmoothness + 1);
            Start();
        }

        public async void Start()
        {
            InitializeComponent();
            InitializeBars();
            capture.DataAvailable += OnDataAvailable;
            capture.StartRecording();
        }

        public void InitializeBars()
        {
            SpectrumStack.Children.Clear();
            _bars.Clear();
            for (int i = 0; i < BarCount; i++)
            {
                var rect = new Rectangle
                {
                    Width = ActualWidth / BarCount,
                    RadiusX = 2,
                    RadiusY = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = brush
                };
                SpectrumStack.Children.Add(rect);
                _bars.Add(rect);
            }
        }
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (DateTime.Now - _lastDraw < _frameInterval)
                return;

            var buffer = new WaveBuffer(e.Buffer);
            int len = Math.Min(buffer.FloatBuffer.Length / 8, fftBuffer.Length);

            for (int i = 0; i < len; i++)
                fftBuffer[i] = new Complex(buffer.FloatBuffer[i], 0);

            Fourier.Forward(fftBuffer, FourierOptions.Default);

            int maxFreqIndex = (int)(len * FrequencyCutoff);
            double[] barMagnitudes = new double[BarCount];
            int step = maxFreqIndex / BarCount;

            for (int i = 0; i < BarCount; i++)
            {
                double sum = 0;
                for (int j = i * step; j < (i + 1) * step; j++)
                    sum += fftBuffer[j].Magnitude;

                barMagnitudes[i] = sum / step;
            }

            _lastDraw = DateTime.Now;

            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                smooth.Enqueue(fftBuffer.ToArray());
                if (smooth.Count > VerticalSmoothness)
                    smooth.Dequeue();

                DrawVisualizer();
            }));
        }
        private double VSmooth(int i, Complex[][] s)
        {
            double value = 0;
            for (int v = 0; v < s.Length; v++)
                value += Math.Abs(s[v]?[i].Magnitude ?? 0.0);
            return value / s.Length;
        }
        private double BothSmooth(int i)
        {
            var s = smooth.ToArray();
            double value = 0;
            for (int h = Math.Max(i - HorizontalSmoothness, 0); h < Math.Min(i + HorizontalSmoothness, BarCount); h++)
                value += VSmooth(h, s);
            return value / ((HorizontalSmoothness + 1) * 2);
        }
        private void DrawVisualizer()
        {
            double canvasHeight = SpectrumStack.ActualHeight;
            double barWidth = ActualWidth / BarCount;

            for (int i = 0; i < BarCount; i++)
            {
                var rect = _bars[i];
                rect.Height = canvasHeight * (0.2 + BothSmooth(i)) * 0.3;
                rect.Width = Math.Max(barWidth - 5, 0);
            }
        }
    }
}