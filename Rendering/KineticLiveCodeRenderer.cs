using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;

namespace LyricalWidget.Rendering;

/// <summary>
/// Renders the LiveCode fullscreen kinetic typography preset:
/// a multiplayer Live-Share IDE visualizer with absolute virtual canvas coordinates,
/// native high-res typography, Tokyo-Night synthwave palette, colored token highlight blocks,
/// active-line glow strip, and coupled background parallax.
/// </summary>
public sealed partial class KineticLiveCodeRenderer
{
    private static readonly FontFamily MonospaceFont = new("JetBrains Mono, Cascadia Mono, Consolas, Nirmala UI, Segoe UI, monospace");
    private static readonly Typeface MonospaceTypeface = new(MonospaceFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly SpringDamper _cameraXSpring = new(3.4, 0.76);
    private readonly SpringDamper _cameraYSpring = new(3.2, 0.78);
    private readonly SpringDamper _cameraZoomSpring = new(2.2, 0.85);
    private readonly SpringDamper _caretXSpring = new(5.5, 0.82);

    private Grid? _artworkGroup;
    private Image? _backdropImage;
    private ScaleTransform? _backdropScale;
    private TranslateTransform? _backdropParallax;

    private Canvas? _viewportCanvas;
    private Canvas? _editorCanvas;
    private ScaleTransform? _cameraScale;
    private TranslateTransform? _cameraTranslate;
    private BlurEffect? _editorBlur;

    private Canvas? _highlightsCanvas;
    private Canvas? _linesCanvas;
    private Canvas? _caretCanvas;
    private Rectangle? _caretBlock;
    private Border? _artistPill;
    private TextBlock? _artistText;
    private Canvas? _doodleCanvas;

    private Border? _findPill;
    private TextBlock? _findText;

    private Border? _activeLineBand;
    private Rectangle? _activeLineGutterBar;
    private Rectangle? _indentGuideCol0;
    private Rectangle? _indentGuideCol4;

    private List<FormattedCodeLine> _formattedLines = new();
    private readonly List<CodeLineRowVisual> _renderedRows = new();
    private TrackInfo? _currentTrack;
    private BitmapSource? _cachedHdArt;

    private Color _accentColor = Color.FromRgb(0xFF, 0x4D, 0x6D);
    private Color _functionColor = Color.FromRgb(0x38, 0xBD, 0xF8);
    private Color _keywordColor = Color.FromRgb(0xC0, 0x84, 0xFC);
    private Color _stringColor = Color.FromRgb(0x34, 0xD3, 0x99);
    private Color _operatorColor = Color.FromRgb(0xFB, 0x71, 0x85);
    private Color _numberColor = Color.FromRgb(0xFB, 0xBF, 0x24);
    private Color _identifierColor = Color.FromRgb(0xF4, 0xF4, 0xF8);
    private Color _commentColor = Color.FromRgb(0x64, 0x74, 0x8B);
    private Color _punctuationColor = Color.FromRgb(0x94, 0xA3, 0xB8);

    private double _camXPos = 0.0;
    private double _camXVel = 0.0;
    private double _camYPos = 0.0;
    private double _camYVel = 0.0;
    private double _camZoomPos = 1.0;
    private double _camZoomVel = 0.0;

    private double _caretCurrentX = 100.0;
    private double _caretCurrentXVel = 0.0;

    private double _lastPositionMs = 0.0;
    private int _lastActiveLineIndex = -1;
    private double _lastWhipPanTime = 0.0;

    private double _viewportWidth = 1920.0;
    private double _viewportHeight = 1080.0;
    private string _artistTag = "live";

    public void Initialize(
        Grid artworkGroup,
        Image backdropImage,
        ScaleTransform backdropScale,
        TranslateTransform backdropParallax,
        Canvas viewportCanvas,
        Canvas editorCanvas,
        ScaleTransform cameraScale,
        TranslateTransform cameraTranslate,
        Canvas highlightsCanvas,
        Canvas linesCanvas,
        Canvas caretCanvas,
        Rectangle caretBlock,
        Border artistPill,
        TextBlock artistText,
        Canvas doodleCanvas,
        Border findPill,
        TextBlock findText)
    {
        _artworkGroup = artworkGroup;
        _backdropImage = backdropImage;
        _backdropScale = backdropScale;
        _backdropParallax = backdropParallax;

        _viewportCanvas = viewportCanvas;
        _editorCanvas = editorCanvas;
        _cameraScale = cameraScale;
        _cameraTranslate = cameraTranslate;

        _editorBlur = new BlurEffect { Radius = 0.0, RenderingBias = RenderingBias.Performance };
        _editorCanvas.Effect = _editorBlur;

        _highlightsCanvas = highlightsCanvas;
        _linesCanvas = linesCanvas;
        _caretCanvas = caretCanvas;
        _caretBlock = caretBlock;
        _artistPill = artistPill;
        _artistText = artistText;
        _doodleCanvas = doodleCanvas;

        _findPill = findPill;
        _findText = findText;

        RenderOptions.SetBitmapScalingMode(_backdropImage, BitmapScalingMode.HighQuality);

        BuildAmbientHighlightElements();
    }

    private void BuildAmbientHighlightElements()
    {
        if (_highlightsCanvas == null) return;
        _highlightsCanvas.Children.Clear();

        _indentGuideCol0 = new Rectangle
        {
            Width = 1.0,
            Height = 80000,
            Fill = new SolidColorBrush(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_indentGuideCol0, 120);
        Canvas.SetTop(_indentGuideCol0, 0);
        _highlightsCanvas.Children.Add(_indentGuideCol0);

        _indentGuideCol4 = new Rectangle
        {
            Width = 1.0,
            Height = 80000,
            Fill = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_indentGuideCol4, 200);
        Canvas.SetTop(_indentGuideCol4, 0);
        _highlightsCanvas.Children.Add(_indentGuideCol4);

        _activeLineBand = new Border
        {
            Height = 60,
            Width = 4000,
            Background = new SolidColorBrush(Color.FromArgb(0x18, _accentColor.R, _accentColor.G, _accentColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, _accentColor.R, _accentColor.G, _accentColor.B)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetLeft(_activeLineBand, 0);
        _highlightsCanvas.Children.Add(_activeLineBand);

        _activeLineGutterBar = new Rectangle
        {
            Width = 3.5,
            Height = 44,
            RadiusX = 1.75,
            RadiusY = 1.75,
            Fill = new SolidColorBrush(_accentColor),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = _accentColor,
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.85
            },
            Visibility = Visibility.Collapsed
        };
        _highlightsCanvas.Children.Add(_activeLineGutterBar);
    }

    public void Deactivate()
    {
        _lastActiveLineIndex = -1;
        _lastPositionMs = 0.0;
        _camXVel = 0.0;
        _camYVel = 0.0;
        _camZoomVel = 0.0;
        _caretCurrentXVel = 0.0;

        if (_caretCanvas != null) _caretCanvas.Visibility = Visibility.Collapsed;
        if (_doodleCanvas != null) _doodleCanvas.Children.Clear();
        if (_editorBlur != null) _editorBlur.Radius = 0.0;
        if (_findPill != null) _findPill.Opacity = 0.0;
        if (_activeLineBand != null) _activeLineBand.Visibility = Visibility.Collapsed;
        if (_activeLineGutterBar != null) _activeLineGutterBar.Visibility = Visibility.Collapsed;
    }

    public void Activate(double width, double height)
    {
        if (width > 0 && height > 0)
        {
            _viewportWidth = width;
            _viewportHeight = height;
        }

        OnResize(_viewportWidth, _viewportHeight);
        Reset();

        EnsureLinesPopulated();
    }

    public void OnResize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _viewportWidth = width;
        _viewportHeight = height;

        UpdateTypographyDimensions();
    }

    public void UpdateTrack(TrackInfo? track)
    {
        _currentTrack = track;
        _artistTag = ExtractArtistTag(track?.Artist);
        if (_artistText != null)
        {
            _artistText.Text = _artistTag;
        }

        if (_findText != null)
        {
            string title = track?.Title ?? "Track";
            string artist = track?.Artist ?? "Artist";
            string album = track?.Album ?? "Album";
            _findText.Text = $"Find: {title}   {_formattedLines.Count} lines   • blame: {artist} · {album}";
        }
    }

    public void UpdateLyrics(LrcDocument? lyrics)
    {
        _formattedLines.Clear();
        _renderedRows.Clear();
        if (_linesCanvas != null) _linesCanvas.Children.Clear();
        if (_doodleCanvas != null) _doodleCanvas.Children.Clear();

        if (lyrics != null && lyrics.Lines != null && lyrics.Lines.Count > 0)
        {
            _formattedLines = CodeLyricFormatter.FormatLyrics(lyrics.Lines, _currentTrack);
        }

        RebuildVisualRows();
        Reset();
    }

    public void UpdateHdAlbumArt(BitmapSource hdArt)
    {
        _cachedHdArt = hdArt;
        if (_backdropImage != null)
        {
            _backdropImage.Source = _cachedHdArt;
        }
        if (_artworkGroup != null)
        {
            _artworkGroup.Opacity = 1.0;
        }
    }

    public void UpdateAlbumArt(BitmapSource? art, VibePalette palette)
    {
        if (art != null && _cachedHdArt == null)
        {
            _cachedHdArt = art;
            if (_backdropImage != null) _backdropImage.Source = _cachedHdArt;
            if (_artworkGroup != null) _artworkGroup.Opacity = 1.0;
        }

        UpdatePalette(palette);
    }

    public void UpdatePalette(VibePalette palette)
    {
        // Keep accent saturated enough to pop against dark backgrounds
        Color candidate = palette.Accent.A > 0 ? palette.Accent : palette.Primary;
        _accentColor = EnsureVibrantAccent(candidate, _currentTrack?.Title ?? _currentTrack?.Artist);
        _functionColor = palette.Highlight.A > 0 ? palette.Highlight : Color.FromRgb(0x38, 0xBD, 0xF8);

        if (_artistPill != null)
        {
            _artistPill.Background = new SolidColorBrush(_accentColor);
        }
        if (_caretBlock != null)
        {
            _caretBlock.Fill = new SolidColorBrush(_functionColor);
        }

        if (_activeLineBand != null)
        {
            _activeLineBand.Background = new SolidColorBrush(Color.FromArgb(0x18, _accentColor.R, _accentColor.G, _accentColor.B));
            _activeLineBand.BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, _accentColor.R, _accentColor.G, _accentColor.B));
        }
        if (_activeLineGutterBar != null)
        {
            _activeLineGutterBar.Fill = new SolidColorBrush(_accentColor);
            if (_activeLineGutterBar.Effect is DropShadowEffect shadow)
            {
                shadow.Color = _accentColor;
            }
        }
    }

    public void Reset()
    {
        _lastActiveLineIndex = -1;
        _lastPositionMs = 0.0;
        _lastWhipPanTime = 0.0;
        _camXPos = 0.0;
        _camXVel = 0.0;
        _camYPos = 0.0;
        _camYVel = 0.0;
        _camZoomPos = 1.0;
        _camZoomVel = 0.0;
        _caretCurrentX = 100.0;
        _caretCurrentXVel = 0.0;

        if (_editorBlur != null) _editorBlur.Radius = 0.0;
        if (_findPill != null) _findPill.Opacity = 0.0;
        if (_activeLineBand != null) _activeLineBand.Visibility = Visibility.Collapsed;
        if (_activeLineGutterBar != null) _activeLineGutterBar.Visibility = Visibility.Collapsed;
    }

    public void UpdateFrame(
        double dt,
        TimeSpan effectivePos,
        double currentTimeSec,
        double bassEnergy,
        double beatImpulse,
        bool isPlaying,
        bool isInstrumental)
    {
        if (dt <= 0.0) return;

        double baseFontSize = Math.Clamp(_viewportHeight * 0.046, 30.0, 56.0);
        double lineHeight = Math.Max(56.0, baseFontSize * 1.85);
        double gutterWidth = Math.Max(76.0, baseFontSize * 1.70);

        int activeLineIdx = FindActiveLineIndex(effectivePos);
        bool hasLyrics = isPlaying && !isInstrumental && activeLineIdx >= 0 && activeLineIdx < _formattedLines.Count;

        bool inInstrumentalGap = false;
        if (activeLineIdx >= 0 && activeLineIdx < _formattedLines.Count)
        {
            var curLine = _formattedLines[activeLineIdx];
            if (activeLineIdx + 1 < _formattedLines.Count)
            {
                var nextLine = _formattedLines[activeLineIdx + 1];
                if (effectivePos > curLine.EndTime && (nextLine.Timestamp - curLine.EndTime).TotalSeconds > 3.2)
                {
                    inInstrumentalGap = true;
                }
            }
            else if (effectivePos > curLine.EndTime + TimeSpan.FromSeconds(2.0))
            {
                inInstrumentalGap = true;
            }
        }
        else
        {
            inInstrumentalGap = true;
        }

        double curClockMs = effectivePos.TotalMilliseconds;
        bool isSeek = _lastPositionMs > 0 && Math.Abs(curClockMs - _lastPositionMs) > 300.0;
        _lastPositionMs = curClockMs;

        if (activeLineIdx != _lastActiveLineIndex && activeLineIdx >= 0)
        {
            _lastActiveLineIndex = activeLineIdx;
            _lastWhipPanTime = currentTimeSec;
        }

        Point caretPoint;
        UpdateRollingLines(activeLineIdx, effectivePos, dt, baseFontSize, lineHeight, gutterWidth, out caretPoint);

        UpdateActiveLineHighlights(activeLineIdx, lineHeight, gutterWidth);

        double targetZoom;
        double targetCamX;
        double targetCamY;

        if (!inInstrumentalGap && hasLyrics)
        {
            targetZoom = Math.Clamp(_viewportHeight / 980.0, 1.00, 1.12);

            double anchorCamX = _viewportWidth * 0.18;
            double followThresholdX = _viewportWidth * 0.55;
            double caretScreenX = anchorCamX + (caretPoint.X * targetZoom);

            if (caretScreenX <= followThresholdX)
            {
                targetCamX = anchorCamX;
            }
            else
            {
                targetCamX = followThresholdX - (caretPoint.X * targetZoom);
            }

            double activeLineCenterY = (activeLineIdx * lineHeight) + (lineHeight * 0.5);
            targetCamY = (_viewportHeight * 0.52) - (activeLineCenterY * targetZoom);

            if (_findPill != null)
            {
                _findPill.Opacity = Math.Max(0.0, _findPill.Opacity - (dt * 3.5));
            }
        }
        else
        {
            // Zoom out during gaps to reveal overview
            targetZoom = Math.Clamp(_viewportHeight / 1500.0, 0.62, 0.72);
            double totalHeight = Math.Max(200.0, _renderedRows.Count * lineHeight);
            int focusLine = activeLineIdx >= 0 ? activeLineIdx : 0;
            double focusY = (focusLine * lineHeight) + (lineHeight * 0.5);

            targetCamX = (_viewportWidth * 0.50) - (420.0 * targetZoom);
            targetCamY = (_viewportHeight * 0.50) - (Math.Min(totalHeight * 0.5, focusY) * targetZoom);

            if (_findPill != null)
            {
                _findPill.Opacity = Math.Min(1.0, _findPill.Opacity + (dt * 2.5));
            }
        }

        if (isSeek)
        {
            _camXPos = targetCamX; _camXVel = 0.0;
            _camYPos = targetCamY; _camYVel = 0.0;
            _camZoomPos = targetZoom; _camZoomVel = 0.0;
        }
        else
        {
            _cameraXSpring.Step(ref _camXPos, ref _camXVel, targetCamX, dt);
            _cameraYSpring.Step(ref _camYPos, ref _camYVel, targetCamY, dt);
            _cameraZoomSpring.Step(ref _camZoomPos, ref _camZoomVel, targetZoom, dt);
        }

        if (_cameraScale != null)
        {
            _cameraScale.ScaleX = _camZoomPos;
            _cameraScale.ScaleY = _camZoomPos;
        }
        if (_cameraTranslate != null)
        {
            _cameraTranslate.X = _camXPos;
            _cameraTranslate.Y = _camYPos;
        }

        if (_backdropScale != null)
        {
            double slowBreathe = 1.0 + (0.015 * Math.Sin(currentTimeSec * 0.30));
            double bgScale = 1.25 * (1.0 + (_camZoomPos - 1.0) * 0.30) * slowBreathe;
            _backdropScale.ScaleX = bgScale;
            _backdropScale.ScaleY = bgScale;
        }

        if (_backdropParallax != null)
        {
            _backdropParallax.X = -_camXPos * 0.28;
            _backdropParallax.Y = -_camYPos * 0.28;
        }

        double whipPanElapsed = currentTimeSec - _lastWhipPanTime;
        if (_editorBlur != null)
        {
            double panSpeed = Math.Abs(_camXVel);
            if (whipPanElapsed < 0.14 && panSpeed > 240.0)
            {
                double blurRadius = Math.Clamp((panSpeed - 240.0) / 90.0, 0.0, 9.0);
                _editorBlur.Radius = blurRadius;
            }
            else if (_editorBlur.Radius > 0.0)
            {
                _editorBlur.Radius = Math.Max(0.0, _editorBlur.Radius - (dt * 55.0));
            }
        }
    }

    private void UpdateActiveLineHighlights(int activeLineIdx, double lineHeight, double gutterWidth)
    {
        if (_activeLineBand == null || _activeLineGutterBar == null) return;

        if (activeLineIdx < 0 || activeLineIdx >= _renderedRows.Count)
        {
            _activeLineBand.Visibility = Visibility.Collapsed;
            _activeLineGutterBar.Visibility = Visibility.Collapsed;
            return;
        }

        double lineTop = activeLineIdx * lineHeight;
        _activeLineBand.Height = lineHeight;
        Canvas.SetTop(_activeLineBand, lineTop);
        _activeLineBand.Visibility = Visibility.Visible;

        _activeLineGutterBar.Height = lineHeight * 0.70;
        Canvas.SetLeft(_activeLineGutterBar, Math.Max(8.0, gutterWidth - 10.0));
        Canvas.SetTop(_activeLineGutterBar, lineTop + (lineHeight * 0.15));
        _activeLineGutterBar.Visibility = Visibility.Visible;
    }

    private int FindActiveLineIndex(TimeSpan effectivePos)
    {
        if (_formattedLines.Count == 0) return -1;

        for (int i = 0; i < _formattedLines.Count; i++)
        {
            var line = _formattedLines[i];
            if (line.Timestamp <= effectivePos && effectivePos < line.EndTime)
            {
                return i;
            }
        }

        for (int i = _formattedLines.Count - 1; i >= 0; i--)
        {
            if (effectivePos >= _formattedLines[i].Timestamp)
            {
                return i;
            }
        }

        return -1;
    }

    private static string ExtractArtistTag(string? rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist)) return "live";
        string trimmed = rawArtist.Trim();
        int space = trimmed.IndexOf(' ');
        string firstWord = space > 0 ? trimmed[..space] : trimmed;
        return firstWord.ToLowerInvariant();
    }
}
