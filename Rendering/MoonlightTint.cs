using System;
using System.Windows.Media;

namespace LyricalWidget.Rendering;

/// <summary>Album colours for Moonlight Bloom, lifted so that even a dark cover glows and stays readable.</summary>
public sealed record MoonlightTint(Color Text, Color InnerGlow, Color OuterGlow)
{
    public Brush TextBrush { get; } = Frozen(Text);
    public Brush GlowBrush { get; } = Frozen(OuterGlow);

    /// <summary>The more colourful of the cover's two main colours. Null for a black-and-white cover, which keeps the plain silver look.</summary>
    public static MoonlightTint? FromPalette(Color primary, Color accent, bool monochrome)
    {
        if (monochrome) return null;

        var p = ToHsl(primary);
        var a = ToHsl(accent);
        var (h, s, _) = a.S > p.S + 0.15 ? a : p;
        if (s < 0.12) return null; // Grey: nothing worth tinting with

        // Saturation held in a range that reads as colour without turning neon; lightness kept high for the glow
        double sat = Math.Clamp(s, 0.45, 0.85);
        return new MoonlightTint(
            FromHsl(h, sat * 0.55, 0.90),  // Letters: a pale wash of the colour, so they stay crisp
            FromHsl(h, sat, 0.80),
            FromHsl(h, sat, 0.68));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        if (max - min < 1e-6) return (0.0, 0.0, l);

        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h = max == r ? ((g - b) / d) + (g < b ? 6.0 : 0.0)
                 : max == g ? ((b - r) / d) + 2.0
                 : ((r - g) / d) + 4.0;
        return (h / 6.0, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
        double p = (2.0 * l) - q;
        return Color.FromRgb(Channel(p, q, h + (1.0 / 3.0)), Channel(p, q, h), Channel(p, q, h - (1.0 / 3.0)));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0.0) t += 1.0;
        if (t > 1.0) t -= 1.0;
        double v = t < 1.0 / 6.0 ? p + ((q - p) * 6.0 * t)
                 : t < 0.5 ? q
                 : t < 2.0 / 3.0 ? p + ((q - p) * ((2.0 / 3.0) - t) * 6.0)
                 : p;
        return (byte)Math.Round(Math.Clamp(v, 0.0, 1.0) * 255.0);
    }
}
