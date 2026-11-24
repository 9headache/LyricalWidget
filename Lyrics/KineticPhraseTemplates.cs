using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LyricalWidget.Lyrics;

public enum KineticTemplateType
{
    EditorialCalm,
    PopRhythmic,
    RapHighEnergy,
    HeroOutlinePoster = PopRhythmic,
    VerticalTurn90 = RapHighEnergy,
    EditorialContrast = EditorialCalm,
    BoldTypewriter = PopRhythmic
}

/// <summary>
/// A single word element and its transform state within a kinetic phrase.
/// </summary>
public sealed class KineticWordItem
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public FrameworkElement Element { get; set; } = null!;
    public ScaleTransform? Scale { get; set; }
    public TranslateTransform? Translate { get; set; }
    public Point CenterOffset { get; set; }

    public FrameworkElement? AttachedIcon { get; set; }
    public ScaleTransform? IconScale { get; set; }
    public double BaselineShift { get; set; } = 0.0;
    public double TargetOpacity { get; set; } = 1.0;

    public bool Triggered { get; set; } = false;
    public double TriggerTimeSec { get; set; } = 0.0;
    public double Progress { get; set; } = 0.0;
}

/// <summary>
/// A kinetic typography phrase visual and its layout container.
/// </summary>
public sealed class KineticPhraseVisual
{
    public FrameworkElement Container { get; set; } = null!;
    public Point WorldAnchor { get; set; }
    public Size VisualSize { get; set; }
    public KineticTemplateType TemplateType { get; set; }
    public int LineIndex { get; set; }
    public TimeSpan LineStartTime { get; set; }
    public TimeSpan LineEndTime { get; set; }
    public List<KineticWordItem> Words { get; set; } = new();

    public bool[] WordTriggered { get; set; } = Array.Empty<bool>();
    public double[] WordProgress { get; set; } = Array.Empty<double>();
    public bool IsExiting { get; set; } = false;

    public double BaseZoom { get; set; } = 1.0;
    public double TargetCameraAngle { get; set; } = 0.0;

    public bool IsPosterMode { get; set; } = false;
    public Color BackdropColor { get; set; } = Color.FromRgb(9, 9, 11);
    public bool TriggerZoomPunch { get; set; } = false;

    public bool HasPortalLetter { get; set; } = false;
    public Point PortalWorldCenter { get; set; }

    public ScaleTransform? ContainerScale { get; set; }
    public double CurrentContainerScale { get; set; } = 1.0;

    public Action<TimeSpan, double, double>? TemplateCustomUpdate { get; set; }

    public void CatchUpToTime(TimeSpan effectivePos)
    {
        if (WordTriggered.Length != Words.Count) WordTriggered = new bool[Words.Count];
        if (WordProgress.Length != Words.Count) WordProgress = new double[Words.Count];

        for (int i = 0; i < Words.Count; i++)
        {
            var w = Words[i];
            if (w.StartTime <= effectivePos)
            {
                WordTriggered[i] = true;
                WordProgress[i] = 1.0;
                w.Triggered = true;
                w.Progress = 1.0;

                w.Element.Opacity = w.TargetOpacity;
                if (w.Translate != null) w.Translate.Y = w.BaselineShift;
                if (w.Scale != null)
                {
                    w.Scale.ScaleX = 1.0;
                    w.Scale.ScaleY = 1.0;
                }
                if (w.AttachedIcon != null)
                {
                    w.AttachedIcon.Opacity = 1.0;
                    if (w.IconScale != null)
                    {
                        w.IconScale.ScaleX = 1.0;
                        w.IconScale.ScaleY = 1.0;
                    }
                }
            }
        }
    }

    public void Update(TimeSpan effectivePos, double dt, double currentTimeSec, double bassEnergy, double beatImpulse)
    {
        // Smooth container emergence scale (0.90 -> 1.0)
        if (CurrentContainerScale < 0.999)
        {
            CurrentContainerScale += (1.0 - CurrentContainerScale) * Math.Min(1.0, 16.0 * dt);
            if (ContainerScale != null)
            {
                ContainerScale.ScaleX = CurrentContainerScale;
                ContainerScale.ScaleY = CurrentContainerScale;
            }
        }

        TemplateCustomUpdate?.Invoke(effectivePos, dt, bassEnergy);

        if (WordTriggered.Length != Words.Count) WordTriggered = new bool[Words.Count];
        if (WordProgress.Length != Words.Count) WordProgress = new double[Words.Count];

        // 45ms vocal lead-in so words land right on beat
        var leadIn = TimeSpan.FromMilliseconds(45);

        for (int i = 0; i < Words.Count; i++)
        {
            var w = Words[i];

            if (effectivePos >= (w.StartTime - leadIn))
            {
                if (!WordTriggered[i])
                {
                    WordTriggered[i] = true;
                    w.Triggered = true;
                    w.TriggerTimeSec = currentTimeSec;
                }

                double elapsed = Math.Max(0.0, currentTimeSec - w.TriggerTimeSec);
                // Fluid duration between 220ms and 320ms based on word duration
                double enterDur = Math.Clamp((w.EndTime - w.StartTime).TotalSeconds * 0.85, 0.22, 0.32);

                double rawT = Math.Clamp(elapsed / enterDur, 0.0, 1.0);
                // Ease-out cubic curve: 1 - (1 - t)^3
                double easeT = 1.0 - Math.Pow(1.0 - rawT, 3.0);

                // Monotonic progress latching: progress never decreases
                WordProgress[i] = Math.Max(WordProgress[i], easeT);
                double p = WordProgress[i];
                w.Progress = p;

                w.Element.Opacity = p * w.TargetOpacity;

                if (w.Translate != null)
                {
                    w.Translate.Y = (16.0 * (1.0 - p)) + (w.BaselineShift * p);
                }

                if (w.Scale != null)
                {
                    double curScale = 0.92 + (0.08 * p);
                    w.Scale.ScaleX = curScale;
                    w.Scale.ScaleY = curScale;
                }

                if (w.AttachedIcon != null)
                {
                    w.AttachedIcon.Opacity = p;
                    if (w.IconScale != null)
                    {
                        double curIconScale = 0.82 + (0.18 * p);
                        w.IconScale.ScaleX = curIconScale;
                        w.IconScale.ScaleY = curIconScale;
                    }
                }
            }
            else if (!WordTriggered[i])
            {
                w.Element.Opacity = 0.0;
                if (w.Translate != null) w.Translate.Y = 16.0;
                if (w.Scale != null)
                {
                    w.Scale.ScaleX = 0.92;
                    w.Scale.ScaleY = 0.92;
                }
                if (w.AttachedIcon != null)
                {
                    w.AttachedIcon.Opacity = 0.0;
                    if (w.IconScale != null)
                    {
                        w.IconScale.ScaleX = 0.82;
                        w.IconScale.ScaleY = 0.82;
                    }
                }
            }
        }
    }

    public void SetDimmed(double opacity)
    {
        Container.Opacity = Math.Clamp(opacity, 0.0, 1.0);
    }

    public KineticWordItem? GetActiveWord(TimeSpan effectivePos)
    {
        if (Words.Count == 0) return null;

        for (int i = 0; i < Words.Count; i++)
        {
            if (effectivePos >= Words[i].StartTime && effectivePos <= Words[i].EndTime)
            {
                return Words[i];
            }
        }

        KineticWordItem? latest = null;
        for (int i = 0; i < Words.Count; i++)
        {
            if (effectivePos >= Words[i].StartTime)
            {
                latest = Words[i];
            }
        }
        return latest ?? Words[0];
    }

    public Point GetWordWorldCenter(KineticWordItem word)
    {
        double halfW = VisualSize.Width * 0.5;
        double halfH = VisualSize.Height * 0.5;
        double dx = word.CenterOffset.X - halfW;
        double dy = word.CenterOffset.Y - halfH;

        if (Math.Abs(TargetCameraAngle) > 0.1)
        {
            double rad = TargetCameraAngle * Math.PI / 180.0;
            double rotX = (dx * Math.Cos(rad)) - (dy * Math.Sin(rad));
            double rotY = (dx * Math.Sin(rad)) + (dy * Math.Cos(rad));
            return new Point(WorldAnchor.X + rotX, WorldAnchor.Y + rotY);
        }

        return new Point(WorldAnchor.X + dx, WorldAnchor.Y + dy);
    }
}
