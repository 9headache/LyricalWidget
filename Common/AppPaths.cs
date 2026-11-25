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
        // Never resolve next to the .exe or the working directory: state stored there travels with
        // zipped/shared copies of the app and makes new users look like returning ones.
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                throw new DirectoryNotFoundException("LocalAppData could not be resolved.");
            }

            string appDir = Path.Combine(localAppData, "LyricalWidget");
            EnsureSubdirectories(appDir);
            return appDir;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize LocalAppData directory: {ex.Message}");
            string fallbackDir = Path.Combine(Path.GetTempPath(), "LyricalWidget");
            EnsureSubdirectories(fallbackDir);
            return fallbackDir;
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
