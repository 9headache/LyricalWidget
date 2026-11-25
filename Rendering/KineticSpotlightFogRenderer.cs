using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;

namespace LyricalWidget.Rendering;

/// <summary>
/// Renders the SpotlightFog fullscreen kinetic typography preset:
/// continuous vertical conveyor belt lyrics with velocity smear over a dual-mode
/// (swirling album fluid or razor-sharp 1200x1200 artwork) frosted fog backdrop.
/// </summary>
public sealed partial class KineticSpotlightFogRenderer
{
    private const int PoolSize = 5;

    private static readonly FontFamily MultiScriptFont = new("Segoe UI Variable Display, Bahnschrift, Arial Black, Nirmala UI, Yu Gothic UI, Malgun Gothic, Segoe UI, sans-serif");
    private static readonly Typeface MultiScriptTypeface = new(MultiScriptFont, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);

    private readonly SpringDamper _conveyorSpring = new(3.0, 0.76);
    private readonly SpringDamper _opacitySpring = new(2.2, 0.85);

    private Grid? _artworkGroup;
    private Image? _sharpImage;
    private ScaleTransform? _sharpScale;
    private Image? _blurredImage;
    private ScaleTransform? _blurredScale;
    private RadialGradientBrush? _fogMask;
    private Canvas? _lyricsCanvas;

    private readonly Grid[] _slotContainers = new Grid[PoolSize];
    private readonly TextBlock[] _slotTextBlocks = new TextBlock[PoolSize];
    private readonly ScaleTransform[] _slotScales = new ScaleTransform[PoolSize];
    private readonly TranslateTransform[] _slotTranslates = new TranslateTransform[PoolSize];
    private readonly DropShadowEffect[] _slotShadows = new DropShadowEffect[PoolSize];
    private readonly BlurEffect[] _slotBlurs = new BlurEffect[PoolSize];

    private readonly List<TimedWord> _allWords = new();
    private BitmapSource? _cachedSharpArt;
    private BitmapSource? _cachedBlurredArt;

    private Color _boostedAccentColor = Color.FromRgb(0xD4, 0xD4, 0xD8);
    private Color _mutedAccentColor = Color.FromArgb(170, 0xD4, 0xD4, 0xD8);

    private string _backdropMode = "HdArtwork";
    private double _artworkGroupOpacity = 1.0;

    private double _scrollPos = 0.0;
    private double _scrollVel = 0.0;
    private double _masterOpacityPos = 0.0;
    private double _masterOpacityVel = 0.0;
    private double _lastPositionMs = 0.0;
    private int _lastActiveIndex = -1;

    private double _viewportWidth = 1920.0;
    private double _viewportHeight = 1080.0;

    public void Initialize(
        Grid artworkGroup,
        Image sharpImage,
        ScaleTransform sharpScale,
        Image blurredImage,
        ScaleTransform blurredScale,
        RadialGradientBrush fogMask,
        Canvas lyricsCanvas)
    {
        _artworkGroup = artworkGroup;
        _sharpImage = sharpImage;
        _sharpScale = sharpScale;
        _blurredImage = blurredImage;
        _blurredScale = blurredScale;
        _fogMask = fogMask;
        _lyricsCanvas = lyricsCanvas;

        RenderOptions.SetBitmapScalingMode(_sharpImage, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(_blurredImage, BitmapScalingMode.HighQuality);

        _lyricsCanvas.Children.Clear();

        for (int i = 0; i < PoolSize; i++)
        {
            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false,
                Opacity = 0.0
            };

            var scale = new ScaleTransform(1.0, 1.0);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            container.RenderTransform = group;

            var shadow = new DropShadowEffect
            {
                BlurRadius = 28,
                Direction = 270,
                ShadowDepth = 3,
                Color = Colors.Black,
                Opacity = 0.85
            };
            container.Effect = shadow;

            var blur = new BlurEffect
            {
                Radius = 0.0,
                RenderingBias = RenderingBias.Performance
            };

            var textBlock = new TextBlock
            {
                FontFamily = MultiScriptFont,
                FontWeight = FontWeights.Black,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = blur
            };

            container.Children.Add(textBlock);

            _slotContainers[i] = container;
            _slotScales[i] = scale;
            _slotTranslates[i] = translate;
            _slotShadows[i] = shadow;
            _slotBlurs[i] = blur;
            _slotTextBlocks[i] = textBlock;

            _lyricsCanvas.Children.Add(container);
        }

        OnResize(_viewportWidth, _viewportHeight);
    }

    public void SetBackdropMode(string mode)
    {
        _backdropMode = mode;
    }

    public void OnResize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _viewportWidth = width;
        _viewportHeight = height;

        double containerW = width * 0.85;
        double containerH = height * 0.26;
        double left = (width - containerW) * 0.5;
        double top = (height - containerH) * 0.5;

        for (int i = 0; i < PoolSize; i++)
        {
            if (_slotContainers[i] != null)
            {
                _slotContainers[i].Width = containerW;
                _slotContainers[i].Height = containerH;
                Canvas.SetLeft(_slotContainers[i], left);
                Canvas.SetTop(_slotContainers[i], top);
            }
        }
    }

    public void UpdateLyrics(LrcDocument? lyrics)
    {
        _allWords.Clear();
        _lastActiveIndex = -1;
        _scrollPos = 0.0;
        _scrollVel = 0.0;

        if (lyrics == null || lyrics.Lines == null || lyrics.Lines.Count == 0)
        {
            return;
        }

        for (int lineIdx = 0; lineIdx < lyrics.Lines.Count; lineIdx++)
        {
            var line = lyrics.Lines[lineIdx];
            TimeSpan lineDur = line.Duration > TimeSpan.Zero ? line.Duration : TimeSpan.FromSeconds(4.0);

            if (line.HasWordSync && line.Words != null && line.Words.Count > 0)
            {
                foreach (var w in line.Words)
                {
                    if (string.IsNullOrWhiteSpace(w.Text)) continue;
                    _allWords.Add(new TimedWord
                    {
                        Text = w.Text.Trim(),
                        Start = w.Timestamp,
                        End = w.Timestamp + (w.Duration > TimeSpan.Zero ? w.Duration : TimeSpan.FromMilliseconds(300)),
                        LineIndex = lineIdx
                    });
                }
            }
            else if (!string.IsNullOrWhiteSpace(line.Text))
            {
                var splits = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (splits.Length > 0)
                {
                    int totalChars = 0;
                    foreach (var s in splits) totalChars += Math.Max(1, s.Length);

                    double lineStartMs = line.Timestamp.TotalMilliseconds;
                    double totalMs = Math.Max(400.0, lineDur.TotalMilliseconds);
                    double curMs = lineStartMs;

                    for (int i = 0; i < splits.Length; i++)
                    {
                        double wDur = (Math.Max(1, splits[i].Length) / (double)totalChars) * totalMs;
                        _allWords.Add(new TimedWord
                        {
                            Text = splits[i],
                            Start = TimeSpan.FromMilliseconds(curMs),
                            End = TimeSpan.FromMilliseconds(curMs + wDur),
                            LineIndex = lineIdx
                        });
                        curMs += wDur;
                    }
                }
            }
        }
    }

    public void UpdateAlbumArt(BitmapSource? sharpArt, VibePalette palette)
    {
        if (sharpArt != null)
        {
            _cachedSharpArt = sharpArt;
            _cachedBlurredArt = CreatePreblurredBitmap(sharpArt);

            if (_sharpImage != null) _sharpImage.Source = _cachedSharpArt;
            if (_blurredImage != null) _blurredImage.Source = _cachedBlurredArt;
        }

        UpdatePalette(palette);
    }

    public void UpdateHdAlbumArt(BitmapSource hdArt)
    {
        _cachedSharpArt = hdArt;
        _cachedBlurredArt = CreatePreblurredBitmap(hdArt);

        if (_sharpImage != null) _sharpImage.Source = _cachedSharpArt;
        if (_blurredImage != null) _blurredImage.Source = _cachedBlurredArt;
    }

    public void UpdatePalette(VibePalette palette)
    {
        Color candidate = palette.Accent;
        if (candidate.A == 0 || (candidate.R == 0 && candidate.G == 0 && candidate.B == 0))
        {
            candidate = palette.Highlight.A > 0 ? palette.Highlight : palette.Primary;
        }

        _boostedAccentColor = BoostLuminanceFloor(candidate, 0.68);
        _mutedAccentColor = Color.FromArgb(170, (byte)(_boostedAccentColor.R * 0.75), (byte)(_boostedAccentColor.G * 0.75), (byte)(_boostedAccentColor.B * 0.75));
    }

    public void Deactivate()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            if (_slotContainers[i] != null) _slotContainers[i].Opacity = 0.0;
            if (_slotBlurs[i] != null) _slotBlurs[i].Radius = 0.0;
            if (_slotTextBlocks[i] != null) _slotTextBlocks[i].Text = string.Empty;
        }

        _lastActiveIndex = -1;
        _lastPositionMs = 0.0;
        _scrollPos = 0.0;
        _scrollVel = 0.0;
        _masterOpacityPos = 0.0;
        _masterOpacityVel = 0.0;
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
    }

    public void Reset()
    {
        _lastActiveIndex = -1;
        _lastPositionMs = 0.0;
        _scrollPos = 0.0;
        _scrollVel = 0.0;
        _masterOpacityPos = 0.0;
        _masterOpacityVel = 0.0;

        for (int i = 0; i < PoolSize; i++)
        {
            if (_slotContainers[i] != null) _slotContainers[i].Opacity = 0.0;
            if (_slotBlurs[i] != null) _slotBlurs[i].Radius = 0.0;
            if (_slotTextBlocks[i] != null) _slotTextBlocks[i].Text = string.Empty;
        }
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

        double breathe = 1.02 + (0.02 * Math.Sin(currentTimeSec * 0.35));
        if (_sharpScale != null)
        {
            _sharpScale.ScaleX = breathe;
            _sharpScale.ScaleY = breathe;
        }
        if (_blurredScale != null)
        {
            _blurredScale.ScaleX = breathe;
            _blurredScale.ScaleY = breathe;
        }

        if (_fogMask != null)
        {
            double fogTime = currentTimeSec * 0.22;
            double cx = 0.50 + (0.08 * Math.Sin(fogTime));
            double cy = 0.50 + (0.06 * Math.Cos(fogTime * 0.82));
            double ox = 0.50 + (0.10 * Math.Cos(fogTime * 0.65));
            double oy = 0.50 + (0.07 * Math.Sin(fogTime * 1.15));

            double rx = 0.48 + (0.05 * Math.Sin(fogTime * 0.50)) + (bassEnergy * 0.03);
            double ry = 0.46 + (0.05 * Math.Cos(fogTime * 0.60)) + (bassEnergy * 0.03);

            _fogMask.Center = new Point(cx, cy);
            _fogMask.GradientOrigin = new Point(ox, oy);
            _fogMask.RadiusX = rx;
            _fogMask.RadiusY = ry;
        }

        if (_artworkGroup != null)
        {
            bool showArtwork = string.Equals(_backdropMode, "HdArtwork", StringComparison.OrdinalIgnoreCase) && _cachedSharpArt != null;
            double targetArtOpacity = showArtwork ? 1.0 : 0.0;
            double artLerp = Math.Clamp(dt / 0.35, 0.0, 1.0);
            _artworkGroupOpacity += (targetArtOpacity - _artworkGroupOpacity) * artLerp;
            _artworkGroup.Opacity = _artworkGroupOpacity;
        }

        double curPosMs = effectivePos.TotalMilliseconds;
        bool isSeek = _lastPositionMs > 0 && Math.Abs(curPosMs - _lastPositionMs) > 300.0;
        _lastPositionMs = curPosMs;

        int activeWordIdx = FindActiveWordIndex(effectivePos);

        if (isSeek && activeWordIdx >= 0)
        {
            _scrollPos = activeWordIdx;
            _scrollVel = 0.0;
            _lastActiveIndex = activeWordIdx;
        }
        else if (activeWordIdx >= 0)
        {
            _lastActiveIndex = activeWordIdx;
            _conveyorSpring.Step(ref _scrollPos, ref _scrollVel, activeWordIdx, dt);
        }
        else if (_lastActiveIndex >= 0)
        {
            _conveyorSpring.Step(ref _scrollPos, ref _scrollVel, _lastActiveIndex, dt);
        }

        bool hasActiveContent = isPlaying && !isInstrumental && activeWordIdx >= 0 && _allWords.Count > 0;
        double targetMasterOpacity = hasActiveContent ? 1.0 : 0.0;
        _opacitySpring.Step(ref _masterOpacityPos, ref _masterOpacityVel, targetMasterOpacity, dt);

        if (_masterOpacityPos < 0.005 && targetMasterOpacity == 0.0)
        {
            for (int i = 0; i < PoolSize; i++) _slotContainers[i].Opacity = 0.0;
            return;
        }

        double scrollSpeed = Math.Abs(_scrollVel);
        double stretchY = 1.0 + Math.Clamp(scrollSpeed * 0.035, 0.0, 0.12);
        double smearBlurRadius = Math.Clamp(scrollSpeed * 3.0, 0.0, 10.0);
        if (scrollSpeed < 0.06) smearBlurRadius = 0.0;

        int centerIdx = (int)Math.Round(_scrollPos);
        double baseHeroFontSize = Math.Clamp(_viewportHeight * 0.12, 68.0, 150.0);
        double maxAllowedWidth = _viewportWidth * 0.78;

        string activeHeroText = (centerIdx >= 0 && centerIdx < _allWords.Count) ? _allWords[centerIdx].Text : string.Empty;
        double measuredHeroWidth = MeasureWordWidth(activeHeroText, MultiScriptTypeface, baseHeroFontSize);

        double heroFontSize = baseHeroFontSize;
        if (measuredHeroWidth > maxAllowedWidth && measuredHeroWidth > 1.0)
        {
            heroFontSize = Math.Max(38.0, baseHeroFontSize * (maxAllowedWidth / measuredHeroWidth));
        }

        double stepY = _viewportHeight * 0.17;

        for (int i = 0; i < PoolSize; i++)
        {
            int relative = i - 2; // -2, -1, 0, 1, 2
            int wordIdx = centerIdx + relative;

            if (wordIdx >= 0 && wordIdx < _allWords.Count)
            {
                var word = _allWords[wordIdx];
                double delta = wordIdx - _scrollPos;
                double absDelta = Math.Abs(delta);

                if (_slotTextBlocks[i].Text != word.Text)
                {
                    _slotTextBlocks[i].Text = word.Text;
                }
                _slotTextBlocks[i].FontSize = heroFontSize;

                double posY = delta * stepY;

                double lineDir = (word.LineIndex % 2 == 0) ? 1.0 : -1.0;
                double leanSign = delta >= 0.0 ? lineDir : -lineDir;
                double posX = leanSign * (_viewportWidth * 0.09) * Math.Min(1.0, absDelta);

                _slotTranslates[i].X = posX;
                _slotTranslates[i].Y = posY;

                double scale;
                if (absDelta <= 1.0)
                {
                    double cos = Math.Cos(absDelta * Math.PI * 0.5);
                    scale = 0.42 + (0.58 * cos * cos);
                }
                else
                {
                    double t = Math.Clamp((absDelta - 1.0) / 1.0, 0.0, 1.0);
                    scale = Math.Max(0.20, 0.42 - (0.17 * t));
                }

                _slotScales[i].ScaleX = scale;
                _slotScales[i].ScaleY = scale * stretchY;

                double opacity;
                if (delta >= 0.0)
                {
                    if (delta <= 1.0)
                        opacity = 1.0 - (0.12 * delta);
                    else
                        opacity = 0.88 * Math.Clamp((1.85 - delta) / 0.85, 0.0, 1.0);
                }
                else
                {
                    double neg = -delta;
                    if (neg <= 1.0)
                        opacity = 1.0 - (0.48 * neg);
                    else
                        opacity = 0.52 * Math.Clamp((1.85 - neg) / 0.85, 0.0, 1.0);
                }

                _slotContainers[i].Opacity = _masterOpacityPos * opacity;

                Color slotColor;
                if (delta >= 0.0)
                {
                    double t = Math.Clamp(delta, 0.0, 1.0);
                    slotColor = LerpColor(_boostedAccentColor, Colors.White, t);
                }
                else
                {
                    double t = Math.Clamp(-delta, 0.0, 1.0);
                    slotColor = LerpColor(_boostedAccentColor, _mutedAccentColor, t);
                }
                _slotTextBlocks[i].Foreground = new SolidColorBrush(slotColor);

                _slotBlurs[i].Radius = (absDelta < 0.65) ? smearBlurRadius : 0.0;
                _slotShadows[i].Opacity = 0.85 * Math.Clamp(1.0 - absDelta, 0.0, 1.0);
            }
            else
            {
                _slotContainers[i].Opacity = 0.0;
            }
        }
    }

    private int FindActiveWordIndex(TimeSpan effectivePos)
    {
        if (_allWords.Count == 0) return -1;

        if (effectivePos < _allWords[0].Start)
        {
            return -1;
        }

        if (_lastActiveIndex >= 0 && _lastActiveIndex < _allWords.Count)
        {
            var cur = _allWords[_lastActiveIndex];
            if (cur.Start <= effectivePos && effectivePos < cur.End)
            {
                return _lastActiveIndex;
            }

            if (_lastActiveIndex + 1 < _allWords.Count)
            {
                var next = _allWords[_lastActiveIndex + 1];
                if (next.Start <= effectivePos && effectivePos < next.End)
                {
                    return _lastActiveIndex + 1;
                }
            }
        }

        int low = 0;
        int high = _allWords.Count - 1;
        int candidate = -1;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            var w = _allWords[mid];

            if (w.Start <= effectivePos && effectivePos < w.End)
            {
                return mid;
            }
            if (w.Start <= effectivePos)
            {
                candidate = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (candidate >= 0 && candidate < _allWords.Count)
        {
            var candWord = _allWords[candidate];
            TimeSpan gap = effectivePos - candWord.End;
            if (gap >= TimeSpan.Zero && gap < TimeSpan.FromMilliseconds(850))
            {
                return candidate;
            }
        }

        return -1;
    }

    private static double MeasureWordWidth(string text, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.0;
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            1.0);
        return ft.Width;
    }

    private sealed class TimedWord
    {
        public string Text { get; init; } = string.Empty;
        public TimeSpan Start { get; init; }
        public TimeSpan End { get; init; }
        public int LineIndex { get; init; }
    }
}
