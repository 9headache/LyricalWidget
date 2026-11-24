using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace LyricalWidget.Common;

/// <summary>
/// Loads application icon frames from embedded assembly resources.
/// </summary>
public static class IconHelper
{
    private const string IconResourceName = "LyricalWidget.Assets.app.ico";

    public static Stream? GetAppIconStream()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream(IconResourceName);
            if (stream != null) return stream;

            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase))
                {
                    return assembly.GetManifestResourceStream(name);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error retrieving icon resource stream: {ex.Message}");
        }
        return null;
    }

    public static Drawing.Icon LoadDrawingIcon(int size = 32)
    {
        try
        {
            using var stream = GetAppIconStream();
            if (stream != null)
            {
                return new Drawing.Icon(stream, size, size);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load drawing icon from stream: {ex.Message}");
        }
        return Drawing.SystemIcons.Application;
    }

    public static BitmapFrame? LoadWpfIcon()
    {
        try
        {
            using var stream = GetAppIconStream();
            if (stream != null)
            {
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load WPF icon from stream: {ex.Message}");
        }
        return null;
    }
}
