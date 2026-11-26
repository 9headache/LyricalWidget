using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LyricalWidget.Rendering;

/// <summary>What to draw: one word in Moonlight Bloom's glow, or the soft blur behind a climax word.</summary>
public sealed record GlyphBake(string Text, FontFamily Font, double FontSize, Color TextColor, Color InnerGlow, Color OuterGlow, double SurgeBlur);

/// <summary>
/// A glowing word drawn once into a picture, on a background thread. Software rendering (Game Mode) would
/// otherwise blur every glow again on every frame, which is what made the widget stall and jump while dragged.
/// </summary>
public sealed class BakedImage : Image
{
    private static readonly Lazy<Dispatcher> BakeThread = new(StartBakeThread);

    private readonly double _padding;
    private readonly double _scale;
    private GlyphBake _request;
    private int _generation;

    /// <param name="padding">Room around the word for glows and blurs that spread past its edges.</param>
    /// <param name="scale">Pixels per DIP: the screen's density, times any magnification the picture will get.</param>
    public BakedImage(GlyphBake request, double padding, double scale)
    {
        _request = request;
        _padding = padding;
        _scale = Math.Max(1.0, scale);
        Stretch = Stretch.Fill;
        IsHitTestVisible = false;
        // The picture is larger than the word by the padding, so it is pulled back out to line up with it
        Margin = new Thickness(-padding);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Bake();
    }

    /// <summary>Draws the picture again in new colours; the old one stays up until the new one is ready.</summary>
    public void Recolor(Color text, Color innerGlow, Color outerGlow)
    {
        var next = _request with { TextColor = text, InnerGlow = innerGlow, OuterGlow = outerGlow };
        if (next == _request) return;
        _request = next;
        Bake();
    }

    private bool _bakeQueued;

    private void Bake()
    {
        // Queued until the current UI work is done, so a recolour straight after creation costs one bake, not two
        if (_bakeQueued) return;
        _bakeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StartBake));
    }

    private void StartBake()
    {
        _bakeQueued = false;
        int generation = ++_generation;
        var request = _request;
        double padding = _padding, scale = _scale;
        var owner = Dispatcher;

        BakeThread.Value.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var (bitmap, size) = Render(request, padding, scale);
            owner.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (generation != _generation || bitmap == null) return;
                Source = bitmap;
                Width = size.Width;
                Height = size.Height;
            }));
        }));
    }

    private static (BitmapSource? Bitmap, Size Size) Render(GlyphBake r, double padding, double scale)
    {
        var text = new TextBlock
        {
            Text = r.Text,
            FontFamily = r.Font,
            FontWeight = FontWeights.Black,
            FontSize = r.FontSize,
            Foreground = new SolidColorBrush(r.TextColor)
        };

        FrameworkElement content;
        if (r.SurgeBlur > 0)
        {
            text.Effect = new BlurEffect { Radius = r.SurgeBlur, RenderingBias = RenderingBias.Quality };
            content = text;
        }
        else
        {
            // Same two layers as the live word: a tight inner glow and a wide soft bloom
            text.Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 0, Color = r.InnerGlow, Opacity = 0.55, RenderingBias = RenderingBias.Quality };
            content = new Border
            {
                Child = text,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Color = r.OuterGlow, Opacity = 0.60, RenderingBias = RenderingBias.Quality }
            };
        }

        var host = new Border { Padding = new Thickness(padding), Child = content };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = host.DesiredSize;
        host.Arrange(new Rect(size));
        if (size.Width < 1 || size.Height < 1) return (null, size);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
        bitmap.Render(host);
        bitmap.Freeze(); // Frozen, so the UI thread can use it
        return (bitmap, size);
    }

    private static Dispatcher StartBakeThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Moonlight glyph baker",
            Priority = ThreadPriority.BelowNormal // Never ahead of a game for CPU time
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
