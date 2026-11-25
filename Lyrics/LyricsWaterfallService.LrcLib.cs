using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LyricalWidget.Media;
using LyricalWidget.Services;

namespace LyricalWidget.Lyrics;

public partial class LyricsWaterfallService
{
    private async Task<LrcDocument?> FetchFromLrcLibAsync(TrackInfo track, string cleanTitle, string cleanArtist, int durationSec, CancellationToken ct)
    {
        string getUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
        if (!string.IsNullOrWhiteSpace(track.Album)) getUrl += $"&album_name={Uri.EscapeDataString(track.Album)}";
        if (durationSec > 0) getUrl += $"&duration={durationSec}";

        var directDoc = await QueryLrcLibEndpointAsync(getUrl, ct);
        if (directDoc != null && directDoc.HasSyncedLyrics) return directDoc;

        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            string simpleUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
            if (durationSec > 0) simpleUrl += $"&duration={durationSec}";
            var simpleDoc = await QueryLrcLibEndpointAsync(simpleUrl, ct);
            if (simpleDoc != null && simpleDoc.HasSyncedLyrics) return simpleDoc;
        }

        string searchQ = $"{cleanArtist} {cleanTitle}".Trim();
        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(searchQ)}";
        var fuzzyDoc = track.IsBrowserSource
            ? await QueryLrcLibFuzzySearchEndpointAsync(searchUrl, durationSec, ct, cleanTitle, cleanArtist)
            : await QueryLrcLibFuzzySearchEndpointAsync(searchUrl, durationSec, ct);
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

    private async Task<LrcDocument?> QueryLrcLibFuzzySearchEndpointAsync(string url, int targetDurationSec, CancellationToken ct, string? requiredTitle = null, string? requiredArtist = null)
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

                if (requiredTitle != null && requiredArtist != null)
                {
                    string itemTitle = item.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "";
                    string itemArtist = item.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "";
                    double itemDur = item.TryGetProperty("duration", out var idur) && idur.ValueKind == JsonValueKind.Number ? idur.GetDouble() : 0;

                    if (!MetadataCleaner.NamesMatch(MetadataCleaner.CleanTitle(itemTitle), requiredTitle) ||
                        !ArtistMatches(itemArtist, requiredArtist)) continue;
                    // Music videos often run longer than the track; far beyond that it is a mix or a different recording
                    if (targetDurationSec > 0 && Math.Abs(itemDur - targetDurationSec) > 45) continue;
                }

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

    private static bool ArtistMatches(string candidate, string required)
    {
        return MetadataCleaner.NamesMatch(candidate, required) ||
               MetadataCleaner.NamesMatch(MetadataCleaner.CleanArtist(candidate), required);
    }
}
