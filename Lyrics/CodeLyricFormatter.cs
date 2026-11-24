using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using LyricalWidget.Media;

namespace LyricalWidget.Lyrics;

public enum CodeTokenType
{
    Comment,
    Keyword,
    Function,
    Identifier,
    StringLiteral,
    Operator,
    Punctuation,
    Number
}

public sealed class CodeToken
{
    public string Text { get; init; } = string.Empty;
    public CodeTokenType Type { get; init; } = CodeTokenType.Identifier;
    public string? KeywordDoodle { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
}

public sealed class FormattedCodeLine
{
    public int LineIndex { get; init; }
    public int LineNumber { get; init; }
    public List<CodeToken> Tokens { get; init; } = new();
    public TimeSpan Timestamp { get; init; }
    public TimeSpan EndTime { get; init; }
    public bool IsHeaderComment { get; init; }
    public string FullText { get; init; } = string.Empty;
}

/// <summary>
/// Procedurally formats synced lyric lines into syntax-highlighted JS/C# style code tokens.
/// </summary>
public static class CodeLyricFormatter
{
    private const int MaxLineLength = 40;

    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "when", "if", "once", "never", "always", "return", "async", "await", "while",
        "const", "let", "var", "function", "class", "new", "this", "true", "false", "null"
    };

    private static readonly HashSet<string> ActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "rule", "sweep", "sing", "feel", "hold", "breathe", "listen", "dream",
        "burn", "run", "fly", "fall", "break", "love", "heal", "cry", "dance",
        "shine", "wait", "call", "stay", "remember", "forget", "know", "see",
        "drippin", "drip", "pray", "shake", "drop", "touch", "swim", "climb", "drive", "chase"
    };

    private static readonly (string Pattern, string Replacement)[] IdiomPairReplacements =
    {
        (@"\bused\s+to\b", "usedTo"),
        (@"\bgotta\s+get\b", "gottaGet"),
        (@"\bwant\s+to\b", "wantTo"),
        (@"\btrying\s+to\b", "tryingTo"),
        (@"\blooking\s+for\b", "lookingFor"),
        (@"\bwake\s+up\b", "wakeUp"),
        (@"\bhold\s+on\b", "holdOn"),
        (@"\blet\s+go\b", "letGo"),
        (@"\bset\s+free\b", "setFree")
    };

    private static readonly (string Pattern, string Replacement)[] WordReplacements =
    {
        (@"\band\b", " && "),
        (@"\bor\b", " || "),
        (@"\b(is|was|are|am|were)\b", " = "),
        (@"\bnot\b", "!")
    };

    private static readonly Regex TokenRegex = new(
        @"(?<comment>//.*$)|(?<str>""[^""]*"")|(?<op>&&|\|\||==|!=|<=|>=|=>|[=!<>\+\-\*/])|(?<punct>[(){}\[\],;\.])|(?<num>\b\d+\b)|(?<word>[a-zA-Z_][a-zA-Z0-9_']*)|(?<ws>\s+)",
        RegexOptions.Compiled);

    public static List<FormattedCodeLine> FormatLyrics(
        IReadOnlyList<LyricLine> lines,
        TrackInfo? track)
    {
        var result = new List<FormattedCodeLine>();

        string title = !string.IsNullOrWhiteSpace(track?.Title) ? track.Title.Trim() : "Track";
        string artist = !string.IsNullOrWhiteSpace(track?.Artist) ? track.Artist.Trim() : "Unknown";

        // Line 1: Header Comment
        string headerComment = $"// {title} — {artist}";
        var headerTokens = new List<CodeToken>
        {
            new()
            {
                Text = headerComment,
                Type = CodeTokenType.Comment,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(2.0)
            }
        };

        result.Add(new FormattedCodeLine
        {
            LineIndex = -1,
            LineNumber = 1,
            Tokens = headerTokens,
            Timestamp = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(2.0),
            IsHeaderComment = true,
            FullText = headerComment
        });

        if (lines == null || lines.Count == 0)
        {
            return result;
        }

        int codeLineNumber = 2;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            var formattedLines = FormatSingleLine(line, i, ref codeLineNumber);
            result.AddRange(formattedLines);
        }

        return result;
    }

    private static List<FormattedCodeLine> FormatSingleLine(LyricLine line, int lineIndex, ref int codeLineNumber)
    {
        TimeSpan duration = line.Duration > TimeSpan.Zero ? line.Duration : TimeSpan.FromSeconds(3.5);
        TimeSpan lineEnd = line.Timestamp + duration;
        string rawText = line.Text.Trim();

        // Non-Latin script fallback: preserve original text safely inside sing("...")
        if (HasNonAscii(rawText))
        {
            string codeStr = $"sing(\"{rawText}\");";
            return WrapAndTokenizeLine(codeStr, lineIndex, ref codeLineNumber, line.Timestamp, lineEnd, null);
        }

        // Procedural lyric code transformation
        string codeLine = BuildCodeStatement(rawText);

        // Detect keyword doodle (capped to 1 per line)
        string? doodle = DetectDoodle(rawText);

        return WrapAndTokenizeLine(codeLine, lineIndex, ref codeLineNumber, line.Timestamp, lineEnd, doodle);
    }

    private static string BuildCodeStatement(string raw)
    {
        string text = raw;

        // 1. CamelCase at most one 2-word helper pair per line
        foreach (var (pattern, replacement) in IdiomPairReplacements)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                text = text.Remove(m.Index, m.Length).Insert(m.Index, replacement);
                break;
            }
        }

        // 2. Strict word-boundary standalone replacements (&&, ||, =, !)
        foreach (var (pattern, replacement) in WordReplacements)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        // 3. Standalone "the <word>" -> "the.<word>" (strict word boundary, max 2 per line)
        text = Regex.Replace(text, @"\bthe\s+([a-zA-Z_][a-zA-Z0-9_']*)", "the.$1", RegexOptions.IgnoreCase);

        // Clean up redundant spaces around operators
        text = Regex.Replace(text, @"\s*&&\s*", " && ");
        text = Regex.Replace(text, @"\s*\|\|\s*", " || ");
        text = Regex.Replace(text, @"\s*=\s*", " = ");
        text = text.Trim();

        // 4. Method call / action verb formatting
        // Check if the line has an ActionVerb to format as a method call
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int verbIndex = -1;
        string? foundVerb = null;

        for (int w = 0; w < words.Length; w++)
        {
            string clean = CleanForVerbCheck(words[w]);
            if (ActionVerbs.Contains(clean))
            {
                verbIndex = w;
                foundVerb = clean.ToLowerInvariant();
                break;
            }
        }

        string result;
        if (verbIndex >= 0 && foundVerb != null)
        {
            // If the verb is the first word: verb(rest);
            if (verbIndex == 0)
            {
                string rest = words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : string.Empty;
                result = string.IsNullOrEmpty(rest) ? $"{foundVerb}();" : $"{foundVerb}({rest});";
            }
            else
            {
                // Verb preceded by a subject/caller: caller.verb(args);
                string before = string.Join(" ", words, 0, verbIndex);
                string after = words.Length > verbIndex + 1 ? string.Join(" ", words, verbIndex + 1, words.Length - verbIndex - 1) : string.Empty;

                if (before.EndsWith("&&") || before.EndsWith("||") || before.EndsWith("="))
                {
                    result = string.IsNullOrEmpty(after) ? $"{before} {foundVerb}();" : $"{before} {foundVerb}({after});";
                }
                else if (before.Contains(" && ") || before.Contains(" || ") || before.Contains(" = "))
                {
                    // e.g. "curvaceous && she" -> "curvaceous && she.drippin(after);"
                    int lastOpSpace = Math.Max(before.LastIndexOf(" && ", StringComparison.Ordinal),
                        Math.Max(before.LastIndexOf(" || ", StringComparison.Ordinal), before.LastIndexOf(" = ", StringComparison.Ordinal)));
                    
                    int splitAt = lastOpSpace >= 0 ? lastOpSpace + 4 : 0;
                    string prefix = before[..splitAt];
                    string subject = before[splitAt..].Trim();

                    if (!string.IsNullOrEmpty(subject))
                    {
                        result = string.IsNullOrEmpty(after)
                            ? $"{prefix}{subject}.{foundVerb}();"
                            : $"{prefix}{subject}.{foundVerb}({after});";
                    }
                    else
                    {
                        result = string.IsNullOrEmpty(after)
                            ? $"{prefix}{foundVerb}();"
                            : $"{prefix}{foundVerb}({after});";
                    }
                }
                else
                {
                    result = string.IsNullOrEmpty(after)
                        ? $"{before}.{foundVerb}();"
                        : $"{before}.{foundVerb}({after});";
                }
            }
        }
        else if (text.StartsWith("when ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
        {
            // Condition statement
            int sp = text.IndexOf(' ');
            string kw = text[..sp].ToLowerInvariant();
            string cond = text[(sp + 1)..].Trim();
            result = $"{kw} ({cond});";
        }
        else
        {
            // General expression ending with semicolon
            result = text.EndsWith(';') ? text : $"{text};";
        }

        return result;
    }

    private static List<FormattedCodeLine> WrapAndTokenizeLine(
        string codeLine,
        int lineIndex,
        ref int codeLineNumber,
        TimeSpan start,
        TimeSpan end,
        string? doodle)
    {
        var result = new List<FormattedCodeLine>();

        if (codeLine.Length <= MaxLineLength)
        {
            var tokens = Tokenize(codeLine, start, end, doodle);
            result.Add(new FormattedCodeLine
            {
                LineIndex = lineIndex,
                LineNumber = codeLineNumber++,
                Tokens = tokens,
                Timestamp = start,
                EndTime = end,
                FullText = codeLine
            });
            return result;
        }

        // Split line into continuation lines near MaxLineLength
        var parts = SplitIntoContinuationParts(codeLine, MaxLineLength);
        double totalChars = 0;
        foreach (var p in parts) totalChars += p.Length;
        if (totalChars <= 0) totalChars = 1;

        double totalMs = (end - start).TotalMilliseconds;
        double curMs = start.TotalMilliseconds;

        for (int p = 0; p < parts.Count; p++)
        {
            string part = parts[p];
            double partMs = (part.Length / totalChars) * totalMs;
            TimeSpan partStart = TimeSpan.FromMilliseconds(curMs);
            TimeSpan partEnd = TimeSpan.FromMilliseconds(curMs + partMs);
            curMs += partMs;

            // Only first part gets doodle if any
            string? partDoodle = p == 0 ? doodle : null;
            var tokens = Tokenize(part, partStart, partEnd, partDoodle);

            result.Add(new FormattedCodeLine
            {
                LineIndex = lineIndex,
                LineNumber = codeLineNumber++,
                Tokens = tokens,
                Timestamp = partStart,
                EndTime = partEnd,
                FullText = part
            });
        }

        return result;
    }

    private static List<string> SplitIntoContinuationParts(string text, int maxLen)
    {
        var parts = new List<string>();
        string remaining = text;

        while (remaining.Length > maxLen)
        {
            int bestSplit = -1;

            // Look for clean break between index 26 and maxLen
            int searchStart = Math.Min(maxLen, remaining.Length - 1);
            int searchEnd = Math.Max(24, maxLen - 16);

            for (int i = searchStart; i >= searchEnd; i--)
            {
                char c = remaining[i];
                if (c == ',' && i + 1 < remaining.Length && remaining[i + 1] == ' ')
                {
                    bestSplit = i + 2;
                    break;
                }
                if (c == '(' || c == '{')
                {
                    bestSplit = i + 1;
                    break;
                }
                if (c == ' ' && (i >= 2 && remaining[i - 1] == '&' || remaining[i - 1] == '|' || remaining[i - 1] == '='))
                {
                    bestSplit = i + 1;
                    break;
                }
            }

            if (bestSplit < 0)
            {
                // Fallback: search for any space
                for (int i = searchStart; i >= searchEnd; i--)
                {
                    if (remaining[i] == ' ')
                    {
                        bestSplit = i + 1;
                        break;
                    }
                }
            }

            if (bestSplit <= 0 || bestSplit >= remaining.Length)
            {
                bestSplit = maxLen;
            }

            string head = remaining[..bestSplit].TrimEnd();
            parts.Add(head);

            remaining = "    " + remaining[bestSplit..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            parts.Add(remaining);
        }

        return parts;
    }

    private static List<CodeToken> Tokenize(string text, TimeSpan start, TimeSpan end, string? lineDoodle)
    {
        var tokens = new List<CodeToken>();
        var matches = TokenRegex.Matches(text);

        double totalMs = Math.Max(100.0, (end - start).TotalMilliseconds);
        double totalLen = Math.Max(1, text.Length);

        string pendingWhitespace = string.Empty;
        bool doodleUsed = false;

        for (int m = 0; m < matches.Count; m++)
        {
            var match = matches[m];

            if (match.Groups["ws"].Success)
            {
                pendingWhitespace += match.Value;
                continue;
            }

            string tokenText = pendingWhitespace + match.Value;
            pendingWhitespace = string.Empty;

            CodeTokenType type = CodeTokenType.Identifier;
            string? tokenDoodle = null;

            if (match.Groups["comment"].Success)
            {
                type = CodeTokenType.Comment;
            }
            else if (match.Groups["str"].Success)
            {
                type = CodeTokenType.StringLiteral;
            }
            else if (match.Groups["op"].Success)
            {
                type = CodeTokenType.Operator;
            }
            else if (match.Groups["punct"].Success)
            {
                type = CodeTokenType.Punctuation;
            }
            else if (match.Groups["num"].Success)
            {
                type = CodeTokenType.Number;
            }
            else if (match.Groups["word"].Success)
            {
                string word = match.Value;
                if (ReservedKeywords.Contains(word))
                {
                    type = CodeTokenType.Keyword;
                }
                else if (ActionVerbs.Contains(word))
                {
                    type = CodeTokenType.Function;
                }
                else if (m + 1 < matches.Count && matches[m + 1].Value.StartsWith('('))
                {
                    type = CodeTokenType.Function;
                }
                else
                {
                    type = CodeTokenType.Identifier;
                }

                if (!doodleUsed && lineDoodle != null && MatchesDoodleKeyword(word, lineDoodle))
                {
                    tokenDoodle = lineDoodle;
                    doodleUsed = true;
                }
            }

            int charStart = match.Index - (tokenText.Length - match.Length);
            int charEnd = match.Index + match.Length;

            double fracStart = Math.Clamp(charStart / totalLen, 0.0, 1.0);
            double fracEnd = Math.Clamp(charEnd / totalLen, 0.0, 1.0);

            TimeSpan tStart = start + TimeSpan.FromMilliseconds(fracStart * totalMs);
            TimeSpan tEnd = start + TimeSpan.FromMilliseconds(fracEnd * totalMs);

            tokens.Add(new CodeToken
            {
                Text = tokenText,
                Type = type,
                KeywordDoodle = tokenDoodle,
                StartTime = tStart,
                EndTime = tEnd
            });
        }

        // If there is leftover trailing whitespace, attach to the last token or add as punctuation
        if (!string.IsNullOrEmpty(pendingWhitespace) && tokens.Count > 0)
        {
            var last = tokens[^1];
            tokens[^1] = new CodeToken
            {
                Text = last.Text + pendingWhitespace,
                Type = last.Type,
                KeywordDoodle = last.KeywordDoodle,
                StartTime = last.StartTime,
                EndTime = last.EndTime
            };
        }

        return tokens;
    }

    private static string? DetectDoodle(string text)
    {
        string lower = text.ToLowerInvariant();
        if (Regex.IsMatch(lower, @"\b(rule|king|queen|crown|royal|reign)\b"))
            return "crown";
        if (Regex.IsMatch(lower, @"\b(mirror|reflect|reflection|shadow|ghost)\b"))
            return "mirror";
        if (Regex.IsMatch(lower, @"\b(never|lie|lies|broken|fake|wrong|lost)\b"))
            return "strikethrough";
        if (Regex.IsMatch(lower, @"\b(sweep|wave|waves|sea|ocean|rise|burn|fire)\b"))
            return "wave";
        if (Regex.IsMatch(lower, @"\b(sleep|sleeping|dream|dreams|night|bed)\b"))
            return "sleep";
        return null;
    }

    private static bool MatchesDoodleKeyword(string word, string doodle)
    {
        string w = word.ToLowerInvariant();
        return doodle switch
        {
            "crown" => w is "rule" or "king" or "queen" or "crown" or "royal" or "reign",
            "mirror" => w is "mirror" or "reflect" or "reflection" or "shadow" or "ghost",
            "strikethrough" => w is "never" or "lie" or "lies" or "broken" or "fake" or "wrong" or "lost",
            "wave" => w is "sweep" or "wave" or "waves" or "sea" or "ocean" or "rise" or "burn" or "fire",
            "sleep" => w is "sleep" or "sleeping" or "dream" or "dreams" or "night" or "bed",
            _ => false
        };
    }

    private static bool HasNonAscii(string text)
    {
        foreach (char c in text)
        {
            if (c > 127) return true;
        }
        return false;
    }

    private static string CleanForVerbCheck(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return string.Empty;
        var sb = new StringBuilder();
        foreach (char c in word)
        {
            if (char.IsLetter(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
