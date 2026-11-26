using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LyricalWidget.Interop;

namespace LyricalWidget;

// Quick-access popover. It lives in its own popup so a small widget can never clip it.
public partial class MainWindow
{
    // One fixed width, so resizing the widget never reflows the menu or changes its height
    private const double ToolbarWidth = 640.0;
    private const double ToolbarGap = 8.0;   // Above the card
    private const double ToolbarGapBelow = 14.0; // Below the card, clear of the resize grip

    private bool _toolbarAbove = true;

    private void InitQuickAccess()
    {
        ToolbarPopup.CustomPopupPlacementCallback = PlaceToolbar;

        IsVisibleChanged += (s, e) => UpdateToolbarPopup();
        StateChanged += (s, e) => UpdateToolbarPopup();
        LocationChanged += (s, e) => RepositionToolbar();
        SizeChanged += (s, e) => RepositionToolbar();

        // A popup does not share the window's opacity, so it stays closed until the window has faded in after the splash
        Loaded += (s, e) => UpdateToolbarPopup();
        var opacity = DependencyPropertyDescriptor.FromProperty(OpacityProperty, typeof(UIElement));
        EventHandler onOpacityChanged = (s, e) => UpdateToolbarPopup();
        opacity.AddValueChanged(this, onOpacityChanged);
        Closed += (s, e) => opacity.RemoveValueChanged(this, onOpacityChanged);
    }

    private void UpdateToolbarPopup()
    {
        UpdateFullscreenPill();
        UpdateFisheyeWarning();
        bool open = !_isLocked && !_isClosing && !IsFullscreenActive && IsLoaded && IsVisible && Opacity > 0.5 && WindowState != WindowState.Minimized;
        if (open == ToolbarPopup.IsOpen) return;

        if (open)
        {
            UnlockToolbar.Width = Math.Min(ToolbarWidth, SystemParameters.WorkArea.Width - 24.0);
            ToolbarScroll.MaxHeight = SystemParameters.WorkArea.Height * 0.65;
            ChooseToolbarSide();
        }
        else
        {
            EndPresetNaming();
        }
        ToolbarPopup.IsOpen = open;
    }

    /// <summary>
    /// The side is settled only here: when the menu opens and when a move or resize has ended.
    /// Content changes and the drag itself never flip it.
    /// </summary>
    private void ChooseToolbarSide()
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(_hwnd, out var window)) return;

        var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info)) return;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        UnlockToolbar.Measure(new Size(UnlockToolbar.Width, double.PositiveInfinity));
        double needed = (UnlockToolbar.DesiredSize.Height + ToolbarGapBelow) * scale;
        double cardTop = window.Top + (LayoutRoot.Margin.Top * scale);
        double above = cardTop - info.rcWork.Top;
        double below = info.rcWork.Bottom - window.Bottom;

        _toolbarAbove = above >= needed || above >= below;
    }

    private CustomPopupPlacement[] PlaceToolbar(Size popupSize, Size targetSize, Point offset)
    {
        // The sizes come in device pixels; the gaps are in DIPs
        double scale = CardBorder.ActualWidth > 0 ? targetSize.Width / CardBorder.ActualWidth : 1.0;
        double y = _toolbarAbove ? -popupSize.Height - (ToolbarGap * scale) : targetSize.Height + (ToolbarGapBelow * scale);
        return new[] { new CustomPopupPlacement(new Point(0, y), PopupPrimaryAxis.Horizontal) };
    }

    private void RepositionToolbar()
    {
        RepositionFullscreenPill();
        if (!ToolbarPopup.IsOpen) return;
        // An open popup only follows its target when one of its offsets changes
        ToolbarPopup.HorizontalOffset = ToolbarPopup.HorizontalOffset == 0.0 ? 0.001 : 0.0;
    }

    private void OnMoveOrResizeEnded()
    {
        if (!ToolbarPopup.IsOpen) return;
        bool wasAbove = _toolbarAbove;
        ChooseToolbarSide();
        if (wasAbove != _toolbarAbove)
        {
            RepositionToolbar();
            RepositionFullscreenPill();
        }
    }
}
