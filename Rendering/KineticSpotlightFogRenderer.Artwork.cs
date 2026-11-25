using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace LyricalWidget.Rendering;

// Backdrop blur and color helpers
public sealed partial class KineticSpotlightFogRenderer
{
    private static Color LerpColor(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte a = (byte)(from.A + ((to.A - from.A) * t));
        byte r = (byte)(from.R + ((to.R - from.R) * t));
        byte g = (byte)(from.G + ((to.G - from.G) * t));
        byte b = (byte)(from.B + ((to.B - from.B) * t));
        return Color.FromArgb(a, r, g, b);
    }

    private static BitmapSource? CreatePreblurredBitmap(BitmapSource? sharpArt, int targetSize = 256)
    {
        if (sharpArt == null) return null;

        try
        {
            double scaleX = (double)targetSize / Math.Max(1, sharpArt.PixelWidth);
            double scaleY = (double)targetSize / Math.Max(1, sharpArt.PixelHeight);

            var transformed = new TransformedBitmap(sharpArt, new ScaleTransform(scaleX, scaleY));
            var converted = new FormatConvertedBitmap(transformed, PixelFormats.Pbgra32, null, 0);

            var img = new Image
            {
                Source = converted,
                Width = targetSize,
                Height = targetSize,
                Effect = new BlurEffect
                {
                    Radius = 22,
                    RenderingBias = RenderingBias.Quality,
                    KernelType = KernelType.Gaussian
                }
            };

            img.Measure(new Size(targetSize, targetSize));
            img.Arrange(new Rect(0, 0, targetSize, targetSize));

            var rtb = new RenderTargetBitmap(targetSize, targetSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(img);

            int stride = targetSize * 4;
            byte[] pixels = new byte[targetSize * stride];
            rtb.CopyPixels(pixels, stride, 0);

            var frozen = BitmapSource.Create(
                targetSize,
                targetSize,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                pixels,
                stride);
            frozen.Freeze();
            return frozen;
        }
        catch
        {
            return null;
        }
    }

    private static Color BoostLuminanceFloor(Color color, double minLuminance)
    {
        double rf = color.R / 255.0;
        double gf = color.G / 255.0;
        double bf = color.B / 255.0;
        double lum = (0.2126 * rf) + (0.7152 * gf) + (0.0722 * bf);

        if (lum >= minLuminance)
        {
            return color;
        }

        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        double h = 0.0;
        if (delta > 0.0001)
        {
            if (Math.Abs(max - rf) < 0.0001)
                h = 60.0 * (((gf - bf) / delta) % 6.0);
            else if (Math.Abs(max - gf) < 0.0001)
                h = 60.0 * (((bf - rf) / delta) + 2.0);
            else
                h = 60.0 * (((rf - gf) / delta) + 4.0);

            if (h < 0.0) h += 360.0;
        }

        double s = max > 0.0001 ? delta / max : 0.0;
        s = Math.Clamp(s, 0.45, 0.85);
        double v = 0.96;

        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0 % 2.0) - 1.0));
        double m = v - c;

        double r1 = 0, g1 = 0, b1 = 0;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        byte rOut = (byte)Math.Clamp((r1 + m) * 255.0, 0, 255);
        byte gOut = (byte)Math.Clamp((g1 + m) * 255.0, 0, 255);
        byte bOut = (byte)Math.Clamp((b1 + m) * 255.0, 0, 255);

        double newLum = (0.2126 * (rOut / 255.0)) + (0.7152 * (gOut / 255.0)) + (0.0722 * (bOut / 255.0));
        if (newLum < minLuminance)
        {
            double blend = (minLuminance - newLum) / Math.Max(0.01, 1.0 - newLum);
            rOut = (byte)(rOut + ((255 - rOut) * blend));
            gOut = (byte)(gOut + ((255 - gOut) * blend));
            bOut = (byte)(bOut + ((255 - bOut) * blend));
        }

        return Color.FromRgb(rOut, gOut, bOut);
    }
}
