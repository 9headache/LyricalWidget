using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LyricalWidget.Media;

namespace LyricalWidget.Lyrics;

internal sealed class PosterTransitionInfo
{
    public Color NextBackdropColor { get; set; }
    public bool IsPushDownSwap { get; set; }
    public TranslateTransform? PushTranslate { get; set; }
    public double PushProgress { get; set; } = 0.0;
    public bool PortalLocked { get; set; } = false;
    public TextBlock? PortalHeroElement { get; set; }
    public double OpenLetterRelX { get; set; } = 0.5;
}

public sealed partial class KineticLayoutDirector
{
    private static readonly FontFamily PosterHeavyFont = new("Arial Black, Impact, Segoe UI Black, sans-serif");
    private static readonly FontFamily PosterImpactFont = new("Impact, Arial Black, Segoe UI Black, sans-serif");
    private static readonly FontFamily PosterSansBlackFont = new("Segoe UI Black, Arial Black, Impact, sans-serif");

    private static readonly HashSet<string> SpatialLowKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "low", "down", "floor", "under", "bottom", "deep", "sink", "fall"
    };

    private static readonly HashSet<string> SpatialHighKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "high", "up", "sky", "top", "above", "rise", "fly", "crown"
    };

    private static readonly HashSet<string> RibbonKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "line", "lines", "bar", "bars", "cut", "stripe", "edge", "rule"
    };

    private static readonly HashSet<string> WhipTurnKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "turn", "spin", "flip", "twist", "switch", "roll", "round", "back"
    };

    private static readonly HashSet<string> SlamKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "drop", "stop", "wait", "hit", "boom", "break", "crash", "freeze"
    };

    private static readonly HashSet<string> SplitWedgeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "split", "half", "side", "between", "slice", "sharp", "through"
    };

    private static readonly char[] WordTrimChars = new[] { ' ', ',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '-', '—' };

    private string? _lastSemanticTrigger = null;
    private double _lastWhipAngle = 0.0;
    private int _lastRotatingLayout = -1;

    public bool IsPosterDimMode { get; set; } = true;

    private KineticPhraseVisual BuildPosterPhrase(
        LyricLine line,
        int lineIndex,
        IReadOnlyList<LyricLine> allLines,
        VibePalette palette,
        string vibeSetting,
        double bassEnergy,
        double beatImpulse,
        Size viewportSize)
    {
        _posterPhraseCounter++;
        _phraseCounter++;

        if (_posterPhraseCounter <= 1)
        {
            _lastSemanticTrigger = null;
            _lastWhipAngle = 0.0;
            _lastRotatingLayout = -1;
        }

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

        var (backdropDeep, backdropMid, backdropAccent, inkLight, heroColor) = GetPosterPaletteColors(palette);
        var (backdropColor, primaryBrush, heroBrush, ribbonBg, ribbonFg) = GetPosterTheme(
            backdropDeep, backdropMid, backdropAccent, inkLight, heroColor, _posterPhraseCounter);
        var (nextBackdropColor, _, _, _, _) = GetPosterTheme(
            backdropDeep, backdropMid, backdropAccent, inkLight, heroColor, _posterPhraseCounter + 1);

        string? semanticTrigger = null;
        int matchedWordIdx = -1;

        if (line.Text.Contains('?') || line.Text.Contains('!'))
        {
            semanticTrigger = "Punctuation";
        }
        else
        {
            for (int i = 0; i < rawWords.Count; i++)
            {
                string clean = rawWords[i].text.Trim(WordTrimChars);
                if (SpatialLowKeywords.Contains(clean)) { semanticTrigger = "SpatialLow"; matchedWordIdx = i; break; }
                if (SpatialHighKeywords.Contains(clean)) { semanticTrigger = "SpatialHigh"; matchedWordIdx = i; break; }
                if (RibbonKeywords.Contains(clean)) { semanticTrigger = "Ribbon"; matchedWordIdx = i; break; }
                if (WhipTurnKeywords.Contains(clean)) { semanticTrigger = "WhipTurn"; matchedWordIdx = i; break; }
                if (SlamKeywords.Contains(clean)) { semanticTrigger = "Slam"; matchedWordIdx = i; break; }
                if (SplitWedgeKeywords.Contains(clean)) { semanticTrigger = "SplitWedge"; matchedWordIdx = i; break; }
            }
        }

        if (semanticTrigger != null && semanticTrigger == _lastSemanticTrigger)
        {
            semanticTrigger = null;
        }
        _lastSemanticTrigger = semanticTrigger;

        double targetAngle;
        if (semanticTrigger == "WhipTurn")
        {
            targetAngle = _lastWhipAngle > 0.0 ? -90.0 : 90.0;
            _lastWhipAngle = targetAngle;
        }
        else
        {
            int anglePattern = (_posterPhraseCounter / 2) % 4;
            targetAngle = anglePattern switch
            {
                1 => 90.0,
                2 => -90.0,
                _ => 0.0
            };
        }

        _currentAnchor = ComputeNextAnchor(_currentAnchor, _phraseCounter, vpW, vpH);

        var info = new PosterTransitionInfo { NextBackdropColor = nextBackdropColor };
        bool isPushDownSwap = false;
        KineticPhraseVisual visual;

        if (semanticTrigger == "Punctuation")
        {
            visual = BuildPosterGiantPunctuation(line, lineIndex, rawWords, primaryBrush, heroBrush);
            _lastPosterLayout = 9;
        }
        else if (semanticTrigger == "SpatialLow")
        {
            visual = BuildPosterSpatialLow(line, lineIndex, rawWords, primaryBrush, heroBrush, matchedWordIdx >= 0 ? matchedWordIdx : 0);
            _lastPosterLayout = 5;
        }
        else if (semanticTrigger == "SpatialHigh")
        {
            visual = BuildPosterSpatialHigh(line, lineIndex, rawWords, primaryBrush, heroBrush, matchedWordIdx >= 0 ? matchedWordIdx : 0);
            _lastPosterLayout = 6;
        }
        else if (semanticTrigger == "Ribbon")
        {
            visual = BuildPosterRotatingRibbon(line, lineIndex, rawWords, primaryBrush, heroBrush, ribbonBg, ribbonFg);
            _lastPosterLayout = 7;
        }
        else if (semanticTrigger == "Slam")
        {
            visual = BuildPosterBeatSlam(line, lineIndex, rawWords, primaryBrush, heroBrush);
            isPushDownSwap = true;
            _lastPosterLayout = 3;
        }
        else if (semanticTrigger == "SplitWedge")
        {
            if (_posterPhraseCounter % 2 == 0)
            {
                visual = BuildPosterHorizonSplit(line, lineIndex, rawWords, primaryBrush, heroBrush, ribbonBg, ribbonFg);
                _lastPosterLayout = 8;
            }
            else
            {
                visual = BuildPosterDiagonalWedgeWipe(line, lineIndex, rawWords, primaryBrush, heroBrush, nextBackdropColor);
                _lastPosterLayout = 11;
            }
        }
        else
        {
            bool triggerPortal = (_posterPhraseCounter % 5 == 0) || (rawWords.Count >= 1 && rawWords.Count <= 3 && _lastPosterLayout != 4);
            if (triggerPortal)
            {
                visual = BuildPosterCenterpieceZoomThrough(line, lineIndex, rawWords, primaryBrush, nextBackdropColor, out Point _, out var heroTb, out double relX);
                info.PortalHeroElement = heroTb;
                info.OpenLetterRelX = relX;
                _lastPosterLayout = 4;
            }
            else if (wps >= 3.6)
            {
                visual = BuildPosterBeatSlam(line, lineIndex, rawWords, primaryBrush, heroBrush);
                _lastPosterLayout = 3;
            }
            else
            {
                int layoutChoice = PickPosterDiverseLayout();
                visual = layoutChoice switch
                {
                    0 => BuildPosterInterlockingHeroGrid(line, lineIndex, rawWords, primaryBrush, heroBrush),
                    1 => BuildPosterInvertedRibbonBar(line, lineIndex, rawWords, primaryBrush, heroBrush, ribbonBg, ribbonFg),
                    2 => BuildPosterSplitScreenContrastCut(line, lineIndex, rawWords, primaryBrush, heroBrush, ribbonBg, ribbonFg),
                    3 => BuildPosterHorizonSplit(line, lineIndex, rawWords, primaryBrush, heroBrush, ribbonBg, ribbonFg),
                    4 => BuildPosterVerticalSideAnchor(line, lineIndex, rawWords, primaryBrush, heroBrush),
                    5 => BuildPosterRotatingRibbon(line, lineIndex, rawWords, primaryBrush, heroBrush, ribbonBg, ribbonFg),
                    _ => BuildPosterDiagonalWedgeWipe(line, lineIndex, rawWords, primaryBrush, heroBrush, nextBackdropColor)
                };
                _lastPosterLayout = layoutChoice;
            }

            if (rawWords.Count <= 3 && _posterPhraseCounter % 2 == 0)
            {
                isPushDownSwap = true;
            }
        }

        visual.TemplateType = KineticTemplateType.PopRhythmic;
        visual.LineIndex = lineIndex;
        visual.LineStartTime = line.Timestamp;
        visual.LineEndTime = line.Timestamp + lineDuration;
        visual.WorldAnchor = _currentAnchor;
        visual.TargetCameraAngle = targetAngle;
        visual.IsPosterMode = true;
        visual.BackdropColor = backdropColor;
        visual.TriggerZoomPunch = (_posterPhraseCounter % 3 == 0);

        info.IsPushDownSwap = isPushDownSwap;
        var containerScale = new ScaleTransform(1.0, 1.0);
        visual.ContainerScale = containerScale;
        var containerTranslate = new TranslateTransform(0.0, 0.0);
        info.PushTranslate = containerTranslate;

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(containerScale);
        transformGroup.Children.Add(containerTranslate);

        if (Math.Abs(targetAngle) > 1.0)
        {
            transformGroup.Children.Add(new RotateTransform(targetAngle));
        }
        visual.Container.RenderTransform = transformGroup;
        visual.Container.Tag = info;

        double maxBlockWidth = Math.Abs(targetAngle) > 45.0 ? Math.Max(320.0, vpH * 0.78) : Math.Max(320.0, vpW * 0.78);
        visual.Container.Measure(new Size(maxBlockWidth, double.PositiveInfinity));
        var desired = visual.Container.DesiredSize;
        visual.Container.Arrange(new Rect(new Point(0, 0), desired));
        visual.VisualSize = desired;

        if (visual.HasPortalLetter && info.PortalHeroElement != null)
        {
            try
            {
                double heroW = info.PortalHeroElement.RenderSize.Width > 0 ? info.PortalHeroElement.RenderSize.Width : info.PortalHeroElement.DesiredSize.Width;
                double heroH = info.PortalHeroElement.RenderSize.Height > 0 ? info.PortalHeroElement.RenderSize.Height : info.PortalHeroElement.DesiredSize.Height;
                Point charCenterLocal = new Point(heroW * info.OpenLetterRelX, heroH * 0.5);
                Point charCenterContainer = info.PortalHeroElement.TransformToAncestor(visual.Container).Transform(charCenterLocal);
                double pivotX = Math.Clamp(charCenterContainer.X / Math.Max(1.0, desired.Width), 0.05, 0.95);
                double pivotY = Math.Clamp(charCenterContainer.Y / Math.Max(1.0, desired.Height), 0.05, 0.95);
                visual.Container.RenderTransformOrigin = new Point(pivotX, pivotY);
            }
            catch
            {
                visual.Container.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }
        else
        {
            visual.Container.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        double safeWidth = vpW * 0.82;
        double safeHeight = vpH * 0.68;
        double effW = Math.Abs(targetAngle) > 45.0 ? desired.Height : desired.Width;
        double effH = Math.Abs(targetAngle) > 45.0 ? desired.Width : desired.Height;
        double zoomX = safeWidth / Math.Max(1.0, effW);
        double zoomY = safeHeight / Math.Max(1.0, effH);
        visual.BaseZoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.55, 1.45);

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

    private int PickPosterDiverseLayout()
    {
        int count = 7;
        int next = (_lastRotatingLayout + 1) % count;
        _lastRotatingLayout = next;
        return next;
    }

    private (Color backdropDeep, Color backdropMid, Color backdropAccent, Color inkLight, Color heroColor) GetPosterPaletteColors(
        VibePalette albumPalette)
    {
        double dimCap = IsPosterDimMode ? 0.70 : 1.0;
        var inkLight = Color.FromRgb(240, 240, 242);

        if (albumPalette.IsMonochrome)
        {
            var backdropDeep = Color.FromRgb(14, 15, 17);
            var backdropMid = Color.FromRgb(26, 28, 32);
            var backdropAccent = Color.FromRgb(44, 48, 55);
            var heroColor = inkLight;
            return (backdropDeep, backdropMid, backdropAccent, inkLight, heroColor);
        }
        else
        {
            var (h, s, l) = ColorExtractor.RgbToHsl(albumPalette.Accent);

            double lDeep = Math.Clamp(0.095 * (IsPosterDimMode ? 0.88 : 1.0), 0.08, 0.11);
            double sDeep = Math.Clamp(s * 0.40, 0.12, 0.35);
            var backdropDeep = ColorExtractor.HslToRgb(h, sDeep, lDeep);

            double lMid = Math.Clamp(0.185 * dimCap, 0.14, 0.21);
            double sMid = Math.Clamp(s * 0.55, 0.18, 0.48);
            var backdropMid = ColorExtractor.HslToRgb(h, sMid, lMid);

            double lAccent = Math.Clamp((IsPosterDimMode ? 0.25 : 0.31) * dimCap, 0.18, 0.34);
            double sAccent = Math.Clamp(s * 0.70, 0.22, 0.60);
            var backdropAccent = ColorExtractor.HslToRgb(h, sAccent, lAccent);

            var heroColor = ColorExtractor.HslToRgb(h, Math.Max(s, 0.80), 0.65);

            return (backdropDeep, backdropMid, backdropAccent, inkLight, heroColor);
        }
    }

    private static (Color backdrop, Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg) GetPosterTheme(
        Color backdropDeep, Color backdropMid, Color backdropAccent, Color inkLight, Color heroColor, int posterPhraseCounter)
    {
        int backdropCycle = (posterPhraseCounter / 3) % 3;
        Color backdrop;
        Color primary;
        Color hero;
        Color ribbonBg;
        Color ribbonFg;

        switch (backdropCycle)
        {
            case 1:
                backdrop = backdropMid;
                primary = inkLight;
                hero = heroColor;
                ribbonBg = backdropAccent;
                ribbonFg = inkLight;
                break;

            case 2:
                backdrop = backdropAccent;
                primary = inkLight;
                hero = Color.FromRgb(255, 255, 255);
                ribbonBg = backdropDeep;
                ribbonFg = inkLight;
                break;

            default:
                backdrop = backdropDeep;
                primary = inkLight;
                hero = heroColor;
                ribbonBg = backdropAccent;
                ribbonFg = inkLight;
                break;
        }

        var pb = new SolidColorBrush(primary);
        var hb = new SolidColorBrush(hero);
        var rbg = new SolidColorBrush(ribbonBg);
        var rfg = new SolidColorBrush(ribbonFg);

        if (pb.CanFreeze) pb.Freeze();
        if (hb.CanFreeze) hb.Freeze();
        if (rbg.CanFreeze) rbg.Freeze();
        if (rfg.CanFreeze) rfg.Freeze();

        return (backdrop, pb, hb, rbg, rfg);
    }

    private KineticPhraseVisual BuildPosterCenterpieceZoomThrough(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Color nextBackdropColor,
        out Point pivotOrigin, out TextBlock heroTb, out double openLetterRelX)
    {
        var visual = new KineticPhraseVisual();
        var mainPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        int heroIdx = 0;
        int maxLen = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].text.Length > maxLen)
            {
                maxLen = words[i].text.Length;
                heroIdx = i;
            }
        }

        var heroWord = words[heroIdx];
        string upperHero = heroWord.text.ToUpperInvariant().Trim(WordTrimChars);
        if (string.IsNullOrEmpty(upperHero)) upperHero = heroWord.text.ToUpperInvariant().Trim();

        char[] openLetters = new[] { 'O', 'A', 'D', 'P', 'R', 'H', 'M', 'N', 'B', 'Q' };
        int openLetterIdx = -1;
        for (int c = 0; c < upperHero.Length; c++)
        {
            if (Array.IndexOf(openLetters, upperHero[c]) >= 0)
            {
                openLetterIdx = c;
                break;
            }
        }

        if (openLetterIdx >= 0)
        {
            openLetterRelX = (openLetterIdx + 0.5) / Math.Max(1.0, upperHero.Length);
            pivotOrigin = new Point(Math.Clamp(openLetterRelX, 0.15, 0.85), 0.5);
        }
        else
        {
            openLetterRelX = 0.5;
            pivotOrigin = new Point(0.5, 0.5);
        }

        double heroFontSize = Math.Clamp(110.0 - (upperHero.Length * 3.5), 58.0, 115.0);

        if (words.Count > 1)
        {
            var setupRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            for (int i = 0; i < words.Count; i++)
            {
                if (i == heroIdx) continue;
                var w = words[i];
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, 32.0, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                setupRow.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            mainPanel.Children.Add(setupRow);
        }

        var nextBackdropBrush = new SolidColorBrush(nextBackdropColor);
        if (nextBackdropBrush.CanFreeze) nextBackdropBrush.Freeze();

        heroTb = CreatePosterWordElement(upperHero, PosterHeavyFont, heroFontSize, FontWeights.Black, nextBackdropBrush, new Thickness(4, 0, 4, 0), out var heroSt, out var heroTt);
        heroTb.HorizontalAlignment = HorizontalAlignment.Center;
        mainPanel.Children.Add(heroTb);
        visual.Words.Add(new KineticWordItem { Text = heroWord.text, StartTime = heroWord.start, EndTime = heroWord.end, Element = heroTb, Scale = heroSt, Translate = heroTt });

        visual.Container = mainPanel;
        visual.HasPortalLetter = true;
        return visual;
    }

    private KineticPhraseVisual BuildPosterSpatialLow(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush,
        int matchedWordIndex)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double baseFontSize = 48.0;

        var setupStack = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            MaxWidth = 720
        };
        Grid.SetRow(setupStack, 0);

        var lowContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(lowContainer, 1);

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i == matchedWordIndex)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant(), PosterHeavyFont, baseFontSize * 1.6, FontWeights.Black, heroBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                tb.VerticalAlignment = VerticalAlignment.Bottom;
                lowContainer.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, baseFontSize * 0.85, FontWeights.Bold, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                setupStack.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
        }

        if (setupStack.Children.Count > 0) mainGrid.Children.Add(setupStack);
        mainGrid.Children.Add(lowContainer);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterSpatialHigh(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush,
        int matchedWordIndex)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double baseFontSize = 48.0;

        var highContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 18)
        };
        Grid.SetRow(highContainer, 0);

        var setupStack = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720
        };
        Grid.SetRow(setupStack, 1);

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i == matchedWordIndex)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant(), PosterHeavyFont, baseFontSize * 1.6, FontWeights.Black, heroBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                highContainer.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, baseFontSize * 0.85, FontWeights.Bold, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                setupStack.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
        }

        mainGrid.Children.Add(highContainer);
        if (setupStack.Children.Count > 0) mainGrid.Children.Add(setupStack);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterHorizonSplit(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double baseFontSize = 50.0;
        int mid = Math.Max(1, (words.Count + 1) / 2);

        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2)
        };
        Grid.SetRow(topRow, 0);

        for (int i = 0; i < mid; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, primaryBrush, new Thickness(3, 0, 3, 0), out var st, out var tt);
            tb.VerticalAlignment = VerticalAlignment.Bottom;
            topRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        mainGrid.Children.Add(topRow);

        var bottomContainer = new Border
        {
            Background = ribbonBg,
            ClipToBounds = true,
            Padding = new Thickness(24, 6, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(bottomContainer, 1);

        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };

        for (int i = mid; i < words.Count; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, ribbonFg, new Thickness(3, 0, 3, 0), out var st, out var tt);
            tb.VerticalAlignment = VerticalAlignment.Top;
            bottomRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }

        bottomContainer.Child = bottomRow;
        mainGrid.Children.Add(bottomContainer);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterGiantPunctuation(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        double baseFontSize = 48.0;
        string punct = line.Text.Contains('?') ? "?" : "!";

        int numRows = words.Count <= 3 ? 1 : 2;
        int perRow = (int)Math.Ceiling(words.Count / (double)numRows);

        var textStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(textStack, 0);

        for (int r = 0; r < numRows; r++)
        {
            var rowWords = words.Skip(r * perRow).Take(perRow).ToList();
            if (rowWords.Count == 0) continue;

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            foreach (var w in rowWords)
            {
                string cleanText = w.text.TrimEnd('?', '!', ' ', ',', '.');
                if (string.IsNullOrWhiteSpace(cleanText)) cleanText = w.text;
                var tb = CreatePosterWordElement(cleanText.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                rowPanel.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            textStack.Children.Add(rowPanel);
        }
        mainGrid.Children.Add(textStack);

        var punctTb = new TextBlock
        {
            Text = punct,
            FontFamily = PosterImpactFont,
            FontSize = baseFontSize * 3.0,
            FontWeight = FontWeights.Black,
            Foreground = heroBrush,
            VerticalAlignment = VerticalAlignment.Center,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = baseFontSize * 2.8,
            Margin = new Thickness(4, -8, 0, -8)
        };
        Grid.SetColumn(punctTb, 1);
        mainGrid.Children.Add(punctTb);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterVerticalSideAnchor(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        double baseFontSize = 46.0;

        int heroIdx = 0;
        int maxLen = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].text.Length > maxLen)
            {
                maxLen = words[i].text.Length;
                heroIdx = i;
            }
        }

        var heroWord = words[heroIdx];
        var otherWords = words.Where((w, idx) => idx != heroIdx).ToList();

        bool anchorOnRight = (_posterPhraseCounter % 2 == 0);
        int colHorizontal = anchorOnRight ? 0 : 1;
        int colVertical = anchorOnRight ? 1 : 0;

        var horizStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = anchorOnRight ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(horizStack, colHorizontal);

        int numRows = otherWords.Count <= 2 ? 1 : 2;
        int perRow = (int)Math.Ceiling(otherWords.Count / (double)numRows);

        for (int r = 0; r < numRows; r++)
        {
            var rowWords = otherWords.Skip(r * perRow).Take(perRow).ToList();
            if (rowWords.Count == 0) continue;

            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var w in rowWords)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                rowPanel.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            horizStack.Children.Add(rowPanel);
        }
        mainGrid.Children.Add(horizStack);

        var vertTb = CreatePosterWordElement(heroWord.text.ToUpperInvariant(), PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, heroBrush, new Thickness(2), out var vertSt, out var vertTt);
        vertTb.LayoutTransform = new RotateTransform(anchorOnRight ? 90.0 : -90.0);
        vertTb.VerticalAlignment = VerticalAlignment.Center;
        vertTb.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(vertTb, colVertical);
        mainGrid.Children.Add(vertTb);
        visual.Words.Add(new KineticWordItem { Text = heroWord.text, StartTime = heroWord.start, EndTime = heroWord.end, Element = vertTb, Scale = vertSt, Translate = vertTt });

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterRotatingRibbon(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        double baseFontSize = 48.0;
        int leadCount = Math.Max(1, Math.Min(3, words.Count / 2));

        var leadWords = words.Take(leadCount).ToList();
        var tailWords = words.Skip(leadCount).ToList();

        var ribbonRotate = new RotateTransform(0.0);
        var ribbonBorder = new Border
        {
            Background = ribbonBg,
            Padding = new Thickness(16, 4, 16, 4),
            CornerRadius = new CornerRadius(0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = ribbonRotate,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(ribbonBorder, 0);

        var leadStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var w in leadWords)
        {
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, ribbonFg, new Thickness(2, 0, 2, 0), out var st, out var tt);
            leadStack.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        ribbonBorder.Child = leadStack;
        mainGrid.Children.Add(ribbonBorder);

        var slideTranslate = new TranslateTransform(-50.0, 0.0);
        var tailContainer = new Border
        {
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tailContainer, 1);

        var tailStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            RenderTransform = slideTranslate,
            Opacity = 0.0,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var w in tailWords)
        {
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
            tailStack.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        tailContainer.Child = tailStack;
        mainGrid.Children.Add(tailContainer);

        int transIdx = Math.Min(visual.Words.Count - 1, leadCount - 1);
        visual.TemplateCustomUpdate = (pos, dt, bass) =>
        {
            bool triggered = transIdx >= 0 && transIdx < visual.Words.Count && visual.Words[transIdx].Triggered && visual.Words[transIdx].Progress >= 0.7;
            double targetAngle = triggered ? 90.0 : 0.0;
            double targetSlide = triggered ? 0.0 : -50.0;
            double targetOpacity = triggered ? 1.0 : 0.0;

            ribbonRotate.Angle += (targetAngle - ribbonRotate.Angle) * Math.Clamp(14.0 * dt, 0.0, 1.0);
            slideTranslate.X += (targetSlide - slideTranslate.X) * Math.Clamp(14.0 * dt, 0.0, 1.0);
            tailStack.Opacity += (targetOpacity - tailStack.Opacity) * Math.Clamp(14.0 * dt, 0.0, 1.0);
        };

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterDiagonalWedgeWipe(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Color wedgeColor)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };

        double baseFontSize = 52.0;
        var textStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize, FontWeights.Black, primaryBrush, new Thickness(3, 0, 3, 0), out var st, out var tt);
            textStack.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        mainGrid.Children.Add(textStack);

        var wedgeTranslate = new TranslateTransform(-300.0, 0.0);
        var wedgeBrush = new SolidColorBrush(wedgeColor);
        if (wedgeBrush.CanFreeze) wedgeBrush.Freeze();

        var wedgePoly = new Polygon
        {
            Fill = wedgeBrush,
            Points = new PointCollection
            {
                new Point(0, 0),
                new Point(200, 0),
                new Point(100, 240),
                new Point(-100, 240)
            },
            RenderTransform = wedgeTranslate,
            IsHitTestVisible = false,
            Opacity = 0.95
        };
        mainGrid.Children.Add(wedgePoly);

        visual.TemplateCustomUpdate = (pos, dt, bass) =>
        {
            double elapsedMs = (pos - visual.LineStartTime).TotalMilliseconds;
            if (elapsedMs < 220.0 && elapsedMs >= 0.0)
            {
                double t = Math.Clamp(elapsedMs / 200.0, 0.0, 1.0);
                double eased = t * (2.0 - t);
                double totalWidth = visual.VisualSize.Width > 0 ? visual.VisualSize.Width + 400.0 : 1200.0;
                wedgeTranslate.X = -300.0 + (totalWidth * eased);
                wedgePoly.Opacity = 0.95 * (1.0 - (t * 0.5));
                wedgePoly.Visibility = Visibility.Visible;
            }
            else
            {
                wedgePoly.Visibility = Visibility.Collapsed;
            }
        };

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterInterlockingHeroGrid(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = 50.0;

        bool hasPunct = line.Text.TrimEnd().EndsWith("?") || line.Text.TrimEnd().EndsWith("!");
        string punct = line.Text.TrimEnd().EndsWith("?") ? "?" : "!";

        var setupCol = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };

        var heroCol = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            bool isHero = heroIndices.Contains(i);

            if (isHero)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.75, FontWeights.Black, heroBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                heroCol.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, baseFontSize * 0.65, FontWeights.Black, primaryBrush, new Thickness(0, 1, 0, 1), out var st, out var tt);
                setupCol.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
        }

        if (hasPunct)
        {
            var punctTb = new TextBlock
            {
                Text = punct,
                FontFamily = PosterImpactFont,
                FontSize = baseFontSize * 1.85,
                FontWeight = FontWeights.Black,
                Foreground = heroBrush,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            heroCol.Children.Add(punctTb);
        }

        if (_phraseCounter % 2 == 0)
        {
            if (setupCol.Children.Count > 0) mainPanel.Children.Add(setupCol);
            mainPanel.Children.Add(heroCol);
        }
        else
        {
            setupCol.Margin = new Thickness(16, 0, 0, 0);
            mainPanel.Children.Add(heroCol);
            if (setupCol.Children.Count > 0) mainPanel.Children.Add(setupCol);
        }

        visual.Container = mainPanel;
        return visual;
    }

    private KineticPhraseVisual BuildPosterInvertedRibbonBar(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = 50.0;

        int firstHeroIdx = heroIndices.Count > 0 ? heroIndices.First() : 0;

        var row0 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            bool isHero = heroIndices.Contains(i);

            if (isHero)
            {
                var ribbonBorder = new Border
                {
                    Background = ribbonBg,
                    Padding = new Thickness(12, 2, 12, 2),
                    CornerRadius = new CornerRadius(0),
                    Margin = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.25, FontWeights.Black, ribbonFg, new Thickness(2, 0, 2, 0), out var st, out var tt);
                ribbonBorder.Child = tb;
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });

                if (i <= firstHeroIdx && words.Count > 5) row0.Children.Add(ribbonBorder);
                else row1.Children.Add(ribbonBorder);
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.05, FontWeights.Bold, primaryBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });

                if (i <= firstHeroIdx && words.Count > 5) row0.Children.Add(tb);
                else row1.Children.Add(tb);
            }
        }

        if (row0.Children.Count > 0) mainStack.Children.Add(row0);
        if (row1.Children.Count > 0) mainStack.Children.Add(row1);

        visual.Container = mainStack;
        return visual;
    }

    private KineticPhraseVisual BuildPosterSplitScreenContrastCut(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        double baseFontSize = 50.0;
        int mid = Math.Max(1, (words.Count + 1) / 2);

        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };

        for (int i = 0; i < mid; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, primaryBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
            topRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        mainStack.Children.Add(topRow);

        var bottomBand = new Border
        {
            Background = ribbonBg,
            Padding = new Thickness(24, 6, 24, 6),
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        for (int i = mid; i < words.Count; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, ribbonFg, new Thickness(4, 0, 4, 0), out var st, out var tt);
            bottomRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }

        bottomBand.Child = bottomRow;
        mainStack.Children.Add(bottomBand);

        visual.Container = mainStack;
        return visual;
    }

    private KineticPhraseVisual BuildPosterBeatSlam(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = words.Count <= 2 ? 72.0 : 54.0;

        int numRows = words.Count <= 2 ? 1 : 2;
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
                bool isHero = heroIndices.Contains(i);
                Brush brush = isHero ? heroBrush : primaryBrush;

                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize, FontWeights.Black, brush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                rowStack.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }

            mainStack.Children.Add(rowStack);
        }

        visual.Container = mainStack;
        return visual;
    }

    private static TextBlock CreatePosterWordElement(
        string text, FontFamily font, double fontSize, FontWeight weight, Brush brush,
        Thickness margin,
        out ScaleTransform scaleTransform, out TranslateTransform translateTransform)
    {
        scaleTransform = new ScaleTransform(0.92, 0.92);
        translateTransform = new TranslateTransform(0.0, 18.0);

        var group = new TransformGroup();
        group.Children.Add(scaleTransform);
        group.Children.Add(translateTransform);

        return new TextBlock
        {
            Text = text,
            FontFamily = font,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = brush,
            Margin = margin,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = fontSize * 0.86,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = group,
            Opacity = 0.0
        };
    }
}
