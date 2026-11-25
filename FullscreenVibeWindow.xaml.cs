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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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
    Editorial,
    Poster,
    SpotlightFog,
    LiveCode
}

public partial class FullscreenVibeWindow : Window
{
    private readonly SmtcMediaService _mediaService;
    private readonly LyricsWaterfallService _lyricsService;
    private readonly AudioBeatService _audioBeatService;
    private readonly AppSettings _settings;
    private readonly KineticSpotlightFogRenderer _spotlightFogRenderer = new();
    private readonly KineticLiveCodeRenderer _liveCodeRenderer = new();
    private readonly HighResArtworkService _highResArtworkService = new();
    private CancellationTokenSource? _hdArtFetchCts;

    public HighResArtworkService HighResArtwork => _highResArtworkService;
    public BitmapSource? CurrentHdArtwork => _highResArtworkService.CurrentHdArtwork;

    private int _paletteUpdateId = 0;
    private LrcDocument _currentLyrics = LrcDocument.Empty();
    private CancellationTokenSource? _lyricsFetchCts;
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

    private VibePalette _currentPalette = VibePalette.Default();
    private ThemedLyricColors _currentThemedLyricColors = new()
    {
        ActiveColor = Colors.White,
        InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
        AccentGlowColor = Color.FromRgb(0x60, 0xA5, 0xFA)
    };

    private bool _isPortraitMode = false;
    private bool _isInstrumentalMode = false;
    private bool _isKineticMode = false;
    private string _kineticVibe = "Auto";
    private KineticPreset _currentKineticPreset = KineticPreset.Editorial;
    private string _kineticPreset => _currentKineticPreset.ToString();
    private bool _isIntroHeroCardVisible = false;
    private bool _isLyricsFetching = false;
    private double _marqueeSingleItemWidth = 1000.0;

    private ThemedLyricColors ComputeCurrentLyricColors(VibePalette palette, bool enabled)
    {
        if (enabled)
        {
            return ColorExtractor.ComputeThemedLyricColors(palette, true);
        }

        return new ThemedLyricColors
        {
            ActiveColor = Colors.White,
            InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), // #66FFFFFF when OFF
            AccentGlowColor = Color.FromRgb(0x60, 0xA5, 0xFA)
        };
    }

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

        // Keep Split as the safe default mode when opening Fullscreen
        _isKineticMode = false;
        _kineticVibe = _settings.FullscreenKineticVibe ?? "Auto";
        if (Enum.TryParse<KineticPreset>(_settings.FullscreenKineticPreset, true, out var savedPreset))
        {
            _currentKineticPreset = savedPreset;
        }
        else
        {
            _currentKineticPreset = KineticPreset.Editorial;
        }

        InitModeBarTimer();
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

        // Hydrate immediately from passed-in track
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

        // Hardware GPU RenderMode enforcement for butter-smooth fullscreen presentation
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
        {
            source.CompositionTarget.RenderMode = RenderMode.Default;
        }
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

        // Build lyrics immediately if already present
        if (_currentLyrics.HasSyncedLyrics)
        {
            RebuildLyricsViews();
            SetInstrumentalMode(false);
        }
        else if (_mediaService.CurrentTrack.IsValid)
        {
            // Fetch lyrics if not pre-hydrated
            OnTrackChanged(this, _mediaService.CurrentTrack);
        }
        else
        {
            SetInstrumentalMode(true);
        }

        ApplyLayoutMode(_isKineticMode);

        // Start render loop
        CompositionTarget.Rendering += OnCompositionRendering;

        // Automatically start audio beat loopback in Fullscreen Vibe mode (overriding Gaming Mode)
        _audioBeatService.UpdateState(_mediaService.IsPlaying, isGamingMode: false, _isAudioReactiveEnabled);
    }

    private void OnTimelineResynced(object? sender, TimeSpan pos)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_isKineticMode)
            {
                _kineticRenderEpoch++;
                _worldPhrases.Clear();
                KineticWorldCanvas.Children.Clear();
                _prebuiltNextPhrase = null;
                _prebuiltNextIndex = -1;
                _prebuiltEpoch = -1;
                _layoutDirector.Reset();
                _cameraController.Reset(new Point(0, 0));
                _currentKineticActiveIndex = -1;
                _lastKineticPosition = TimeSpan.Zero;
                _lastKineticClockMs = 0.0;
            }
        });
    }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdatePlayPauseIcon(isPlaying);
            _audioBeatService.UpdateState(isPlaying, isGamingMode: false, _isAudioReactiveEnabled);
        });
    }

    private void OnBeatUpdated(double bassEnergy, double beatImpulse)
    {
        _currentBassEnergy = bassEnergy;
        _currentBeatImpulse = beatImpulse;

        // Subtle Beat Micro-Shake on strong kick/bass transients (>0.35)
        // Damped jolt between 10px and 22px in random direction (ONLY on BackgroundOrbsCanvas)
        if (beatImpulse > 0.35)
        {
            double joltMagnitude = 10.0 + ((beatImpulse - 0.35) / 0.65) * 12.0; // 10.0px to 22.0px
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

    private void ApplyTrackMetadata(TrackInfo track)
    {
        MarqueeStack.Children.Clear();

        if (!track.IsValid)
        {
            TrackTitleText.Text = "No Track Playing";
            TrackArtistText.Text = "Play music in Spotify or browser";
            PortraitTrackTitleText.Text = "No Track Playing";
            PortraitTrackArtistText.Text = "Play music in Spotify or browser";
            KineticCornerTitle.Text = "No Track Playing";
            KineticCornerArtist.Text = "Play music in Spotify or browser";
            IntroHeroTitle.Text = "NO TRACK PLAYING";
            IntroHeroArtist.Text = "SELECT MUSIC";
            AlbumCoverImage.Source = null;
            PortraitAlbumCoverImage.Source = null;
            KineticCornerImage.Source = null;
            AlbumFallbackIcon.Visibility = Visibility.Visible;
            PortraitAlbumFallbackIcon.Visibility = Visibility.Visible;
            ProgressBarFill.Width = 0;
            PortraitProgressBarFill.Width = 0;
            KineticCornerProgressFill.Width = 0;
            CurrentTimeText.Text = "0:00";
            TotalTimeText.Text = "0:00";
            return;
        }

        TrackTitleText.Text = track.Title;
        TrackArtistText.Text = track.Artist;
        PortraitTrackTitleText.Text = track.Title;
        PortraitTrackArtistText.Text = track.Artist;
        KineticCornerTitle.Text = track.Title;
        KineticCornerArtist.Text = track.Artist;
        IntroHeroTitle.Text = track.Title.ToUpperInvariant();
        IntroHeroArtist.Text = track.Artist.ToUpperInvariant();
        TotalTimeText.Text = FormatTime(track.Duration);

        // Build continuous Giant Faint Outlined Title Marquee paths
        string marqueeUnit = $"{track.Title.ToUpperInvariant()}   —   ";
        var marqueeTypeface = new Typeface(new FontFamily("Segoe UI Black, Impact, Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
        var size = MeasureMarqueeText(marqueeUnit, marqueeTypeface, 96.0);
        _marqueeSingleItemWidth = Math.Max(200.0, size.Width);

        for (int i = 0; i < 6; i++)
        {
            var path = CreateMarqueeTextPath(
                marqueeUnit, marqueeTypeface, 96.0, Brushes.White, 2.0);
            MarqueeStack.Children.Add(path);
        }

        if (track.Thumbnail != null)
        {
            AlbumCoverImage.Source = track.Thumbnail;
            PortraitAlbumCoverImage.Source = track.Thumbnail;
            KineticCornerImage.Source = track.Thumbnail;
            AlbumFallbackIcon.Visibility = Visibility.Collapsed;
            PortraitAlbumFallbackIcon.Visibility = Visibility.Collapsed;
            _spotlightFogRenderer.UpdateAlbumArt(track.Thumbnail, _currentPalette);
            _liveCodeRenderer.UpdateTrack(track);
            _liveCodeRenderer.UpdateAlbumArt(track.Thumbnail, _currentPalette);
        }
        else
        {
            AlbumCoverImage.Source = null;
            PortraitAlbumCoverImage.Source = null;
            KineticCornerImage.Source = null;
            AlbumFallbackIcon.Visibility = Visibility.Visible;
            PortraitAlbumFallbackIcon.Visibility = Visibility.Visible;
            _spotlightFogRenderer.UpdateAlbumArt(null, _currentPalette);
            _liveCodeRenderer.UpdateTrack(track);
            _liveCodeRenderer.UpdateAlbumArt(null, _currentPalette);
        }

        // Asynchronously fetch 1200x1200 HD artwork via keyless iTunes/Deezer APIs
        _hdArtFetchCts?.Cancel();
        _hdArtFetchCts = new CancellationTokenSource();
        var ct = _hdArtFetchCts.Token;
        _ = Task.Run(async () =>
        {
            var hdArt = await _highResArtworkService.FetchHighResArtworkAsync(track.Artist, track.Title, ct);
            if (hdArt != null && !ct.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        AlbumCoverImage.Source = hdArt;
                        PortraitAlbumCoverImage.Source = hdArt;
                        KineticCornerImage.Source = hdArt;
                        _spotlightFogRenderer.UpdateHdAlbumArt(hdArt);
                        _liveCodeRenderer.UpdateHdAlbumArt(hdArt);
                    }
                });
            }
        }, ct);
    }

    private static Path CreateMarqueeTextPath(
        string text,
        Typeface typeface,
        double fontSize,
        Brush strokeBrush,
        double strokeThickness)
    {
        if (string.IsNullOrEmpty(text)) text = " ";
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            1.0);

        Geometry geom = formatted.BuildGeometry(new Point(0, 0));
        geom.Freeze();

        var path = new Path
        {
            Data = geom,
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
            Fill = Brushes.Transparent,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetEdgeMode(path, EdgeMode.Unspecified);
        return path;
    }

    private static Size MeasureMarqueeText(string text, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return new Size(0, fontSize);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            1.0);
        return new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }

    public void UpdateTrackFromMainWindow(TrackInfo track)
    {
        OnTrackChanged(this, track);
    }

    private void OnTrackChanged(object? sender, TrackInfo track)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            _lyricsFetchCts?.Cancel();
            _lyricsFetchCts = new CancellationTokenSource();
            var ct = _lyricsFetchCts.Token;

            _kineticRenderEpoch++;
            _currentActiveIndex = -1;
            _lastVibeEffectivePos = TimeSpan.Zero;
            _currentKineticActiveIndex = -1;
            _lastKineticPosition = TimeSpan.Zero;
            _lastKineticClockMs = 0.0;
            _prebuiltNextPhrase = null;
            _prebuiltNextIndex = -1;
            _prebuiltEpoch = -1;
            _isLyricsFetching = true;

            _worldPhrases.Clear();
            KineticWorldCanvas.Children.Clear();
            _layoutDirector.Reset();
            _cameraController.Reset(new Point(0, 0));

            if (!track.IsValid)
            {
                _isLyricsFetching = false;
                ApplyTrackMetadata(track);
                _currentLyrics = LrcDocument.Empty();
                RebuildLyricsViews();
                SetInstrumentalMode(true);
                ApplyLayoutMode(_isKineticMode);
                CrossfadePalette(VibePalette.Default());
                return;
            }

            ApplyTrackMetadata(track);

            int currentId = Interlocked.Increment(ref _paletteUpdateId);

            if (track.Thumbnail != null)
            {
                // Guarantee extraction runs directly from decoded BitmapSource right after album art is set
                var palette = ColorExtractor.ExtractPalette(track.Thumbnail);
                CrossfadePalette(palette);
            }
            else
            {
                CrossfadePalette(VibePalette.Default());
            }

            // Background waterfall lyrics fetch
            var doc = await _lyricsService.GetLyricsAsync(track, upgradedDoc =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (currentId == _paletteUpdateId && !ct.IsCancellationRequested)
                    {
                        _currentLyrics = upgradedDoc;
                        RebuildLyricsViews();
                        SetInstrumentalMode(!upgradedDoc.HasSyncedLyrics);
                        ApplyLayoutMode(_isKineticMode);
                    }
                });
            }, ct);
            if (ct.IsCancellationRequested) return;

            _isLyricsFetching = false;
            _currentLyrics = doc;
            RebuildLyricsViews();
            SetInstrumentalMode(!doc.HasSyncedLyrics);
            ApplyLayoutMode(_isKineticMode);
        });
    }

    public void ApplyPaletteImmediate(VibePalette palette)
    {
        _currentPalette = palette;
        _spotlightFogRenderer.UpdatePalette(palette);
        _liveCodeRenderer.UpdatePalette(palette);
        _currentThemedLyricColors = ComputeCurrentLyricColors(palette, _settings.ThemeColoredLyrics);
        ApplyLyricColorsToVisuals(_vibeVisuals, _currentThemedLyricColors, crossfade: false);
        TintedBackground.Background = new SolidColorBrush(palette.DarkAnchor);

        void SetOrb(RadialGradientBrush brush, Color c)
        {
            if (brush.GradientStops.Count >= 2)
            {
                brush.GradientStops[0].Color = c;
                brush.GradientStops[1].Color = Color.FromArgb(0, c.R, c.G, c.B);
            }
        }

        if (Orb0.Fill is RadialGradientBrush b0) SetOrb(b0, palette.Primary);
        if (Orb1.Fill is RadialGradientBrush b1) SetOrb(b1, palette.Secondary);
        if (Orb2.Fill is RadialGradientBrush b2) SetOrb(b2, palette.Accent);
        if (Orb3.Fill is RadialGradientBrush b3) SetOrb(b3, palette.Highlight);
        if (Orb4.Fill is RadialGradientBrush b4) SetOrb(b4, palette.Colors.Length > 4 ? palette.Colors[4] : palette.Primary);

        AlbumAuraGlow.Color = palette.Primary;
        PortraitAlbumAuraGlow.Color = palette.Primary;

        ProgressStop0.Color = palette.Highlight;
        ProgressStop1.Color = palette.Secondary;
        PortraitProgressStop0.Color = palette.Highlight;
        PortraitProgressStop1.Color = palette.Secondary;

        IntroHeroArtistPill.Background = new SolidColorBrush(palette.Accent);
        KineticCornerStop0.Color = palette.Highlight;
        KineticCornerStop1.Color = palette.Secondary;

        VolumeFillBar.Background = new SolidColorBrush(palette.Primary);
        VolumeFillGlow.Color = palette.Primary;
        PortraitVolumeFillBar.Background = new SolidColorBrush(palette.Primary);
        PortraitVolumeFillGlow.Color = palette.Primary;
    }

    private void CrossfadePalette(VibePalette palette)
    {
        _currentPalette = palette;
        _spotlightFogRenderer.UpdatePalette(palette);
        _liveCodeRenderer.UpdatePalette(palette);
        var duration = TimeSpan.FromMilliseconds(800);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Crossfade tinted background to dark anchor
        var bgAnim = new ColorAnimation(palette.DarkAnchor, duration) { EasingFunction = ease };
        if (TintedBackground.Background is SolidColorBrush sb && !sb.IsFrozen)
        {
            sb.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
        }
        else
        {
            var brush = new SolidColorBrush(palette.DarkAnchor);
            TintedBackground.Background = brush;
        }

        void AnimateOrb(RadialGradientBrush brush, Color target)
        {
            if (brush.GradientStops.Count < 2) return;
            var c0 = new ColorAnimation(target, duration) { EasingFunction = ease };
            Color transparentTarget = Color.FromArgb(0, target.R, target.G, target.B);
            var c1 = new ColorAnimation(transparentTarget, duration) { EasingFunction = ease };

            brush.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, c0);
            brush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, c1);
        }

        if (Orb0.Fill is RadialGradientBrush b0) AnimateOrb(b0, palette.Primary);
        if (Orb1.Fill is RadialGradientBrush b1) AnimateOrb(b1, palette.Secondary);
        if (Orb2.Fill is RadialGradientBrush b2) AnimateOrb(b2, palette.Accent);
        if (Orb3.Fill is RadialGradientBrush b3) AnimateOrb(b3, palette.Highlight);
        if (Orb4.Fill is RadialGradientBrush b4) AnimateOrb(b4, palette.Colors.Length > 4 ? palette.Colors[4] : palette.Primary);

        AlbumAuraGlow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(palette.Primary, duration) { EasingFunction = ease });
        PortraitAlbumAuraGlow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(palette.Primary, duration) { EasingFunction = ease });

        // Crossfade Progress Bar
        ProgressStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Highlight, duration) { EasingFunction = ease });
        ProgressStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Secondary, duration) { EasingFunction = ease });
        PortraitProgressStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Highlight, duration) { EasingFunction = ease });
        PortraitProgressStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Secondary, duration) { EasingFunction = ease });

        // Crossfade Volume Bar Fill & Glow
        var fillAnim = new ColorAnimation(palette.Primary, duration) { EasingFunction = ease };
        if (VolumeFillBar.Background is SolidColorBrush vfb && !vfb.IsFrozen)
        {
            vfb.BeginAnimation(SolidColorBrush.ColorProperty, fillAnim);
        }
        else
        {
            var brush = new SolidColorBrush(palette.Primary);
            VolumeFillBar.Background = brush;
        }
        VolumeFillGlow.BeginAnimation(DropShadowEffect.ColorProperty, fillAnim);

        if (PortraitVolumeFillBar.Background is SolidColorBrush pvfb && !pvfb.IsFrozen)
        {
            pvfb.BeginAnimation(SolidColorBrush.ColorProperty, fillAnim);
        }
        else
        {
            var brush = new SolidColorBrush(palette.Primary);
            PortraitVolumeFillBar.Background = brush;
        }
        PortraitVolumeFillGlow.BeginAnimation(DropShadowEffect.ColorProperty, fillAnim);

        // Crossfade Intro Hero Card Artist Pill & Kinetic Corner Badge
        var accentFillAnim = new ColorAnimation(palette.Accent, duration) { EasingFunction = ease };
        if (IntroHeroArtistPill.Background is SolidColorBrush apb && !apb.IsFrozen)
        {
            apb.BeginAnimation(SolidColorBrush.ColorProperty, accentFillAnim);
        }
        else
        {
            var brush = new SolidColorBrush(palette.Accent);
            IntroHeroArtistPill.Background = brush;
        }

        KineticCornerStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Highlight, duration) { EasingFunction = ease });
        KineticCornerStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Secondary, duration) { EasingFunction = ease });

        UpdateVisualsAccentColor(_vibeVisuals, _interludeSlot, palette.Primary);
        _currentThemedLyricColors = ComputeCurrentLyricColors(palette, _settings.ThemeColoredLyrics);
        ApplyLyricColorsToVisuals(_vibeVisuals, _currentThemedLyricColors, crossfade: !_settings.GamingMode);
    }


    private void SetInstrumentalMode(bool isInstrumental)
    {
        if (_isInstrumentalMode == isInstrumental) return;
        _isInstrumentalMode = isInstrumental;

        var animDur = TimeSpan.FromMilliseconds(400);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (isInstrumental)
        {
            var fadeOutLyrics = new DoubleAnimation(0.0, animDur) { EasingFunction = ease };
            fadeOutLyrics.Completed += (s, e) => LyricsSideContainer.Visibility = Visibility.Collapsed;
            LyricsSideContainer.BeginAnimation(UIElement.OpacityProperty, fadeOutLyrics);

            LandscapeColLeft.Width = new GridLength(1.0, GridUnitType.Star);
            LandscapeColRight.Width = new GridLength(0.0, GridUnitType.Pixel);
            AlbumSideContainer.HorizontalAlignment = HorizontalAlignment.Center;
            AlbumSideContainer.Margin = new Thickness(0);

            TrackTitleText.TextAlignment = TextAlignment.Center;
            TrackArtistText.TextAlignment = TextAlignment.Center;
        }
        else
        {
            LyricsSideContainer.Visibility = Visibility.Visible;
            LyricsSideContainer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, animDur) { EasingFunction = ease });

            LandscapeColLeft.Width = new GridLength(0.44, GridUnitType.Star);
            LandscapeColRight.Width = new GridLength(0.56, GridUnitType.Star);
            AlbumSideContainer.HorizontalAlignment = HorizontalAlignment.Center;
            AlbumSideContainer.Margin = new Thickness(0, 0, 32, 0);

            TrackTitleText.TextAlignment = TextAlignment.Left;
            TrackArtistText.TextAlignment = TextAlignment.Left;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _spotlightFogRenderer.OnResize(ActualWidth, ActualHeight);
        _liveCodeRenderer.OnResize(ActualWidth, ActualHeight);
        _prebuiltNextPhrase = null;
        _prebuiltNextIndex = -1;
        _prebuiltEpoch = -1;

        bool isPortrait = ActualHeight > ActualWidth;
        if (isPortrait != _isPortraitMode)
        {
            _isPortraitMode = isPortrait;
            if (!_isKineticMode)
            {
                if (isPortrait)
                {
                    LandscapeLayout.Visibility = Visibility.Collapsed;
                    PortraitLayout.Visibility = Visibility.Visible;
                }
                else
                {
                    LandscapeLayout.Visibility = Visibility.Visible;
                    PortraitLayout.Visibility = Visibility.Collapsed;
                }
            }
            RebuildLyricsViews();
        }
        else
        {
            ComputeStaticLineLayouts();
        }
    }

    // Lyrics views and spring physics initialization

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_isClosing) return;

        long nowTicks = Stopwatch.GetTimestamp();
        double rawDt = _lastFrameTimestamp > 0
            ? (double)(nowTicks - _lastFrameTimestamp) / Stopwatch.Frequency
            : 0.016;
        _lastFrameTimestamp = nowTicks;
        double dt = Math.Clamp(rawDt, 0.001, 0.033);

        // Orbital motion with beat surges
        double speedSurge = 1.0 + (_currentBassEnergy * 0.40);
        _timeElapsedSec += dt * speedSurge;
        double t = _timeElapsedSec;
        double bass = _currentBassEnergy;
        double impulse = _currentBeatImpulse;

        // Damped micro-shake decay (~140ms exponential decay back to 0,0)
        double shakeDecay = Math.Exp(-dt / 0.07);
        _shakeOffsetX *= shakeDecay;
        _shakeOffsetY *= shakeDecay;
        if (Math.Abs(_shakeOffsetX) < 0.2) _shakeOffsetX = 0.0;
        if (Math.Abs(_shakeOffsetY) < 0.2) _shakeOffsetY = 0.0;

        // Apply shake ONLY to BackgroundOrbsCanvas
        CanvasShakeTransform.X = _shakeOffsetX;
        CanvasShakeTransform.Y = _shakeOffsetY;

        // Background fluid orbs
        double winW = Math.Max(800, ActualWidth);
        double winH = Math.Max(600, ActualHeight);
        double cx = winW * 0.5 - 500;
        double cy = winH * 0.5 - 500;

        double baseOrbDim = Math.Max(winW, winH);
        double swell = 1.0 + (bass * 0.15) + (impulse * 0.09); // Swells up to 1.24 on strong beats
        double bloom = (bass * 0.10) + (impulse * 0.08);       // Brightens up to +0.18 opacity

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

        // Bass pulse on album cover glow
        double glowBlur = 52.0 + (bass * 26.0);
        double glowOpacity = 0.55 + (bass * 0.35);
        AlbumAuraGlow.BlurRadius = glowBlur;
        AlbumAuraGlow.Opacity = glowOpacity;
        PortraitAlbumAuraGlow.BlurRadius = glowBlur * 0.7;
        PortraitAlbumAuraGlow.Opacity = glowOpacity;

        // Progress bar sync
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

        // Giant Faint Outlined Title Marquee Drift
        if (_isKineticMode && BottomMarqueeCanvas.Visibility == Visibility.Visible)
        {
            double shift = (_timeElapsedSec * 55.0) % _marqueeSingleItemWidth;
            MarqueeTranslate.X = -shift;
        }

        int totalOffsetMs = _settings.SyncOffsetMs + _currentLyrics.UserOffsetMs;
        TimeSpan effectivePos = currentPos + TimeSpan.FromMilliseconds(totalOffsetMs);
        if (effectivePos < TimeSpan.Zero) effectivePos = TimeSpan.Zero;

        // Intro Hero Card Handling (Stacked poster style during loading or instrumental opening >= 3.0s)
        bool isIntroEligible = _mediaService.CurrentTrack.IsValid &&
            (_isLyricsFetching ||
             (_isKineticMode && !_currentLyrics.HasSyncedLyrics) ||
             (_currentLyrics.HasSyncedLyrics &&
              _currentLyrics.Lines.Count > 0 &&
              _currentLyrics.Lines[0].Timestamp >= TimeSpan.FromSeconds(3.0) &&
              effectivePos < (_currentLyrics.Lines[0].Timestamp - TimeSpan.FromMilliseconds(320))));

        if (isIntroEligible)
        {
            if (!_isIntroHeroCardVisible)
            {
                _isIntroHeroCardVisible = true;
                IntroHeroCard.Visibility = Visibility.Visible;
                IntroHeroCard.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                IntroHeroScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                IntroHeroScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                IntroHeroScale.ScaleX = 1.0;
                IntroHeroScale.ScaleY = 1.0;
            }

            double heroDrift = 1.0 + Math.Sin(_timeElapsedSec * 0.6) * 0.02;
            IntroHeroScale.ScaleX = heroDrift;
            IntroHeroScale.ScaleY = heroDrift;
            IntroHeroTranslate.Y = Math.Sin(_timeElapsedSec * 0.9) * 4.0;
        }
        else if (_isIntroHeroCardVisible)
        {
            _isIntroHeroCardVisible = false;
            var exitFade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            exitFade.Completed += (s, e) =>
            {
                if (!_isIntroHeroCardVisible)
                {
                    IntroHeroCard.Visibility = Visibility.Collapsed;
                }
            };
            var exitScale = new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            IntroHeroCard.BeginAnimation(UIElement.OpacityProperty, exitFade);
            IntroHeroScale.BeginAnimation(ScaleTransform.ScaleXProperty, exitScale);
            IntroHeroScale.BeginAnimation(ScaleTransform.ScaleYProperty, exitScale);
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

    public void SetThemeColoredLyrics(bool enabled)
    {
        _settings.ThemeColoredLyrics = enabled;
        BtnToggleThemeLyrics.Content = $"Theme Lyrics: {(enabled ? "ON" : "OFF")}";
        SettingsService.Save(_settings);
        _currentThemedLyricColors = ComputeCurrentLyricColors(_currentPalette, enabled);
        ApplyLyricColorsToVisuals(_vibeVisuals, _currentThemedLyricColors, crossfade: false);
    }


    private void OnToggleLayoutModeClicked(object sender, RoutedEventArgs e)
    {
        ToggleLayoutMode();
    }

    private void ToggleLayoutMode()
    {
        if (!_isKineticMode)
        {
            ShowKineticBetaModal();
        }
        else
        {
            ApplyLayoutMode(false);
        }
    }

    private void ShowKineticBetaModal()
    {
        KineticBetaModalOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        KineticBetaModalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
    }

    private void HideKineticBetaModal()
    {
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, e) =>
        {
            if (KineticBetaModalOverlay.Opacity <= 0.05)
            {
                KineticBetaModalOverlay.Visibility = Visibility.Collapsed;
            }
        };
        KineticBetaModalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnKineticBetaModalBackdropClicked(object sender, MouseButtonEventArgs e)
    {
        HideKineticBetaModal();
    }

    private void OnKineticBetaCancelClicked(object sender, RoutedEventArgs e)
    {
        HideKineticBetaModal();
    }

    private void OnKineticBetaOkClicked(object sender, RoutedEventArgs e)
    {
        HideKineticBetaModal();
        ApplyLayoutMode(true);
    }

    private void OnToggleVibeClicked(object sender, RoutedEventArgs e)
    {
        // Cycle: Auto -> Calm -> Rap -> Pop -> Auto
        _kineticVibe = _kineticVibe switch
        {
            "Auto" => "Calm",
            "Calm" => "Rap",
            "Rap" => "Pop",
            _ => "Auto"
        };
        _settings.FullscreenKineticVibe = _kineticVibe;
        SettingsService.Save(_settings);
        UpdateVibeButtonLabel();
        _currentKineticActiveIndex = -1; // Force immediate re-render of current phrase
    }

    private void OnToggleKineticPresetClicked(object sender, RoutedEventArgs e)
    {
        var nextPreset = _currentKineticPreset switch
        {
            KineticPreset.Editorial => KineticPreset.Poster,
            KineticPreset.Poster => KineticPreset.SpotlightFog,
            KineticPreset.SpotlightFog => KineticPreset.LiveCode,
            KineticPreset.LiveCode => KineticPreset.Editorial,
            _ => KineticPreset.Editorial
        };

        SetActiveKineticPreset(nextPreset);
    }

    public void SetActiveKineticPreset(KineticPreset newPreset)
    {
        KineticStandardHost.Visibility = Visibility.Collapsed;
        PosterBackdrop.Visibility = Visibility.Collapsed;
        _currentKineticActiveIndex = -1;
        _worldPhrases.Clear();
        KineticWorldCanvas.Children.Clear();
        _prebuiltNextPhrase = null;
        _prebuiltNextIndex = -1;
        _prebuiltEpoch = -1;
        _layoutDirector.Reset();
        _cameraController.Reset(new Point(0, 0));
        _lastKineticClockMs = 0.0;
        _lastKineticPosition = TimeSpan.Zero;

        SpotlightFogHost.Visibility = Visibility.Collapsed;
        _spotlightFogRenderer.Deactivate();

        LiveCodeHost.Visibility = Visibility.Collapsed;
        _liveCodeRenderer.Deactivate();

        _currentKineticPreset = newPreset;
        _settings.FullscreenKineticPreset = newPreset.ToString();
        SettingsService.Save(_settings);
        UpdateKineticPresetButtonLabel();

        if (!_isKineticMode)
        {
            return;
        }

        // Viewport dimensions from root window
        double vpW = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : (ActualWidth > 0 ? ActualWidth : 1920.0);
        double vpH = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : (ActualHeight > 0 ? ActualHeight : 1080.0);
        if (vpW <= 0) vpW = 1920.0;
        if (vpH <= 0) vpH = 1080.0;

        switch (newPreset)
        {
            case KineticPreset.Editorial:
            case KineticPreset.Poster:
                KineticStandardHost.Visibility = Visibility.Visible;
                KineticStandardHost.Opacity = 1.0;
                PosterBackdrop.Visibility = (newPreset == KineticPreset.Poster) ? Visibility.Visible : Visibility.Collapsed;
                KineticViewportCanvas.Visibility = Visibility.Visible;
                KineticViewportCanvas.Opacity = 1.0;
                _layoutDirector.Reset();
                _layoutDirector.IsPosterDimMode = _settings.PosterDimMode;
                _cameraController.Reset(new Point(vpW * 0.52, vpH * 0.46));
                _currentKineticActiveIndex = -1;
                _lastKineticClockMs = 0.0;
                _lastKineticPosition = TimeSpan.Zero;

                if (_currentLyrics.HasSyncedLyrics && _currentLyrics.Lines.Count > 0)
                {
                    int totalOffsetMs = _settings.SyncOffsetMs + _currentLyrics.UserOffsetMs;
                    TimeSpan effectivePos = _mediaService.CurrentPosition + TimeSpan.FromMilliseconds(totalOffsetMs);
                    if (effectivePos < TimeSpan.Zero) effectivePos = TimeSpan.Zero;

                    int activeIndex = FindCurrentLyricLineIndex(_currentLyrics.Lines, effectivePos);
                    if (activeIndex >= 0 && activeIndex < _currentLyrics.Lines.Count)
                    {
                        _currentKineticActiveIndex = activeIndex;
                        var line = _currentLyrics.Lines[activeIndex];
                        var newPhrase = _layoutDirector.CreatePhrase(
                            line,
                            activeIndex,
                            _currentLyrics.Lines,
                            _currentPalette,
                            _kineticVibe,
                            newPreset.ToString(),
                            _currentBassEnergy,
                            _currentBeatImpulse,
                            new Size(vpW, vpH));

                        Canvas.SetLeft(newPhrase.Container, newPhrase.WorldAnchor.X - (newPhrase.VisualSize.Width * 0.5));
                        Canvas.SetTop(newPhrase.Container, newPhrase.WorldAnchor.Y - (newPhrase.VisualSize.Height * 0.5));
                        Panel.SetZIndex(newPhrase.Container, 2);
                        newPhrase.Container.Visibility = Visibility.Visible;
                        newPhrase.Container.Opacity = 1.0;
                        newPhrase.IsExiting = false;
                        newPhrase.CatchUpToTime(effectivePos);

                        KineticWorldCanvas.Children.Add(newPhrase.Container);
                        _worldPhrases.Add(newPhrase);

                        if (newPhrase.IsPosterMode)
                        {
                            PosterBackdrop.Visibility = Visibility.Visible;
                            PosterBackdrop.Background = new SolidColorBrush(newPhrase.BackdropColor);
                        }

                        _cameraController.SnapTo(newPhrase.WorldAnchor.X, newPhrase.WorldAnchor.Y, newPhrase.BaseZoom, newPhrase.TargetCameraAngle);
                        _cameraController.ApplyMatrix(vpW, vpH, WorldCameraMatrix, _timeElapsedSec);
                    }
                }
                break;

            case KineticPreset.SpotlightFog:
                SpotlightFogHost.Visibility = Visibility.Visible;
                SpotlightFogHost.Opacity = 1.0;
                _spotlightFogRenderer.Activate(vpW, vpH);
                _spotlightFogRenderer.SetBackdropMode(_settings.SpotlightBackdropMode ?? "HdArtwork");
                _spotlightFogRenderer.UpdateLyrics(_currentLyrics);
                var track = _mediaService.CurrentTrack;
                if (track != null && track.Thumbnail != null)
                {
                    _spotlightFogRenderer.UpdateAlbumArt(track.Thumbnail, _currentPalette);
                }
                if (_highResArtworkService.CurrentHdArtwork != null)
                {
                    _spotlightFogRenderer.UpdateHdAlbumArt(_highResArtworkService.CurrentHdArtwork);
                }
                _spotlightFogRenderer.Reset();
                break;

            case KineticPreset.LiveCode:
                LiveCodeHost.Visibility = Visibility.Visible;
                LiveCodeHost.Opacity = 1.0;
                _liveCodeRenderer.Activate(vpW, vpH);
                var liveTrack = _mediaService.CurrentTrack;
                _liveCodeRenderer.UpdateTrack(liveTrack);
                _liveCodeRenderer.UpdatePalette(_currentPalette);
                _liveCodeRenderer.UpdateLyrics(_currentLyrics);
                if (_highResArtworkService.CurrentHdArtwork != null)
                {
                    _liveCodeRenderer.UpdateHdAlbumArt(_highResArtworkService.CurrentHdArtwork);
                }
                else if (liveTrack != null && liveTrack.Thumbnail != null)
                {
                    _liveCodeRenderer.UpdateAlbumArt(liveTrack.Thumbnail, _currentPalette);
                }
                _liveCodeRenderer.Reset();
                break;
        }

        // Forward size sync on Render priority
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            double renderW = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : ActualWidth;
            double renderH = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : ActualHeight;
            if (renderW > 0 && renderH > 0)
            {
                ForwardSizeToActivePreset(renderW, renderH);
            }
        }));
    }

    private void ForwardSizeToActivePreset(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _spotlightFogRenderer.OnResize(width, height);
        _liveCodeRenderer.OnResize(width, height);
    }

    private void UpdateKineticPresetButtonLabel()
    {
        if (BtnToggleKineticPreset != null)
        {
            BtnToggleKineticPreset.Content = $"Style: {_currentKineticPreset}";
        }
        UpdatePosterDimButtonUi();
        UpdateSpotlightBackdropButtonUi();
    }

    private void UpdateSpotlightBackdropButtonUi()
    {
        if (BtnToggleSpotlightBackdrop != null)
        {
            bool isSpotlight = _currentKineticPreset == KineticPreset.SpotlightFog;
            BtnToggleSpotlightBackdrop.Visibility = (_isKineticMode && isSpotlight) ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleSpotlightBackdrop.Content = _settings.SpotlightBackdropMode == "FluidColor" ? "Backdrop: Fluid" : "Backdrop: HD Art";
        }
    }

    private void OnToggleSpotlightBackdropClicked(object sender, RoutedEventArgs e)
    {
        _settings.SpotlightBackdropMode = _settings.SpotlightBackdropMode == "HdArtwork" ? "FluidColor" : "HdArtwork";
        SettingsService.Save(_settings);
        _spotlightFogRenderer.SetBackdropMode(_settings.SpotlightBackdropMode);
        UpdateSpotlightBackdropButtonUi();
    }

    private void UpdatePosterDimButtonUi()
    {
        if (BtnTogglePosterDim != null)
        {
            bool isPoster = _currentKineticPreset == KineticPreset.Poster;
            BtnTogglePosterDim.Visibility = (_isKineticMode && isPoster) ? Visibility.Visible : Visibility.Collapsed;
            BtnTogglePosterDim.Content = _settings.PosterDimMode ? "Tone: Dim" : "Tone: Vivid";
        }
    }

    private void OnTogglePosterDimClicked(object sender, RoutedEventArgs e)
    {
        _settings.PosterDimMode = !_settings.PosterDimMode;
        SettingsService.Save(_settings);
        _layoutDirector.IsPosterDimMode = _settings.PosterDimMode;
        UpdatePosterDimButtonUi();
    }

    private void UpdateLayoutModeButtonLabel()
    {
        BtnToggleLayoutMode.Content = _isKineticMode ? "Mode: Kinetic (Beta)" : "Mode: Split";
    }

    private void UpdateVibeButtonLabel()
    {
        BtnToggleVibe.Content = $"Vibe: {_kineticVibe}";
    }

    private void ApplyLayoutMode(bool isKinetic)
    {
        _isKineticMode = isKinetic;
        _settings.FullscreenLayoutMode = isKinetic ? "Kinetic" : "Split";
        SettingsService.Save(_settings);

        UpdateLayoutModeButtonLabel();
        UpdatePosterDimButtonUi();
        UpdateSpotlightBackdropButtonUi();

        if (isKinetic)
        {
            KineticControlsPanel.Visibility = Visibility.Visible;
            VignetteEdgeStop.Color = (Color)ColorConverter.ConvertFromString("#9504060A");
            BottomMarqueeCanvas.Visibility = Visibility.Visible;

            if (!_currentLyrics.HasSyncedLyrics)
            {
                // No-Lyrics / Instrumental Guard in Kinetic Mode:
                LandscapeLayout.Visibility = Visibility.Visible;
                PortraitLayout.Visibility = Visibility.Collapsed;
                LyricsSideContainer.Visibility = Visibility.Collapsed;
                LandscapeColLeft.Width = new GridLength(1.0, GridUnitType.Star);
                LandscapeColRight.Width = new GridLength(0.0, GridUnitType.Pixel);
                AlbumSideContainer.HorizontalAlignment = HorizontalAlignment.Center;
                AlbumSideContainer.Margin = new Thickness(0);
                TrackTitleText.TextAlignment = TextAlignment.Center;
                TrackArtistText.TextAlignment = TextAlignment.Center;

                KineticStandardHost.Visibility = Visibility.Collapsed;
                SpotlightFogHost.Visibility = Visibility.Collapsed;
                LiveCodeHost.Visibility = Visibility.Collapsed;
                KineticCornerBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Synced lyrics active: animated typography owns 100% of the screen
                LandscapeLayout.Visibility = Visibility.Collapsed;
                PortraitLayout.Visibility = Visibility.Collapsed;
                KineticCornerBadge.Visibility = Visibility.Visible;
                KineticCornerBadge.Opacity = 1.0;

                SetActiveKineticPreset(_currentKineticPreset);
            }
        }
        else
        {
            KineticControlsPanel.Visibility = Visibility.Collapsed;
            VignetteEdgeStop.Color = (Color)ColorConverter.ConvertFromString("#7504060A");
            BottomMarqueeCanvas.Visibility = Visibility.Collapsed;

            KineticStandardHost.Visibility = Visibility.Collapsed;
            SpotlightFogHost.Visibility = Visibility.Collapsed;
            LiveCodeHost.Visibility = Visibility.Collapsed;
            KineticCornerBadge.Visibility = Visibility.Collapsed;

            _spotlightFogRenderer.Deactivate();
            _liveCodeRenderer.Deactivate();

            _currentKineticActiveIndex = -1;
            _worldPhrases.Clear();
            KineticWorldCanvas.Children.Clear();
            _layoutDirector.Reset();
            _cameraController.Reset(new Point(0, 0));

            if (_isPortraitMode)
            {
                PortraitLayout.Visibility = Visibility.Visible;
                LandscapeLayout.Visibility = Visibility.Collapsed;
            }
            else
            {
                LandscapeLayout.Visibility = Visibility.Visible;
                PortraitLayout.Visibility = Visibility.Collapsed;
            }

            SetInstrumentalMode(!_currentLyrics.HasSyncedLyrics);
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
        _modeBarTimer?.Stop();
        _hoverLeaveTimer?.Stop();

        _volumeService?.Dispose();
        _volumeService = null;

        _mediaService.TrackChanged -= OnTrackChanged;
        _mediaService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _mediaService.TimelineResynced -= OnTimelineResynced;
        _audioBeatService.BeatUpdated -= OnBeatUpdated;

        // Cleanly clear kinetic world canvas and visuals
        _worldPhrases.Clear();
        KineticWorldCanvas.Children.Clear();
    }

}
