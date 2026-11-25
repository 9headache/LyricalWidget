using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LyricalWidget;

public partial class MainWindow
{
    private bool _isInIdleClockMode = false;

    private void TransitionToIdleClock()
    {
        if (IsFullscreenActive) return;
        if (_isInIdleClockMode) return;
        _isInIdleClockMode = true;

        Background = Brushes.Transparent;

        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, null);
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, null);

        var fadeOutMedia = new DoubleAnimation(MediaContentRoot.Opacity, 0.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOutMedia.Completed += (s, e) =>
        {
            if (_isInIdleClockMode)
            {
                MediaContentRoot.Visibility = Visibility.Collapsed;
            }
        };
        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, fadeOutMedia);

        IdleClockRoot.Visibility = Visibility.Visible;
        var fadeInClock = new DoubleAnimation(IdleClockRoot.Opacity, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, fadeInClock);

        ApplyIdleClockStyling();
        UpdateClockTime(0.016, forceUpdate: true);
    }

    private void ApplyIdleClockStyling()
    {
        if (!_isInIdleClockMode) return;

        MoonlightBackdrop.Visibility = Visibility.Collapsed;
        ApplyCardGlowStyle(moonlight: false);

        if (_settings.BackgroundStyle == "Glass")
        {
            CardBorder.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x0A, 0x0C, 0x12));
            GlassBackgroundContainer.Visibility = Visibility.Visible;
            CardBorder.BorderThickness = new Thickness(1);
            CardBorder.BorderBrush = BuildSpecularBrush(_currentAccentColor);
            CardDropShadow.Opacity = _settings.GamingMode ? 0.0 : 0.5;
        }
        else
        {
            CardBorder.Background = Brushes.Transparent;
            GlassBackgroundContainer.Visibility = Visibility.Collapsed;
            CardBorder.BorderThickness = new Thickness(0);
            CardBorder.BorderBrush = Brushes.Transparent;
            CardDropShadow.Opacity = 0.0;
        }
    }

    private void TransitionFromIdleClock()
    {
        Background = Brushes.Transparent;
        if (IsFullscreenActive) return;
        if (!_isInIdleClockMode)
        {
            if (IdleClockRoot.Visibility != Visibility.Collapsed)
            {
                IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, null);
                IdleClockRoot.Visibility = Visibility.Collapsed;
                IdleClockRoot.Opacity = 0.0;
            }
            if (MediaContentRoot.Visibility != Visibility.Visible)
            {
                MediaContentRoot.Visibility = Visibility.Visible;
                MediaContentRoot.Opacity = 1.0;
            }
            return;
        }
        _isInIdleClockMode = false;

        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, null);
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, null);

        var fadeOutClock = new DoubleAnimation(IdleClockRoot.Opacity, 0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOutClock.Completed += (s, e) =>
        {
            if (!_isInIdleClockMode)
            {
                IdleClockRoot.Visibility = Visibility.Collapsed;
            }
        };
        IdleClockRoot.BeginAnimation(UIElement.OpacityProperty, fadeOutClock);

        MediaContentRoot.Visibility = Visibility.Visible;
        var fadeInMedia = new DoubleAnimation(MediaContentRoot.Opacity, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MediaContentRoot.BeginAnimation(UIElement.OpacityProperty, fadeInMedia);

        ApplyDisplayModeAndTheme();
    }

    private class ClockDigitSlot
    {
        public TextBlock CurrentBlock { get; set; } = null!;
        public TextBlock NextBlock { get; set; } = null!;
        public char DisplayedChar { get; set; } = ' ';
        public char TargetChar { get; set; } = ' ';
        public double CurrentY = 0.0;
        public double VelocityY = 0.0;
        public bool IsRolling { get; set; } = false;
    }

    private ClockDigitSlot[]? _clockSlots;

    private void EnsureClockSlotsInitialized()
    {
        if (_clockSlots != null) return;
        _clockSlots = new ClockDigitSlot[]
        {
            new() { CurrentBlock = ClockH1Current, NextBlock = ClockH1Next },
            new() { CurrentBlock = ClockH2Current, NextBlock = ClockH2Next },
            new() { CurrentBlock = ClockM1Current, NextBlock = ClockM1Next },
            new() { CurrentBlock = ClockM2Current, NextBlock = ClockM2Next }
        };
    }

    private void UpdateClockTime(double dt, bool forceUpdate = false)
    {
        EnsureClockSlotsInitialized();
        if (_clockSlots == null) return;

        var now = DateTime.Now;
        string timeStr = now.ToString("HHmm");
        IdleClockDateText.Text = now.ToString("dddd, MMMM d");

        for (int i = 0; i < 4; i++)
        {
            var slot = _clockSlots[i];
            char c = timeStr[i];

            if (forceUpdate || slot.DisplayedChar == ' ')
            {
                slot.DisplayedChar = c;
                slot.TargetChar = c;
                slot.CurrentBlock.Text = c.ToString();
                Canvas.SetTop(slot.CurrentBlock, 0);
                slot.CurrentBlock.Opacity = 1.0;
                Canvas.SetTop(slot.NextBlock, 58.0);
                slot.NextBlock.Opacity = 0.0;
                slot.IsRolling = false;
                slot.CurrentY = 0.0;
                slot.VelocityY = 0.0;
            }
            else if (slot.TargetChar != c && !slot.IsRolling)
            {
                slot.TargetChar = c;
                slot.NextBlock.Text = c.ToString();
                slot.CurrentY = 0.0;
                slot.VelocityY = 0.0;
                slot.IsRolling = true;
            }

            if (slot.IsRolling)
            {
                bool settled = SpringLineFluid.Step(ref slot.CurrentY, ref slot.VelocityY, -58.0, dt, 0.5);

                double progress = Math.Clamp(-slot.CurrentY / 58.0, 0.0, 1.0);
                Canvas.SetTop(slot.CurrentBlock, slot.CurrentY);
                slot.CurrentBlock.Opacity = 1.0 - progress;

                Canvas.SetTop(slot.NextBlock, 58.0 + slot.CurrentY);
                slot.NextBlock.Opacity = progress;

                if (settled || progress >= 0.99)
                {
                    slot.DisplayedChar = slot.TargetChar;
                    slot.CurrentBlock.Text = slot.TargetChar.ToString();
                    Canvas.SetTop(slot.CurrentBlock, 0);
                    slot.CurrentBlock.Opacity = 1.0;
                    Canvas.SetTop(slot.NextBlock, 58.0);
                    slot.NextBlock.Opacity = 0.0;
                    slot.IsRolling = false;
                    slot.CurrentY = 0.0;
                    slot.VelocityY = 0.0;
                }
            }
        }
    }
}
