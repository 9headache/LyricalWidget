using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// Album art sizing: one setting drives the cover, its corners and everything lined up with it
public partial class MainWindow : Window
{
    // The render loop fills the progress bars in these units, so the tracks are scaled rather than resized
    private const double SideProgressUnits = 150.0;
    private const double AlbumOnlyProgressUnits = 165.0;

    private const double MinInfoWidth = 96.0;         // Title, times and progress stay readable under a tiny cover
    private const double MediaControlsWidth = 122.0;  // Natural width of the hover controls
    private bool _albumArtReady = false;

    private void InitAlbumArt()
    {
        // Painting the cover as a brush gives a centred square crop with clean corners at any size.
        // The Image elements remain as the place the rest of the app sets the artwork.
        PaintCover(SideAlbumArtSurface, SideAlbumArtImage);
        PaintCover(AlbumOnlyArtSurface, AlbumOnlyImage);

        SliderAlbumArt.Minimum = AppSettings.MinAlbumArtSize;
        SliderAlbumArt.Maximum = AppSettings.MaxAlbumArtSize;
        SliderAlbumArt.Value = _settings.WidgetAlbumArtSize;
        _albumArtReady = true;

        CardBorder.SizeChanged += (s, e) => ApplyAlbumArtSize();
        Loaded += (s, e) => ApplyAlbumArtSize();
        ApplyAlbumArtSize();
    }

    private static void PaintCover(Border surface, Image source)
    {
        var brush = new ImageBrush { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
        BindingOperations.SetBinding(brush, ImageBrush.ImageSourceProperty, new Binding(nameof(Image.Source)) { Source = source });
        RenderOptions.SetBitmapScalingMode(surface, BitmapScalingMode.HighQuality);
        surface.Background = brush;
    }

    private void SetAlbumArtSize(double size)
    {
        size = AppSettings.ClampAlbumArtSize(Math.Round(size));
        if (Math.Abs(size - _settings.WidgetAlbumArtSize) < 0.5) return;

        _settings.WidgetAlbumArtSize = size;
        SliderAlbumArt.Value = size;
        ApplyAlbumArtSize();
        SaveCurrentBounds();
    }

    private void ApplyAlbumArtSize()
    {
        if (!_albumArtReady) return;

        double wanted = _settings.WidgetAlbumArtSize;
        double side = FitToCard(wanted, SideTimeRow, SideTrackTitle, SideTrackArtist, SideProgressTrack);
        double sideInfo = Math.Max(side, MinInfoWidth);
        SizeCover(SideAlbumBorder, SideAlbumArtSurface, SideFallbackIcon, SideMediaControlsOverlay, side, maxRadius: 14.0, iconRatio: 0.24);
        SideInfoColumn.Width = sideInfo;
        SideTrackTitle.MaxWidth = sideInfo;
        SideTrackArtist.MaxWidth = sideInfo;
        SideProgressTrack.Width = sideInfo;
        SideTimeRow.Width = sideInfo;
        SideProgressFillScale.ScaleX = sideInfo / SideProgressUnits;

        // This mode has always shown the cover a tenth larger
        double only = FitToCard(Math.Round(wanted * 1.1), AlbumOnlyTimeRow, AlbumOnlyTrackTitle, AlbumOnlyTrackArtist, AlbumOnlyProgressTrack);
        double onlyInfo = Math.Max(only, MinInfoWidth);
        SizeCover(AlbumOnlyBorder, AlbumOnlyArtSurface, AlbumOnlyFallbackIcon, AlbumOnlyMediaControlsOverlay, only, maxRadius: 16.0, iconRatio: 0.27);
        AlbumOnlyProgressTrack.Width = onlyInfo;
        AlbumOnlyTimeRow.Width = onlyInfo;
        AlbumOnlyProgressFillScale.ScaleX = onlyInfo / AlbumOnlyProgressUnits;

        // Say so when the widget is too small for the chosen size, instead of seeming to ignore the slider
        LblAlbumArt.Text = side < wanted - 0.5 ? $"{wanted:0}px (fits {side:0})" : $"{wanted:0}px";
        foreach (var (button, size) in AlbumArtPresets())
        {
            bool active = Math.Abs(size - wanted) < 0.5;
            button.Background = active ? PillActiveBrush : PillBrush;
            button.BorderBrush = active ? PillActiveBorder : PillBorder;
        }

        // The lyric column just changed width: its lines are re-wrapped once resizing pauses
        ScheduleLayoutRefresh();
    }

    /// <summary>The wanted size, reduced only as far as the card needs so that nothing is cut off.</summary>
    private double FitToCard(double wanted, FrameworkElement timeRow, params FrameworkElement[] text)
    {
        double cardW = CardBorder.ActualWidth;
        double cardH = CardBorder.ActualHeight;
        if (cardW < 10 || cardH < 10) return wanted;

        const double margins = 24.0 + 6.0; // View margin plus a little air for the border and shadow
        double textHeight = 0.0;
        foreach (var element in text) textHeight += NaturalHeight(element);

        // On the very shortest cards the time readout gives way before the cover does
        double room = cardH - margins - textHeight;
        double timeHeight = NaturalHeight(timeRow);
        bool showTime = room - timeHeight >= AppSettings.MinAlbumArtSize;
        timeRow.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
        if (showTime) room -= timeHeight;

        double fit = Math.Min(wanted, Math.Min(room, cardW * 0.42));
        return Math.Round(Math.Max(AppSettings.MinAlbumArtSize * 0.6, fit));
    }

    private static double NaturalHeight(FrameworkElement element)
    {
        double height = !double.IsNaN(element.Height) ? element.Height
            : element is TextBlock text ? text.FontSize * 1.36
            : 13.0;
        return height + element.Margin.Top + element.Margin.Bottom;
    }

    private static void SizeCover(Border frame, Border surface, FrameworkElement icon, Border controls, double size, double maxRadius, double iconRatio)
    {
        var radius = new CornerRadius(Math.Clamp(size * 0.14, 6.0, maxRadius));
        frame.Width = size;
        frame.Height = size;
        frame.CornerRadius = radius;
        surface.Width = size;
        surface.Height = size;
        surface.CornerRadius = radius;
        icon.Width = size * iconRatio;
        icon.Height = size * iconRatio;

        // Hover controls may extend past a small cover, but never past the column under it
        double scale = Math.Min(1.0, Math.Max(size, MinInfoWidth) / MediaControlsWidth);
        controls.LayoutTransform = scale < 1.0 ? new ScaleTransform(scale, scale) : Transform.Identity;
        double overhang = Math.Max(0.0, ((MediaControlsWidth * scale) - size) / 2.0);
        double overhangV = Math.Max(0.0, ((42.0 * scale) - size) / 2.0);
        controls.Margin = new Thickness(-overhang, -overhangV, -overhang, -overhangV);
    }

    private (Button Button, double Size)[] AlbumArtPresets() => new[]
    {
        (BtnArtCompact, 44.0),
        (BtnArtSmall, 54.0),
        (BtnArtMedium, 64.0),
        (BtnArtLarge, 80.0),
        (BtnArtDefault, AppSettings.DefaultAlbumArtSize)
    };

    private void OnAlbumArtPresetClicked(object sender, RoutedEventArgs e)
    {
        foreach (var (button, size) in AlbumArtPresets())
        {
            if (button == sender) SetAlbumArtSize(size);
        }
    }

    private void OnAlbumArtSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_albumArtReady && IsLoaded) SetAlbumArtSize(e.NewValue);
    }
}
