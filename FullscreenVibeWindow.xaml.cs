using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LyricalWidget.Audio;
using LyricalWidget.Common;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;
using LyricalWidget.Rendering;
using LyricalWidget.Services;

namespace LyricalWidget;

public enum KineticPreset
{
    MoonlightBloom,
    LiveCode,
    Editorial,
    Poster,
    SpotlightFog
}

public partial class FullscreenVibeWindow : Window
{
    private readonly SmtcMediaService _mediaService;
    private readonly LyricsWaterfallService _lyricsService;
    private readonly AudioBeatService _audioBeatService;
    private readonly AppSettings _settings;
    private readonly KineticSpotlightFogRenderer _spotlightFogRenderer = new();
    private readonly KineticLiveCodeRenderer _liveCodeRenderer = new();
    private readonly MoonlightBloomRenderer _moonlightRenderer;
    private readonly FisheyeLens _fisheyeLens;
    private readonly HighResArtworkService _highResArtworkService = new();
    private LrcDocument _currentLyrics = LrcDocument.Empty();
    private bool _isClosing = false;
    private bool _isAudioReactiveEnabled = true;

    private long _lastFrameTimestamp = 0;
    private double _timeElapsedSec = 0;
    private double _currentBassEnergy = 0;
    private double _currentBeatImpulse = 0;
    private int _lastDisplayedCurSec = -1;
    private int _lastDisplayedTotalSec = -1;

    private readonly Random _shakeRng = new();
    private double _shakeOffsetX = 0.0;
    private double _shakeOffsetY = 0.0;

    private bool _isPortraitMode = false;
    private bool _isInstrumentalMode = false;
    private bool _isKineticMode = false;

    public FullscreenVibeWindow(
        SmtcMediaService mediaService,
        LyricsWaterfallService lyricsService,
        AudioBeatService audioBeatService,
        AppSettings settings,
        LrcDocument? initialLyrics = null,
        TrackInfo? initialTrack = null,
        VibePalette? initialPalette = null)
    {
        InitializeComponent();

        var wpfIcon = IconHelper.LoadWpfIcon();
        if (wpfIcon != null)
        {
            Icon = wpfIcon;
        }

        _moonlightRenderer = new MoonlightBloomRenderer(MoonlightCanvas, showInterludeNote: false);
        _fisheyeLens = new FisheyeLens(MoonlightLensViewport, MoonlightStage);

        _mediaService = mediaService;
        _lyricsService = lyricsService;
        _audioBeatService = audioBeatService;
        _settings = settings;

        if (initialPalette != null)
        {
            _currentPalette = initialPalette;
        }

        if (initialLyrics != null && initialLyrics.HasSyncedLyrics)
        {
            _currentLyrics = initialLyrics;
        }

        Loaded += OnLoaded;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        MouseMove += OnWindowMouseMove;
        SizeChanged += OnWindowSizeChanged;

        _volumeService = new SystemVolumeService();
        _volumeService.VolumeChanged += OnSystemVolumeChanged;

        _isKineticMode = false;
        _kineticVibe = _settings.FullscreenKineticVibe ?? "Auto";
        if (Enum.TryParse<KineticPreset>(_settings.FullscreenKineticPreset, true, out var savedPreset))
        {
            _currentKineticPreset = savedPreset;
        }
        else
        {
            _currentKineticPreset = KineticPreset.MoonlightBloom;
        }

        // Moonlight Bloom picked as the lyric style carries straight into fullscreen
        _isKineticMode = _settings.IsMoonlightBloom && _currentKineticPreset == KineticPreset.MoonlightBloom;

        InitModeBarTimer();
        InitHoverMenus();
        InitHoverControls();
        _layoutDirector.IsPosterDimMode = _settings.PosterDimMode;
        UpdateAudioReactiveButtonLabel();
        UpdateLayoutModeButtonLabel();
        UpdateKineticPresetButtonLabel();
        UpdateVibeButtonLabel();

        _spotlightFogRenderer.Initialize(
            SpotlightArtworkGroup,
            SpotlightSharpImage,
            SpotlightSharpScale,
            SpotlightBlurredImage,
            SpotlightBlurredScale,
            SpotlightFogMask,
            SpotlightLyricsCanvas);
        _spotlightFogRenderer.SetBackdropMode(_settings.SpotlightBackdropMode ?? "HdArtwork");

        _liveCodeRenderer.Initialize(
            LiveCodeArtworkGroup,
            LiveCodeBackdropImage,
            LiveCodeBackdropScale,
            LiveCodeBackdropParallax,
            LiveCodeViewportCanvas,
            LiveCodeEditorCanvas,
            LiveCodeCameraScale,
            LiveCodeCameraTranslate,
            LiveCodeHighlightsCanvas,
            LiveCodeLinesCanvas,
            LiveCodeCaretCanvas,
            LiveCodeCaretBlock,
            LiveCodeArtistPill,
            LiveCodeArtistText,
            LiveCodeDoodleCanvas,
            LiveCodeFindPill,
            LiveCodeFindText);

        BtnToggleThemeLyrics.Content = $"Theme Lyrics: {(_settings.ThemeColoredLyrics ? "ON" : "OFF")}";
        _currentThemedLyricColors = ComputeCurrentLyricColors(_currentPalette, _settings.ThemeColoredLyrics);

        var trackToUse = initialTrack ?? _mediaService.CurrentTrack;
        if (trackToUse != null && trackToUse.IsValid)
        {
            ApplyTrackMetadata(trackToUse);
            if (trackToUse.Thumbnail != null)
            {
                ApplyPaletteImmediate(_currentPalette);
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Gaming Mode may have put the process on software rendering
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.RenderMode = RenderMode.Default;
        }

        InitWindowing();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastFrameTimestamp = Stopwatch.GetTimestamp();

        _mediaService.TrackChanged += OnTrackChanged;
        _mediaService.PlaybackStateChanged += OnPlaybackStateChanged;
        _mediaService.TimelineResynced += OnTimelineResynced;
        _audioBeatService.BeatUpdated += OnBeatUpdated;

        ApplyTrackMetadata(_mediaService.CurrentTrack);
        UpdatePlayPauseIcon(_mediaService.IsPlaying);

        if (_currentLyrics.HasSyncedLyrics)
        {
            RebuildLyricsViews();
            SetInstrumentalMode(false);
        }
        else if (_mediaService.CurrentTrack.IsValid)
        {
            OnTrackChanged(this, _mediaService.CurrentTrack);
        }
        else
        {
            SetInstrumentalMode(true);
        }

        ApplyLayoutMode(_isKineticMode);
        UpdateResponsiveLayout();
        UpdateMaximizedInset();
        InitAmbient();

        CompositionTarget.Rendering += OnCompositionRendering;

        // The beat effects need loopback audio even with Gaming Mode on
        _audioBeatService.UpdateState(_mediaService.IsPlaying, isGamingMode: false, _isAudioReactiveEnabled);
    }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdatePlayPauseIcon(isPlaying);
            UpdateAmbientForPlayback(isPlaying);
            _audioBeatService.UpdateState(isPlaying, isGamingMode: false, _isAudioReactiveEnabled);
        });
    }

    private void OnBeatUpdated(double bassEnergy, double beatImpulse)
    {
        _currentBassEnergy = bassEnergy;
        _currentBeatImpulse = beatImpulse;

        // A strong kick jolts the background orbs, and only those
        if (beatImpulse > 0.35)
        {
            double joltMagnitude = 10.0 + ((beatImpulse - 0.35) / 0.65) * 12.0;
            double angle = _shakeRng.NextDouble() * Math.PI * 2.0;
            _shakeOffsetX = Math.Cos(angle) * joltMagnitude;
            _shakeOffsetY = Math.Sin(angle) * joltMagnitude;
        }
    }

    private static readonly Geometry PlayGeometry = Geometry.Parse("M8 5v14l11-7z");
    private static readonly Geometry PauseGeometry = Geometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z");
    private static readonly Geometry SpeakerLoudGeometry = Geometry.Parse("M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z");
    private static readonly Geometry SpeakerLowGeometry = Geometry.Parse("M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z");
    private static readonly Geometry MuteIconGeometry = Geometry.Parse("M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z");

    private void UpdatePlayPauseIcon(bool isPlaying)
    {
        var geom = isPlaying ? PauseGeometry : PlayGeometry;
        PlayPauseIcon.Data = geom;
        PortraitPlayPauseIcon.Data = geom;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _prebuiltNextPhrase = null;
        _prebuiltNextIndex = -1;
        _prebuiltEpoch = -1;

        // Only the cheap work happens per pixel of a drag. Everything that measures text waits for OnResizeSettled.
        BeginResize();
        TrackNormalRect();
        UpdateResponsiveLayout();

        bool isPortrait = UsePortraitLayout(ActualWidth, ActualHeight);
        if (isPortrait != _isPortraitMode)
        {
            _isPortraitMode = isPortrait;
            if (!_isKineticMode)
            {
                LandscapeLayout.Visibility = isPortrait ? Visibility.Collapsed : Visibility.Visible;
                PortraitLayout.Visibility = isPortrait ? Visibility.Visible : Visibility.Collapsed;
                RebuildLyricsViews();
            }
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_isClosing) return;

        long nowTicks = Stopwatch.GetTimestamp();
        if (UpdateAmbientFrame(nowTicks)) return;
        if (SkipFrameWhileResizing(nowTicks)) return;

        double rawDt = _lastFrameTimestamp > 0
            ? (double)(nowTicks - _lastFrameTimestamp) / Stopwatch.Frequency
            : 0.016;
        _lastFrameTimestamp = nowTicks;
        double dt = Math.Clamp(rawDt, 0.001, 0.033);

        double speedSurge = 1.0 + (_currentBassEnergy * 0.40);
        _timeElapsedSec += dt * speedSurge;
        double t = _timeElapsedSec;
        double bass = _currentBassEnergy;
        double impulse = _currentBeatImpulse;

        double shakeDecay = Math.Exp(-dt / 0.07);
        _shakeOffsetX *= shakeDecay;
        _shakeOffsetY *= shakeDecay;
        if (Math.Abs(_shakeOffsetX) < 0.2) _shakeOffsetX = 0.0;
        if (Math.Abs(_shakeOffsetY) < 0.2) _shakeOffsetY = 0.0;

        CanvasShakeTransform.X = _shakeOffsetX;
        CanvasShakeTransform.Y = _shakeOffsetY;

        double winW = Math.Max(800, ActualWidth);
        double winH = Math.Max(600, ActualHeight);
        double cx = winW * 0.5 - 500;
        double cy = winH * 0.5 - 500;

        double baseOrbDim = Math.Max(winW, winH);
        double swell = 1.0 + (bass * 0.15) + (impulse * 0.09);
        double bloom = (bass * 0.10) + (impulse * 0.08);

        // Orb 0 (Primary)
        Orb0Translate.X = cx + (winW * 0.28) * Math.Sin(t * 0.35);
        Orb0Translate.Y = cy + (winH * 0.22) * Math.Cos(t * 0.40);
        double s0 = (baseOrbDim * 1.10 / 1000.0) * swell;
        Orb0Scale.ScaleX = s0; Orb0Scale.ScaleY = s0;
        Orb0.Opacity = Math.Clamp(0.78 + bloom, 0.55, 0.96);

        // Orb 1 (Secondary)
        Orb1Translate.X = cx + (winW * 0.32) * Math.Cos(t * 0.42);
        Orb1Translate.Y = cy + (winH * 0.26) * Math.Sin(t * 0.32);
        double s1 = (baseOrbDim * 0.98 / 1000.0) * swell;
        Orb1Scale.ScaleX = s1; Orb1Scale.ScaleY = s1;
        Orb1.Opacity = Math.Clamp(0.70 + bloom, 0.50, 0.92);

        // Orb 2 (Accent)
        Orb2Translate.X = cx + (winW * 0.25) * Math.Sin(t * 0.48 + 1.2);
        Orb2Translate.Y = cy + (winH * 0.28) * Math.Cos(t * 0.36 + 0.8);
        double s2 = (baseOrbDim * 0.90 / 1000.0) * swell;
        Orb2Scale.ScaleX = s2; Orb2Scale.ScaleY = s2;
        Orb2.Opacity = Math.Clamp(0.68 + bloom, 0.48, 0.90);

        // Orb 3 (Highlight)
        Orb3Translate.X = cx + (winW * 0.35) * Math.Cos(t * 0.30 + 2.1);
        Orb3Translate.Y = cy + (winH * 0.20) * Math.Sin(t * 0.44 + 1.5);
        double s3 = (baseOrbDim * 0.82 / 1000.0) * swell;
        Orb3Scale.ScaleX = s3; Orb3Scale.ScaleY = s3;
        Orb3.Opacity = Math.Clamp(0.62 + bloom, 0.42, 0.85);

        // Orb 4 (Warm Glow)
        Orb4Translate.X = cx + (winW * 0.22) * Math.Sin(t * 0.52 + 3.0);
        Orb4Translate.Y = cy + (winH * 0.24) * Math.Cos(t * 0.50 + 2.2);
        double s4 = (baseOrbDim * 0.75 / 1000.0) * swell;
        Orb4Scale.ScaleX = s4; Orb4Scale.ScaleY = s4;
        Orb4.Opacity = Math.Clamp(0.58 + bloom, 0.38, 0.82);

        double glowBlur = 52.0 + (bass * 26.0);
        double glowOpacity = 0.55 + (bass * 0.35);
        AlbumAuraGlow.BlurRadius = glowBlur;
        AlbumAuraGlow.Opacity = glowOpacity;
        PortraitAlbumAuraGlow.BlurRadius = glowBlur * 0.7;
        PortraitAlbumAuraGlow.Opacity = glowOpacity;

        TimeSpan currentPos = _mediaService.CurrentPosition;
        TimeSpan duration = _mediaService.CurrentTrack.Duration;
        double totalSec = duration.TotalSeconds;
        double currentSec = currentPos.TotalSeconds;
        double fillRatio = totalSec > 0 ? Math.Clamp(currentSec / totalSec, 0.0, 1.0) : 0.0;

        ProgressBarFill.Width = 380.0 * fillRatio;
        PortraitProgressBarFill.Width = 280.0 * fillRatio;
        KineticCornerProgressFill.Width = 190.0 * fillRatio;

        int curSecInt = (int)currentSec;
        if (curSecInt != _lastDisplayedCurSec)
        {
            _lastDisplayedCurSec = curSecInt;
            CurrentTimeText.Text = FormatTime(currentPos);
        }

        int totalSecInt = (int)totalSec;
        if (totalSecInt != _lastDisplayedTotalSec)
        {
            _lastDisplayedTotalSec = totalSecInt;
            TotalTimeText.Text = FormatTime(duration);
        }

        if (_isKineticMode && BottomMarqueeCanvas.Visibility == Visibility.Visible)
        {
            double shift = (_timeElapsedSec * 55.0) % _marqueeSingleItemWidth;
            MarqueeTranslate.X = -shift;
        }

        int totalOffsetMs = _settings.SyncOffsetMs + _currentLyrics.UserOffsetMs;
        TimeSpan effectivePos = currentPos + TimeSpan.FromMilliseconds(totalOffsetMs);
        if (effectivePos < TimeSpan.Zero) effectivePos = TimeSpan.Zero;

        // Clear the way as soon as the first line is due
        if (_isIntroVisible && _currentLyrics.HasSyncedLyrics &&
            effectivePos >= _currentLyrics.Lines[0].Timestamp - TimeSpan.FromMilliseconds(320))
        {
            DismissIntro();
        }

        if (!_currentLyrics.HasSyncedLyrics) return;

        int activeIndex = FindCurrentLyricLineIndex(_currentLyrics.Lines, effectivePos);
        double currentTimeSec = (double)nowTicks / Stopwatch.Frequency;

        if (_isKineticMode)
        {
            UpdateKineticFrame(dt, nowTicks, effectivePos, currentTimeSec);
        }
        else
        {
            UpdateSplitFrame(dt, nowTicks, effectivePos, activeIndex, currentTimeSec);
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        CompositionTarget.Rendering -= OnCompositionRendering;

        _lyricsFetchCts?.Cancel();
        HideIntro();
        ShutdownAmbient();
        ShutdownWindowing();
        _modeBarTimer?.Stop();
        _menuLeaveTimer.Stop();
        HidePacingPill();
        _hoverLeaveTimer?.Stop();

        _volumeService?.Dispose();
        _volumeService = null;

        _mediaService.TrackChanged -= OnTrackChanged;
        _mediaService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _mediaService.TimelineResynced -= OnTimelineResynced;
        _audioBeatService.BeatUpdated -= OnBeatUpdated;

        _worldPhrases.Clear();
        KineticWorldCanvas.Children.Clear();
    }
}
