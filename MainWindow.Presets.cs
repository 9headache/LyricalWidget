using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LyricalWidget.Common;

namespace LyricalWidget;

// Presets: saved snapshots of every widget and Vibe setting, including where the widget sits
public partial class MainWindow
{
    private readonly List<(Button Button, WidgetPreset Preset)> _presetButtons = new();
    private string? _shownActivePreset;
    private int _presetRun;
    private bool _applyingPreset = false;

    private IEnumerable<WidgetPreset> AllPresets => _settings.Presets;

    private void RebuildPresetList()
    {
        PresetOptions.Children.Clear();
        _presetButtons.Clear();

        foreach (var preset in AllPresets)
        {
            var button = new Button
            {
                Content = preset.Name,
                Style = (Style)FindResource("ToolbarPillButton"),
                Margin = new Thickness(0, 0, 3, 4),
                ToolTip = "Load this preset"
            };
            button.Click += (s, e) => LoadPreset(preset.Name);
            _presetButtons.Add((button, preset));

            button.Margin = new Thickness(0);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 3, 4) };
            group.Children.Add(button);
            group.Children.Add(BuildDeleteButton(preset.Name));
            PresetOptions.Children.Add(group);
        }

        TxtNoPresets.Visibility = _settings.Presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool full = _settings.Presets.Count >= WidgetPreset.MaxUserPresets;
        BtnPresetSaveAs.ToolTip = full
            ? "All slots are used: save under an existing name to replace it, or delete one"
            : "Save the current setup as a preset";

        _shownActivePreset = null;
        UpdatePresetHighlight(force: true);
    }

    private Button BuildDeleteButton(string name)
    {
        var delete = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("ToolbarPillButton"),
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(1, 0, 0, 0),
            ToolTip = $"Delete \"{name}\""
        };
        // Two clicks, so a stray one cannot lose a preset
        var disarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        disarm.Tick += (s, e) => { disarm.Stop(); delete.Content = "✕"; };
        delete.Click += (s, e) =>
        {
            if (!disarm.IsEnabled)
            {
                delete.Content = "Delete?";
                disarm.Start();
                return;
            }
            disarm.Stop();
            _settings.Presets.RemoveAll(p => WidgetPreset.SameName(p.Name, name));
            SaveCurrentBounds();
            RebuildPresetList();
        };
        return delete;
    }

    private void UpdatePresetHighlight(bool force = false)
    {
        if (_applyingPreset) return;

        string? active = AllPresets.FirstOrDefault(p => p.State.Matches(_settings))?.Name;
        if (!force && active == _shownActivePreset) return;
        _shownActivePreset = active;

        foreach (var (button, preset) in _presetButtons)
        {
            bool isActive = preset.Name == active;
            button.Background = isActive ? PillActiveBrush : PillBrush;
            button.BorderBrush = isActive ? PillActiveBorder : PillBorder;
        }
        _trayService?.UpdatePresets(AllPresets.Select(p => p.Name).ToList(), active);
    }

    public void LoadPreset(string name)
    {
        var preset = AllPresets.FirstOrDefault(p => WidgetPreset.SameName(p.Name, name));
        if (preset == null) return;

        int run = ++_presetRun;
        if (!IsVisible || IsFullscreenActive)
        {
            LayoutRoot.BeginAnimation(OpacityProperty, null);
            ApplyPreset(preset.State);
            return;
        }

        // Everything changes while the widget is faded out, so nothing is seen jumping
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (s, e) =>
        {
            if (run != _presetRun || _isClosing) return;
            ApplyPreset(preset.State);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (run != _presetRun) return;
                LayoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }));
        };
        LayoutRoot.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ApplyPreset(WidgetState state)
    {
        _applyingPreset = true;
        try
        {
            // First: it compares with the style still in use, and it may move the Vibe preset
            SetAnimationStyle(state.AnimationStyle ?? _settings.AnimationStyle);
            state.ApplyTo(_settings);
            if (state.VibeLayoutMode != null) _settings.RestoreVibeLayout = true;
            SettingsService.Normalize(_settings);
            // The open Vibe window saves its own placement as it changes, so its targets are kept aside
            var target = WidgetState.Capture(_settings);

            SyncControlsToSettings();
            SetThemeColoredLyrics(_settings.ThemeColoredLyrics);
            SetAudioReactiveGlow(_settings.AudioReactiveGlow);
            SetBrowserMediaBehavior(_settings.BrowserMediaBehavior);
            SliderAlbumArt.Value = _settings.WidgetAlbumArtSize;
            PlaceWidget(new Rect(_settings.Left, _settings.Top, _settings.Width, _settings.Height));
            ApplyAlbumArtSize();

            _currentActiveIndex = -1;
            _lastRenderEffectivePos = TimeSpan.Zero;
            RebuildLyricsViews();
            ApplyDisplayModeAndTheme();

            if (_vibeWindow != null)
            {
                _vibeWindow.ApplyPreset(target);
                // Gaming Mode waits until the Vibe window closes, as it does when the window opens
                RenderOptions.ProcessRenderMode = RenderMode.Default;
                _audioBeatService.UpdateState(_mediaService.IsPlaying, false, _settings.AudioReactiveGlow);
            }
            else
            {
                UpdateAutoHideState(_mediaService.IsPlaying);
                EnsureRenderingSubscribed();
            }
            SyncTrayState();
            RefreshVibeMonitors();
        }
        finally
        {
            _applyingPreset = false;
        }
        SaveCurrentBounds();
        UpdatePresetHighlight(force: true);
    }

    private void OnPresetSaveAsClicked(object sender, RoutedEventArgs e)
    {
        BtnPresetSaveAs.Visibility = Visibility.Collapsed;
        PresetNamePanel.Visibility = Visibility.Visible;
        TxtPresetName.Text = _settings.Presets.Any(p => p.Name == _shownActivePreset) ? _shownActivePreset : "";
        OnPresetNameChanged(sender, e);

        // Keys reach a popup through its owner window, which has to be the active one
        Activate();
        TxtPresetName.Focus();
        Keyboard.Focus(TxtPresetName);
        TxtPresetName.SelectAll();
    }

    private void OnPresetNameChanged(object sender, RoutedEventArgs e)
    {
        string name = WidgetPreset.CleanName(TxtPresetName.Text);
        bool exists = _settings.Presets.Any(p => WidgetPreset.SameName(p.Name, name));
        bool full = _settings.Presets.Count >= WidgetPreset.MaxUserPresets;
        BtnPresetSave.Content = exists ? "Replace" : "Save";
        BtnPresetSave.IsEnabled = name.Length > 0 && (exists || !full);
        BtnPresetSave.Opacity = BtnPresetSave.IsEnabled ? 1.0 : 0.45;
    }

    private void OnPresetNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BtnPresetSave.IsEnabled) SavePreset(TxtPresetName.Text);
        else if (e.Key == Key.Escape) EndPresetNaming();
        else return;
        e.Handled = true;
    }

    private void OnPresetSaveClicked(object sender, RoutedEventArgs e) => SavePreset(TxtPresetName.Text);

    private void OnPresetCancelClicked(object sender, RoutedEventArgs e) => EndPresetNaming();

    private void SavePreset(string rawName)
    {
        string name = WidgetPreset.CleanName(rawName);
        if (name.Length == 0) return;

        SaveCurrentBounds();
        var preset = new WidgetPreset { Name = name, State = WidgetState.Capture(_settings) };
        int existing = _settings.Presets.FindIndex(p => WidgetPreset.SameName(p.Name, name));
        if (existing >= 0) _settings.Presets[existing] = preset;
        else if (_settings.Presets.Count < WidgetPreset.MaxUserPresets) _settings.Presets.Add(preset);
        else return;

        EndPresetNaming();
        SaveCurrentBounds();
        RebuildPresetList();
    }

    private void EndPresetNaming()
    {
        PresetNamePanel.Visibility = Visibility.Collapsed;
        BtnPresetSaveAs.Visibility = Visibility.Visible;
    }
}
