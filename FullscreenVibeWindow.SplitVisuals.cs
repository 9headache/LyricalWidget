using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LyricalWidget.Lyrics;
using LyricalWidget.Rendering;

namespace LyricalWidget;

// Builds the Split mode lyric lines
public partial class FullscreenVibeWindow
{
    private void RebuildLyricsViews()
    {
        _spotlightFogRenderer.UpdateLyrics(_currentLyrics);
        _liveCodeRenderer.UpdateLyrics(_currentLyrics);
        _moonlightRenderer.UpdateLyrics(_currentLyrics);
        _builtLyricFontSize = GetLyricFontSize();
        _builtLyricWrapWidth = GetLyricWrapWidth();
        LyricsCanvas.Children.Clear();
        PortraitLyricsCanvas.Children.Clear();
        _vibeVisuals.Clear();

        _currentActiveIndex = -1;

        if (!_currentLyrics.HasSyncedLyrics) return;

        double viewportWidth = GetLyricWrapWidth();

        var lines = _currentLyrics.Lines;
        var canvas = _isPortraitMode ? PortraitLyricsCanvas : LyricsCanvas;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            bool hasInterlude = false;
            TimeSpan prevEnd = i > 0 ? lines[i - 1].EndTime : TimeSpan.Zero;
            if (line.Timestamp - prevEnd >= TimeSpan.FromSeconds(4.0))
            {
                hasInterlude = true;
            }

            TimeSpan nextStart = (i + 1 < lines.Count) ? lines[i + 1].Timestamp : line.EndTime;
            TimeSpan gap = nextStart - line.Timestamp;
            TimeSpan calcLineEnd = (gap > TimeSpan.Zero && gap <= TimeSpan.FromMilliseconds(10000))
                ? nextStart
                : (line.Timestamp + (gap > TimeSpan.Zero ? TimeSpan.FromMilliseconds(10000) : (line.Duration > TimeSpan.Zero ? line.Duration : TimeSpan.FromSeconds(4))));
            double lineEndMs = calcLineEnd.TotalMilliseconds;

            var item = CreateVibeLineItem(line, viewportWidth);
            item.HasPrecedingInterlude = hasInterlude;
            item.InterludeStart = prevEnd;
            item.InterludeEnd = line.Timestamp;
            item.LineEndMs = lineEndMs;

            _vibeVisuals.Add(item);
            canvas.Children.Add(item.Container);
        }

        _interludeSlot = BuildInterludeSlot();
        canvas.Children.Add(_interludeSlot.Container);

        ComputeStaticLineLayouts();
    }

    private InterludeSlotItem BuildInterludeSlot()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed,
            Opacity = 0.0,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        var containerScale = new ScaleTransform(1.0, 1.0);
        panel.RenderTransform = containerScale;

        var dot1T = new TranslateTransform(0, 0);
        var dot2T = new TranslateTransform(0, 0);
        var dot3T = new TranslateTransform(0, 0);

        var dot1S = new ScaleTransform(1.0, 1.0);
        var dot2S = new ScaleTransform(1.0, 1.0);
        var dot3S = new ScaleTransform(1.0, 1.0);

        var dot1 = CreateGlowingOrb(dot1T, dot1S);
        var dot2 = CreateGlowingOrb(dot2T, dot2S);
        var dot3 = CreateGlowingOrb(dot3T, dot3S);

        panel.Children.Add(dot1);
        panel.Children.Add(dot2);
        panel.Children.Add(dot3);

        return new InterludeSlotItem
        {
            Container = panel,
            Dot1 = dot1,
            Dot2 = dot2,
            Dot3 = dot3,
            Dot1Transform = dot1T,
            Dot2Transform = dot2T,
            Dot3Transform = dot3T,
            Dot1Scale = dot1S,
            Dot2Scale = dot2S,
            Dot3Scale = dot3S,
            ContainerScale = containerScale
        };
    }

    private Ellipse CreateGlowingOrb(TranslateTransform transform, ScaleTransform scale)
    {
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(transform);

        var orb = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = Brushes.White,
            Margin = new Thickness(9, 0, 9, 0),
            Opacity = 0.40,
            RenderTransform = group,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        orb.Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 0,
            Color = _currentPalette.Primary,
            Opacity = 0.85
        };

        return orb;
    }

    private LyricWordVisualItem CreateWordVisualItem(string text, double fontSize, FontFamily fontFamily, LyricWord? model, int index)
    {
        var wordTranslate = new TranslateTransform(0, 0);
        var wordScale = new ScaleTransform(1.0, 1.0);
        var wordGroup = new TransformGroup();
        wordGroup.Children.Add(wordScale);
        wordGroup.Children.Add(wordTranslate);

        var stopActive1 = new GradientStop(_currentThemedLyricColors.ActiveColor, 0.0);
        var stopActive2 = new GradientStop(_currentThemedLyricColors.ActiveColor, 0.0);
        var stopInactive1 = new GradientStop(_currentThemedLyricColors.InactiveColor, 0.0);
        var stopInactive2 = new GradientStop(_currentThemedLyricColors.InactiveColor, 1.0);
        var wordBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        wordBrush.GradientStops.Add(stopActive1);
        wordBrush.GradientStops.Add(stopActive2);
        wordBrush.GradientStops.Add(stopInactive1);
        wordBrush.GradientStops.Add(stopInactive2);

        var glow = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 0,
            Color = _currentThemedLyricColors.AccentGlowColor,
            Opacity = 0.0
        };

        var wordBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            FontFamily = fontFamily,
            Foreground = wordBrush,
            RenderTransform = wordGroup,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = glow
        };

        return new LyricWordVisualItem
        {
            Element = wordBlock,
            Transform = wordTranslate,
            ScaleTransform = wordScale,
            GlowEffect = glow,
            WordBrush = wordBrush,
            StopActive1 = stopActive1,
            StopActive2 = stopActive2,
            StopInactive1 = stopInactive1,
            StopInactive2 = stopInactive2,
            Model = model,
            WordIndex = index,
            CurrentTranslateY = 0,
            CurrentScale = 1.0,
            CurrentGlowOpacity = 0.0,
            IsSettled = true
        };
    }

    private static List<LyricWord> GenerateFallbackWords(LyricLine line)
    {
        var words = new List<LyricWord>();
        if (string.IsNullOrWhiteSpace(line.Text)) return words;

        string[] tokens = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return words;

        if (tokens.Length == 1 && line.Text.Length > 1 && !line.Text.Contains(' '))
        {
            var charTokens = new List<string>(line.Text.Length);
            for (int i = 0; i < line.Text.Length; i++)
            {
                charTokens.Add(line.Text[i].ToString());
            }
            tokens = charTokens.ToArray();
        }

        double lineStartMs = line.Timestamp.TotalMilliseconds;
        double lineDurMs = line.Duration.TotalMilliseconds > 0 ? line.Duration.TotalMilliseconds : 3500.0;

        int totalChars = 0;
        foreach (var t in tokens)
        {
            totalChars += Math.Max(1, t.Length);
        }
        if (totalChars == 0) totalChars = 1;

        double curStartMs = lineStartMs;
        for (int j = 0; j < tokens.Length; j++)
        {
            string token = tokens[j];
            int charCount = Math.Max(1, token.Length);
            double wDurMs = lineDurMs * ((double)charCount / totalChars);
            words.Add(new LyricWord(
                TimeSpan.FromMilliseconds(curStartMs),
                TimeSpan.FromMilliseconds(wDurMs),
                token
            ));
            curStartMs += wDurMs;
        }

        return words;
    }

    private LyricLineVisualItem CreateVibeLineItem(LyricLine line, double viewportWidth)
    {
        var container = new Grid
        {
            Width = viewportWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Opacity = 0.35,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0, 0.5)
        };

        var transformGroup = new TransformGroup();
        var scale = new ScaleTransform(0.95, 0.95);
        var translate = new TranslateTransform(0, 0);
        transformGroup.Children.Add(scale);
        transformGroup.Children.Add(translate);
        container.RenderTransform = transformGroup;

        var indicatorGlow = new DropShadowEffect
        {
            Color = _currentThemedLyricColors.AccentGlowColor,
            BlurRadius = 10,
            ShadowDepth = 0,
            Opacity = 0.85
        };
        var indicatorBar = new Border
        {
            Width = 4.0,
            CornerRadius = new CornerRadius(2.0),
            Background = new SolidColorBrush(_currentThemedLyricColors.ActiveColor),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(-12, 4, 0, 4),
            Opacity = 0.0,
            Effect = indicatorGlow
        };
        container.Children.Add(indicatorBar);

        var fontFamily = new FontFamily("Segoe UI Variable Display, Inter, Segoe UI, sans-serif");
        double fontSize = _builtLyricFontSize > 0 ? _builtLyricFontSize : GetLyricFontSize();

        var wordsList = new List<LyricWordVisualItem>();
        var wrapPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };

        var sourceWords = (line.HasWordSync && line.Words != null && line.Words.Count > 0)
            ? line.Words
            : GenerateFallbackWords(line);

        for (int j = 0; j < sourceWords.Count; j++)
        {
            var word = sourceWords[j];
            string wText = word.Text + (word.Text.EndsWith(" ") ? "" : " ");
            var wItem = CreateWordVisualItem(wText, fontSize, fontFamily, word, j);
            wordsList.Add(wItem);
            wrapPanel.Children.Add(wItem.Element);
        }

        TextBlock? subTextBlock = null;
        if (line.HasSubText && _settings.ShowSubLines)
        {
            var mainStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            mainStack.Children.Add(wrapPanel);

            subTextBlock = new TextBlock
            {
                Text = line.SubText,
                FontFamily = fontFamily,
                FontSize = Math.Max(14.0, fontSize * 0.58),
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xCB, 0xD5, 0xE1)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
                Opacity = 0.65
            };
            mainStack.Children.Add(subTextBlock);
            container.Children.Add(mainStack);
        }
        else
        {
            container.Children.Add(wrapPanel);
        }

        container.MouseLeftButtonUp += (s, e) =>
        {
            _ = _mediaService.TrySeekAsync(line.Timestamp);
        };

        container.Measure(new Size(viewportWidth, double.PositiveInfinity));
        double measuredH = Math.Max(32.0, container.DesiredSize.Height);

        return new LyricLineVisualItem
        {
            Container = container,
            SubTextBlock = subTextBlock,
            TranslateTransform = translate,
            ScaleTransform = scale,
            IndicatorBar = indicatorBar,
            IndicatorGlow = indicatorGlow,
            Model = line,
            Words = wordsList,
            MeasuredHeight = measuredH,
            CurrentOpacity = 0.35,
            CurrentScale = 0.95,
            IsSettled = false
        };
    }

    private void ComputeStaticLineLayouts()
    {
        double viewportW = GetLyricWrapWidth();

        FluidLyricWaveHelper.ComputeStaticLayoutForList(_vibeVisuals, viewportW, _interludeSlot, lineGap: 32.0, interludeHeight: 44.0, interludeGap: 32.0, minHeight: 32.0);
    }
}
