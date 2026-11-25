using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// Hover dropdowns for the Mode, Style and Vibe pills
public partial class FullscreenVibeWindow
{
    private readonly DispatcherTimer _menuLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private Button? _openMenuPill;
    private DateTime _menuOpenedAt;

    private static readonly (KineticPreset Preset, string Label)[] StyleOptions =
    {
        (KineticPreset.MoonlightBloom, AppSettings.MoonlightBloomStyle),
        (KineticPreset.LiveCode, "LiveCode"),
        (KineticPreset.Editorial, "Editorial"),
        (KineticPreset.Poster, "Poster"),
        (KineticPreset.SpotlightFog, "SpotlightFog")
    };

    private static string GetPresetLabel(KineticPreset preset)
    {
        foreach (var option in StyleOptions)
        {
            if (option.Preset == preset) return option.Label;
        }
        return preset.ToString();
    }

    private void InitHoverMenus()
    {
        _menuLeaveTimer.Tick += (s, e) => CloseHoverMenu();

        foreach (var pill in new[] { BtnToggleLayoutMode, BtnToggleKineticPreset, BtnToggleVibe })
        {
            pill.MouseEnter += (s, e) => OpenHoverMenu(pill);
            pill.MouseLeave += (s, e) => StartMenuLeaveTimer();
            pill.Click += (s, e) =>
            {
                if (_openMenuPill != pill)
                {
                    OpenHoverMenu(pill);
                }
                // A touch tap or quick click hovers first; do not let the same press close what it just opened
                else if ((DateTime.UtcNow - _menuOpenedAt).TotalMilliseconds > 400)
                {
                    CloseHoverMenu();
                }
            };
        }

        HoverMenu.MouseEnter += (s, e) => _menuLeaveTimer.Stop();
        HoverMenu.MouseLeave += (s, e) => StartMenuLeaveTimer();
    }

    private void StartMenuLeaveTimer()
    {
        _menuLeaveTimer.Stop();
        _menuLeaveTimer.Start();
    }

    private void OpenHoverMenu(Button pill)
    {
        _menuLeaveTimer.Stop();
        if (_openMenuPill == pill) return;

        _openMenuPill = pill;
        _menuOpenedAt = DateTime.UtcNow;
        HoverMenuItems.Children.Clear();

        if (pill == BtnToggleLayoutMode)
        {
            AddMenuItem("Split", !_isKineticMode, () => ApplyLayoutMode(false));
            AddMenuItem("Kinetic", _isKineticMode, () => ApplyLayoutMode(true));
        }
        else if (pill == BtnToggleKineticPreset)
        {
            foreach (var (preset, label) in StyleOptions)
            {
                AddMenuItem(label, _isKineticMode && _currentKineticPreset == preset, () => SelectStyle(preset));
            }
        }
        else
        {
            foreach (string vibe in new[] { "Auto", "Calm", "Rap", "Pop" })
            {
                AddMenuItem(vibe, _kineticVibe == vibe, () => SetKineticVibe(vibe));
            }
        }

        // Positioned from scratch on every open. Canvas coordinates stay out of the menu's own measured size,
        // so nothing from the previous open can leak into this one.
        HoverMenu.Visibility = Visibility.Visible;
        HoverMenuOverlay.UpdateLayout();
        // The menu's transparent 6px top padding bridges the gap to the pill
        var origin = pill.TranslatePoint(new Point(0, pill.ActualHeight), HoverMenuOverlay);
        double maxLeft = HoverMenuOverlay.ActualWidth - HoverMenu.ActualWidth - 12.0;
        Canvas.SetLeft(HoverMenu, Math.Max(0.0, Math.Min(origin.X, maxLeft)));
        Canvas.SetTop(HoverMenu, origin.Y);
        HoverMenu.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120)));
    }

    private void CloseHoverMenu()
    {
        _menuLeaveTimer.Stop();
        if (_openMenuPill == null) return;

        _openMenuPill = null;
        HoverMenu.Visibility = Visibility.Collapsed;
        HoverMenuItems.Children.Clear();
        // Let the bar auto-hide again
        _modeBarTimer?.Stop();
        _modeBarTimer?.Start();
    }

    private void AddMenuItem(string label, bool isActive, Action select)
    {
        var item = new Button
        {
            Content = label,
            Tag = isActive ? "Active" : null,
            Style = (Style)FindResource("HoverMenuItem")
        };
        item.Click += (s, e) =>
        {
            CloseHoverMenu();
            select();
        };
        HoverMenuItems.Children.Add(item);
    }

    /// <summary>Styles belong to Kinetic mode, so picking one from Split switches over in the same click.</summary>
    private void SelectStyle(KineticPreset preset)
    {
        bool entersMoonlight = preset == KineticPreset.MoonlightBloom && !IsMoonlightActive;
        SetActiveKineticPreset(preset);
        if (!_isKineticMode) ApplyLayoutMode(true);
        else if (entersMoonlight) ShowPacingPill();
    }
}
