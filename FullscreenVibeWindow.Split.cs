using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LyricalWidget.Lyrics;
using LyricalWidget.Media;
using LyricalWidget.Rendering;

namespace LyricalWidget;

public partial class FullscreenVibeWindow : Window
{
    private static readonly SpringDamper SpringLineFluid = SpringDamper.LineFluid;

    private readonly List<LyricLineVisualItem> _vibeVisuals = new();
    private InterludeSlotItem? _interludeSlot;
    private int _currentActiveIndex = -1;
    private TimeSpan _lastVibeEffectivePos = TimeSpan.Zero;
    private double _activeLineChangedTime = 0;

    private void UpdateSplitFrame(
        double dt,
        long nowTicks,
        TimeSpan effectivePos,
        int activeIndex,
        double currentTimeSec)
    {
        if (_vibeVisuals.Count == 0) return;

        if (_lastVibeEffectivePos != TimeSpan.Zero)
        {
            TimeSpan posDelta = effectivePos - _lastVibeEffectivePos;
            if (Math.Abs(posDelta.TotalMilliseconds) > 250)
            {
                _currentActiveIndex = -999;
                ResetVisualsOnSeek(_vibeVisuals, effectivePos);
            }
        }
        _lastVibeEffectivePos = effectivePos;

        if (activeIndex != _currentActiveIndex)
        {
            _currentActiveIndex = activeIndex;
            _activeLineChangedTime = currentTimeSec;
            TriggerStaggeredWaveOffsets(_vibeVisuals, activeIndex, currentTimeSec);
        }

        double vh = _isPortraitMode ? PortraitLyricsViewport.ActualHeight : LyricsViewport.ActualHeight;
        if (vh <= 10) vh = 600;

        SimulateBeautifulLyrics(
            _vibeVisuals,
            activeIndex,
            effectivePos,
            vh,
            dt,
            currentTimeSec,
            _interludeSlot);
    }

    private void ApplyLyricColorsToVisuals(List<LyricLineVisualItem> visuals, ThemedLyricColors colors, bool crossfade)
    {
        FluidLyricWaveHelper.ApplyLyricColorsToVisuals(visuals, colors, crossfade, _settings.GamingMode);
    }

    private static void UpdateVisualsAccentColor(List<LyricLineVisualItem> visuals, InterludeSlotItem? slot, Color accent)
    {
        foreach (var line in visuals)
        {
            if (line.Words != null)
            {
                foreach (var w in line.Words)
                {
                    w.GlowEffect.Color = accent;
                }
            }
        }
        if (slot != null)
        {
            if (slot.Dot1.Effect is DropShadowEffect d1) d1.Color = accent;
            if (slot.Dot2.Effect is DropShadowEffect d2) d2.Color = accent;
            if (slot.Dot3.Effect is DropShadowEffect d3) d3.Color = accent;
        }
    }

    private void TriggerStaggeredWaveOffsets(List<LyricLineVisualItem> visuals, int activeIndex, double currentTimeSec)
    {
        FluidLyricWaveHelper.TriggerStaggeredWaveOffsets(visuals, activeIndex, currentTimeSec);
    }

    private bool SimulateBeautifulLyrics(
        List<LyricLineVisualItem> visuals,
        int activeIndex,
        TimeSpan effectivePos,
        double viewportHeight,
        double dt,
        double currentTimeSec,
        InterludeSlotItem? interludeSlot)
    {
        if (visuals.Count == 0 || viewportHeight <= 10) return true;

        var springLine = SpringLineFluid;

        double anchorRatio = _isPortraitMode ? 0.40 : 0.42;
        double targetCenterY = viewportHeight * anchorRatio;

        int refIndex = Math.Clamp(activeIndex >= 0 ? activeIndex : 0, 0, visuals.Count - 1);
        double targetTop_A = targetCenterY - (visuals[refIndex].MeasuredHeight / 2.0);

        int minVisibleIdx = Math.Max(0, activeIndex - 5);
        int maxVisibleIdx = Math.Min(visuals.Count - 1, activeIndex + 5);

        bool allSettled = true;
        int numSubSteps = Math.Clamp((int)Math.Round(dt / 0.016), 1, 8);
        double subDt = dt / numSubSteps;

        for (int i = 0; i < visuals.Count; i++)
        {
            var line = visuals[i];

            if (i < minVisibleIdx || i > maxVisibleIdx)
            {
                if (line.Container.Visibility != Visibility.Collapsed)
                {
                    line.Container.Visibility = Visibility.Collapsed;
                    line.CurrentOpacity = 0.0;
                }
                line.IsSettled = true;
                continue;
            }

            int dist = Math.Abs(i - activeIndex);
            line.TargetY = targetTop_A + (line.StaticTop - visuals[refIndex].StaticTop);

            double targetOpacity = 0.0;
            double targetScale = 0.84;

            if (i == activeIndex)
            {
                targetOpacity = 1.0;
                targetScale = 1.045;
            }
            else if (dist <= 4)
            {
                if (dist == 1)
                {
                    targetOpacity = 0.38;
                    targetScale = 0.94;
                }
                else if (dist == 2)
                {
                    targetOpacity = 0.18;
                    targetScale = 0.88;
                }
                else if (dist == 3)
                {
                    targetOpacity = 0.08;
                    targetScale = 0.84;
                }
            }

            if (currentTimeSec >= line.StartTimeOffset)
            {
                bool settledY;
                if (dt > 0.25)
                {
                    line.CurrentY = line.TargetY;
                    line.VelocityY = 0.0;
                    line.CurrentOpacity = targetOpacity;
                    line.CurrentScale = targetScale;
                    settledY = true;
                }
                else
                {
                    double curY = line.CurrentY;
                    double vY = line.VelocityY;
                    settledY = false;
                    for (int s = 0; s < numSubSteps; s++)
                    {
                        settledY = springLine.Step(ref curY, ref vY, line.TargetY, subDt, 0.05);
                    }
                    line.CurrentY = curY;
                    line.VelocityY = vY;

                    line.CurrentOpacity += (targetOpacity - line.CurrentOpacity) * Math.Min(1.0, 14.0 * dt);
                    line.CurrentScale += (targetScale - line.CurrentScale) * Math.Min(1.0, 14.0 * dt);
                }

                bool settledOpacity = Math.Abs(targetOpacity - line.CurrentOpacity) < 0.01;
                bool settledScale = Math.Abs(targetScale - line.CurrentScale) < 0.01;
                line.IsSettled = settledY && settledOpacity && settledScale;

                line.TranslateTransform.Y = line.CurrentY;
                line.Container.Opacity = line.CurrentOpacity;
                line.ScaleTransform.ScaleX = line.CurrentScale;
                line.ScaleTransform.ScaleY = line.CurrentScale;

                if (line.CurrentTranslateX != 0)
                {
                    line.CurrentTranslateX = 0;
                    line.TranslateTransform.X = 0;
                }
                if (line.IndicatorBar != null && line.IndicatorBar.Opacity > 0)
                {
                    line.IndicatorBar.Opacity = 0.0;
                }

                if (line.Container.Effect != null)
                {
                    line.Container.Effect = null;
                }

                if (line.CurrentOpacity <= 0.005)
                {
                    if (line.Container.Visibility != Visibility.Collapsed)
                        line.Container.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (line.Container.Visibility != Visibility.Visible)
                        line.Container.Visibility = Visibility.Visible;
                }
            }
            else
            {
                line.IsSettled = false;
            }

            if (!line.IsSettled) allSettled = false;

            if (i == activeIndex && line.Words != null && line.Words.Count > 0)
            {
                bool wordsSettled = UpdateWordWave(line, effectivePos, dt);
                if (!wordsSettled) allSettled = false;
            }
            else if (line.Words != null && line.Words.Count > 0)
            {
                UpdateInactiveWords(line, isPast: i < activeIndex, effectivePos, dt);
            }
        }

        bool interludeActive = UpdateInterludeOrbs(activeIndex, effectivePos, visuals, targetTop_A, dt, currentTimeSec, interludeSlot);
        if (interludeActive) allSettled = false;

        return allSettled;
    }

    private bool UpdateWordWave(LyricLineVisualItem line, TimeSpan effectivePos, double dt, bool isFluid = true)
    {
        return FluidLyricWaveHelper.UpdateWordWave(line, effectivePos, dt, _settings.GamingMode);
    }

    private static void ResetVisualsOnSeek(List<LyricLineVisualItem> visuals, TimeSpan effectivePos)
    {
        double nowMs = effectivePos.TotalMilliseconds;
        foreach (var line in visuals)
        {
            line.VelocityY = 0.0;
            line.CurrentTranslateX = 0.0;
            line.TranslateTransform.X = 0.0;
            if (line.Words != null && line.Words.Count > 0)
            {
                double lineStartMs = line.Model != null ? line.Model.Timestamp.TotalMilliseconds : 0.0;
                double lineEndMs = line.LineEndMs > lineStartMs
                    ? line.LineEndMs
                    : (line.Model != null && line.Model.EndTime.TotalMilliseconds > lineStartMs
                        ? line.Model.EndTime.TotalMilliseconds
                        : lineStartMs + 3500.0);
                bool hasWordSync = line.Model != null && line.Model.HasWordSync;

                int n = line.Words.Count;
                double spread = Math.Min(3.0, Math.Max(1.0, n * 0.85));
                double lineDur = Math.Max(350.0, lineEndMs - lineStartMs);
                double t = Math.Clamp((nowMs - lineStartMs) / (lineDur * 0.94), 0.0, 1.0);
                double wavePos = t * ((n - 1) + spread);

                for (int i = 0; i < n; i++)
                {
                    var w = line.Words[i];
                    w.VelocityY = 0.0;
                    w.VelocityScale = 0.0;
                    w.VelocityGlow = 0.0;
                    w.CurrentTranslateY = 0.0;
                    w.CurrentScale = 1.0;
                    w.CurrentGlowOpacity = 0.0;
                    w.Transform.Y = 0.0;
                    w.ScaleTransform.ScaleX = 1.0;
                    w.ScaleTransform.ScaleY = 1.0;
                    w.GlowEffect.Opacity = 0.0;

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
                        w.StopActive2.Offset = 0.0;
                        w.StopInactive1.Offset = 0.0;
                    }
                    else if (p >= 0.999)
                    {
                        w.StopActive2.Offset = 1.0;
                        w.StopInactive1.Offset = 1.0;
                    }
                    else
                    {
                        w.StopActive2.Offset = Math.Max(0.0, p - 0.07);
                        w.StopInactive1.Offset = Math.Min(1.0, p + 0.07);
                    }
                    w.IsSettled = true;
                }
            }
        }
    }

    private void UpdateInactiveWords(LyricLineVisualItem line, bool isPast, TimeSpan effectivePos, double dt)
    {
        FluidLyricWaveHelper.UpdateInactiveWords(line, isPast, effectivePos, dt);
    }

    private bool UpdateInterludeOrbs(
        int activeIndex,
        TimeSpan effectivePos,
        List<LyricLineVisualItem> visuals,
        double targetTop_A,
        double dt,
        double currentTimeSec,
        InterludeSlotItem? interludeSlot)
    {
        if (interludeSlot == null || visuals.Count == 0) return false;

        LyricLineVisualItem? targetLine = null;
        for (int i = 0; i < visuals.Count; i++)
        {
            if (visuals[i].HasPrecedingInterlude)
            {
                if (effectivePos >= (visuals[i].InterludeStart - TimeSpan.FromMilliseconds(500)) &&
                    effectivePos < visuals[i].InterludeEnd)
                {
                    targetLine = visuals[i];
                    break;
                }
            }
        }

        bool isInterludeActive = targetLine != null;
        interludeSlot.IsActive = isInterludeActive;

        if (isInterludeActive && targetLine != null)
        {
            interludeSlot.Container.Visibility = Visibility.Visible;
            double targetY = targetTop_A + (interludeSlot.StaticTop - visuals[Math.Clamp(activeIndex, 0, visuals.Count - 1)].StaticTop);

            double curY = interludeSlot.CurrentY;
            double vY = interludeSlot.VelocityY;
            SpringLineFluid.Step(ref curY, ref vY, targetY, dt, 0.05);
            interludeSlot.CurrentY = curY;
            interludeSlot.VelocityY = vY;

            interludeSlot.CurrentOpacity += (1.0 - interludeSlot.CurrentOpacity) * Math.Min(1.0, 10.0 * dt);
            interludeSlot.Container.Opacity = interludeSlot.CurrentOpacity;

            double time = currentTimeSec * 3.8;
            double b1 = Math.Sin(time);
            double b2 = Math.Sin(time - 0.7);
            double b3 = Math.Sin(time - 1.4);

            interludeSlot.Dot1Transform.Y = -7.0 * Math.Max(0, b1);
            interludeSlot.Dot2Transform.Y = -7.0 * Math.Max(0, b2);
            interludeSlot.Dot3Transform.Y = -7.0 * Math.Max(0, b3);

            double sc1 = 1.0 + 0.22 * Math.Max(0, b1);
            double sc2 = 1.0 + 0.22 * Math.Max(0, b2);
            double sc3 = 1.0 + 0.22 * Math.Max(0, b3);

            interludeSlot.Dot1Scale.ScaleX = sc1; interludeSlot.Dot1Scale.ScaleY = sc1;
            interludeSlot.Dot2Scale.ScaleX = sc2; interludeSlot.Dot2Scale.ScaleY = sc2;
            interludeSlot.Dot3Scale.ScaleX = sc3; interludeSlot.Dot3Scale.ScaleY = sc3;

            interludeSlot.Dot1.Opacity = 0.35 + 0.65 * Math.Max(0, b1);
            interludeSlot.Dot2.Opacity = 0.35 + 0.65 * Math.Max(0, b2);
            interludeSlot.Dot3.Opacity = 0.35 + 0.65 * Math.Max(0, b3);

            return true;
        }
        else
        {
            if (interludeSlot.CurrentOpacity > 0.01)
            {
                interludeSlot.CurrentOpacity += (0.0 - interludeSlot.CurrentOpacity) * Math.Min(1.0, 14.0 * dt);
                interludeSlot.Container.Opacity = interludeSlot.CurrentOpacity;
                return true;
            }
            else
            {
                interludeSlot.Container.Visibility = Visibility.Collapsed;
                return false;
            }
        }
    }

    private static int FindCurrentLyricLineIndex(List<LyricLine> lines, TimeSpan currentPos)
    {
        if (lines.Count == 0) return -1;
        if (currentPos < lines[0].Timestamp) return -1;

        int low = 0;
        int high = lines.Count - 1;
        int result = -1;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (lines[mid].Timestamp <= currentPos)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result >= 0 ? result : 0;
    }
}
