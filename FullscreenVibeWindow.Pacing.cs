using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// Dynamic Word Pacing pill
public partial class FullscreenVibeWindow
{
    private static readonly TimeSpan PacingFade = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PacingHold = TimeSpan.FromSeconds(4.8);
    private DispatcherTimer? _pacingTimer;
    private bool _pacingShownForKinetic = false;
    private int _pacingRun = 0; // Bumped on every show/hide so a stale fade-out cannot hide a newer pill

    /// <summary>
    /// Layout is re-applied on every lyrics load, so only an actual change into Kinetic shows the pill.
    /// </summary>
    private void UpdatePacingPill(bool isKinetic)
    {
        if (!isKinetic)
        {
            HidePacingPill();
        }
        else if (!_pacingShownForKinetic)
        {
            ShowPacingPill();
        }
        _pacingShownForKinetic = isKinetic;
    }

    private void ShowPacingPill()
    {
        HidePacingPill();
        if (!_settings.ShowKineticPacingPill) return;

        PacingPill.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        DoubleAnimation In(double from, double to) => new(from, to, PacingFade) { EasingFunction = ease };
        PacingPill.BeginAnimation(UIElement.OpacityProperty, In(0.0, 1.0));
        PacingTranslate.BeginAnimation(TranslateTransform.YProperty, In(14.0, 0.0));
        PacingScale.BeginAnimation(ScaleTransform.ScaleXProperty, In(0.96, 1.0));
        PacingScale.BeginAnimation(ScaleTransform.ScaleYProperty, In(0.96, 1.0));

        PacingPill.UpdateLayout();
        PacingSheen.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.Zero) { BeginTime = PacingFade });
        PacingSheenTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-160.0, PacingClip.ActualWidth + 60.0, TimeSpan.FromMilliseconds(950))
        {
            BeginTime = PacingFade,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        PacingProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 0.0, PacingHold) { BeginTime = PacingFade });

        var pulse = new DoubleAnimation(0.86, 1.12, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        PacingIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        PacingIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        if (_pacingTimer == null)
        {
            _pacingTimer = new DispatcherTimer { Interval = PacingFade + PacingHold };
            _pacingTimer.Tick += (s, e) => DismissPacingPill();
        }
        _pacingTimer.Start();
    }

    /// <summary>Glides down and fades out from wherever the pill currently is.</summary>
    private void DismissPacingPill()
    {
        _pacingTimer?.Stop();
        if (PacingPill.Visibility != Visibility.Visible) return;

        int run = ++_pacingRun;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0.0, PacingFade) { EasingFunction = ease };
        fade.Completed += (s, e) =>
        {
            if (run == _pacingRun) HidePacingPill();
        };
        PacingTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14.0, PacingFade) { EasingFunction = ease });
        PacingPill.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Stops everything at once and returns the pill to its resting state.</summary>
    private void HidePacingPill()
    {
        _pacingRun++;
        _pacingTimer?.Stop();

        PacingPill.Visibility = Visibility.Collapsed;
        PacingPill.BeginAnimation(UIElement.OpacityProperty, null);
        PacingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        PacingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PacingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PacingSheen.BeginAnimation(UIElement.OpacityProperty, null);
        PacingSheenTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PacingProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PacingIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PacingIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void OnPacingPillClicked(object sender, MouseButtonEventArgs e)
    {
        DismissPacingPill();
        e.Handled = true;
    }

    private void OnPacingDontShowClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _settings.ShowKineticPacingPill = false;
        SettingsService.Save(_settings);
        DismissPacingPill();
    }

    private void OnPacingClipSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PacingClip.Clip = new RectangleGeometry(new Rect(e.NewSize), 27.0, 27.0);
    }
}
