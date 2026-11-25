using System;
using System.Windows;
using System.Windows.Controls;
using LyricalWidget.Common;

namespace LyricalWidget;

// Wide-angle lens for Moonlight Bloom
public partial class FullscreenVibeWindow
{
    private void OnToggleFisheyeClicked(object sender, RoutedEventArgs e)
    {
        _settings.MoonlightFisheyeEnabled = !_settings.MoonlightFisheyeEnabled;
        SettingsService.Save(_settings);
        ApplyFisheye();
        UpdateFisheyeButton();
    }

    private void UpdateFisheyeButton()
    {
        BtnToggleFisheye.Visibility = IsMoonlightActive ? Visibility.Visible : Visibility.Collapsed;
        BtnToggleFisheye.Content = $"Fisheye: {(_settings.MoonlightFisheyeEnabled ? "ON" : "OFF")}";
    }

    private void ApplyFisheye()
    {
        bool active = _settings.MoonlightFisheyeEnabled && MoonlightHost.Visibility == Visibility.Visible;
        _fisheyeLens.SetActive(active);
        MoonlightStageWrap.Opacity = active ? 0.0 : 1.0;
        MoonlightLensVignette.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _moonlightRenderer.LensMode = _settings.MoonlightFisheyeEnabled;
        UpdateMoonlightMargin(RootGrid.ActualWidth, RootGrid.ActualHeight);
    }

    /// <summary>Room around the lyrics: clear of the toolbar and badge, or the lens safe zone.</summary>
    private void UpdateMoonlightMargin(double w, double h)
    {
        if (w < 50 || h < 50) return;

        if (_settings.MoonlightFisheyeEnabled)
        {
            MoonlightCanvas.Margin = new Thickness(w * 0.14, h * 0.12, w * 0.14, h * 0.12);
            return;
        }
        double top = Grid.GetRow(TopModeBar) == 1 ? TopChrome.ActualHeight + 10.0 : 0.0;
        MoonlightCanvas.Margin = new Thickness(0, Math.Max(48.0, top), 0, h < 600 ? 88 : 48);
    }
}
