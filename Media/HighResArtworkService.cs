using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace LyricalWidget.Media;

/// <summary>
/// Asynchronously fetches high-resolution (1200x1200 / 1000x1000) album artwork
/// using public keyless endpoints (iTunes Search API with Deezer fallback),
/// caching results in memory for instant reuse.
/// </summary>
public sealed class HighResArtworkService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(7)
    };

    static HighResArtworkService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "LyricalWidget/2.0");
    }

    private readonly ConcurrentDictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BitmapImage? CurrentHdArtwork { get; private set; }
    public event Action<BitmapImage?>? HighResArtworkLoaded;

    public async Task<BitmapImage?> FetchHighResArtworkAsync(string artist, string title, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
        {
            CurrentHdArtwork = null;
            HighResArtworkLoaded?.Invoke(null);
            return null;
        }

        string cacheKey = $"{artist.Trim()}_{title.Trim()}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            CurrentHdArtwork = cached;
            HighResArtworkLoaded?.Invoke(cached);
            return cached;
        }

        string cleanArtist = CleanQuery(artist);
        string cleanTitle = CleanQuery(title);
        string query = string.IsNullOrWhiteSpace(cleanArtist) ? cleanTitle : $"{cleanArtist} {cleanTitle}";

        BitmapImage? result = await TryFetchFromItunesAsync(query, ct).ConfigureAwait(false);
        if (result == null && !ct.IsCancellationRequested)
        {
            result = await TryFetchFromDeezerAsync(query, ct).ConfigureAwait(false);
        }

        if (result != null)
        {
            _cache[cacheKey] = result;
        }

        CurrentHdArtwork = result;
        HighResArtworkLoaded?.Invoke(result);
        return result;
    }

    private static string CleanQuery(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        int paren = raw.IndexOf('(');
        if (paren > 0) raw = raw[..paren];
        int dash = raw.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) raw = raw[..dash];
        return raw.Trim();
    }

    private static async Task<BitmapImage?> TryFetchFromItunesAsync(string query, CancellationToken ct)
    {
        try
        {
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=1";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("resultCount", out var countProp) || countProp.GetInt32() == 0)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (!first.TryGetProperty("artworkUrl100", out var artProp)) return null;

            string? artUrl = artProp.GetString();
            if (string.IsNullOrWhiteSpace(artUrl)) return null;

            // Upgrade 100x100 to 1200x1200bb.jpg
            string hdUrl = artUrl.Replace("100x100bb.jpg", "1200x1200bb.jpg")
                                 .Replace("100x100bb.png", "1200x1200bb.png")
                                 .Replace("100x100", "1200x1200");

            return await DownloadImageAsync(hdUrl, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> TryFetchFromDeezerAsync(string query, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                return null;
            }

            var first = data[0];
            if (!first.TryGetProperty("album", out var album)) return null;

            string? coverUrl = null;
            if (album.TryGetProperty("cover_xl", out var xlProp)) coverUrl = xlProp.GetString();
            if (string.IsNullOrWhiteSpace(coverUrl) && album.TryGetProperty("cover_big", out var bigProp)) coverUrl = bigProp.GetString();

            if (string.IsNullOrWhiteSpace(coverUrl)) return null;

            return await DownloadImageAsync(coverUrl, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> DownloadImageAsync(string imageUrl, CancellationToken ct)
    {
        try
        {
            byte[] bytes = await HttpClient.GetByteArrayAsync(imageUrl, ct).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            var memStream = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = memStream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
