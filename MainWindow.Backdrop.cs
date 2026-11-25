using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace LyricalWidget;

// Blurred backdrop, baked once per cover
public partial class MainWindow
{
    private bool _activeBackdropIsA = true;

    private static async Task<BitmapSource?> BakeAmbientBackdropAsync(BitmapSource? source)
    {
        if (source == null) return null;

        return await Task.Run(() =>
        {
            try
            {
                const int w = 48;
                const int h = 48;

                double scaleX = (double)w / source.PixelWidth;
                double scaleY = (double)h / source.PixelHeight;

                var transformed = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
                var converted = new FormatConvertedBitmap(transformed, PixelFormats.Bgra32, null, 0);

                int stride = w * 4;
                byte[] srcPixels = new byte[h * stride];
                converted.CopyPixels(srcPixels, stride, 0);

                byte[] buffer1 = (byte[])srcPixels.Clone();
                byte[] buffer2 = new byte[srcPixels.Length];

                const int radius = 6;
                BoxBlur(buffer1, buffer2, w, h, radius);
                BoxBlur(buffer2, buffer1, w, h, radius);
                BoxBlur(buffer1, buffer2, w, h, radius);

                for (int i = 0; i < buffer2.Length; i += 4)
                {
                    double b = buffer2[i];
                    double g = buffer2[i + 1];
                    double r = buffer2[i + 2];

                    double lum = (0.299 * r) + (0.587 * g) + (0.114 * b);

                    buffer2[i] = (byte)Math.Clamp(lum + ((b - lum) * 1.25), 0, 255);
                    buffer2[i + 1] = (byte)Math.Clamp(lum + ((g - lum) * 1.25), 0, 255);
                    buffer2[i + 2] = (byte)Math.Clamp(lum + ((r - lum) * 1.25), 0, 255);
                }

                var result = BitmapSource.Create(
                    w, h, 96, 96, PixelFormats.Bgra32, null, buffer2, stride);
                result.Freeze();
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to bake ambient backdrop: {ex.Message}");
                return null;
            }
        });
    }

    private static void BoxBlur(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int rSum = 0, gSum = 0, bSum = 0, count = 0;
                for (int k = -r; k <= r; k++)
                {
                    int px = Math.Clamp(x + k, 0, w - 1);
                    int idx = rowStart + (px * 4);
                    bSum += src[idx];
                    gSum += src[idx + 1];
                    rSum += src[idx + 2];
                    count++;
                }

                int outIdx = rowStart + (x * 4);
                dst[outIdx] = (byte)(bSum / count);
                dst[outIdx + 1] = (byte)(gSum / count);
                dst[outIdx + 2] = (byte)(rSum / count);
                dst[outIdx + 3] = 255;
            }
        }
    }

    private void CrossfadeBackdrop(BitmapSource newBackdrop)
    {
        var fadeIn = new DoubleAnimation(0.0, 0.32, TimeSpan.FromMilliseconds(250));
        var fadeOut = new DoubleAnimation(0.32, 0.0, TimeSpan.FromMilliseconds(250));

        if (_activeBackdropIsA)
        {
            BackdropImageB.Source = newBackdrop;
            BackdropImageB.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            BackdropImageA.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _activeBackdropIsA = false;
        }
        else
        {
            BackdropImageA.Source = newBackdrop;
            BackdropImageA.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            BackdropImageB.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _activeBackdropIsA = true;
        }
    }
}
