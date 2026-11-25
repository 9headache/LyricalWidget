using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LyricalWidget.Common;

namespace LyricalWidget.Services;

public sealed record ReleaseNote(string Title, string Text);

/// <summary>
/// Loads the changelog for the running version, with a built-in list for when GitHub has none to offer.
/// </summary>
public static class ReleaseNotesService
{
    public static IReadOnlyList<ReleaseNote> Fallback { get; } = new[]
    {
        new ReleaseNote("What's New in v2.5.0 — Moonlight Bloom", ""),
        new ReleaseNote("Highlights", ""),
        new ReleaseNote("Moonlight Bloom Kinetic Style", "Word-by-word glow sweeps and dynamic beat-reactive scaling."),
        new ReleaseNote("Wide-Angle Fisheye Lens", "Optional 60 FPS GPU fisheye mode for Moonlight Bloom with an automatic safe-zone so lyrics never clip at the edges."),
        new ReleaseNote("Windowed & Snappable Vibe Mode", "Vibe Mode now runs as a resizable borderless window with native Windows Snap support, plus F11 for true fullscreen (Esc returns to windowed mode)."),
        new ReleaseNote("Multi-Monitor Support", "Automatically detects multi-display setups so you can pick which screen Vibe Mode opens on from Settings or the System Tray."),
        new ReleaseNote("Ambient Idle Mode", "Pausing playback smoothly fades into a glowing obsidian clock with subtle particle drift."),
        new ReleaseNote("Resizable Widget Album Art", "Adjust the desktop widget cover between 36px and 180px (with Compact, Small, Medium, Large, and Default presets) with zero corner clipping."),
        new ReleaseNote("Dynamic Word Pacing Pill", "Added an optional pacing indicator for line-synced tracks in Kinetic mode, with a quick dismiss and Settings toggle."),
        new ReleaseNote("Fixes", ""),
        new ReleaseNote("", "Fixed toolbar dropdown menus misaligning on repeated hovers in Vibe Mode."),
        new ReleaseNote("", "Fixed the Kinetic track-title overlay staying stuck over the album cover on tracks without lyrics."),
        new ReleaseNote("", "General stability, cleanup, and settings persistence improvements across restarts.")
    };

    public static async Task<IReadOnlyList<ReleaseNote>> GetForCurrentVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LyricalWidget");

            string json = await client.GetStringAsync(AppVersionInfo.ReleasesApiUrl, cancellationToken);
            return FromReleaseJson(json, AppVersionInfo.Version);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Release notes unavailable: {ex.Message}");
            return Fallback;
        }
    }

    /// <summary>Returns the release's notes when its tag is the given version, otherwise the built-in list.</summary>
    public static IReadOnlyList<ReleaseNote> FromReleaseJson(string json, string version)
    {
        using var doc = JsonDocument.Parse(json);
        string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        if (!UpdateService.TryParseCleanVersion(tag, out var remote) ||
            !UpdateService.TryParseCleanVersion(version, out var local) ||
            Normalize(remote) != Normalize(local))
        {
            return Fallback;
        }

        var notes = ParseMarkdown(body);
        return notes.Count > 0 ? notes : Fallback;
    }

    // "2.2" and "2.2.0" are the same release
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static List<ReleaseNote> ParseMarkdown(string body)
    {
        var notes = new List<ReleaseNote>();
        bool skipSection = false;

        string withoutHtml = Regex.Replace(body, @"<!--.*?-->|</?[A-Za-z][^>]*>", "", RegexOptions.Singleline);
        foreach (string rawLine in withoutHtml.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || Regex.IsMatch(line, @"^([-*_]\s*){3,}$")) continue;

            var heading = Regex.Match(line, @"^#{1,6}\s+(.*)$");
            if (heading.Success)
            {
                string title = StripInline(heading.Groups[1].Value);
                // Install steps make no sense to someone already running this version
                skipSection = Regex.IsMatch(title, @"^(how to|install|download)", RegexOptions.IgnoreCase);
                if (!skipSection && !Regex.IsMatch(title, @"^what.?s new", RegexOptions.IgnoreCase))
                {
                    notes.Add(new ReleaseNote(title, ""));
                }
                continue;
            }
            if (skipSection) continue;

            line = Regex.Replace(line, @"^([-*+]|\d+\.)\s+", "");
            var lead = Regex.Match(line, @"^\*\*(.+?)\*\*\s*[:\-–—]?\s*(.*)$");
            var note = lead.Success
                ? new ReleaseNote(StripInline(lead.Groups[1].Value).TrimEnd(':', ' '), StripInline(lead.Groups[2].Value))
                : new ReleaseNote("", StripInline(line));
            if (note.Title.Length > 0 || note.Text.Length > 0) notes.Add(note);
        }
        return notes;
    }

    private static string StripInline(string text)
    {
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        return Regex.Replace(text, @"\*\*|__|`", "").Trim();
    }
}
