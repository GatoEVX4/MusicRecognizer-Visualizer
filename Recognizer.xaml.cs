using Force.Crc32;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using static Music.API;
using static System.Net.WebRequestMethods;

namespace Music
{
    public partial class Recognizer : UserControl
    {
        private DateTime lastrecognized;
        private byte[] lastsigsent = [];
        private List<LandmarkInfo> referenceLandmarks = [];
        private ShazamResult? lastresult;
        private int streak = 2;
        private int siglenght = 0;

        public Recognizer()
        {
            Start();
        }

        public async void Start()
        {
            InitializeComponent();

            while (true)
            {
                await Task.Delay(100);

                try
                {
                    await Shazam(new CancellationToken());
                    var delay = Clamp(streak * 2000, 4000, 8000);
                    await Task.Delay(delay);
                }
                catch { }
            }
        }

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var canvas = (Border)sender;
            canvas.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight),
                RadiusX = 10,
                RadiusY = 10
            };

            Dispatcher.InvokeAsync(() =>
            {
                StartTextScroll(Name, NameTransform, true);
                StartTextScroll(Artist, ArtistTransform, true);
                StartTextScroll(Genre, GenreTransform, true);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void StartTextScroll(TextBlock textBlock, TranslateTransform transform, bool force = false)
        {
            if (!force && textBlock.Tag?.ToString() == textBlock.Text)
                return;

            textBlock.Tag = textBlock.Text;

            transform.BeginAnimation(TranslateTransform.XProperty, null);
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double textWidth = textBlock.DesiredSize.Width;
            double containerWidth = ((FrameworkElement)textBlock.Parent).ActualWidth;

            if (textWidth <= containerWidth || containerWidth == 0)
                return;

            Canvas.SetLeft(textBlock, 0);
            textBlock.Width = double.NaN;

            double offset = textWidth + 20 - containerWidth;

            DoubleAnimation scrollAnim = new DoubleAnimation
            {
                From = 0,
                To = -offset,
                Duration = TimeSpan.FromSeconds(8),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            transform.BeginAnimation(TranslateTransform.XProperty, scrollAnim);
        }


        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }


        private async Task Shazam(CancellationToken cancellationToken)
        {
            var analysis = new Analysis();
            var finder = new LandmarkFinder(analysis);

            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var capture = new WasapiLoopbackCapture(device);
            var captureBuffer = new BufferedWaveProvider(capture.WaveFormat) { ReadFully = false };

            capture.DataAvailable += (s, e) => captureBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            capture.StartRecording();

            using var resampler = new MediaFoundationResampler(captureBuffer, new WaveFormat(16000, 1))
            {
                ResamplerQuality = 40
            };
            var sampleProvider = resampler.ToSampleProvider();

            int retryMs = Clamp(streak * 2000, 3000, 8000);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    while (captureBuffer.BufferedDuration.TotalSeconds < 1.0)
                        await Task.Delay(100, cancellationToken);

                    analysis.ReadChunk(sampleProvider);

                    if (analysis.StripeCount > 94)
                        finder.Find(analysis.StripeCount - 48);

                    if (analysis.ProcessedMs < retryMs)
                        continue;

                    byte[] signature = Sig.Write(16000, analysis.ProcessedSamples, finder);

                    if (signature.Length < 200)
                    {
                        if (siglenght == signature.Length)
                        {
                            //Console.WriteLine("nothing playing");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                Visibility = Visibility.Collapsed;
                            });
                            break;
                        }
                        siglenght = signature.Length;
                        continue;
                    }

                    if (signature == lastsigsent)
                    {
                        lastrecognized = DateTime.Now;
                        continue;
                    }

                    lastsigsent = signature;

                    //Console.WriteLine($"ProcessedMs: {analysis.ProcessedMs} Sig Lenght: {signature.Length}");
                    var (result, rawResponse) = await ShazamApi.SendRequest(analysis.ProcessedMs, signature);

                    if (!result.Success)
                    {
                        retryMs = result.RetryMs;
                        if (retryMs == 0)
                        {
                            if (DateTime.Now - lastrecognized > TimeSpan.FromSeconds(10))
                            {
                                streak = 2;
                                await Dispatcher.InvokeAsync(() => Visibility = Visibility.Collapsed);
                            }
                            break;
                        }
                        continue;
                    }

                    if (lastresult?.Title == result.Title)
                        streak++;
                    else
                        streak = 2;

                    lastrecognized = DateTime.Now;
                    lastresult = result;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        Visibility = Visibility.Visible;
                        UpdateUIWithSong(result);
                    });

                    //Console.WriteLine($"{result.Artist} {result.Title}");
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Recognizing Song: {ex.Message}");
            }
            finally
            {
                //capture.StopRecording();
            }
        }

        private void UpdateUIWithSong(ShazamResult result)
        {
            Name.Text = result.Title;
            Artist.Text = result.Artist;
            Genre.Text = result.Genre;

            Dispatcher.InvokeAsync(() =>
            {
                StartTextScroll(Name, NameTransform);
                StartTextScroll(Artist, ArtistTransform);
                StartTextScroll(Genre, GenreTransform);
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            Background.Background = null;
            Name.Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229));
            Visualizer.brush = new SolidColorBrush(Color.FromRgb(229, 229, 229));
            Visualizer.InitializeBars();

            Artist.Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229));
            Genre.Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229));

            if (result.JoeColor != null)
            {
                Name.Foreground = result.JoeColor.Primary;
                Artist.Foreground = result.JoeColor.Secondary;
                Genre.Foreground = result.JoeColor.Tertiary;

                Visualizer.brush = result.JoeColor.Tertiary;
                Visualizer.InitializeBars();
                Background.Background = result.JoeColor.Background;
            }

            if (result.CoverArtUrl == null)
            {
                MusicImg.Source = null;
                MusicImgBackground.Source = null;
                MusicImageGlow.Background = null;
                return;
            }

            var bitmap = new BitmapImage(new Uri(result.CoverArtUrl, UriKind.Absolute));
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);

            MusicImg.Source = bitmap;
            MusicImgBackground.Source = bitmap;
            MusicImageGlow.Background = brush;
        }
    }

    internal class API
    {
        public static class ShazamApi
        {
            private static readonly HttpClient Http;
            static ShazamApi()
            {
                //var proxy = new WebProxy("http://104.143.226.122:5725")
                //{
                //    BypassProxyOnLocal = false
                //};

                Http = new HttpClient(new HttpClientHandler
                {
                    //Proxy = proxy,
                    UseProxy = false,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                    SslProtocols = SslProtocols.None
                });
            }

            public static async Task<(ShazamResult, string)> SendRequest(int sampleMs, byte[] sig)
            {
                var requestBody = new
                {
                    signature = new
                    {
                        uri = "data:audio/vnd.shazam.sig;base64," + Convert.ToBase64String(sig),
                        samplems = sampleMs
                    }
                };

                string requestUri = $"https://amp.shazam.com/discovery/v5/en/US/android/-/tag/{Guid.NewGuid().ToString()}/{Guid.NewGuid().ToString()}";
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                try
                {
                    var response = await Http.PostAsync(requestUri, content);
                    response.EnsureSuccessStatusCode();

                    var jsonString = await response.Content.ReadAsStringAsync();

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var data = JsonSerializer.Deserialize<ShazamResponse>(jsonString, options);

                    if (data?.Track != null)
                    {
                        var releaseYear = data.Track.Sections?.FirstOrDefault()?.Metadata?
                            .FirstOrDefault(m => m.Title == "Released")?.Text;

                        var offset = data.Matches?.FirstOrDefault()?.Offset;

                        var result = new ShazamResult
                        {
                            Success = true,
                            Title = data.Track.Title,
                            Artist = data.Track.Subtitle,
                            Genre = data.Track.Genres?.Primary,
                            ReleaseYear = releaseYear,
                            Isrc = data.Track.Isrc,
                            CoverArtUrl = data.Track.Images?.CoverArt,
                            JoeColor = ParseJoeColor(data.Track.Images?.JoeColor),
                            Offset = offset,
                            RetryMs = data.RetryMs
                        };

                        return (result, jsonString);
                    }

                    return (new ShazamResult
                    {
                        Success = false,
                        RetryMs = data?.RetryMs ?? 0
                    }, jsonString);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Request error: {ex.Message}");
                    return (new ShazamResult
                    {
                        IsError = true,
                        ErrorMessage = ex.Message
                    }, "error");
                }
            }

            private static JoeColorRgb ParseJoeColor(string joecolor)
            {
                if (string.IsNullOrEmpty(joecolor)) return null;

                var parts = joecolor.Split(new string[] { "b:", "p:", "s:", "t:", "q:" }, StringSplitOptions.RemoveEmptyEntries);

                System.Windows.Media.Brush HexToBrush(string hex)
                {
                    var cleanHex = hex.Length >= 6 ? hex.Substring(0, 6) : "000000";
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + cleanHex);
                    return new SolidColorBrush(color);
                }

                return new JoeColorRgb
                {
                    Background = HexToBrush(parts[0]),
                    Primary = HexToBrush(parts[1]),
                    Secondary = HexToBrush(parts[2]),
                    Tertiary = HexToBrush(parts[3]),
                    Quaternary = HexToBrush(parts[4])
                };
            }
        }

        public class ShazamResponse
        {
            public Track Track { get; set; }
            public Match[] Matches { get; set; }
            public int RetryMs { get; set; }
        }

        public class Track
        {
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public string Isrc { get; set; }
            public Genres Genres { get; set; }
            public Section[] Sections { get; set; }
            public Images Images { get; set; }
        }

        public class Genres
        {
            public string Primary { get; set; }
        }

        public class Section
        {
            public Metadata[] Metadata { get; set; }
        }

        public class Metadata
        {
            public string Title { get; set; }
            public string Text { get; set; }
        }

        public class Images
        {
            public string CoverArt { get; set; }
            public string JoeColor { get; set; }
        }

        public class Match
        {
            public double Offset { get; set; }
        }



        public class ShazamResult : Music
        {
            public bool Success { get; set; }
            public double? Offset { get; set; }
            public double? TotalS { get; set; }
            public int RetryMs { get; set; }
            public string ErrorMessage { get; set; }
            public bool IsError { get; set; }
            public string Genre { get; set; }
            public string Isrc { get; set; }
            public string CoverArtUrl { get; set; }
            public JoeColorRgb JoeColor { get; set; }
        }

        public class JoeColorRgb
        {
            public Brush Background { get; set; }
            public Brush Primary { get; set; }
            public Brush Secondary { get; set; }
            public Brush Tertiary { get; set; }
            public Brush Quaternary { get; set; }

            public override string ToString()
            {
                string BrushToHex(System.Windows.Media.Brush brush)
                {
                    if (brush is SolidColorBrush solidColorBrush)
                    {
                        var color = solidColorBrush.Color;
                        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
                    }
                    return "000000"; // Valor padrão caso não seja SolidColorBrush
                }

                return $"b:{BrushToHex(Background)}p:{BrushToHex(Primary)}s:{BrushToHex(Secondary)}t:{BrushToHex(Tertiary)}q:{BrushToHex(Quaternary)}";
            }
        }


        public class Music
        {
            public string Title { get; set; }
            public string Artist { get; set; }
            public string ReleaseYear { get; set; }
        }



        public static class Sig
        {
            public static byte[] Write(int sampleRate, int sampleCount, LandmarkFinder finder)
            {
                using MemoryStream memoryStream = new MemoryStream();
                using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
                binaryWriter.Write(3405653376u);
                binaryWriter.Write(-1);
                binaryWriter.Write(-1);
                binaryWriter.Write(2484182016u);
                binaryWriter.Write(0);
                binaryWriter.Write(0);
                binaryWriter.Write(0);
                binaryWriter.Write(GetSampleRateCode(sampleRate) << 27);
                binaryWriter.Write(0);
                binaryWriter.Write(0);
                binaryWriter.Write(sampleCount);
                binaryWriter.Write(8126464);
                binaryWriter.Write(1073741824);
                binaryWriter.Write(-1);
                byte[][] bandData = GetBandData(finder);
                for (int i = 0; i < bandData.Length; i++)
                {
                    binaryWriter.Write(1610809408 + i);
                    binaryWriter.Write(bandData[i].Length);
                    binaryWriter.Write(bandData[i]);
                }
                int num = (int)memoryStream.Length;
                int value = num - 48;
                int[] array = new int[2] { 2, 13 };
                foreach (int num2 in array)
                {
                    memoryStream.Position = num2 * 4;
                    binaryWriter.Write(value);
                }
                uint value2 = Crc32Algorithm.Compute(memoryStream.GetBuffer(), 8, num - 8);
                memoryStream.Position = 4L;
                binaryWriter.Write(value2);
                return memoryStream.ToArray();
            }

            private static byte[][] GetBandData(LandmarkFinder finder)
            {
                return finder.EnumerateBandedLandmarks().Select(GetBandData).ToArray();
            }

            private static byte[] GetBandData(IEnumerable<LandmarkInfo> landmarks)
            {
                using MemoryStream memoryStream = new MemoryStream();
                using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
                int num = 0;
                foreach (LandmarkInfo landmark in landmarks)
                {
                    if (landmark.StripeIndex - num >= 100)
                    {
                        num = landmark.StripeIndex;
                        binaryWriter.Write(byte.MaxValue);
                        binaryWriter.Write(num);
                    }
                    if (landmark.StripeIndex < num)
                    {
                        throw new InvalidOperationException();
                    }
                    binaryWriter.Write(Convert.ToByte(landmark.StripeIndex - num));
                    binaryWriter.Write(Convert.ToUInt16(landmark.InterpolatedLogMagnitude));
                    binaryWriter.Write(Convert.ToUInt16(64f * landmark.InterpolatedBin));
                    num = landmark.StripeIndex;
                }
                while (memoryStream.Length % 4 != 0L)
                {
                    binaryWriter.Write((byte)0);
                }
                return memoryStream.ToArray();
            }

            private static int GetSampleRateCode(int sampleRate)
            {
                return sampleRate switch
                {
                    8000 => 1,
                    16000 => 3,
                    32000 => 4,
                    _ => throw new NotSupportedException(),
                };
            }
        }

        public struct LandmarkInfo
        {
            public readonly int StripeIndex;

            public readonly float InterpolatedBin;

            public readonly float InterpolatedLogMagnitude;

            public LandmarkInfo(int stripeIndex, float interpolatedBin, float interpolatedLogMagnitude)
            {
                StripeIndex = stripeIndex;
                InterpolatedBin = interpolatedBin;
                InterpolatedLogMagnitude = interpolatedLogMagnitude;
            }
        }

        public class LandmarkFinder
        {
            public const int RADIUS_TIME = 47;

            public const int RADIUS_FREQ = 9;

            private const int RATE = 12;

            private static readonly IReadOnlyList<int> BAND_FREQS = new int[5] { 250, 520, 1450, 3500, 5500 };

            private static readonly int MIN_BIN = Math.Max(Analysis.FreqToBin(BAND_FREQS.Min()), 9);

            private static readonly int MAX_BIN = Math.Min(Analysis.FreqToBin(BAND_FREQS.Max()), 1016);

            private static readonly float MIN_MAGN_SQUARED = 3.8146973E-06f;

            private static readonly float LOG_MIN_MAGN_SQUARED = (float)Math.Log(MIN_MAGN_SQUARED);

            private readonly Analysis Analysis;

            private readonly IReadOnlyList<List<LandmarkInfo>> Bands;

            public LandmarkFinder(Analysis analysis)
            {
                Analysis = analysis;
                Bands = (from _ in Enumerable.Range(0, BAND_FREQS.Count - 1)
                         select new List<LandmarkInfo>()).ToList();
            }

            public void Find(int stripe)
            {
                for (int i = MIN_BIN; i < MAX_BIN; i++)
                {
                    if (!(Analysis.GetMagnitudeSquared(stripe, i) < MIN_MAGN_SQUARED) && IsPeak(stripe, i, 47, 0) && IsPeak(stripe, i, 3, 9))
                    {
                        AddLandmarkAt(stripe, i);
                    }
                }
            }

            public IEnumerable<IEnumerable<LandmarkInfo>> EnumerateBandedLandmarks()
            {
                return Bands;
            }

            public IEnumerable<LandmarkInfo> EnumerateAllLandmarks()
            {
                return Bands.SelectMany((List<LandmarkInfo> i) => i);
            }

            private int GetBandIndex(float bin)
            {
                float num = Analysis.BinToFreq(bin);
                if (num < BAND_FREQS[0])
                {
                    return -1;
                }
                for (int i = 1; i < BAND_FREQS.Count; i++)
                {
                    if (num < BAND_FREQS[i])
                    {
                        return i - 1;
                    }
                }
                return -1;
            }

            private LandmarkInfo CreateLandmarkAt(int stripe, int bin)
            {
                float logMagnitude = GetLogMagnitude(stripe, bin - 1);
                float logMagnitude2 = GetLogMagnitude(stripe, bin);
                float logMagnitude3 = GetLogMagnitude(stripe, bin + 1);
                float num = (logMagnitude - logMagnitude3) / (logMagnitude - 2f * logMagnitude2 + logMagnitude3) / 2f;
                return new LandmarkInfo(stripe, bin + num, logMagnitude2 - (logMagnitude - logMagnitude3) * num / 4f);
            }

            private float GetLogMagnitude(int stripe, int bin)
            {
                return (float)(18432.0 * (1.0 - Math.Log(Analysis.GetMagnitudeSquared(stripe, bin)) / LOG_MIN_MAGN_SQUARED));
            }

            private bool IsPeak(int stripe, int bin, int stripeRadius, int binRadius)
            {
                float magnitudeSquared = Analysis.GetMagnitudeSquared(stripe, bin);
                for (int i = -stripeRadius; i <= stripeRadius; i++)
                {
                    for (int j = -binRadius; j <= binRadius; j++)
                    {
                        if ((i != 0 || j != 0) && Analysis.GetMagnitudeSquared(stripe + i, bin + j) >= magnitudeSquared)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            private void AddLandmarkAt(int stripe, int bin)
            {
                LandmarkInfo newLandmark = CreateLandmarkAt(stripe, bin);
                int bandIndex = GetBandIndex(newLandmark.InterpolatedBin);
                if (bandIndex < 0)
                {
                    return;
                }
                List<LandmarkInfo> list = Bands[bandIndex];
                if (list.Count > 0)
                {
                    double num = 0.008 * (stripe - list[0].StripeIndex);
                    double num2 = 1.0 + num * 12.0;
                    if (list.Count > num2)
                    {
                        int num3 = list.FindLastIndex((LandmarkInfo l) => l.InterpolatedLogMagnitude < newLandmark.InterpolatedLogMagnitude);
                        if (num3 < 0)
                        {
                            return;
                        }
                        list.RemoveAt(num3);
                    }
                }
                list.Add(newLandmark);
            }
        }

        public class Analysis
        {
            public const int SAMPLE_RATE = 16000;
            public const int CHUNKS_PER_SECOND = 125;
            public const int CHUNK_SIZE = 128;
            public const int WINDOW_SIZE = 2048;
            public const int BIN_COUNT = 1025;

            private static readonly float[] HANN = Array.ConvertAll(MathNet.Numerics.Window.Hann(WINDOW_SIZE), Convert.ToSingle);

            private readonly float[] WindowRing = new float[WINDOW_SIZE];
            private readonly List<float[]> Stripes = new List<float[]>(375);
            private readonly List<float> SampleHistory = new List<float>();

            private readonly System.Numerics.Complex[] FFTBuf = new System.Numerics.Complex[WINDOW_SIZE];

            public int ProcessedSamples { get; private set; }

            public int ProcessedMs => ProcessedSamples * 1000 / SAMPLE_RATE;

            public int StripeCount => Stripes.Count;

            private int WindowRingPos => ProcessedSamples % WINDOW_SIZE;

            public void ReadChunk(ISampleProvider sampleProvider)
            {
                float[] chunk = new float[CHUNK_SIZE];
                int read = sampleProvider.Read(chunk, 0, CHUNK_SIZE);
                if (read != CHUNK_SIZE)
                    throw new Exception();

                for (int i = 0; i < CHUNK_SIZE; i++)
                {
                    int pos = (WindowRingPos + i) % WINDOW_SIZE;
                    WindowRing[pos] = chunk[i];
                    SampleHistory.Add(chunk[i]);
                }

                ProcessedSamples += CHUNK_SIZE;

                if (ProcessedSamples >= WINDOW_SIZE)
                {
                    AddStripe();
                }
            }

            private void AddStripe()
            {
                for (int i = 0; i < WINDOW_SIZE; i++)
                {
                    int num = (WindowRingPos + i) % WINDOW_SIZE;
                    FFTBuf[i] = new Complex(WindowRing[num] * HANN[i], 0);
                }

                Fourier.Forward(FFTBuf, FourierOptions.NoScaling);
                float[] array = new float[BIN_COUNT];
                for (int j = 0; j < BIN_COUNT; j++)
                {
                    array[j] = (float)(2.0 * FFTBuf[j].MagnitudeSquared());
                }

                Stripes.Add(array);
            }

            public float GetMagnitudeSquared(int stripe, int bin)
            {
                return Stripes[stripe][bin];
            }

            public float FindMaxMagnitudeSquared()
            {
                return Stripes.Max(s => s.Max());
            }

            public static int FreqToBin(float freq) => Convert.ToInt32(freq * WINDOW_SIZE / SAMPLE_RATE);

            public static float BinToFreq(float bin) => bin * SAMPLE_RATE / WINDOW_SIZE;

            public float[] GetLastSamples(int ms)
            {
                int sampleCount = ms * SAMPLE_RATE / 1000;
                if (SampleHistory.Count < sampleCount)
                    return SampleHistory.ToArray();

                return SampleHistory.Skip(SampleHistory.Count - sampleCount).Take(sampleCount).ToArray();
            }

            public void Advance(int ms)
            {
                int samplesToRemove = ms * SAMPLE_RATE / 1000;

                if (samplesToRemove >= SampleHistory.Count)
                {
                    SampleHistory.Clear();
                    Stripes.Clear();
                    ProcessedSamples = 0;
                    return;
                }

                SampleHistory.RemoveRange(0, samplesToRemove);

                int stripesToRemove = samplesToRemove / CHUNK_SIZE;
                if (stripesToRemove > 0 && stripesToRemove < Stripes.Count)
                    Stripes.RemoveRange(0, stripesToRemove);

                ProcessedSamples -= samplesToRemove;
                if (ProcessedSamples < 0) ProcessedSamples = 0;
            }
        }



        public class SignatureCompareResult
        {
            public bool IsMatch { get; set; }
            public int BestOffset { get; set; }
            public int BestOffsetMatches { get; set; }
            public double ZScore { get; set; }
            public double PValueApprox { get; set; }
            public int TotalMatches { get; set; }
        }

        public static class FingerprintComparer
        {
            private const int MAX_TARGET_STRIPES = 47; // lookahead (como no finder)
            private const int MAX_OFFSET_BUCKETS = 1000; // tamanho do histograma (suficiente)
            private const int MAX_ANCHORS = 5000; // limitar para desempenho se necessário

            // Gera chaves (anchor-target) para um conjunto de landmarks
            private static IEnumerable<(string key, int anchorStripe, int anchorStripeInt)> GeneratePairs(IEnumerable<LandmarkInfo> landmarks)
            {
                var list = landmarks.OrderBy(l => l.StripeIndex).ToList();
                int n = list.Count;
                // limitar big-O se necessário
                for (int i = 0; i < n; i++)
                {
                    var a = list[i];
                    int anchorsStripe = a.StripeIndex;
                    // para cada anchor, emparelhar com próximos dentro de janela
                    for (int j = i + 1; j < n && list[j].StripeIndex - anchorsStripe <= MAX_TARGET_STRIPES; j++)
                    {
                        var b = list[j];
                        int delta = b.StripeIndex - anchorsStripe;
                        // quantizar bins para reduzir sensibilidade
                        int binA = (int)Math.Round(a.InterpolatedBin);
                        int binB = (int)Math.Round(b.InterpolatedBin);

                        // construir chave: (binA, binB, delta)
                        string key = $"{binA}:{binB}:{delta}";
                        yield return (key, anchorsStripe, anchorsStripe);
                    }
                }
            }

            public static SignatureCompareResult Compare(IEnumerable<LandmarkInfo> A, IEnumerable<LandmarkInfo> B)
            {
                var pairsA = GeneratePairs(A).ToList();
                var pairsB = GeneratePairs(B).ToList();

                // Index pairsB por chave -> lista de anchor stripes (onde apareceu)
                var dictB = new Dictionary<string, List<int>>();
                foreach (var p in pairsB)
                {
                    if (!dictB.TryGetValue(p.key, out var lst)) { lst = new List<int>(); dictB[p.key] = lst; }
                    lst.Add(p.anchorStripe);
                }

                // histograma de offsets (offset = stripeA - stripeB)
                var offsetCounts = new Dictionary<int, int>();
                int totalMatches = 0;

                foreach (var pa in pairsA)
                {
                    if (dictB.TryGetValue(pa.key, out var anchorsB))
                    {
                        foreach (var bStripe in anchorsB)
                        {
                            int offset = pa.anchorStripe - bStripe; // se A começou depois, offset>0
                            if (!offsetCounts.TryGetValue(offset, out var c)) c = 0;
                            offsetCounts[offset] = c + 1;
                            totalMatches++;
                        }
                    }
                }

                if (offsetCounts.Count == 0)
                {
                    return new SignatureCompareResult
                    {
                        IsMatch = false,
                        BestOffset = 0,
                        BestOffsetMatches = 0,
                        TotalMatches = 0,
                        ZScore = 0,
                        PValueApprox = 1.0
                    };
                }

                // melhor offset
                var bestPair = offsetCounts.OrderByDescending(kv => kv.Value).First();
                int bestOffset = bestPair.Key;
                int bestCount = bestPair.Value;

                // estimativa do "esperado por acaso" por bucket
                // espaço de chaves possível S ≈ (#bins)^2 * (MAX_TARGET_STRIPES)
                // mas usamos uma estimativa empírica: número de buckets observados
                int bucketsObserved = Math.Max(1, offsetCounts.Count);
                // total de pairs gerados em A e B
                int nPairsA = pairsA.Count;
                int nPairsB = pairsB.Count;

                // estimativa simples: mu = totalMatches / bucketsObserved
                double mu = (double)totalMatches / bucketsObserved;
                double sigma = Math.Sqrt(Math.Max(1.0, mu)); // approx (Poisson-like)

                // z-score
                double z = (bestCount - mu) / (sigma > 0 ? sigma : 1.0);

                // p-value aproximado via cauda normal (one-sided)
                double pValue = NormalTailFromZ(z);

                // heurística de decisão
                bool isMatch = (bestCount >= 8 && z > 6) || (bestCount >= 10 && z > 4);
                // (esses thresholds podem ser ajustados empiricamente)

                return new SignatureCompareResult
                {
                    IsMatch = isMatch,
                    BestOffset = bestOffset,
                    BestOffsetMatches = bestCount,
                    TotalMatches = totalMatches,
                    ZScore = z,
                    PValueApprox = pValue
                };
            }

            // aproximação da cauda superior da Normal(0,1)
            private static double NormalTailFromZ(double z)
            {
                if (z <= 0) return 0.5;
                // usar aprox de erro complementar (erfc), aqui implementação rápida
                // Abramowitz-Stegun approximation (Rational approximation)
                double t = 1.0 / (1.0 + 0.2316419 * z);
                double d = 0.3989423 * Math.Exp(-z * z / 2.0);
                double prob = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
                return prob; // esta é a cauda superior aproximada
            }
        }
    }
}