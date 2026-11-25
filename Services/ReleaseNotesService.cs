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
        new ReleaseNote("Obsidian & Chrome UI overhaul", "Windows, sliders and controls now share one dark, brushed-chrome look."),
        new ReleaseNote("Custom dark system tray menu", "A redesigned menu with instant Lyrics Refresh (Alt+R) and a Quick Guide shortcut."),
        new ReleaseNote("Live update download progress bar", "Follow the download, preparation and restart, and cancel any time before the restart."),
        new ReleaseNote("Reliable first-launch onboarding", "New users always see the welcome guide, and settings stay in your own user profile.")
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
