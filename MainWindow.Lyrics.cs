using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LyricalWidget.Audio;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;
using LyricalWidget.Rendering;
using LyricalWidget.Services;

namespace LyricalWidget;

public partial class MainWindow : Window
{
    private long _lastFrameTimestamp = 0;
    private long _nextFrameDueTimestamp = 0;
    private int _currentActiveIndex = -1;
    private double _activeLineChangedTime = 0;
    private int _lastDisplayedCurSec = -1;
    private int _lastDisplayedTotalSec = -1;
    private bool _isRenderingSubscribed = false;
    private TimeSpan _lastRenderEffectivePos = TimeSpan.Zero;

    private static readonly SpringDamper SpringLineFluid = SpringDamper.LineFluid;
    private static readonly SpringDamper SpringLineSnappy = SpringDamper.LineSnappy;
    private static readonly SpringDamper SpringWordYFluid = SpringDamper.WordYFluid;
    private static readonly SpringDamper SpringWordYSnappy = SpringDamper.WordYSnappy;
    private static readonly SpringDamper SpringWordScaleFluid = SpringDamper.WordScaleFluid;
    private static readonly SpringDamper SpringWordScaleSnappy = SpringDamper.WordScaleSnappy;
    private static readonly SpringDamper SpringGlowDecayFluid = SpringDamper.GlowDecayFluid;
    private static readonly SpringDamper SpringGlowDecaySnappy = SpringDamper.GlowDecaySnappy;

    private readonly List<LyricLineVisualItem> _lyricsOnlyVisuals = new();
    private readonly List<LyricLineVisualItem> _sideLyricsVisuals = new();

    private readonly MoonlightBloomRenderer _lyricsOnlyMoonlight;
    private readonly MoonlightBloomRenderer _sideMoonlight;

    private InterludeSlotItem? _lyricsOnlyInterludeSlot;
    private InterludeSlotItem? _sideLyricsInterludeSlot;


    private void OnTimelineResynced(object? sender, TimeSpan pos)
    {
        Dispatcher.InvokeAsync(EnsureRenderingSubscribed);
    }

    private void EnsureRenderingSubscribed()
    {
        if (!_isRenderingSubscribed && !_isClosing)
        {
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnCompositionRendering;
            _isRenderingSubscribed = true;
        }
    }

    private void StopRenderingLoop()
    {
        if (_isRenderingSubscribed)
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
            _isRenderingSubscribed = false;
        }
    }


    // Browser / YouTube media
    private bool _clockForBrowser = false;
    private bool _browserLyricsPending = false;

    public void SetBrowserMediaBehavior(BrowserMediaBehavior behavior)
    {
        _settings.BrowserMediaBehavior = behavior;
        _mediaService.BrowserBehavior = behavior;
        _trayService?.UpdateBrowserMediaState(behavior);
        UpdateToolbarButtons();

        if (!_browserLyricsPending) ResolveBrowserClock(_mediaService.CurrentTrack);
        if (behavior != BrowserMediaBehavior.YouTubeLyricsWithClockFallback) _clockForBrowser = false;

        _mediaService.ReevaluateSessions();
        if (!IsFullscreenActive) UpdateAutoHideState(_mediaService.IsPlaying);
        SaveCurrentBounds();
    }

    private bool UsesBrowserClockFallback(TrackInfo track)
    {
        return track.IsBrowserSource && _settings.BrowserMediaBehavior == BrowserMediaBehavior.YouTubeLyricsWithClockFallback;
    }

    /// <summary>Called before a track's lyrics are known: stay on the clock unless lyrics are already on screen.</summary>
    private void BeginBrowserLookup(TrackInfo track)
    {
        bool wasShowingLyrics = _currentLyrics.HasSyncedLyrics || (_browserLyricsPending && !_clockForBrowser);
        bool usesFallback = UsesBrowserClockFallback(track);
        _browserLyricsPending = usesFallback;
        _clockForBrowser = usesFallback && !wasShowingLyrics;
    }

    /// <summary>Called once the lookup has finished.</summary>
    private void ResolveBrowserClock(TrackInfo track)
    {
        _browserLyricsPending = false;
        _clockForBrowser = UsesBrowserClockFallback(track) && !_currentLyrics.HasSyncedLyrics;
    }

    private void NudgeSongOffset(int deltaMs)
    {
        _currentLyrics.UserOffsetMs += deltaMs;
        _ = _lyricsService.UpdateCachedUserOffsetAsync(_mediaService.CurrentTrack, _currentLyrics.UserOffsetMs);

        int totalOffset = _settings.SyncOffsetMs + _currentLyrics.UserOffsetMs;
        string sign = totalOffset >= 0 ? "+" : "";
        ShowBannerToast("SYNC NUDGE", $"{sign}{totalOffset} ms ({deltaMs:+0;-0}ms live shift)", "Saved for this song");
    }

    // Display modes, backgrounds, and settings

    private void RebuildLyricsViews()
    {
        _lyricsOnlyMoonlight.UpdateLyrics(_currentLyrics);
        _sideMoonlight.UpdateLyrics(_currentLyrics);
        // The glyphs are far wider than the default face, so the size slider maps to a larger cap here
        _lyricsOnlyMoonlight.MaxFontSize = _settings.FontSize * 1.7;
        _sideMoonlight.MaxFontSize = _settings.FontSize * 1.5;

        LyricsOnlyCanvas.Children.Clear();
        SideLyricsCanvas.Children.Clear();
        _lyricsOnlyVisuals.Clear();
        _sideLyricsVisuals.Clear();

        _currentActiveIndex = -1;

        if (!_currentLyrics.HasSyncedLyrics) return;

        bool isLeftAlign = _settings.LyricAlignment == "Left";
        var lines = _currentLyrics.Lines;

        double viewportWidthOnly = Math.Max(200, LyricsOnlyViewport.ActualWidth > 10 ? LyricsOnlyViewport.ActualWidth : Width - 40);
        double viewportWidthSide = Math.Max(200, SideLyricsViewport.ActualWidth > 10 ? SideLyricsViewport.ActualWidth : Width - 240);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // Preceding interlude break detection
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

            // LyricsOnly Canvas item
            var v1 = CreateLineVisualItem(line, isLeftAlign ? HorizontalAlignment.Left : HorizontalAlignment.Center, isLeftAlign ? TextAlignment.Left : TextAlignment.Center, viewportWidthOnly, isLeftAlign);
            v1.HasPrecedingInterlude = hasInterlude;
            v1.InterludeStart = prevEnd;
            v1.InterludeEnd = line.Timestamp;
            v1.LineEndMs = lineEndMs;
            _lyricsOnlyVisuals.Add(v1);
            LyricsOnlyCanvas.Children.Add(v1.Container);

            // SideLyrics Canvas item (Left-aligned)
            var v2 = CreateLineVisualItem(line, HorizontalAlignment.Left, TextAlignment.Left, viewportWidthSide, isLeftOrigin: true);
            v2.HasPrecedingInterlude = hasInterlude;
            v2.InterludeStart = prevEnd;
            v2.InterludeEnd = line.Timestamp;
            v2.LineEndMs = lineEndMs;
            _sideLyricsVisuals.Add(v2);
            SideLyricsCanvas.Children.Add(v2.Container);
        }

        CreateInterludeOrbsElements();
        ComputeStaticLineLayouts();

        ApplyEffectBudget();
    }

    private void CreateInterludeOrbsElements()
    {
        var slot1 = BuildInterludeSlot(HorizontalAlignment.Center);
        _lyricsOnlyInterludeSlot = slot1;
        LyricsOnlyCanvas.Children.Add(slot1.Container);

        var slot2 = BuildInterludeSlot(HorizontalAlignment.Left);
        _sideLyricsInterludeSlot = slot2;
        SideLyricsCanvas.Children.Add(slot2.Container);
    }

    private InterludeSlotItem BuildInterludeSlot(HorizontalAlignment horizAlign)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = horizAlign,
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
            Width = 10,
            Height = 10,
            Fill = Brushes.White,
            Margin = new Thickness(7, 0, 7, 0),
            Opacity = 0.35,
            RenderTransform = group,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        orb.Effect = new DropShadowEffect
        {
            BlurRadius = 14,
            ShadowDepth = 0,
            Color = _currentAccentColor,
            Opacity = _settings.GamingMode ? 0.0 : 0.8
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
            BlurRadius = 14,
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

        // If single token without space but has multiple characters (e.g. CJK text), split per character
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

    private LyricLineVisualItem CreateLineVisualItem(LyricLine line, HorizontalAlignment horizAlign, TextAlignment textAlign, double viewportWidth, bool isLeftOrigin)
    {
        var container = new Grid
        {
            Width = viewportWidth,
            HorizontalAlignment = horizAlign,
            Opacity = 0.35,
            RenderTransformOrigin = isLeftOrigin ? new Point(0, 0.5) : new Point(0.5, 0.5)
        };

        var transformGroup = new TransformGroup();
        var scaleTransform = new ScaleTransform(0.95, 0.95);
        var translateTransform = new TranslateTransform(0, 0);
        transformGroup.Children.Add(scaleTransform);
        transformGroup.Children.Add(translateTransform);
        container.RenderTransform = transformGroup;

        var indicatorGlow = new DropShadowEffect
        {
            Color = _currentThemedLyricColors.AccentGlowColor,
            BlurRadius = 8,
            ShadowDepth = 0,
            Opacity = 0.8
        };
        var indicatorBar = new Border
        {
            Width = 3.5,
            CornerRadius = new CornerRadius(1.75),
            Background = new SolidColorBrush(_currentThemedLyricColors.ActiveColor),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(-9, 2, 0, 2),
            Opacity = 0.0,
            Effect = indicatorGlow
        };
        container.Children.Add(indicatorBar);

        var fontFamily = new FontFamily("Segoe UI Variable Display, Inter, Segoe UI, sans-serif");
        double fontSize = _settings.FontSize;

        var visualItem = new LyricLineVisualItem
        {
            Container = container,
            ScaleTransform = scaleTransform,
            TranslateTransform = translateTransform,
            IndicatorBar = indicatorBar,
            IndicatorGlow = indicatorGlow,
            Model = line,
            CurrentOpacity = 0.35,
            CurrentScale = 0.95,
            IsSettled = false
        };

        var wordsList = new List<LyricWordVisualItem>();
        var wrapPanel = new WrapPanel
        {
            HorizontalAlignment = horizAlign
        };

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

        visualItem.Words = wordsList;

        if (line.HasSubText && _settings.ShowSubLines)
        {
            var mainStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = horizAlign
            };
            mainStack.Children.Add(wrapPanel);

            var subTextBlock = new TextBlock
            {
                Text = line.SubText,
                FontFamily = fontFamily,
                FontSize = Math.Max(10.0, fontSize * 0.58),
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(0xA6, 0xCB, 0xD5, 0xE1)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = textAlign,
                HorizontalAlignment = horizAlign,
                Margin = new Thickness(0, 3, 0, 0),
                Opacity = 0.65
            };
            visualItem.SubTextBlock = subTextBlock;
            mainStack.Children.Add(subTextBlock);
            container.Children.Add(mainStack);
        }
        else
        {
            container.Children.Add(wrapPanel);
        }

        // CRITICAL BUG FIX: Explicitly measure line container at true viewport width
        container.Measure(new Size(viewportWidth, double.PositiveInfinity));
        visualItem.MeasuredHeight = Math.Max(28.0, container.DesiredSize.Height);

        return visualItem;
    }

    private void ComputeStaticLineLayouts()
    {
        double widthOnly = Math.Max(180, LyricsOnlyViewport.ActualWidth > 10 ? LyricsOnlyViewport.ActualWidth - 6 : Width - 46);
        double widthSide = Math.Max(180, SideLyricsViewport.ActualWidth > 10 ? SideLyricsViewport.ActualWidth - 6 : Width - 246);

        ComputeStaticLayoutForList(_lyricsOnlyVisuals, widthOnly, _lyricsOnlyInterludeSlot);
        ComputeStaticLayoutForList(_sideLyricsVisuals, widthSide, _sideLyricsInterludeSlot);
    }

    private static void ComputeStaticLayoutForList(List<LyricLineVisualItem> visuals, double viewportWidth, InterludeSlotItem? interludeSlot)
    {
        FluidLyricWaveHelper.ComputeStaticLayoutForList(visuals, viewportWidth, interludeSlot);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SaveCurrentBounds();

        double vh = Math.Max(LyricsOnlyViewport.ActualHeight, SideLyricsViewport.ActualHeight);
        if (vh > 10)
        {
            double fadeMargin = vh < 200 ? 0.08 : 0.12;
            LyricsOnlyMaskTop.Offset = fadeMargin;
            LyricsOnlyMaskBottom.Offset = 1.0 - fadeMargin;
            SideLyricsMaskTop.Offset = fadeMargin;
            SideLyricsMaskBottom.Offset = 1.0 - fadeMargin;
        }

        ComputeStaticLineLayouts();
    }

    // Main composition render loop and spring physics
    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        try
        {
            if (Visibility != Visibility.Visible || _isManuallyHidden) return;
            if (_isAutoHidden && !ShowsIdleClockWhenHidden) return;

        long nowTicks = Stopwatch.GetTimestamp();
        int targetFps = _settings.FpsCap > 0 ? _settings.FpsCap : 60;
        if (_settings.GamingMode) targetFps = Math.Min(targetFps, 30); // Never competes with a game for frames
        double targetInterval = 1.0 / Math.Clamp(targetFps, 15, 120);

        double elapsedSinceLastRender = _lastFrameTimestamp > 0
            ? (double)(nowTicks - _lastFrameTimestamp) / Stopwatch.Frequency
            : targetInterval;

        // Pace frames against a running deadline rather than the previous frame: a late frame then shortens the
        // next wait instead of pushing every later frame back, so the average really is the cap.
        // (1.5ms slack so tick jitter does not skip a frame that is all but due)
        long slackTicks = (long)(0.0015 * Stopwatch.Frequency);
        if (nowTicks < _nextFrameDueTimestamp - slackTicks)
        {
            return;
        }
        _nextFrameDueTimestamp = Math.Max(nowTicks, _nextFrameDueTimestamp + (long)(targetInterval * Stopwatch.Frequency));

        _lastFrameTimestamp = nowTicks;
        double dt = elapsedSinceLastRender;
        if (dt <= 0.0) dt = targetInterval;

        if (_isAutoHidden && ShowsIdleClockWhenHidden)
        {
            UpdateClockTime(dt);
            return;
        }

        if (!_settings.GamingMode && _settings.AudioReactiveGlow)
        {
            double bassPulse = _currentBassEnergy;
            // Whole steps: an unchanged value does not make the effect repaint
            double auraBlur = Math.Round(26.0 + (bassPulse * 16.0));
            double auraOpacity = Math.Round((0.55 + (bassPulse * 0.35)) * 50.0) / 50.0;
            SideAlbumAuraGlow.BlurRadius = auraBlur;
            SideAlbumAuraGlow.Opacity = auraOpacity;
            AlbumOnlyShadow.BlurRadius = auraBlur;
            AlbumOnlyShadow.Opacity = auraOpacity;
        }

        TimeSpan currentPos = _mediaService.CurrentPosition;
        TimeSpan duration = _mediaService.CurrentTrack.Duration;

        int totalOffsetMs = _settings.SyncOffsetMs + _currentLyrics.UserOffsetMs;
        TimeSpan effectivePos = currentPos + TimeSpan.FromMilliseconds(totalOffsetMs);
        if (effectivePos < TimeSpan.Zero) effectivePos = TimeSpan.Zero;

        if (_lastRenderEffectivePos != TimeSpan.Zero)
        {
            TimeSpan posDelta = effectivePos - _lastRenderEffectivePos;
            if (Math.Abs(posDelta.TotalMilliseconds) > 250)
            {
                _currentActiveIndex = -999;
                ResetVisualsOnSeek(_lyricsOnlyVisuals, effectivePos);
                ResetVisualsOnSeek(_sideLyricsVisuals, effectivePos);
            }
        }
        _lastRenderEffectivePos = effectivePos;

        // Progress bars (zero GC allocations: mutate width directly, format text only once per second)
        double totalSec = duration.TotalSeconds;
        double currentSec = currentPos.TotalSeconds;
        double fillRatio = totalSec > 0 ? Math.Clamp(currentSec / totalSec, 0.0, 1.0) : 0.0;

        // Rounded, so the bars (and the layered window) only repaint when a step is actually visible
        SideProgressFill.Width = Math.Round(150.0 * fillRatio * 2.0) / 2.0;
        AlbumOnlyProgressFill.Width = Math.Round(165.0 * fillRatio * 2.0) / 2.0;

        int curSecInt = (int)currentPos.TotalSeconds;
        if (curSecInt != _lastDisplayedCurSec)
        {
            _lastDisplayedCurSec = curSecInt;
            string curTimeStr = FormatTime(currentPos);
            SideCurrentTime.Text = curTimeStr;
            AlbumOnlyCurrentTime.Text = curTimeStr;
        }

        int totalSecInt = (int)duration.TotalSeconds;
        if (totalSecInt != _lastDisplayedTotalSec)
        {
            _lastDisplayedTotalSec = totalSecInt;
            string totalTimeStr = FormatTime(duration);
            SideTotalTime.Text = totalTimeStr;
            AlbumOnlyTotalTime.Text = totalTimeStr;
        }

        if (!_currentLyrics.HasSyncedLyrics)
        {
            // If paused and no synced lyrics, pause rendering loop
            if (!_mediaService.IsPlaying)
            {
                StopRenderingLoop();
            }
            else
            {
                // Only the progress bar moves: four updates a second are plenty
                _nextFrameDueTimestamp = nowTicks + (Stopwatch.Frequency / 4);
            }
            return;
        }

        if (_settings.IsMoonlightBloom)
        {
            bool settled = true;
            // Game Mode renders in software, where the liquid layers are not worth their cost
            _lyricsOnlyMoonlight.LiteEffects = _settings.GamingMode;
            _sideMoonlight.LiteEffects = _settings.GamingMode;
            if (LyricsOnlyView.Visibility == Visibility.Visible)
            {
                settled = _lyricsOnlyMoonlight.UpdateFrame(dt, effectivePos, _mediaService.IsPlaying, _audioBeatService.BeatImpulse, _audioBeatService.SnareImpulse);
            }
            else if (SideBySideView.Visibility == Visibility.Visible)
            {
                settled = _sideMoonlight.UpdateFrame(dt, effectivePos, _mediaService.IsPlaying, _audioBeatService.BeatImpulse, _audioBeatService.SnareImpulse);
            }

            if (!_mediaService.IsPlaying && settled)
            {
                StopRenderingLoop();
            }
            return;
        }

        int activeIndex = FindCurrentLyricLineIndex(_currentLyrics.Lines, effectivePos);
        double currentTimeSec = (double)nowTicks / Stopwatch.Frequency;

        if (activeIndex != _currentActiveIndex)
        {
            _currentActiveIndex = activeIndex;
            _activeLineChangedTime = currentTimeSec;

            // Trigger liquid wave cascade (65ms delay per line away from active, capped at 4 lines)
            TriggerStaggeredWaveOffsets(_lyricsOnlyVisuals, activeIndex, currentTimeSec);
            TriggerStaggeredWaveOffsets(_sideLyricsVisuals, activeIndex, currentTimeSec);
        }

        bool visualsSettled = true;

        // Simulate LyricsOnly Viewport
        if (LyricsOnlyView.Visibility == Visibility.Visible && _lyricsOnlyVisuals.Count > 0)
        {
            bool settled1 = SimulateSpicyWave(
                _lyricsOnlyVisuals,
                activeIndex,
                effectivePos,
                LyricsOnlyViewport.ActualHeight,
                dt,
                currentTimeSec,
                _lyricsOnlyInterludeSlot);
            if (!settled1) visualsSettled = false;
        }

        // Simulate SideLyrics Viewport
        if (SideBySideView.Visibility == Visibility.Visible && _sideLyricsVisuals.Count > 0)
        {
            bool settled2 = SimulateSpicyWave(
                _sideLyricsVisuals,
                activeIndex,
                effectivePos,
                SideLyricsViewport.ActualHeight,
                dt,
                currentTimeSec,
                _sideLyricsInterludeSlot);
            if (!settled2) visualsSettled = false;
        }

        // 0% Idle GPU/CPU Usage:
        // When playback is paused or stopped, and all active springs have settled, disconnect the loop!
        if (!_mediaService.IsPlaying && visualsSettled)
        {
            StopRenderingLoop();
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"OnCompositionRendering error: {ex.Message}");
    }
}

    private void TriggerStaggeredWaveOffsets(List<LyricLineVisualItem> visuals, int activeIndex, double currentTimeSec)
    {
        FluidLyricWaveHelper.TriggerStaggeredWaveOffsets(visuals, activeIndex, currentTimeSec);
    }

    private bool SimulateSpicyWave(
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

        // Adaptive Visible Lines Determination
        int maxDist = DetermineMaxLineDistance(viewportHeight);
        bool isCompact = maxDist <= 1;

        // Anchor active line vertical center strictly to viewport height * 0.42 (or 0.50 in compact)
        double anchorRatio = isCompact ? 0.50 : 0.42;
        double targetCenterY_A = viewportHeight * anchorRatio;

        int refIndex = Math.Clamp(activeIndex >= 0 ? activeIndex : 0, 0, visuals.Count - 1);
        double targetTop_A = targetCenterY_A - (visuals[refIndex].MeasuredHeight / 2.0);

        // Sleep Settled Springs: Only update physics for lines within visible window (activeLineIndex ± 4)
        int minVisibleIdx = Math.Max(0, activeIndex - 4);
        int maxVisibleIdx = Math.Min(visuals.Count - 1, activeIndex + 4);

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

            // True untransformed cumulative delta
            line.TargetY = targetTop_A + (line.StaticTop - visuals[refIndex].StaticTop);

            // Target opacity and scale for Fluid Wave
            double targetOpacity = 0.0;
            double targetScale = 0.85;

            if (i == activeIndex)
            {
                targetOpacity = 1.0;
                targetScale = 1.045;
            }
            else if (dist <= maxDist)
            {
                if (dist == 1)
                {
                    targetOpacity = isCompact ? 0.32 : 0.36;
                    targetScale = 0.92;
                }
                else if (dist == 2)
                {
                    targetOpacity = 0.16;
                    targetScale = 0.86;
                }
                else
                {
                    targetOpacity = 0.08;
                    targetScale = 0.82;
                }
            }

            // Spring simulation after staggered wave ripple delay
            if (currentTimeSec >= line.StartTimeOffset)
            {
                bool settledY;
                if (dt > 0.25)
                {
                    // Snap directly on extreme lag spikes (>250ms) to eliminate slow-motion desync
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
                bool wasSettled = line.IsSettled;
                line.IsSettled = settledY && settledOpacity && settledScale;

                // Dirty-rect repaint optimization:
                // Skip mutating WPF visual properties for settled inactive lines
                if (!(wasSettled && line.IsSettled && i != activeIndex))
                {
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
            }
            else
            {
                line.IsSettled = false;
            }

            if (!line.IsSettled)
            {
                allSettled = false;
            }

            // Word-by-Word karaoke swipe & expressive bounce
            if (i == activeIndex && line.Words != null && line.Words.Count > 0)
            {
                bool wordsSettled = UpdateWordWave(line, effectivePos, dt, isFluid: true);
                if (!wordsSettled) allSettled = false;
            }
            else if (line.Words != null && line.Words.Count > 0)
            {
                UpdateInactiveWords(line, isPast: i < activeIndex, effectivePos, dt);
            }
        }

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
        InterludeSlotItem? slot)
    {
        if (slot == null) return false;

        bool isBreak = false;
        var lines = _currentLyrics.Lines;
        TimeSpan breakStart = TimeSpan.Zero;
        TimeSpan breakEnd = TimeSpan.Zero;

        if (activeIndex < 0 && lines.Count > 0)
        {
            if (lines[0].Timestamp - effectivePos >= TimeSpan.FromSeconds(2.0))
            {
                isBreak = true;
                breakStart = TimeSpan.Zero;
                breakEnd = lines[0].Timestamp;
            }
        }
        else if (activeIndex >= 0 && activeIndex + 1 < lines.Count)
        {
            TimeSpan currentEnd = lines[activeIndex].EndTime;
            TimeSpan nextStart = lines[activeIndex + 1].Timestamp;

            if (nextStart - currentEnd >= TimeSpan.FromSeconds(4.0) && effectivePos >= currentEnd && effectivePos <= nextStart)
            {
                isBreak = true;
                breakStart = currentEnd;
                breakEnd = nextStart;
            }
        }

        if (isBreak)
        {
            double totalBreakSec = (breakEnd - breakStart).TotalSeconds;
            double elapsedBreakSec = (effectivePos - breakStart).TotalSeconds;
            double remainingSec = (breakEnd - effectivePos).TotalSeconds;
            double progress = totalBreakSec > 0 ? Math.Clamp(elapsedBreakSec / totalBreakSec, 0.0, 1.0) : 0.0;

            // In the last 350ms of break: smoothly shrink into nothingness before first vocal hits
            if (remainingSec <= 0.35)
            {
                double collapseP = Math.Clamp(remainingSec / 0.35, 0.0, 1.0);
                slot.CurrentScale += (collapseP - slot.CurrentScale) * Math.Min(1.0, 20.0 * dt);
                slot.CurrentOpacity += (collapseP - slot.CurrentOpacity) * Math.Min(1.0, 20.0 * dt);
            }
            else
            {
                // Organic breathing sine-wave expansion while waiting
                double breathScale = 1.0 + (0.09 * Math.Sin(currentTimeSec * 2.4));
                slot.CurrentScale += (breathScale - slot.CurrentScale) * Math.Min(1.0, 10.0 * dt);
                slot.CurrentOpacity += (1.0 - slot.CurrentOpacity) * Math.Min(1.0, 12.0 * dt);
            }

            slot.ContainerScale.ScaleX = slot.CurrentScale;
            slot.ContainerScale.ScaleY = slot.CurrentScale;
            slot.Container.Opacity = slot.CurrentOpacity;
            slot.Container.Visibility = slot.CurrentOpacity > 0.01 ? Visibility.Visible : Visibility.Collapsed;

            // Orbs progressive fill and organic float
            double p1 = Math.Clamp(progress / 0.33, 0.0, 1.0);
            slot.Dot1.Opacity = 0.35 + (0.65 * p1);
            slot.Dot1Transform.Y = -3.5 * Math.Sin(p1 * Math.PI) + (1.0 * Math.Sin(currentTimeSec * 2.6));
            double d1Scale = 1.0 + (0.12 * Math.Sin(currentTimeSec * 2.6));
            slot.Dot1Scale.ScaleX = d1Scale; slot.Dot1Scale.ScaleY = d1Scale;

            double p2 = Math.Clamp((progress - 0.33) / 0.33, 0.0, 1.0);
            slot.Dot2.Opacity = 0.35 + (0.65 * p2);
            slot.Dot2Transform.Y = -3.5 * Math.Sin(p2 * Math.PI) + (1.0 * Math.Sin(currentTimeSec * 2.6 - 0.5));
            double d2Scale = 1.0 + (0.12 * Math.Sin(currentTimeSec * 2.6 - 0.5));
            slot.Dot2Scale.ScaleX = d2Scale; slot.Dot2Scale.ScaleY = d2Scale;

            double p3 = Math.Clamp((progress - 0.66) / 0.34, 0.0, 1.0);
            slot.Dot3.Opacity = 0.35 + (0.65 * p3);
            slot.Dot3Transform.Y = -3.5 * Math.Sin(p3 * Math.PI) + (1.0 * Math.Sin(currentTimeSec * 2.6 - 1.0));
            double d3Scale = 1.0 + (0.12 * Math.Sin(currentTimeSec * 2.6 - 1.0));
            slot.Dot3Scale.ScaleX = d3Scale; slot.Dot3Scale.ScaleY = d3Scale;

            int refIdx = Math.Clamp(activeIndex >= 0 ? activeIndex : 0, 0, visuals.Count - 1);
            double targetY = targetTop_A + (slot.StaticTop - visuals[refIdx].StaticTop);
            if (dt > 0.25)
            {
                slot.CurrentY = targetY;
            }
            else
            {
                slot.CurrentY += (targetY - slot.CurrentY) * Math.Min(1.0, 14.0 * dt);
            }
            Canvas.SetTop(slot.Container, slot.CurrentY);
            slot.IsActive = true;
            return true;
        }
        else
        {
            slot.Container.Visibility = Visibility.Collapsed;
            slot.CurrentOpacity = 0.0;
            slot.CurrentScale = 0.0;
            slot.IsActive = false;
            return false;
        }
    }

    private int DetermineMaxLineDistance(double viewportHeight)
    {
        string linesMode = _settings.VisibleLinesMode;
        if (linesMode == "2") return 1;
        if (linesMode == "3") return 1;
        if (linesMode == "5") return 2;

        if (viewportHeight < 200) return 1;
        if (viewportHeight < 300) return 1;
        if (viewportHeight < 420) return 2;
        return 3;
    }

    private static int FindCurrentLyricLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan current)
    {
        if (lines.Count == 0) return -1;
        if (current < lines[0].Timestamp) return -1;

        int low = 0;
        int high = lines.Count - 1;
        int result = 0;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (lines[mid].Timestamp <= current)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    // Track change handler and ambient backdrop

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

}
