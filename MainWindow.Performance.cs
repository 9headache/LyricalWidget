using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LyricalWidget.Rendering;

namespace LyricalWidget;

// Game Mode budget. Software rendering still runs an effect whose opacity is zero, over everything under it,
// every time that area repaints, so in Game Mode the effects are taken off the elements instead of faded out.
public partial class MainWindow
{
    private readonly ConditionalWeakTable<UIElement, Effect> _parkedEffects = new();
    private DispatcherTimer? _clockWakeTimer;
    private int _shownClockMinute = -1;

    private void ApplyEffectBudget()
    {
        bool lean = _settings.GamingMode;

        SetEffectParked(CardBorder, lean);
        SetEffectParked(SideAlbumBorder, lean);
        SetEffectParked(AlbumOnlyBorder, lean);
        SetEffectParked(TrackSwitchBanner, lean);
        SetEffectParked(IdleClockRoot, lean);

        ParkLineEffects(_lyricsOnlyVisuals, lean);
        ParkLineEffects(_sideLyricsVisuals, lean);
        ParkOrbEffects(_lyricsOnlyInterludeSlot, lean);
        ParkOrbEffects(_sideLyricsInterludeSlot, lean);
    }

    private void ParkLineEffects(List<LyricLineVisualItem> lines, bool park)
    {
        foreach (var line in lines)
        {
            if (line.IndicatorBar != null) SetEffectParked(line.IndicatorBar, park);
            if (line.Words == null) continue;
            foreach (var word in line.Words)
            {
                // The fill animation keeps writing to GlowEffect, so it only has to be attached again
                word.Element.Effect = park ? null : word.GlowEffect;
            }
        }
    }

    private void ParkOrbEffects(InterludeSlotItem? slot, bool park)
    {
        if (slot == null) return;
        foreach (var dot in new[] { slot.Dot1, slot.Dot2, slot.Dot3 })
        {
            SetEffectParked(dot, park);
            // Accent changes skip parked orbs
            if (!park && dot.Effect is DropShadowEffect glow) glow.Color = _currentAccentColor;
        }
    }

    private void SetEffectParked(UIElement element, bool park)
    {
        if (park)
        {
            if (element.Effect == null) return;
            _parkedEffects.AddOrUpdate(element, element.Effect);
            element.Effect = null;
        }
        else if (element.Effect == null && _parkedEffects.TryGetValue(element, out var effect))
        {
            element.Effect = effect;
            _parkedEffects.Remove(element);
        }
    }

    /// <summary>
    /// The idle clock only changes once a minute. Between its digit rolls the frame loop is switched off,
    /// and a timer wakes it for the next minute.
    /// </summary>
    private void RestClockUntilNextMinute()
    {
        StopRenderingLoop();

        _clockWakeTimer ??= CreateClockWakeTimer();
        var now = DateTime.Now;
        var nextMinute = now.AddTicks(TimeSpan.TicksPerMinute - (now.Ticks % TimeSpan.TicksPerMinute));
        _clockWakeTimer.Stop();
        _clockWakeTimer.Interval = (nextMinute - now) + TimeSpan.FromMilliseconds(30);
        _clockWakeTimer.Start();
    }

    private DispatcherTimer CreateClockWakeTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (_isClosing || !_isInIdleClockMode) return;
            EnsureRenderingSubscribed();
        };
        return timer;
    }
}
