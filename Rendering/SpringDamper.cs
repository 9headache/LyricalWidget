using System;

namespace LyricalWidget.Rendering;

/// <summary>
/// Harmonic spring damper parameterized by natural frequency (Hz) and damping ratio (zeta).
/// </summary>
public readonly struct SpringDamper
{
    public readonly double K;
    public readonly double C;

    public SpringDamper(double frequencyHz, double dampingRatio)
    {
        double omega = 2.0 * Math.PI * frequencyHz;
        K = omega * omega;
        C = 2.0 * dampingRatio * omega;
    }

    public bool Step(ref double position, ref double velocity, double target, double dt, double threshold = 0.05)
    {
        double displacement = position - target;
        double force = (-K * displacement) - (C * velocity);
        velocity += force * dt;
        position += velocity * dt;

        if (Math.Abs(displacement) < threshold && Math.Abs(velocity) < threshold)
        {
            position = target;
            velocity = 0.0;
            return true;
        }
        return false;
    }

    public static readonly SpringDamper LineFluid = new(1.65, 0.88);
    public static readonly SpringDamper LineSnappy = new(2.35, 0.72);
    public static readonly SpringDamper WordYFluid = new(1.35, 0.72);
    public static readonly SpringDamper WordYSnappy = new(2.10, 0.70);
    public static readonly SpringDamper WordScaleFluid = new(1.45, 0.68);
    public static readonly SpringDamper WordScaleSnappy = new(2.20, 0.65);
    public static readonly SpringDamper GlowDecayFluid = new(1.15, 0.95);
    public static readonly SpringDamper GlowDecaySnappy = new(1.80, 0.90);
}
