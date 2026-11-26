using System;
using System.Collections.Generic;
using System.Linq;

namespace LyricalWidget.Common;

/// <summary>A snapshot of how the app looks and behaves. A null value leaves that setting as it is.</summary>
public sealed record WidgetState
{
    public string? DisplayMode { get; init; }
    public string? BackgroundStyle { get; init; }
    public string? AnimationStyle { get; init; }
    public string? LyricAlignment { get; init; }
    public string? VisibleLinesMode { get; init; }
    public bool? ThemeColoredLyrics { get; init; }
    public double? FontSize { get; init; }
    public double? Opacity { get; init; }
    public int? SyncOffsetMs { get; init; }
    public double? AlbumArtSize { get; init; }
    public bool? GamingMode { get; init; }
    public int? FpsCap { get; init; }
    public bool? ShowTrackSwitchBanner { get; init; }
    public bool? AutoHideWhenPaused { get; init; }
    public bool? ShowIdleClock { get; init; }
    public bool? ShowSubLines { get; init; }
    public bool? AudioReactiveGlow { get; init; }
    public bool? ShowKineticPacingPill { get; init; }
    public bool? WidgetFisheye { get; init; }
    public double? WidgetLeft { get; init; }
    public double? WidgetTop { get; init; }
    public double? WidgetWidth { get; init; }
    public double? WidgetHeight { get; init; }
    public BrowserMediaBehavior? BrowserMedia { get; init; }

    public string? VibeLayoutMode { get; init; }
    public string? VibeKineticVibe { get; init; }
    public string? VibeKineticPreset { get; init; }
    public bool? VibeFisheye { get; init; }
    public string? VibeSpotlightBackdrop { get; init; }
    public bool? VibePosterDim { get; init; }
    public bool? VibeFullscreen { get; init; }
    public string? VibeWindowState { get; init; }
    public string? VibeMonitor { get; init; }
    public VibeWindowBounds? VibeBounds { get; init; }

    public static WidgetState Capture(AppSettings s) => new()
    {
        DisplayMode = s.DisplayMode,
        BackgroundStyle = s.BackgroundStyle,
        AnimationStyle = s.AnimationStyle,
        LyricAlignment = s.LyricAlignment,
        VisibleLinesMode = s.VisibleLinesMode,
        ThemeColoredLyrics = s.ThemeColoredLyrics,
        FontSize = s.FontSize,
        Opacity = s.Opacity,
        SyncOffsetMs = s.SyncOffsetMs,
        AlbumArtSize = s.WidgetAlbumArtSize,
        GamingMode = s.GamingMode,
        FpsCap = s.FpsCap,
        ShowTrackSwitchBanner = s.ShowTrackSwitchBanner,
        AutoHideWhenPaused = s.AutoHideWhenPaused,
        ShowIdleClock = s.ShowIdleClock,
        ShowSubLines = s.ShowSubLines,
        AudioReactiveGlow = s.AudioReactiveGlow,
        ShowKineticPacingPill = s.ShowKineticPacingPill,
        WidgetFisheye = s.WidgetFisheyeEnabled,
        WidgetLeft = s.Left,
        WidgetTop = s.Top,
        WidgetWidth = s.Width,
        WidgetHeight = s.Height,
        BrowserMedia = s.BrowserMediaBehavior,
        VibeLayoutMode = s.FullscreenLayoutMode,
        VibeKineticVibe = s.FullscreenKineticVibe,
        VibeKineticPreset = s.FullscreenKineticPreset,
        VibeFisheye = s.MoonlightFisheyeEnabled,
        VibeSpotlightBackdrop = s.SpotlightBackdropMode,
        VibePosterDim = s.PosterDimMode,
        VibeFullscreen = s.VibeIsFullscreen,
        VibeWindowState = s.VibeWindowState,
        VibeMonitor = s.PreferredVibeMonitor,
        VibeBounds = s.VibeWindowBounds
    };

    public void ApplyTo(AppSettings s)
    {
        s.DisplayMode = DisplayMode ?? s.DisplayMode;
        s.BackgroundStyle = BackgroundStyle ?? s.BackgroundStyle;
        s.AnimationStyle = AnimationStyle ?? s.AnimationStyle;
        s.LyricAlignment = LyricAlignment ?? s.LyricAlignment;
        s.VisibleLinesMode = VisibleLinesMode ?? s.VisibleLinesMode;
        s.ThemeColoredLyrics = ThemeColoredLyrics ?? s.ThemeColoredLyrics;
        s.FontSize = FontSize ?? s.FontSize;
        s.Opacity = Opacity ?? s.Opacity;
        s.SyncOffsetMs = SyncOffsetMs ?? s.SyncOffsetMs;
        s.WidgetAlbumArtSize = AlbumArtSize ?? s.WidgetAlbumArtSize;
        s.GamingMode = GamingMode ?? s.GamingMode;
        s.FpsCap = FpsCap ?? s.FpsCap;
        s.ShowTrackSwitchBanner = ShowTrackSwitchBanner ?? s.ShowTrackSwitchBanner;
        s.AutoHideWhenPaused = AutoHideWhenPaused ?? s.AutoHideWhenPaused;
        s.ShowIdleClock = ShowIdleClock ?? s.ShowIdleClock;
        s.ShowSubLines = ShowSubLines ?? s.ShowSubLines;
        s.AudioReactiveGlow = AudioReactiveGlow ?? s.AudioReactiveGlow;
        s.ShowKineticPacingPill = ShowKineticPacingPill ?? s.ShowKineticPacingPill;
        s.WidgetFisheyeEnabled = WidgetFisheye ?? s.WidgetFisheyeEnabled;
        s.Left = WidgetLeft ?? s.Left;
        s.Top = WidgetTop ?? s.Top;
        s.Width = WidgetWidth ?? s.Width;
        s.Height = WidgetHeight ?? s.Height;
        s.BrowserMediaBehavior = BrowserMedia ?? s.BrowserMediaBehavior;
        s.FullscreenLayoutMode = VibeLayoutMode ?? s.FullscreenLayoutMode;
        s.FullscreenKineticVibe = VibeKineticVibe ?? s.FullscreenKineticVibe;
        s.FullscreenKineticPreset = VibeKineticPreset ?? s.FullscreenKineticPreset;
        s.MoonlightFisheyeEnabled = VibeFisheye ?? s.MoonlightFisheyeEnabled;
        s.SpotlightBackdropMode = VibeSpotlightBackdrop ?? s.SpotlightBackdropMode;
        s.PosterDimMode = VibePosterDim ?? s.PosterDimMode;
        s.VibeIsFullscreen = VibeFullscreen ?? s.VibeIsFullscreen;
        s.VibeWindowState = VibeWindowState ?? s.VibeWindowState;
        s.PreferredVibeMonitor = VibeMonitor ?? s.PreferredVibeMonitor;
        s.VibeWindowBounds = VibeBounds ?? s.VibeWindowBounds;
    }

    /// <summary>True when applying this would change nothing. Where the widget and the Vibe window happen to sit does not count.</summary>
    public bool Matches(AppSettings settings)
    {
        var current = Capture(settings);
        var applied = new AppSettings();
        current.ApplyTo(applied);
        ApplyTo(applied);
        SettingsService.Normalize(applied);
        return Capture(applied) with
        {
            VibeBounds = current.VibeBounds,
            VibeWindowState = current.VibeWindowState,
            WidgetLeft = current.WidgetLeft,
            WidgetTop = current.WidgetTop,
            WidgetWidth = current.WidgetWidth,
            WidgetHeight = current.WidgetHeight
        } == current;
    }
}

public sealed record WidgetPreset
{
    public string Name { get; init; } = "";
    public WidgetState State { get; init; } = new();

    public const int MaxNameLength = 24;
    public const int MaxUserPresets = 12;

    public static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Drops anything unusable from a hand-edited settings file.</summary>
    public static List<WidgetPreset> Clean(IEnumerable<WidgetPreset?>? presets)
    {
        var clean = new List<WidgetPreset>();
        foreach (var preset in presets ?? Enumerable.Empty<WidgetPreset?>())
        {
            if (preset?.State == null) continue;
            string name = CleanName(preset.Name);
            if (name.Length == 0 || clean.Any(p => SameName(p.Name, name))) continue;
            clean.Add(preset with { Name = name });
            if (clean.Count == MaxUserPresets) break;
        }
        return clean;
    }

    public static string CleanName(string? name)
    {
        string clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > MaxNameLength ? clean[..MaxNameLength].Trim() : clean;
    }
}
