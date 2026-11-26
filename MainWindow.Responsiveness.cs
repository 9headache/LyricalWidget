using System;
using System.Windows;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// Keeps dragging and resizing smooth: per-pixel events only do cheap work, and text layout and
// settings writes wait until things have been still for a moment.
public partial class MainWindow
{
    private DispatcherTimer? _layoutSettleTimer;
    private DispatcherTimer? _saveTimer;
    private bool _isResizingWidget = false;

    private void InitResponsiveness()
    {
        _layoutSettleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
        _layoutSettleTimer.Tick += (s, e) => OnLayoutSettled();

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (s, e) => FlushSettings();

        SizeChanged += (s, e) => ScheduleLayoutRefresh();
        ResizeGripThumb.DragCompleted += (s, e) => OnMoveOrResizeEnded();
    }

    /// <summary>Called for every size change: the text is re-wrapped once the size has settled.</summary>
    private void ScheduleLayoutRefresh()
    {
        if (_layoutSettleTimer == null) return;
        if (!_isResizingWidget)
        {
            _isResizingWidget = true;
            // Moonlight Bloom rescales its current line instead of reflowing it on every tick
            _lyricsOnlyMoonlight.HoldLayout = true;
            _sideMoonlight.HoldLayout = true;
        }
        _layoutSettleTimer.Stop();
        _layoutSettleTimer.Start();
    }

    private void OnLayoutSettled()
    {
        _layoutSettleTimer?.Stop();
        _isResizingWidget = false;
        _lyricsOnlyMoonlight.HoldLayout = false;
        _sideMoonlight.HoldLayout = false;
        if (_isClosing) return;
        OnWindowSizeChanged(this, null!);
    }

    private void ScheduleSettingsSave()
    {
        if (_saveTimer == null)
        {
            FlushSettings();
            return;
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSettings()
    {
        _saveTimer?.Stop();
        SettingsService.Save(_settings);
        if (!_isClosing) UpdatePresetHighlight();
    }
}
