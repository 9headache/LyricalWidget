using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private AppSettings _settings = new();

    private readonly SmtcMediaService _mediaService = new();
    private readonly LyricsWaterfallService _lyricsService = new();
    private readonly AudioBeatService _audioBeatService = new();
    private FullscreenVibeWindow? _vibeWindow;
    private bool _isFullscreenActive = false;
    public bool IsFullscreenActive => _isFullscreenActive;
    private TrayIconService? _trayService;

    private LrcDocument _currentLyrics = LrcDocument.Empty();
    private long _trackSequenceId = 0;
    private CancellationTokenSource? _trackLoadCts;
    private readonly object _trackLoadLock = new();
    private CancellationTokenSource? _bannerCts;
    private bool _isClosing = false;

    public MainWindow()
    {
        InitializeComponent();

        var wpfIcon = IconHelper.LoadWpfIcon();
        if (wpfIcon != null)
        {
            Icon = wpfIcon;
        }

        _lyricsOnlyMoonlight = new MoonlightBloomRenderer(LyricsOnlyMoonlightCanvas, showInterludeNote: true);
        _sideMoonlight = new MoonlightBloomRenderer(SideMoonlightCanvas, showInterludeNote: true);

        _settings = SettingsService.Load();
        _mediaService.BrowserBehavior = _settings.BrowserMediaBehavior;
        RestoreSettings();
        InitAlbumArt();

        Loaded += OnLoaded;
        Closing += OnClosing;
        MouseDown += OnWindowMouseDown;
        LocationChanged += (s, e) => SaveCurrentBounds();
        SizeChanged += OnWindowSizeChanged;
        ResizeGripThumb.DragDelta += OnResizeGripDragDelta;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastFrameTimestamp = Stopwatch.GetTimestamp();

        _mediaService.TrackChanged += OnTrackChanged;
        _mediaService.PlaybackStateChanged += OnPlaybackStateChanged;
        _mediaService.TimelineResynced += OnTimelineResynced;
        _mediaService.BrowserSuppressionChanged += (s, args) => Dispatcher.InvokeAsync(() =>
        {
            if (!IsFullscreenActive) UpdateAutoHideState(_mediaService.IsPlaying);
        });

        // Hidden until the welcome window or splash has gone
        Opacity = 0.0;

        if (!_settings.HasSeenWelcome)
        {
            var welcome = new WelcomeWindow(isFirstBoot: true, isReplay: false, closedCallback: () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isClosing) return;
                    _settings.HasSeenWelcome = true;
                    _settings.LastSeenVersion = AppVersionInfo.Version;
                    SettingsService.Save(_settings);
                    if (IsFullscreenActive)
                    {
                        Opacity = 1.0;
                        return;
                    }
                    if (!_settings.HasCustomPosition)
                    {
                        // Centred where the welcome window dissolved
                        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2.0);
                        Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2.0);
                        _settings.Left = Left;
                        _settings.Top = Top;
                        _settings.HasCustomPosition = true;
                        SettingsService.Save(_settings);
                    }

                    EnsureTopmost();
                    ApplyEffectiveVisibility();

                    var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    BeginAnimation(OpacityProperty, fadeIn);
                });
            });
            _guideWindow = welcome;
            welcome.Show();
        }
        else
        {
            var splash = new WelcomeWindow(isFirstBoot: false, isReplay: false, closedCallback: () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isClosing) return;
                    if (IsFullscreenActive)
                    {
                        Opacity = 1.0;
                        return;
                    }
                    EnsureTopmost();
                    ApplyEffectiveVisibility();

                    var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    BeginAnimation(OpacityProperty, fadeIn);

                    if (_settings.LastSeenVersion != AppVersionInfo.Version)
                    {
                        _settings.LastSeenVersion = AppVersionInfo.Version;
                        SettingsService.Save(_settings);
                        ShowWhatsNew();
                    }
                });
            });
            splash.Show();
        }

        EnsureRenderingSubscribed();

        WireMediaControlsHover();
        InitMediaShortcutTimer();
        UpdateMediaControlsPlayPauseIcon(_mediaService.IsPlaying);

        await _mediaService.InitializeAsync();
        _audioBeatService.UpdateState(_mediaService.IsPlaying, _settings.GamingMode, _settings.AudioReactiveGlow);

        if (_trayService != null)
        {
            _trayService.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
        }

#if DEBUG
        string[] args = Environment.GetCommandLineArgs();
        bool previewFail = Array.IndexOf(args, "--preview-update-fail") >= 0;
        if (previewFail || Array.IndexOf(args, "--preview-update") >= 0)
        {
            ShowUpdatePrompt(
                new UpdateInfo { TagName = "v2.2.0", HtmlUrl = AppVersionInfo.RepositoryUrl, Body = "Preview only: nothing is downloaded or restarted." },
                (update, progress, ct) => UpdateService.SimulateUpdateAsync(progress, previewFail, ct));
        }
#endif

        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            var result = await UpdateService.CheckForUpdateAsync();
            if (result.Update != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_isClosing)
                    {
                        ShowUpdatePrompt(result.Update);
                    }
                });
            }
        });
    }

    public void ToggleFullscreenVibe()
    {
        if (_vibeWindow != null && _vibeWindow.IsVisible)
        {
            _vibeWindow.Close();
            return;
        }

        // Fullscreen needs the GPU, so Gaming Mode's software rendering is lifted while it is open
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.Default;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set ProcessRenderMode: {ex.Message}");
        }

        var palette = _mediaService.CurrentTrack.Thumbnail != null
            ? ColorExtractor.ExtractPalette(_mediaService.CurrentTrack.Thumbnail)
            : VibePalette.Default();

        _vibeWindow = new FullscreenVibeWindow(
            _mediaService,
            _lyricsService,
            _audioBeatService,
            _settings,
            _currentLyrics,
            _mediaService.CurrentTrack,
            palette);

        _isFullscreenActive = true;
        Visibility = Visibility.Hidden;
        StopRenderingLoop();

        _vibeWindow.Closed += (s, e) =>
        {
            _vibeWindow = null;
            _isFullscreenActive = false;
            // The pill's own "Don't show again" may have changed it
            ChkPacingPill.IsChecked = _settings.ShowKineticPacingPill;
            RefreshVibeMonitors();
            Background = Brushes.Transparent;
            ApplyGamingMode(_settings.GamingMode);
            UpdateAutoHideState(_mediaService.IsPlaying);
            EnsureTopmost();
            EnsureRenderingSubscribed();
        };

        _vibeWindow.Show();
    }

    private void OnFullscreenVibeClicked(object sender, RoutedEventArgs e) => ToggleFullscreenVibe();

    private void CloseApp()
    {
        _isClosing = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _mediaShortcutTimer?.Stop();
        StopRenderingLoop();
        lock (_trackLoadLock)
        {
            try
            {
                _trackLoadCts?.Cancel();
                _trackLoadCts?.Dispose();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error canceling track load: {ex.Message}");
            }
        }
        _bannerCts?.Cancel();
        _audioBeatService.Dispose();

        if (_vibeWindow != null && _vibeWindow.IsVisible)
        {
            _vibeWindow.Close();
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_LOCK_TOGGLE);
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_HIDE_TOGGLE);
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_NUDGE_BACK);
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_NUDGE_FORWARD);
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_VIBE_TOGGLE);
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_RECONNECT);
        }

        _hwndSource?.RemoveHook(HwndHook);
        _trayService?.Dispose();
        _mediaService.Dispose();
        _lyricsService.Dispose();

        SaveCurrentBounds();
    }
}
