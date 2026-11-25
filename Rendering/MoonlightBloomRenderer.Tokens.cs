using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LyricalWidget.Lyrics;

namespace LyricalWidget.Rendering;

// Splitting a line into timed words
public sealed partial class MoonlightBloomRenderer
{
    private static List<Token> InterludeTokens(double posMs) => new() { new Token("♪", posMs, false, true) };

    private List<Token> BuildTokens(int index)
    {
        var lines = _lyrics.Lines;
        var line = lines[index];
        double startMs = line.Timestamp.TotalMilliseconds;

        if (line.Words != null && line.Words.Count > 0)
        {
            return TokensFromWordSync(line);
        }

        var tokens = SplitText(line.Text.Trim());
        double nextMs = index + 1 < lines.Count ? lines[index + 1].Timestamp.TotalMilliseconds : line.EndTime.TotalMilliseconds;
        double span = Math.Max(250.0, (nextMs - startMs) * 0.85);
        // A long gap after the line should not slow its words to a crawl
        double weightTotal = tokens.Sum(t => t.Text.Length + 2.0);
        span = Math.Min(span, weightTotal * 85.0);

        double passed = 0.0;
        for (int i = 0; i < tokens.Count; i++)
        {
            tokens[i] = tokens[i] with { StartMs = startMs + (span * passed / weightTotal) };
            passed += tokens[i].Text.Length + 2.0;
        }
        return tokens;
    }

    private static List<Token> TokensFromWordSync(LyricLine line)
    {
        var tokens = new List<Token>();
        string full = line.Text;
        int cursor = 0;
        // Sources that drop spaces from both the words and the line carry plain word lists
        bool wordsAreSpaced = line.Words!.Any(w => w.Text.Length > 0 && (char.IsWhiteSpace(w.Text[0]) || char.IsWhiteSpace(w.Text[^1])));

        foreach (var word in line.Words!)
        {
            string text = word.Text.Trim();
            if (text.Length == 0) continue;

            bool gapAfter = word.Text.Length > 0 && char.IsWhiteSpace(word.Text[^1]);
            int at = full.IndexOf(text, Math.Min(cursor, full.Length), StringComparison.Ordinal);
            if (at >= 0)
            {
                cursor = at + text.Length;
                gapAfter |= cursor < full.Length && char.IsWhiteSpace(full[cursor]);
            }
            else if (!wordsAreSpaced && !IsCjk(text[^1]))
            {
                gapAfter = true;
            }
            tokens.Add(new Token(text, word.Timestamp.TotalMilliseconds, gapAfter, gapAfter || IsCjk(text[^1])));
        }
        return tokens;
    }

    private static List<Token> SplitText(string text)
    {
        var tokens = new List<Token>();
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!word.Any(IsCjk))
            {
                tokens.Add(new Token(word, 0, true, true));
                continue;
            }

            // Scripts without spaces reveal character by character
            var parts = new List<string>();
            var e = StringInfo.GetTextElementEnumerator(word);
            while (e.MoveNext())
            {
                string element = e.GetTextElement();
                bool joins = parts.Count > 0 && !IsCjk(element[0]) && !IsCjk(parts[^1][^1]);
                if (joins) parts[^1] += element;
                else parts.Add(element);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                tokens.Add(new Token(parts[i], 0, i == parts.Count - 1, true));
            }
        }
        return tokens;
    }

    private static bool IsCjk(char c) =>
        (c >= 0x3040 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0x0E00 && c <= 0x0E7F);
}
