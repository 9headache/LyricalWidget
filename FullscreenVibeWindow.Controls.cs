using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LyricalWidget;

public partial class FullscreenVibeWindow : Window
{
    private DispatcherTimer? _modeBarTimer;

    private void InitModeBarTimer()
    {
        _modeBarTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _modeBarTimer.Tick += (s, e) =>
        {
            // Keep the bar up while one of its menus is open
            if (_openMenuPill != null) return;

            _modeBarTimer.Stop();
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            TopChrome.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            Cursor = Cursors.None;
        };
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e) => ShowTopChrome();

    private void ShowTopChrome()
    {
        Cursor = Cursors.Arrow;
        TopChrome.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)));
        _modeBarTimer?.Stop();
        _modeBarTimer?.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_openMenuPill != null)
            {
                CloseHoverMenu();
            }
            else
            {
                // Leaves fullscreen only; closing is ×, Alt+F4 or the tray
                ExitFullscreen();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            _ = _mediaService.TryTogglePlayPauseAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.K)
        {
            ToggleLayoutMode();
            e.Handled = true;
        }
    }

    private void OnProgressBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement bar && _mediaService.CurrentTrack.Duration > TimeSpan.Zero)
        {
            Point pt = e.GetPosition(bar);
            double ratio = Math.Clamp(pt.X / Math.Max(1, bar.ActualWidth), 0.0, 1.0);
            TimeSpan target = TimeSpan.FromSeconds(_mediaService.CurrentTrack.Duration.TotalSeconds * ratio);
            _ = _mediaService.TrySeekAsync(target);
        }
    }

    private void OnPrevClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = _mediaService.TrySkipPreviousAsync();
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = _mediaService.TryTogglePlayPauseAsync();
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = _mediaService.TrySkipNextAsync();
    }

    private void OnToggleAudioReactiveClicked(object sender, RoutedEventArgs e)
    {
        _isAudioReactiveEnabled = !_isAudioReactiveEnabled;
        UpdateAudioReactiveButtonLabel();
        _audioBeatService.UpdateState(_mediaService.IsPlaying, isGamingMode: false, _isAudioReactiveEnabled);
    }

    private void UpdateAudioReactiveButtonLabel()
    {
        BtnToggleAudioReactive.Content = _isAudioReactiveEnabled ? "Audio Reactive: ON" : "Audio Reactive: OFF";
    }

    private void OnToggleThemeLyricsClicked(object sender, RoutedEventArgs e)
    {
        SetThemeColoredLyrics(!_settings.ThemeColoredLyrics);
    }
}
