using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LyricalWidget.Rendering;

// One lyric line: layout, reveal and exit
public sealed partial class MoonlightBloomRenderer
{
    private readonly record struct Token(string Text, double StartMs, bool GapAfter, bool CanBreakAfter);

    private sealed class Word
    {
        public required FrameworkElement Element { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Translate { get; init; }
        public required double StartMs { get; init; }
        public required double X { get; init; }      // Left edge within its row
        public required double Width { get; init; }
        public required int Row { get; init; }
        public TextBlock? Sheen { get; init; }
        public TranslateTransform? SheenShift { get; init; }
        public FrameworkElement? Surge { get; init; }   // Extra bloom for the climax word
        public required TextBlock Text { get; init; }
        public required DropShadowEffect InnerGlow { get; init; }
        public required DropShadowEffect OuterGlow { get; init; }
        public TextBlock? SurgeText { get; init; }
        public BakedImage? BakedGlyphs { get; init; }   // Game Mode: the glowing word as a finished picture
        public BakedImage? BakedSurge { get; init; }
        public List<Droplet> Droplets { get; } = new();
        public double Age = -1.0;                    // Seconds since revealed; negative while hidden
    }

    private sealed class Phrase
    {
        private readonly List<Word> _words = new();

        /// <summary>Recolours every word. Each cached word bitmap is redrawn once, not every frame.</summary>
        public void ApplyTint(MoonlightTint? tint)
        {
            foreach (var w in _words)
            {
                w.Text.Foreground = tint?.TextBrush ?? Brushes.White;
                w.InnerGlow.Color = tint?.InnerGlow ?? Color.FromRgb(0xE4, 0xE4, 0xE7);
                w.OuterGlow.Color = tint?.OuterGlow ?? Colors.White;
                if (w.SurgeText != null) w.SurgeText.Foreground = tint?.GlowBrush ?? Brushes.White;
                w.BakedGlyphs?.Recolor(((SolidColorBrush)w.Text.Foreground).Color, w.InnerGlow.Color, w.OuterGlow.Color);
                if (w.SurgeText != null) w.BakedSurge?.Recolor(((SolidColorBrush)w.SurgeText.Foreground).Color, Colors.White, Colors.White);
            }
        }
        private readonly double[] _rowX;
        private readonly double[] _rowVelocity;
        private readonly double _rowHeight;
        private readonly ScaleTransform _exitScale = new(1, 1);
        private readonly TranslateTransform _exitTranslate = new();
        private double _top;
        private double _topVelocity;
        private readonly double _climaxAtMs;
        private readonly bool _lens;
        private readonly double _stageWidth;
        private readonly double _stageMidX;
        private readonly double _stageMidY;
        private readonly double _slideFrom;
        private double _climaxAge = -1.0;
        private double _enterAge;
        private double _exitAge;
        private bool _placed;
        private int _revealed;

        public Canvas Root { get; } = new() { IsHitTestVisible = false };
        public double FontSize { get; }

        public Phrase(List<Token> tokens, double stageW, double stageH, double maxFontSize, double dpi, double climaxAtMs, int slideDirection, bool lite, bool lens, Random rng)
        {
            _lens = lens;
            _stageWidth = stageW;
            _climaxAtMs = climaxAtMs;
            _stageMidX = stageW / 2.0;
            _stageMidY = stageH / 2.0;
            _slideFrom = slideDirection * stageW * 0.055;

            // The lens stage already has its margins, so the block can use nearly all of it
            double maxWidth = stageW * (lens ? 0.96 : 0.84);
            double maxHeight = stageH * (lens ? 0.90 : 0.78);
            double size = Math.Max(12.0, Math.Min(maxFontSize, Math.Min(stageW * 0.085, stageH * 0.26)));

            List<(int Row, double X, double Width)> layout;
            int rows;
            while (true)
            {
                layout = Layout(tokens, size, maxWidth, dpi, out rows, out double widest);
                bool fits = widest <= maxWidth && rows * size * 1.28 <= maxHeight && rows <= 4;
                if (fits || size <= 12.0) break;
                size = Math.Max(12.0, size * 0.93);
            }

            // Even out the rows so the last one is not left with a lone word
            for (double narrower = maxWidth * 0.95; rows > 1 && narrower > maxWidth * 0.5; narrower -= maxWidth * 0.05)
            {
                var balanced = Layout(tokens, size, narrower, dpi, out int balancedRows, out double balancedWidest);
                if (balancedRows > rows || balancedWidest > narrower) break;
                layout = balanced;
            }

            FontSize = size;
            _rowHeight = size * 1.28;
            _rowX = new double[rows];
            _rowVelocity = new double[rows];

            var exit = new TransformGroup();
            exit.Children.Add(_exitScale);
            exit.Children.Add(_exitTranslate);
            _exitScale.CenterX = stageW / 2.0;
            _exitScale.CenterY = stageH / 2.0;
            Root.RenderTransform = exit;

            for (int i = 0; i < tokens.Count; i++)
            {
                var (row, x, width) = layout[i];
                bool isClimaxWord = i == tokens.Count - 1 && !double.IsInfinity(climaxAtMs);
                int droplets = lite ? 0 : Math.Min(tokens[i].Text.Length >= 5 ? 3 : 2, Math.Max(1, MaxDroplets / Math.Max(1, tokens.Count)));
                if (i * droplets >= MaxDroplets) droplets = 0;
                var word = CreateWord(tokens[i], row, x, width, dpi, isClimaxWord, lite, droplets, rng);
                _words.Add(word);
                Root.Children.Add(word.Element);
            }
        }

        private static List<(int Row, double X, double Width)> Layout(List<Token> tokens, double size, double maxWidth, double dpi, out int rows, out double widest)
        {
            var result = new List<(int, double, double)>(tokens.Count);
            double space = size * 0.30;
            double x = 0.0;
            int row = 0;
            widest = 0.0;

            for (int i = 0; i < tokens.Count;)
            {
                // Unbreakable run: syllables of one word stay together
                int end = i;
                double run = 0.0;
                var widths = new List<double>();
                while (true)
                {
                    double w = Measure(tokens[end].Text, size, dpi).WidthIncludingTrailingWhitespace;
                    widths.Add(w);
                    run += w;
                    if (tokens[end].CanBreakAfter || end == tokens.Count - 1) break;
                    end++;
                }

                if (x > 0.0 && x + run > maxWidth)
                {
                    row++;
                    x = 0.0;
                }
                for (int k = 0; k < widths.Count; k++)
                {
                    result.Add((row, x, widths[k]));
                    x += widths[k];
                }
                widest = Math.Max(widest, x);
                if (tokens[end].GapAfter) x += space;
                i = end + 1;
            }

            rows = row + 1;
            return result;
        }

        private Word CreateWord(Token token, int row, double x, double width, double dpi, bool isClimaxWord, bool lite, int dropletCount, Random rng)
        {
            // Tight inner glow plus a wide soft bloom, flattened into one cached bitmap
            var text = new TextBlock
            {
                Text = token.Text,
                FontFamily = Font,
                FontWeight = FontWeights.Black,
                FontSize = FontSize,
                Foreground = Brushes.White,
                Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 0, Color = Color.FromRgb(0xE4, 0xE4, 0xE7), Opacity = 0.55, RenderingBias = RenderingBias.Performance }
            };
            var outerGlow = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Color = Colors.White, Opacity = 0.60, RenderingBias = RenderingBias.Performance };
            var bloom = new Border
            {
                Child = text,
                Effect = outerGlow
            };

            var scale = new ScaleTransform(0.88, 0.88, width / 2.0, _rowHeight / 2.0);
            var translate = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);

            // Cached at the screen's real pixel density, larger for the zooming word, and larger again under the
            // lens, which magnifies the middle by up to 1 / (1 - Strength): otherwise the bitmap is stretched
            BakedImage? bakedGlyphs = null;
            BakedImage? bakedSurge = null;
            double cacheScale = Math.Max(1.0, dpi) * (isClimaxWord ? 1.5 : 1.0) * (_lens ? 1.0 / (1.0 - FisheyeLens.Strength) : 1.0);
            FrameworkElement glyphs = lite
                // Game Mode draws on the CPU, where the two glows would be blurred again on every frame.
                // Drawn once into a picture instead, the word then costs no more than any image.
                ? bakedGlyphs = new BakedImage(new GlyphBake(token.Text, Font, FontSize, Colors.White, Color.FromRgb(0xE4, 0xE4, 0xE7), Colors.White, 0.0), padding: 30.0, cacheScale)
                : new Border
            {
                Child = bloom,
                CacheMode = new BitmapCache { RenderAtScale = cacheScale, SnapsToDevicePixels = false }
            };

            var element = new Grid { Opacity = 0.0, RenderTransform = group, IsHitTestVisible = false };

            FrameworkElement? surge = null;
            TextBlock? surgeText = null;
            if (isClimaxWord)
            {
                var surgeBlock = new TextBlock
                {
                    Text = token.Text,
                    FontFamily = Font,
                    FontWeight = FontWeights.Black,
                    FontSize = FontSize,
                    Foreground = Brushes.White,
                    Effect = new BlurEffect { Radius = Math.Clamp(FontSize * 0.42, 12.0, 60.0), RenderingBias = RenderingBias.Performance }
                };
                surgeText = surgeBlock;
                double surgeBlur = Math.Clamp(FontSize * 0.42, 12.0, 60.0);
                surge = lite
                    ? bakedSurge = new BakedImage(new GlyphBake(token.Text, Font, FontSize, Colors.White, Colors.White, Colors.White, surgeBlur), padding: surgeBlur + 4.0, Math.Max(1.0, dpi) * 0.5) { Opacity = 0.0 }
                    : new Border
                {
                    Opacity = 0.0,
                    CacheMode = new BitmapCache { RenderAtScale = Math.Max(1.0, dpi) * 0.5 },
                    Child = surgeBlock
                };
                element.Children.Add(surge);
                Panel.SetZIndex(element, 1);
            }
            element.Children.Add(glyphs);

            TextBlock? sheen = null;
            TranslateTransform? sheenShift = null;
            if (!lite)
            {
                // A silver band between two bright edges, swept across the letters like light on water
                sheenShift = new TranslateTransform(-1.2, 0);
                sheen = new TextBlock
                {
                    Text = token.Text,
                    FontFamily = Font,
                    FontWeight = FontWeights.Black,
                    FontSize = FontSize,
                    Visibility = Visibility.Collapsed,
                    Foreground = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.0, 0.15),
                        EndPoint = new Point(1.0, 0.85),
                        RelativeTransform = sheenShift,
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0x00, 0xA1, 0xA1, 0xAA), 0.18),
                            new GradientStop(Color.FromArgb(0x99, 0xA1, 0xA1, 0xAA), 0.38),
                            new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.50),
                            new GradientStop(Color.FromArgb(0x88, 0x71, 0x71, 0x7A), 0.60),
                            new GradientStop(Color.FromArgb(0x00, 0xA1, 0xA1, 0xAA), 0.80)
                        }
                    }
                };
                element.Children.Add(sheen);
            }

            var word = new Word
            {
                Element = element,
                Scale = scale,
                Translate = translate,
                StartMs = token.StartMs,
                X = x,
                Width = width,
                Row = row,
                Sheen = sheen,
                SheenShift = sheenShift,
                Surge = surge,
                Text = text,
                InnerGlow = (DropShadowEffect)text.Effect,
                OuterGlow = outerGlow,
                SurgeText = surgeText,
                BakedGlyphs = bakedGlyphs,
                BakedSurge = bakedSurge
            };

            if (dropletCount > 0)
            {
                var beads = new Canvas();
                // Beads sit on the letter strokes, not in the gaps between them
                var outline = Measure(token.Text, FontSize, dpi).BuildGeometry(new Point(0, 0));
                for (int i = 0; i < dropletCount; i++)
                {
                    for (int attempt = 0; attempt < 14; attempt++)
                    {
                        var at = new Point(
                            outline.Bounds.Left + (rng.NextDouble() * outline.Bounds.Width),
                            outline.Bounds.Top + (rng.NextDouble() * outline.Bounds.Height));
                        if (outline.Bounds.IsEmpty || !outline.FillContains(at)) continue;

                        var droplet = new Droplet(at, FontSize * (0.055 + (rng.NextDouble() * 0.060)), 0.10 + (rng.NextDouble() * 0.30));
                        beads.Children.Add(droplet.Element);
                        word.Droplets.Add(droplet);
                        break;
                    }
                }
                element.Children.Add(beads);
            }
            return word;
        }

        /// <summary>Shows every word that should already be visible, without animating.</summary>
        public void CatchUp(double posMs, double stageW, double stageH)
        {
            Reveal(posMs);
            foreach (var w in _words)
            {
                if (w.Age >= 0) w.Age = LiquidSec;
            }
            _enterAge = SlideInSec;
            Reflow(0.0, posMs, stageW, stageH, snap: true);
            Apply();
        }

        public bool Update(double dt, double posMs, double stageW, double stageH)
        {
            bool settled = true;
            foreach (var w in _words)
            {
                if (w.Age >= 0 && w.Age < LiquidSec)
                {
                    w.Age = Math.Min(LiquidSec, w.Age + dt);
                    settled = false;
                }
            }

            if (_enterAge < SlideInSec)
            {
                _enterAge = Math.Min(SlideInSec, _enterAge + dt);
                settled = false;
            }
            if (_climaxAge < 0 && posMs >= _climaxAtMs) _climaxAge = 0.0;
            if (_climaxAge >= 0 && _climaxAge < ClimaxSec)
            {
                _climaxAge = Math.Min(ClimaxSec, _climaxAge + dt);
                settled = false;
            }

            Reveal(posMs);
            if (!Reflow(dt, posMs, stageW, stageH, snap: !_placed)) settled = false;
            Apply();
            return settled;
        }

        private void Reveal(double posMs)
        {
            while (_revealed < _words.Count && _words[_revealed].StartMs - WordLeadMs <= posMs)
            {
                _words[_revealed].Age = 0.0;
                _revealed++;
            }
            // A line always opens with its first word, even if that word's own timestamp is late
            if (_revealed == 0 && _words.Count > 0)
            {
                _words[0].Age = 0.0;
                _revealed = 1;
            }
        }

        /// <summary>Glides rows and the whole stack toward being centred around the visible words.</summary>
        private bool Reflow(double dt, double posMs, double stageW, double stageH, bool snap)
        {
            if (_revealed == 0) return true;

            bool settled = true;
            int shown = _revealed;
            while (shown < _words.Count && _words[shown].StartMs - WordLeadMs - ReflowLeadMs <= posMs) shown++;

            int lastRow = _words[shown - 1].Row;
            for (int row = 0; row < _rowX.Length; row++)
            {
                double visible = 0.0;
                for (int i = 0; i < shown; i++)
                {
                    if (_words[i].Row == row) visible = Math.Max(visible, _words[i].X + _words[i].Width);
                }

                double target = (stageW - visible) / 2.0;
                // A row that has not started yet waits where its first word will be centred
                if (visible == 0.0)
                {
                    foreach (var w in _words)
                    {
                        if (w.Row == row)
                        {
                            target = (stageW - w.Width) / 2.0;
                            break;
                        }
                    }
                }

                if (snap || row > lastRow)
                {
                    _rowX[row] = target;
                    _rowVelocity[row] = 0.0;
                }
                else if (!Spring(ref _rowX[row], ref _rowVelocity[row], target, dt))
                {
                    settled = false;
                }
            }

            double topTarget = (stageH - ((lastRow + 1) * _rowHeight)) / 2.0;
            if (snap)
            {
                _top = topTarget;
                _topVelocity = 0.0;
                _placed = true;
            }
            else if (!Spring(ref _top, ref _topVelocity, topTarget, dt))
            {
                settled = false;
            }
            return settled;
        }

        /// <summary>Critically damped spring: fast, with no overshoot.</summary>
        private static bool Spring(ref double value, ref double velocity, double target, double dt)
        {
            if (Math.Abs(target - value) < 0.1 && Math.Abs(velocity) < 0.5)
            {
                value = target;
                velocity = 0.0;
                return true;
            }

            double omega = Math.Sqrt(ReflowStiffness);
            double delta = value - target;
            double temp = (velocity + (omega * delta)) * dt;
            double decay = Math.Exp(-omega * dt);
            velocity = (velocity - (omega * temp)) * decay;
            value = target + ((delta + temp) * decay);
            return false;
        }

        private void Apply()
        {
            // Lines glide in from alternating sides, stretched along their travel
            double enter = Math.Clamp(_enterAge / SlideInSec, 0.0, 1.0);
            double glide = Math.Pow(1.0 - enter, 3.0);
            _exitTranslate.X = _slideFrom * glide;
            _exitScale.ScaleX = 1.0 + (0.09 * glide);

            double climaxT = _climaxAge < 0 ? 0.0 : Math.Clamp(_climaxAge / ClimaxSec, 0.0, 1.0);
            double climax = 1.0 - Math.Pow(1.0 - climaxT, 4.0);

            foreach (var w in _words)
            {
                w.Translate.X = _rowX[w.Row] + w.X;
                w.Translate.Y = _top + (w.Row * _rowHeight);

                if (w.Age < 0)
                {
                    w.Element.Opacity = 0.0;
                    continue;
                }

                double t = Math.Clamp(w.Age / WordEnterSec, 0.0, 1.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
                double s = 0.88 + (0.12 * eased);
                // Damped squash and stretch, like a drop settling
                double ripple = Math.Exp(-6.0 * w.Age) * Math.Sin(w.Age * 19.0) * 0.045;
                w.Scale.ScaleX = s * (1.0 + ripple);
                w.Scale.ScaleY = s * (1.0 - ripple);
                w.Element.Opacity = Math.Clamp(t * 1.9, 0.0, 1.0);
                ApplyLiquid(w);

                if (_climaxAge < 0) continue;
                if (w.Surge == null)
                {
                    w.Element.Opacity *= 1.0 - (0.93 * climax);
                    continue;
                }

                // Never shrinks: a long word simply runs to the stage edge
                double zoom = Math.Max(1.45, Math.Min(1.75, (_stageMidX * 1.9) / Math.Max(1.0, w.Width)));
                // Through the lens it must stay whole: capped, and never wider than the safe zone
                if (_lens) zoom = Math.Max(1.0, Math.Min(LensMaxZoom, _stageWidth / Math.Max(1.0, w.Width)));
                double scale = 1.0 + ((zoom - 1.0) * climax);
                w.Scale.ScaleX *= scale;
                w.Scale.ScaleY *= scale;
                w.Translate.X += (_stageMidX - (w.Width / 2.0) - w.Translate.X) * 0.85 * climax;
                w.Translate.Y += (_stageMidY - (_rowHeight / 2.0) - w.Translate.Y) * 0.85 * climax;
                w.Surge.Opacity = 0.45 * climax + (0.55 * Math.Sin(Math.PI * climaxT));
            }
        }

        private static void ApplyLiquid(Word w)
        {
            if (w.Sheen != null && w.SheenShift != null)
            {
                double sweep = (w.Age - 0.12) / 0.75;
                bool visible = sweep > 0.0 && sweep < 1.0;
                w.Sheen.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) w.SheenShift.X = -1.0 + (2.0 * sweep);
            }
            foreach (var d in w.Droplets) d.Form(w.Age);
        }

        /// <summary>Rapid vertical stretch and fade, or a push past the camera after a climax. Returns true once gone.</summary>
        public bool UpdateExit(double dt, double stageW)
        {
            _exitAge += dt;
            double t = Math.Clamp(_exitAge / ExitSec, 0.0, 1.0);
            double eased = t * t * (3.0 - (2.0 * t));
            if (_climaxAge >= 0)
            {
                _exitScale.ScaleX = 1.0 + ((_lens ? 0.10 : 0.45) * eased);
                _exitScale.ScaleY = 1.0 + ((_lens ? 0.10 : 0.45) * eased);
            }
            else
            {
                _exitScale.ScaleY = 1.0 + (0.38 * eased);
                _exitScale.ScaleX = 1.0 - (0.04 * eased);
                _exitTranslate.Y = -FontSize * 0.55 * eased;
            }

            foreach (var w in _words)
            {
                if (w.Age < 0) continue;
                if (w.Sheen != null && w.SheenShift != null)
                {
                    w.Sheen.Visibility = Visibility.Visible;
                    w.SheenShift.X = -1.0 + (2.0 * t);
                }
                foreach (var d in w.Droplets) d.Fall(t, FontSize);
            }
            Root.Opacity = (1.0 - t) * (1.0 - t);
            return t >= 1.0;
        }

        /// <summary>Stage bounds of the newest word, or sometimes the one before it.</summary>
        public Rect GetActiveWordBounds(Random rng, out TranslateTransform? anchor)
        {
            anchor = null;
            if (_revealed == 0) return Rect.Empty;
            int i = _revealed - 1;
            if (i > 0 && rng.Next(4) == 0) i--;
            var w = _words[i];
            anchor = w.Translate;
            return new Rect(w.Translate.X, w.Translate.Y + (_rowHeight * 0.08), Math.Max(1.0, w.Width), _rowHeight * 0.84);
        }
    }
}
