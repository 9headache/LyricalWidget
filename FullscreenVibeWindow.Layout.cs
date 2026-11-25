using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LyricalWidget;

// Responsive sizing and drag-resize handling
public partial class FullscreenVibeWindow
{
    private readonly DispatcherTimer _resizeSettleTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private bool _isResizing = false;
    private long _nextResizeFrame = 0;
    private double _builtLyricFontSize = 0.0;
    private double _builtLyricWrapWidth = 0.0;

    private static bool UsePortraitLayout(double width, double height)
    {
        return height > width || (width < 860.0 && height > 560.0);
    }

    private double GetLyricFontSize()
    {
        double vw = _isPortraitMode ? PortraitLyricsViewport.ActualWidth : LyricsViewport.ActualWidth;
        if (vw <= 50) vw = _isPortraitMode ? ActualWidth - 48 : ActualWidth * 0.52;
        return _isPortraitMode ? Math.Clamp(vw / 18.0, 17.0, 26.0) : Math.Clamp(vw / 19.5, 18.0, 34.0);
    }

    private double GetLyricWrapWidth()
    {
        double vw = _isPortraitMode ? PortraitLyricsViewport.ActualWidth : LyricsViewport.ActualWidth;
        // Viewport not sized yet: estimate from the window
        if (vw <= 50) vw = _isPortraitMode ? ActualWidth - 48 : ActualWidth * 0.52;
        // The active line is drawn slightly enlarged, so full-width lines need slack to stay inside
        return Math.Max(200.0, Math.Min(vw - 30.0, vw / 1.07));
    }

    private void OnTopChromeSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        double w = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : ActualWidth;
        double h = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : ActualHeight;
        if (w < 50 || h < 50) return;

        // Split: scale the fixed-size album blocks into the room they have
        bool albumOnly = LandscapeColRight.Width.Value == 0.0;
        double albumRoom = albumOnly ? w - 96.0 : ((w - 96.0) * 0.44) - 32.0;
        // Toolbar: beside the window buttons when it fits, wrapped underneath when it does not
        bool stacked = w < 1000.0;
        Grid.SetRow(TopModeBar, stacked ? 1 : 0);
        Grid.SetColumnSpan(TopModeBar, stacked ? 2 : 1);
        // Once wrapped it is tall enough to cover content, so everything starts below it
        double top = stacked ? TopChrome.ActualHeight + 18.0 : 0.0;
        LandscapeLayout.Margin = new Thickness(48, Math.Max(36.0, top), 48, 36);
        PortraitLayout.Margin = new Thickness(24, Math.Max(36.0, top), 24, 24);
        TopModeBar.Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(0, 0, 10, 0);

        SetScale(LandscapeAlbumScale, Math.Min(albumRoom / 400.0, (h - 48.0 - LandscapeLayout.Margin.Top) / 560.0));
        SetScale(PortraitAlbumScale, Math.Min((w - 48.0) / 320.0, ((h - 24.0 - PortraitLayout.Margin.Top) * 0.38) / 325.0));

        // Kinetic
        UpdateMoonlightMargin(w, h);
        if (!_isResizing || _isIntroVisible)
        {
            IntroHeroTitle.FontSize = Math.Clamp(w / 22.0, 22.0, 56.0);
            IntroHeroTitle.MaxWidth = Math.Min(920.0, w * 0.88);
            IntroHeroArtist.MaxWidth = w * 0.7;
        }

        // Short windows: beside the track badge rather than up over the lyrics
        bool besideBadge = h < 600.0 && w >= 700.0;
        bool compactPill = w < 640.0 || besideBadge;
        PacingSubtext.Visibility = compactPill ? Visibility.Collapsed : Visibility.Visible;
        PacingPill.HorizontalAlignment = besideBadge ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        PacingPill.Margin = besideBadge ? new Thickness(0, 0, 24, 38) : new Thickness(12, 0, 12, w < 1240.0 ? 116 : 34);

        if (!_isResizing || _isAmbient) UpdateAmbientLayout(w, h);
        CloseHoverMenu();
    }

    private static void SetScale(ScaleTransform transform, double scale)
    {
        scale = Math.Clamp(scale, 0.35, 1.0);
        transform.ScaleX = scale;
        transform.ScaleY = scale;
    }

    private void BeginResize()
    {
        _resizeSettleTimer.Stop();
        _resizeSettleTimer.Start();
        if (_isResizing) return;

        _isResizing = true;
        _moonlightRenderer.HoldLayout = true;
        SetLyricGlows(false);
        RenderOptions.SetBitmapScalingMode(AlbumCoverImage, BitmapScalingMode.LowQuality);
        RenderOptions.SetBitmapScalingMode(PortraitAlbumCoverImage, BitmapScalingMode.LowQuality);
    }

    /// <summary>Every lyric word has its own glow shader, drawn even when fully faded: by far the largest cost of a resize frame.</summary>
    private void SetLyricGlows(bool enabled)
    {
        foreach (var line in _vibeVisuals)
        {
            if (line.Words == null) continue;
            foreach (var word in line.Words) word.Element.Effect = enabled ? word.GlowEffect : null;
        }
    }

    /// <summary>Leaves the UI thread room to follow the mouse: 60 FPS is plenty while the window is changing shape.</summary>
    private bool SkipFrameWhileResizing(long nowTicks)
    {
        if (!_isResizing) return false;
        if (nowTicks < _nextResizeFrame) return true;
        _nextResizeFrame = nowTicks + (System.Diagnostics.Stopwatch.Frequency / 62);
        return false;
    }

    /// <summary>Rebuilding lyrics is too heavy for every pixel of a drag, so it waits for the size to settle.</summary>
    private void OnResizeSettled()
    {
        _resizeSettleTimer.Stop();
        if (_isClosing) return;

        _isResizing = false;
        _moonlightRenderer.HoldLayout = false;
        SetLyricGlows(true);
        RenderOptions.SetBitmapScalingMode(AlbumCoverImage, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(PortraitAlbumCoverImage, BitmapScalingMode.HighQuality);
        UpdateResponsiveLayout();
        // The other renderers are sized when they are activated
        if (_isKineticMode && _currentKineticPreset == KineticPreset.SpotlightFog) _spotlightFogRenderer.OnResize(ActualWidth, ActualHeight);
        if (_isKineticMode && _currentKineticPreset == KineticPreset.LiveCode) _liveCodeRenderer.OnResize(ActualWidth, ActualHeight);

        ComputeStaticLineLayouts();
        if (Math.Abs(GetLyricFontSize() - _builtLyricFontSize) > 0.4 || Math.Abs(GetLyricWrapWidth() - _builtLyricWrapWidth) > 2.0)
        {
            RebuildLyricsViews();
        }
        // Editorial and Poster phrases are laid out for one viewport size
        if (_isKineticMode && _currentLyrics.HasSyncedLyrics &&
            _currentKineticPreset is KineticPreset.Editorial or KineticPreset.Poster)
        {
            SetActiveKineticPreset(_currentKineticPreset);
        }
    }
}
