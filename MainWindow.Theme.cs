using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LyricalWidget.Audio;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;
using LyricalWidget.Rendering;
using LyricalWidget.Services;

namespace LyricalWidget;

public partial class MainWindow : Window
{
    private bool _activeBackdropIsA = true;
    private Color _currentAccentColor = Color.FromRgb(0x60, 0xA5, 0xFA);
    private double _currentBassEnergy = 0.0;

    private ThemedLyricColors _currentThemedLyricColors = new()
    {
        ActiveColor = Colors.White,
        InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
        AccentGlowColor = Color.FromRgb(0x60, 0xA5, 0xFA)
    };
    private VibePalette _currentPalette = VibePalette.Default();


    public void SetDisplayMode(string mode)
    {
        _settings.DisplayMode = mode;
        _trayService?.UpdateDisplayModeState(mode);
        ApplyDisplayModeAndTheme();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetBackgroundStyle(string style)
    {
        _settings.BackgroundStyle = style;
        _trayService?.UpdateBackgroundStyleState(style);
        ApplyDisplayModeAndTheme();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetLyricAlignment(string align)
    {
        _settings.LyricAlignment = align;
        ApplyDisplayModeAndTheme();
        RebuildLyricsViews();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetVisibleLinesMode(string linesMode)
    {
        _settings.VisibleLinesMode = linesMode;
        UpdateToolbarButtons();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetAnimationStyle(string style)
    {
        _settings.AnimationStyle = style;
        _trayService?.UpdateAnimationStyleState(style);
        UpdateToolbarButtons();
        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetAnimationIntensity(string intensity) => SetAnimationStyle(intensity);

    private void ApplyDisplayModeAndTheme()
    {
        string mode = _settings.DisplayMode;
        string bg = _settings.BackgroundStyle;
        bool hasLyrics = _currentLyrics.HasSyncedLyrics;

        UpdateToolbarButtons();

        if (_isInIdleClockMode)
        {
            ApplyIdleClockStyling();
            return;
        }

        // Background Style: Glassify vs Transparent
        if (bg == "Glass")
        {
            CardBorder.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x0A, 0x0C, 0x12));
            GlassBackgroundContainer.Visibility = Visibility.Visible;
            CardBorder.BorderThickness = new Thickness(1);
            CardBorder.BorderBrush = BuildSpecularBrush(_currentAccentColor);
        }
        else // Transparent (No Background)
        {
            CardBorder.Background = Brushes.Transparent;
            GlassBackgroundContainer.Visibility = Visibility.Collapsed;

            if (_isLocked)
            {
                CardBorder.BorderThickness = new Thickness(0);
                CardBorder.BorderBrush = Brushes.Transparent;
            }
            else
            {
                CardBorder.BorderThickness = new Thickness(1.5);
                CardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x38, 0xBD, 0xF8));
            }
        }

        // Display Mode: Fall back to Album Art Only when no synced lyrics available
        if (!hasLyrics || mode == "AlbumOnly")
        {
            LyricsOnlyView.Visibility = Visibility.Collapsed;
            SideBySideView.Visibility = Visibility.Collapsed;
            AlbumOnlyView.Visibility = Visibility.Visible;
            AlignTogglePanel.Visibility = Visibility.Collapsed;
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
        }
        else if (mode == "LyricsOnly")
        {
            LyricsOnlyView.Visibility = Visibility.Visible;
            SideBySideView.Visibility = Visibility.Collapsed;
            AlbumOnlyView.Visibility = Visibility.Collapsed;
            AlignTogglePanel.Visibility = Visibility.Visible;
        }
        else // AlbumAndLyrics (Side-by-Side)
        {
            LyricsOnlyView.Visibility = Visibility.Collapsed;
            SideBySideView.Visibility = Visibility.Visible;
            AlbumOnlyView.Visibility = Visibility.Collapsed;
            AlignTogglePanel.Visibility = Visibility.Collapsed;
            TrackSwitchBanner.Visibility = Visibility.Collapsed;
        }
    }

    private static LinearGradientBrush BuildSpecularBrush(Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x35, accent.R, accent.G, accent.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    private void UpdateToolbarButtons()
    {
        var activeBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x38, 0xBD, 0xF8));
        var defaultBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        var activeBorder = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
        var defaultBorder = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));

        BtnModeLyricsOnly.Background = _settings.DisplayMode == "LyricsOnly" ? activeBrush : defaultBrush;
        BtnModeLyricsOnly.BorderBrush = _settings.DisplayMode == "LyricsOnly" ? activeBorder : defaultBorder;

        BtnModeSideBySide.Background = _settings.DisplayMode == "AlbumAndLyrics" ? activeBrush : defaultBrush;
        BtnModeSideBySide.BorderBrush = _settings.DisplayMode == "AlbumAndLyrics" ? activeBorder : defaultBorder;

        BtnModeAlbumOnly.Background = _settings.DisplayMode == "AlbumOnly" ? activeBrush : defaultBrush;
        BtnModeAlbumOnly.BorderBrush = _settings.DisplayMode == "AlbumOnly" ? activeBorder : defaultBorder;

        BtnBgTransparent.Background = _settings.BackgroundStyle == "Transparent" ? activeBrush : defaultBrush;
        BtnBgTransparent.BorderBrush = _settings.BackgroundStyle == "Transparent" ? activeBorder : defaultBorder;

        BtnBgGlass.Background = _settings.BackgroundStyle == "Glass" ? activeBrush : defaultBrush;
        BtnBgGlass.BorderBrush = _settings.BackgroundStyle == "Glass" ? activeBorder : defaultBorder;

        BtnLinesAuto.Background = _settings.VisibleLinesMode == "Auto" ? activeBrush : defaultBrush;
        BtnLinesAuto.BorderBrush = _settings.VisibleLinesMode == "Auto" ? activeBorder : defaultBorder;

        BtnLines2.Background = _settings.VisibleLinesMode == "2" ? activeBrush : defaultBrush;
        BtnLines2.BorderBrush = _settings.VisibleLinesMode == "2" ? activeBorder : defaultBorder;

        BtnLines3.Background = _settings.VisibleLinesMode == "3" ? activeBrush : defaultBrush;
        BtnLines3.BorderBrush = _settings.VisibleLinesMode == "3" ? activeBorder : defaultBorder;

        BtnLines5.Background = _settings.VisibleLinesMode == "5" ? activeBrush : defaultBrush;
        BtnLines5.BorderBrush = _settings.VisibleLinesMode == "5" ? activeBorder : defaultBorder;

        BtnMotionFluid.Background = activeBrush;
        BtnMotionFluid.BorderBrush = activeBorder;

        ChkThemeLyrics.IsChecked = _settings.ThemeColoredLyrics;

        BtnAlignCenter.Background = _settings.LyricAlignment == "Center" ? activeBrush : defaultBrush;
        BtnAlignCenter.BorderBrush = _settings.LyricAlignment == "Center" ? activeBorder : defaultBorder;

        BtnAlignLeft.Background = _settings.LyricAlignment == "Left" ? activeBrush : defaultBrush;
        BtnAlignLeft.BorderBrush = _settings.LyricAlignment == "Left" ? activeBorder : defaultBorder;
    }

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

    // Lyrics views and static layout calculation

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
            _currentLyrics = LrcDocument.Empty();
            RebuildLyricsViews();

            if (!track.IsValid)
            {
                ApplyInvalidTrackState(track);
                return;
            }

            if (track.IsDjOrInterstitial)
            {
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
            TriggerTrackSwitchBanner(track);

            if (!IsFullscreenActive)
            {
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

            await _mediaService.ForceReconnectAsync();

            var current = _mediaService.CurrentTrack;
            if (current != null)
            {
                OnTrackChanged(this, current);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ForceReconnect error: {ex.Message}");
        }
    }

    private static async Task<BitmapSource?> BakeAmbientBackdropAsync(BitmapSource? source)
    {
        if (source == null) return null;

        return await Task.Run(() =>
        {
            try
            {
                const int w = 48;
                const int h = 48;

                double scaleX = (double)w / source.PixelWidth;
                double scaleY = (double)h / source.PixelHeight;

                var transformed = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
                var converted = new FormatConvertedBitmap(transformed, PixelFormats.Bgra32, null, 0);

                int stride = w * 4;
                byte[] srcPixels = new byte[h * stride];
                converted.CopyPixels(srcPixels, stride, 0);

                byte[] buffer1 = (byte[])srcPixels.Clone();
                byte[] buffer2 = new byte[srcPixels.Length];

                const int radius = 6;
                BoxBlur(buffer1, buffer2, w, h, radius);
                BoxBlur(buffer2, buffer1, w, h, radius);
                BoxBlur(buffer1, buffer2, w, h, radius);

                for (int i = 0; i < buffer2.Length; i += 4)
                {
                    double b = buffer2[i];
                    double g = buffer2[i + 1];
                    double r = buffer2[i + 2];

                    double lum = (0.299 * r) + (0.587 * g) + (0.114 * b);

                    buffer2[i] = (byte)Math.Clamp(lum + ((b - lum) * 1.25), 0, 255);
                    buffer2[i + 1] = (byte)Math.Clamp(lum + ((g - lum) * 1.25), 0, 255);
                    buffer2[i + 2] = (byte)Math.Clamp(lum + ((r - lum) * 1.25), 0, 255);
                }

                var result = BitmapSource.Create(
                    w, h, 96, 96, PixelFormats.Bgra32, null, buffer2, stride);
                result.Freeze();
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to bake ambient backdrop: {ex.Message}");
                return null;
            }
        });
    }

    private static void BoxBlur(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int rSum = 0, gSum = 0, bSum = 0, count = 0;
                for (int k = -r; k <= r; k++)
                {
                    int px = Math.Clamp(x + k, 0, w - 1);
                    int idx = rowStart + (px * 4);
                    bSum += src[idx];
                    gSum += src[idx + 1];
                    rSum += src[idx + 2];
                    count++;
                }

                int outIdx = rowStart + (x * 4);
                dst[outIdx] = (byte)(bSum / count);
                dst[outIdx + 1] = (byte)(gSum / count);
                dst[outIdx + 2] = (byte)(rSum / count);
                dst[outIdx + 3] = 255;
            }
        }
    }

    private void CrossfadeBackdrop(BitmapSource newBackdrop)
    {
        // Snappy 250ms crossfade
        var fadeIn = new DoubleAnimation(0.0, 0.32, TimeSpan.FromMilliseconds(250));
        var fadeOut = new DoubleAnimation(0.32, 0.0, TimeSpan.FromMilliseconds(250));

        if (_activeBackdropIsA)
        {
            BackdropImageB.Source = newBackdrop;
            BackdropImageB.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            BackdropImageA.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _activeBackdropIsA = false;
        }
        else
        {
            BackdropImageA.Source = newBackdrop;
            BackdropImageA.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            BackdropImageB.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _activeBackdropIsA = true;
        }
    }

    private void UpdateAccentColors(Color accent)
    {
        _currentAccentColor = accent;

        byte r2 = (byte)Math.Min(255, accent.R + 45);
        byte g2 = (byte)Math.Min(255, accent.G + 35);
        byte b2 = (byte)Math.Min(255, accent.B + 55);

        var progressBrush = new LinearGradientBrush(accent, Color.FromRgb(r2, g2, b2), new Point(0, 0), new Point(1, 0));
        progressBrush.Freeze();

        SideProgressBarBorder.Background = progressBrush;
        AlbumOnlyProgressBarBorder.Background = progressBrush;

        SideAlbumAuraGlow.Color = accent;
        AlbumOnlyShadow.Color = accent;

        if (_settings.BackgroundStyle == "Glass" && !_isInIdleClockMode)
        {
            CardBorder.BorderBrush = BuildSpecularBrush(accent);
        }

        UpdateVisualsAccentColor(_lyricsOnlyVisuals, _lyricsOnlyInterludeSlot, accent);
        UpdateVisualsAccentColor(_sideLyricsVisuals, _sideLyricsInterludeSlot, accent);
    }

    private static void UpdateVisualsAccentColor(List<LyricLineVisualItem> visuals, InterludeSlotItem? slot, Color accent)
    {
        foreach (var line in visuals)
        {
            if (line.Words != null)
            {
                foreach (var w in line.Words)
                {
                    w.GlowEffect.Color = accent;
                }
            }
        }
        if (slot != null)
        {
            if (slot.Dot1.Effect is DropShadowEffect d1) d1.Color = accent;
            if (slot.Dot2.Effect is DropShadowEffect d2) d2.Color = accent;
            if (slot.Dot3.Effect is DropShadowEffect d3) d3.Color = accent;
        }
    }

    public void SetThemeColoredLyrics(bool enabled)
    {
        _settings.ThemeColoredLyrics = enabled;
        ChkThemeLyrics.IsChecked = enabled;
        _trayService?.UpdateThemeColoredLyricsState(enabled);
        SettingsService.Save(_settings);
        ApplyThemedLyricColors(_currentPalette, crossfade: false);
        if (IsFullscreenActive && _vibeWindow != null)
        {
            _vibeWindow.SetThemeColoredLyrics(enabled);
        }
    }

    private ThemedLyricColors ComputeCurrentLyricColors(VibePalette palette, bool enabled)
    {
        if (enabled)
        {
            return ColorExtractor.ComputeThemedLyricColors(palette, true);
        }

        return new ThemedLyricColors
        {
            ActiveColor = Colors.White,
            InactiveColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), // #66FFFFFF when OFF
            AccentGlowColor = Color.FromRgb(0x60, 0xA5, 0xFA)
        };
    }

    private void ApplyThemedLyricColors(VibePalette palette, bool crossfade)
    {
        bool isGaming = _settings.GamingMode;
        _currentThemedLyricColors = ComputeCurrentLyricColors(palette, _settings.ThemeColoredLyrics);
        ApplyLyricColorsToVisuals(_lyricsOnlyVisuals, _currentThemedLyricColors, crossfade && !isGaming, isGaming);
        ApplyLyricColorsToVisuals(_sideLyricsVisuals, _currentThemedLyricColors, crossfade && !isGaming, isGaming);
    }

    private static void ApplyLyricColorsToVisuals(List<LyricLineVisualItem> visuals, ThemedLyricColors colors, bool crossfade, bool isGamingMode = false)
    {
        FluidLyricWaveHelper.ApplyLyricColorsToVisuals(visuals, colors, crossfade, isGamingMode);
    }


    public void SetAudioReactiveGlow(bool enabled)
    {
        _settings.AudioReactiveGlow = enabled;
        _trayService?.UpdateAudioReactiveGlowState(enabled);
        if (ChkAudioReactive.IsChecked != enabled)
        {
            ChkAudioReactive.IsChecked = enabled;
        }
        SettingsService.Save(_settings);
        _audioBeatService.UpdateState(_mediaService.IsPlaying, _settings.GamingMode, enabled);
    }

    private void OnBassEnergyChanged(double bass)
    {
        _currentBassEnergy = bass;
    }


    public void ApplyGamingMode(bool enabled)
    {
        Background = Brushes.Transparent;
        _settings.GamingMode = enabled;
        _trayService?.UpdateGamingModeState(enabled);
        if (ChkGamingMode.IsChecked != enabled)
        {
            ChkGamingMode.IsChecked = enabled;
        }
        _audioBeatService.UpdateState(_mediaService.IsPlaying, enabled, _settings.AudioReactiveGlow);

        try
        {
            RenderOptions.ProcessRenderMode = enabled ? RenderMode.SoftwareOnly : RenderMode.Default;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set ProcessRenderMode: {ex.Message}");
        }

        // Toggle expensive live pixel shaders (drop shadows, aura glows)
        double cardShadowOpacity = enabled ? 0.0 : 0.5;
        double auraShadowOpacity = enabled ? 0.0 : 0.55;

        CardDropShadow.Opacity = cardShadowOpacity;
        SideAlbumAuraGlow.Opacity = auraShadowOpacity;
        AlbumOnlyShadow.Opacity = auraShadowOpacity;

        // In Gaming Mode, clear word drop shadow opacities so software rasterizer doesn't emulate shaders
        if (enabled)
        {
            foreach (var item in _lyricsOnlyVisuals)
            {
                if (item.Words != null)
                {
                    foreach (var w in item.Words)
                    {
                        w.GlowEffect.Opacity = 0.0;
                        w.CurrentGlowOpacity = 0.0;
                    }
                }
            }
            foreach (var item in _sideLyricsVisuals)
            {
                if (item.Words != null)
                {
                    foreach (var w in item.Words)
                    {
                        w.GlowEffect.Opacity = 0.0;
                        w.CurrentGlowOpacity = 0.0;
                    }
                }
            }
        }

        EnsureRenderingSubscribed();
        SaveCurrentBounds();
    }

    public void SetFpsCap(int fps)
    {
        _settings.FpsCap = fps <= 30 ? 30 : 60;
        _trayService?.UpdateFpsCapState(_settings.FpsCap);
        ChkEcoFps.IsChecked = (_settings.FpsCap <= 30);
        SaveCurrentBounds();
    }

    // Window drag, resize, and lifecycle
}
