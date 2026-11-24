using System;
using System.IO;

namespace LyricalWidget.Common;

/// <summary>
/// Resolves and manages application directories for settings and lyrics cache.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _baseDirectoryLazy = new(ResolveBaseDirectory);

    public static string BaseDirectory => _baseDirectoryLazy.Value;

    public static string SettingsFilePath => Path.Combine(BaseDirectory, "settings.json");

    public static string CacheDirectory => Path.Combine(BaseDirectory, "cache");

    private static string ResolveBaseDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDir = Path.Combine(localAppData, "LyricalWidget");

        try
        {
            EnsureSubdirectories(appDir);
            MigrateLegacySettingsIfNeeded(appDir);
            return appDir;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize LocalAppData directory: {ex.Message}");
            string fallbackCurrent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            EnsureSubdirectories(fallbackCurrent);
            return fallbackCurrent;
        }
    }

    private static void MigrateLegacySettingsIfNeeded(string targetBaseDir)
    {
        try
        {
            string targetSettingsFile = Path.Combine(targetBaseDir, "settings.json");
            if (File.Exists(targetSettingsFile))
            {
                return; // Already exists, no migration needed
            }

            // Search potential previous local locations
            string[] candidateLocations = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "settings.json")
            };

            foreach (var candidate in candidateLocations)
            {
                try
                {
                    if (File.Exists(candidate) && !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(targetSettingsFile), StringComparison.OrdinalIgnoreCase))
                    {
                        var info = new FileInfo(candidate);
                        if (info.Length > 0)
                        {
                            File.Copy(candidate, targetSettingsFile, overwrite: false);
                            System.Diagnostics.Debug.WriteLine($"Migrated settings from {candidate} to {targetSettingsFile}");
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore candidate read errors
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings migration check failed: {ex.Message}");
        }
    }

    private static void EnsureSubdirectories(string basePath)
    {
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        string cacheDir = Path.Combine(basePath, "cache");
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }
    }
}
