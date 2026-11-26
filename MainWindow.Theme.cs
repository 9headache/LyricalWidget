using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LyricalWidget.Common;
using LyricalWidget.Media;
using LyricalWidget.Rendering;

namespace LyricalWidget;

public partial class MainWindow : Window
{
    private Color _currentAccentColor = Color.FromRgb(0xD4, 0xD4, 0xD8);
    private double _currentBassEnergy = 0.0;

    private ThemedLyricColors _currentThemedLyricColors = new()
    {
        ActiveColor = Colors.White,
        InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
        AccentGlowColor = Color.FromRgb(0xD4, 0xD4, 0xD8)
    };
    private VibePalette _currentPalette = VibePalette.Default();

    public void SetDisplayMode(string mode)
    {
        _settings.DisplayMode = mode;
        _trayService?.UpdateDisplayModeState(mode);
        ApplyDisplayModeAndTheme();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetBackgroundStyle(string style)
    {
        _settings.BackgroundStyle = style;
        _trayService?.UpdateBackgroundStyleState(style);
        ApplyDisplayModeAndTheme();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetLyricAlignment(string align)
    {
        _settings.LyricAlignment = align;
        ApplyDisplayModeAndTheme();
        RebuildLyricsViews();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetVisibleLinesMode(string linesMode)
    {
        _settings.VisibleLinesMode = linesMode;
        UpdateToolbarButtons();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetAnimationStyle(string style)
    {
        style = AppSettings.NormalizeAnimationStyle(style);
        bool wasMoonlight = _settings.IsMoonlightBloom;
        _settings.RestoreVibeLayout = false;
        _settings.AnimationStyle = style;
        _trayService?.UpdateAnimationStyleState(style);

        // The style is shared with fullscreen, whether it is open now or opened later
        if (_settings.IsMoonlightBloom)
        {
            _settings.FullscreenKineticPreset = nameof(KineticPreset.MoonlightBloom);
            _vibeWindow?.EnterMoonlightBloom();
        }
        else if (wasMoonlight)
        {
            _vibeWindow?.LeaveMoonlightBloom();
        }

        _currentActiveIndex = -1;
        _lastRenderEffectivePos = TimeSpan.Zero;
        RebuildLyricsViews();
        ApplyDisplayModeAndTheme();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    private void ApplyDisplayModeAndTheme()
    {
        string mode = _settings.DisplayMode;
        string bg = _settings.BackgroundStyle;
        bool hasLyrics = _currentLyrics.HasSyncedLyrics;

        UpdateToolbarButtons();

        if (_isInIdleClockMode)
        {
            ApplyIdleClockStyling();
            return;
        }

        bool moonlight = _settings.IsMoonlightBloom;
        bool moonlightCard = UsesMoonlightCard;
        MoonlightBackdrop.Visibility = moonlightCard ? Visibility.Visible : Visibility.Collapsed;
        LyricsOnlyMoonlightCanvas.Visibility = moonlight ? Visibility.Visible : Visibility.Collapsed;
        SideMoonlightCanvas.Visibility = moonlight ? Visibility.Visible : Visibility.Collapsed;
        LyricsOnlyCanvas.Visibility = moonlight ? Visibility.Collapsed : Visibility.Visible;
        SideLyricsCanvas.Visibility = moonlight ? Visibility.Collapsed : Visibility.Visible;
        ApplyCardGlowStyle(moonlightCard);

        // With Glassify, Moonlight Bloom brings its own card
        if (moonlightCard)
        {
            CardBorder.Background = Brushes.Transparent;
            GlassBackgroundContainer.Visibility = Visibility.Collapsed;
            CardBorder.BorderThickness = new Thickness(1);
            CardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        }
        else if (bg == "Glass")
        {
            CardBorder.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x0A, 0x0C, 0x12));
            GlassBackgroundContainer.Visibility = Visibility.Visible;
            CardBorder.BorderThickness = new Thickness(1);
            CardBorder.BorderBrush = BuildSpecularBrush(_currentAccentColor);
        }
        else
        {
            CardBorder.Background = Brushes.Transparent;
            GlassBackgroundContainer.Visibility = Visibility.Collapsed;

            if (_isLocked)
            {
                CardBorder.BorderThickness = new Thickness(0);
                CardBorder.BorderBrush = Brushes.Transparent;
            }
            else
            {
                CardBorder.BorderThickness = new Thickness(1.5);
                CardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            }
        }

        if (!hasLyrics || mode == "AlbumOnly")
        {
            LyricsOnlyView.Visibility = Visibility.Collapsed;
            SideBySideView.Visibility = Visibility.Collapsed;
            AlbumOnlyView.Visibility = Visibility.Visible;
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
        }
        else if (mode == "LyricsOnly")
        {
            LyricsOnlyView.Visibility = Visibility.Visible;
            SideBySideView.Visibility = Visibility.Collapsed;
            AlbumOnlyView.Visibility = Visibility.Collapsed;
        }
        else
        {
            LyricsOnlyView.Visibility = Visibility.Collapsed;
            SideBySideView.Visibility = Visibility.Visible;
            AlbumOnlyView.Visibility = Visibility.Collapsed;
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
        }

        ApplyWidgetFisheye();
    }

    private static LinearGradientBrush BuildSpecularBrush(Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x35, accent.R, accent.G, accent.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    private void ApplyCardGlowStyle(bool moonlight)
    {
        CardDropShadow.Color = moonlight ? Colors.White : Colors.Black;
        CardDropShadow.ShadowDepth = moonlight ? 0.0 : 4.0;
        CardDropShadow.Opacity = _settings.GamingMode ? 0.0 : moonlight ? 0.14 : 0.5;
    }

    private bool UsesMoonlightCard => _settings.IsMoonlightBloom && _settings.BackgroundStyle == "Glass";

    private static readonly Brush PillBrush = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush PillActiveBrush = Frozen(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
    private static readonly Brush PillBorder = Frozen(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
    private static readonly Brush PillActiveBorder = Frozen(Color.FromRgb(0xD4, 0xD4, 0xD8));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateToolbarButtons()
    {
        var activeBrush = PillActiveBrush;
        var defaultBrush = PillBrush;
        var activeBorder = PillActiveBorder;
        var defaultBorder = PillBorder;

        BtnModeLyricsOnly.Background = _settings.DisplayMode == "LyricsOnly" ? activeBrush : defaultBrush;
        BtnModeLyricsOnly.BorderBrush = _settings.DisplayMode == "LyricsOnly" ? activeBorder : defaultBorder;

        BtnModeSideBySide.Background = _settings.DisplayMode == "AlbumAndLyrics" ? activeBrush : defaultBrush;
        BtnModeSideBySide.BorderBrush = _settings.DisplayMode == "AlbumAndLyrics" ? activeBorder : defaultBorder;

        BtnModeAlbumOnly.Background = _settings.DisplayMode == "AlbumOnly" ? activeBrush : defaultBrush;
        BtnModeAlbumOnly.BorderBrush = _settings.DisplayMode == "AlbumOnly" ? activeBorder : defaultBorder;

        BtnBgTransparent.Background = _settings.BackgroundStyle == "Transparent" ? activeBrush : defaultBrush;
        BtnBgTransparent.BorderBrush = _settings.BackgroundStyle == "Transparent" ? activeBorder : defaultBorder;

        BtnBgGlass.Background = _settings.BackgroundStyle == "Glass" ? activeBrush : defaultBrush;
        BtnBgGlass.BorderBrush = _settings.BackgroundStyle == "Glass" ? activeBorder : defaultBorder;

        BtnLinesAuto.Background = _settings.VisibleLinesMode == "Auto" ? activeBrush : defaultBrush;
        BtnLinesAuto.BorderBrush = _settings.VisibleLinesMode == "Auto" ? activeBorder : defaultBorder;

        BtnLines2.Background = _settings.VisibleLinesMode == "2" ? activeBrush : defaultBrush;
        BtnLines2.BorderBrush = _settings.VisibleLinesMode == "2" ? activeBorder : defaultBorder;

        BtnLines3.Background = _settings.VisibleLinesMode == "3" ? activeBrush : defaultBrush;
        BtnLines3.BorderBrush = _settings.VisibleLinesMode == "3" ? activeBorder : defaultBorder;

        BtnLines5.Background = _settings.VisibleLinesMode == "5" ? activeBrush : defaultBrush;
        BtnLines5.BorderBrush = _settings.VisibleLinesMode == "5" ? activeBorder : defaultBorder;

        var browserMode = _settings.BrowserMediaBehavior;
        BtnBrowserClock.Background = browserMode == BrowserMediaBehavior.ShowClock ? activeBrush : defaultBrush;
        BtnBrowserClock.BorderBrush = browserMode == BrowserMediaBehavior.ShowClock ? activeBorder : defaultBorder;

        BtnBrowserLyrics.Background = browserMode == BrowserMediaBehavior.YouTubeLyricsWithClockFallback ? activeBrush : defaultBrush;
        BtnBrowserLyrics.BorderBrush = browserMode == BrowserMediaBehavior.YouTubeLyricsWithClockFallback ? activeBorder : defaultBorder;

        BtnBrowserPlayer.Background = browserMode == BrowserMediaBehavior.AlwaysShowPlayer ? activeBrush : defaultBrush;
        BtnBrowserPlayer.BorderBrush = browserMode == BrowserMediaBehavior.AlwaysShowPlayer ? activeBorder : defaultBorder;

        bool isMoonlight = _settings.IsMoonlightBloom;
        BtnMotionFluid.Background = isMoonlight ? defaultBrush : activeBrush;
        BtnMotionFluid.BorderBrush = isMoonlight ? defaultBorder : activeBorder;
        BtnMotionMoonlight.Background = isMoonlight ? activeBrush : defaultBrush;
        BtnMotionMoonlight.BorderBrush = isMoonlight ? activeBorder : defaultBorder;
        BtnFisheye.Visibility = isMoonlight ? Visibility.Visible : Visibility.Collapsed;
        BtnFisheye.Background = _settings.WidgetFisheyeEnabled ? activeBrush : defaultBrush;
        BtnFisheye.BorderBrush = _settings.WidgetFisheyeEnabled ? activeBorder : defaultBorder;

        ChkThemeLyrics.IsChecked = _settings.ThemeColoredLyrics;

        BtnAlignCenter.Background = _settings.LyricAlignment == "Center" ? activeBrush : defaultBrush;
        BtnAlignCenter.BorderBrush = _settings.LyricAlignment == "Center" ? activeBorder : defaultBorder;

        BtnAlignLeft.Background = _settings.LyricAlignment == "Left" ? activeBrush : defaultBrush;
        BtnAlignLeft.BorderBrush = _settings.LyricAlignment == "Left" ? activeBorder : defaultBorder;
    }

    private void UpdateAccentColors(Color accent)
    {
        _currentAccentColor = accent;

        byte r2 = (byte)Math.Min(255, accent.R + 45);
        byte g2 = (byte)Math.Min(255, accent.G + 35);
        byte b2 = (byte)Math.Min(255, accent.B + 55);

        var progressBrush = new LinearGradientBrush(accent, Color.FromRgb(r2, g2, b2), new Point(0, 0), new Point(1, 0));
        progressBrush.Freeze();

        SideProgressBarBorder.Background = progressBrush;
        AlbumOnlyProgressBarBorder.Background = progressBrush;

        SideAlbumAuraGlow.Color = accent;
        AlbumOnlyShadow.Color = accent;

        if (_settings.BackgroundStyle == "Glass" && !_isInIdleClockMode && !_settings.IsMoonlightBloom)
        {
            CardBorder.BorderBrush = BuildSpecularBrush(accent);
        }

        UpdateVisualsAccentColor(_lyricsOnlyVisuals, _lyricsOnlyInterludeSlot, accent);
        UpdateVisualsAccentColor(_sideLyricsVisuals, _sideLyricsInterludeSlot, accent);
    }

    private static void UpdateVisualsAccentColor(List<LyricLineVisualItem> visuals, InterludeSlotItem? slot, Color accent)
    {
        foreach (var line in visuals)
        {
            if (line.Words != null)
            {
                foreach (var w in line.Words)
                {
                    w.GlowEffect.Color = accent;
                }
            }
        }
        if (slot != null)
        {
            if (slot.Dot1.Effect is DropShadowEffect d1) d1.Color = accent;
            if (slot.Dot2.Effect is DropShadowEffect d2) d2.Color = accent;
            if (slot.Dot3.Effect is DropShadowEffect d3) d3.Color = accent;
        }
    }

    public void SetThemeColoredLyrics(bool enabled)
    {
        _settings.ThemeColoredLyrics = enabled;
        ChkThemeLyrics.IsChecked = enabled;
        _trayService?.UpdateThemeColoredLyricsState(enabled);
        SettingsService.Save(_settings);
        UpdatePresetHighlight();
        ApplyThemedLyricColors(_currentPalette, crossfade: false);
        if (IsFullscreenActive && _vibeWindow != null)
        {
            _vibeWindow.SetThemeColoredLyrics(enabled);
        }
    }

    private ThemedLyricColors ComputeCurrentLyricColors(VibePalette palette, bool enabled)
    {
        if (enabled)
        {
            return ColorExtractor.ComputeThemedLyricColors(palette, true);
        }

        return new ThemedLyricColors
        {
            ActiveColor = Colors.White,
            InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
            AccentGlowColor = Color.FromRgb(0xD4, 0xD4, 0xD8)
        };
    }

    private void ApplyThemedLyricColors(VibePalette palette, bool crossfade)
    {
        bool isGaming = _settings.GamingMode;
        _currentThemedLyricColors = ComputeCurrentLyricColors(palette, _settings.ThemeColoredLyrics);
        ApplyLyricColorsToVisuals(_lyricsOnlyVisuals, _currentThemedLyricColors, crossfade && !isGaming, isGaming);
        ApplyLyricColorsToVisuals(_sideLyricsVisuals, _currentThemedLyricColors, crossfade && !isGaming, isGaming);

        // Moonlight Bloom follows the same switch: album colours when on, plain silver when off
        var tint = _settings.ThemeColoredLyrics ? MoonlightTint.FromPalette(palette.Primary, palette.Accent, palette.IsMonochrome) : null;
        _lyricsOnlyMoonlight.SetTint(tint);
        _sideMoonlight.SetTint(tint);
    }

    private static void ApplyLyricColorsToVisuals(List<LyricLineVisualItem> visuals, ThemedLyricColors colors, bool crossfade, bool isGamingMode = false)
    {
        FluidLyricWaveHelper.ApplyLyricColorsToVisuals(visuals, colors, crossfade, isGamingMode);
    }

    public void SetAudioReactiveGlow(bool enabled)
    {
        _settings.AudioReactiveGlow = enabled;
        _trayService?.UpdateAudioReactiveGlowState(enabled);
        if (ChkAudioReactive.IsChecked != enabled)
        {
            ChkAudioReactive.IsChecked = enabled;
        }
        SettingsService.Save(_settings);
        UpdatePresetHighlight();
        _audioBeatService.UpdateState(_mediaService.IsPlaying, _settings.GamingMode, enabled);
    }

    private void OnBassEnergyChanged(double bass)
    {
        _currentBassEnergy = bass;
    }

    public void ApplyGamingMode(bool enabled)
    {
        Background = Brushes.Transparent;
        _settings.GamingMode = enabled;
        _trayService?.UpdateGamingModeState(enabled);
        UpdateFullscreenPill();
        if (ChkGamingMode.IsChecked != enabled)
        {
            ChkGamingMode.IsChecked = enabled;
        }
        _audioBeatService.UpdateState(_mediaService.IsPlaying, enabled, _settings.AudioReactiveGlow);

        try
        {
            RenderOptions.ProcessRenderMode = enabled ? RenderMode.SoftwareOnly : RenderMode.Default;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set ProcessRenderMode: {ex.Message}");
        }

        double cardShadowOpacity = enabled ? 0.0 : UsesMoonlightCard ? 0.14 : 0.5;
        double auraShadowOpacity = enabled ? 0.0 : 0.55;

        CardDropShadow.Opacity = cardShadowOpacity;
        ApplyEffectBudget();
        ApplyWidgetFisheye();
        SideAlbumAuraGlow.Opacity = auraShadowOpacity;
        AlbumOnlyShadow.Opacity = auraShadowOpacity;

        // Word shadows would be emulated on the CPU in software rendering
        if (enabled)
        {
            foreach (var item in _lyricsOnlyVisuals)
            {
                if (item.Words != null)
                {
                    foreach (var w in item.Words)
                    {
                        w.GlowEffect.Opacity = 0.0;
                        w.CurrentGlowOpacity = 0.0;
                    }
                }
            }
            foreach (var item in _sideLyricsVisuals)
            {
                if (item.Words != null)
                {
                    foreach (var w in item.Words)
                    {
                        w.GlowEffect.Opacity = 0.0;
                        w.CurrentGlowOpacity = 0.0;
                    }
                }
            }
        }

        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetFpsCap(int fps)
    {
        _settings.FpsCap = fps <= 30 ? 30 : 60;
        _trayService?.UpdateFpsCapState(_settings.FpsCap);
        ChkEcoFps.IsChecked = (_settings.FpsCap <= 30);
        SaveCurrentBounds();
    }
}
