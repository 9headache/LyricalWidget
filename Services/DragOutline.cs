using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace LyricalWidget.Services;

/// <summary>
/// A chrome outline of where the widget will land, shown while it is being dragged. It is a plain GDI window,
/// so it keeps up with the mouse even when WPF's render thread is slow to move the widget itself.
/// Click-through and never activated, so it can sit over a game without taking input.
/// </summary>
public sealed class DragOutline : WinForms.Form
{
    private static readonly Color Key = Color.FromArgb(1, 2, 3); // Painted as see-through
    private static readonly Color Rim = Color.FromArgb(0xD4, 0xD4, 0xD8);
    private static readonly Color Inner = Color.FromArgb(0x52, 0x52, 0x5B);
    private int _radius = 22;

    public DragOutline()
    {
        FormBorderStyle = WinForms.FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = WinForms.FormStartPosition.Manual;
        TopMost = true;
        BackColor = Key;
        TransparencyKey = Key;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override WinForms.CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>Shows the outline at a screen rectangle, in physical pixels.</summary>
    public void ShowAt(Rectangle bounds, int cornerRadius)
    {
        _radius = Math.Max(4, cornerRadius);
        if (Bounds != bounds)
        {
            Bounds = bounds;
            Invalidate();
        }
        if (!Visible) Show();
    }

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        // No anti-aliasing: blended edge pixels would pick up the transparency key colour
        e.Graphics.SmoothingMode = SmoothingMode.None;
        DrawRounded(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), _radius, Rim, 2f);
        DrawRounded(e.Graphics, new Rectangle(4, 4, Width - 9, Height - 9), Math.Max(2, _radius - 3), Inner, 1f);
    }

    private static void DrawRounded(Graphics g, Rectangle r, int radius, Color color, float width)
    {
        if (r.Width <= 2 * radius || r.Height <= 2 * radius) return;
        int d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}
