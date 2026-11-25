using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;

namespace LyricalWidget;

// Track changes, metadata and the title marquee
public partial class FullscreenVibeWindow
{
    private CancellationTokenSource? _hdArtFetchCts;
    private CancellationTokenSource? _lyricsFetchCts;
    private double _marqueeSingleItemWidth = 1000.0;

    private void OnTimelineResynced(object? sender, TimeSpan pos)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_isKineticMode)
            {
                _kineticRenderEpoch++;
                _worldPhrases.Clear();
                KineticWorldCanvas.Children.Clear();
                _prebuiltNextPhrase = null;
                _prebuiltNextIndex = -1;
                _prebuiltEpoch = -1;
                _layoutDirector.Reset();
                _cameraController.Reset(new Point(0, 0));
                _currentKineticActiveIndex = -1;
                _lastKineticPosition = TimeSpan.Zero;
                _lastKineticClockMs = 0.0;
            }
        });
    }

    private void ApplyTrackMetadata(TrackInfo track)
    {
        MarqueeStack.Children.Clear();

        if (!track.IsValid)
        {
            TrackTitleText.Text = "No Track Playing";
            TrackArtistText.Text = "Play music in Spotify or browser";
            PortraitTrackTitleText.Text = "No Track Playing";
            PortraitTrackArtistText.Text = "Play music in Spotify or browser";
            KineticCornerTitle.Text = "No Track Playing";
            KineticCornerArtist.Text = "Play music in Spotify or browser";
            AlbumCoverImage.Source = null;
            PortraitAlbumCoverImage.Source = null;
            KineticCornerImage.Source = null;
            AlbumFallbackIcon.Visibility = Visibility.Visible;
            PortraitAlbumFallbackIcon.Visibility = Visibility.Visible;
            ProgressBarFill.Width = 0;
            PortraitProgressBarFill.Width = 0;
            KineticCornerProgressFill.Width = 0;
            CurrentTimeText.Text = "0:00";
            TotalTimeText.Text = "0:00";
            return;
        }

        TrackTitleText.Text = track.Title;
        TrackArtistText.Text = track.Artist;
        PortraitTrackTitleText.Text = track.Title;
        PortraitTrackArtistText.Text = track.Artist;
        KineticCornerTitle.Text = track.Title;
        KineticCornerArtist.Text = track.Artist;
        IntroHeroTitle.Text = Track(track.Title, "\u200A");
        IntroHeroArtist.Text = Track(track.Artist.ToUpperInvariant(), "\u200A\u200A");
        IntroHeroArtistPill.Visibility = string.IsNullOrWhiteSpace(track.Artist) ? Visibility.Collapsed : Visibility.Visible;
        TotalTimeText.Text = FormatTime(track.Duration);

        string marqueeUnit = $"{track.Title.ToUpperInvariant()}   —   ";
        var marqueeTypeface = new Typeface(new FontFamily("Segoe UI Black, Impact, Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
        var size = MeasureMarqueeText(marqueeUnit, marqueeTypeface, 96.0);
        _marqueeSingleItemWidth = Math.Max(200.0, size.Width);

        for (int i = 0; i < 6; i++)
        {
            var path = CreateMarqueeTextPath(
                marqueeUnit, marqueeTypeface, 96.0, Brushes.White, 2.0);
            MarqueeStack.Children.Add(path);
        }

        if (track.Thumbnail != null)
        {
            AlbumCoverImage.Source = track.Thumbnail;
            PortraitAlbumCoverImage.Source = track.Thumbnail;
            KineticCornerImage.Source = track.Thumbnail;
            AlbumFallbackIcon.Visibility = Visibility.Collapsed;
            PortraitAlbumFallbackIcon.Visibility = Visibility.Collapsed;
            _spotlightFogRenderer.UpdateAlbumArt(track.Thumbnail, _currentPalette);
            _liveCodeRenderer.UpdateTrack(track);
            _liveCodeRenderer.UpdateAlbumArt(track.Thumbnail, _currentPalette);
        }
        else
        {
            AlbumCoverImage.Source = null;
            PortraitAlbumCoverImage.Source = null;
            KineticCornerImage.Source = null;
            AlbumFallbackIcon.Visibility = Visibility.Visible;
            PortraitAlbumFallbackIcon.Visibility = Visibility.Visible;
            _spotlightFogRenderer.UpdateAlbumArt(null, _currentPalette);
            _liveCodeRenderer.UpdateTrack(track);
            _liveCodeRenderer.UpdateAlbumArt(null, _currentPalette);
        }

        _hdArtFetchCts?.Cancel();
        _hdArtFetchCts = new CancellationTokenSource();
        var ct = _hdArtFetchCts.Token;
        _ = Task.Run(async () =>
        {
            var hdArt = await _highResArtworkService.FetchHighResArtworkAsync(track.Artist, track.Title, ct);
            if (hdArt != null && !ct.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        AlbumCoverImage.Source = hdArt;
                        PortraitAlbumCoverImage.Source = hdArt;
                        KineticCornerImage.Source = hdArt;
                        _spotlightFogRenderer.UpdateHdAlbumArt(hdArt);
                        _liveCodeRenderer.UpdateHdAlbumArt(hdArt);
                    }
                });
            }
        }, ct);
    }

    private static Path CreateMarqueeTextPath(
        string text,
        Typeface typeface,
        double fontSize,
        Brush strokeBrush,
        double strokeThickness)
    {
        if (string.IsNullOrEmpty(text)) text = " ";
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            1.0);

        Geometry geom = formatted.BuildGeometry(new Point(0, 0));
        geom.Freeze();

        var path = new Path
        {
            Data = geom,
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
            Fill = Brushes.Transparent,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetEdgeMode(path, EdgeMode.Unspecified);
        return path;
    }

    private static Size MeasureMarqueeText(string text, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return new Size(0, fontSize);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            1.0);
        return new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }

    public void UpdateTrackFromMainWindow(TrackInfo track)
    {
        OnTrackChanged(this, track);
    }

    private void OnTrackChanged(object? sender, TrackInfo track)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            _lyricsFetchCts?.Cancel();
            _lyricsFetchCts = new CancellationTokenSource();
            var ct = _lyricsFetchCts.Token;

            _kineticRenderEpoch++;
            _currentActiveIndex = -1;
            _lastVibeEffectivePos = TimeSpan.Zero;
            _currentKineticActiveIndex = -1;
            _lastKineticPosition = TimeSpan.Zero;
            _lastKineticClockMs = 0.0;
            _prebuiltNextPhrase = null;
            _prebuiltNextIndex = -1;
            _prebuiltEpoch = -1;

            _worldPhrases.Clear();
            KineticWorldCanvas.Children.Clear();
            _layoutDirector.Reset();
            _cameraController.Reset(new Point(0, 0));

            if (!track.IsValid)
            {
                HideIntro();
                _introTrackKey = string.Empty;
                ApplyTrackMetadata(track);
                _currentLyrics = LrcDocument.Empty();
                RebuildLyricsViews();
                SetInstrumentalMode(true);
                ApplyLayoutMode(_isKineticMode);
                CrossfadePalette(VibePalette.Default());
                return;
            }

            ApplyTrackMetadata(track);

            // The same track is announced several times as its details arrive; only a real change counts
            string trackKey = $"{track.Title}|{track.Artist}";
            if (trackKey != _introTrackKey)
            {
                HideIntro();
                _introTrackKey = trackKey;
                // Never let the previous song's words play on over the new one while it loads
                _currentLyrics = LrcDocument.Empty();
                RebuildLyricsViews();
                // With the album card on screen, wait to learn whether this track has lyrics at all
                if (_isKineticMode && LandscapeLayout.Visibility != Visibility.Visible) ShowIntro();
            }

            int currentId = Interlocked.Increment(ref _paletteUpdateId);

            if (track.Thumbnail != null)
            {
                var palette = ColorExtractor.ExtractPalette(track.Thumbnail);
                CrossfadePalette(palette);
            }
            else
            {
                CrossfadePalette(VibePalette.Default());
            }

            var doc = await _lyricsService.GetLyricsAsync(track, upgradedDoc =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (currentId == _paletteUpdateId && !ct.IsCancellationRequested)
                    {
                        _currentLyrics = upgradedDoc;
                        RebuildLyricsViews();
                        SetInstrumentalMode(!upgradedDoc.HasSyncedLyrics);
                        ApplyLayoutMode(_isKineticMode);
                    }
                });
            }, ct);
            if (ct.IsCancellationRequested) return;

            _currentLyrics = doc;
            RebuildLyricsViews();
            SetInstrumentalMode(!doc.HasSyncedLyrics);
            ApplyLayoutMode(_isKineticMode);
            if (_isKineticMode && doc.HasSyncedLyrics && _introShownKey != trackKey) ShowIntro();
        });
    }

    private void SetInstrumentalMode(bool isInstrumental)
    {
        if (_isInstrumentalMode == isInstrumental) return;
        _isInstrumentalMode = isInstrumental;

        var animDur = TimeSpan.FromMilliseconds(400);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (isInstrumental)
        {
            var fadeOutLyrics = new DoubleAnimation(0.0, animDur) { EasingFunction = ease };
            fadeOutLyrics.Completed += (s, e) => LyricsSideContainer.Visibility = Visibility.Collapsed;
            LyricsSideContainer.BeginAnimation(UIElement.OpacityProperty, fadeOutLyrics);

            LandscapeColLeft.Width = new GridLength(1.0, GridUnitType.Star);
            LandscapeColRight.Width = new GridLength(0.0, GridUnitType.Pixel);
            AlbumSideContainer.HorizontalAlignment = HorizontalAlignment.Center;
            AlbumSideContainer.Margin = new Thickness(0);

            TrackTitleText.TextAlignment = TextAlignment.Center;
            TrackArtistText.TextAlignment = TextAlignment.Center;
        }
        else
        {
            LyricsSideContainer.Visibility = Visibility.Visible;
            LyricsSideContainer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, animDur) { EasingFunction = ease });

            LandscapeColLeft.Width = new GridLength(0.44, GridUnitType.Star);
            LandscapeColRight.Width = new GridLength(0.56, GridUnitType.Star);
            AlbumSideContainer.HorizontalAlignment = HorizontalAlignment.Center;
            AlbumSideContainer.Margin = new Thickness(0, 0, 32, 0);

            TrackTitleText.TextAlignment = TextAlignment.Left;
            TrackArtistText.TextAlignment = TextAlignment.Left;
        }

        UpdateResponsiveLayout();
    }
}
