using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using LyricalWidget.Audio;
using LyricalWidget.Common;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;
using LyricalWidget.Rendering;
using LyricalWidget.Services;

namespace LyricalWidget;

public partial class FullscreenVibeWindow : Window
{
    private SystemVolumeService? _volumeService;
    private DispatcherTimer? _hoverLeaveTimer;
    private bool _isHoverActive = false;
    private bool _isDraggingVolume = false;
    private bool _isDraggingPortraitVolume = false;
    private DispatcherTimer? _modeBarTimer;

    private void InitModeBarTimer()
    {
        _modeBarTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _modeBarTimer.Tick += (s, e) =>
        {
            _modeBarTimer.Stop();
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            TopModeBar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            Cursor = Cursors.None;
        };
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        Cursor = Cursors.Arrow;
        TopModeBar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)));
        _modeBarTimer?.Stop();
        _modeBarTimer?.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (KineticBetaModalOverlay.Visibility == Visibility.Visible)
            {
                HideKineticBetaModal();
                e.Handled = true;
                return;
            }
            Close();
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

    // Kinetic typography mode toggle and preset controls

    private void InitHoverControls()
    {
        _hoverLeaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _hoverLeaveTimer.Tick += (s, e) =>
        {
            _hoverLeaveTimer.Stop();
            if (!_isDraggingVolume && !_isDraggingPortraitVolume)
            {
                HideHoverControls();
            }
        };

        AlbumHoverZone.MouseEnter += (s, e) => ShowHoverControls();
        AlbumHoverZone.MouseLeave += (s, e) => StartHoverLeaveTimer();
        AlbumHoverZone.PreviewMouseWheel += OnAlbumZoneMouseWheel;

        PortraitAlbumHoverZone.MouseEnter += (s, e) => ShowHoverControls();
        PortraitAlbumHoverZone.MouseLeave += (s, e) => StartHoverLeaveTimer();
        PortraitAlbumHoverZone.PreviewMouseWheel += OnAlbumZoneMouseWheel;

        // Sync initial volume UI
        if (_volumeService != null)
        {
            UpdateVolumeUi(_volumeService.Volume, _volumeService.IsMuted);
        }
    }

    private void StartHoverLeaveTimer()
    {
        if (_isDraggingVolume || _isDraggingPortraitVolume) return;
        _hoverLeaveTimer?.Stop();
        _hoverLeaveTimer?.Start();
    }

    private void OnSystemVolumeChanged(float volume, bool isMuted)
    {
        Dispatcher.InvokeAsync(() => UpdateVolumeUi(volume, isMuted));
    }

    private void UpdateVolumeUi(float volume, bool isMuted)
    {
        int pct = (int)Math.Round(volume * 100f);
        string pctStr = isMuted ? "MUTED" : $"{pct}%";

        VolumePercentText.Text = pctStr;
        PortraitVolumePercentText.Text = pctStr;

        double trackHeight = VolumeSliderTrack.ActualHeight > 10 ? VolumeSliderTrack.ActualHeight : 180;
        double fillH = isMuted ? 0 : Math.Clamp(volume * trackHeight, 0, trackHeight);
        VolumeFillBar.Height = fillH;

        double portraitTrackHeight = PortraitVolumeSliderTrack.ActualHeight > 10 ? PortraitVolumeSliderTrack.ActualHeight : 90;
        double portraitFillH = isMuted ? 0 : Math.Clamp(volume * portraitTrackHeight, 0, portraitTrackHeight);
        PortraitVolumeFillBar.Height = portraitFillH;

        Geometry iconData = (isMuted || volume <= 0.001f)
            ? MuteIconGeometry
            : (volume <= 0.50f ? SpeakerLowGeometry : SpeakerLoudGeometry);

        VolumeSpeakerIcon.Data = iconData;
        PortraitVolumeSpeakerIcon.Data = iconData;
    }

    private void OnVolumeTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingVolume = true;
            VolumeSliderTrack.CaptureMouse();
            SetVolumeFromTrackPosition(e.GetPosition(VolumeSliderTrack), VolumeSliderTrack.ActualHeight);
            e.Handled = true;
        }
    }

    private void OnVolumeTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingVolume && e.LeftButton == MouseButtonState.Pressed)
        {
            SetVolumeFromTrackPosition(e.GetPosition(VolumeSliderTrack), VolumeSliderTrack.ActualHeight);
            e.Handled = true;
        }
    }

    private void OnVolumeTrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingVolume)
        {
            _isDraggingVolume = false;
            VolumeSliderTrack.ReleaseMouseCapture();
            if (!AlbumHoverZone.IsMouseOver)
            {
                StartHoverLeaveTimer();
            }
            e.Handled = true;
        }
    }

    private void OnPortraitVolumeTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingPortraitVolume = true;
            PortraitVolumeSliderTrack.CaptureMouse();
            SetVolumeFromTrackPosition(e.GetPosition(PortraitVolumeSliderTrack), PortraitVolumeSliderTrack.ActualHeight);
            e.Handled = true;
        }
    }

    private void OnPortraitVolumeTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingPortraitVolume && e.LeftButton == MouseButtonState.Pressed)
        {
            SetVolumeFromTrackPosition(e.GetPosition(PortraitVolumeSliderTrack), PortraitVolumeSliderTrack.ActualHeight);
            e.Handled = true;
        }
    }

    private void OnPortraitVolumeTrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingPortraitVolume)
        {
            _isDraggingPortraitVolume = false;
            PortraitVolumeSliderTrack.ReleaseMouseCapture();
            if (!PortraitAlbumHoverZone.IsMouseOver)
            {
                StartHoverLeaveTimer();
            }
            e.Handled = true;
        }
    }

    private void SetVolumeFromTrackPosition(Point pt, double actualHeight)
    {
        if (actualHeight <= 0) return;
        double ratio = 1.0 - Math.Clamp(pt.Y / actualHeight, 0.0, 1.0);
        if (_volumeService != null)
        {
            _volumeService.Volume = (float)ratio;
            if (_volumeService.IsMuted && ratio > 0.01)
            {
                _volumeService.IsMuted = false;
            }
            UpdateVolumeUi((float)ratio, _volumeService.IsMuted);
        }
    }

    private void OnVolumeMuteToggleClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_volumeService != null)
        {
            _volumeService.ToggleMute();
            UpdateVolumeUi(_volumeService.Volume, _volumeService.IsMuted);
        }
    }

    private void OnAlbumZoneMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float delta = e.Delta > 0 ? 0.03f : -0.03f; // 3% step
        _volumeService?.StepVolume(delta);
        e.Handled = true;
    }

    private void ShowHoverControls()
    {
        _hoverLeaveTimer?.Stop();
        _isHoverActive = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur180 = TimeSpan.FromMilliseconds(180);
        var dur220 = TimeSpan.FromMilliseconds(220);

        AlbumCoverScrim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, dur180) { EasingFunction = ease });
        PortraitAlbumCoverScrim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, dur180) { EasingFunction = ease });

        MediaControlsOverlay.Visibility = Visibility.Visible;
        MediaControlsOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, dur180) { EasingFunction = ease });
        MediaControlsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, dur180) { EasingFunction = ease });
        MediaControlsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, dur180) { EasingFunction = ease });

        PortraitMediaControlsOverlay.Visibility = Visibility.Visible;
        PortraitMediaControlsOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, dur180) { EasingFunction = ease });
        PortraitMediaControlsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, dur180) { EasingFunction = ease });
        PortraitMediaControlsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, dur180) { EasingFunction = ease });

        // Volume pill: Glides smoothly out to the right (TranslateX -16 -> 0, Opacity 0 -> 1, Scale 0.92 -> 1.0, 220ms CubicEase, IsHitTestVisible = true)
        VolumePopoutPill.IsHitTestVisible = true;
        VolumePopoutPill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, dur220) { EasingFunction = ease });
        VolumePillTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, dur220) { EasingFunction = ease });
        VolumePillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, dur220) { EasingFunction = ease });
        VolumePillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, dur220) { EasingFunction = ease });

        PortraitVolumePopoutPill.IsHitTestVisible = true;
        PortraitVolumePopoutPill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, dur220) { EasingFunction = ease });
        PortraitVolumePillTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, dur220) { EasingFunction = ease });
        PortraitVolumePillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, dur220) { EasingFunction = ease });
        PortraitVolumePillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, dur220) { EasingFunction = ease });
    }

    private void HideHoverControls()
    {
        if (_isDraggingVolume || _isDraggingPortraitVolume) return;
        _isHoverActive = false;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var dur180 = TimeSpan.FromMilliseconds(180);
        var dur220 = TimeSpan.FromMilliseconds(220);

        AlbumCoverScrim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, dur180) { EasingFunction = ease });
        PortraitAlbumCoverScrim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, dur180) { EasingFunction = ease });

        var fadeMedia = new DoubleAnimation(0.0, dur180) { EasingFunction = ease };
        fadeMedia.Completed += (s, e) =>
        {
            if (!_isHoverActive)
            {
                MediaControlsOverlay.Visibility = Visibility.Collapsed;
                PortraitMediaControlsOverlay.Visibility = Visibility.Collapsed;
            }
        };
        MediaControlsOverlay.BeginAnimation(UIElement.OpacityProperty, fadeMedia);
        MediaControlsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, dur180) { EasingFunction = ease });
        MediaControlsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, dur180) { EasingFunction = ease });

        PortraitMediaControlsOverlay.BeginAnimation(UIElement.OpacityProperty, fadeMedia);
        PortraitMediaControlsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, dur180) { EasingFunction = ease });
        PortraitMediaControlsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, dur180) { EasingFunction = ease });

        // Volume pill: Glides cleanly back (TranslateX -> -16, Opacity -> 0, Scale -> 0.92, 220ms, IsHitTestVisible = false)
        VolumePopoutPill.IsHitTestVisible = false;
        VolumePopoutPill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, dur220) { EasingFunction = ease });
        VolumePillTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-16.0, dur220) { EasingFunction = ease });
        VolumePillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, dur220) { EasingFunction = ease });
        VolumePillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, dur220) { EasingFunction = ease });

        PortraitVolumePopoutPill.IsHitTestVisible = false;
        PortraitVolumePopoutPill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, dur220) { EasingFunction = ease });
        PortraitVolumePillTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-12.0, dur220) { EasingFunction = ease });
        PortraitVolumePillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, dur220) { EasingFunction = ease });
        PortraitVolumePillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, dur220) { EasingFunction = ease });
    }
}
