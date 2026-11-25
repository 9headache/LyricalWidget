using System;
using System.Windows.Media;
using LyricalWidget.Lyrics;

namespace LyricalWidget.Rendering;

public sealed partial class KineticLiveCodeRenderer
{
    private Brush GetTokenBrush(CodeTokenType type)
    {
        return type switch
        {
            CodeTokenType.Keyword => new SolidColorBrush(_keywordColor),
            CodeTokenType.Function => new SolidColorBrush(_functionColor),
            CodeTokenType.StringLiteral => new SolidColorBrush(_stringColor),
            CodeTokenType.Operator => new SolidColorBrush(_operatorColor),
            CodeTokenType.Number => new SolidColorBrush(_numberColor),
            CodeTokenType.Comment => new SolidColorBrush(_commentColor),
            CodeTokenType.Punctuation => new SolidColorBrush(_punctuationColor),
            _ => new SolidColorBrush(_identifierColor)
        };
    }

    private static Color EnsureVibrantAccent(Color raw, string? seed)
    {
        RgbToHsv(raw, out double h, out double s, out double v);

        // Dark, monochrome or washed-out covers fall back to fixed accents
        if (s < 0.35 || v < 0.45)
        {
            Color[] neonPalette =
            {
                Color.FromRgb(0xFF, 0x4D, 0x6D),
                Color.FromRgb(0xA7, 0x8B, 0xFA),
                Color.FromRgb(0x38, 0xBD, 0xF8),
                Color.FromRgb(0xF5, 0x9E, 0x0B),
                Color.FromRgb(0x10, 0xB9, 0x81)
            };

            int index = Math.Abs((seed ?? "Lyrical").GetHashCode()) % neonPalette.Length;
            return neonPalette[index];
        }

        s = Math.Clamp(s * 1.15, 0.70, 1.0);
        v = Math.Clamp(v * 1.10, 0.85, 1.0);
        return HsvToRgb(h, s, v);
    }

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            h = 0;
            return;
        }

        if (r >= max)
            h = (g - b) / delta;
        else if (g >= max)
            h = 2.0 + (b - r) / delta;
        else
            h = 4.0 + (r - g) / delta;

        h *= 60.0;
        if (h < 0) h += 360.0;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0 % 2.0) - 1.0));
        double m = v - c;

        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Clamp((r + m) * 255.0, 0, 255),
            (byte)Math.Clamp((g + m) * 255.0, 0, 255),
            (byte)Math.Clamp((b + m) * 255.0, 0, 255));
    }
}
