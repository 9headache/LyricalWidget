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
    // Editorial Windows Fonts
    private static readonly FontFamily SerifFont = new("Georgia, Palatino Linotype, Times New Roman, serif");
    private static readonly FontFamily SansLightFont = new("Segoe UI, Arial, sans-serif");
    private static readonly FontFamily SansBoldFont = new("Segoe UI Variable Display, Bahnschrift, Segoe UI, sans-serif");
    private static readonly FontFamily CondensedFont = new("Bahnschrift Condensed, Impact, Arial Black, sans-serif");


    private static readonly Dictionary<string, Geometry> KeywordIcons = InitializeKeywordIcons();

    private static readonly Dictionary<string, string> WordToIconKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["heart"] = "heart", ["hearts"] = "heart", ["love"] = "heart", ["loves"] = "heart", ["loved"] = "heart", ["loving"] = "heart",
        ["fire"] = "fire", ["burn"] = "fire", ["burning"] = "fire", ["burns"] = "fire", ["flame"] = "fire", ["flames"] = "fire", ["blaze"] = "fire",
        ["time"] = "time", ["clock"] = "time", ["wait"] = "time", ["waiting"] = "time", ["hours"] = "time", ["minutes"] = "time", ["forever"] = "time",
        ["night"] = "moon", ["nights"] = "moon", ["moon"] = "moon", ["dark"] = "moon", ["darkness"] = "moon", ["midnight"] = "moon",
        ["star"] = "star", ["stars"] = "star", ["shine"] = "star", ["shining"] = "star", ["sky"] = "star", ["skies"] = "star", ["light"] = "star", ["glow"] = "star",
        ["rain"] = "rain", ["raining"] = "rain", ["tears"] = "rain", ["tear"] = "rain", ["cry"] = "rain", ["crying"] = "rain", ["drown"] = "rain",
        ["crown"] = "crown", ["king"] = "crown", ["queen"] = "crown", ["royalty"] = "crown", ["reign"] = "crown",
        ["eyes"] = "eye", ["eye"] = "eye", ["see"] = "eye", ["seeing"] = "eye", ["look"] = "eye", ["looking"] = "eye", ["blind"] = "eye",
        ["angel"] = "angel", ["angels"] = "angel", ["wings"] = "angel", ["wing"] = "angel", ["heaven"] = "angel", ["fly"] = "angel",
        ["bolt"] = "bolt", ["spark"] = "bolt", ["sparks"] = "bolt", ["flash"] = "bolt", ["thunder"] = "bolt", ["lightning"] = "bolt", ["electric"] = "bolt",
        ["music"] = "music", ["song"] = "music", ["songs"] = "music", ["beat"] = "music", ["beats"] = "music", ["melody"] = "music", ["rhythm"] = "music",
        ["diamond"] = "diamond", ["diamonds"] = "diamond", ["ice"] = "diamond", ["gold"] = "diamond", ["golden"] = "diamond", ["cold"] = "diamond"
    };

    private static Dictionary<string, Geometry> InitializeKeywordIcons()
    {
        var icons = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);

        AddIcon(icons, "heart", "M 12,21.35 C 11.5,20.9 2,12.3 2,6.5 C 2,3.5 4.5,1 7.5,1 C 9.2,1 10.9,1.8 12,3.1 C 13.1,1.8 14.8,1 16.5,1 C 19.5,1 22,3.5 22,6.5 C 22,12.3 12.5,20.9 12,21.35 Z");
        AddIcon(icons, "fire", "M 12,2 C 10,6 7,8 7,13 C 7,16.5 9.5,19.5 12,21 C 14.5,19.5 17,16.5 17,13 C 17,9 14,7 13.5,4 C 13,8 11,10 11,12 C 11,13.5 12,14.5 12,15 C 11,14.5 10,13.5 10,12 C 10,9 12,5 12,2 Z");
        AddIcon(icons, "time", "M 12,2 A 10,10 0 1,0 12,22 A 10,10 0 1,0 12,2 Z M 12,6 L 12,12 L 16,14");
        AddIcon(icons, "moon", "M 12,2 C 6.5,2 2,6.5 2,12 C 2,17.5 6.5,22 12,22 C 14.5,22 16.8,21 18.5,19.5 C 13.5,19 9.5,15 9.5,10 C 9.5,6.5 11.5,3.5 14.5,2.2 C 13.7,2.1 12.8,2 12,2 Z");
        AddIcon(icons, "star", "M 12,2 C 12,7.5 16.5,12 22,12 C 16.5,12 12,16.5 12,22 C 12,16.5 7.5,12 2,12 C 7.5,12 12,7.5 12,2 Z");
        AddIcon(icons, "rain", "M 12,2 C 12,2 4,12 4,16 C 4,20 7.5,22 12,22 C 16.5,22 20,20 20,16 C 20,12 12,2 12,2 Z");
        AddIcon(icons, "crown", "M 3,19 L 21,19 M 4,15 L 20,15 L 22,7 L 16,11 L 12,4 L 8,11 L 2,7 Z");
        AddIcon(icons, "eye", "M 2,12 C 5,6 19,6 22,12 C 19,18 5,18 2,12 Z M 12,9 A 3,3 0 1,0 12,15 A 3,3 0 1,0 12,9 Z");
        AddIcon(icons, "angel", "M 2,11 C 6,5 11,8 12,12 C 13,8 18,5 22,11 C 18,17 14,14 12,13 C 10,14 6,17 2,11 Z");
        AddIcon(icons, "bolt", "M 13,2 L 5,13 L 11,13 L 9,22 L 19,10 L 13,10 Z");
        AddIcon(icons, "music", "M 12,3 L 19,3 L 19,13 C 18.2,12.4 17.1,12 16,12 C 13.8,12 12,13.8 12,16 C 12,18.2 13.8,20 16,20 C 18.2,20 20,18.2 20,16 L 20,7 L 12,7 L 12,15 C 11.2,14.4 10.1,14 9,14 C 6.8,14 5,15.8 5,18 C 5,20.2 6.8,22 9,22 C 11.2,22 13,20.2 13,18 L 13,3 Z");
        AddIcon(icons, "diamond", "M 6,3 L 18,3 L 22,9 L 12,21 L 2,9 Z M 2,9 L 22,9 M 6,3 L 12,21 L 18,3");

        return icons;
    }

    private static void AddIcon(Dictionary<string, Geometry> dict, string key, string pathData)
    {
        var geom = Geometry.Parse(pathData);
        geom.Freeze();
        dict[key] = geom;
    }


    // Editorial layout builder: serif/sans typography, quotes, and keyword icons
    private KineticPhraseVisual BuildEditorialPhrase(
        LyricLine line,
        int lineIndex,
        IReadOnlyList<LyricLine> allLines,
        VibePalette palette,
        string vibeSetting,
        double bassEnergy,
        double beatImpulse,
        Size viewportSize)
    {
        _phraseCounter++;

        double vpW = viewportSize.Width > 200 ? viewportSize.Width : 1920;
        double vpH = viewportSize.Height > 200 ? viewportSize.Height : 1080;

        TimeSpan lineDuration = TimeSpan.FromSeconds(3.5);
        if (lineIndex + 1 < allLines.Count)
        {
            var diff = allLines[lineIndex + 1].Timestamp - line.Timestamp;
            if (diff > TimeSpan.FromMilliseconds(400) && diff < TimeSpan.FromSeconds(10))
            {
                lineDuration = diff;
            }
        }
        else if (line.Duration > TimeSpan.Zero)
        {
            lineDuration = line.Duration;
        }

        var rawWords = ExtractWordTimings(line, lineDuration);
        if (rawWords.Count == 0)
        {
            rawWords.Add((line.Text, line.Timestamp, line.Timestamp + lineDuration));
        }

        double wps = rawWords.Count / Math.Max(0.4, lineDuration.TotalSeconds);

        var template = ChooseTemplate(wps, vibeSetting);
        _lastTemplate = template;

        _currentAnchor = ComputeNextAnchor(_currentAnchor, _phraseCounter, vpW, vpH);

        SolidColorBrush accentBrush;
        SolidColorBrush primaryBrush;
        if (palette.IsMonochrome)
        {
            accentBrush = Brushes.White;
            primaryBrush = new SolidColorBrush(Color.FromRgb(220, 224, 230));
        }
        else
        {
            accentBrush = new SolidColorBrush(palette.Accent);
            primaryBrush = new SolidColorBrush(Color.FromRgb(245, 247, 252));
        }
        if (accentBrush.CanFreeze) accentBrush.Freeze();
        if (primaryBrush.CanFreeze) primaryBrush.Freeze();

        KineticPhraseVisual visual = template switch
        {
            KineticTemplateType.EditorialCalm => BuildEditorialCalm(line, lineIndex, rawWords, palette, accentBrush, primaryBrush, wps),
            KineticTemplateType.RapHighEnergy => BuildRapHighEnergy(line, lineIndex, rawWords, palette, accentBrush, primaryBrush, wps),
            _ => BuildPopRhythmic(line, lineIndex, rawWords, palette, accentBrush, primaryBrush, wps)
        };

        visual.TemplateType = template;
        visual.LineIndex = lineIndex;
        visual.LineStartTime = line.Timestamp;
        visual.LineEndTime = line.Timestamp + lineDuration;
        visual.WorldAnchor = _currentAnchor;
        visual.IsPosterMode = false;

        var containerScale = new ScaleTransform(1.0, 1.0);
        visual.ContainerScale = containerScale;
        visual.Container.RenderTransformOrigin = new Point(0.5, 0.5);
        visual.Container.RenderTransform = containerScale;

        double maxBlockWidth = Math.Max(320.0, vpW * 0.78);
        visual.Container.Measure(new Size(maxBlockWidth, double.PositiveInfinity));
        var desired = visual.Container.DesiredSize;
        visual.Container.Arrange(new Rect(new Point(0, 0), desired));
        visual.VisualSize = desired;

        double safeWidth = vpW * 0.82;
        double safeHeight = vpH * 0.68;
        double zoomX = safeWidth / Math.Max(1.0, desired.Width);
        double zoomY = safeHeight / Math.Max(1.0, desired.Height);
        double targetZoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.55, 1.45);
        visual.BaseZoom = targetZoom;

        for (int i = 0; i < visual.Words.Count; i++)
        {
            var w = visual.Words[i];
            try
            {
                double ew = w.Element.RenderSize.Width > 0 ? w.Element.RenderSize.Width : w.Element.DesiredSize.Width;
                double eh = w.Element.RenderSize.Height > 0 ? w.Element.RenderSize.Height : w.Element.DesiredSize.Height;
                var rel = w.Element.TransformToAncestor(visual.Container).Transform(new Point(ew * 0.5, eh * 0.5));
                w.CenterOffset = rel;
            }
            catch
            {
                w.CenterOffset = new Point(desired.Width * 0.5, desired.Height * 0.5);
            }
        }

        return visual;
    }

    // Poster layout builder: 3-color graphic brutalist layouts with 90° whip-turns

    private KineticPhraseVisual BuildEditorialCalm(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        VibePalette palette, Brush accentBrush, Brush primaryBrush, double wps)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);

        int iconWordIdx = -1;
        Geometry? matchedIconGeom = null;
        if (wps < 3.2 && lineIndex != _lastIconLineIndex + 1)
        {
            for (int i = 0; i < words.Count; i++)
            {
                string clean = CleanWord(words[i].text);
                if (WordToIconKey.TryGetValue(clean, out var iconKey) && KeywordIcons.TryGetValue(iconKey, out var geom))
                {
                    iconWordIdx = i;
                    matchedIconGeom = geom;
                    _lastIconLineIndex = lineIndex;
                    break;
                }
            }
        }

        double baseFontSize = 52.0;

        bool hasQuotes = (words.Count >= 4 || lineIndex % 3 != 1);

        if (hasQuotes)
        {
            var headerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var openQuote = new TextBlock
            {
                Text = "“",
                FontFamily = SerifFont,
                FontSize = baseFontSize * 2.6,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush,
                Opacity = 0.16,
                Margin = new Thickness(0, 0, 0, -baseFontSize * 1.1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            headerStack.Children.Add(openQuote);

            var rule = new Border
            {
                Height = 1.5,
                Width = 56.0,
                Background = accentBrush,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 0, 6)
            };
            headerStack.Children.Add(rule);

            mainStack.Children.Add(headerStack);
        }

        int numRows = words.Count <= 4 ? 1 : (words.Count <= 8 ? 2 : 3);
        int perRow = (int)Math.Ceiling(words.Count / (double)numRows);

        bool isShortDramatic = words.Count <= 3;
        Thickness wordMargin = isShortDramatic ? new Thickness(10, 0, 10, 0) : new Thickness(5, 0, 5, 0);

        for (int r = 0; r < numRows; r++)
        {
            var rowIndices = Enumerable.Range(r * perRow, perRow).Where(i => i < words.Count).ToList();
            if (rowIndices.Count == 0) continue;

            var rowStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };

            foreach (int i in rowIndices)
            {
                var w = words[i];
                string clean = CleanWord(w.text);
                bool isHero = heroIndices.Contains(i);
                bool isConnector = ConnectorWords.Contains(clean);

                FontFamily font = isConnector ? SansLightFont : SerifFont;
                FontStyle style = isHero ? FontStyles.Italic : FontStyles.Normal;
                FontWeight weight = isHero ? FontWeights.SemiBold : (isConnector ? FontWeights.Light : FontWeights.Normal);
                double fontSize = isHero ? (baseFontSize * 1.25) : (isConnector ? (baseFontSize * 0.90) : baseFontSize);
                Brush brush = isHero ? accentBrush : primaryBrush;
                double targetOpacity = isConnector ? 0.85 : 1.0;

                string display = isConnector ? (w.text.ToLowerInvariant() + " ") : (w.text + " ");

                var tb = CreateWordElement(display, font, fontSize, weight, style, brush, wordMargin, out var st, out var tt);
                rowStack.Children.Add(tb);

                var wordItem = new KineticWordItem
                {
                    Text = w.text,
                    StartTime = w.start,
                    EndTime = w.end,
                    Element = tb,
                    Scale = st,
                    Translate = tt,
                    TargetOpacity = targetOpacity
                };

                if (i == iconWordIdx && matchedIconGeom != null)
                {
                    var iconPath = CreateIconElement(matchedIconGeom, accentBrush, fontSize * 0.85, out var iconScale);
                    rowStack.Children.Add(iconPath);
                    wordItem.AttachedIcon = iconPath;
                    wordItem.IconScale = iconScale;
                }

                visual.Words.Add(wordItem);
            }

            mainStack.Children.Add(rowStack);
        }

        if (hasQuotes)
        {
            var closeQuote = new TextBlock
            {
                Text = "”",
                FontFamily = SerifFont,
                FontSize = baseFontSize * 2.6,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush,
                Opacity = 0.16,
                Margin = new Thickness(0, -baseFontSize * 1.0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            mainStack.Children.Add(closeQuote);
        }

        visual.Container = mainStack;
        visual.TargetCameraAngle = (_phraseCounter % 2 == 0) ? 1.5 : -1.5;
        return visual;
    }

    private KineticPhraseVisual BuildPopRhythmic(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        VibePalette palette, Brush accentBrush, Brush primaryBrush, double wps)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);

        int iconWordIdx = -1;
        Geometry? matchedIconGeom = null;
        if (wps < 3.2 && lineIndex != _lastIconLineIndex + 1)
        {
            for (int i = 0; i < words.Count; i++)
            {
                string clean = CleanWord(words[i].text);
                if (WordToIconKey.TryGetValue(clean, out var iconKey) && KeywordIcons.TryGetValue(iconKey, out var geom))
                {
                    iconWordIdx = i;
                    matchedIconGeom = geom;
                    _lastIconLineIndex = lineIndex;
                    break;
                }
            }
        }

        double baseFontSize = 52.0;

        int alignPattern = _phraseCounter % 3;
        var rowAlignment = alignPattern switch
        {
            1 => HorizontalAlignment.Left,
            2 => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        Thickness rowMargin = alignPattern switch
        {
            1 => new Thickness(24, 4, 0, 4),
            2 => new Thickness(0, 4, 24, 4),
            _ => new Thickness(0, 4, 0, 4)
        };

        int numRows = words.Count <= 4 ? 1 : (words.Count <= 7 ? 2 : 3);
        int perRow = (int)Math.Ceiling(words.Count / (double)numRows);

        for (int r = 0; r < numRows; r++)
        {
            var rowIndices = Enumerable.Range(r * perRow, perRow).Where(i => i < words.Count).ToList();
            if (rowIndices.Count == 0) continue;

            var rowStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = rowAlignment,
                Margin = rowMargin
            };

            foreach (int i in rowIndices)
            {
                var w = words[i];
                string clean = CleanWord(w.text);
                bool isHero = heroIndices.Contains(i);
                bool isConnector = ConnectorWords.Contains(clean);

                FontFamily font = isConnector ? SansLightFont : SansBoldFont;
                FontWeight weight = isHero ? FontWeights.ExtraBold : (isConnector ? FontWeights.Light : FontWeights.SemiBold);
                double fontSize = isHero ? (baseFontSize * 1.28) : (isConnector ? (baseFontSize * 0.82) : baseFontSize);
                Brush brush = isHero ? accentBrush : primaryBrush;
                double targetOpacity = isConnector ? 0.72 : 1.0;
                double baselineShift = isHero ? -5.0 : (isConnector ? 3.0 : 0.0);

                string display = isHero ? (w.text.ToUpperInvariant() + " ") : (isConnector ? (w.text.ToLowerInvariant() + " ") : (w.text + " "));

                var tb = CreateWordElement(display, font, fontSize, weight, FontStyles.Normal, brush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                rowStack.Children.Add(tb);

                var wordItem = new KineticWordItem
                {
                    Text = w.text,
                    StartTime = w.start,
                    EndTime = w.end,
                    Element = tb,
                    Scale = st,
                    Translate = tt,
                    BaselineShift = baselineShift,
                    TargetOpacity = targetOpacity
                };

                if (i == iconWordIdx && matchedIconGeom != null)
                {
                    var iconPath = CreateIconElement(matchedIconGeom, accentBrush, fontSize * 0.85, out var iconScale);
                    rowStack.Children.Add(iconPath);
                    wordItem.AttachedIcon = iconPath;
                    wordItem.IconScale = iconScale;
                }

                visual.Words.Add(wordItem);
            }

            mainStack.Children.Add(rowStack);
        }

        visual.Container = mainStack;
        visual.TargetCameraAngle = (_phraseCounter % 2 == 0) ? -2.2 : 2.2;
        return visual;
    }

    private KineticPhraseVisual BuildRapHighEnergy(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        VibePalette palette, Brush accentBrush, Brush primaryBrush, double wps)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = 52.0;

        int numRows = words.Count <= 4 ? 1 : (words.Count <= 8 ? 2 : 3);
        int perRow = (int)Math.Ceiling(words.Count / (double)numRows);

        for (int r = 0; r < numRows; r++)
        {
            var rowIndices = Enumerable.Range(r * perRow, perRow).Where(i => i < words.Count).ToList();
            if (rowIndices.Count == 0) continue;

            var rowStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };

            foreach (int i in rowIndices)
            {
                var w = words[i];
                string clean = CleanWord(w.text);
                bool isHero = heroIndices.Contains(i);
                bool isConnector = ConnectorWords.Contains(clean);

                FontWeight weight = isHero ? FontWeights.Black : (isConnector ? FontWeights.Normal : FontWeights.Bold);
                double fontSize = isHero ? (baseFontSize * 1.45) : (isConnector ? (baseFontSize * 0.90) : baseFontSize);
                Brush brush = isHero ? accentBrush : primaryBrush;
                double targetOpacity = isConnector ? 0.80 : 1.0;

                string display = w.text.ToUpperInvariant() + " ";

                var tb = CreateWordElement(display, CondensedFont, fontSize, weight, FontStyles.Normal, brush, new Thickness(3, 0, 3, 0), out var st, out var tt);
                rowStack.Children.Add(tb);

                var wordItem = new KineticWordItem
                {
                    Text = w.text,
                    StartTime = w.start,
                    EndTime = w.end,
                    Element = tb,
                    Scale = st,
                    Translate = tt,
                    TargetOpacity = targetOpacity
                };

                visual.Words.Add(wordItem);
            }

            mainStack.Children.Add(rowStack);
        }

        visual.Container = mainStack;
        visual.TargetCameraAngle = (_phraseCounter % 2 == 0) ? -2.8 : 2.8;
        return visual;
    }

    private static Path CreateIconElement(Geometry geometry, Brush strokeBrush, double size, out ScaleTransform scaleTransform)
    {
        scaleTransform = new ScaleTransform(0.82, 0.82);
        return new Path
        {
            Data = geometry,
            Stroke = strokeBrush,
            StrokeThickness = 2.0,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
            Opacity = 0.0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scaleTransform
        };
    }

    private static TextBlock CreateWordElement(
        string text, FontFamily font, double fontSize, FontWeight weight, FontStyle style, Brush brush,
        Thickness margin,
        out ScaleTransform scaleTransform, out TranslateTransform translateTransform)
    {
        scaleTransform = new ScaleTransform(0.92, 0.92);
        translateTransform = new TranslateTransform(0.0, 16.0);

        var group = new TransformGroup();
        group.Children.Add(scaleTransform);
        group.Children.Add(translateTransform);

        return new TextBlock
        {
            Text = text,
            FontFamily = font,
            FontSize = fontSize,
            FontWeight = weight,
            FontStyle = style,
            Foreground = brush,
            Margin = margin,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = group,
            Opacity = 0.0
        };
    }
}
