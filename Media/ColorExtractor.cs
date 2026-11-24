using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LyricalWidget.Media;

public sealed class VibePalette
{
    public Color Primary { get; set; } = Color.FromRgb(0x60, 0xA5, 0xFA);
    public Color Secondary { get; set; } = Color.FromRgb(0xA8, 0x55, 0xF7);
    public Color Accent { get; set; } = Color.FromRgb(0xEC, 0x48, 0x99);
    public Color Highlight { get; set; } = Color.FromRgb(0x38, 0xBD, 0xF8);
    public Color DarkAnchor { get; set; } = Color.FromRgb(0x0B, 0x0E, 0x17);
    public Color[] Colors { get; set; } = Array.Empty<Color>();
    public bool IsMonochrome { get; set; } = false;

    public static VibePalette Default()
    {
        var p = new VibePalette();
        p.Colors = new[] { p.Primary, p.Secondary, p.Accent, p.Highlight, p.DarkAnchor };
        return p;
    }
}

public sealed class ThemedLyricColors
{
    public Color ActiveColor { get; init; } = Colors.White;
    public Color InactiveColor { get; init; } = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF);
    public Color AccentGlowColor { get; init; } = Color.FromRgb(0x60, 0xA5, 0xFA);

    public static ThemedLyricColors DefaultWhite() => new()
    {
        ActiveColor = Colors.White,
        InactiveColor = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF),
        AccentGlowColor = Color.FromRgb(0x60, 0xA5, 0xFA)
    };
}

public static class ColorExtractor
{
    private static readonly Color DefaultAccent = Color.FromRgb(0x60, 0xA5, 0xFA); // Sky Blue 400

    public static Color ExtractDominantColor(BitmapSource? bitmap)
    {
        var palette = ExtractPalette(bitmap);
        return palette.Primary;
    }

    /// <summary>
    /// Extracts palette colors from downscaled album art or generates a monochrome palette for grayscale covers.
    /// </summary>
    public static VibePalette ExtractPalette(BitmapSource? bitmap)
    {
        if (bitmap == null)
        {
            return VibePalette.Default();
        }

        try
        {
            const int targetSize = 48;
            double scaleX = (double)targetSize / Math.Max(1, bitmap.PixelWidth);
            double scaleY = (double)targetSize / Math.Max(1, bitmap.PixelHeight);

            var transformed = new TransformedBitmap(bitmap, new ScaleTransform(scaleX, scaleY));
            var converted = new FormatConvertedBitmap(transformed, PixelFormats.Bgr32, null, 0);

            int stride = targetSize * 4;
            byte[] pixels = new byte[targetSize * stride];
            converted.CopyPixels(pixels, stride, 0);

            var buckets = new Dictionary<int, ColorBucket>();
            Color darkAnchorCandidate = Color.FromRgb(0x0C, 0x0F, 0x18);
            double darkestBrightness = 1.0;

            var brightnessValues = new List<double>(pixels.Length / 4);
            double totalR = 0, totalG = 0, totalB = 0;
            long totalPixels = 0;
            long chromaticPixelCount = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                // STRICT RULE: NEVER check or reject pixels based on the alpha byte (bytes[i + 3] is 0 on Bgr32 JPEGs)

                double rf = r / 255.0;
                double gf = g / 255.0;
                double bf = b / 255.0;

                double luminance = 0.2126 * rf + 0.7152 * gf + 0.0722 * bf;
                brightnessValues.Add(luminance);
                totalR += r;
                totalG += g;
                totalB += b;
                totalPixels++;

                double max = Math.Max(rf, Math.Max(gf, bf));
                double min = Math.Min(rf, Math.Min(gf, bf));
                double delta = max - min;
                double lightness = (max + min) / 2.0;

                // HSV calculation for precise chromatic gating
                double vHsv = max;
                double sHsv = max > 0.0001 ? (delta / max) : 0.0;

                // Strict Noise & Shadow Rejection:
                // Ignore dark pixels (HSV Value < 0.22) and near-white/washed pixels (HSV Saturation < 0.22)
                bool isColorful = (sHsv >= 0.22 && vHsv >= 0.22 && !(sHsv < 0.25 && vHsv > 0.85));

                if (isColorful)
                {
                    chromaticPixelCount++;
                }

                // Track dark anchor (dark, rich background base)
                if (lightness >= 0.04 && lightness <= 0.22 && lightness < darkestBrightness)
                {
                    darkestBrightness = lightness;
                    darkAnchorCandidate = Color.FromRgb(r, g, b);
                }

                // For colorful clustering, only include genuinely colorful pixels
                if (!isColorful)
                {
                    continue;
                }

                // Quantize to 5-bit color space (32 bins per channel)
                int qr = (r >> 3) & 0x1F;
                int qg = (g >> 3) & 0x1F;
                int qb = (b >> 3) & 0x1F;
                int key = (qr << 10) | (qg << 5) | qb;

                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new ColorBucket();
                    buckets[key] = bucket;
                }

                bucket.Count++;
                bucket.SumR += r;
                bucket.SumG += g;
                bucket.SumB += b;
                bucket.SaturationSum += sHsv;
                bucket.BrightnessSum += lightness;
            }

            double chromaticRatio = totalPixels > 0 ? (double)chromaticPixelCount / totalPixels : 0.0;

            // Monochrome Coverage Gate: If fewer than 8% of pixels are genuinely colorful, treat as monochrome
            if (totalPixels == 0 || chromaticRatio < 0.08 || buckets.Count == 0)
            {
                return GenerateMonochromeGlassPalette(brightnessValues, totalR, totalG, totalB, totalPixels);
            }

            var scoredList = buckets.Values
                .Select(b =>
                {
                    int c = b.Count;
                    byte avgR = (byte)(b.SumR / c);
                    byte avgG = (byte)(b.SumG / c);
                    byte avgB = (byte)(b.SumB / c);
                    double avgSat = b.SaturationSum / c;
                    double avgBri = b.BrightnessSum / c;

                    // Weighted primarily by pixel count / area
                    double lightWeight = (avgBri >= 0.15 && avgBri <= 0.88) ? 1.0 : 0.6;
                    double score = c * lightWeight;
                    return new ScoredColor(Color.FromRgb(avgR, avgG, avgB), score, avgSat, avgBri);
                })
                .OrderByDescending(sc => sc.Score)
                .ToList();

            var selected = new List<Color>();

            // Pick the dominant color of the cover
            if (scoredList.Count > 0)
            {
                selected.Add(scoredList[0].Color);
            }

            // Pick distinct colors with high color distance
            for (int i = 1; i < scoredList.Count && selected.Count < 4; i++)
            {
                var candidate = scoredList[i];
                bool isDistinct = true;
                foreach (var sel in selected)
                {
                    if (ColorDistance(candidate.Color, sel) < 35.0)
                    {
                        isDistinct = false;
                        break;
                    }
                }

                if (isDistinct)
                {
                    selected.Add(candidate.Color);
                }
            }

            // Fill harmonious variations if fewer than 4 distinct colors
            while (selected.Count < 4 && selected.Count > 0)
            {
                Color baseC = selected[0];
                var (h, s, l) = RgbToHsl(baseC);
                double newH = (h + 30.0 * selected.Count) % 360.0;
                double newS = Math.Min(0.85, s * 1.15);
                double newL = Math.Clamp(l + (selected.Count % 2 == 0 ? 0.08 : -0.08), 0.40, 0.74);
                selected.Add(HslToRgb(newH, newS, newL));
            }

            // Preserve real hue and saturation: cap saturation boosting at Math.Min(0.85, originalS * 1.15)
            // and only lift lightness into 0.62–0.74 for readability against dark backgrounds
            var (p0H, p0S, p0L) = RgbToHsl(selected[0]);
            selected[0] = HslToRgb(p0H, Math.Min(0.85, p0S * 1.15), Math.Clamp(Math.Max(p0L, 0.62), 0.62, 0.74));

            // Dark anchor with slight tint of primary
            Color darkAnchor = darkAnchorCandidate;
            if (darkestBrightness >= 0.9)
            {
                Color prim = selected[0];
                darkAnchor = Color.FromRgb((byte)(prim.R / 6), (byte)(prim.G / 6), (byte)(prim.B / 6));
            }

            var palette = new VibePalette
            {
                Primary = selected[0],
                Secondary = selected[1],
                Accent = selected[2],
                Highlight = selected[3],
                DarkAnchor = darkAnchor,
                Colors = new[] { selected[0], selected[1], selected[2], selected[3], darkAnchor },
                IsMonochrome = false
            };

            return palette;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ColorExtractor] Palette exception: {ex.Message}");
            return VibePalette.Default();
        }
    }

    // Generates a 5-color monochrome palette derived from pixel brightness distribution.
    private static VibePalette GenerateMonochromeGlassPalette(List<double> brightnessValues, double totalR, double totalG, double totalB, long totalPixels)
    {
        if (brightnessValues.Count == 0 || totalPixels == 0)
        {
            var p0 = Color.FromRgb(0x7A, 0x82, 0x90);
            var p1 = Color.FromRgb(0x24, 0x28, 0x30);
            var p2 = Color.FromRgb(0x43, 0x4A, 0x56);
            var p3 = Color.FromRgb(0xB8, 0xC0, 0xCC);
            var p4 = Color.FromRgb(0x10, 0x12, 0x16);
            return new VibePalette
            {
                Primary = p0,
                Secondary = p1,
                Accent = p2,
                Highlight = p3,
                DarkAnchor = p4,
                Colors = new[] { p0, p1, p2, p3, p4 },
                IsMonochrome = true
            };
        }

        brightnessValues.Sort();
        double GetPercentile(double pct)
        {
            int idx = (int)Math.Clamp(pct * (brightnessValues.Count - 1), 0, brightnessValues.Count - 1);
            return brightnessValues[idx];
        }

        double pAnchor = GetPercentile(0.08);
        double pCharcoal = GetPercentile(0.28);
        double pSlate = GetPercentile(0.50);
        double pSilver = GetPercentile(0.75);
        double pMist = GetPercentile(0.92);

        // Blend channel ratios with slate tone to prevent color inversion
        double avgR = totalR / totalPixels;
        double avgG = totalG / totalPixels;
        double avgB = totalB / totalPixels;
        double maxCh = Math.Max(avgR, Math.Max(avgG, avgB));

        double rRatio = 1.0;
        double gRatio = 1.08;
        double bRatio = 1.22;
        if (maxCh > 12.0)
        {
            rRatio = 0.5 * rRatio + 0.5 * (avgR / maxCh * 1.1);
            gRatio = 0.5 * gRatio + 0.5 * (avgG / maxCh * 1.1);
            bRatio = 0.5 * bRatio + 0.5 * (avgB / maxCh * 1.2);
        }

        Color CreateTone(double lum, Color refTone)
        {
            double refLum = (refTone.R * 0.2126 + refTone.G * 0.7152 + refTone.B * 0.0722) / 255.0;
            double targetLum = 0.45 * refLum + 0.55 * lum;
            byte r = (byte)Math.Clamp(Math.Round(targetLum * 255.0 * (rRatio / 1.15)), 12, 248);
            byte g = (byte)Math.Clamp(Math.Round(targetLum * 255.0 * (gRatio / 1.15)), 14, 250);
            byte b = (byte)Math.Clamp(Math.Round(targetLum * 255.0 * (bRatio / 1.15)), 18, 255);
            return Color.FromRgb(r, g, b);
        }

        Color darkObsidian = CreateTone(Math.Min(0.12, pAnchor), Color.FromRgb(0x10, 0x12, 0x16));
        Color charcoal = CreateTone(Math.Clamp(pCharcoal, 0.16, 0.30), Color.FromRgb(0x24, 0x28, 0x30));
        Color slate = CreateTone(Math.Clamp(pSlate, 0.30, 0.50), Color.FromRgb(0x43, 0x4A, 0x56));
        Color silverGrey = CreateTone(Math.Clamp(pSilver, 0.52, 0.75), Color.FromRgb(0x7A, 0x82, 0x90));
        Color softMist = CreateTone(Math.Clamp(pMist, 0.76, 0.96), Color.FromRgb(0xB8, 0xC0, 0xCC));

        return new VibePalette
        {
            Primary = silverGrey,
            Secondary = charcoal,
            Accent = slate,
            Highlight = softMist,
            DarkAnchor = darkObsidian,
            Colors = new[] { silverGrey, charcoal, slate, softMist, darkObsidian },
            IsMonochrome = true
        };
    }

    public static ThemedLyricColors ComputeThemedLyricColors(VibePalette palette, bool enabled)
    {
        if (!enabled)
        {
            return ThemedLyricColors.DefaultWhite();
        }

        // Monochrome Cover Fallback: on black-and-white / grayscale covers
        if (palette.IsMonochrome)
        {
            return new ThemedLyricColors
            {
                ActiveColor = Colors.White, // pure crisp White #FFFFFF
                InactiveColor = Color.FromArgb((byte)Math.Round(0.35 * 255.0), 255, 255, 255), // soft translucent grey Opacity ~0.35
                AccentGlowColor = Color.FromRgb(0x60, 0xA5, 0xFA)
            };
        }

        Color dominant = palette.Primary;
        var (hue, sat, light) = RgbToHsl(dominant);

        // Cap saturation boosting at Math.Min(0.85, originalS * 1.15)
        double activeSat = Math.Min(0.85, sat * 1.15);
        // Ensure bright lightness (0.70 <= L <= 0.82) so the lit left side pops clearly against the dim unlit side
        double activeLightness = Math.Clamp(Math.Max(light, 0.70), 0.70, 0.82);
        Color activeColor = HslToRgb(hue, activeSat, activeLightness, 255);

        // Same hue at ~0.35 alpha for inactive words
        byte unsungAlpha = (byte)Math.Round(0.35 * 255.0);
        Color inactiveColor = Color.FromArgb(unsungAlpha, activeColor.R, activeColor.G, activeColor.B);

        return new ThemedLyricColors
        {
            ActiveColor = activeColor,
            InactiveColor = inactiveColor,
            AccentGlowColor = HslToRgb(hue, activeSat, 0.65, 255)
        };
    }

    public static (double H, double S, double L) RgbToHsl(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0.0;
        double s = 0.0;
        double l = (max + min) / 2.0;

        if (delta > 0.00001)
        {
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

            if (Math.Abs(max - r) < 0.0001)
            {
                h = (g - b) / delta + (g < b ? 6.0 : 0.0);
            }
            else if (Math.Abs(max - g) < 0.0001)
            {
                h = (b - r) / delta + 2.0;
            }
            else
            {
                h = (r - g) / delta + 4.0;
            }
            h /= 6.0;
        }

        return (h * 360.0, s, l);
    }

    public static Color HslToRgb(double h, double s, double l, byte alpha = 255)
    {
        h = (h % 360.0 + 360.0) % 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);

        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }

        byte red = (byte)Math.Clamp(Math.Round((r + m) * 255.0), 0, 255);
        byte green = (byte)Math.Clamp(Math.Round((g + m) * 255.0), 0, 255);
        byte blue = (byte)Math.Clamp(Math.Round((b + m) * 255.0), 0, 255);

        return Color.FromArgb(alpha, red, green, blue);
    }

    private static double ColorDistance(Color c1, Color c2)
    {
        double dr = (c1.R - c2.R) * 0.30;
        double dg = (c1.G - c2.G) * 0.59;
        double db = (c1.B - c2.B) * 0.11;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private class ColorBucket
    {
        public int Count;
        public long SumR;
        public long SumG;
        public long SumB;
        public double SaturationSum;
        public double BrightnessSum;
    }

    private readonly record struct ScoredColor(Color Color, double Score, double Saturation, double Brightness);
}
