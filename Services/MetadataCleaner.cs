using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricalWidget.Services;

public static class MetadataCleaner
{
    private static readonly Regex FeatRegex = new(@"\s*[\(\[](feat\.|ft\.|featuring).*?[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FeatTrailingRegex = new(@"\s+(feat\.|ft\.|featuring)\s+.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RemasterRegex = new(@"\s*[\(\[](remastered|remaster|deluxe|bonus|anniversary|expanded).*?[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RemasterDashRegex = new(@"\s*-\s*(remastered|remaster|deluxe|bonus|anniversary|expanded).*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoRegex = new(@"\s*[\(\[](official\s*(music\s*)?video|official\s*audio|visualizer|lyric\s*video|lyrics|audio|4k|hd|hq).*?[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoBracketsRegex = new(@"\s*\[(4k|hd|hq|official\s*video)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TopicRegex = new(@"\s*[-–—]\s*Topic\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // YouTube / browser titles
    private static readonly Regex NotificationCountRegex = new(@"^\(\d+\+?\)\s+", RegexOptions.Compiled);
    private static readonly Regex YouTubeSuffixRegex = new(@"\s*[-–—]\s*YouTube(\s+Music)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PipeTailRegex = new(@"\s*[|｜].*$", RegexOptions.Compiled);
    private static readonly Regex VideoTagRegex = new(
        @"\s*[\(\[【](?=[^\)\]】]*\b(official|lyrics?|letra|m/?v|music\s+video|video\s*clip|visuali[sz]er|audio|video|4k|8k|uhd|hd|hq|1080p|2160p|60\s*fps)\b)[^\)\]】]*[\)\]】]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingVideoWordsRegex = new(
        @"[\s\-–—]+(official\s+(hd\s+|4k\s+)?(music\s+video|lyric\s+video|video|audio|m/?v|visuali[sz]er)|lyric\s+video|music\s+video|m/v)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DashSplitRegex = new(@"\s+[-–—]\s+", RegexOptions.Compiled);
    private static readonly Regex QuotedTitleRegex = new(@"^(?<artist>.+?)\s+['""‘“](?<title>[^'""’”]+(?:['’]\w+[^'""’”]*)*)['""’”]$", RegexOptions.Compiled);
    private static readonly Regex VersionNoteRegex = new(
        @"^(live|radio\s+edit|acoustic|single\s+version|album\s+version|extended|mono|stereo|demo|instrumental|from\s|\w+\s+remix$|\d{4}\s+(remaster|mix))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VevoRegex = new(@"\s*VEVO$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> GenericChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "YouTube", "YouTube Music", "Spotify", "Google Chrome", "Microsoft Edge", "Mozilla Firefox", "Firefox", "Brave", "Opera", "Vivaldi"
    };

    /// <summary>Strips the video tags and suffixes that YouTube uploads add to a song title.</summary>
    public static string CleanYouTubeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        string clean = NotificationCountRegex.Replace(title.Trim(), "");
        clean = YouTubeSuffixRegex.Replace(clean, "");
        clean = PipeTailRegex.Replace(clean, "");
        clean = VideoTagRegex.Replace(clean, "");
        clean = TrailingVideoWordsRegex.Replace(clean, "");
        clean = clean.Trim(' ', '-', '—', '–');

        return clean.Length > 0 ? clean : title.Trim();
    }

    /// <summary>
    /// Reads a browser media title as ordered (artist, title) guesses. The first is the best one and is
    /// what gets displayed; the rest are only used to look up lyrics.
    /// </summary>
    public static List<(string Artist, string Title)> ParseBrowserMedia(string rawTitle, string rawChannel)
    {
        string title = CleanYouTubeTitle(rawTitle);
        string channel = (rawChannel ?? string.Empty).Trim();
        if (GenericChannels.Contains(channel)) channel = string.Empty;

        bool isTopicChannel = TopicRegex.IsMatch(channel);
        channel = VevoRegex.Replace(TopicRegex.Replace(channel, ""), "").Trim();

        var guesses = new List<(string Artist, string Title)>();
        void Add(string artist, string song)
        {
            artist = artist.Trim();
            song = song.Trim(' ', '"', '“', '”');
            if (song.Length > 0 && !guesses.Contains((artist, song))) guesses.Add((artist, song));
        }

        // "Artist - Topic" channels carry the bare song title
        if (!isTopicChannel)
        {
            var dash = DashSplitRegex.Match(title);
            if (dash.Success && dash.Index > 0)
            {
                string left = title[..dash.Index];
                string right = title[(dash.Index + dash.Length)..];

                if (!VersionNoteRegex.IsMatch(right) && !RemasterDashRegex.IsMatch(title))
                {
                    // "Track - Artist" uploads: the channel name tells which side is the artist
                    if (channel.Length > 0 && NamesMatch(right, channel) && !NamesMatch(left, channel))
                    {
                        Add(right, left);
                    }
                    else
                    {
                        Add(left, right);
                    }
                }
            }
            else
            {
                var quoted = QuotedTitleRegex.Match(title);
                if (quoted.Success) Add(quoted.Groups["artist"].Value, quoted.Groups["title"].Value);
            }
        }

        Add(channel, title);
        return guesses;
    }

    /// <summary>Loose name equality: ignores case, accents and punctuation, and allows one name to extend the other.</summary>
    public static bool NamesMatch(string a, string b)
    {
        string x = NormalizeName(a);
        string y = NormalizeName(b);
        if (x.Length == 0 || y.Length == 0) return false;
        if (x == y) return true;

        var (shorter, longer) = x.Length <= y.Length ? (x, y) : (y, x);
        return shorter.Length >= 4 && shorter.Length >= longer.Length * 0.5 && longer.Contains(shorter, StringComparison.Ordinal);
    }

    private static string NormalizeName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    public static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        string clean = FeatRegex.Replace(title, "");
        clean = FeatTrailingRegex.Replace(clean, "");
        clean = RemasterRegex.Replace(clean, "");
        clean = RemasterDashRegex.Replace(clean, "");
        clean = VideoRegex.Replace(clean, "");
        clean = VideoBracketsRegex.Replace(clean, "");

        return clean.Trim(' ', '-', '—', '–', '(', ')', '[', ']', '"', '\'');
    }

    public static string CleanArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;

        string clean = TopicRegex.Replace(artist, "");
        clean = FeatRegex.Replace(clean, "");
        clean = FeatTrailingRegex.Replace(clean, "");

        string[] separators = new[] { ",", ";", "/", "&", " feat.", " ft.", " Feat.", " Ft." };
        foreach (var sep in separators)
        {
            int idx = clean.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                clean = clean[..idx];
            }
        }

        return clean.Trim(' ', '-', '—', '–');
    }
}
