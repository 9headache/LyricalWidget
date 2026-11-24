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
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _isLocked = false;
    private bool _isManuallyHidden = false;
    private bool _isAutoHidden = false;

    private void RestoreSettings()
    {
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vWidth = SystemParameters.VirtualScreenWidth;
        double vHeight = SystemParameters.VirtualScreenHeight;

        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);

        if (_settings.HasCustomPosition &&
            _settings.Left >= vLeft && _settings.Left + Width <= vLeft + vWidth &&
            _settings.Top >= vTop && _settings.Top + Height <= vTop + vHeight)
        {
            Left = _settings.Left;
            Top = _settings.Top;
        }
        else
        {
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2.0);
            Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2.0);
        }

        _isLocked = _settings.IsLocked;
        _isManuallyHidden = _settings.IsHidden;
        _isAutoHidden = false;

        SliderFontSize.Value = _settings.FontSize;
        LblFontSize.Text = $"{_settings.FontSize:0}px";

        SliderOpacity.Value = _settings.Opacity;
        LblOpacity.Text = $"{(int)(_settings.Opacity * 100)}%";
        CardBorder.Opacity = _settings.Opacity;

        SliderSyncOffset.Value = _settings.SyncOffsetMs;
        LblSyncOffset.Text = $"{(_settings.SyncOffsetMs >= 0 ? "+" : "")}{_settings.SyncOffsetMs} ms";

        ChkThemeLyrics.IsChecked = _settings.ThemeColoredLyrics;
        ChkAutoHide.IsChecked = _settings.AutoHideWhenPaused;
        ChkIdleClock.IsChecked = _settings.ShowIdleClock;
        ChkSubLines.IsChecked = _settings.ShowSubLines;
        ChkAudioReactive.IsChecked = _settings.AudioReactiveGlow;
        ChkTrackBanner.IsChecked = _settings.ShowTrackSwitchBanner;
        ChkGamingMode.IsChecked = _settings.GamingMode;
        ChkEcoFps.IsChecked = (_settings.FpsCap <= 30);

        ApplyGamingMode(_settings.GamingMode);

        if (_isManuallyHidden)
        {
            Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);

        NativeMethods.EnableToolWindowStyle(_hwnd);

        // Register Global Hotkeys with automatic fallback for Alt + L
        string lockShortcutText = "Alt+L";
        bool lockRegistered = NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_LOCK_TOGGLE,
            NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_L);

        if (!lockRegistered)
        {
            // Fallback 1: Ctrl + Alt + L
            lockRegistered = NativeMethods.RegisterHotKey(_hwnd, NativeMethods.HOTKEY_ID_LOCK_TOGGLE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_L);
            if (lockRegistered)
            {
                lockShortcutText = "Ctrl+Alt+L";
            }
            else
            {
                // Fallback 2: Alt + Shift + L
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
        _trayService.ResetPositionRequested += () => Dispatcher.Invoke(ResetOverlayPosition);
        _trayService.ReportBugRequested += () => Dispatcher.Invoke(ShowBugReporter);
        _trayService.ClearLyricsCacheRequested += () => _lyricsService.ClearCache();
        _trayService.ExitRequested += () => Dispatcher.Invoke(CloseApp);

        _trayService.UpdateDisplayModeState(_settings.DisplayMode);
        _trayService.UpdateBackgroundStyleState(_settings.BackgroundStyle);
        _trayService.UpdateAnimationStyleState(_settings.AnimationStyle);
        _trayService.UpdateThemeColoredLyricsState(_settings.ThemeColoredLyrics);
        _trayService.UpdateAutoHideState(_settings.AutoHideWhenPaused);
        _trayService.UpdateIdleClockState(_settings.ShowIdleClock);
        _trayService.UpdateSubLinesState(_settings.ShowSubLines);
        _trayService.UpdateAudioReactiveGlowState(_settings.AudioReactiveGlow);
        _trayService.UpdateTrackBannerState(_settings.ShowTrackSwitchBanner);
        _trayService.UpdateGamingModeState(_settings.GamingMode);
        _trayService.UpdateFpsCapState(_settings.FpsCap);
        _trayService.UpdateVisibilityState(!_isManuallyHidden);

        _audioBeatService.BassEnergyChanged += OnBassEnergyChanged;

        ApplyLockState(_isLocked);
        ApplyDisplayModeAndTheme();
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
            SaveCurrentBounds();
        }
        return IntPtr.Zero;
    }

    private void HandleWindowMoving(IntPtr hwnd, IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return;
        var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.RECT>(lParam);

        IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO();
        mi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        var work = mi.rcWork;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        int workLeft = work.Left;
        int workTop = work.Top;
        int workRight = work.Right;
        int workBottom = work.Bottom;

        int workCenterX = workLeft + (workRight - workLeft) / 2;
        int workCenterY = workTop + (workBottom - workTop) / 2;

        int winCenterX = rect.Left + width / 2;
        int winCenterY = rect.Top + height / 2;

        const int snapThreshold = 14;

        bool snapL = false, snapR = false, snapT = false, snapB = false, snapCX = false, snapCY = false;

        // Horizontal snapping
        if (Math.Abs(rect.Left - workLeft) <= snapThreshold)
        {
            rect.Left = workLeft;
            rect.Right = workLeft + width;
            snapL = true;
        }
        else if (Math.Abs(rect.Right - workRight) <= snapThreshold)
        {
            rect.Right = workRight;
            rect.Left = workRight - width;
            snapR = true;
        }
        else if (Math.Abs(winCenterX - workCenterX) <= snapThreshold)
        {
            rect.Left = workCenterX - width / 2;
            rect.Right = rect.Left + width;
            snapCX = true;
        }

        // Vertical snapping
        if (Math.Abs(rect.Top - workTop) <= snapThreshold)
        {
            rect.Top = workTop;
            rect.Bottom = workTop + height;
            snapT = true;
        }
        else if (Math.Abs(rect.Bottom - workBottom) <= snapThreshold)
        {
            rect.Bottom = workBottom;
            rect.Top = workBottom - height;
            snapB = true;
        }
        else if (Math.Abs(winCenterY - workCenterY) <= snapThreshold)
        {
            rect.Top = workCenterY - height / 2;
            rect.Bottom = rect.Top + height;
            snapCY = true;
        }

        System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, true);

        SnapGuideLeft.Visibility = snapL ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideRight.Visibility = snapR ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideTop.Visibility = snapT ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideBottom.Visibility = snapB ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideCenterX.Visibility = snapCX ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideCenterY.Visibility = snapCY ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAllSnapGuides()
    {
        SnapGuideLeft.Visibility = Visibility.Collapsed;
        SnapGuideRight.Visibility = Visibility.Collapsed;
        SnapGuideTop.Visibility = Visibility.Collapsed;
        SnapGuideBottom.Visibility = Visibility.Collapsed;
        SnapGuideCenterX.Visibility = Visibility.Collapsed;
        SnapGuideCenterY.Visibility = Visibility.Collapsed;
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
        bool shouldAutoHide = _settings.AutoHideWhenPaused && (!isPlaying || !_mediaService.CurrentTrack.IsValid);
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

        // Window must be physically visible in WPF/DWM
        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }
        _trayService?.UpdateVisibilityState(true);

        bool showIdleClock = _settings.AutoHideWhenPaused && _settings.ShowIdleClock && _isAutoHidden;

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

                // Smoothly animate the root container opacity to 0.0 over 250ms
                var fadeOut = new DoubleAnimation(CardBorder.Opacity, 0.0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, e) =>
                {
                    if (_isAutoHidden && !_settings.ShowIdleClock)
                    {
                        StopRenderingLoop();
                    }
                };
                CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }
        else
        {
            // Resumed / normal playback
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

    private bool _isInIdleClockMode = false;

    private void TransitionToIdleClock()
    {
        if (IsFullscreenActive) return;
        if (_isInIdleClockMode) return;
        _isInIdleClockMode = true;

        Background = Brushes.Transparent;

        // Cancel in-progress animations on both roots before transition
        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, null);
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, null);

        var fadeOutMedia = new DoubleAnimation(MediaContentRoot.Opacity, 0.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOutMedia.Completed += (s, e) =>
        {
            if (_isInIdleClockMode)
            {
                MediaContentRoot.Visibility = Visibility.Collapsed;
            }
        };
        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, fadeOutMedia);

        IdleClockRoot.Visibility = Visibility.Visible;
        var fadeInClock = new DoubleAnimation(IdleClockRoot.Opacity, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, fadeInClock);

        ApplyIdleClockStyling();
        UpdateClockTime(0.016, forceUpdate: true);
    }

    private void ApplyIdleClockStyling()
    {
        if (!_isInIdleClockMode) return;

        if (_settings.BackgroundStyle == "Glass")
        {
            CardBorder.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x0A, 0x0C, 0x12));
            GlassBackgroundContainer.Visibility = Visibility.Visible;
            CardBorder.BorderThickness = new Thickness(1);
            CardBorder.BorderBrush = BuildSpecularBrush(_currentAccentColor);
            CardDropShadow.Opacity = _settings.GamingMode ? 0.0 : 0.5;
        }
        else
        {
            CardBorder.Background = Brushes.Transparent;
            GlassBackgroundContainer.Visibility = Visibility.Collapsed;
            CardBorder.BorderThickness = new Thickness(0);
            CardBorder.BorderBrush = Brushes.Transparent;
            CardDropShadow.Opacity = 0.0;
        }
    }

    private void TransitionFromIdleClock()
    {
        Background = Brushes.Transparent;
        if (IsFullscreenActive) return;
        if (!_isInIdleClockMode)
        {
            if (IdleClockRoot.Visibility != Visibility.Collapsed)
            {
                IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, null);
                IdleClockRoot.Visibility = Visibility.Collapsed;
                IdleClockRoot.Opacity = 0.0;
            }
            if (MediaContentRoot.Visibility != Visibility.Visible)
            {
                MediaContentRoot.Visibility = Visibility.Visible;
                MediaContentRoot.Opacity = 1.0;
            }
            return;
        }
        _isInIdleClockMode = false;

        // Cancel in-progress animations on both roots before transition
        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, null);
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, null);

        var fadeOutClock = new DoubleAnimation(IdleClockRoot.Opacity, 0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOutClock.Completed += (s, e) =>
        {
            if (!_isInIdleClockMode)
            {
                IdleClockRoot.Visibility = Visibility.Collapsed;
            }
        };
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, fadeOutClock);

        MediaContentRoot.Visibility = Visibility.Visible;
        var fadeInMedia = new DoubleAnimation(MediaContentRoot.Opacity, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, fadeInMedia);

        ApplyDisplayModeAndTheme();
    }

    private class ClockDigitSlot
    {
        public TextBlock CurrentBlock { get; set; } = null!;
        public TextBlock NextBlock { get; set; } = null!;
        public char DisplayedChar { get; set; } = ' ';
        public char TargetChar { get; set; } = ' ';
        public double CurrentY = 0.0;
        public double VelocityY = 0.0;
        public bool IsRolling { get; set; } = false;
    }

    private ClockDigitSlot[]? _clockSlots;

    private void EnsureClockSlotsInitialized()
    {
        if (_clockSlots != null) return;
        _clockSlots = new ClockDigitSlot[]
        {
            new() { CurrentBlock = ClockH1Current, NextBlock = ClockH1Next },
            new() { CurrentBlock = ClockH2Current, NextBlock = ClockH2Next },
            new() { CurrentBlock = ClockM1Current, NextBlock = ClockM1Next },
            new() { CurrentBlock = ClockM2Current, NextBlock = ClockM2Next }
        };
    }

    private void UpdateClockTime(double dt, bool forceUpdate = false)
    {
        EnsureClockSlotsInitialized();
        if (_clockSlots == null) return;

        var now = DateTime.Now;
        string timeStr = now.ToString("HHmm");
        IdleClockDateText.Text = now.ToString("dddd, MMMM d");

        for (int i = 0; i < 4; i++)
        {
            var slot = _clockSlots[i];
            char c = timeStr[i];

            if (forceUpdate || slot.DisplayedChar == ' ')
            {
                slot.DisplayedChar = c;
                slot.TargetChar = c;
                slot.CurrentBlock.Text = c.ToString();
                Canvas.SetTop(slot.CurrentBlock, 0);
                slot.CurrentBlock.Opacity = 1.0;
                Canvas.SetTop(slot.NextBlock, 58.0);
                slot.NextBlock.Opacity = 0.0;
                slot.IsRolling = false;
                slot.CurrentY = 0.0;
                slot.VelocityY = 0.0;
            }
            else if (slot.TargetChar != c && !slot.IsRolling)
            {
                slot.TargetChar = c;
                slot.NextBlock.Text = c.ToString();
                slot.CurrentY = 0.0;
                slot.VelocityY = 0.0;
                slot.IsRolling = true;
            }

            if (slot.IsRolling)
            {
                bool settled = SpringLineFluid.Step(ref slot.CurrentY, ref slot.VelocityY, -58.0, dt, 0.5);

                double progress = Math.Clamp(-slot.CurrentY / 58.0, 0.0, 1.0);
                Canvas.SetTop(slot.CurrentBlock, slot.CurrentY);
                slot.CurrentBlock.Opacity = 1.0 - progress;

                Canvas.SetTop(slot.NextBlock, 58.0 + slot.CurrentY);
                slot.NextBlock.Opacity = progress;

                if (settled || progress >= 0.99)
                {
                    slot.DisplayedChar = slot.TargetChar;
                    slot.CurrentBlock.Text = slot.TargetChar.ToString();
                    Canvas.SetTop(slot.CurrentBlock, 0);
                    slot.CurrentBlock.Opacity = 1.0;
                    Canvas.SetTop(slot.NextBlock, 58.0);
                    slot.NextBlock.Opacity = 0.0;
                    slot.IsRolling = false;
                    slot.CurrentY = 0.0;
                    slot.VelocityY = 0.0;
                }
            }
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
            UnlockToolbar.Visibility = Visibility.Collapsed;
            ResizeGripThumb.Visibility = Visibility.Collapsed;
            HideAllSnapGuides();
            ShowMediaControls(false);
        }
        else
        {
            UnlockToolbar.Visibility = Visibility.Visible;
            ResizeGripThumb.Visibility = Visibility.Visible;
        }

        ApplyDisplayModeAndTheme();
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

    private void SaveCurrentBounds()
    {
        if (_isClosing) return;

        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.IsLocked = _isLocked;
        _settings.IsHidden = _isManuallyHidden;
        _settings.HasCustomPosition = true;

        SettingsService.Save(_settings);
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
