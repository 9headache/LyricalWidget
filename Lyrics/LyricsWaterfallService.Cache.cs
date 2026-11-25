using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LyricalWidget.Common;
using LyricalWidget.Media;

namespace LyricalWidget.Lyrics;

// Memory and disk cache
public partial class LyricsWaterfallService
{
    private LrcDocument? GetFromL1Cache(string key)
    {
        lock (_l1Lock)
        {
            if (_l1Map.TryGetValue(key, out var node))
            {
                _l1List.Remove(node);
                _l1List.AddFirst(node);
                return node.Value.Doc;
            }
        }
        return null;
    }

    private void AddToL1Cache(string key, LrcDocument doc)
    {
        if (!doc.HasSyncedLyrics) return;
        lock (_l1Lock)
        {
            if (_l1Map.TryGetValue(key, out var existing))
            {
                _l1List.Remove(existing);
            }
            else if (_l1List.Count >= L1Capacity)
            {
                var last = _l1List.Last;
                if (last != null)
                {
                    _l1Map.Remove(last.Value.Key);
                    _l1List.RemoveLast();
                }
            }

            var newNode = new LinkedListNode<(string Key, LrcDocument Doc)>((key, doc));
            _l1List.AddFirst(newNode);
            _l1Map[key] = newNode;
        }
    }

    public async Task UpdateCachedUserOffsetAsync(TrackInfo track, int userOffsetMs, CancellationToken ct = default)
    {
        if (!track.IsValid) return;
        string cacheFilePath = GetCacheFilePath(track);

        try
        {
            if (File.Exists(cacheFilePath))
            {
                string json = await File.ReadAllTextAsync(cacheFilePath, ct);
                var doc = JsonSerializer.Deserialize<LrcDocument>(json, _jsonOptions);
                if (doc != null)
                {
                    doc.UserOffsetMs = userOffsetMs;
                    AddToL1Cache(track.CacheKey, doc);
                    await SaveToCacheAsync(cacheFilePath, doc, ct);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update cached song offset: {ex.Message}");
        }
    }

    private static string GetCacheFilePath(TrackInfo track)
    {
        string rawKey = $"{track.Artist}|{track.Title}|{track.Album}|{(int)track.Duration.TotalSeconds}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        string hash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();

        string safeArtist = SanitizeForFileName(track.Artist);
        string safeTitle = SanitizeForFileName(track.Title);

        if (safeArtist.Length > 20) safeArtist = safeArtist[..20];
        if (safeTitle.Length > 20) safeTitle = safeTitle[..20];

        string fileName = $"{safeArtist}_{safeTitle}_{hash}.json";
        return Path.Combine(AppPaths.CacheDirectory, fileName);
    }

    private static async Task SaveToCacheAsync(string path, LrcDocument doc, CancellationToken ct)
    {
        try
        {
            if (IsPoisonedGibberish(doc))
            {
                return;
            }

            string dir = Path.GetDirectoryName(path) ?? AppPaths.CacheDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(doc, _jsonOptions);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to cache lyrics: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for known bot-detection phrases in lyrics.
    /// </summary>
    public static bool IsPoisonedGibberish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("wob gopini den", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("tefe woxica fero", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("gogoh vudob wiya", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPoisonedGibberish(LrcDocument? doc)
    {
        if (doc == null || doc.Lines == null) return false;
        foreach (var line in doc.Lines)
        {
            if (IsPoisonedGibberish(line.Text) || IsPoisonedGibberish(line.SubText))
            {
                return true;
            }
            if (line.Words != null)
            {
                foreach (var word in line.Words)
                {
                    if (IsPoisonedGibberish(word.Text)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Scans cache directory for poisoned files containing bot-detection phrases and deletes them.
    /// </summary>
    public static void PurgePoisonedDiskCache()
    {
        try
        {
            string cacheDir = AppPaths.CacheDirectory;
            if (!Directory.Exists(cacheDir)) return;

            var files = Directory.GetFiles(cacheDir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    if (IsPoisonedGibberish(content))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not purge cache file '{file}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics] Error during cache purge: {ex.Message}");
        }
    }

    /// <summary>
    /// Evicts cached lyric files older than 30 days and enforces a 500-file maximum cap.
    /// </summary>
    public static void CleanDiskCache()
    {
        try
        {
            string cacheDir = AppPaths.CacheDirectory;
            if (!Directory.Exists(cacheDir)) return;

            var dirInfo = new DirectoryInfo(cacheDir);
            var files = dirInfo.GetFiles("*.json");
            if (files.Length == 0) return;

            var cutoff = DateTime.UtcNow.AddDays(-30);
            var remainingFiles = new List<FileInfo>(files.Length);

            foreach (var file in files)
            {
                try
                {
                    if (file.LastWriteTimeUtc < cutoff)
                    {
                        file.Delete();
                    }
                    else
                    {
                        remainingFiles.Add(file);
                    }
                }
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not evict expired file '{file.Name}': {ex.Message}");
                    remainingFiles.Add(file);
                }
            }

            const int maxCapacity = 500;
            if (remainingFiles.Count > maxCapacity)
            {
                remainingFiles.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                int excessCount = remainingFiles.Count - maxCapacity;
                for (int i = 0; i < excessCount; i++)
                {
                    try
                    {
                        remainingFiles[i].Delete();
                    }
                    catch (IOException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not evict excess file '{remainingFiles[i].Name}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics] Error during cache cleanup: {ex.Message}");
        }
    }

    public void ClearL1Cache()
    {
        lock (_l1Lock)
        {
            _l1List.Clear();
            _l1Map.Clear();
        }
    }

    public void ClearCache()
    {
        ClearL1Cache();
        ClearDiskCacheFiles();
    }

    public static void ClearDiskCacheFiles()
    {
        try
        {
            string cacheDir = AppPaths.CacheDirectory;
            if (Directory.Exists(cacheDir))
            {
                var files = Directory.GetFiles(cacheDir, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not delete cache file: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics] Error clearing disk cache: {ex.Message}");
        }
    }

    private static string SanitizeForFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (c == ' ' || c == '_' || c == '-')
            {
                sb.Append('_');
            }
        }
        string result = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "track" : result;
    }
}
