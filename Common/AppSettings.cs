using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyricalWidget.Common;

public class AppSettings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 640;
    public double Height { get; set; } = 280;
    public bool IsLocked { get; set; } = false;
    public bool IsHidden { get; set; } = false;
    public double Opacity { get; set; } = 0.95;

    // Display & Styling
    public string DisplayMode { get; set; } = "AlbumAndLyrics"; // "LyricsOnly", "AlbumAndLyrics", "AlbumOnly"
    public string BackgroundStyle { get; set; } = "Glass"; // "Transparent", "Glass"
    public string LyricAlignment { get; set; } = "Center"; // "Center", "Left"
    public string VisibleLinesMode { get; set; } = "Auto"; // "Auto", "2", "3", "5"
    public string AnimationStyle { get; set; } = "Fluid Wave";

    public static string NormalizeAnimationStyle(string? style)
    {
        return "Fluid Wave";
    }

    public bool ThemeColoredLyrics { get; set; } = true;

    public double FontSize { get; set; } = 22.0;
    public int SyncOffsetMs { get; set; } = 150; // Default +150ms SMTC latency compensation
    public bool AutoHideWhenPaused { get; set; } = false;
    public bool ShowTrackSwitchBanner { get; set; } = true;

    // Performance & Gaming Mode
    public bool GamingMode { get; set; } = true; // Default ON: Software rasterization, no GPU contention/stalls
    public int FpsCap { get; set; } = 60; // 60 or 30 (Eco Cap)

    // Startup & Onboarding
    public bool HasSeenWelcome { get; set; } = false;
    public bool HasCustomPosition { get; set; } = false;

    // UX Features
    public bool ShowSubLines { get; set; } = true; // Romanization / Translation sub-lines (default ON)
    public bool ShowIdleClock { get; set; } = false; // Minimal glassify clock when auto-hidden (default OFF)
    public bool AudioReactiveGlow { get; set; } = true; // Audio-reactive bass breath glow (default ON)

    // Fullscreen Vibe & Kinetic Typography
    public string FullscreenLayoutMode { get; set; } = "Split"; // "Split", "Kinetic"
    public string FullscreenKineticVibe { get; set; } = "Auto"; // "Auto", "Calm", "Rap", "Pop"
    public string FullscreenKineticPreset { get; set; } = "Editorial"; // "Editorial", "Poster"
    public bool FullscreenKineticIcons { get; set; } = true; // default ON
    public bool PosterDimMode { get; set; } = true; // Night-safe muted album palette cap (default ON)

    // Backwards-compatible deserialization for older settings.json
    [JsonInclude]
    internal string? LyricAnimationStyle { set => AnimationStyle = NormalizeAnimationStyle(value); }

    [JsonInclude]
    internal string? AnimationIntensity { set => AnimationStyle = NormalizeAnimationStyle(value); }

    [JsonInclude]
    internal bool? IdleClockEnabled { set => ShowIdleClock = value ?? ShowIdleClock; }

    [JsonInclude]
    internal bool? AudioReactiveEnabled { set => AudioReactiveGlow = value ?? AudioReactiveGlow; }

    [JsonInclude]
    internal string? KineticStyleOverride
    {
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                FullscreenKineticVibe = value;
        }
    }

    [JsonInclude]
    internal string? KineticPreset
    {
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                FullscreenKineticPreset = value;
        }
    }
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            string filePath = AppPaths.SettingsFilePath;
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    if (settings.Width < 260) settings.Width = 640;
                    if (settings.Height < 140) settings.Height = 280;
                    if (settings.FontSize < 14 || settings.FontSize > 36) settings.FontSize = 22.0;
                    if (settings.Opacity < 0.2 || settings.Opacity > 1.0) settings.Opacity = 0.95;
                    if (settings.SyncOffsetMs < -500 || settings.SyncOffsetMs > 500) settings.SyncOffsetMs = 0;
                    if (string.IsNullOrWhiteSpace(settings.DisplayMode)) settings.DisplayMode = "AlbumAndLyrics";
                    if (string.IsNullOrWhiteSpace(settings.BackgroundStyle)) settings.BackgroundStyle = "Glass";
                    if (string.IsNullOrWhiteSpace(settings.LyricAlignment)) settings.LyricAlignment = "Center";
                    settings.AnimationStyle = "Fluid Wave";
                    if (settings.FpsCap != 30 && settings.FpsCap != 60) settings.FpsCap = 60;
                    if (settings.FullscreenLayoutMode != "Kinetic") settings.FullscreenLayoutMode = "Split";
                    if (string.IsNullOrWhiteSpace(settings.FullscreenKineticVibe)) settings.FullscreenKineticVibe = "Auto";
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string filePath = AppPaths.SettingsFilePath;
            string dir = Path.GetDirectoryName(filePath) ?? AppPaths.BaseDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempFile = filePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
