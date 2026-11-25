using System;
using System.Windows;
using LyricalWidget.Interop;

namespace LyricalWidget;

// Snap to screen edges and centre while dragging
public partial class MainWindow
{
    private void HandleWindowMoving(IntPtr hwnd, IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return;
        var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.RECT>(lParam);

        IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO();
        mi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        var work = mi.rcWork;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        int workLeft = work.Left;
        int workTop = work.Top;
        int workRight = work.Right;
        int workBottom = work.Bottom;

        int workCenterX = workLeft + (workRight - workLeft) / 2;
        int workCenterY = workTop + (workBottom - workTop) / 2;

        int winCenterX = rect.Left + width / 2;
        int winCenterY = rect.Top + height / 2;

        const int snapThreshold = 14;

        bool snapL = false, snapR = false, snapT = false, snapB = false, snapCX = false, snapCY = false;

        if (Math.Abs(rect.Left - workLeft) <= snapThreshold)
        {
            rect.Left = workLeft;
            rect.Right = workLeft + width;
            snapL = true;
        }
        else if (Math.Abs(rect.Right - workRight) <= snapThreshold)
        {
            rect.Right = workRight;
            rect.Left = workRight - width;
            snapR = true;
        }
        else if (Math.Abs(winCenterX - workCenterX) <= snapThreshold)
        {
            rect.Left = workCenterX - width / 2;
            rect.Right = rect.Left + width;
            snapCX = true;
        }

        if (Math.Abs(rect.Top - workTop) <= snapThreshold)
        {
            rect.Top = workTop;
            rect.Bottom = workTop + height;
            snapT = true;
        }
        else if (Math.Abs(rect.Bottom - workBottom) <= snapThreshold)
        {
            rect.Bottom = workBottom;
            rect.Top = workBottom - height;
            snapB = true;
        }
        else if (Math.Abs(winCenterY - workCenterY) <= snapThreshold)
        {
            rect.Top = workCenterY - height / 2;
            rect.Bottom = rect.Top + height;
            snapCY = true;
        }

        System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, true);

        SnapGuideLeft.Visibility = snapL ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideRight.Visibility = snapR ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideTop.Visibility = snapT ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideBottom.Visibility = snapB ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideCenterX.Visibility = snapCX ? Visibility.Visible : Visibility.Collapsed;
        SnapGuideCenterY.Visibility = snapCY ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAllSnapGuides()
    {
        SnapGuideLeft.Visibility = Visibility.Collapsed;
        SnapGuideRight.Visibility = Visibility.Collapsed;
        SnapGuideTop.Visibility = Visibility.Collapsed;
        SnapGuideBottom.Visibility = Visibility.Collapsed;
        SnapGuideCenterX.Visibility = Visibility.Collapsed;
        SnapGuideCenterY.Visibility = Visibility.Collapsed;
    }
}
