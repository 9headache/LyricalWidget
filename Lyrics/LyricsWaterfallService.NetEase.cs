using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LyricalWidget.Services;

namespace LyricalWidget.Lyrics;

public partial class LyricsWaterfallService
{
    private static bool NetEaseSongMatches(JsonElement song, string title, string artist)
    {
        string songName = song.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (!MetadataCleaner.NamesMatch(MetadataCleaner.CleanTitle(songName), title)) return false;
        if (!song.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array) return false;

        foreach (var a in artists.EnumerateArray())
        {
            if (a.TryGetProperty("name", out var an) && ArtistMatches(an.GetString() ?? "", artist)) return true;
        }
        return false;
    }

    private async Task<LrcDocument?> FetchFromNetEaseAsync(string cleanTitle, string cleanArtist, TimeSpan duration, CancellationToken ct, bool verifyNames = false)
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
                if (verifyNames && !NetEaseSongMatches(song, cleanTitle, cleanArtist)) continue;

                long id = song.GetProperty("id").GetInt64();
                long songDurMs = song.TryGetProperty("duration", out var dur) ? dur.GetInt64() : 0;

                double diffSec = targetDurMs > 0 ? Math.Abs((songDurMs - targetDurMs) / 1000.0) : 0;

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
}
