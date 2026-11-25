using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LyricalWidget;

public partial class FullscreenVibeWindow
{
    // Track intro: one timer owns its whole life, so it can never outlast it

    private static readonly TimeSpan IntroFadeIn = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan IntroFadeOut = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan IntroTotal = TimeSpan.FromMilliseconds(2400); // Fades included
    private DispatcherTimer? _introTimer;
    private bool _isIntroVisible = false; // False from the moment it starts leaving
    private int _introRun = 0;
    private string _introTrackKey = string.Empty;
    private string _introShownKey = string.Empty;

    private void ShowIntro()
    {
        HideIntro();
        if (_isClosing || !_mediaService.CurrentTrack.IsValid) return;

        _introShownKey = _introTrackKey;
        _isIntroVisible = true;
        IntroHeroCard.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        IntroHeroCard.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, IntroFadeIn) { EasingFunction = ease });
        IntroHeroTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10.0, 0.0, IntroFadeIn) { EasingFunction = ease });
        // Slow push-in for as long as it stays
        var push = new DoubleAnimation(0.985, 1.02, IntroTotal);
        IntroHeroScale.BeginAnimation(ScaleTransform.ScaleXProperty, push);
        IntroHeroScale.BeginAnimation(ScaleTransform.ScaleYProperty, push);

        if (_introTimer == null)
        {
            _introTimer = new DispatcherTimer { Interval = IntroTotal - IntroFadeOut };
            _introTimer.Tick += (s, e) => DismissIntro();
        }
        _introTimer.Start();
    }

    private void DismissIntro()
    {
        _introTimer?.Stop();
        if (!_isIntroVisible) return;

        _isIntroVisible = false;
        int run = ++_introRun;
        var fade = new DoubleAnimation(0.0, IntroFadeOut) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (s, e) =>
        {
            if (run == _introRun) HideIntro();
        };
        IntroHeroCard.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void HideIntro()
    {
        _introRun++;
        _isIntroVisible = false;
        _introTimer?.Stop();
        IntroHeroCard.Visibility = Visibility.Collapsed;
        IntroHeroCard.BeginAnimation(UIElement.OpacityProperty, null);
        IntroHeroCard.Opacity = 0.0;
        IntroHeroTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        IntroHeroScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        IntroHeroScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    /// <summary>Letter-spaces text. WPF has no tracking, so thin spaces go between letters, joined so words still wrap whole.</summary>
    private static string Track(string text, string gap)
    {
        // Joined scripts (Arabic, Indic, Thai...) would fall apart
        foreach (char c in text)
        {
            if ((c >= 0x0590 && c <= 0x10FF) || char.IsSurrogate(c)) return text;
        }

        var sb = new System.Text.StringBuilder(text.Length * 4);
        for (int i = 0; i < text.Length; i++)
        {
            // Word gaps grow with the letter gaps, or tracked words would run together
            if (text[i] == ' ') sb.Append(' ', gap.Length);
            sb.Append(text[i]);
            bool last = i == text.Length - 1;
            if (last || char.IsWhiteSpace(text[i]) || char.IsWhiteSpace(text[i + 1]) ||
                char.GetUnicodeCategory(text[i + 1]) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append('\u2060').Append(gap).Append('\u2060');
        }
        return sb.ToString();
    }
}
