using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricalWidget.Media;

namespace LyricalWidget.Rendering;

/// <summary>
/// Helpers for lyric wave animations, gradient stops, and line measurements.
/// </summary>
public static class FluidLyricWaveHelper
{
    public const double DelayPerLine = 0.065; // 65ms wave delay in Fluid mode

    public static void TriggerStaggeredWaveOffsets(List<LyricLineVisualItem> visuals, int activeIndex, double currentTimeSec)
    {
        for (int i = 0; i < visuals.Count; i++)
        {
            int dist = Math.Abs(i - activeIndex);
            double delay = Math.Min(dist, 4) * DelayPerLine;
            visuals[i].StartTimeOffset = currentTimeSec + delay;
            visuals[i].IsSettled = false;
        }
    }

    public static void UpdateInactiveWords(LyricLineVisualItem line, bool isPast, TimeSpan effectivePos, double dt)
    {
        if (line.Words == null) return;

        double targetOffset = isPast ? 1.0 : 0.0;

        foreach (var w in line.Words)
        {
            if (w.StopActive2.Offset != targetOffset) w.StopActive2.Offset = targetOffset;
            if (w.StopInactive1.Offset != targetOffset) w.StopInactive1.Offset = targetOffset;

            if (w.IsSettled && Math.Abs(w.CurrentTranslateY) < 0.01 && Math.Abs(w.CurrentScale - 1.0) < 0.01 && w.CurrentGlowOpacity < 0.01) continue;

            w.CurrentTranslateY += (0.0 - w.CurrentTranslateY) * Math.Min(1.0, 18.0 * dt);
            w.CurrentScale += (1.0 - w.CurrentScale) * Math.Min(1.0, 18.0 * dt);
            w.CurrentGlowOpacity += (0.0 - w.CurrentGlowOpacity) * Math.Min(1.0, 18.0 * dt);

            w.Transform.Y = w.CurrentTranslateY;
            w.ScaleTransform.ScaleX = w.CurrentScale;
            w.ScaleTransform.ScaleY = w.CurrentScale;
            w.GlowEffect.Opacity = w.CurrentGlowOpacity;

            w.IsSettled = Math.Abs(w.CurrentTranslateY) < 0.05 && Math.Abs(w.CurrentScale - 1.0) < 0.01 && w.CurrentGlowOpacity < 0.01;
        }
    }

    public static bool UpdateWordWave(LyricLineVisualItem line, TimeSpan effectivePos, double dt, bool isGamingMode)
    {
        if (line.Words == null || line.Words.Count == 0) return true;

        bool allWordsSettled = true;
        double nowMs = effectivePos.TotalMilliseconds;

        double lineStartMs = line.Model != null ? line.Model.Timestamp.TotalMilliseconds : 0.0;
        double lineEndMs = line.LineEndMs > lineStartMs
            ? line.LineEndMs
            : (line.Model != null && line.Model.EndTime.TotalMilliseconds > lineStartMs
                ? line.Model.EndTime.TotalMilliseconds
                : lineStartMs + 3500.0);
        bool hasWordSync = line.Model != null && line.Model.HasWordSync;

        int n = line.Words.Count;
        double spread = Math.Min(3.0, Math.Max(1.0, n * 0.85)); // Keeps ~3 adjacent words filling simultaneously
        double lineDur = Math.Max(350.0, lineEndMs - lineStartMs);
        double t = Math.Clamp((nowMs - lineStartMs) / (lineDur * 0.94), 0.0, 1.0);
        double wavePos = t * ((n - 1) + spread);

        for (int i = 0; i < n; i++)
        {
            var w = line.Words[i];

            double p;
            if (!hasWordSync)
            {
                p = Math.Clamp((wavePos - i) / spread, 0.0, 1.0);
            }
            else
            {
                double wordStartMs = w.Model != null ? w.Model.Timestamp.TotalMilliseconds : lineStartMs;
                double wordEndMs = w.Model != null ? w.Model.EndTime.TotalMilliseconds : lineEndMs;

                double targetEndMs;
                if (i + 2 < n && line.Words[i + 2].Model != null)
                {
                    targetEndMs = line.Words[i + 2].Model!.EndTime.TotalMilliseconds;
                }
                else
                {
                    targetEndMs = Math.Max(wordEndMs + 350.0, lineEndMs);
                }

                double sweepMs = Math.Clamp(targetEndMs - wordStartMs, 450.0, 2400.0);
                p = Math.Clamp((nowMs - wordStartMs) / sweepMs, 0.0, 1.0);
            }

            if (p <= 0.001)
            {
                if (w.StopActive2.Offset != 0.0) w.StopActive2.Offset = 0.0;
                if (w.StopInactive1.Offset != 0.0) w.StopInactive1.Offset = 0.0;
            }
            else if (p >= 0.999)
            {
                if (w.StopActive2.Offset != 1.0) w.StopActive2.Offset = 1.0;
                if (w.StopInactive1.Offset != 1.0) w.StopInactive1.Offset = 1.0;
            }
            else
            {
                w.StopActive2.Offset = Math.Max(0.0, p - 0.07);
                w.StopInactive1.Offset = Math.Min(1.0, p + 0.07);
            }

            double waveSin = (p > 0.0 && p < 1.0) ? Math.Sin(p * Math.PI) : 0.0;
            double targetY = -waveSin * 3.5;
            double targetScale = 1.0 + (0.045 * waveSin);
            double targetGlow = isGamingMode ? 0.0 : 0.75 * waveSin;

            w.CurrentTranslateY += (targetY - w.CurrentTranslateY) * Math.Min(1.0, 20.0 * dt);
            w.CurrentScale += (targetScale - w.CurrentScale) * Math.Min(1.0, 20.0 * dt);
            w.Transform.Y = w.CurrentTranslateY;
            w.ScaleTransform.ScaleX = w.CurrentScale;
            w.ScaleTransform.ScaleY = w.CurrentScale;

            if (isGamingMode)
            {
                w.CurrentGlowOpacity = 0.0;
                if (w.GlowEffect.Opacity != 0.0) w.GlowEffect.Opacity = 0.0;
            }
            else
            {
                w.CurrentGlowOpacity += (targetGlow - w.CurrentGlowOpacity) * Math.Min(1.0, 16.0 * dt);
                w.GlowEffect.Opacity = w.CurrentGlowOpacity;
            }

            w.IsSettled = (p <= 0.001 || p >= 0.999) && Math.Abs(w.CurrentTranslateY) < 0.05 && Math.Abs(w.CurrentScale - 1.0) < 0.01;
            if (!w.IsSettled) allWordsSettled = false;
        }

        return allWordsSettled;
    }

    public static void ComputeStaticLayoutForList(
        List<LyricLineVisualItem> visuals,
        double viewportWidth,
        InterludeSlotItem? interludeSlot,
        double lineGap = 26.0,
        double interludeHeight = 36.0,
        double interludeGap = 28.0,
        double minHeight = 28.0)
    {
        double accumulatedY = 0;

        for (int i = 0; i < visuals.Count; i++)
        {
            var item = visuals[i];

            item.Container.Width = viewportWidth;
            item.Container.Measure(new Size(viewportWidth, double.PositiveInfinity));
            item.MeasuredHeight = Math.Max(minHeight, item.Container.DesiredSize.Height);

            if (item.HasPrecedingInterlude && interludeSlot != null)
            {
                if (i == 0 || accumulatedY == 0)
                {
                    interludeSlot.StaticTop = 0;
                }
                else
                {
                    interludeSlot.StaticTop = accumulatedY;
                }
                accumulatedY += interludeHeight + interludeGap;
            }

            item.StaticTop = accumulatedY;
            item.StaticCenter = accumulatedY + (item.MeasuredHeight / 2.0);
            accumulatedY += item.MeasuredHeight + lineGap;
        }
    }

    public static void ApplyLyricColorsToVisuals(
        List<LyricLineVisualItem> visuals,
        ThemedLyricColors colors,
        bool crossfade,
        bool isGamingMode = false)
    {
        TimeSpan duration = (crossfade && !isGamingMode) ? TimeSpan.FromMilliseconds(600) : TimeSpan.Zero;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        foreach (var line in visuals)
        {
            if (line.Words != null)
            {
                foreach (var w in line.Words)
                {
                    if (crossfade && !isGamingMode)
                    {
                        var animActive = new ColorAnimation(colors.ActiveColor, duration) { EasingFunction = ease };
                        var animInactive = new ColorAnimation(colors.InactiveColor, duration) { EasingFunction = ease };
                        w.StopActive1.BeginAnimation(GradientStop.ColorProperty, animActive);
                        w.StopActive2.BeginAnimation(GradientStop.ColorProperty, animActive);
                        w.StopInactive1.BeginAnimation(GradientStop.ColorProperty, animInactive);
                        w.StopInactive2.BeginAnimation(GradientStop.ColorProperty, animInactive);
                    }
                    else
                    {
                        w.StopActive1.BeginAnimation(GradientStop.ColorProperty, null);
                        w.StopActive2.BeginAnimation(GradientStop.ColorProperty, null);
                        w.StopInactive1.BeginAnimation(GradientStop.ColorProperty, null);
                        w.StopInactive2.BeginAnimation(GradientStop.ColorProperty, null);
                        w.StopActive1.Color = colors.ActiveColor;
                        w.StopActive2.Color = colors.ActiveColor;
                        w.StopInactive1.Color = colors.InactiveColor;
                        w.StopInactive2.Color = colors.InactiveColor;
                    }

                    if (isGamingMode)
                    {
                        w.GlowEffect.Opacity = 0.0;
                    }
                    else
                    {
                        w.GlowEffect.Color = colors.AccentGlowColor;
                    }
                }
            }

            if (line.LineGlowEffect != null)
            {
                if (isGamingMode)
                {
                    line.LineGlowEffect.Opacity = 0.0;
                }
                else
                {
                    line.LineGlowEffect.Color = colors.AccentGlowColor;
                }
            }

            if (line.IndicatorBar != null)
            {
                if (line.IndicatorBar.Background is SolidColorBrush sb && !sb.IsFrozen)
                {
                    if (crossfade && !isGamingMode)
                    {
                        sb.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(colors.ActiveColor, duration) { EasingFunction = ease });
                    }
                    else
                    {
                        sb.BeginAnimation(SolidColorBrush.ColorProperty, null);
                        sb.Color = colors.ActiveColor;
                    }
                }
                else
                {
                    line.IndicatorBar.Background = new SolidColorBrush(colors.ActiveColor);
                }

                if (line.IndicatorGlow != null)
                {
                    if (isGamingMode)
                    {
                        line.IndicatorGlow.Opacity = 0.0;
                    }
                    else
                    {
                        line.IndicatorGlow.Color = colors.AccentGlowColor;
                    }
                }
            }
        }
    }
}
