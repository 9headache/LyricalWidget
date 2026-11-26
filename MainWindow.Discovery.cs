using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using LyricalWidget.Common;

namespace LyricalWidget;

// First-launch guide and the "Have you tried Fullscreen Mode?" pill
public partial class MainWindow
{
    private const double PillGap = 10.0;
    private bool _isOnboarding = false; // Stays set for the session, so the pill waits for a later launch
    private bool _pillLeaving = false;

    private void ShowOnboarding(Action onClosed)
    {
        _isOnboarding = true;
        bool returningUser = _settings.HasSeenWelcome;
        var card = new OnboardingWindow(LblLockShortcut.Text.Replace(" ", ""));
        card.Closed += (s, e) =>
        {
            if (card.Completed)
            {
                _settings.HasCompletedOnboarding = true;
                _settings.HasSeenWelcome = true;
            }
            // Someone upgrading still gets this version's notes; a brand-new user has just read the guide
            bool showNotes = returningUser && _settings.LastSeenVersion != AppVersionInfo.Version;
            _settings.LastSeenVersion = AppVersionInfo.Version;
            SettingsService.Save(_settings);

            onClosed();
            if (showNotes && !_isClosing) ShowWhatsNew();
        };
        _guideWindow = null;
        card.Show();
    }

    private void InitFullscreenPill()
    {
        FullscreenPillPopup.CustomPopupPlacementCallback = PlaceFullscreenPill;
    }

    private void UpdateFullscreenPill()
    {
        bool show = _settings.HasCompletedOnboarding && !_settings.DismissedFullscreenPill && !_isOnboarding && !_pillLeaving &&
                    !_isLocked && !_settings.GamingMode && !_isClosing && !IsFullscreenActive && IsLoaded && IsVisible && Opacity > 0.5 &&
                    WindowState != WindowState.Minimized;
        if (show == FullscreenPillPopup.IsOpen) return;

        FullscreenPill.BeginAnimation(OpacityProperty, null);
        FullscreenPill.Opacity = 1.0;
        FullscreenPillPopup.IsOpen = show;
    }

    // On the side of the card the quick-access menu is not on, so the two never overlap
    private CustomPopupPlacement[] PlaceFullscreenPill(Size popupSize, Size targetSize, Point offset)
    {
        double scale = CardBorder.ActualWidth > 0 ? targetSize.Width / CardBorder.ActualWidth : 1.0;
        double x = (targetSize.Width - popupSize.Width) / 2.0;
        double y = _toolbarAbove ? targetSize.Height + (PillGap * scale) : -popupSize.Height - (PillGap * scale);
        return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal) };
    }

    private void RepositionFullscreenPill()
    {
        RepositionFisheyeWarning();
        if (!FullscreenPillPopup.IsOpen) return;
        FullscreenPillPopup.HorizontalOffset = FullscreenPillPopup.HorizontalOffset == 0.0 ? 0.001 : 0.0;
    }

    private void OnTryFullscreenClicked(object sender, RoutedEventArgs e)
    {
        DismissFullscreenPill(() => { if (!IsFullscreenActive) ToggleFullscreenVibe(); });
    }

    private void OnHideFullscreenPillClicked(object sender, RoutedEventArgs e) => DismissFullscreenPill(null);

    private void DismissFullscreenPill(Action? then)
    {
        if (_pillLeaving) return;
        _settings.DismissedFullscreenPill = true;
        SettingsService.Save(_settings);
        _pillLeaving = true;

        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (s, e) =>
        {
            FullscreenPillPopup.IsOpen = false;
            _pillLeaving = false;
            then?.Invoke();
        };
        FullscreenPill.BeginAnimation(OpacityProperty, fade);
    }
}
