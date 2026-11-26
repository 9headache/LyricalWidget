using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricalWidget.Common;
using LyricalWidget.Rendering;

namespace LyricalWidget;

// Optional wide-angle lens over the widget's Moonlight Bloom lyrics, and the Game Mode warning that comes with it
public partial class MainWindow
{
    private WidgetFisheyeEffect? _fisheyeEffect;
    private bool _fisheyeWarningSeen = false; // Dismissed for this session

    private void InitWidgetFisheye()
    {
        try
        {
            // One frozen shader shared by both stages; only the one on screen ever repaints
            _fisheyeEffect = new WidgetFisheyeEffect();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fisheye shader unavailable: {ex.Message}");
        }
        LyricsOnlyMoonlightStage.SizeChanged += (s, e) => ApplyLensMargin(LyricsOnlyMoonlightCanvas, e.NewSize);
        SideMoonlightStage.SizeChanged += (s, e) => ApplyLensMargin(SideMoonlightCanvas, e.NewSize);
        FisheyeWarningPopup.CustomPopupPlacementCallback = PlaceFullscreenPill;
        DpiChanged += (s, e) => ApplyWidgetFisheye();
    }

    private bool UsesWidgetLens => _settings.WidgetFisheyeEnabled && _settings.IsMoonlightBloom && _fisheyeEffect != null;

    private void OnFisheyeClicked(object sender, RoutedEventArgs e)
    {
        _settings.WidgetFisheyeEnabled = !_settings.WidgetFisheyeEnabled;
        ApplyWidgetFisheye();
        UpdateToolbarButtons();
        SaveCurrentBounds();
    }

    private void ApplyWidgetFisheye()
    {
        bool lens = UsesWidgetLens && !_isInIdleClockMode;
        SetLens(LyricsOnlyMoonlightStage, lens && LyricsOnlyView.Visibility == Visibility.Visible);
        SetLens(SideMoonlightStage, lens && SideBySideView.Visibility == Visibility.Visible);

        // Smaller lines and a capped zoom keep the words inside the lens's undistorted middle
        _lyricsOnlyMoonlight.LensMode = lens;
        _sideMoonlight.LensMode = lens;
        ApplyLensMargin(LyricsOnlyMoonlightCanvas, LyricsOnlyMoonlightStage.RenderSize);
        ApplyLensMargin(SideMoonlightCanvas, SideMoonlightStage.RenderSize);
        UpdateFisheyeWarning();
    }

    private void SetLens(FrameworkElement stage, bool active)
    {
        stage.Effect = active ? _fisheyeEffect : null;

        // The lens magnifies the middle by up to 1 / (1 - Strength). Feeding it a screen-resolution picture would
        // stretch that picture's pixels, so it gets one drawn at that magnification. The words on the stage are
        // cached bitmaps themselves, so this is a resample, not a redraw, and it is skipped whenever nothing moved.
        double scale = VisualTreeHelper.GetDpi(this).PixelsPerDip / (1.0 - FisheyeLens.Strength);
        if (!active)
        {
            stage.CacheMode = null;
        }
        else if (stage.CacheMode is not BitmapCache cache || System.Math.Abs(cache.RenderAtScale - scale) > 0.01)
        {
            stage.CacheMode = new BitmapCache { RenderAtScale = scale, SnapsToDevicePixels = false };
        }
    }

    private void ApplyLensMargin(Canvas canvas, Size stage)
    {
        canvas.Margin = UsesWidgetLens && stage.Width > 0
            ? new Thickness(stage.Width * 0.12, stage.Height * 0.10, stage.Width * 0.12, stage.Height * 0.10)
            : new Thickness(0);
    }

    // Shown while Fisheye and Game Mode are both on, and only while the widget is unlocked, so it is never in front of a game
    private void UpdateFisheyeWarning()
    {
        bool show = UsesWidgetLens && _settings.GamingMode && !_fisheyeWarningSeen && !_settings.DismissedFisheyeGameModeWarning &&
                    !_isLocked && !_isClosing && !IsFullscreenActive && IsLoaded && IsVisible && Opacity > 0.5 &&
                    WindowState != WindowState.Minimized;
        if (show == FisheyeWarningPopup.IsOpen) return;
        FisheyeWarningPopup.IsOpen = show;
    }

    private void RepositionFisheyeWarning()
    {
        if (!FisheyeWarningPopup.IsOpen) return;
        FisheyeWarningPopup.HorizontalOffset = FisheyeWarningPopup.HorizontalOffset == 0.0 ? 0.001 : 0.0;
    }

    private void OnFisheyeWarningGotItClicked(object sender, RoutedEventArgs e)
    {
        _fisheyeWarningSeen = true;
        UpdateFisheyeWarning();
    }

    private void OnFisheyeWarningDontShowClicked(object sender, RoutedEventArgs e)
    {
        _settings.DismissedFisheyeGameModeWarning = true;
        SettingsService.Save(_settings);
        UpdateFisheyeWarning();
    }
}
