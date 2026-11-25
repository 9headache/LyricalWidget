using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricalWidget.Interop;

namespace LyricalWidget;

public partial class MainWindow
{
    private static readonly Geometry PlayGeometry = Geometry.Parse("M8 5v14l11-7z");
    private static readonly Geometry PauseGeometry = Geometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z");

    private System.Windows.Threading.DispatcherTimer? _mediaShortcutTimer;
    private bool _isHoldingMediaShortcut = false;

    private async void OnMediaPrevClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TrySkipPreviousAsync();
    }

    private async void OnMediaPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TryTogglePlayPauseAsync();
    }

    private async void OnMediaNextClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await _mediaService.TrySkipNextAsync();
    }

    private void UpdateMediaControlsPlayPauseIcon(bool isPlaying)
    {
        var geo = isPlaying ? PauseGeometry : PlayGeometry;
        if (SidePlayPauseIcon != null) SidePlayPauseIcon.Data = geo;
        if (AlbumPlayPauseIcon != null) AlbumPlayPauseIcon.Data = geo;
    }

    private void WireMediaControlsHover()
    {
        SideAlbumBorder.MouseEnter += (s, e) =>
        {
            if (!_isLocked) ShowMediaControls(true);
        };
        SideAlbumBorder.MouseLeave += (s, e) =>
        {
            if (!_isHoldingMediaShortcut) ShowMediaControls(false);
        };
        AlbumOnlyBorder.MouseEnter += (s, e) =>
        {
            if (!_isLocked) ShowMediaControls(true);
        };
        AlbumOnlyBorder.MouseLeave += (s, e) =>
        {
            if (!_isHoldingMediaShortcut) ShowMediaControls(false);
        };
    }

    private void InitMediaShortcutTimer()
    {
        _mediaShortcutTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _mediaShortcutTimer.Tick += (s, e) =>
        {
            if (_isClosing) return;
            bool altHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LMENU) & 0x8000) != 0;
            bool shiftHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LSHIFT) & 0x8000) != 0;
            bool holding = altHeld && shiftHeld;

            if (holding != _isHoldingMediaShortcut)
            {
                _isHoldingMediaShortcut = holding;
                if (holding)
                {
                    if (_hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetClickThrough(_hwnd, false);
                    }
                    ShowMediaControls(true);
                }
                else
                {
                    ShowMediaControls(false);
                    if (_isLocked && _hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetClickThrough(_hwnd, true);
                    }
                }
            }
        };
        _mediaShortcutTimer.Start();
    }

    private void ShowMediaControls(bool show)
    {
        var targetOpacity = show ? 1.0 : 0.0;
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn }
        };

        void ApplyTo(Border overlay)
        {
            if (overlay == null) return;
            if (show)
            {
                overlay.Visibility = Visibility.Visible;
            }
            else
            {
                anim.Completed += (s, e) =>
                {
                    if (!_isHoldingMediaShortcut && targetOpacity == 0.0)
                    {
                        overlay.Visibility = Visibility.Collapsed;
                    }
                };
            }
            overlay.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        ApplyTo(SideMediaControlsOverlay);
        ApplyTo(AlbumOnlyMediaControlsOverlay);
    }
}
