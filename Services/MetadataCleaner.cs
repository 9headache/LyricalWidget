using System;
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

        // Extract primary artist if multiple are separated
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
