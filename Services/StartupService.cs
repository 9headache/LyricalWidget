using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace LyricalWidget.Services;

/// <summary>
/// Manages automatic startup with Windows via the CurrentVersion\Run registry key.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LyricalWidget";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            string? val = key?.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(val)) return false;

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            return val.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read startup registry: {ex.Message}");
            return false;
        }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update startup registry: {ex.Message}");
        }
    }
}
