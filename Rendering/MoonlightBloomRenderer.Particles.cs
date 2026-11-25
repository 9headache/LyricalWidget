using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LyricalWidget.Rendering;

// Sparkles and droplets
public sealed partial class MoonlightBloomRenderer
{
    private bool UpdateSparkles(double dt, bool isPlaying)
    {
        bool idle = true;
        foreach (var s in _sparkles)
        {
            if (s.IsActive)
            {
                s.Update(dt);
                idle = false;
            }
            else if (isPlaying && _current != null)
            {
                s.Delay -= dt;
                if (s.Delay > 0) continue;

                var rect = _current.GetActiveWordBounds(_rng, out var anchor);
                if (anchor == null) continue;

                s.Delay = 0.15 + (_rng.NextDouble() * 0.9);
                s.Start(anchor, PickEdgePoint(rect), Math.Clamp(_current.FontSize * (0.16 + (_rng.NextDouble() * 0.14)), 6.0, 26.0), 0.8 + (_rng.NextDouble() * 0.7));
                idle = false;
            }
        }
        return idle;
    }

    /// <summary>A point on the top or bottom edge, or a side, so the letters themselves stay clear.</summary>
    private Point PickEdgePoint(Rect r)
    {
        double along = _rng.NextDouble();
        return _rng.Next(4) switch
        {
            0 => new Point(r.Left, r.Top + (r.Height * along)),
            1 => new Point(r.Right, r.Top + (r.Height * along)),
            2 => new Point(r.Left + (r.Width * along), r.Bottom),
            _ => new Point(r.Left + (r.Width * along), r.Top)
        };
    }

    private static Geometry CreateStar()
    {
        // Four-point star with concave sides, centred on the origin, radius 1
        var g = Geometry.Parse("M 0,-1 Q 0.1,-0.1 1,0 Q 0.1,0.1 0,1 Q -0.1,0.1 -1,0 Q -0.1,-0.1 0,-1 Z");
        g.Freeze();
        return g;
    }

    /// <summary>A glossy bead of water resting on a letter.</summary>
    private sealed class Droplet
    {
        private static readonly Brush Gloss = CreateGloss();
        private readonly ScaleTransform _scale = new(0, 0);
        private readonly TranslateTransform _translate;
        private readonly double _delay;
        private readonly double _restY;
        private readonly double _radius;

        public Ellipse Element { get; }

        public Droplet(Point at, double radius, double delay)
        {
            _radius = radius;
            _delay = delay;
            _restY = at.Y - radius;
            _translate = new TranslateTransform(at.X - radius, _restY);
            _scale.CenterX = radius;
            _scale.CenterY = radius;

            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_translate);
            Element = new Ellipse
            {
                Width = radius * 2.0,
                Height = radius * 2.1,
                Fill = Gloss,
                RenderTransform = group,
                IsHitTestVisible = false
            };
        }

        public void Form(double wordAge)
        {
            double t = Math.Clamp((wordAge - _delay) / 0.45, 0.0, 1.0);
            double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            _scale.ScaleX = eased;
            _scale.ScaleY = eased;
            // Creeps a little way down as it gathers
            _translate.Y = _restY + (_radius * 0.9 * eased);
        }

        public void Fall(double t, double fontSize)
        {
            _translate.Y = _restY + (_radius * 0.9) + (fontSize * 0.5 * t * t);
            _scale.ScaleY = 1.0 + (0.7 * t);
            _scale.ScaleX = 1.0 - (0.25 * t);
        }

        private static Brush CreateGloss()
        {
            // Dark enough at the rim to read on white letters, with a hard specular spot
            var brush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.34, 0.28), RadiusX = 0.5, RadiusY = 0.5 };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), 0.20));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xD4, 0xD4, 0xD8), 0.36));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, 0xA1, 0xA1, 0xAA), 0.70));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x52, 0x52, 0x5B), 0.94));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x52, 0x52, 0x5B), 1.00));
            brush.Freeze();
            return brush;
        }
    }

    private sealed class Sparkle
    {
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly RotateTransform _rotate = new();
        private readonly TranslateTransform _translate = new();
        private double _age;
        private double _life = 1.0;
        private double _size;
        private TranslateTransform? _anchor;         // Word position this sparkle travels with
        private Vector _offset;

        public Path Element { get; }
        public bool IsActive { get; private set; }
        public double Delay { get; set; }

        public Sparkle(Geometry star)
        {
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_rotate);
            group.Children.Add(_translate);
            Element = new Path { Data = star, Fill = Brushes.White, Opacity = 0.0, RenderTransform = group, IsHitTestVisible = false };
        }

        public void Start(TranslateTransform anchor, Point at, double size, double life)
        {
            _age = 0.0;
            _life = life;
            _size = size;
            _anchor = anchor;
            _offset = new Vector(at.X - anchor.X, at.Y - anchor.Y);
            _translate.X = at.X;
            _translate.Y = at.Y;
            IsActive = true;
        }

        public void Update(double dt)
        {
            _age += dt;
            double t = _age / _life;
            if (t >= 1.0)
            {
                Hide();
                return;
            }

            if (_anchor != null)
            {
                _translate.X = _anchor.X + _offset.X;
                _translate.Y = _anchor.Y + _offset.Y;
            }

            double pulse = Math.Sin(Math.PI * t);
            double radius = _size * 0.5 * (0.35 + (0.65 * pulse));
            _scale.ScaleX = radius;
            _scale.ScaleY = radius;
            _rotate.Angle = 18.0 * t;
            Element.Opacity = 0.9 * pulse;
        }

        /// <summary>Lets go of a word that is leaving and fades out quickly where it is.</summary>
        public void Release()
        {
            if (!IsActive) return;
            _anchor = null;
            _age = Math.Max(_age, _life * 0.75);
        }

        public void Hide()
        {
            IsActive = false;
            Element.Opacity = 0.0;
        }
    }
}
