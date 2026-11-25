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
    private AppSettings _settings = new();

    private readonly SmtcMediaService _mediaService = new();
    private readonly LyricsWaterfallService _lyricsService = new();
    private readonly AudioBeatService _audioBeatService = new();
    private FullscreenVibeWindow? _vibeWindow;
    private bool _isFullscreenActive = false;
    public bool IsFullscreenActive => _isFullscreenActive;
    private TrayIconService? _trayService;
    private WelcomeWindow? _guideWindow;
    private UpdatePromptWindow? _updatePrompt;
    private AppDialogWindow? _updateStatusDialog;
    private WhatsNewWindow? _whatsNewWindow;
    private bool _isCheckingForUpdates = false;

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

        _settings = SettingsService.Load();
        RestoreSettings();

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

        // Keep MainWindow transparent until Welcome/Splash finishes
        Opacity = 0.0;

        if (!_settings.HasSeenWelcome)
        {
            // Mode A: First-Boot Full Interactive Guide
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
                        // Cleanly center on primary monitor where welcome dissolved
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
            // Mode B: Returning-User Quick Splash (1.6s)
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

        // Non-blocking background update check ~5 seconds after startup
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


    // Toolbar event handlers
    private void OnModeLyricsOnlyClicked(object sender, RoutedEventArgs e) => SetDisplayMode("LyricsOnly");
    private void OnModeSideBySideClicked(object sender, RoutedEventArgs e) => SetDisplayMode("AlbumAndLyrics");
    private void OnModeAlbumOnlyClicked(object sender, RoutedEventArgs e) => SetDisplayMode("AlbumOnly");

    private void OnBgTransparentClicked(object sender, RoutedEventArgs e) => SetBackgroundStyle("Transparent");
    private void OnBgGlassClicked(object sender, RoutedEventArgs e) => SetBackgroundStyle("Glass");

    private void OnLinesAutoClicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("Auto");
    private void OnLines2Clicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("2");
    private void OnLines3Clicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("3");
    private void OnLines5Clicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("5");

    private void OnMotionFluidClicked(object sender, RoutedEventArgs e) => SetAnimationStyle("Fluid Wave");

    private void OnThemeLyricsChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkThemeLyrics.IsChecked == true;
        SetThemeColoredLyrics(isChecked);
    }

    private void OnAlignCenterClicked(object sender, RoutedEventArgs e) => SetLyricAlignment("Center");
    private void OnAlignLeftClicked(object sender, RoutedEventArgs e) => SetLyricAlignment("Left");

    private void OnLockClicked(object sender, RoutedEventArgs e) => ToggleLock();

    private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _settings.FontSize = e.NewValue;
        LblFontSize.Text = $"{e.NewValue:0}px";
        RebuildLyricsViews();
        SaveCurrentBounds();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _settings.Opacity = e.NewValue;
        LblOpacity.Text = $"{(int)(e.NewValue * 100)}%";
        CardBorder.Opacity = e.NewValue;
        SaveCurrentBounds();
    }

    private void OnSyncOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        int offset = (int)Math.Round(e.NewValue / 50.0) * 50;
        _settings.SyncOffsetMs = offset;
        LblSyncOffset.Text = $"{(offset >= 0 ? "+" : "")}{offset} ms";
        SaveCurrentBounds();
    }

    private void OnAutoHideChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkAutoHide.IsChecked == true;
        _settings.AutoHideWhenPaused = isChecked;
        _trayService?.UpdateAutoHideState(isChecked);
        UpdateAutoHideState(_mediaService.IsPlaying);
        SaveCurrentBounds();
    }

    private void OnIdleClockChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkIdleClock.IsChecked == true;
        _settings.ShowIdleClock = isChecked;
        _trayService?.UpdateIdleClockState(isChecked);
        UpdateAutoHideState(_mediaService.IsPlaying);
        SaveCurrentBounds();
    }

    private void OnSubLinesChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkSubLines.IsChecked == true;
        _settings.ShowSubLines = isChecked;
        _trayService?.UpdateSubLinesState(isChecked);
        RebuildLyricsViews();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    private void OnTrackBannerChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkTrackBanner.IsChecked == true;
        _settings.ShowTrackSwitchBanner = isChecked;
        _trayService?.UpdateTrackBannerState(isChecked);
        SaveCurrentBounds();
    }

    private void OnGamingModeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkGamingMode.IsChecked == true;
        ApplyGamingMode(isChecked);
    }

    private void OnEcoFpsChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isChecked = ChkEcoFps.IsChecked == true;
        SetFpsCap(isChecked ? 30 : 60);
    }

    private void OnAudioReactiveChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetAudioReactiveGlow(ChkAudioReactive.IsChecked == true);
    }


    public void ToggleFullscreenVibe()
    {
        if (_vibeWindow != null && _vibeWindow.IsVisible)
        {
            _vibeWindow.Close();
            return;
        }

        // Temporarily suspend Gaming Mode rendering while Fullscreen is open (force hardware GPU acceleration)
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
            Background = Brushes.Transparent;
            // Cleanly restore MainWindow's Gaming Mode state (SoftwareOnly + stops/disposes loopback if enabled)
            ApplyGamingMode(_settings.GamingMode);
            ApplyEffectiveVisibility();
            EnsureTopmost();
            EnsureRenderingSubscribed();
        };

        _vibeWindow.Show();
    }

    private void OnFullscreenVibeClicked(object sender, RoutedEventArgs e) => ToggleFullscreenVibe();


    // Media playback controls
    private static readonly Geometry PlayGeometry = Geometry.Parse("M8 5v14l11-7z");
    private static readonly Geometry PauseGeometry = Geometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z");

    private System.Windows.Threading.DispatcherTimer? _mediaShortcutTimer;
    private bool _isHoldingMediaShortcut = false;

    private async void OnMediaPrevClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TrySkipPreviousAsync();
    }

    private async void OnMediaPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TryTogglePlayPauseAsync();
    }

    private async void OnMediaNextClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TrySkipNextAsync();
    }

    private void UpdateMediaControlsPlayPauseIcon(bool isPlaying)
    {
        var geo = isPlaying ? PauseGeometry : PlayGeometry;
        if (SidePlayPauseIcon != null) SidePlayPauseIcon.Data = geo;
        if (AlbumPlayPauseIcon != null) AlbumPlayPauseIcon.Data = geo;
    }

    private void WireMediaControlsHover()
    {
        SideAlbumBorder.MouseEnter += (s, e) =>
        {
            if (!_isLocked) ShowMediaControls(true);
        };
        SideAlbumBorder.MouseLeave += (s, e) =>
        {
            if (!_isHoldingMediaShortcut) ShowMediaControls(false);
        };
        AlbumOnlyBorder.MouseEnter += (s, e) =>
        {
            if (!_isLocked) ShowMediaControls(true);
        };
        AlbumOnlyBorder.MouseLeave += (s, e) =>
        {
            if (!_isHoldingMediaShortcut) ShowMediaControls(false);
        };
    }

    private void InitMediaShortcutTimer()
    {
        _mediaShortcutTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _mediaShortcutTimer.Tick += (s, e) =>
        {
            if (_isClosing) return;
            bool altHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LMENU) & 0x8000) != 0;
            bool shiftHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LSHIFT) & 0x8000) != 0;
            bool holding = altHeld && shiftHeld;

            if (holding != _isHoldingMediaShortcut)
            {
                _isHoldingMediaShortcut = holding;
                if (holding)
                {
                    if (_hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetClickThrough(_hwnd, false);
                    }
                    ShowMediaControls(true);
                }
                else
                {
                    ShowMediaControls(false);
                    if (_isLocked && _hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetClickThrough(_hwnd, true);
                    }
                }
            }
        };
        _mediaShortcutTimer.Start();
    }

    private void ShowMediaControls(bool show)
    {
        var targetOpacity = show ? 1.0 : 0.0;
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn }
        };

        void ApplyTo(Border overlay)
        {
            if (overlay == null) return;
            if (show)
            {
                overlay.Visibility = Visibility.Visible;
            }
            else
            {
                anim.Completed += (s, e) =>
                {
                    if (!_isHoldingMediaShortcut && targetOpacity == 0.0)
                    {
                        overlay.Visibility = Visibility.Collapsed;
                    }
                };
            }
            overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        ApplyTo(SideMediaControlsOverlay);
        ApplyTo(AlbumOnlyMediaControlsOverlay);
    }


    public void ShowStartupGuide()
    {
        if (_guideWindow != null && _guideWindow.IsVisible)
        {
            _guideWindow.Activate();
            return;
        }

        _guideWindow = new WelcomeWindow(isFirstBoot: true, isReplay: true);
        _guideWindow.Show();
    }


    public void ShowBugReporter()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppVersionInfo.SupportUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open support link: {ex.Message}");
        }
    }

    private void ShowUpdatePrompt(UpdateInfo update, UpdateRunner? runner = null)
    {
        // One prompt at a time: two downloads would share the same temp folder
        if (_updatePrompt != null && _updatePrompt.IsVisible)
        {
            _updatePrompt.Activate();
            return;
        }

        _updatePrompt = new UpdatePromptWindow(update, runner) { Owner = this };
        _updatePrompt.Show();
    }

    public void ShowWhatsNew()
    {
        if (_whatsNewWindow != null && _whatsNewWindow.IsVisible)
        {
            _whatsNewWindow.Activate();
            return;
        }

        _whatsNewWindow = new WhatsNewWindow();
        _whatsNewWindow.Show();
    }

    private void ShowUpdateStatus(AppDialogWindow dialog)
    {
        _updateStatusDialog?.Close();
        _updateStatusDialog = dialog;
        dialog.Show();
    }

    private async void OnCheckForUpdatesRequested()
    {
        if (_isCheckingForUpdates) return;
        _isCheckingForUpdates = true;
        try
        {
            var result = await UpdateService.CheckForUpdateAsync();
            if (_isClosing) return;
            if (result.Update != null)
            {
                ShowUpdatePrompt(result.Update);
            }
            else if (result.IsUpToDate)
            {
                ShowUpdateStatus(new AppDialogWindow(
                    AppDialogGlyph.Check,
                    "You're running the latest build.",
                    $"LyricalWidget v{AppVersionInfo.Version} is already up to date — nothing to install.",
                    "View What's New",
                    ShowWhatsNew));
            }
            else
            {
                ShowUpdateStatus(new AppDialogWindow(
                    AppDialogGlyph.Info,
                    "Couldn't check for updates.",
                    $"Check your internet connection and try again.\n\n{result.ErrorMessage}"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check error: {ex.Message}");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private void CloseApp()
    {
        _isClosing = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
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
