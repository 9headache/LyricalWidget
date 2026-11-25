using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Services;
using Drawing = System.Drawing;

namespace LyricalWidget;

// Window chrome, F11 fullscreen, saved placement, monitor targeting and responsive sizing
public partial class FullscreenVibeWindow : Window
{
    private const double CaptionHeight = 44.0;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isFullscreen = false;
    private bool _placementReady = false;
    private WindowState _windowedState = WindowState.Normal;
    private Drawing.Rectangle? _normalRect; // Last windowed, non-maximized place (includes Aero Snap)

    private void InitWindowing()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WindowingHook);
        _resizeSettleTimer.Tick += (s, e) => OnResizeSettled();
        LocationChanged += (s, e) => TrackNormalRect();
        StateChanged += (s, e) =>
        {
            UpdateMaximizedInset();
            UpdateWindowControls();
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        var saved = _settings.VibeWindowBounds;
        _normalRect = MonitorService.ChoosePlacement(
            MonitorService.GetMonitors(),
            _settings.PreferredVibeMonitor,
            saved is { Width: >= 200, Height: >= 200 }
                ? new Drawing.Rectangle((int)saved.Left, (int)saved.Top, (int)saved.Width, (int)saved.Height)
                : null);
        PlaceWindow(_normalRect.Value);

        _windowedState = _settings.VibeWindowState == "Maximized" ? WindowState.Maximized : WindowState.Normal;
        if (_settings.VibeIsFullscreen)
        {
            EnterFullscreenCore();
        }
        else
        {
            WindowState = _windowedState;
        }
        _placementReady = true;
        UpdateWindowControls();
    }

    private void ShutdownWindowing()
    {
        _resizeSettleTimer.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SaveWindowPlacement();
    }

    private IntPtr WindowingHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The drag region is non-client area, so WPF gets no MouseMove there
        if (msg == WM_NCMOUSEMOVE) ShowTopChrome();
        return IntPtr.Zero;
    }

    private void PlaceWindow(Drawing.Rectangle r)
    {
        const uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, flags);
        // Crossing to a display with another DPI can rescale the window on the first move
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, flags);
    }

    private void TrackNormalRect()
    {
        if (!_placementReady || _isFullscreen || _hwnd == IntPtr.Zero) return;
        if (NativeMethods.IsZoomed(_hwnd) || NativeMethods.IsIconic(_hwnd)) return;
        if (NativeMethods.GetWindowRect(_hwnd, out var r) && r.Right > r.Left && r.Bottom > r.Top)
        {
            _normalRect = Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    private void SaveWindowPlacement()
    {
        if (!_placementReady) return;

        if (_normalRect is { } r)
        {
            _settings.VibeWindowBounds = new VibeWindowBounds { Left = r.X, Top = r.Y, Width = r.Width, Height = r.Height };
        }
        if (!_isFullscreen && WindowState != WindowState.Minimized) _windowedState = WindowState;
        _settings.VibeWindowState = _windowedState == WindowState.Maximized ? "Maximized" : "Normal";
        _settings.VibeIsFullscreen = _isFullscreen;

        // Dragging to another display moves the preference along, so the window reopens where it was left.
        // A preference for a display that is merely unplugged is kept.
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count > 1 && _settings.PreferredVibeMonitor is { } preferred &&
            MonitorService.Resolve(monitors, preferred)?.DeviceName == preferred &&
            CurrentMonitor() is { } current)
        {
            _settings.PreferredVibeMonitor = current.DeviceName;
        }
        SettingsService.Save(_settings);
    }

    private MonitorTarget? CurrentMonitor()
    {
        var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return null;
        return MonitorService.FromPoint(MonitorService.GetMonitors(), info.rcMonitor.Left + 1, info.rcMonitor.Top + 1);
    }

    public void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (_isFullscreen) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        _windowedState = WindowState;
        var target = CurrentMonitor();
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        EnterFullscreenCore(target);
        SaveWindowPlacement();
    }

    private void EnterFullscreenCore(MonitorTarget? target = null)
    {
        _isFullscreen = true;
        target ??= CurrentMonitor() ?? MonitorService.Resolve(MonitorService.GetMonitors(), _settings.PreferredVibeMonitor);

        ApplyChrome(0.0, 0.0);
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        RootGrid.Margin = new Thickness(0);
        WindowHairline.Visibility = Visibility.Collapsed;
        if (target != null) PlaceWindow(target.Bounds);
        UpdateWindowControls();
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;

        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ApplyChrome(CaptionHeight, 6.0);
        if (_normalRect is { } r) PlaceWindow(r);
        _isFullscreen = false;
        WindowState = _windowedState;
        UpdateMaximizedInset();
        UpdateWindowControls();
        SaveWindowPlacement();
    }

    private void ApplyChrome(double caption, double resizeBorder)
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = caption,
            ResizeBorderThickness = new Thickness(resizeBorder),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
    }

    /// <summary>A maximized borderless window hangs over the screen edges; pull the content back in by exactly that much.</summary>
    private void UpdateMaximizedInset()
    {
        if (_isFullscreen) return;

        WindowHairline.Visibility = WindowState == WindowState.Normal ? Visibility.Visible : Visibility.Collapsed;
        if (WindowState != WindowState.Maximized)
        {
            RootGrid.Margin = new Thickness(0);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (WindowState != WindowState.Maximized || _isFullscreen) return;

            var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !NativeMethods.GetWindowRect(_hwnd, out var w)) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            RootGrid.Margin = new Thickness(
                Math.Max(0, info.rcWork.Left - w.Left) / dpi.DpiScaleX,
                Math.Max(0, info.rcWork.Top - w.Top) / dpi.DpiScaleY,
                Math.Max(0, w.Right - info.rcWork.Right) / dpi.DpiScaleX,
                Math.Max(0, w.Bottom - info.rcWork.Bottom) / dpi.DpiScaleY);
        }));
    }

    public void MoveToMonitor(string deviceName)
    {
        var target = MonitorService.Resolve(MonitorService.GetMonitors(), deviceName);
        if (target == null || _hwnd == IntPtr.Zero) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        var placement = MonitorService.ChoosePlacement(MonitorService.GetMonitors(), target.DeviceName, _normalRect);
        if (_isFullscreen)
        {
            _normalRect = placement; // Where F11 / Esc will return to
            PlaceWindow(target.Bounds);
        }
        else
        {
            var state = WindowState;
            if (state == WindowState.Maximized) WindowState = WindowState.Normal;
            PlaceWindow(placement);
            WindowState = state;
            UpdateMaximizedInset();
        }
        SaveWindowPlacement();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_isClosing) return;

            var monitors = MonitorService.GetMonitors();
            if (_normalRect is { } r && MonitorService.FromPoint(monitors, r.X + (r.Width / 2), r.Y + (r.Height / 2)) == null)
            {
                _normalRect = MonitorService.ChoosePlacement(monitors, null, r);
                if (!_isFullscreen && WindowState == WindowState.Normal) PlaceWindow(_normalRect.Value);
            }
            if (_isFullscreen && CurrentMonitor() is { } current) PlaceWindow(current.Bounds);
            UpdateMaximizedInset();
        }));
    }

    private static readonly Geometry EnterFullscreenGlyph = Geometry.Parse("M0,3.5 L0,0 L3.5,0 M6.5,0 L10,0 L10,3.5 M10,6.5 L10,10 L6.5,10 M3.5,10 L0,10 L0,6.5");
    private static readonly Geometry ExitFullscreenGlyph = Geometry.Parse("M3.5,0 L3.5,3.5 L0,3.5 M10,3.5 L6.5,3.5 L6.5,0 M6.5,10 L6.5,6.5 L10,6.5 M0,6.5 L3.5,6.5 L3.5,10");
    private static readonly Geometry MaximizeGlyph = Geometry.Parse("M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z");
    private static readonly Geometry RestoreGlyph = Geometry.Parse("M2.5,2.5 L2.5,0.5 L9.5,0.5 L9.5,7.5 L7.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z");

    private void UpdateWindowControls()
    {
        GlyphFullscreen.Data = _isFullscreen ? ExitFullscreenGlyph : EnterFullscreenGlyph;
        BtnWinFullscreen.ToolTip = _isFullscreen ? "Exit Fullscreen (F11 / Esc)" : "Fullscreen (F11)";
        GlyphMaximize.Data = WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
        BtnWinMaximize.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        BtnWinMaximize.Visibility = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnWinFullscreenClicked(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnWinMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnWinMaximizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
