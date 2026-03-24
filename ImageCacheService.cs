using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Music
{
    // ── Cache service ──────────────────────────────────────────────────────────

    public sealed class ImageCacheService
    {
        // CacheDir must be declared before Instance so it is initialized first.
        private static readonly string CacheDir = Path.Combine(
            Path.GetTempPath(), "MusicRecognizer", "covers");

        public static readonly ImageCacheService Instance = new();

        private static readonly HttpClient Http = new();

        // Tracks URLs currently being downloaded so we don't double-fetch.
        private readonly HashSet<string> _inFlight = new();
        private readonly object _lock = new();

        private ImageCacheService()
        {
            Directory.CreateDirectory(CacheDir);
        }

        /// Returns the local cached path if the file already exists, otherwise null.
        public string? GetCachedPath(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var path = UrlToPath(url);
            return File.Exists(path) ? path : null;
        }

        /// Starts a background download if the file isn't cached yet.
        public void EnsureCached(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            var path = UrlToPath(url);
            if (File.Exists(path)) return;

            lock (_lock)
            {
                if (_inFlight.Contains(url)) return;
                _inFlight.Add(url);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                    // Write to a temp file first, then rename — avoids partial reads.
                    var tmp = path + ".tmp";
                    await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                }
                catch { /* network error or disk full — silently skip */ }
                finally
                {
                    lock (_lock) _inFlight.Remove(url);
                }
            });
        }

        private static string UrlToPath(string url)
        {
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url)));

            // Preserve the original extension when possible (jpg/png/webp).
            string ext;
            try
            {
                var seg = new Uri(url).AbsolutePath;
                ext = Path.GetExtension(seg);
                if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
            }
            catch { ext = ".jpg"; }

            return Path.Combine(CacheDir, hash + ext);
        }
    }

    // ── WPF value converter ────────────────────────────────────────────────────

    /// Binding converter: returns a BitmapImage from the local disk cache when
    /// available, falls back to the remote URL (and kicks off a background
    /// download so the next load is instant).
    public sealed class CoverArtConverter : IValueConverter
    {
        public static readonly CoverArtConverter Instance = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var url = value as string;
            if (string.IsNullOrEmpty(url)) return null;

            var svc       = ImageCacheService.Instance;
            var localPath = svc.GetCachedPath(url);

            if (localPath != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource   = new Uri(localPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;  // release file lock immediately
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch
                {
                    // Cached file corrupt — delete and re-download next time.
                    try { File.Delete(localPath); } catch { }
                }
            }

            // Not cached yet — trigger a background download for next time.
            svc.EnsureCached(url);

            // IMPORTANT: do NOT use BitmapCacheOption.OnLoad or Freeze() here.
            // OnLoad tries to download synchronously and Freeze() would lock the
            // BitmapImage in its "not yet loaded" state, preventing it from ever
            // rendering. Let WPF handle the async HTTP download naturally.
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
