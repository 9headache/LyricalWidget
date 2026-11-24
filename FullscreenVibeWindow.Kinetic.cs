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
    private readonly KineticCameraController _cameraController = new();
    private readonly KineticLayoutDirector _layoutDirector = new();
    private readonly List<KineticPhraseVisual> _worldPhrases = new();
    private int _currentKineticActiveIndex = -1;
    private TimeSpan _lastKineticPosition = TimeSpan.Zero;
    private int _kineticRenderEpoch = 0;
    private double _lastKineticClockMs = 0.0;
    private KineticPhraseVisual? _prebuiltNextPhrase = null;
    private int _prebuiltNextIndex = -1;
    private int _prebuiltEpoch = -1;

    private Color _targetPosterBackdropColor = Colors.Transparent;
    private Color _currentPosterBackdropColor = Colors.Transparent;
    private readonly SolidColorBrush _posterBackdropBrush = new(Colors.Transparent);

    private void UpdateKineticFrame(
        double dt,
        long nowTicks,
        TimeSpan effectivePos,
        double currentTimeSec)
    {
        try
        {
            if (_currentKineticPreset == KineticPreset.SpotlightFog)
            {
                _spotlightFogRenderer.UpdateFrame(
                    dt,
                    effectivePos,
                    currentTimeSec,
                    _currentBassEnergy,
                    _currentBeatImpulse,
                    _mediaService.IsPlaying,
                    _isInstrumentalMode);
                return;
            }

            if (_currentKineticPreset == KineticPreset.LiveCode)
            {
                _liveCodeRenderer.UpdateFrame(
                    dt,
                    effectivePos,
                    currentTimeSec,
                    _currentBassEnergy,
                    _currentBeatImpulse,
                    _mediaService.IsPlaying,
                    _isInstrumentalMode);
                return;
            }

        // 2D Virtual Camera World update with smooth exponential damping
            _cameraController.Update(dt, _timeElapsedSec, _currentBassEnergy, _currentBeatImpulse, _kineticVibe);
            _cameraController.ApplyMatrix(ActualWidth, ActualHeight, WorldCameraMatrix, _timeElapsedSec);

            // Smooth 150ms interpolation for PosterBackdrop brush color
            if (PosterBackdrop.Visibility == Visibility.Visible && _targetPosterBackdropColor.A > 0)
            {
                if (_currentPosterBackdropColor != _targetPosterBackdropColor)
                {
                    double lerpFactor = Math.Clamp(dt / 0.150, 0.0, 1.0);
                    byte r = (byte)(_currentPosterBackdropColor.R + (_targetPosterBackdropColor.R - _currentPosterBackdropColor.R) * lerpFactor);
                    byte g = (byte)(_currentPosterBackdropColor.G + (_targetPosterBackdropColor.G - _currentPosterBackdropColor.G) * lerpFactor);
                    byte b = (byte)(_currentPosterBackdropColor.B + (_targetPosterBackdropColor.B - _currentPosterBackdropColor.B) * lerpFactor);
                    byte a = (byte)(_currentPosterBackdropColor.A + (_targetPosterBackdropColor.A - _currentPosterBackdropColor.A) * lerpFactor);
                    _currentPosterBackdropColor = Color.FromArgb(a, r, g, b);
                    _posterBackdropBrush.Color = _currentPosterBackdropColor;
                    PosterBackdrop.Background = _posterBackdropBrush;
                }
            }

            double curClockMs = effectivePos.TotalMilliseconds;
            bool isUserSeek = _lastKineticClockMs > 0 && Math.Abs(curClockMs - _lastKineticClockMs) > 250;

            if (isUserSeek)
            {
                // Genuine user seek (> 250ms jump): clear canvas & phrases, increment epoch
                _kineticRenderEpoch++;
                _worldPhrases.Clear();
                KineticWorldCanvas.Children.Clear();
                _prebuiltNextPhrase = null;
                _prebuiltNextIndex = -1;
                _prebuiltEpoch = -1;
                _currentKineticActiveIndex = -1;
                _lastKineticClockMs = curClockMs;
                _lastKineticPosition = effectivePos;
                _layoutDirector.Reset();
                _cameraController.Reset(new Point(ActualWidth > 0 ? ActualWidth * 0.52 : 960, ActualHeight > 0 ? ActualHeight * 0.46 : 500));
            }
            else
            {
                if (!_mediaService.IsPlaying)
                {
                    for (int idx = 0; idx < _worldPhrases.Count; idx++)
                    {
                        var ph = _worldPhrases[idx];
                        if (ph.ContainerScale != null && ph.ContainerScale.ScaleX > 1.05)
                        {
                            ph.ContainerScale.ScaleX = 1.0;
                            ph.ContainerScale.ScaleY = 1.0;
                        }
                    }
                }

                // Guard against slight backward SMTC timestamp jitter under 800ms
                double drift = _lastKineticClockMs - curClockMs;
                if (drift > 0 && drift < 800)
                {
                    // Clamp to monotonic time to prevent re-triggering lines/words
                    curClockMs = _lastKineticClockMs;
                    effectivePos = TimeSpan.FromMilliseconds(curClockMs);
                }
                else
                {
                    _lastKineticClockMs = curClockMs;
                    _lastKineticPosition = effectivePos;
                }
            }

            int rawIndex = FindCurrentLyricLineIndex(_currentLyrics.Lines, effectivePos);
            int targetLineIndex = rawIndex;

            // Strict Latching: during gaps after a line ends, or small backward jitter, NEVER decrement line index
            if (!isUserSeek && _currentKineticActiveIndex >= 0 && targetLineIndex < _currentKineticActiveIndex)
            {
                targetLineIndex = _currentKineticActiveIndex;
            }

            // NEVER rebuild or re-animate a line if targetLineIndex == _currentKineticActiveIndex
            if (targetLineIndex >= 0 && targetLineIndex < _currentLyrics.Lines.Count && targetLineIndex != _currentKineticActiveIndex)
            {
                _currentKineticActiveIndex = targetLineIndex;
                var line = _currentLyrics.Lines[targetLineIndex];
                double vpW = ActualWidth > 0 ? ActualWidth : 1920;
                double vpH = ActualHeight > 0 ? ActualHeight : 1080;
                var vpSize = new Size(vpW, vpH);

                KineticPhraseVisual newPhrase;
                if (_prebuiltNextPhrase != null && _prebuiltNextIndex == targetLineIndex && _prebuiltEpoch == _kineticRenderEpoch)
                {
                    newPhrase = _prebuiltNextPhrase;
                    _prebuiltNextPhrase = null;
                    _prebuiltNextIndex = -1;
                    _prebuiltEpoch = -1;
                }
                else
                {
                    newPhrase = _layoutDirector.CreatePhrase(
                        line,
                        targetLineIndex,
                        _currentLyrics.Lines,
                        _currentPalette,
                        _kineticVibe,
                        _kineticPreset,
                        _currentBassEnergy,
                        _currentBeatImpulse,
                        vpSize);
                }

                if (newPhrase.IsPosterMode)
                {
                    PosterBackdrop.Visibility = Visibility.Visible;
                    _targetPosterBackdropColor = newPhrase.BackdropColor;
                    if (_currentPosterBackdropColor.A == 0)
                    {
                        _currentPosterBackdropColor = _targetPosterBackdropColor;
                        _posterBackdropBrush.Color = _currentPosterBackdropColor;
                        PosterBackdrop.Background = _posterBackdropBrush;
                    }
                    if (newPhrase.TriggerZoomPunch)
                    {
                        _cameraController.TriggerZoomPunch(1.35);
                    }
                }
                else
                {
                    PosterBackdrop.Visibility = Visibility.Collapsed;
                    _targetPosterBackdropColor = Colors.Transparent;
                    _currentPosterBackdropColor = Colors.Transparent;
                }

                // Place container on canvas, guarantee Visibility, Opacity, and top ZIndex
                Canvas.SetLeft(newPhrase.Container, newPhrase.WorldAnchor.X - (newPhrase.VisualSize.Width * 0.5));
                Canvas.SetTop(newPhrase.Container, newPhrase.WorldAnchor.Y - (newPhrase.VisualSize.Height * 0.5));
                Panel.SetZIndex(newPhrase.Container, 2);
                newPhrase.Container.Visibility = Visibility.Visible;
                newPhrase.Container.Opacity = 1.0;
                newPhrase.IsExiting = false;

                // If seeking/jumping mid-line, instantly reveal all words up to effectivePos
                if (isUserSeek)
                {
                    newPhrase.CatchUpToTime(effectivePos);
                }

                KineticWorldCanvas.Children.Add(newPhrase.Container);
                _worldPhrases.Add(newPhrase);

                // Mark older phrases as exiting and lower ZIndex
                for (int i = 0; i < _worldPhrases.Count - 1; i++)
                {
                    _worldPhrases[i].IsExiting = true;
                    Panel.SetZIndex(_worldPhrases[i].Container, 1);
                }

                if (newPhrase.IsPosterMode && newPhrase.Container.Tag is PosterTransitionInfo newInfo && newInfo.IsPushDownSwap)
                {
                    var exitingPhrase = _worldPhrases.Count > 1 ? _worldPhrases[^2] : null;
                    double pushDist = Math.Max(260.0, Math.Max(newPhrase.VisualSize.Height, exitingPhrase != null ? exitingPhrase.VisualSize.Height : 260.0) + 30.0);
                    if (newInfo.PushTranslate != null)
                    {
                        newInfo.PushTranslate.Y = -pushDist;
                    }
                }

                // Dynamic camera transition speed based on line duration
                double lineDurMs = Math.Max(250.0, (line.EndTime - line.Timestamp).TotalMilliseconds);
                _cameraController.FocusPhrase(newPhrase, lineDurMs);

                // Asynchronous Lookahead Pre-build: pre-build next line (targetLineIndex + 1)
                int nextIndex = targetLineIndex + 1;
                if (nextIndex < _currentLyrics.Lines.Count)
                {
                    int epoch = _kineticRenderEpoch;
                    var nextLine = _currentLyrics.Lines[nextIndex];
                    var pal = _currentPalette;
                    var vibe = _kineticVibe;
                    double lookaheadBass = _currentBassEnergy;
                    double lookaheadBeat = _currentBeatImpulse;
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (epoch == _kineticRenderEpoch && _currentKineticActiveIndex == targetLineIndex)
                        {
                            _prebuiltNextPhrase = _layoutDirector.CreatePhrase(
                                nextLine,
                                nextIndex,
                                _currentLyrics.Lines,
                                pal,
                                vibe,
                                _kineticPreset,
                                lookaheadBass,
                                lookaheadBeat,
                                vpSize);
                            _prebuiltNextIndex = nextIndex;
                            _prebuiltEpoch = epoch;
                        }
                    }, DispatcherPriority.Background);
                }
            }

            // Word nudging & continuous zoom creep (1.00 -> 1.04) over active phrase
            if (_worldPhrases.Count > 0)
            {
                var activePhrase = _worldPhrases[^1];

                if (activePhrase.IsPosterMode && activePhrase.Container.Tag is PosterTransitionInfo activeInfo && activeInfo.IsPushDownSwap && activeInfo.PushProgress < 1.0)
                {
                    activeInfo.PushProgress = Math.Clamp(activeInfo.PushProgress + (dt / 0.180), 0.0, 1.0);
                    double easedPush = Math.Sin(activeInfo.PushProgress * Math.PI * 0.5);
                    var exitingPhrase = _worldPhrases.Count > 1 ? _worldPhrases[^2] : null;
                    double pushDist = Math.Max(260.0, Math.Max(activePhrase.VisualSize.Height, exitingPhrase != null ? exitingPhrase.VisualSize.Height : 260.0) + 30.0);
                    if (activeInfo.PushTranslate != null)
                    {
                        activeInfo.PushTranslate.Y = -pushDist * (1.0 - easedPush);
                    }
                    if (exitingPhrase != null && exitingPhrase.Container.Tag is PosterTransitionInfo exitInfo && exitInfo.PushTranslate != null)
                    {
                        exitInfo.PushTranslate.Y = pushDist * easedPush;
                    }
                    if (activeInfo.PushProgress >= 1.0)
                    {
                        if (activeInfo.PushTranslate != null) activeInfo.PushTranslate.Y = 0.0;
                        if (exitingPhrase != null)
                        {
                            exitingPhrase.Container.Opacity = 0.0;
                            exitingPhrase.IsExiting = true;
                        }
                    }
                }

                // Continuous zoom creep over line duration so camera is never static
                double totalSpan = (activePhrase.LineEndTime - activePhrase.LineStartTime).TotalSeconds;
                if (totalSpan > 0.2)
                {
                    double elapsed = (effectivePos - activePhrase.LineStartTime).TotalSeconds;
                    double progressRatio = Math.Clamp(elapsed / totalSpan, 0.0, 1.0);
                    _cameraController.SetZoomCreep(progressRatio);
                }

                // Subtly pan camera along active word/row
                var activeWord = activePhrase.GetActiveWord(effectivePos);
                if (activeWord != null)
                {
                    Point wordWorldCenter = activePhrase.GetWordWorldCenter(activeWord);
                    _cameraController.NudgeTowardWord(wordWorldCenter, 0.08);
                }
            }

            // Hard cap to strictly max 2 phrases in canvas (the active one and the exiting one)
            while (_worldPhrases.Count > 2)
            {
                var oldest = _worldPhrases[0];
                KineticWorldCanvas.Children.Remove(oldest.Container);
                _worldPhrases.RemoveAt(0);
            }

            // Smooth 60 FPS transitions: update phrases and smoothly fade out exiting phrases
            for (int i = _worldPhrases.Count - 1; i >= 0; i--)
            {
                var p = _worldPhrases[i];

                // True Letter Fly-Through Portal Transition
                if (p.IsPosterMode && p.HasPortalLetter && p.ContainerScale != null && p.Container.Tag is PosterTransitionInfo portalInfo)
                {
                    double remainingMs = (p.LineEndTime - effectivePos).TotalMilliseconds;
                    bool inPortalWindow = p.IsExiting || (remainingMs <= 180.0 && remainingMs >= -400.0);
                    if (inPortalWindow)
                    {
                        double zoomProgress = Math.Clamp((180.0 - remainingMs) / 180.0, 0.0, 1.0);
                        double eased = zoomProgress * zoomProgress * zoomProgress;
                        double currentZoom = 1.0 + (13.0 * eased);

                        if (double.IsFinite(currentZoom))
                        {
                            p.ContainerScale.ScaleX = currentZoom;
                            p.ContainerScale.ScaleY = currentZoom;
                        }

                        if (zoomProgress >= 0.92 && !portalInfo.PortalLocked)
                        {
                            portalInfo.PortalLocked = true;
                            _targetPosterBackdropColor = portalInfo.NextBackdropColor;
                            _currentPosterBackdropColor = portalInfo.NextBackdropColor;
                            _posterBackdropBrush.Color = portalInfo.NextBackdropColor;
                            PosterBackdrop.Background = _posterBackdropBrush;
                            p.Container.Opacity = 0.0;
                        }
                    }
                }

                if (p.IsExiting)
                {
                    p.Container.Opacity -= dt * (p.HasPortalLetter ? 4.5 : 2.5);
                    if (p.Container.Opacity <= 0.02)
                    {
                        KineticWorldCanvas.Children.Remove(p.Container);
                        _worldPhrases.RemoveAt(i);
                        continue;
                    }
                }

                p.Update(effectivePos, dt, currentTimeSec, _currentBassEnergy, _currentBeatImpulse);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateKineticFrame] Error in preset {_currentKineticPreset}: {ex.Message}");
        }
    }

}
