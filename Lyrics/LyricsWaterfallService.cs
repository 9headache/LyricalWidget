using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LyricalWidget.Common;
using LyricalWidget.Media;
using LyricalWidget.Services;

namespace LyricalWidget.Lyrics;

public class LyricsWaterfallService : IDisposable
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    // L1 In-Memory LRU Cache (last 50 tracks -> 0ms instant hit)
    private readonly object _l1Lock = new();
    private readonly LinkedList<(string Key, LrcDocument Doc)> _l1List = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, LrcDocument Doc)>> _l1Map = new();
    private const int L1Capacity = 50;

    // Tracks whose next load must go back to the providers instead of the cache
    private readonly HashSet<string> _refetchKeys = new();

    public LyricsWaterfallService()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(7)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LyricalWidget/2.0 (Windows; native lyrics overlay)");

        // Clean expired or excess cache files asynchronously on startup
        Task.Run(() =>
        {
            PurgePoisonedDiskCache();
            CleanDiskCache();
        });
    }

    /// <summary>
    /// Makes the next load of this track skip the cache. The request survives cancelled loads, and the
    /// cached copy stays on disk as a fallback for when the providers are unreachable.
    /// </summary>
    public void RequestRefetch(TrackInfo track)
    {
        if (!track.IsValid) return;
        lock (_l1Lock)
        {
            _refetchKeys.Add(track.CacheKey);
        }
    }

    private bool IsRefetchPending(string cacheKey)
    {
        lock (_l1Lock)
        {
            return _refetchKeys.Contains(cacheKey);
        }
    }

    private void CompleteRefetch(string cacheKey)
    {
        lock (_l1Lock)
        {
            _refetchKeys.Remove(cacheKey);
        }
    }

    public Task<LrcDocument> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        return GetLyricsAsync(track, onUpgradeAvailable: null, cancellationToken);
    }

    public async Task<LrcDocument> GetLyricsAsync(TrackInfo track, Action<LrcDocument>? onUpgradeAvailable, CancellationToken cancellationToken = default)
    {
        if (!track.IsValid)
        {
            return LrcDocument.Empty();
        }

        string cacheKey = track.CacheKey;
        bool refetch = IsRefetchPending(cacheKey);
        LrcDocument? refetchFallback = null;

        // Check L1 In-Memory LRU Cache
        var l1Doc = GetFromL1Cache(cacheKey);
        if (l1Doc != null && l1Doc.HasSyncedLyrics)
        {
            try
            {
                string path = GetCacheFilePath(track);
                if (File.Exists(path))
                {
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not refresh cache timestamp: {ex.Message}");
            }
            if (!refetch) return l1Doc;
            refetchFallback = l1Doc;
        }

        string cacheFilePath = GetCacheFilePath(track);

        // Check L2 Disk Cache
        if (File.Exists(cacheFilePath))
        {
            try
            {
                string cachedJson = await File.ReadAllTextAsync(cacheFilePath, cancellationToken);
                if (IsPoisonedGibberish(cachedJson))
                {
                    try
                    {
                        File.Delete(cacheFilePath);
                        System.Diagnostics.Debug.WriteLine($"[Lyrics] Deleted poisoned cache file on read: {cacheFilePath}");
                    }
                    catch (IOException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not delete poisoned cache file: {ex.Message}");
                    }
                }
                else
                {
                    var cachedDoc = JsonSerializer.Deserialize<LrcDocument>(cachedJson, _jsonOptions);
                    if (cachedDoc != null && cachedDoc.HasSyncedLyrics && !IsPoisonedGibberish(cachedDoc))
                    {
                        try
                        {
                            File.SetLastWriteTimeUtc(cacheFilePath, DateTime.UtcNow);
                        }
                        catch (IOException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Lyrics] Could not refresh disk cache timestamp: {ex.Message}");
                        }

                        if (!refetch)
                        {
                            AddToL1Cache(cacheKey, cachedDoc);
                            return cachedDoc;
                        }
                        // Disk copy also carries the saved per-song sync nudge
                        refetchFallback = cachedDoc;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read disk cached lyrics: {ex.Message}");
            }
        }

        if (cancellationToken.IsCancellationRequested) return LrcDocument.Empty();

        int durationSec = (int)track.Duration.TotalSeconds;
        string cleanTitle = MetadataCleaner.CleanTitle(track.Title);
        string cleanArtist = MetadataCleaner.CleanArtist(track.Artist);

        // Race LRCLIB and NetEase in parallel
        var providerTasks = new List<Task<LrcDocument?>>
        {
            RunProviderWithTimeout(ct => FetchFromLrcLibAsync(track, cleanTitle, cleanArtist, durationSec, ct), cancellationToken),
            RunProviderWithTimeout(ct => FetchFromNetEaseAsync(cleanTitle, cleanArtist, track.Duration, ct), cancellationToken)
        };

        var remainingTasks = new List<Task<LrcDocument?>>(providerTasks);
        LrcDocument? winner = null;

        while (remainingTasks.Count > 0)
        {
            var finishedTask = await Task.WhenAny(remainingTasks);
            remainingTasks.Remove(finishedTask);

            try
            {
                var res = await finishedTask;
                if (res != null && res.HasSyncedLyrics && !IsPoisonedGibberish(res))
                {
                    winner = res;
                    break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lyrics] Provider racing task failed: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested) break;
        }

        if (winner != null)
        {
            // If winner is line-synced, give NetEase a brief 250ms window to see if word-synced YRC arrives right away
            if (!winner.HasWordSync && remainingTasks.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var quickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    quickCts.CancelAfter(TimeSpan.FromMilliseconds(250));
                    var remainingArray = remainingTasks.Cast<Task>().ToArray();
                    var quickTask = await Task.WhenAny(remainingArray.Append(Task.Delay(250, quickCts.Token)));
                    if (quickTask is Task<LrcDocument?> finishedQuickTask)
                    {
                        remainingTasks.Remove(finishedQuickTask);
                        var candidate = await finishedQuickTask;
                        if (candidate != null && candidate.HasSyncedLyrics && candidate.HasWordSync && !IsPoisonedGibberish(candidate))
                        {
                            winner = candidate;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] Quick-sync check error: {ex.Message}");
                }
            }

            if (refetchFallback != null)
            {
                winner.UserOffsetMs = refetchFallback.UserOffsetMs;
            }
            if (!cancellationToken.IsCancellationRequested) CompleteRefetch(cacheKey);

            AddToL1Cache(cacheKey, winner);
            await SaveToCacheAsync(cacheFilePath, winner, cancellationToken);

            if (winner.HasWordSync || onUpgradeAvailable == null || remainingTasks.Count == 0 || cancellationToken.IsCancellationRequested)
            {
                return winner;
            }

            // Winner is line-synced: listen for up to 3.0s in background for NetEase to return word-synced YRC
            var pendingTasksCopy = new List<Task<LrcDocument?>>(remainingTasks);
            _ = Task.Run(async () =>
            {
                try
                {
                    using var upgradeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    upgradeCts.CancelAfter(TimeSpan.FromMilliseconds(3000));

                    while (pendingTasksCopy.Count > 0 && !upgradeCts.IsCancellationRequested)
                    {
                        var delayTask = Task.Delay(3000, upgradeCts.Token);
                        var anyTask = await Task.WhenAny(pendingTasksCopy.Cast<Task>().Append(delayTask));
                        if (anyTask == delayTask || upgradeCts.IsCancellationRequested)
                        {
                            break;
                        }

                        var finishedUpgradeTask = (Task<LrcDocument?>)anyTask;
                        pendingTasksCopy.Remove(finishedUpgradeTask);

                        try
                        {
                            var candidate = await finishedUpgradeTask;
                            if (candidate != null && candidate.HasSyncedLyrics && candidate.HasWordSync && !IsPoisonedGibberish(candidate))
                            {
                                candidate.UserOffsetMs = winner.UserOffsetMs;
                                AddToL1Cache(cacheKey, candidate);
                                await SaveToCacheAsync(cacheFilePath, candidate, cancellationToken);
                                onUpgradeAvailable(candidate);
                                break;
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Lyrics] Background upgrade candidate error: {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] Background upgrade task error: {ex.Message}");
                }
            }, cancellationToken);

            return winner;
        }

        if (!cancellationToken.IsCancellationRequested) CompleteRefetch(cacheKey);
        if (refetchFallback != null)
        {
            AddToL1Cache(cacheKey, refetchFallback);
            return refetchFallback;
        }

        return LrcDocument.Empty(isInstrumental: false, isNotFound: true, provider: "None");
    }

    private static async Task<LrcDocument?> RunProviderWithTimeout(Func<CancellationToken, Task<LrcDocument?>> providerFunc, CancellationToken parentToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5.5));
        try
        {
            return await providerFunc(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Provider error: {ex.Message}");
            return null;
        }
    }

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

    private async Task<LrcDocument?> FetchFromLrcLibAsync(TrackInfo track, string cleanTitle, string cleanArtist, int durationSec, CancellationToken ct)
    {
        // Direct metadata query
        string getUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
        if (!string.IsNullOrWhiteSpace(track.Album)) getUrl += $"&album_name={Uri.EscapeDataString(track.Album)}";
        if (durationSec > 0) getUrl += $"&duration={durationSec}";

        var directDoc = await QueryLrcLibEndpointAsync(getUrl, ct);
        if (directDoc != null && directDoc.HasSyncedLyrics) return directDoc;

        // If direct get with album and duration failed, try direct get with just track & artist
        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            string simpleUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
            if (durationSec > 0) simpleUrl += $"&duration={durationSec}";
            var simpleDoc = await QueryLrcLibEndpointAsync(simpleUrl, ct);
            if (simpleDoc != null && simpleDoc.HasSyncedLyrics) return simpleDoc;
        }

        // Fuzzy fallback search
        string searchQ = $"{cleanArtist} {cleanTitle}".Trim();
        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(searchQ)}";
        var fuzzyDoc = await QueryLrcLibFuzzySearchEndpointAsync(searchUrl, durationSec, ct);
        if (fuzzyDoc != null && fuzzyDoc.HasSyncedLyrics) return fuzzyDoc;

        return null;
    }

    private async Task<LrcDocument?> QueryLrcLibEndpointAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool instrumental = root.TryGetProperty("instrumental", out var inst) && inst.GetBoolean();
            string? syncedLyrics = root.TryGetProperty("syncedLyrics", out var syn) ? syn.GetString() : null;

            if (instrumental)
            {
                return LrcDocument.Empty(isInstrumental: true, provider: "LRCLIB");
            }

            if (!string.IsNullOrWhiteSpace(syncedLyrics))
            {
                var lines = LrcParser.Parse(syncedLyrics);
                if (lines.Count > 0)
                {
                    return new LrcDocument
                    {
                        Lines = lines,
                        Provider = "LRCLIB",
                        PlainLyrics = root.TryGetProperty("plainLyrics", out var pl) ? pl.GetString() ?? "" : ""
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LRCLIB direct query error: {ex.Message}");
        }

        return null;
    }

    private async Task<LrcDocument?> QueryLrcLibFuzzySearchEndpointAsync(string url, int targetDurationSec, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            JsonElement bestItem = default;
            double bestDiff = double.MaxValue;
            JsonElement firstSynced = default;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? synced = item.TryGetProperty("syncedLyrics", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(synced)) continue;

                if (firstSynced.ValueKind == JsonValueKind.Undefined)
                {
                    firstSynced = item;
                }

                double dur = item.TryGetProperty("duration", out var d) ? d.GetDouble() : 0;
                double diff = targetDurationSec > 0 ? Math.Abs(dur - targetDurationSec) : 0;

                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestItem = item;
                    if (diff <= 1.0) break;
                }
            }

            var chosen = (bestDiff <= 12.0 && bestItem.ValueKind == JsonValueKind.Object)
                ? bestItem
                : (firstSynced.ValueKind == JsonValueKind.Object ? firstSynced : default);

            if (chosen.ValueKind == JsonValueKind.Object)
            {
                string? synced = chosen.GetProperty("syncedLyrics").GetString();
                var lines = LrcParser.Parse(synced);
                if (lines.Count > 0)
                {
                    return new LrcDocument
                    {
                        Lines = lines,
                        Provider = "LRCLIB",
                        PlainLyrics = chosen.TryGetProperty("plainLyrics", out var pl) ? pl.GetString() ?? "" : ""
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LRCLIB search query error: {ex.Message}");
        }

        return null;
    }

    private async Task<LrcDocument?> FetchFromNetEaseAsync(string cleanTitle, string cleanArtist, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            string searchWord = $"{cleanTitle} {cleanArtist}".Trim();
            string searchUrl = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(searchWord)}&type=1&limit=5";

            using var searchReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            using var searchResp = await _httpClient.SendAsync(searchReq, ct);
            if (!searchResp.IsSuccessStatusCode) return null;

            string searchJson = await searchResp.Content.ReadAsStringAsync(ct);
            using var searchDoc = JsonDocument.Parse(searchJson);

            if (!searchDoc.RootElement.TryGetProperty("result", out var resultElem) ||
                !resultElem.TryGetProperty("songs", out var songsElem) ||
                songsElem.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            long targetDurMs = (long)duration.TotalMilliseconds;
            long selectedId = 0;
            double minDiffSec = 9999;

            foreach (var song in songsElem.EnumerateArray())
            {
                long id = song.GetProperty("id").GetInt64();
                long songDurMs = song.TryGetProperty("duration", out var dur) ? dur.GetInt64() : 0;

                double diffSec = targetDurMs > 0 ? Math.Abs((songDurMs - targetDurMs) / 1000.0) : 0;

                // Duration-locked: Reject if off by more than 12 seconds
                if (targetDurMs > 0 && diffSec > 12.0)
                {
                    continue;
                }

                if (diffSec < minDiffSec)
                {
                    minDiffSec = diffSec;
                    selectedId = id;
                    if (diffSec <= 1.5) break;
                }
            }

            if (selectedId > 0)
            {
                string lyricUrl = $"https://music.163.com/api/song/lyric?id={selectedId}&lv=1&kv=1&tv=-1&yv=1&rv=1";
                using var lyricReq = new HttpRequestMessage(HttpMethod.Get, lyricUrl);
                lyricReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                using var lyricResp = await _httpClient.SendAsync(lyricReq, ct);
                if (!lyricResp.IsSuccessStatusCode) return null;

                string lyricJson = await lyricResp.Content.ReadAsStringAsync(ct);
                using var lyricDoc = JsonDocument.Parse(lyricJson);

                string? subLrc = null;
                if (lyricDoc.RootElement.TryGetProperty("romalrc", out var rElem) && rElem.TryGetProperty("lyric", out var rl))
                    subLrc = rl.GetString();
                else if (lyricDoc.RootElement.TryGetProperty("rlyric", out var r2Elem) && r2Elem.TryGetProperty("lyric", out var r2l))
                    subLrc = r2l.GetString();
                else if (lyricDoc.RootElement.TryGetProperty("tlyric", out var tElem) && tElem.TryGetProperty("lyric", out var tl))
                    subLrc = tl.GetString();

                // Try parsing YRC (word-level timestamps) if available
                if (lyricDoc.RootElement.TryGetProperty("yrc", out var yrcElem) &&
                    yrcElem.TryGetProperty("lyric", out var yrcLyricElem))
                {
                    string? rawYrc = yrcLyricElem.GetString();
                    if (!string.IsNullOrWhiteSpace(rawYrc))
                    {
                        var yrcLines = ParseNetEaseYrc(rawYrc);
                        if (yrcLines.Count > 0)
                        {
                            MergeSubLines(yrcLines, subLrc);
                            return new LrcDocument
                            {
                                Lines = yrcLines,
                                Provider = "NetEase YRC"
                            };
                        }
                    }
                }

                // Fall back to standard line-synced LRC
                if (lyricDoc.RootElement.TryGetProperty("lrc", out var lrcElem) &&
                    lrcElem.TryGetProperty("lyric", out var lyricTextElem))
                {
                    string? rawLrc = lyricTextElem.GetString();
                    if (!string.IsNullOrWhiteSpace(rawLrc))
                    {
                        var lines = LrcParser.Parse(rawLrc);
                        if (lines.Count > 0)
                        {
                            MergeSubLines(lines, subLrc);
                            return new LrcDocument
                            {
                                Lines = lines,
                                Provider = "NetEase"
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NetEase lyric query error: {ex.Message}");
        }

        return null;
    }

    private static List<LyricLine> ParseNetEaseYrc(string yrcContent)
    {
        var result = new List<LyricLine>();
        var lineRegex = new Regex(@"^\[(?<start>\d+),(?<dur>\d+)\](?<content>.*)$", RegexOptions.Compiled);
        var wordRegex = new Regex(@"\((?<start>\d+),(?<dur>\d+),\d+\)(?<word>[^\(]+)", RegexOptions.Compiled);

        string[] lines = yrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var raw in lines)
        {
            string trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var match = lineRegex.Match(trimmed);
            if (!match.Success) continue;

            if (long.TryParse(match.Groups["start"].Value, out long lineStartMs) &&
                long.TryParse(match.Groups["dur"].Value, out long lineDurMs))
            {
                string content = match.Groups["content"].Value;
                var wordMatches = wordRegex.Matches(content);

                var words = new List<LyricWord>();
                var lineSb = new StringBuilder();

                foreach (Match wm in wordMatches)
                {
                    if (long.TryParse(wm.Groups["start"].Value, out long wStart) &&
                        long.TryParse(wm.Groups["dur"].Value, out long wDur))
                    {
                        string wText = wm.Groups["word"].Value;
                        lineSb.Append(wText);
                        words.Add(new LyricWord(TimeSpan.FromMilliseconds(wStart), TimeSpan.FromMilliseconds(wDur), wText));
                    }
                }

                string fullText = lineSb.ToString().Trim();
                if (string.IsNullOrEmpty(fullText)) fullText = content;

                result.Add(new LyricLine(
                    TimeSpan.FromMilliseconds(lineStartMs),
                    fullText,
                    TimeSpan.FromMilliseconds(lineDurMs),
                    words.Count > 0 ? words : null));
            }
        }

        return result;
    }

    private static void MergeSubLines(List<LyricLine> mainLines, string? subLrc)
    {
        if (string.IsNullOrWhiteSpace(subLrc) || mainLines.Count == 0) return;
        var subLines = LrcParser.Parse(subLrc);
        if (subLines.Count == 0) return;

        foreach (var main in mainLines)
        {
            LyricLine? bestMatch = null;
            double minDiff = 1.5;

            foreach (var sub in subLines)
            {
                double diff = Math.Abs((main.Timestamp - sub.Timestamp).TotalSeconds);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestMatch = sub;
                }
            }

            if (bestMatch != null && !string.IsNullOrWhiteSpace(bestMatch.Text))
            {
                main.SubText = bestMatch.Text;
            }
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
                System.Diagnostics.Debug.WriteLine($"[Lyrics] Rejected poisoned gibberish from caching: {path}");
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
                        System.Diagnostics.Debug.WriteLine($"[Lyrics] Auto-purged poisoned cache file: {file}");
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

    public static void ClearAllCache() => ClearDiskCacheFiles();

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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
