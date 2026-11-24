using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricalWidget.Lyrics;

public static partial class LrcParser
{
    private static readonly Regex TimestampRegex = new(
        @"\[(?<min>\d+):(?<sec>\d+(?:\.\d+)?)\]",
        RegexOptions.Compiled);

    public static List<LyricLine> Parse(string? lrcContent)
    {
        var rawLines = new List<(TimeSpan Timestamp, string Text)>();
        if (string.IsNullOrWhiteSpace(lrcContent))
        {
            return new List<LyricLine>();
        }

        string[] lines = lrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var matches = TimestampRegex.Matches(line);
            if (matches.Count == 0) continue;

            int lastMatchEnd = 0;
            foreach (Match match in matches)
            {
                int end = match.Index + match.Length;
                if (end > lastMatchEnd) lastMatchEnd = end;
            }

            string text = line.Substring(lastMatchEnd).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                // Strip empty/whitespace-only lines to prevent phantom gaps
                continue;
            }

            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                    double.TryParse(match.Groups["sec"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sec))
                {
                    TimeSpan timestamp = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec);
                    rawLines.Add((timestamp, text));
                }
            }
        }

        rawLines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var result = new List<LyricLine>(rawLines.Count);
        for (int i = 0; i < rawLines.Count; i++)
        {
            TimeSpan duration = TimeSpan.FromSeconds(4);
            if (i + 1 < rawLines.Count)
            {
                TimeSpan gap = rawLines[i + 1].Timestamp - rawLines[i].Timestamp;
                if (gap > TimeSpan.Zero)
                {
                    duration = gap > TimeSpan.FromMilliseconds(10000) ? TimeSpan.FromMilliseconds(10000) : gap;
                }
            }

            result.Add(new LyricLine(rawLines[i].Timestamp, rawLines[i].Text, duration));
        }

        return result;
    }
}
