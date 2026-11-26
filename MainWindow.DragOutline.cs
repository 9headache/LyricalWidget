using System;
using System.Windows;
using System.Windows.Media;
using LyricalWidget.Interop;
using LyricalWidget.Services;

namespace LyricalWidget;

// While the widget is dragged, a chrome outline shows where it will land, right under the mouse
public partial class MainWindow
{
    private DragOutline? _dragOutline;

    /// <param name="window">Where Windows is about to put the widget, after snapping, in screen pixels.</param>
    private void ShowDragOutline(NativeMethods.RECT window)
    {
        if (_isLocked || IsFullscreenActive) return;

        // The outline traces the card, which sits inside the window's margin
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int inset = (int)Math.Round(LayoutRoot.Margin.Left * scale);
        var card = new System.Drawing.Rectangle(
            window.Left + inset, window.Top + inset,
            Math.Max(1, window.Right - window.Left - (2 * inset)), Math.Max(1, window.Bottom - window.Top - (2 * inset)));

        _dragOutline ??= new DragOutline();
        _dragOutline.ShowAt(card, (int)Math.Round(CardBorder.CornerRadius.TopLeft * scale));
    }

    private void HideDragOutline()
    {
        if (_dragOutline is { Visible: true }) _dragOutline.Hide();
    }
}
