using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
