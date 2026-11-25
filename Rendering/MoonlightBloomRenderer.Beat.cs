using System;
using System.Collections.Generic;
using System.Linq;

namespace LyricalWidget.Rendering;

public sealed partial class MoonlightBloomRenderer
{
    /// <summary>Snappy damped jolt on beats. Softer hits only register while the tempo is high.</summary>
    private sealed class BeatVibration
    {
        private const double Omega = 2.0 * Math.PI * 11.0;
        private const double Damping = 0.30;
        private readonly Random _rng = new();
        private readonly Queue<double> _intervals = new();
        private double _clock;
        private double _lastBeatAt = double.NegativeInfinity;
        private double _lastJoltAt = double.NegativeInfinity;
        private double _previousBeat;
        private double _previousSnare;
        private double _vx;
        private double _vy;

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Pulse { get; private set; }
        public double Bpm { get; private set; }

        public bool Update(double dt, double beat, double snare)
        {
            _clock += dt;

            bool beatHit = beat > 0.22 && beat > _previousBeat + 0.10;
            bool snareHit = snare > 0.30 && snare > _previousSnare + 0.12;
            if (beatHit) TrackTempo();

            double strength = Math.Max(beatHit ? beat : 0.0, snareHit ? snare * 0.85 : 0.0);
            bool fast = Bpm >= HighTempoBpm;
            if (strength > (fast ? 0.22 : 0.42) && _clock - _lastJoltAt > 0.09)
            {
                _lastJoltAt = _clock;
                double amount = 1.5 + (2.5 * Math.Clamp((strength - 0.22) / 0.60, 0.0, 1.0));
                if (!fast) amount = Math.Max(1.5, amount * 0.75);

                double angle = _rng.NextDouble() * Math.PI * 2.0;
                X = Math.Cos(angle) * amount;
                Y = Math.Sin(angle) * amount;
                _vx = 0.0;
                _vy = 0.0;
                Pulse = 0.010 * Math.Clamp(strength, 0.0, 1.0);
                _previousBeat = beat;
                _previousSnare = snare;
                return false; // Show the full hit for one frame before the spring pulls it back
            }
            _previousBeat = beat;
            _previousSnare = snare;

            // Small fixed steps keep the stiff spring stable at 30 FPS
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
            double h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                _vx += ((-Omega * Omega * X) - (2.0 * Damping * Omega * _vx)) * h;
                _vy += ((-Omega * Omega * Y) - (2.0 * Damping * Omega * _vy)) * h;
                X = Math.Clamp(X + (_vx * h), -4.0, 4.0);
                Y = Math.Clamp(Y + (_vy * h), -4.0, 4.0);
            }
            Pulse *= Math.Exp(-9.0 * dt);

            if (Math.Abs(X) + Math.Abs(Y) < 0.04 && Math.Abs(_vx) + Math.Abs(_vy) < 1.0 && Pulse < 0.0003)
            {
                X = Y = _vx = _vy = Pulse = 0.0;
                return true;
            }
            return false;
        }

        private void TrackTempo()
        {
            double gap = _clock - _lastBeatAt;
            if (gap < 0.22) return;
            _lastBeatAt = _clock;

            if (gap > 1.5)
            {
                // Beats stopped for a while: start the estimate again
                _intervals.Clear();
                Bpm = 0.0;
                return;
            }

            _intervals.Enqueue(gap);
            if (_intervals.Count > 8) _intervals.Dequeue();
            if (_intervals.Count < 4) return;
            var sorted = _intervals.OrderBy(v => v).ToList();
            Bpm = 60.0 / sorted[sorted.Count / 2];
        }
    }
}
