using System;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LyricalWidget.Common;
using LyricalWidget.Media;

namespace LyricalWidget;

// Album palette and lyric colors
public partial class FullscreenVibeWindow
{
    private int _paletteUpdateId = 0;

    private VibePalette _currentPalette = VibePalette.Default();
    private ThemedLyricColors _currentThemedLyricColors = new()
    {
        ActiveColor = Colors.White,
        InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
        AccentGlowColor = Color.FromRgb(0xD4, 0xD4, 0xD8)
    };

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

        ProgressStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Highlight, duration) { EasingFunction = ease });
        ProgressStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Secondary, duration) { EasingFunction = ease });
        PortraitProgressStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Highlight, duration) { EasingFunction = ease });
        PortraitProgressStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Secondary, duration) { EasingFunction = ease });

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

        KineticCornerStop0.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Highlight, duration) { EasingFunction = ease });
        KineticCornerStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(palette.Secondary, duration) { EasingFunction = ease });

        UpdateVisualsAccentColor(_vibeVisuals, _interludeSlot, palette.Primary);
        _currentThemedLyricColors = ComputeCurrentLyricColors(palette, _settings.ThemeColoredLyrics);
        ApplyLyricColorsToVisuals(_vibeVisuals, _currentThemedLyricColors, crossfade: !_settings.GamingMode);
    }

    public void SetThemeColoredLyrics(bool enabled)
    {
        _settings.ThemeColoredLyrics = enabled;
        BtnToggleThemeLyrics.Content = $"Theme Lyrics: {(enabled ? "ON" : "OFF")}";
        SettingsService.Save(_settings);
        _currentThemedLyricColors = ComputeCurrentLyricColors(_currentPalette, enabled);
        ApplyLyricColorsToVisuals(_vibeVisuals, _currentThemedLyricColors, crossfade: false);
    }
}
