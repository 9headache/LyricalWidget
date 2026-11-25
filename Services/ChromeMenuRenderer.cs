using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LyricalWidget.Services;

/// <summary>
/// Obsidian and brushed chrome look for the tray context menu and its submenus.
/// </summary>
public sealed class ChromeMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color MenuBackground = Color.FromArgb(0x0E, 0x10, 0x16);
    private static readonly Color MenuBorder = Color.FromArgb(0x27, 0x27, 0x2A);
    private static readonly Color HoverFill = Color.FromArgb(0x18, 0x1A, 0x22);
    private static readonly Color HoverEdge = Color.FromArgb(23, 255, 255, 255);
    private static readonly Color HoverSheen = Color.FromArgb(70, 255, 255, 255);
    private static readonly Color PrimaryText = Color.FromArgb(0xE4, 0xE4, 0xE7);
    private static readonly Color MutedTone = Color.FromArgb(0x71, 0x71, 0x7A);
    private static readonly Color SeparatorTone = Color.FromArgb(90, 0x71, 0x71, 0x7A);
    private static readonly Color Chrome = Color.FromArgb(0xF4, 0xF4, 0xF5);

    private ChromeMenuRenderer() : base(new ChromeColorTable())
    {
        RoundedEdges = false;
    }

    /// <summary>
    /// Styles a finished menu. Call after all items and submenus have been added.
    /// </summary>
    public static void Apply(ToolStripDropDownMenu menu)
    {
        ApplyRecursive(menu, new ChromeMenuRenderer());
    }

    private static void ApplyRecursive(ToolStripDropDownMenu menu, ChromeMenuRenderer renderer)
    {
        menu.Renderer = renderer;
        menu.BackColor = MenuBackground;
        menu.ForeColor = PrimaryText;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem menuItem) continue;

            menuItem.Padding = new Padding(0, 3, 0, 3);
            if (menuItem.HasDropDownItems && menuItem.DropDown is ToolStripDropDownMenu subMenu)
            {
                ApplyRecursive(subMenu, renderer);
            }
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // No separate margin strip: the menu background shows through
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(MenuBorder);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        bool isOpen = e.Item is ToolStripMenuItem menuItem && menuItem.DropDown.Visible;
        if (!e.Item.Enabled || (!e.Item.Selected && !isOpen)) return;

        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        if (bounds.Width <= 2 || bounds.Height <= 2) return;

        using var fill = new SolidBrush(HoverFill);
        e.Graphics.FillRectangle(fill, bounds);

        using var edge = new Pen(HoverEdge);
        e.Graphics.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        // Chrome sheen along the top edge, fading out at both ends
        using var sheen = new LinearGradientBrush(bounds, Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.Transparent, HoverSheen, Color.Transparent },
                Positions = new[] { 0f, 0.5f, 1f }
            }
        };
        e.Graphics.FillRectangle(sheen, bounds.X, bounds.Y, bounds.Width, 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Shortcut hints go through this same method with their own text
        bool isShortcutHint = e.Item is ToolStripMenuItem menuItem &&
                              !string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString) &&
                              e.Text == menuItem.ShortcutKeyDisplayString;

        e.TextColor = !e.Item.Enabled || isShortcutHint ? MutedTone
            : e.Item.Selected ? Color.White
            : PrimaryText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Selected: true } ? Chrome : MutedTone;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        float size = Math.Min(r.Width, r.Height);
        float x = r.X + (r.Width - size) / 2f;
        float y = r.Y + (r.Height - size) / 2f;

        var previousMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(e.Item.Enabled ? Chrome : MutedTone, Math.Max(1.5f, size / 10f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(x + size * 0.24f, y + size * 0.52f),
            new PointF(x + size * 0.43f, y + size * 0.70f),
            new PointF(x + size * 0.77f, y + size * 0.32f)
        });
        e.Graphics.SmoothingMode = previousMode;
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(SeparatorTone);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 11, y);
    }

    private sealed class ChromeColorTable : ProfessionalColorTable
    {
        public ChromeColorTable()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => MenuBackground;
        public override Color ImageMarginGradientBegin => MenuBackground;
        public override Color ImageMarginGradientMiddle => MenuBackground;
        public override Color ImageMarginGradientEnd => MenuBackground;
        public override Color MenuBorder => ChromeMenuRenderer.MenuBorder;
        public override Color MenuItemBorder => HoverEdge;
        public override Color MenuItemSelected => HoverFill;
        public override Color MenuItemSelectedGradientBegin => HoverFill;
        public override Color MenuItemSelectedGradientEnd => HoverFill;
        public override Color MenuItemPressedGradientBegin => HoverFill;
        public override Color MenuItemPressedGradientMiddle => HoverFill;
        public override Color MenuItemPressedGradientEnd => HoverFill;
        public override Color CheckBackground => MenuBackground;
        public override Color CheckSelectedBackground => HoverFill;
        public override Color CheckPressedBackground => HoverFill;
        public override Color SeparatorDark => SeparatorTone;
        public override Color SeparatorLight => MenuBackground;
    }
}
