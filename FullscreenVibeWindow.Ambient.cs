using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LyricalWidget;

// Ambient Idle: a glowing clock and drifting dust while playback is paused
public partial class FullscreenVibeWindow : Window
{
    private const int DustCount = 40;
    private readonly DispatcherTimer _ambientDelayTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly List<DustMote> _dust = new();
    private bool _isAmbient = false;
    private bool _ambientCoversStage = false; // Fade-in finished: the stage underneath can stop working
    private int _ambientRun = 0;
    private long _ambientNextFrame = 0;
    private long _ambientLastFrame = 0;
    private int _ambientShownMinute = -1;

    private void InitAmbient()
    {
        _ambientDelayTimer.Tick += (s, e) =>
        {
            _ambientDelayTimer.Stop();
            if (!_mediaService.IsPlaying) EnterAmbient();
        };

        var rng = new Random();
        for (int i = 0; i < DustCount; i++)
        {
            var mote = new DustMote(rng);
            _dust.Add(mote);
            AmbientDustCanvas.Children.Add(mote.Element);
        }
        UpdateAmbientForPlayback(_mediaService.IsPlaying);
    }

    private void ShutdownAmbient() => _ambientDelayTimer.Stop();

    private void UpdateAmbientForPlayback(bool isPlaying)
    {
        if (isPlaying)
        {
            _ambientDelayTimer.Stop();
            ExitAmbient();
        }
        else if (!_isAmbient && !_ambientDelayTimer.IsEnabled)
        {
            // Long enough that skipping tracks never flashes the clock
            _ambientDelayTimer.Start();
        }
    }

    private void EnterAmbient()
    {
        if (_isAmbient || _isClosing) return;
        _isAmbient = true;
        int run = ++_ambientRun;

        _ambientLastFrame = Stopwatch.GetTimestamp();
        _ambientNextFrame = 0;
        RefreshAmbientText(force: true);
        AmbientHost.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (s, e) =>
        {
            if (run == _ambientRun) _ambientCoversStage = true;
        };
        AmbientHost.BeginAnimation(OpacityProperty, fade);
    }

    private void ExitAmbient()
    {
        if (!_isAmbient) return;
        _isAmbient = false;
        _ambientCoversStage = false;
        int run = ++_ambientRun;
        _lastFrameTimestamp = Stopwatch.GetTimestamp();

        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (s, e) =>
        {
            if (run == _ambientRun) AmbientHost.Visibility = Visibility.Collapsed;
        };
        AmbientHost.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Returns true when the ambient stage hides everything else and the frame is done.</summary>
    private bool UpdateAmbientFrame(long nowTicks)
    {
        if (AmbientHost.Visibility != Visibility.Visible) return false;
        // Nothing to draw while minimized
        if (WindowState == WindowState.Minimized) return _ambientCoversStage;

        // Paced to 60 FPS so a high refresh rate display does not multiply the cost of drifting dust
        if (nowTicks >= _ambientNextFrame - (long)(0.0015 * Stopwatch.Frequency))
        {
            _ambientNextFrame = Math.Max(nowTicks, _ambientNextFrame + (Stopwatch.Frequency / 60));
            double dt = Math.Clamp((double)(nowTicks - _ambientLastFrame) / Stopwatch.Frequency, 0.0, 0.1);
            _ambientLastFrame = nowTicks;

            double w = AmbientDustCanvas.ActualWidth;
            double h = AmbientDustCanvas.ActualHeight;
            foreach (var mote in _dust) mote.Update(dt, w, h);
            RefreshAmbientText(force: false);
        }
        return _ambientCoversStage;
    }

    private void RefreshAmbientText(bool force)
    {
        var now = DateTime.Now;
        if (force || now.Minute != _ambientShownMinute)
        {
            _ambientShownMinute = now.Minute;
            string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            AmbientClockText.Text = time;
            AmbientClockGlow.Text = time;
            AmbientDateText.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        }

        var track = _mediaService.CurrentTrack;
        string label = track.IsValid ? $"Paused • {track.Title}" : string.Empty;
        if (AmbientPausedText.Text != label)
        {
            AmbientPausedText.Text = label;
            AmbientPausedPill.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateAmbientLayout(double w, double h)
    {
        // About 3 ems wide, so this fills roughly two thirds of the width and never more than a third of the height
        double size = Math.Clamp(Math.Min(w * 0.215, h * 0.34), 48.0, 420.0);
        AmbientClockText.FontSize = size;
        AmbientClockGlow.FontSize = size;
        AmbientGlowEffect.Radius = Math.Clamp(size * 0.14, 12.0, 44.0);
        AmbientDateText.FontSize = Math.Clamp(size * 0.105, 13.0, 34.0);
        AmbientPausedText.MaxWidth = Math.Max(140.0, w * 0.6);
    }

    private void OnAmbientPausedPillClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _ = _mediaService.TryTogglePlayPauseAsync();
    }

    private sealed class DustMote
    {
        private readonly TranslateTransform _translate = new();
        private readonly double _speedX;
        private readonly double _speedY;
        private readonly double _phase;
        private readonly double _brightness;
        private double _x; // 0..1 across the canvas, so resizing spreads the dust instead of stranding it
        private double _y;
        private double _time;

        public Ellipse Element { get; }

        public DustMote(Random rng)
        {
            double size = 1.4 + (rng.NextDouble() * rng.NextDouble() * 3.4);
            _x = rng.NextDouble();
            _y = rng.NextDouble();
            _speedX = (rng.NextDouble() - 0.5) * 9.0;
            _speedY = -(3.0 + (rng.NextDouble() * 9.0));
            _phase = rng.NextDouble() * Math.PI * 2.0;
            _brightness = 0.10 + (rng.NextDouble() * 0.34);
            Element = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = rng.Next(3) == 0 ? Brushes.White : Silver,
                Opacity = 0.0,
                RenderTransform = _translate,
                IsHitTestVisible = false
            };
        }

        private static readonly Brush Silver = CreateSilver();

        private static Brush CreateSilver()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8));
            brush.Freeze();
            return brush;
        }

        public void Update(double dt, double w, double h)
        {
            if (w < 1 || h < 1) return;
            _time += dt;
            _x += (_speedX + (Math.Sin((_time * 0.35) + _phase) * 7.0)) * dt / w;
            _y += _speedY * dt / h;
            if (_y < -0.02) _y = 1.02;
            if (_x < -0.02) _x = 1.02;
            else if (_x > 1.02) _x = -0.02;

            _translate.X = _x * w;
            _translate.Y = _y * h;
            // Fades near the top and bottom edges so nothing pops in or out
            double edge = Math.Clamp(Math.Min(_y, 1.0 - _y) / 0.12, 0.0, 1.0);
            Element.Opacity = _brightness * edge * (0.65 + (0.35 * Math.Sin((_time * 0.8) + _phase)));
        }
    }
}
