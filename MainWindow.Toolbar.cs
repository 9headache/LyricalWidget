using System;
using System.Windows;
using LyricalWidget.Common;

namespace LyricalWidget;

// Unlock toolbar handlers
public partial class MainWindow
{
    private void OnModeLyricsOnlyClicked(object sender, RoutedEventArgs e) => SetDisplayMode("LyricsOnly");
    private void OnModeSideBySideClicked(object sender, RoutedEventArgs e) => SetDisplayMode("AlbumAndLyrics");
    private void OnModeAlbumOnlyClicked(object sender, RoutedEventArgs e) => SetDisplayMode("AlbumOnly");

    private void OnBgTransparentClicked(object sender, RoutedEventArgs e) => SetBackgroundStyle("Transparent");
    private void OnBgGlassClicked(object sender, RoutedEventArgs e) => SetBackgroundStyle("Glass");

    private void OnLinesAutoClicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("Auto");
    private void OnLines2Clicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("2");
    private void OnLines3Clicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("3");
    private void OnLines5Clicked(object sender, RoutedEventArgs e) => SetVisibleLinesMode("5");

    private void OnBrowserClockClicked(object sender, RoutedEventArgs e) => SetBrowserMediaBehavior(BrowserMediaBehavior.ShowClock);
    private void OnBrowserLyricsClicked(object sender, RoutedEventArgs e) => SetBrowserMediaBehavior(BrowserMediaBehavior.YouTubeLyricsWithClockFallback);
    private void OnBrowserPlayerClicked(object sender, RoutedEventArgs e) => SetBrowserMediaBehavior(BrowserMediaBehavior.AlwaysShowPlayer);

    private void OnMotionMoonlightClicked(object sender, RoutedEventArgs e) => SetAnimationStyle(AppSettings.MoonlightBloomStyle);
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

    private void OnPacingPillChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.ShowKineticPacingPill = ChkPacingPill.IsChecked == true;
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
}
