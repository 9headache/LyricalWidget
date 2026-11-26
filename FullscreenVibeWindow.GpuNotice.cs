using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// "Performance Notice": shown once per session when Vibe Mode opens, until the user turns it off for good
public partial class FullscreenVibeWindow
{
    private static readonly TimeSpan GpuNoticeDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan GpuNoticeHold = TimeSpan.FromSeconds(12);
    private static bool _gpuNoticeShownThisSession;

    private DispatcherTimer? _gpuNoticeTimer;
    private bool _gpuNoticeLeaving;

    private void ScheduleGpuNotice()
    {
        if (_settings.DismissedVibeGpuNotice || _gpuNoticeShownThisSession) return;
        _gpuNoticeShownThisSession = true;

        _gpuNoticeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = GpuNoticeDelay };
        _gpuNoticeTimer.Tick += (s, e) =>
        {
            _gpuNoticeTimer.Stop();
            if (_isClosing) return;
            ShowGpuNotice();
        };
        _gpuNoticeTimer.Start();
    }

    private void ShowGpuNotice()
    {
        GpuNotice.Visibility = Visibility.Visible;
        GpuNotice.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        // Fades by itself; resting the mouse on it holds it
        _gpuNoticeTimer!.Interval = GpuNoticeHold;
        _gpuNoticeTimer.Tick -= OnGpuNoticeExpired;
        _gpuNoticeTimer.Tick += OnGpuNoticeExpired;
        _gpuNoticeTimer.Start();
    }

    private void OnGpuNoticeExpired(object? sender, EventArgs e)
    {
        if (GpuNotice.IsMouseOver) return;
        HideGpuNotice();
    }

    private void HideGpuNotice()
    {
        _gpuNoticeTimer?.Stop();
        if (_gpuNoticeLeaving || GpuNotice.Visibility != Visibility.Visible) return;
        _gpuNoticeLeaving = true;

        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (s, e) =>
        {
            GpuNotice.Visibility = Visibility.Collapsed;
            _gpuNoticeLeaving = false;
        };
        GpuNotice.BeginAnimation(OpacityProperty, fade);
    }

    private void OnGpuNoticeClicked(object sender, MouseButtonEventArgs e) => HideGpuNotice();

    private void OnGpuNoticeGotItClicked(object sender, RoutedEventArgs e) => HideGpuNotice();

    private void OnGpuNoticeDontShowClicked(object sender, RoutedEventArgs e)
    {
        _settings.DismissedVibeGpuNotice = true;
        SettingsService.Save(_settings);
        HideGpuNotice();
    }
}
