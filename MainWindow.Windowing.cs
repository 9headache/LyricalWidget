using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Services;
using WinForms = System.Windows.Forms;

namespace LyricalWidget;

public partial class MainWindow : Window
{
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isLocked = false;
    private bool _isManuallyHidden = false;
    private bool _isAutoHidden = false;
    private bool _forceIdleClock = false;
    private bool ShowsIdleClockWhenHidden => _forceIdleClock || _settings.ShowIdleClock;

    private void RestoreSettings()
    {
        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);

        if (_settings.HasCustomPosition)
        {
            PlaceWidget(new Rect(_settings.Left, _settings.Top, Width, Height));
        }
        else
        {
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2.0);
            Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2.0);
        }

        _isLocked = _settings.IsLocked;
        _isManuallyHidden = _settings.IsHidden;
        _isAutoHidden = false;

        SyncControlsToSettings();

        if (_isManuallyHidden)
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private void SyncControlsToSettings()
    {
        SliderFontSize.Value = _settings.FontSize;
        LblFontSize.Text = $"{_settings.FontSize:0}px";

        SliderOpacity.Value = _settings.Opacity;
        LblOpacity.Text = $"{(int)(_settings.Opacity * 100)}%";
        CardBorder.Opacity = _settings.Opacity;

        SliderSyncOffset.Value = _settings.SyncOffsetMs;
        LblSyncOffset.Text = $"{(_settings.SyncOffsetMs >= 0 ? "+" : "")}{_settings.SyncOffsetMs} ms";

        ChkThemeLyrics.IsChecked = _settings.ThemeColoredLyrics;
        ChkAutoHide.IsChecked = _settings.AutoHideWhenPaused;
        ChkPacingPill.IsChecked = _settings.ShowKineticPacingPill;
        ChkIdleClock.IsChecked = _settings.ShowIdleClock;
        ChkSubLines.IsChecked = _settings.ShowSubLines;
        ChkAudioReactive.IsChecked = _settings.AudioReactiveGlow;
        ChkTrackBanner.IsChecked = _settings.ShowTrackSwitchBanner;
        ChkGamingMode.IsChecked = _settings.GamingMode;
        ChkEcoFps.IsChecked = (_settings.FpsCap <= 30);

        ApplyGamingMode(_settings.GamingMode);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);

        NativeMethods.EnableToolWindowStyle(_hwnd);

        // Alt+L is often taken by other apps, so there are two fallbacks
        string lockShortcutText = "Alt+L";
        bool lockRegistered = NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_LOCK_TOGGLE,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_L);

        if (!lockRegistered)
        {
            lockRegistered = NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_LOCK_TOGGLE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_L);
            if (lockRegistered)
            {
                lockShortcutText = "Ctrl+Alt+L";
            }
            else
            {
                lockRegistered = NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_LOCK_TOGGLE,
                    NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_L);
                if (lockRegistered)
                {
                    lockShortcutText = "Alt+Shift+L";
                }
                else
                {
                    lockShortcutText = "(None)";
                }
            }
        }

        NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_HIDE_TOGGLE,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_H);

        NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_NUDGE_BACK,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_OEM_4);

        NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_NUDGE_FORWARD,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_OEM_6);

        NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_VIBE_TOGGLE,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F);

        NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_RECONNECT,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_R);

        _trayService = new TrayIconService();
        _trayService.UpdateLockShortcutText(lockShortcutText);
        LblLockShortcut.Text = lockShortcutText.Replace("+", " + ");
        _trayService.ToggleLockRequested += () => Dispatcher.Invoke(ToggleLock);
        _trayService.ToggleVisibilityRequested += () => Dispatcher.Invoke(ToggleVisibility);
        _trayService.ToggleFullscreenVibeRequested += () => Dispatcher.Invoke(ToggleFullscreenVibe);
        _trayService.ForceReconnectRequested += () => Dispatcher.Invoke(() => _ = ForceRefreshAndReconnectAsync());
        _trayService.DisplayModeSelected += mode => Dispatcher.Invoke(() => SetDisplayMode(mode));
        _trayService.BackgroundStyleSelected += bg => Dispatcher.Invoke(() => SetBackgroundStyle(bg));
        _trayService.AnimationStyleSelected += style => Dispatcher.Invoke(() => SetAnimationStyle(style));
        _trayService.ThemeColoredLyricsToggled += enabled => Dispatcher.Invoke(() => SetThemeColoredLyrics(enabled));
        _trayService.AutoHideToggled += autoHide => Dispatcher.Invoke(() =>
        {
            _settings.AutoHideWhenPaused = autoHide;
            ChkAutoHide.IsChecked = autoHide;
            UpdateAutoHideState(_mediaService.IsPlaying);
            SaveCurrentBounds();
        });
        _trayService.IdleClockToggled += showClock => Dispatcher.Invoke(() =>
        {
            _settings.ShowIdleClock = showClock;
            ChkIdleClock.IsChecked = showClock;
            UpdateAutoHideState(_mediaService.IsPlaying);
            SaveCurrentBounds();
        });
        _trayService.SubLinesToggled += showSub => Dispatcher.Invoke(() =>
        {
            _settings.ShowSubLines = showSub;
            ChkSubLines.IsChecked = showSub;
            RebuildLyricsViews();
            EnsureRenderingSubscribed();
            SaveCurrentBounds();
        });
        _trayService.AudioReactiveGlowToggled += enabled => Dispatcher.Invoke(() => SetAudioReactiveGlow(enabled));
        _trayService.TrackBannerToggled += showBanner => Dispatcher.Invoke(() =>
        {
            _settings.ShowTrackSwitchBanner = showBanner;
            ChkTrackBanner.IsChecked = showBanner;
            SettingsService.Save(_settings);
        });
        _trayService.GamingModeToggled += enabled => Dispatcher.Invoke(() => ApplyGamingMode(enabled));
        _trayService.FpsCapToggled += fps => Dispatcher.Invoke(() => SetFpsCap(fps));
        _trayService.ShowStartupGuideRequested += () => Dispatcher.Invoke(ShowStartupGuide);
        _trayService.WhatsNewRequested += () => Dispatcher.Invoke(ShowWhatsNew);
        _trayService.BrowserMediaBehaviorSelected += behavior => Dispatcher.Invoke(() => SetBrowserMediaBehavior(behavior));
        _trayService.PresetSelected += name => Dispatcher.Invoke(() => LoadPreset(name));
        _trayService.VibeMonitorSelected += device => Dispatcher.Invoke(() => SetPreferredVibeMonitor(device));
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        RefreshVibeMonitors();
        RebuildPresetList();
        _trayService.ResetPositionRequested += () => Dispatcher.Invoke(ResetOverlayPosition);
        _trayService.ReportBugRequested += () => Dispatcher.Invoke(ShowBugReporter);
        _trayService.ClearLyricsCacheRequested += () => _lyricsService.ClearCache();
        _trayService.ExitRequested += () => Dispatcher.Invoke(CloseApp);

        SyncTrayState();
        _trayService.UpdateVisibilityState(!_isManuallyHidden);

        _audioBeatService.BassEnergyChanged += OnBassEnergyChanged;

        ApplyLockState(_isLocked);
        ApplyDisplayModeAndTheme();
    }

    private void SyncTrayState()
    {
        if (_trayService == null) return;
        _trayService.UpdateDisplayModeState(_settings.DisplayMode);
        _trayService.UpdateBackgroundStyleState(_settings.BackgroundStyle);
        _trayService.UpdateAnimationStyleState(_settings.AnimationStyle);
        _trayService.UpdateThemeColoredLyricsState(_settings.ThemeColoredLyrics);
        _trayService.UpdateBrowserMediaState(_settings.BrowserMediaBehavior);
        _trayService.UpdateAutoHideState(_settings.AutoHideWhenPaused);
        _trayService.UpdateIdleClockState(_settings.ShowIdleClock);
        _trayService.UpdateSubLinesState(_settings.ShowSubLines);
        _trayService.UpdateAudioReactiveGlowState(_settings.AudioReactiveGlow);
        _trayService.UpdateTrackBannerState(_settings.ShowTrackSwitchBanner);
        _trayService.UpdateGamingModeState(_settings.GamingMode);
        _trayService.UpdateFpsCapState(_settings.FpsCap);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            if (hotkeyId == NativeMethods.HOTKEY_ID_LOCK_TOGGLE)
            {
                ToggleLock();
                handled = true;
            }
            else if (hotkeyId == NativeMethods.HOTKEY_ID_HIDE_TOGGLE)
            {
                ToggleVisibility();
                handled = true;
            }
            else if (hotkeyId == NativeMethods.HOTKEY_ID_NUDGE_BACK)
            {
                NudgeSongOffset(-100);
                handled = true;
            }
            else if (hotkeyId == NativeMethods.HOTKEY_ID_NUDGE_FORWARD)
            {
                NudgeSongOffset(+100);
                handled = true;
            }
            else if (hotkeyId == NativeMethods.HOTKEY_ID_VIBE_TOGGLE)
            {
                ToggleFullscreenVibe();
                handled = true;
            }
            else if (hotkeyId == NativeMethods.HOTKEY_ID_RECONNECT)
            {
                _ = ForceRefreshAndReconnectAsync();
                handled = true;
            }
        }
        else if (msg == NativeMethods.WM_MOVING && !_isLocked)
        {
            HandleWindowMoving(hwnd, lParam);
            handled = true;
        }
        else if (msg == NativeMethods.WM_EXITSIZEMOVE)
        {
            HideAllSnapGuides();
            HideDragOutline();
            SaveCurrentBounds();
            OnMoveOrResizeEnded();
        }
        return IntPtr.Zero;
    }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateMediaControlsPlayPauseIcon(isPlaying);
            if (!IsFullscreenActive)
            {
                if (isPlaying)
                {
                    EnsureRenderingSubscribed();
                }
                UpdateAutoHideState(isPlaying);
            }
            _audioBeatService.UpdateState(isPlaying, _settings.GamingMode, _settings.AudioReactiveGlow);
        });
    }

    private void UpdateAutoHideState(bool isPlaying)
    {
        // Browser modes show the clock on their own, independent of the auto-hide settings
        _forceIdleClock = _clockForBrowser || (_mediaService.IsBrowserSuppressed && !isPlaying);
        bool shouldAutoHide = _forceIdleClock || _settings.AutoHideWhenPaused && (!isPlaying || !_mediaService.CurrentTrack.IsValid);
        _isAutoHidden = shouldAutoHide;
        ApplyEffectiveVisibility();
    }

    private void ApplyEffectiveVisibility()
    {
        if (IsFullscreenActive)
        {
            Visibility = Visibility.Hidden;
            return;
        }

        if (_isManuallyHidden)
        {
            Visibility = Visibility.Collapsed;
            _trayService?.UpdateVisibilityState(false);
            StopRenderingLoop();
            return;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }
        _trayService?.UpdateVisibilityState(true);

        bool showIdleClock = _isAutoHidden && (_forceIdleClock || (_settings.AutoHideWhenPaused && _settings.ShowIdleClock));

        if (_isAutoHidden)
        {
            if (showIdleClock)
            {
                EnsureTopmost();
                EnsureRenderingSubscribed();
                TransitionToIdleClock();

                var fadeIn = new DoubleAnimation(CardBorder.Opacity, _settings.Opacity, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
            else
            {
                TransitionFromIdleClock();

                var fadeOut = new DoubleAnimation(CardBorder.Opacity, 0.0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, e) =>
                {
                    if (_isAutoHidden && !ShowsIdleClockWhenHidden)
                    {
                        StopRenderingLoop();
                    }
                };
                CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }
        else
        {
            EnsureTopmost();
            EnsureRenderingSubscribed();
            TransitionFromIdleClock();

            var fadeIn = new DoubleAnimation(CardBorder.Opacity, _settings.Opacity, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isClosing) RefreshVibeMonitors();
        }));
    }

    private void SetPreferredVibeMonitor(string deviceName)
    {
        _settings.PreferredVibeMonitor = deviceName;
        SettingsService.Save(_settings);
        _vibeWindow?.MoveToMonitor(deviceName);
        RefreshVibeMonitors();
    }

    /// <summary>Rebuilds the Vibe display choices. With one display there is nothing to choose, so they stay hidden.</summary>
    private void RefreshVibeMonitors()
    {
        var monitors = MonitorService.GetMonitors();
        string? selected = MonitorService.Resolve(monitors, _settings.PreferredVibeMonitor)?.DeviceName;
        _trayService?.UpdateVibeMonitors(monitors, selected);

        VibeMonitorOptions.Children.Clear();
        VibeMonitorRow.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (monitors.Count < 2) return;

        foreach (var monitor in monitors)
        {
            bool isSelected = monitor.DeviceName == selected;
            var button = new Button
            {
                Content = monitor.Label,
                Style = (Style)FindResource("ToolbarPillButton"),
                Margin = new Thickness(0, 0, 3, 0),
                Background = new SolidColorBrush(isSelected ? Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(0xD4, 0xD4, 0xD8) : Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF))
            };
            button.Click += (s, e) => SetPreferredVibeMonitor(monitor.DeviceName);
            VibeMonitorOptions.Children.Add(button);
        }
    }

    public void ToggleLock()
    {
        ApplyLockState(!_isLocked);
        SaveCurrentBounds();
    }

    public void ToggleVisibility()
    {
        if (IsFullscreenActive) return;
        _isManuallyHidden = !_isManuallyHidden;
        _settings.IsHidden = _isManuallyHidden;
        ApplyEffectiveVisibility();
        SaveCurrentBounds();
    }

    private void ApplyLockState(bool locked)
    {
        _isLocked = locked;
        _settings.IsLocked = locked;

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetClickThrough(_hwnd, locked);
        }

        if (locked)
        {
            ResizeGripThumb.Visibility = Visibility.Collapsed;
            HideAllSnapGuides();
            ShowMediaControls(false);
        }
        else
        {
            ResizeGripThumb.Visibility = Visibility.Visible;
        }

        ApplyDisplayModeAndTheme();
        UpdateToolbarPopup();
        _trayService?.UpdateLockState(locked);
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isLocked)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                HideAllSnapGuides();
                SaveCurrentBounds();
            }
        }
    }

    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        double newWidth = Width + e.HorizontalChange;
        double newHeight = Height + e.VerticalChange;

        if (newWidth >= MinWidth) Width = newWidth;
        if (newHeight >= MinHeight) Height = newHeight;
    }

    /// <summary>
    /// Puts the widget exactly where it was saved. Only when that spot is no longer on any connected display
    /// (a monitor was unplugged) is it moved onto the primary one, keeping its size.
    /// </summary>
    private void PlaceWidget(Rect saved)
    {
        Width = Math.Max(MinWidth, saved.Width);
        Height = Math.Max(MinHeight, saved.Height);

        // Screen coordinates are in pixels of the primary display's DPI; the window's are in DIPs
        double scale = WinForms.Screen.PrimaryScreen is { } primary && primary.Bounds.Width > 0
            ? SystemParameters.PrimaryScreenWidth / primary.Bounds.Width
            : 1.0;
        var rect = new Rect(saved.Left, saved.Top, Width, Height);
        bool onScreen = false;
        foreach (var screen in WinForms.Screen.AllScreens)
        {
            var work = screen.WorkingArea;
            var area = new Rect(work.X * scale, work.Y * scale, work.Width * scale, work.Height * scale);
            var visible = Rect.Intersect(area, rect);
            if (!visible.IsEmpty && visible.Width >= 48 && visible.Height >= 32) onScreen = true;
        }

        if (onScreen)
        {
            Left = saved.Left;
            Top = saved.Top;
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(saved.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(saved.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void SaveCurrentBounds()
    {
        if (_isClosing || _applyingPreset) return;

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.IsLocked = _isLocked;
        _settings.IsHidden = _isManuallyHidden;
        _settings.HasCustomPosition = true;

        // Called for every pixel of a drag: the file is written once things are still
        ScheduleSettingsSave();
    }

    public void EnsureTopmost()
    {
        if (IsFullscreenActive) return;
        Topmost = false;
        Topmost = true;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    public void ResetOverlayPosition()
    {
        if (IsFullscreenActive) return;
        Width = Math.Max(MinWidth, 640);
        Height = Math.Max(MinHeight, 280);
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2.0);
        Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2.0);
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.HasCustomPosition = true;

        if (_isManuallyHidden)
        {
            _isManuallyHidden = false;
            _settings.IsHidden = false;
        }
        _isAutoHidden = false;
        ApplyEffectiveVisibility();
        EnsureTopmost();
        SaveCurrentBounds();
    }
}
