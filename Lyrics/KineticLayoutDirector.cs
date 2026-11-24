using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LyricalWidget.Media;

namespace LyricalWidget.Lyrics;

/// <summary>
/// Measures and arranges typographic phrase blocks for Fullscreen Kinetic Typography Mode.
/// </summary>
public sealed partial class KineticLayoutDirector
{
    private static readonly HashSet<string> ConnectorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "in", "on", "at", "to", "for", "of", "and", "or", "but", "so",
        "is", "it", "my", "your", "i", "you", "we", "he", "she", "me", "us", "be", "was",
        "i'm", "it's", "you're", "we're", "they're", "all", "with", "from", "by", "as", "if",
        "not", "no", "that", "this", "do", "did", "up", "down", "out", "off", "too"
    };


    private KineticTemplateType _lastTemplate = KineticTemplateType.PopRhythmic;
    private Point _currentAnchor = new(0, 0);
    private int _phraseCounter = 0;
    private int _lastIconLineIndex = -99;

    private int _posterPhraseCounter = 0;
    private int _lastPosterLayout = -1;

    public void Reset()
    {
        _lastTemplate = KineticTemplateType.PopRhythmic;
        _currentAnchor = new Point(0, 0);
        _phraseCounter = 0;
        _lastIconLineIndex = -99;
        _posterPhraseCounter = 0;
        _lastPosterLayout = -1;
    }

    public KineticPhraseVisual CreatePhrase(
        LyricLine line,
        int lineIndex,
        IReadOnlyList<LyricLine> allLines,
        VibePalette palette,
        string vibeSetting,
        double bassEnergy,
        double beatImpulse,
        Size viewportSize)
    {
        return CreatePhrase(line, lineIndex, allLines, palette, vibeSetting, "Editorial", bassEnergy, beatImpulse, viewportSize);
    }

    public KineticPhraseVisual CreatePhrase(
        LyricLine line,
        int lineIndex,
        IReadOnlyList<LyricLine> allLines,
        VibePalette palette,
        string vibeSetting,
        string kineticPreset,
        double bassEnergy,
        double beatImpulse,
        Size viewportSize)
    {
        if (string.Equals(kineticPreset, "Poster", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPosterPhrase(line, lineIndex, allLines, palette, vibeSetting, bassEnergy, beatImpulse, viewportSize);
        }

        return BuildEditorialPhrase(line, lineIndex, allLines, palette, vibeSetting, bassEnergy, beatImpulse, viewportSize);
    }


    private static Point ComputeNextAnchor(Point current, int counter, double vpW, double vpH)
    {
        double stepX = vpW * 0.12;
        double stepY = vpH * 0.10;

        int pattern = counter % 4;
        return pattern switch
        {
            0 => new Point(stepX * 0.5, stepY * 0.5),
            1 => new Point(-stepX * 0.4, stepY * 0.8),
            2 => new Point(stepX * 0.6, -stepY * 0.5),
            _ => new Point(-stepX * 0.5, -stepY * 0.6)
        };
    }

    private static List<(string text, TimeSpan start, TimeSpan end)> ExtractWordTimings(LyricLine line, TimeSpan lineDuration)
    {
        var rawWords = new List<(string text, TimeSpan start, TimeSpan end)>();

        if (line.HasWordSync && line.Words != null && line.Words.Count > 0)
        {
            foreach (var w in line.Words)
            {
                rawWords.Add((w.Text, w.Timestamp, w.Timestamp + w.Duration));
            }
        }
        else
        {
            var split = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (split.Length > 0)
            {
                int totalChars = 0;
                foreach (var s in split) totalChars += Math.Max(1, s.Length);

                double vocalSpan = Math.Min(lineDuration.TotalSeconds, Math.Max(1.2, split.Length * 0.45));
                double accumulated = 0;

                for (int i = 0; i < split.Length; i++)
                {
                    double wSpan = (Math.Max(1, split[i].Length) / (double)totalChars) * vocalSpan;
                    var wStart = line.Timestamp + TimeSpan.FromSeconds(accumulated);
                    var wEnd = wStart + TimeSpan.FromSeconds(wSpan);
                    accumulated += wSpan;
                    rawWords.Add((split[i], wStart, wEnd));
                }
            }
        }

        return rawWords;
    }

    private static KineticTemplateType ChooseTemplate(double wps, string vibeSetting)
    {
        string vibe = vibeSetting;
        if (string.Equals(vibe, "Auto", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(vibe))
        {
            if (wps < 2.2) return KineticTemplateType.EditorialCalm;
            if (wps < 3.4) return KineticTemplateType.PopRhythmic;
            return KineticTemplateType.RapHighEnergy;
        }

        if (string.Equals(vibe, "Calm", StringComparison.OrdinalIgnoreCase)) return KineticTemplateType.EditorialCalm;
        if (string.Equals(vibe, "Rap", StringComparison.OrdinalIgnoreCase)) return KineticTemplateType.RapHighEnergy;
        return KineticTemplateType.PopRhythmic;
    }

    private static HashSet<int> IdentifyHeroWords(List<(string text, TimeSpan start, TimeSpan end)> words)
    {
        var heroIndices = new HashSet<int>();
        if (words.Count == 0) return heroIndices;

        for (int i = 0; i < words.Count; i++)
        {
            string clean = CleanWord(words[i].text);
            if (WordToIconKey.ContainsKey(clean))
            {
                heroIndices.Add(i);
                break;
            }
        }

        int bestIdx = -1;
        int maxLen = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (heroIndices.Contains(i)) continue;
            string clean = CleanWord(words[i].text);
            if (!ConnectorWords.Contains(clean) && clean.Length > maxLen)
            {
                maxLen = clean.Length;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
        {
            heroIndices.Add(bestIdx);
        }

        if (words.Count >= 6 && heroIndices.Count < 2)
        {
            int lastIdx = words.Count - 1;
            string lastClean = CleanWord(words[lastIdx].text);
            if (!ConnectorWords.Contains(lastClean) && !heroIndices.Contains(lastIdx))
            {
                heroIndices.Add(lastIdx);
            }
        }

        if (heroIndices.Count == 0)
        {
            heroIndices.Add(words.Count - 1);
        }

        if (words.Count <= 3 && heroIndices.Count > 1)
        {
            int first = heroIndices.First();
            heroIndices.Clear();
            heroIndices.Add(first);
        }

        return heroIndices;
    }

    private static string CleanWord(string raw)
    {
        return raw.Trim(' ', ',', '.', '!', '?', '"', '\'', ':', ';', '(', ')', '[', ']', '{', '}', '“', '”', '`', '~');
    }

}
