using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using LyricalWidget.Audio;
using LyricalWidget.Common;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;
using LyricalWidget.Rendering;
using LyricalWidget.Services;

namespace LyricalWidget;

public partial class FullscreenVibeWindow : Window
{
    private static readonly SpringDamper SpringLineFluid = SpringDamper.LineFluid;
    private static readonly SpringDamper SpringLineSnappy = SpringDamper.LineSnappy;
    private static readonly SpringDamper SpringWordYFluid = SpringDamper.WordYFluid;
    private static readonly SpringDamper SpringWordScaleFluid = SpringDamper.WordScaleFluid;
    private static readonly SpringDamper SpringGlowDecayFluid = SpringDamper.GlowDecayFluid;

    private readonly List<LyricLineVisualItem> _vibeVisuals = new();
    private InterludeSlotItem? _interludeSlot;
    private int _currentActiveIndex = -1;
    private TimeSpan _lastVibeEffectivePos = TimeSpan.Zero;
    private double _activeLineChangedTime = 0;

    private void UpdateSplitFrame(
        double dt,
        long nowTicks,
        TimeSpan effectivePos,
        int activeIndex,
        double currentTimeSec)
    {
        if (_vibeVisuals.Count == 0) return;

        if (_lastVibeEffectivePos != TimeSpan.Zero)
        {
            TimeSpan posDelta = effectivePos - _lastVibeEffectivePos;
            if (Math.Abs(posDelta.TotalMilliseconds) > 250)
            {
                _currentActiveIndex = -999;
                ResetVisualsOnSeek(_vibeVisuals, effectivePos);
            }
        }
        _lastVibeEffectivePos = effectivePos;

        if (activeIndex != _currentActiveIndex)
        {
            _currentActiveIndex = activeIndex;
            _activeLineChangedTime = currentTimeSec;
            TriggerStaggeredWaveOffsets(_vibeVisuals, activeIndex, currentTimeSec);
        }

        double vh = _isPortraitMode ? PortraitLyricsViewport.ActualHeight : LyricsViewport.ActualHeight;
        if (vh <= 10) vh = 600;

        SimulateBeautifulLyrics(
            _vibeVisuals,
            activeIndex,
            effectivePos,
            vh,
            dt,
            currentTimeSec,
            _interludeSlot);
    }

    private void ApplyLyricColorsToVisuals(List<LyricLineVisualItem> visuals, ThemedLyricColors colors, bool crossfade)
    {
        FluidLyricWaveHelper.ApplyLyricColorsToVisuals(visuals, colors, crossfade, _settings.GamingMode);
    }

    private static void UpdateVisualsAccentColor(List<LyricLineVisualItem> visuals, InterludeSlotItem? slot, Color accent)
    {
        foreach (var line in visuals)
        {
            if (line.Words != null)
            {
                foreach (var w in line.Words)
                {
                    w.GlowEffect.Color = accent;
                }
            }
        }
        if (slot != null)
        {
            if (slot.Dot1.Effect is DropShadowEffect d1) d1.Color = accent;
            if (slot.Dot2.Effect is DropShadowEffect d2) d2.Color = accent;
            if (slot.Dot3.Effect is DropShadowEffect d3) d3.Color = accent;
        }
    }


    private void RebuildLyricsViews()
    {
        LyricsCanvas.Children.Clear();
        PortraitLyricsCanvas.Children.Clear();
        _vibeVisuals.Clear();

        _currentActiveIndex = -1;

        if (!_currentLyrics.HasSyncedLyrics) return;

        double vh = _isPortraitMode ? PortraitLyricsViewport.ActualHeight : LyricsViewport.ActualHeight;
        double vw = _isPortraitMode ? PortraitLyricsViewport.ActualWidth : LyricsViewport.ActualWidth;

        // If viewport is not yet sized (>50px), defer measurement until layout settles
        double viewportWidth = Math.Max(320, vw > 50 ? vw - 30 : (_isPortraitMode ? ActualWidth - 60 : ActualWidth * 0.52));

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

        // Dedicated glowing interlude slot
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
        // Fullscreen proportional font size
        double fontSize = _isPortraitMode ? 26.0 : 34.0;

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

        // Click-to-seek: clicking any lyric line immediately jumps Spotify to that timestamp
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
        double vw = _isPortraitMode ? PortraitLyricsViewport.ActualWidth : LyricsViewport.ActualWidth;
        double viewportW = Math.Max(320, vw > 50 ? vw - 30 : (_isPortraitMode ? ActualWidth - 60 : ActualWidth * 0.52));

        FluidLyricWaveHelper.ComputeStaticLayoutForList(_vibeVisuals, viewportW, _interludeSlot, lineGap: 32.0, interludeHeight: 44.0, interludeGap: 32.0, minHeight: 32.0);
    }

    private void TriggerStaggeredWaveOffsets(List<LyricLineVisualItem> visuals, int activeIndex, double currentTimeSec)
    {
        FluidLyricWaveHelper.TriggerStaggeredWaveOffsets(visuals, activeIndex, currentTimeSec);
    }

    // Main composition render loop

    private bool SimulateBeautifulLyrics(
        List<LyricLineVisualItem> visuals,
        int activeIndex,
        TimeSpan effectivePos,
        double viewportHeight,
        double dt,
        double currentTimeSec,
        InterludeSlotItem? interludeSlot)
    {
        if (visuals.Count == 0 || viewportHeight <= 10) return true;

        var springLine = SpringLineFluid;

        // Vertical Anchor: 0.42 of viewport height
        double anchorRatio = _isPortraitMode ? 0.40 : 0.42;
        double targetCenterY = viewportHeight * anchorRatio;

        int refIndex = Math.Clamp(activeIndex >= 0 ? activeIndex : 0, 0, visuals.Count - 1);
        double targetTop_A = targetCenterY - (visuals[refIndex].MeasuredHeight / 2.0);

        int minVisibleIdx = Math.Max(0, activeIndex - 5);
        int maxVisibleIdx = Math.Min(visuals.Count - 1, activeIndex + 5);

        bool allSettled = true;
        int numSubSteps = Math.Clamp((int)Math.Round(dt / 0.016), 1, 8);
        double subDt = dt / numSubSteps;

        for (int i = 0; i < visuals.Count; i++)
        {
            var line = visuals[i];

            if (i < minVisibleIdx || i > maxVisibleIdx)
            {
                if (line.Container.Visibility != Visibility.Collapsed)
                {
                    line.Container.Visibility = Visibility.Collapsed;
                    line.CurrentOpacity = 0.0;
                }
                line.IsSettled = true;
                continue;
            }

            int dist = Math.Abs(i - activeIndex);
            line.TargetY = targetTop_A + (line.StaticTop - visuals[refIndex].StaticTop);

            double targetOpacity = 0.0;
            double targetScale = 0.84;

            if (i == activeIndex)
            {
                targetOpacity = 1.0;
                targetScale = 1.045;
            }
            else if (dist <= 4)
            {
                if (dist == 1)
                {
                    targetOpacity = 0.38;
                    targetScale = 0.94;
                }
                else if (dist == 2)
                {
                    targetOpacity = 0.18;
                    targetScale = 0.88;
                }
                else if (dist == 3)
                {
                    targetOpacity = 0.08;
                    targetScale = 0.84;
                }
            }

            // Staggered ripple spring simulation
            if (currentTimeSec >= line.StartTimeOffset)
            {
                bool settledY;
                if (dt > 0.25)
                {
                    line.CurrentY = line.TargetY;
                    line.VelocityY = 0.0;
                    line.CurrentOpacity = targetOpacity;
                    line.CurrentScale = targetScale;
                    settledY = true;
                }
                else
                {
                    double curY = line.CurrentY;
                    double vY = line.VelocityY;
                    settledY = false;
                    for (int s = 0; s < numSubSteps; s++)
                    {
                        settledY = springLine.Step(ref curY, ref vY, line.TargetY, subDt, 0.05);
                    }
                    line.CurrentY = curY;
                    line.VelocityY = vY;

                    line.CurrentOpacity += (targetOpacity - line.CurrentOpacity) * Math.Min(1.0, 14.0 * dt);
                    line.CurrentScale += (targetScale - line.CurrentScale) * Math.Min(1.0, 14.0 * dt);
                }

                bool settledOpacity = Math.Abs(targetOpacity - line.CurrentOpacity) < 0.01;
                bool settledScale = Math.Abs(targetScale - line.CurrentScale) < 0.01;
                line.IsSettled = settledY && settledOpacity && settledScale;

                line.TranslateTransform.Y = line.CurrentY;
                line.Container.Opacity = line.CurrentOpacity;
                line.ScaleTransform.ScaleX = line.CurrentScale;
                line.ScaleTransform.ScaleY = line.CurrentScale;

                if (line.CurrentTranslateX != 0)
                {
                    line.CurrentTranslateX = 0;
                    line.TranslateTransform.X = 0;
                }
                if (line.IndicatorBar != null && line.IndicatorBar.Opacity > 0)
                {
                    line.IndicatorBar.Opacity = 0.0;
                }

                if (line.Container.Effect != null)
                {
                    line.Container.Effect = null;
                }

                if (line.CurrentOpacity <= 0.005)
                {
                    if (line.Container.Visibility != Visibility.Collapsed)
                        line.Container.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (line.Container.Visibility != Visibility.Visible)
                        line.Container.Visibility = Visibility.Visible;
                }
            }
            else
            {
                line.IsSettled = false;
            }

            if (!line.IsSettled) allSettled = false;

            // Word-by-Word karaoke swipe & expressive bounce
            if (i == activeIndex && line.Words != null && line.Words.Count > 0)
            {
                bool wordsSettled = UpdateWordWave(line, effectivePos, dt);
                if (!wordsSettled) allSettled = false;
            }
            else if (line.Words != null && line.Words.Count > 0)
            {
                UpdateInactiveWords(line, isPast: i < activeIndex, effectivePos, dt);
            }
        }

        // Dedicated glowing interlude orbs
        bool interludeActive = UpdateInterludeOrbs(activeIndex, effectivePos, visuals, targetTop_A, dt, currentTimeSec, interludeSlot);
        if (interludeActive) allSettled = false;

        return allSettled;
    }

    private bool UpdateWordWave(LyricLineVisualItem line, TimeSpan effectivePos, double dt, bool isFluid = true)
    {
        return FluidLyricWaveHelper.UpdateWordWave(line, effectivePos, dt, _settings.GamingMode);
    }

    private static void ResetVisualsOnSeek(List<LyricLineVisualItem> visuals, TimeSpan effectivePos)
    {
        double nowMs = effectivePos.TotalMilliseconds;
        foreach (var line in visuals)
        {
            line.VelocityY = 0.0;
            line.CurrentTranslateX = 0.0;
            line.TranslateTransform.X = 0.0;
            if (line.Words != null && line.Words.Count > 0)
            {
                double lineStartMs = line.Model != null ? line.Model.Timestamp.TotalMilliseconds : 0.0;
                double lineEndMs = line.LineEndMs > lineStartMs
                    ? line.LineEndMs
                    : (line.Model != null && line.Model.EndTime.TotalMilliseconds > lineStartMs
                        ? line.Model.EndTime.TotalMilliseconds
                        : lineStartMs + 3500.0);
                bool hasWordSync = line.Model != null && line.Model.HasWordSync;

                int n = line.Words.Count;
                double spread = Math.Min(3.0, Math.Max(1.0, n * 0.85));
                double lineDur = Math.Max(350.0, lineEndMs - lineStartMs);
                double t = Math.Clamp((nowMs - lineStartMs) / (lineDur * 0.94), 0.0, 1.0);
                double wavePos = t * ((n - 1) + spread);

                for (int i = 0; i < n; i++)
                {
                    var w = line.Words[i];
                    w.VelocityY = 0.0;
                    w.VelocityScale = 0.0;
                    w.VelocityGlow = 0.0;
                    w.CurrentTranslateY = 0.0;
                    w.CurrentScale = 1.0;
                    w.CurrentGlowOpacity = 0.0;
                    w.Transform.Y = 0.0;
                    w.ScaleTransform.ScaleX = 1.0;
                    w.ScaleTransform.ScaleY = 1.0;
                    w.GlowEffect.Opacity = 0.0;

                    double p;
                    if (!hasWordSync)
                    {
                        p = Math.Clamp((wavePos - i) / spread, 0.0, 1.0);
                    }
                    else
                    {
                        double wordStartMs = w.Model != null ? w.Model.Timestamp.TotalMilliseconds : lineStartMs;
                        double wordEndMs = w.Model != null ? w.Model.EndTime.TotalMilliseconds : lineEndMs;

                        double targetEndMs;
                        if (i + 2 < n && line.Words[i + 2].Model != null)
                        {
                            targetEndMs = line.Words[i + 2].Model!.EndTime.TotalMilliseconds;
                        }
                        else
                        {
                            targetEndMs = Math.Max(wordEndMs + 350.0, lineEndMs);
                        }

                        double sweepMs = Math.Clamp(targetEndMs - wordStartMs, 450.0, 2400.0);
                        p = Math.Clamp((nowMs - wordStartMs) / sweepMs, 0.0, 1.0);
                    }

                    if (p <= 0.001)
                    {
                        w.StopActive2.Offset = 0.0;
                        w.StopInactive1.Offset = 0.0;
                    }
                    else if (p >= 0.999)
                    {
                        w.StopActive2.Offset = 1.0;
                        w.StopInactive1.Offset = 1.0;
                    }
                    else
                    {
                        w.StopActive2.Offset = Math.Max(0.0, p - 0.07);
                        w.StopInactive1.Offset = Math.Min(1.0, p + 0.07);
                    }
                    w.IsSettled = true;
                }
            }
        }
    }

    private void UpdateInactiveWords(LyricLineVisualItem line, bool isPast, TimeSpan effectivePos, double dt)
    {
        FluidLyricWaveHelper.UpdateInactiveWords(line, isPast, effectivePos, dt);
    }

    private bool UpdateInterludeOrbs(
        int activeIndex,
        TimeSpan effectivePos,
        List<LyricLineVisualItem> visuals,
        double targetTop_A,
        double dt,
        double currentTimeSec,
        InterludeSlotItem? interludeSlot)
    {
        if (interludeSlot == null || visuals.Count == 0) return false;

        LyricLineVisualItem? targetLine = null;
        for (int i = 0; i < visuals.Count; i++)
        {
            if (visuals[i].HasPrecedingInterlude)
            {
                if (effectivePos >= (visuals[i].InterludeStart - TimeSpan.FromMilliseconds(500)) &&
                    effectivePos < visuals[i].InterludeEnd)
                {
                    targetLine = visuals[i];
                    break;
                }
            }
        }

        bool isInterludeActive = targetLine != null;
        interludeSlot.IsActive = isInterludeActive;

        if (isInterludeActive && targetLine != null)
        {
            interludeSlot.Container.Visibility = Visibility.Visible;
            double targetY = targetTop_A + (interludeSlot.StaticTop - visuals[Math.Clamp(activeIndex, 0, visuals.Count - 1)].StaticTop);

            double curY = interludeSlot.CurrentY;
            double vY = interludeSlot.VelocityY;
            SpringLineFluid.Step(ref curY, ref vY, targetY, dt, 0.05);
            interludeSlot.CurrentY = curY;
            interludeSlot.VelocityY = vY;

            interludeSlot.CurrentOpacity += (1.0 - interludeSlot.CurrentOpacity) * Math.Min(1.0, 10.0 * dt);
            interludeSlot.Container.Opacity = interludeSlot.CurrentOpacity;

            double time = currentTimeSec * 3.8;
            double b1 = Math.Sin(time);
            double b2 = Math.Sin(time - 0.7);
            double b3 = Math.Sin(time - 1.4);

            interludeSlot.Dot1Transform.Y = -7.0 * Math.Max(0, b1);
            interludeSlot.Dot2Transform.Y = -7.0 * Math.Max(0, b2);
            interludeSlot.Dot3Transform.Y = -7.0 * Math.Max(0, b3);

            double sc1 = 1.0 + 0.22 * Math.Max(0, b1);
            double sc2 = 1.0 + 0.22 * Math.Max(0, b2);
            double sc3 = 1.0 + 0.22 * Math.Max(0, b3);

            interludeSlot.Dot1Scale.ScaleX = sc1; interludeSlot.Dot1Scale.ScaleY = sc1;
            interludeSlot.Dot2Scale.ScaleX = sc2; interludeSlot.Dot2Scale.ScaleY = sc2;
            interludeSlot.Dot3Scale.ScaleX = sc3; interludeSlot.Dot3Scale.ScaleY = sc3;

            interludeSlot.Dot1.Opacity = 0.35 + 0.65 * Math.Max(0, b1);
            interludeSlot.Dot2.Opacity = 0.35 + 0.65 * Math.Max(0, b2);
            interludeSlot.Dot3.Opacity = 0.35 + 0.65 * Math.Max(0, b3);

            return true;
        }
        else
        {
            if (interludeSlot.CurrentOpacity > 0.01)
            {
                interludeSlot.CurrentOpacity += (0.0 - interludeSlot.CurrentOpacity) * Math.Min(1.0, 14.0 * dt);
                interludeSlot.Container.Opacity = interludeSlot.CurrentOpacity;
                return true;
            }
            else
            {
                interludeSlot.Container.Visibility = Visibility.Collapsed;
                return false;
            }
        }
    }

    private static int FindCurrentLyricLineIndex(List<LyricLine> lines, TimeSpan currentPos)
    {
        if (lines.Count == 0) return -1;
        if (currentPos < lines[0].Timestamp) return -1;

        int low = 0;
        int high = lines.Count - 1;
        int result = -1;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (lines[mid].Timestamp <= currentPos)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result >= 0 ? result : 0;
    }

}
