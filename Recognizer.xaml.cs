using Force.Crc32;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.IO;
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

namespace Music
{
    public static class Logger
    {
        public static void Log(string text, ConsoleColor color)
        {
            var previousc = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = previousc;
        }
    }

    public partial class Recognizer : UserControl
    {
        private DateTime lastrecognized;
        private byte[] lastsigsent = [];
        private ShazamResult? lastresult;
        private int streak = 1;
        private int siglenght = 0;

        public Recognizer()
        {
            Start();
        }

        public async void Start()
        {
            InitializeComponent();

            stackpa.Margin = new Thickness(10, 0, 10, 0);
            Visualizer.Margin = new Thickness(10, 0, 10, 0);

            while (true)
            {
                await Task.Delay(100);

                try
                {
                    await Shazam(new CancellationToken());
                    var delay = Clamp(streak * 2200, 2200, 5500);
                    Logger.Log($"Shazam Request Delay: {delay}", ConsoleColor.DarkCyan);
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
                RadiusX = 5,
                RadiusY = 5
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

            int retryMs = Clamp(streak * 2000, 3000, 7000);
            Logger.Log($"Listening to {retryMs}ms of desktop audio...", ConsoleColor.Magenta);

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
                            Logger.Log("nothing playing", ConsoleColor.DarkRed);
                            streak = 1;
                            App.DiscordPresence?.UpdateMusic(null);
                            await Dispatcher.InvokeAsync(() =>
                            {
                                MusicImgBackground.Visibility = Visibility.Collapsed;
                                InfoBar.Visibility = Visibility.Collapsed;
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

                    Logger.Log($"Signature (Base64): {Convert.ToBase64String(signature)}", ConsoleColor.DarkGray);
                    Logger.Log($"Processed Audio Ms: {analysis.ProcessedMs} Sig Lenght: {signature.Length}", ConsoleColor.Yellow);
                    var (result, rawResponse) = await ShazamApi.SendRequest(analysis.ProcessedMs, signature);

                    if (!result.Success)
                    {
                        retryMs = result.RetryMs;
                        if (retryMs == 0)
                        {
                            if (DateTime.Now - lastrecognized > TimeSpan.FromSeconds(10))
                            {
                                streak = 11;
                                App.DiscordPresence?.UpdateMusic(null);
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    MusicImgBackground.Visibility = Visibility.Collapsed;
                                    InfoBar.Visibility = Visibility.Collapsed;
                                });
                            }
                            break;
                        }
                        continue;
                    }

                    if (lastresult?.Title == result.Title)
                        streak++;
                    else
                        streak = 1;

                    lastrecognized = DateTime.Now;
                    lastresult = result;
                    Logger.Log($"Recognized: {result.Artist} {result.Title}", ConsoleColor.Green);

                    // Adicionar ao histórico
                    DataManager.Instance.AddToHistory(result);

                    // Buscar recomendações se reconhecido 8 vezes
                    var recognitionCount = DataManager.Instance.GetRecognitionCount(result.Id);
                    if (recognitionCount == 8)
                    {
                        Logger.Log($"Track recognized 8 times, fetching recommendations...", ConsoleColor.Magenta);
                        _ = Task.Run(async () =>
                        {
                            var recommendations = await RecommendationService.GetSimilarTracksAsync(result.Id);
                            if (recommendations.Any())
                            {
                                DataManager.Instance.SaveSimilarTracks(result.Id, recommendations);
                                Logger.Log($"Saved {recommendations.Count} recommendations", ConsoleColor.Green);
                            }
                        });
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        MusicImgBackground.Visibility = Visibility.Visible;
                        InfoBar.Visibility = Visibility.Visible;
                        UpdateUIWithSong(result);
                    });

                    result.DurationSeconds = await ShazamApi.GetTrackDurationAsync(result.Isrc, result.Title, result.Artist);
                    App.DiscordPresence?.UpdateMusic(result);
                    break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error Recognizing Song: {ex.Message}", ConsoleColor.Red);
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

            blackborderformusictitle.Visibility = Visibility.Collapsed;
            blackborderformusictitle2.Visibility = Visibility.Collapsed;

            if (result.JoeColor != null)
            {
                Name.Foreground = result.JoeColor.Primary;
                Artist.Foreground = result.JoeColor.Secondary;
                Genre.Foreground = result.JoeColor.Tertiary;

                Visualizer.brush = result.JoeColor.Tertiary;
                Visualizer.InitializeBars();
                Background.Background = result.JoeColor.Background;

                blackborderformusictitle.Visibility = IsBrushLight(result.JoeColor.Tertiary) && IsBrushLight(result.JoeColor.Tertiary) ? Visibility.Visible : Visibility.Collapsed;
                blackborderformusictitle2.Visibility = !IsBrushLight(result.JoeColor.Tertiary) && !IsBrushLight(result.JoeColor.Tertiary) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (result.CoverArtUrl == null)
            {
                MusicImg.Source = null;
                MusicImgBackground.Source = null;
                MusicImageGlow.Background = null;

                stackpa.Margin = new Thickness(10, 0, 10, 0);
                return;
            }

            var bitmap = new BitmapImage(new Uri(result.CoverArtUrl, UriKind.Absolute));
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);

            MusicImg.Source = bitmap;
            MusicImgBackground.Source = bitmap;
            MusicImageGlow.Background = brush;

            stackpa.Margin = new Thickness(50, 0, 10, 0);
        }

        public static bool IsColorLight(Color color)
        {
            double luminance = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
            return luminance > 128;
        }

        public static bool IsBrushLight(Brush brush)
        {
            if (brush is SolidColorBrush solid)
                return IsColorLight(solid.Color);
            return false;
        }
    }

    public class API
    {
        public static class ShazamApi
        {
            private static readonly HttpClient Http;
            static ShazamApi()
            {
                Http = new HttpClient(new HttpClientHandler
                {
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
                            Id = data.Track.Key,
                            Title = data.Track.Title,
                            Artist = data.Track.Subtitle,
                            Genre = data.Track.Genres?.Primary,
                            ReleaseYear = releaseYear,
                            Isrc = data.Track.Isrc,
                            CoverArtUrl = data.Track.Images?.CoverArt,
                            JoeColor = ParseJoeColor(data.Track.Images?.JoeColor),
                            Offset = offset + 6,
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
                    Logger.Log($"Request error: {ex.Message}", ConsoleColor.Red);
                    return (new ShazamResult
                    {
                        IsError = true,
                        ErrorMessage = ex.Message
                    }, "error");
                }
            }

            public static async Task<int?> GetTrackDurationAsync(string isrc, string title, string artist)
            {
                if (string.IsNullOrWhiteSpace(isrc) && (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)))
                    return null;

                try
                {
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
                        return await GetDurationFromLastFM(title, artist)
                            ?? await GetDurationFromMusicBrainzByTitle(title, artist);

                    if (!string.IsNullOrWhiteSpace(isrc))
                        return await GetDurationFromMusicBrainzByIsrc(isrc);
                }
                catch
                {
                }

                return null;
            }

            private static async Task<int?> GetDurationFromMusicBrainzByIsrc(string isrc)
            {
                try
                {
                    var url = $"https://musicbrainz.org/ws/2/recording/?query=isrc:{Uri.EscapeDataString(isrc)}&fmt=json&limit=1";
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(6);
                    client.DefaultRequestHeaders.Add("User-Agent", "MusicRecognizer/1.0 (https://github.com/GatoEVX4/MusicRecognizer-Visualizer)");
                    var response = await client.GetStringAsync(url);
                    var json = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    if (json.TryGetProperty("recordings", out var recordings) && recordings.GetArrayLength() > 0)
                    {
                        var recording = recordings[0];
                        if (recording.TryGetProperty("length", out var lengthElement))
                        {
                            var lengthMs = lengthElement.GetInt32();
                            if (lengthMs > 0)
                                return lengthMs / 1000;
                        }
                    }
                }
                catch
                {
                    
                }
                return null;
            }

            private static async Task<int?> GetDurationFromMusicBrainzByTitle(string title, string artist)
            {
                try
                {
                    var query = $"recording:\"{Uri.EscapeDataString(title)}\" AND artist:\"{Uri.EscapeDataString(artist)}\"";
                    var url = $"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=1";
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(6);
                    client.DefaultRequestHeaders.Add("User-Agent", "MusicRecognizer/1.0 (https://github.com/GatoEVX4/MusicRecognizer-Visualizer)");
                    
                    var response = await client.GetStringAsync(url);
                    var json = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    if (json.TryGetProperty("recordings", out var recordings) && recordings.GetArrayLength() > 0)
                    {
                        var recording = recordings[0];
                        if (recording.TryGetProperty("length", out var lengthElement))
                        {
                            var lengthMs = lengthElement.GetInt32();
                            if (lengthMs > 0)
                                return lengthMs / 1000;
                        }
                    }
                }
                catch
                {
                    
                }
                return null;
            }

            private static async Task<int?> GetDurationFromLastFM(string title, string artist)
            {
                try
                {
                    var url = $"https://ws.audioscrobbler.com/2.0/?method=track.getInfo&api_key=b25b959554ed76058ac220b7b2e0a026&artist={Uri.EscapeDataString(artist)}&track={Uri.EscapeDataString(title)}&format=json";
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(6);
                    var response = await client.GetStringAsync(url);
                    var json = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    if (json.TryGetProperty("track", out var track))
                    {
                        if (track.TryGetProperty("duration", out var durationElement))
                        {
                            var durationStr = durationElement.GetString();
                            if (int.TryParse(durationStr, out var durationMs) && durationMs > 0)
                                return durationMs / 1000;
                        }
                    }
                }
                catch
                {
                    
                }
                return null;
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
            public string Key { get; set; }
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
            public string Id { get; set; }
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
            public int? DurationSeconds { get; set; }
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
                    return "000000";
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
    }
}