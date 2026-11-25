using System;
using System.Collections.Generic;
using System.Linq;
using LyricalWidget.Interop;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace LyricalWidget.Services;

/// <summary>A connected display. Bounds are in the coordinates SetWindowPos uses in this process.</summary>
public sealed record MonitorTarget(string DeviceName, string Label, Drawing.Rectangle Bounds, Drawing.Rectangle WorkingArea, bool IsPrimary);

public static class MonitorService
{
    public static IReadOnlyList<MonitorTarget> GetMonitors()
    {
        var screens = WinForms.Screen.AllScreens;
        return screens
            .Select((s, i) =>
            {
                // Screen.Bounds is DPI-scaled for displays that differ from the system DPI; the mode is the real resolution
                var size = NativeMethods.GetDisplayResolution(s.DeviceName) ?? (s.Bounds.Width, s.Bounds.Height);
                return new MonitorTarget(s.DeviceName, BuildLabel(s.DeviceName, i, s.Primary, size.Item1, size.Item2), s.Bounds, s.WorkingArea, s.Primary);
            })
            .OrderBy(m => m.Label, StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildLabel(string deviceName, int index, bool isPrimary, int width, int height)
    {
        string digits = new(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        string number = digits.Length > 0 ? digits : (index + 1).ToString();
        bool vertical = height > width;
        string kind = isPrimary ? (vertical ? "Primary, Vertical" : "Primary") : vertical ? "Vertical" : "Landscape";
        return $"Display {number} — {kind} ({width}×{height})";
    }

    /// <summary>The preferred display, or the primary one when none is set or it is no longer connected.</summary>
    public static MonitorTarget? Resolve(IReadOnlyList<MonitorTarget> monitors, string? preferredDevice)
    {
        return monitors.FirstOrDefault(m => m.DeviceName == preferredDevice)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors.FirstOrDefault();
    }

    public static MonitorTarget? FromPoint(IReadOnlyList<MonitorTarget> monitors, int x, int y)
    {
        return monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));
    }

    /// <summary>
    /// Where a window should open: its saved place when that is on the wanted display (or, with no preference,
    /// on any connected display), otherwise centred on the wanted display at its saved size.
    /// </summary>
    public static Drawing.Rectangle ChoosePlacement(IReadOnlyList<MonitorTarget> monitors, string? preferredDevice, Drawing.Rectangle? saved)
    {
        var target = Resolve(monitors, preferredDevice);
        if (target == null) return saved ?? new Drawing.Rectangle(100, 100, 1280, 800);

        bool hasPreference = preferredDevice != null;
        if (saved is { } s)
        {
            var centre = FromPoint(monitors, s.X + (s.Width / 2), s.Y + (s.Height / 2));
            if (centre != null && (!hasPreference || centre.DeviceName == target.DeviceName)) return s;
        }

        var work = target.WorkingArea;
        int width = Math.Min(saved?.Width ?? (int)(work.Width * 0.7), work.Width);
        int height = Math.Min(saved?.Height ?? (int)(work.Height * 0.75), work.Height);
        return new Drawing.Rectangle(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height);
    }
}
