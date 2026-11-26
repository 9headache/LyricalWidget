using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// Layout mode and Kinetic style switching
public partial class FullscreenVibeWindow
{
    private bool IsMoonlightActive => _isKineticMode && _currentKineticPreset == KineticPreset.MoonlightBloom;

    public void EnterMoonlightBloom()
    {
        if (IsMoonlightActive) return;
        SetActiveKineticPreset(KineticPreset.MoonlightBloom);
        ApplyLayoutMode(true);
    }

    public void LeaveMoonlightBloom()
    {
        if (IsMoonlightActive) ApplyLayoutMode(false);
    }

    /// <summary>Brings an open window in line with a preset that has just been loaded.</summary>
    public void ApplyPreset(WidgetState state)
    {
        SetKineticVibe(_settings.FullscreenKineticVibe);
        _spotlightFogRenderer.SetBackdropMode(_settings.SpotlightBackdropMode);
        _isAudioReactiveEnabled = _settings.AudioReactiveGlow;
        UpdateAudioReactiveButtonLabel();
        bool kinetic = state.VibeLayoutMode == "Kinetic";
        if (Enum.TryParse<KineticPreset>(state.VibeKineticPreset, out var preset)) SetActiveKineticPreset(preset);
        ApplyLayoutMode(kinetic);
        ApplyFisheye();
        UpdateFisheyeButton();

        if (state.VibeMonitor != null && CurrentMonitor()?.DeviceName != state.VibeMonitor) MoveToMonitor(state.VibeMonitor);
        if (state.VibeFullscreen == true) EnterFullscreen();
        else if (state.VibeFullscreen == false) ExitFullscreen();
        if (!_isFullscreen)
        {
            if (state.VibeWindowState == "Maximized") WindowState = WindowState.Maximized;
            else if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        }
    }

    private void SetMoonlightVisible(bool visible)
    {
        MoonlightHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        // The stage is opaque, so the orbs behind it would only cost frames
        BackgroundOrbsCanvas.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        if (!visible) _moonlightRenderer.Reset();
        ApplyFisheye();
    }

    private string _kineticVibe = "Auto";
    private KineticPreset _currentKineticPreset = KineticPreset.MoonlightBloom;
    private string _kineticPreset => _currentKineticPreset.ToString();

    private void ToggleLayoutMode() => ApplyLayoutMode(!_isKineticMode);

    private void SetKineticVibe(string vibe)
    {
        _kineticVibe = vibe;
        _settings.FullscreenKineticVibe = _kineticVibe;
        SettingsService.Save(_settings);
        UpdateVibeButtonLabel();
        _currentKineticActiveIndex = -1; // Forces the current phrase to be rebuilt
    }

    public void SetActiveKineticPreset(KineticPreset newPreset)
    {
        KineticStandardHost.Visibility = Visibility.Collapsed;
        PosterBackdrop.Visibility = Visibility.Collapsed;
        _currentKineticActiveIndex = -1;
        _worldPhrases.Clear();
        KineticWorldCanvas.Children.Clear();
        _prebuiltNextPhrase = null;
        _prebuiltNextIndex = -1;
        _prebuiltEpoch = -1;
        _layoutDirector.Reset();
        _cameraController.Reset(new Point(0, 0));
        _lastKineticClockMs = 0.0;
        _lastKineticPosition = TimeSpan.Zero;

        SetMoonlightVisible(false);

        SpotlightFogHost.Visibility = Visibility.Collapsed;
        _spotlightFogRenderer.Deactivate();

        LiveCodeHost.Visibility = Visibility.Collapsed;
        _liveCodeRenderer.Deactivate();

        _currentKineticPreset = newPreset;
        _settings.FullscreenKineticPreset = newPreset.ToString();
        SettingsService.Save(_settings);
        UpdateKineticPresetButtonLabel();

        if (!_isKineticMode)
        {
            return;
        }

        double vpW = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : (ActualWidth > 0 ? ActualWidth : 1920.0);
        double vpH = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : (ActualHeight > 0 ? ActualHeight : 1080.0);
        if (vpW <= 0) vpW = 1920.0;
        if (vpH <= 0) vpH = 1080.0;

        switch (newPreset)
        {
            case KineticPreset.Editorial:
            case KineticPreset.Poster:
                KineticStandardHost.Visibility = Visibility.Visible;
                KineticStandardHost.Opacity = 1.0;
                PosterBackdrop.Visibility = (newPreset == KineticPreset.Poster) ? Visibility.Visible : Visibility.Collapsed;
                KineticViewportCanvas.Visibility = Visibility.Visible;
                KineticViewportCanvas.Opacity = 1.0;
                _layoutDirector.Reset();
                _layoutDirector.IsPosterDimMode = _settings.PosterDimMode;
                _cameraController.Reset(new Point(vpW * 0.52, vpH * 0.46));
                _currentKineticActiveIndex = -1;
                _lastKineticClockMs = 0.0;
                _lastKineticPosition = TimeSpan.Zero;

                if (_currentLyrics.HasSyncedLyrics && _currentLyrics.Lines.Count > 0)
                {
                    int totalOffsetMs = _settings.SyncOffsetMs + _currentLyrics.UserOffsetMs;
                    TimeSpan effectivePos = _mediaService.CurrentPosition + TimeSpan.FromMilliseconds(totalOffsetMs);
                    if (effectivePos < TimeSpan.Zero) effectivePos = TimeSpan.Zero;

                    int activeIndex = FindCurrentLyricLineIndex(_currentLyrics.Lines, effectivePos);
                    if (activeIndex >= 0 && activeIndex < _currentLyrics.Lines.Count)
                    {
                        _currentKineticActiveIndex = activeIndex;
                        var line = _currentLyrics.Lines[activeIndex];
                        var newPhrase = _layoutDirector.CreatePhrase(
                            line,
                            activeIndex,
                            _currentLyrics.Lines,
                            _currentPalette,
                            _kineticVibe,
                            newPreset.ToString(),
                            _currentBassEnergy,
                            _currentBeatImpulse,
                            new Size(vpW, vpH));

                        Canvas.SetLeft(newPhrase.Container, newPhrase.WorldAnchor.X - (newPhrase.VisualSize.Width * 0.5));
                        Canvas.SetTop(newPhrase.Container, newPhrase.WorldAnchor.Y - (newPhrase.VisualSize.Height * 0.5));
                        Panel.SetZIndex(newPhrase.Container, 2);
                        newPhrase.Container.Visibility = Visibility.Visible;
                        newPhrase.Container.Opacity = 1.0;
                        newPhrase.IsExiting = false;
                        newPhrase.CatchUpToTime(effectivePos);

                        KineticWorldCanvas.Children.Add(newPhrase.Container);
                        _worldPhrases.Add(newPhrase);

                        if (newPhrase.IsPosterMode)
                        {
                            PosterBackdrop.Visibility = Visibility.Visible;
                            PosterBackdrop.Background = new SolidColorBrush(newPhrase.BackdropColor);
                        }

                        _cameraController.SnapTo(newPhrase.WorldAnchor.X, newPhrase.WorldAnchor.Y, newPhrase.BaseZoom, newPhrase.TargetCameraAngle);
                        _cameraController.ApplyMatrix(vpW, vpH, WorldCameraMatrix, _timeElapsedSec);
                    }
                }
                break;

            case KineticPreset.SpotlightFog:
                SpotlightFogHost.Visibility = Visibility.Visible;
                SpotlightFogHost.Opacity = 1.0;
                _spotlightFogRenderer.Activate(vpW, vpH);
                _spotlightFogRenderer.SetBackdropMode(_settings.SpotlightBackdropMode ?? "HdArtwork");
                _spotlightFogRenderer.UpdateLyrics(_currentLyrics);
                var track = _mediaService.CurrentTrack;
                if (track != null && track.Thumbnail != null)
                {
                    _spotlightFogRenderer.UpdateAlbumArt(track.Thumbnail, _currentPalette);
                }
                if (_highResArtworkService.CurrentHdArtwork != null)
                {
                    _spotlightFogRenderer.UpdateHdAlbumArt(_highResArtworkService.CurrentHdArtwork);
                }
                _spotlightFogRenderer.Reset();
                break;

            case KineticPreset.MoonlightBloom:
                SetMoonlightVisible(true);
                _moonlightRenderer.UpdateLyrics(_currentLyrics);
                break;

            case KineticPreset.LiveCode:
                LiveCodeHost.Visibility = Visibility.Visible;
                LiveCodeHost.Opacity = 1.0;
                _liveCodeRenderer.Activate(vpW, vpH);
                var liveTrack = _mediaService.CurrentTrack;
                _liveCodeRenderer.UpdateTrack(liveTrack);
                _liveCodeRenderer.UpdatePalette(_currentPalette);
                _liveCodeRenderer.UpdateLyrics(_currentLyrics);
                if (_highResArtworkService.CurrentHdArtwork != null)
                {
                    _liveCodeRenderer.UpdateHdAlbumArt(_highResArtworkService.CurrentHdArtwork);
                }
                else if (liveTrack != null && liveTrack.Thumbnail != null)
                {
                    _liveCodeRenderer.UpdateAlbumArt(liveTrack.Thumbnail, _currentPalette);
                }
                _liveCodeRenderer.Reset();
                break;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            double renderW = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : ActualWidth;
            double renderH = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : ActualHeight;
            if (renderW > 0 && renderH > 0)
            {
                ForwardSizeToActivePreset(renderW, renderH);
            }
        }));
    }

    private void ForwardSizeToActivePreset(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _spotlightFogRenderer.OnResize(width, height);
        _liveCodeRenderer.OnResize(width, height);
    }

    private void UpdateKineticPresetButtonLabel()
    {
        if (BtnToggleKineticPreset != null)
        {
            BtnToggleKineticPreset.Content = $"Style: {GetPresetLabel(_currentKineticPreset)}";
        }
        UpdatePosterDimButtonUi();
        UpdateSpotlightBackdropButtonUi();
        UpdateFisheyeButton();
    }

    private void UpdateSpotlightBackdropButtonUi()
    {
        if (BtnToggleSpotlightBackdrop != null)
        {
            bool isSpotlight = _currentKineticPreset == KineticPreset.SpotlightFog;
            BtnToggleSpotlightBackdrop.Visibility = (_isKineticMode && isSpotlight) ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleSpotlightBackdrop.Content = _settings.SpotlightBackdropMode == "FluidColor" ? "Backdrop: Fluid" : "Backdrop: HD Art";
        }
    }

    private void OnToggleSpotlightBackdropClicked(object sender, RoutedEventArgs e)
    {
        _settings.SpotlightBackdropMode = _settings.SpotlightBackdropMode == "HdArtwork" ? "FluidColor" : "HdArtwork";
        SettingsService.Save(_settings);
        _spotlightFogRenderer.SetBackdropMode(_settings.SpotlightBackdropMode);
        UpdateSpotlightBackdropButtonUi();
    }

    private void UpdatePosterDimButtonUi()
    {
        if (BtnTogglePosterDim != null)
        {
            bool isPoster = _currentKineticPreset == KineticPreset.Poster;
            BtnTogglePosterDim.Visibility = (_isKineticMode && isPoster) ? Visibility.Visible : Visibility.Collapsed;
            BtnTogglePosterDim.Content = _settings.PosterDimMode ? "Tone: Dim" : "Tone: Vivid";
        }
    }

    private void OnTogglePosterDimClicked(object sender, RoutedEventArgs e)
    {
        _settings.PosterDimMode = !_settings.PosterDimMode;
        SettingsService.Save(_settings);
        _layoutDirector.IsPosterDimMode = _settings.PosterDimMode;
        UpdatePosterDimButtonUi();
    }

    private void UpdateLayoutModeButtonLabel()
    {
        BtnToggleLayoutMode.Content = _isKineticMode ? "Mode: Kinetic" : "Mode: Split";
    }

    private void UpdateVibeButtonLabel()
    {
        BtnToggleVibe.Content = $"Vibe: {_kineticVibe}";
    }

    private void ApplyLayoutMode(bool isKinetic)
    {
        _isKineticMode = isKinetic;
        _settings.FullscreenLayoutMode = isKinetic ? "Kinetic" : "Split";
        SettingsService.Save(_settings);

        UpdateLayoutModeButtonLabel();
        UpdateKineticPresetButtonLabel();
        SetMoonlightVisible(false);
        UpdatePacingPill(isKinetic);

        if (isKinetic)
        {
            KineticControlsPanel.Visibility = Visibility.Visible;
            VignetteEdgeStop.Color = (Color)ColorConverter.ConvertFromString("#9504060A");
            BottomMarqueeCanvas.Visibility = Visibility.Visible;

            if (!_currentLyrics.HasSyncedLyrics)
            {
                // Nothing to animate: the album card gets the stage to itself
                DismissIntro();
                BottomMarqueeCanvas.Visibility = Visibility.Collapsed;
                LandscapeLayout.Visibility = Visibility.Visible;
                PortraitLayout.Visibility = Visibility.Collapsed;
                LyricsSideContainer.Visibility = Visibility.Collapsed;
                LandscapeColLeft.Width = new GridLength(1.0, GridUnitType.Star);
                LandscapeColRight.Width = new GridLength(0.0, GridUnitType.Pixel);
                AlbumSideContainer.HorizontalAlignment = HorizontalAlignment.Center;
                AlbumSideContainer.Margin = new Thickness(0);
                TrackTitleText.TextAlignment = TextAlignment.Center;
                TrackArtistText.TextAlignment = TextAlignment.Center;
                UpdateResponsiveLayout();

                KineticStandardHost.Visibility = Visibility.Collapsed;
                SpotlightFogHost.Visibility = Visibility.Collapsed;
                LiveCodeHost.Visibility = Visibility.Collapsed;
                KineticCornerBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                LandscapeLayout.Visibility = Visibility.Collapsed;
                PortraitLayout.Visibility = Visibility.Collapsed;
                KineticCornerBadge.Visibility = Visibility.Visible;
                KineticCornerBadge.Opacity = 1.0;

                SetActiveKineticPreset(_currentKineticPreset);
            }
        }
        else
        {
            HideIntro();
            KineticControlsPanel.Visibility = Visibility.Collapsed;
            VignetteEdgeStop.Color = (Color)ColorConverter.ConvertFromString("#7504060A");
            BottomMarqueeCanvas.Visibility = Visibility.Collapsed;

            KineticStandardHost.Visibility = Visibility.Collapsed;
            SpotlightFogHost.Visibility = Visibility.Collapsed;
            LiveCodeHost.Visibility = Visibility.Collapsed;
            KineticCornerBadge.Visibility = Visibility.Collapsed;

            _spotlightFogRenderer.Deactivate();
            _liveCodeRenderer.Deactivate();

            _currentKineticActiveIndex = -1;
            _worldPhrases.Clear();
            KineticWorldCanvas.Children.Clear();
            _layoutDirector.Reset();
            _cameraController.Reset(new Point(0, 0));

            if (_isPortraitMode)
            {
                PortraitLayout.Visibility = Visibility.Visible;
                LandscapeLayout.Visibility = Visibility.Collapsed;
            }
            else
            {
                LandscapeLayout.Visibility = Visibility.Visible;
                PortraitLayout.Visibility = Visibility.Collapsed;
            }

            SetInstrumentalMode(!_currentLyrics.HasSyncedLyrics);
        }
    }
}
