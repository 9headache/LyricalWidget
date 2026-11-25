using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LyricalWidget.Media;
using LyricalWidget.Services;

namespace LyricalWidget.Lyrics;

public partial class LyricsWaterfallService : IDisposable
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    // In-memory LRU of the last 50 tracks
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

    public async Task<LrcDocument> GetLyricsAsync(TrackInfo track, Action<LrcDocument>? onUpgradeAvailable, CancellationToken cancellationToken = default)
    {
        if (!track.IsValid)
        {
            return LrcDocument.Empty();
        }

        string cacheKey = track.CacheKey;
        bool refetch = IsRefetchPending(cacheKey);
        LrcDocument? refetchFallback = null;

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

        var providerTasks = new List<Task<LrcDocument?>>
        {
            RunProviderWithTimeout(ct => track.IsBrowserSource
                ? FetchFirstCandidateAsync(track, (title, artist) => FetchFromLrcLibAsync(track, title, artist, durationSec, ct), ct)
                : FetchFromLrcLibAsync(track, cleanTitle, cleanArtist, durationSec, ct), cancellationToken),
            RunProviderWithTimeout(ct => track.IsBrowserSource
                ? FetchFirstCandidateAsync(track, (title, artist) => FetchFromNetEaseAsync(title, artist, track.Duration, ct, verifyNames: true), ct)
                : FetchFromNetEaseAsync(cleanTitle, cleanArtist, track.Duration, ct), cancellationToken)
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
            // Give NetEase a moment to answer with word-level timing
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

            // Keep listening in the background for word-level timing
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

    /// <summary>Tries each (artist, title) guess for a browser video in order until one has synced lyrics.</summary>
    private static async Task<LrcDocument?> FetchFirstCandidateAsync(TrackInfo track, Func<string, string, Task<LrcDocument?>> fetch, CancellationToken ct)
    {
        foreach (var (artist, title) in track.LookupCandidates)
        {
            ct.ThrowIfCancellationRequested();

            string cleanTitle = MetadataCleaner.CleanTitle(title);
            string cleanArtist = MetadataCleaner.CleanArtist(artist);
            // Without both names any hit would be a coincidence
            if (cleanTitle.Length == 0 || cleanArtist.Length == 0) continue;

            var doc = await fetch(cleanTitle, cleanArtist);
            if (doc != null && doc.HasSyncedLyrics) return doc;
        }
        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
