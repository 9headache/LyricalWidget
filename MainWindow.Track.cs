using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;

namespace LyricalWidget;

// Track changes, lyric loading and the track banner
public partial class MainWindow
{
    private void TriggerTrackSwitchBanner(TrackInfo track)
    {
        if (IsFullscreenActive) return;
        if (!_settings.ShowTrackSwitchBanner || _settings.DisplayMode != "LyricsOnly" || !track.IsValid)
        {
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
            return;
        }

        BannerAlbumArt.Source = track.Thumbnail;
        ShowBannerToast("NOW PLAYING", track.Title, track.Artist);
    }

    private void ShowBannerToast(string microLabel, string title, string artist)
    {
        _bannerCts?.Cancel();
        _bannerCts = new CancellationTokenSource();
        var ct = _bannerCts.Token;

        BannerMicroLabel.Text = microLabel;
        BannerTrackTitle.Text = title;
        BannerTrackArtist.Text = artist;

        if (_settings.BackgroundStyle == "Glass")
        {
            TrackSwitchBanner.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x11, 0x13, 0x1D));
            TrackSwitchBanner.BorderThickness = new Thickness(1);
            TrackSwitchBanner.BorderBrush = BuildSpecularBrush(_currentAccentColor);
        }
        else
        {
            TrackSwitchBanner.Background = Brushes.Transparent;
            TrackSwitchBanner.BorderThickness = new Thickness(0);
        }

        TrackSwitchBanner.Visibility = Visibility.Visible;

        var slideIn = new DoubleAnimation(-14.0, 0.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220));

        BannerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        TrackSwitchBanner.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        Task.Delay(4000, ct).ContinueWith(t =>
        {
            if (ct.IsCancellationRequested) return;

            Dispatcher.Invoke(() =>
            {
                var slideOut = new DoubleAnimation(0.0, -14.0, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(260));
                fadeOut.Completed += (s, e) =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        TrackSwitchBanner.Visibility = Visibility.Collapsed;
                    }
                };

                BannerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
                TrackSwitchBanner.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            });
        }, TaskScheduler.Default);
    }

    private void OnTrackChanged(object? sender, TrackInfo track)
    {
        long seqId = Interlocked.Increment(ref _trackSequenceId);
        CancellationTokenSource cts;
        lock (_trackLoadLock)
        {
            try
            {
                _trackLoadCts?.Cancel();
                _trackLoadCts?.Dispose();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error canceling track load: {ex.Message}");
            }
            _trackLoadCts = new CancellationTokenSource();
            cts = _trackLoadCts;
        }
        var ct = cts.Token;

        Dispatcher.InvokeAsync(async () =>
        {
            if (seqId != Volatile.Read(ref _trackSequenceId) || ct.IsCancellationRequested) return;

            _bannerCts?.Cancel();
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
            _currentActiveIndex = -1;
            _lastRenderEffectivePos = TimeSpan.Zero;
            BeginBrowserLookup(track);
            _currentLyrics = LrcDocument.Empty();
            RebuildLyricsViews();

            if (!track.IsValid)
            {
                ApplyInvalidTrackState(track);
                return;
            }

            if (track.IsDjOrInterstitial)
            {
                ResolveBrowserClock(track);
                ApplyInterstitialTrackState(track);
                return;
            }

            if (_vibeWindow != null && _vibeWindow.IsVisible)
            {
                _vibeWindow.UpdateTrackFromMainWindow(track);
            }

            UpdateTrackMetadataDisplay(track);
            ApplyAlbumArtThumbnail(track.Thumbnail);
            ApplyDisplayModeAndTheme();
            if (!_clockForBrowser) TriggerTrackSwitchBanner(track);

            if (!IsFullscreenActive)
            {
                UpdateAutoHideState(_mediaService.IsPlaying);
                EnsureRenderingSubscribed();
            }

            try
            {
                await Task.Delay(180, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (seqId != Volatile.Read(ref _trackSequenceId) || ct.IsCancellationRequested) return;

            await LoadAlbumArtThumbnailAsync(track, ct, seqId);

            if (seqId != Volatile.Read(ref _trackSequenceId) || ct.IsCancellationRequested) return;

            await FetchLyricsAsync(track, ct, seqId);
        });
    }

    private void ApplyInvalidTrackState(TrackInfo track)
    {
        if (_vibeWindow != null && _vibeWindow.IsVisible)
        {
            _vibeWindow.UpdateTrackFromMainWindow(track);
        }

        SideTrackTitle.Text = "No Track Playing";
        SideTrackArtist.Text = "Play music in Spotify or browser";
        AlbumOnlyTrackTitle.Text = "No Track Playing";
        AlbumOnlyTrackArtist.Text = "Play music in Spotify or browser";

        SideAlbumArtImage.Source = null;
        AlbumOnlyImage.Source = null;
        BannerAlbumArt.Source = null;
        SideFallbackIcon.Visibility = Visibility.Visible;
        AlbumOnlyFallbackIcon.Visibility = Visibility.Visible;
        BackdropImageA.Source = null;
        BackdropImageB.Source = null;

        SideProgressFill.Width = 0;
        AlbumOnlyProgressFill.Width = 0;
        SideCurrentTime.Text = "0:00";
        AlbumOnlyCurrentTime.Text = "0:00";
        SideTotalTime.Text = "0:00";
        AlbumOnlyTotalTime.Text = "0:00";
        ApplyDisplayModeAndTheme();
        if (!IsFullscreenActive)
        {
            UpdateAutoHideState(false);
        }
    }

    private void ApplyInterstitialTrackState(TrackInfo track)
    {
        if (_vibeWindow != null && _vibeWindow.IsVisible)
        {
            _vibeWindow.UpdateTrackFromMainWindow(track);
        }

        string djTitle = string.IsNullOrWhiteSpace(track.Title) ? "Spotify DJ" : track.Title;
        string djArtist = string.IsNullOrWhiteSpace(track.Artist) ? "Commentary" : track.Artist;

        SideTrackTitle.Text = djTitle;
        SideTrackArtist.Text = djArtist;
        AlbumOnlyTrackTitle.Text = djTitle;
        AlbumOnlyTrackArtist.Text = djArtist;

        SideProgressFill.Width = 0;
        AlbumOnlyProgressFill.Width = 0;
        SideCurrentTime.Text = "0:00";
        AlbumOnlyCurrentTime.Text = "0:00";
        SideTotalTime.Text = FormatTime(track.Duration);
        AlbumOnlyTotalTime.Text = FormatTime(track.Duration);

        if (track.Thumbnail != null)
        {
            SideAlbumArtImage.Source = track.Thumbnail;
            AlbumOnlyImage.Source = track.Thumbnail;
            SideFallbackIcon.Visibility = Visibility.Collapsed;
            AlbumOnlyFallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            SideAlbumArtImage.Source = null;
            AlbumOnlyImage.Source = null;
            SideFallbackIcon.Visibility = Visibility.Visible;
            AlbumOnlyFallbackIcon.Visibility = Visibility.Visible;
        }

        ApplyDisplayModeAndTheme();
        if (!IsFullscreenActive)
        {
            UpdateAutoHideState(_mediaService.IsPlaying);
            EnsureRenderingSubscribed();
        }
    }

    private void UpdateTrackMetadataDisplay(TrackInfo track)
    {
        SideTrackTitle.Text = track.Title;
        SideTrackArtist.Text = track.Artist;
        AlbumOnlyTrackTitle.Text = track.Title;
        AlbumOnlyTrackArtist.Text = track.Artist;

        SideProgressFill.Width = 0;
        AlbumOnlyProgressFill.Width = 0;
        SideCurrentTime.Text = "0:00";
        AlbumOnlyCurrentTime.Text = "0:00";
        SideTotalTime.Text = FormatTime(track.Duration);
        AlbumOnlyTotalTime.Text = FormatTime(track.Duration);
        _lastDisplayedCurSec = -1;
        _lastDisplayedTotalSec = (int)track.Duration.TotalSeconds;
    }

    private void ApplyAlbumArtThumbnail(BitmapImage? thumbnail)
    {
        if (thumbnail != null)
        {
            SideAlbumArtImage.Source = thumbnail;
            AlbumOnlyImage.Source = thumbnail;
            SideFallbackIcon.Visibility = Visibility.Collapsed;
            AlbumOnlyFallbackIcon.Visibility = Visibility.Collapsed;

            var palette = ColorExtractor.ExtractPalette(thumbnail);
            ApplyExtractedPalette(palette, crossfade: false);
        }
        else
        {
            SideAlbumArtImage.Source = null;
            AlbumOnlyImage.Source = null;
            SideFallbackIcon.Visibility = Visibility.Visible;
            AlbumOnlyFallbackIcon.Visibility = Visibility.Visible;

            ApplyExtractedPalette(VibePalette.Default(), crossfade: false);
        }
    }

    private void ApplyExtractedPalette(VibePalette palette, bool crossfade)
    {
        _currentPalette = palette;
        UpdateAccentColors(palette.Primary);
        ApplyThemedLyricColors(palette, crossfade: crossfade);
        if (IsFullscreenActive && _vibeWindow != null)
        {
            _vibeWindow.ApplyPaletteImmediate(palette);
        }
    }

    private async Task LoadAlbumArtThumbnailAsync(TrackInfo track, CancellationToken ct, long seqId)
    {
        if (track.Thumbnail != null)
        {
            var palette = ColorExtractor.ExtractPalette(track.Thumbnail);
            if (seqId != Volatile.Read(ref _trackSequenceId) || ct.IsCancellationRequested) return;

            ApplyExtractedPalette(palette, crossfade: true);

            if (!IsFullscreenActive)
            {
                _ = Task.Run(async () =>
                {
                    var bakedBackdrop = await BakeAmbientBackdropAsync(track.Thumbnail);
                    if (bakedBackdrop != null && !ct.IsCancellationRequested && seqId == Volatile.Read(ref _trackSequenceId))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (seqId == Volatile.Read(ref _trackSequenceId) && !ct.IsCancellationRequested)
                            {
                                CrossfadeBackdrop(bakedBackdrop);
                            }
                        });
                    }
                }, ct);
            }
        }
        else
        {
            ApplyExtractedPalette(VibePalette.Default(), crossfade: true);
        }
    }

    private async Task FetchLyricsAsync(TrackInfo track, CancellationToken ct, long seqId)
    {
        LrcDocument doc;
        try
        {
            doc = await _lyricsService.GetLyricsAsync(track, upgradedDoc =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (seqId == Volatile.Read(ref _trackSequenceId) && !ct.IsCancellationRequested)
                    {
                        _currentLyrics = upgradedDoc;
                        RebuildLyricsViews();
                        ApplyDisplayModeAndTheme();
                        EnsureRenderingSubscribed();
                    }
                });
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (seqId != Volatile.Read(ref _trackSequenceId) || ct.IsCancellationRequested) return;

        _currentLyrics = doc;
        ResolveBrowserClock(track);
        RebuildLyricsViews();
        ApplyDisplayModeAndTheme();
        if (!IsFullscreenActive)
        {
            UpdateAutoHideState(_mediaService.IsPlaying);
            EnsureRenderingSubscribed();
        }
    }

    public async Task ForceRefreshAndReconnectAsync()
    {
        try
        {
            long seqId = Interlocked.Increment(ref _trackSequenceId);
            lock (_trackLoadLock)
            {
                try
                {
                    _trackLoadCts?.Cancel();
                    _trackLoadCts?.Dispose();
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error canceling track load: {ex.Message}");
                }
                _trackLoadCts = new CancellationTokenSource();
            }

            _bannerCts?.Cancel();
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
            _currentActiveIndex = -1;
            _lastRenderEffectivePos = TimeSpan.Zero;
            _currentLyrics = LrcDocument.Empty();
            RebuildLyricsViews();
            ApplyDisplayModeAndTheme();

            var previous = _mediaService.CurrentTrack;
            _lyricsService.RequestRefetch(previous);

            await _mediaService.ForceReconnectAsync();

            var current = _mediaService.CurrentTrack;
            if (current != null)
            {
                if (current.CacheKey != previous.CacheKey)
                {
                    _lyricsService.RequestRefetch(current);
                }
                OnTrackChanged(this, current);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ForceReconnect error: {ex.Message}");
        }
    }
}
