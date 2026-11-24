using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
public sealed class KineticLiveCodeRenderer
{
    private static readonly FontFamily MonospaceFont = new("JetBrains Mono, Cascadia Mono, Consolas, Nirmala UI, Segoe UI, monospace");
    private static readonly Typeface MonospaceTypeface = new(MonospaceFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // Spring dampers for camera & smooth continuous caret glide
    private readonly SpringDamper _cameraXSpring = new(3.4, 0.76);
    private readonly SpringDamper _cameraYSpring = new(3.2, 0.78);
    private readonly SpringDamper _cameraZoomSpring = new(2.2, 0.85);
    private readonly SpringDamper _caretXSpring = new(5.5, 0.82);

    // Visual references
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

    // Active line highlight strip elements
    private Border? _activeLineBand;
    private Rectangle? _activeLineGutterBar;
    private Rectangle? _indentGuideCol0;
    private Rectangle? _indentGuideCol4;

    // Data state
    private List<FormattedCodeLine> _formattedLines = new();
    private readonly List<CodeLineRowVisual> _renderedRows = new();
    private TrackInfo? _currentTrack;
    private BitmapSource? _cachedHdArt;

    // Curated vibrant synthwave colors
    private Color _accentColor = Color.FromRgb(0xFF, 0x4D, 0x6D);    // Vibrant coral
    private Color _functionColor = Color.FromRgb(0x38, 0xBD, 0xF8);  // Neon cyan
    private Color _keywordColor = Color.FromRgb(0xC0, 0x84, 0xFC);   // Bright violet
    private Color _stringColor = Color.FromRgb(0x34, 0xD3, 0x99);    // Vibrant mint
    private Color _operatorColor = Color.FromRgb(0xFB, 0x71, 0x85);  // Hot coral
    private Color _numberColor = Color.FromRgb(0xFB, 0xBF, 0x24);    // Warm amber
    private Color _identifierColor = Color.FromRgb(0xF4, 0xF4, 0xF8);// Crisp warm white
    private Color _commentColor = Color.FromRgb(0x64, 0x74, 0x8B);   // Cool slate
    private Color _punctuationColor = Color.FromRgb(0x94, 0xA3, 0xB8);

    // Camera & Caret state
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

        // Vertical indent guides
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

        // Active line highlight band
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

        // Vertical accent bar on gutter
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

        // Ensure visible window around active line or line 0 is immediately populated
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

        // Locate active code line
        int activeLineIdx = FindActiveLineIndex(effectivePos);
        bool hasLyrics = isPlaying && !isInstrumental && activeLineIdx >= 0 && activeLineIdx < _formattedLines.Count;

        // Instrumental gap detection
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

        // Line change whip-pan detection
        if (activeLineIdx != _lastActiveLineIndex && activeLineIdx >= 0)
        {
            _lastActiveLineIndex = activeLineIdx;
            _lastWhipPanTime = currentTimeSec;
        }

        // Virtual canvas line and caret layout
        Point caretPoint;
        UpdateRollingLines(activeLineIdx, effectivePos, dt, baseFontSize, lineHeight, gutterWidth, out caretPoint);

        // Active line highlights
        UpdateActiveLineHighlights(activeLineIdx, lineHeight, gutterWidth);

        // Camera framing and deadzone tracking
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

        // Step camera springs
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

        // Background parallax
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

        // Whip-pan motion blur on line breaks
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

    private void UpdateTypographyDimensions()
    {
        if (_renderedRows.Count == 0) return;

        double baseFontSize = Math.Clamp(_viewportHeight * 0.046, 30.0, 56.0);
        double lineHeight = Math.Max(56.0, baseFontSize * 1.85);
        double gutterWidth = Math.Max(76.0, baseFontSize * 1.70);
        double gutterFontSize = Math.Max(14.0, baseFontSize * 0.44);

        if (_indentGuideCol0 != null) Canvas.SetLeft(_indentGuideCol0, gutterWidth);
        if (_indentGuideCol4 != null) Canvas.SetLeft(_indentGuideCol4, gutterWidth + (MeasureTextWidth("    ", baseFontSize)));

        for (int i = 0; i < _renderedRows.Count; i++)
        {
            var row = _renderedRows[i];
            row.RowGrid.Height = lineHeight;
            Canvas.SetTop(row.RowGrid, i * lineHeight);

            if (row.RowGrid.ColumnDefinitions.Count > 0)
            {
                row.RowGrid.ColumnDefinitions[0].Width = new GridLength(gutterWidth, GridUnitType.Pixel);
            }
            row.NumberBlock.FontSize = gutterFontSize;
            row.CodeBlock.FontSize = baseFontSize;

            // Update token highlight chips dimensions
            UpdateTokenHighlightChips(row, gutterWidth, baseFontSize, lineHeight);
        }
    }

    private void RebuildVisualRows()
    {
        _renderedRows.Clear();
        if (_linesCanvas == null) return;
        _linesCanvas.Children.Clear();

        double baseFontSize = Math.Clamp(_viewportHeight * 0.046, 30.0, 56.0);
        double lineHeight = Math.Max(56.0, baseFontSize * 1.85);
        double gutterWidth = Math.Max(76.0, baseFontSize * 1.70);
        double gutterFontSize = Math.Max(14.0, baseFontSize * 0.44);

        if (_indentGuideCol0 != null) Canvas.SetLeft(_indentGuideCol0, gutterWidth);
        if (_indentGuideCol4 != null) Canvas.SetLeft(_indentGuideCol4, gutterWidth + (MeasureTextWidth("    ", baseFontSize)));

        for (int i = 0; i < _formattedLines.Count; i++)
        {
            var line = _formattedLines[i];

            var rowGrid = new Grid
            {
                Height = lineHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(rowGrid, 0);
            Canvas.SetTop(rowGrid, i * lineHeight);

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gutterWidth, GridUnitType.Pixel) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Line number in gutter
            var numText = new TextBlock
            {
                Text = line.LineNumber.ToString(CultureInfo.InvariantCulture),
                FontFamily = MonospaceFont,
                FontSize = gutterFontSize,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x5F, 0x89)),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };
            Grid.SetColumn(numText, 0);
            rowGrid.Children.Add(numText);

            var textCell = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            // Syntax highlight chips
            var chipsCanvas = new Canvas
            {
                IsHitTestVisible = false
            };
            textCell.Children.Add(chipsCanvas);

            // Code tokens text
            var codeText = new TextBlock
            {
                FontFamily = MonospaceFont,
                FontSize = baseFontSize,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(codeText, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(codeText, TextRenderingMode.Auto);

            var runs = new List<(Run run, CodeToken token)>();
            foreach (var token in line.Tokens)
            {
                var run = new Run(token.Text)
                {
                    Foreground = GetTokenBrush(token.Type)
                };
                codeText.Inlines.Add(run);
                runs.Add((run, token));
            }

            textCell.Children.Add(codeText);
            Grid.SetColumn(textCell, 1);
            rowGrid.Children.Add(textCell);

            var visualRow = new CodeLineRowVisual
            {
                RowGrid = rowGrid,
                NumberBlock = numText,
                CodeBlock = codeText,
                ChipsCanvas = chipsCanvas,
                Runs = runs,
                Line = line,
                LineIndex = i
            };

            _renderedRows.Add(visualRow);
            _linesCanvas.Children.Add(rowGrid);

            // Build hero token highlight chips
            UpdateTokenHighlightChips(visualRow, gutterWidth, baseFontSize, lineHeight);
        }
    }

    private void UpdateTokenHighlightChips(CodeLineRowVisual row, double gutterWidth, double baseFontSize, double lineHeight)
    {
        if (row.ChipsCanvas == null) return;
        row.ChipsCanvas.Children.Clear();

        var prefixSb = new StringBuilder();

        foreach (var (_, token) in row.Runs)
        {
            // Only add highlight chips to Hero Tokens (functions, strings, or tagged expressions)
            if (token.Type is CodeTokenType.Function or CodeTokenType.StringLiteral)
            {
                double tokenOffset = MeasureTextWidth(prefixSb.ToString(), baseFontSize);
                double tokenWidth = MeasureTextWidth(token.Text, baseFontSize);

                Color chipColor = token.Type switch
                {
                    CodeTokenType.Function => _functionColor,
                    CodeTokenType.StringLiteral => _stringColor,
                    _ => _accentColor
                };

                var chip = new Border
                {
                    Width = Math.Max(12.0, tokenWidth + 6.0),
                    Height = baseFontSize * 1.25,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x1E, chipColor.R, chipColor.G, chipColor.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, chipColor.R, chipColor.G, chipColor.B)),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(chip, tokenOffset - 3.0);
                Canvas.SetTop(chip, (lineHeight - (baseFontSize * 1.25)) * 0.5);
                row.ChipsCanvas.Children.Add(chip);
            }

            prefixSb.Append(token.Text);
        }
    }

    private void EnsureLinesPopulated()
    {
        if (_renderedRows.Count == 0) return;
        int activeIdx = _lastActiveLineIndex >= 0 ? _lastActiveLineIndex : 0;
        int minIdx = Math.Max(0, activeIdx - 10);
        int maxIdx = Math.Min(_renderedRows.Count - 1, activeIdx + 8);

        for (int i = 0; i < _renderedRows.Count; i++)
        {
            var row = _renderedRows[i];
            if (i >= minIdx && i <= maxIdx)
            {
                row.RowGrid.Visibility = Visibility.Visible;
                row.RowGrid.Opacity = (i < activeIdx) ? 0.42 : (i == activeIdx ? 1.0 : 0.0);
            }
            else
            {
                row.RowGrid.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateRollingLines(
        int activeLineIdx,
        TimeSpan effectivePos,
        double dt,
        double baseFontSize,
        double lineHeight,
        double gutterWidth,
        out Point caretPoint)
    {
        caretPoint = new Point(gutterWidth + 20, 100);
        if (_linesCanvas == null || _renderedRows.Count == 0) return;

        for (int i = 0; i < _renderedRows.Count; i++)
        {
            var row = _renderedRows[i];

            if (activeLineIdx < 0)
            {
                // Before playback: reveal header comment
                row.RowGrid.Opacity = i == 0 ? 0.70 : 0.0;
                row.RowGrid.Visibility = i <= 2 ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }

            // Rolling buffer of visible lines around active line
            int dist = i - activeLineIdx;
            if (dist < -16 || dist > 8)
            {
                row.RowGrid.Visibility = Visibility.Collapsed;
                continue;
            }
            row.RowGrid.Visibility = Visibility.Visible;

            if (i < activeLineIdx)
            {
                // Dim completed lines
                row.RowGrid.Opacity = 0.42;
                foreach (var (run, token) in row.Runs)
                {
                    run.Text = token.Text;
                }
            }
            else if (i == activeLineIdx)
            {
                // Active line typing progress
                row.RowGrid.Opacity = 1.0;
                var line = row.Line;
                double elapsedMs = (effectivePos - line.Timestamp).TotalMilliseconds;
                double totalMs = Math.Max(120.0, (line.EndTime - line.Timestamp).TotalMilliseconds);
                double progress = Math.Clamp(elapsedMs / totalMs, 0.0, 1.0);

                int totalChars = 0;
                foreach (var (_, tok) in row.Runs) totalChars += tok.Text.Length;

                double charProgress = totalChars * progress;
                int revealChars = (int)Math.Floor(charProgress);
                double fracChar = charProgress - revealChars;

                var revealedSb = new StringBuilder();
                int runningRevealed = 0;

                foreach (var (run, token) in row.Runs)
                {
                    int tokLen = token.Text.Length;
                    if (revealChars >= runningRevealed + tokLen)
                    {
                        run.Text = token.Text;
                        revealedSb.Append(token.Text);
                    }
                    else if (revealChars > runningRevealed)
                    {
                        int sub = revealChars - runningRevealed;
                        string partial = token.Text[..sub];
                        run.Text = partial;
                        revealedSb.Append(partial);
                    }
                    else
                    {
                        run.Text = string.Empty;
                    }
                    runningRevealed += tokLen;
                }

                string revealedStr = revealedSb.ToString();
                double textWidth = MeasureTextWidth(revealedStr, baseFontSize);

                double approxCharWidth = MeasureTextWidth("m", baseFontSize);
                double targetCaretX = gutterWidth + textWidth + (fracChar * approxCharWidth * 0.45);

                _caretXSpring.Step(ref _caretCurrentX, ref _caretCurrentXVel, targetCaretX, dt);

                double caretHeight = baseFontSize * 1.15;
                double caretY = (i * lineHeight) + ((lineHeight - caretHeight) * 0.5);
                caretPoint = new Point(_caretCurrentX, caretY + (caretHeight * 0.5));

                if (_caretCanvas != null)
                {
                    _caretCanvas.Visibility = Visibility.Visible;

                    if (_caretBlock != null)
                    {
                        _caretBlock.Width = Math.Max(3.5, baseFontSize * 0.08);
                        _caretBlock.Height = caretHeight;
                        Canvas.SetLeft(_caretBlock, _caretCurrentX);
                        Canvas.SetTop(_caretBlock, caretY);
                    }

                    if (_artistPill != null)
                    {
                        double pillFontSize = Math.Clamp(baseFontSize * 0.38, 11.0, 16.0);
                        if (_artistText != null)
                        {
                            _artistText.FontSize = pillFontSize;
                        }

                        double pillY = caretY - 26.0;
                        double pillX = Math.Max(0.0, _caretCurrentX - 4.0);
                        Canvas.SetLeft(_artistPill, pillX);
                        Canvas.SetTop(_artistPill, pillY);
                    }
                }

                UpdateLineDoodle(line, row, gutterWidth, baseFontSize, caretY);
            }
            else
            {
                row.RowGrid.Opacity = 0.0;
            }
        }
    }

    private double MeasureTextWidth(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0.0;

        double dpi = 1.0;
        if (_editorCanvas != null)
        {
            dpi = VisualTreeHelper.GetDpi(_editorCanvas).PixelsPerDip;
        }

        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonospaceTypeface,
            fontSize,
            Brushes.White,
            dpi);

        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private void UpdateLineDoodle(
        FormattedCodeLine line,
        CodeLineRowVisual row,
        double gutterWidth,
        double baseFontSize,
        double caretY)
    {
        if (_doodleCanvas == null) return;
        _doodleCanvas.Children.Clear();

        CodeToken? matchedToken = null;
        var prefixSb = new StringBuilder();

        foreach (var (_, token) in row.Runs)
        {
            if (!string.IsNullOrEmpty(token.KeywordDoodle))
            {
                matchedToken = token;
                break;
            }
            prefixSb.Append(token.Text);
        }

        if (matchedToken == null) return;

        double wordPrefixWidth = MeasureTextWidth(prefixSb.ToString(), baseFontSize);
        double doodleX = gutterWidth + wordPrefixWidth + 4.0;
        double doodleY = caretY;

        switch (matchedToken.KeywordDoodle)
        {
            case "crown":
                var crown = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0 10 L 3.5 2 L 8 7 L 12.5 2 L 16 10 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                    Width = 16,
                    Height = 10,
                    Stretch = Stretch.Uniform
                };
                Canvas.SetLeft(crown, doodleX);
                Canvas.SetTop(crown, doodleY - 14.0);
                _doodleCanvas.Children.Add(crown);
                break;

            case "mirror":
                var mirrorText = new TextBlock
                {
                    Text = matchedToken.Text.Trim(),
                    FontFamily = MonospaceFont,
                    FontSize = baseFontSize * 0.9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x44, 0x7A, 0xA2, 0xF7)),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1.0, -1.0)
                };
                Canvas.SetLeft(mirrorText, doodleX);
                Canvas.SetTop(mirrorText, doodleY + baseFontSize + 4.0);
                _doodleCanvas.Children.Add(mirrorText);
                break;

            case "strikethrough":
                double tokenWidth = MeasureTextWidth(matchedToken.Text, baseFontSize);
                var strikeLine = new Rectangle
                {
                    Width = Math.Max(24.0, tokenWidth + 6.0),
                    Height = 2.5,
                    Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                };
                Canvas.SetLeft(strikeLine, doodleX - 3.0);
                Canvas.SetTop(strikeLine, doodleY + (baseFontSize * 0.55));
                _doodleCanvas.Children.Add(strikeLine);
                break;

            case "wave":
                var wavePath = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0 0 Q 6 5 12 0 T 24 0 T 36 0"),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                    StrokeThickness = 2.2
                };
                Canvas.SetLeft(wavePath, doodleX);
                Canvas.SetTop(wavePath, doodleY + baseFontSize + 6.0);
                _doodleCanvas.Children.Add(wavePath);
                break;

            case "sleep":
                for (int z = 0; z < 2; z++)
                {
                    var zText = new TextBlock
                    {
                        Text = "z",
                        FontFamily = MonospaceFont,
                        FontSize = (baseFontSize * 0.35) + (z * 4.0),
                        Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xA8, 0x55, 0xF7))
                    };
                    Canvas.SetLeft(zText, doodleX + (z * 12.0));
                    Canvas.SetTop(zText, doodleY - 14.0 - (z * 10.0));
                    _doodleCanvas.Children.Add(zText);
                }
                break;
        }
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

    private Brush GetTokenBrush(CodeTokenType type)
    {
        return type switch
        {
            CodeTokenType.Keyword => new SolidColorBrush(_keywordColor),          // Bright violet
            CodeTokenType.Function => new SolidColorBrush(_functionColor),        // Luminous cyan
            CodeTokenType.StringLiteral => new SolidColorBrush(_stringColor),     // Vibrant mint
            CodeTokenType.Operator => new SolidColorBrush(_operatorColor),        // Hot coral
            CodeTokenType.Number => new SolidColorBrush(_numberColor),            // Warm amber
            CodeTokenType.Comment => new SolidColorBrush(_commentColor),          // Cool slate
            CodeTokenType.Punctuation => new SolidColorBrush(_punctuationColor),  // Silver-grey
            _ => new SolidColorBrush(_identifierColor)                            // Luminous warm white
        };
    }

    private static Color EnsureVibrantAccent(Color raw, string? seed)
    {
        // Convert to HSV to evaluate saturation & value
        RgbToHsv(raw, out double h, out double s, out double v);

        // If album cover is dark, monochrome, or washed out, boost or fall back to curated neon IDE accents
        if (s < 0.35 || v < 0.45)
        {
            Color[] neonPalette =
            {
                Color.FromRgb(0xFF, 0x4D, 0x6D), // Vivid coral
                Color.FromRgb(0xA7, 0x8B, 0xFA), // Electric violet
                Color.FromRgb(0x38, 0xBD, 0xF8), // Neon cyan
                Color.FromRgb(0xF5, 0x9E, 0x0B), // Warm amber gold
                Color.FromRgb(0x10, 0xB9, 0x81)  // Emerald neon
            };

            int index = Math.Abs((seed ?? "Lyrical").GetHashCode()) % neonPalette.Length;
            return neonPalette[index];
        }

        // Boost saturation and brightness slightly to guarantee glowing IDE presence
        s = Math.Clamp(s * 1.15, 0.70, 1.0);
        v = Math.Clamp(v * 1.10, 0.85, 1.0);
        return HsvToRgb(h, s, v);
    }

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            h = 0;
            return;
        }

        if (r >= max)
            h = (g - b) / delta;
        else if (g >= max)
            h = 2.0 + (b - r) / delta;
        else
            h = 4.0 + (r - g) / delta;

        h *= 60.0;
        if (h < 0) h += 360.0;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0 % 2.0) - 1.0));
        double m = v - c;

        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Clamp((r + m) * 255.0, 0, 255),
            (byte)Math.Clamp((g + m) * 255.0, 0, 255),
            (byte)Math.Clamp((b + m) * 255.0, 0, 255));
    }

    private static string ExtractArtistTag(string? rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist)) return "live";
        string trimmed = rawArtist.Trim();
        int space = trimmed.IndexOf(' ');
        string firstWord = space > 0 ? trimmed[..space] : trimmed;
        return firstWord.ToLowerInvariant();
    }

    private sealed class CodeLineRowVisual
    {
        public Grid RowGrid { get; init; } = null!;
        public TextBlock NumberBlock { get; init; } = null!;
        public TextBlock CodeBlock { get; init; } = null!;
        public Canvas ChipsCanvas { get; init; } = null!;
        public List<(Run run, CodeToken token)> Runs { get; init; } = new();
        public FormattedCodeLine Line { get; init; } = null!;
        public int LineIndex { get; init; }
    }
}
