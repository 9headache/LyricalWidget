using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricalWidget.Lyrics;

namespace LyricalWidget.Rendering;

/// <summary>
/// "Moonlight Bloom" lyric style: chunky glowing white words that appear one by one on a black stage.
/// The glow of each word is baked into a bitmap cache, so frames only move, scale and fade bitmaps.
/// </summary>
public sealed partial class MoonlightBloomRenderer
{
    private const double WordLeadMs = 60.0;      // Reveal slightly early so the word is readable on its beat
    private const double WordEnterSec = 0.34;
    private const double ExitSec = 0.24;
    private const double SeekThresholdMs = 800.0;
    private const double ReflowStiffness = 210.0;
    private const double ReflowLeadMs = 240.0;    // Make room before a word arrives instead of after
    private const int SparkleCount = 4;
    private const double LiquidSec = 1.1;        // Sheen sweep and droplet formation after a word enters
    private const double SlideInSec = 0.50;
    private const double ClimaxSec = 0.34;
    private const double ClimaxLeadMs = 520.0;   // Zoom begins this long before the line leaves
    private const double ClimaxShare = 0.30;
    private const int MaxDroplets = 16;
    private const double HighTempoBpm = 120.0;
    private const double LensBlockScale = 0.86;
    private const double LensMaxZoom = 1.30;

    private static readonly FontFamily Font = new("Arial Rounded MT Bold, Arial Rounded MT, Segoe UI Black, Segoe UI");
    private static readonly Typeface Face = new(Font, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
    private static readonly Geometry StarGeometry = CreateStar();

    private readonly Canvas _stage;
    private readonly Canvas _phraseLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _sparkleLayer = new() { IsHitTestVisible = false };
    private readonly List<Phrase> _exiting = new();
    private readonly List<Sparkle> _sparkles = new();
    private readonly Random _rng = new();
    private readonly bool _showInterludeNote;

    // Whole-block motion: sway, tilt and beat vibration, shared by the words and the sparkles
    private readonly TranslateTransform _blockTranslate = new();
    private readonly ScaleTransform _blockScale = new(1, 1);
    private readonly SkewTransform _blockSkew = new();
    private readonly BeatVibration _vibration = new();
    private readonly HashSet<int> _climaxLines = new();
    private double _swayTime;
    private int _slideDirection = 1;

    private LrcDocument _lyrics = LrcDocument.Empty();
    private Phrase? _current;
    private int _currentIndex = int.MinValue;
    private double _lastPosMs = double.NaN;
    private double _stageW;
    private double _stageH;
    private double _maxFontSize = double.PositiveInfinity;
    private double _builtMaxFontSize = double.PositiveInfinity;
    private bool _lensMode;
    private double _holdX;
    private double _holdY;
    private double _fit = 1.0; // Temporary rescale while HoldLayout is set

    /// <param name="stage">Canvas to draw on. Its actual size is the lyric area.</param>
    /// <param name="showInterludeNote">Show a music note while no line is being sung.</param>
    public MoonlightBloomRenderer(Canvas stage, bool showInterludeNote)
    {
        _stage = stage;
        _showInterludeNote = showInterludeNote;
        var block = new TransformGroup();
        block.Children.Add(_blockScale);
        block.Children.Add(_blockSkew);
        block.Children.Add(_blockTranslate);
        _phraseLayer.RenderTransform = block;
        _sparkleLayer.RenderTransform = block;

        _stage.Children.Add(_phraseLayer);
        _stage.Children.Add(_sparkleLayer);

        for (int i = 0; i < SparkleCount; i++)
        {
            var sparkle = new Sparkle(StarGeometry);
            _sparkles.Add(sparkle);
            _sparkleLayer.Children.Add(sparkle.Element);
        }
    }

    public double MaxFontSize
    {
        get => _maxFontSize;
        set => _maxFontSize = value;
    }

    /// <summary>Keeps everything inside the fisheye safe zone: smaller block, less sway, a capped last-word zoom.</summary>
    public bool LensMode
    {
        get => _lensMode;
        set
        {
            if (_lensMode == value) return;
            _lensMode = value;
            Reset();
        }
    }

    /// <summary>While set, a size change only rescales what is on stage instead of laying it out again.</summary>
    public bool HoldLayout { get; set; }

    /// <summary>Skips the per-word liquid sheen and droplets, for software rendering.</summary>
    public bool LiteEffects { get; set; }

    public void UpdateLyrics(LrcDocument lyrics)
    {
        _lyrics = lyrics;
        ChooseClimaxLines();
        Reset();
    }

    /// <summary>Clears the stage. The next frame rebuilds the active line already caught up.</summary>
    public void Reset()
    {
        _phraseLayer.Children.Clear();
        _exiting.Clear();
        _current = null;
        _currentIndex = int.MinValue;
        _lastPosMs = double.NaN;
        foreach (var s in _sparkles) s.Hide();
    }

    /// <summary>Advances one frame. Returns true when nothing is moving any more.</summary>
    public bool UpdateFrame(double dt, TimeSpan position, bool isPlaying, double beatImpulse = 0.0, double snareImpulse = 0.0)
    {
        double w = _stage.ActualWidth;
        double h = _stage.ActualHeight;
        if (w < 40 || h < 30 || !_lyrics.HasSyncedLyrics) return true;

        dt = Math.Clamp(dt, 0.0, 0.1);
        double posMs = position.TotalMilliseconds;

        bool resized = Math.Abs(w - _stageW) > 0.5 || Math.Abs(h - _stageH) > 0.5;
        bool hold = HoldLayout && resized && _current != null;
        _fit = hold ? Math.Min(w / _stageW, h / _stageH) : 1.0;
        _holdX = hold ? (w - _stageW) / 2.0 : 0.0;
        _holdY = hold ? (h - _stageH) / 2.0 : 0.0;

        if (!hold && (resized || _builtMaxFontSize != _maxFontSize))
        {
            _stageW = w;
            _stageH = h;
            _builtMaxFontSize = _maxFontSize;
            Reset();
        }

        bool catchUp = double.IsNaN(_lastPosMs) || Math.Abs(posMs - _lastPosMs) > SeekThresholdMs;
        // Ignore small backward clock corrections so revealed words never replay
        if (!catchUp && posMs < _lastPosMs) posMs = _lastPosMs;
        _lastPosMs = posMs;

        int index = FindActiveIndex(posMs);
        if (catchUp || index != _currentIndex)
        {
            SwitchTo(index, posMs, catchUp);
        }

        bool settled = true;
        if (_current != null)
        {
            settled &= _current.Update(dt, posMs, _stageW, _stageH);
        }

        for (int i = _exiting.Count - 1; i >= 0; i--)
        {
            settled = false;
            if (_exiting[i].UpdateExit(dt, _stageW))
            {
                _phraseLayer.Children.Remove(_exiting[i].Root);
                _exiting.RemoveAt(i);
            }
        }

        settled &= UpdateSparkles(dt, isPlaying);
        settled &= UpdateBlockMotion(dt, isPlaying, beatImpulse, snareImpulse);
        return settled;
    }

    private bool UpdateBlockMotion(double dt, bool isPlaying, double beatImpulse, double snareImpulse)
    {
        if (isPlaying) _swayTime += dt;

        // Two slow waves of unrelated length, so the drift never visibly loops
        double reach = Math.Clamp(_stageW * 0.013, 3.0, 24.0) * (_lensMode ? 0.5 : 1.0);
        double size = _fit * (_lensMode ? LensBlockScale : 1.0);
        double sway = (Math.Sin(_swayTime * 0.95) * 0.75) + (Math.Sin((_swayTime * 0.41) + 1.3) * 0.35);
        double speed = (Math.Cos(_swayTime * 0.95) * 0.75) + (Math.Cos((_swayTime * 0.41) + 1.3) * 0.15);

        bool still = _vibration.Update(dt, isPlaying ? beatImpulse : 0.0, isPlaying ? snareImpulse : 0.0);

        _blockTranslate.X = _holdX + (sway * reach) + _vibration.X;
        _blockTranslate.Y = _holdY + (Math.Sin((_swayTime * 0.63) + 0.4) * reach * 0.18) + _vibration.Y;
        _blockSkew.CenterY = _stageH / 2.0;
        _blockSkew.AngleX = -speed * 0.55;
        _blockScale.CenterX = _stageW / 2.0;
        _blockScale.CenterY = _stageH / 2.0;
        _blockScale.ScaleX = size * (1.0 + (Math.Abs(speed) * 0.004) + _vibration.Pulse);
        _blockScale.ScaleY = size * (1.0 + _vibration.Pulse);
        return still;
    }

    /// <summary>
    /// Picks about 30% of the lines for the last-word zoom, favouring short and emphatic ones and never two
    /// in a row. The choice depends only on the lyrics, so a song plays the same way every time.
    /// </summary>
    private void ChooseClimaxLines()
    {
        _climaxLines.Clear();
        var lines = _lyrics.Lines;
        var ranked = new List<(int Index, double Key)>();
        int sung = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i].Text)) continue;
            sung++;

            var tokens = BuildTokens(i);
            // The last word needs time to land before it can zoom
            if (tokens.Count == 0 || LineEndMs(i) - tokens[^1].StartMs < 380.0) continue;

            string text = lines[i].Text.Trim();
            double weight = tokens.Count <= 4 ? 3.0 : tokens.Count <= 7 ? 1.6 : 0.8;
            if (text.EndsWith('!') || text.EndsWith('?')) weight *= 2.0;

            // Weighted draw without replacement
            double roll = (StableHash(text, i) % 10007 + 1) / 10008.0;
            ranked.Add((i, Math.Pow(roll, 1.0 / weight)));
        }

        int target = (int)Math.Round(sung * ClimaxShare);
        foreach (var (index, _) in ranked.OrderByDescending(r => r.Key))
        {
            if (_climaxLines.Count >= target) break;
            if (_climaxLines.Contains(index - 1) || _climaxLines.Contains(index + 1)) continue;
            _climaxLines.Add(index);
        }
    }

    private static uint StableHash(string text, int salt)
    {
        uint h = 2166136261u ^ ((uint)salt * 16777619u);
        foreach (char c in text) h = (h ^ c) * 16777619u;
        h ^= h >> 15;
        h *= 2246822519u;
        return h ^ (h >> 13);
    }

    /// <summary>When the line leaves the stage.</summary>
    private double LineEndMs(int index)
    {
        var lines = _lyrics.Lines;
        double start = lines[index].Timestamp.TotalMilliseconds;
        double spoken = Math.Max(lines[index].EndTime.TotalMilliseconds, start + 4000.0);
        double next = index + 1 < lines.Count ? lines[index + 1].Timestamp.TotalMilliseconds : double.PositiveInfinity;
        // Matches the long-gap rule in FindActiveIndex
        return next - spoken > 6000.0 ? spoken + 2500.0 : next;
    }

    /// <summary>Index of the line to show, or -1 for none (before the first line and in long instrumental gaps).</summary>
    private int FindActiveIndex(double posMs)
    {
        var lines = _lyrics.Lines;
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (lines[mid].Timestamp.TotalMilliseconds - WordLeadMs <= posMs)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        if (found < 0 || string.IsNullOrWhiteSpace(lines[found].Text)) return -1;

        double start = lines[found].Timestamp.TotalMilliseconds;
        double next = found + 1 < lines.Count ? lines[found + 1].Timestamp.TotalMilliseconds : double.PositiveInfinity;
        double spoken = Math.Max(lines[found].EndTime.TotalMilliseconds, start + 4000.0);
        bool longGap = next - spoken > 6000.0;
        return longGap && posMs > spoken + 2500.0 ? -1 : found;
    }

    private void SwitchTo(int index, double posMs, bool catchUp)
    {
        if (_current != null)
        {
            foreach (var s in _sparkles) s.Release();
            if (catchUp)
            {
                _phraseLayer.Children.Remove(_current.Root);
            }
            else
            {
                _exiting.Add(_current);
                while (_exiting.Count > 2)
                {
                    _phraseLayer.Children.Remove(_exiting[0].Root);
                    _exiting.RemoveAt(0);
                }
            }
            _current = null;
        }

        _currentIndex = index;
        var tokens = index >= 0 ? BuildTokens(index) : _showInterludeNote ? InterludeTokens(posMs) : new List<Token>();
        if (tokens.Count == 0) return;

        double dpi = VisualTreeHelper.GetDpi(_stage).PixelsPerDip;
        double climaxAtMs = double.PositiveInfinity;
        if (_climaxLines.Contains(index))
        {
            climaxAtMs = Math.Max(LineEndMs(index) - ClimaxLeadMs, tokens[^1].StartMs + 160.0);
        }

        _slideDirection = -_slideDirection;
        _current = new Phrase(tokens, _stageW, _stageH, _maxFontSize, dpi, climaxAtMs, _slideDirection, LiteEffects, _lensMode, _rng);
        _phraseLayer.Children.Add(_current.Root);
        if (catchUp) _current.CatchUp(posMs, _stageW, _stageH);
    }

    private static FormattedText Measure(string text, double size, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White, dpi);
}
