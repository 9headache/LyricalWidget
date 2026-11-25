using System;
using System.Collections.Generic;
using System.Diagnostics;
using LyricalWidget.Common;
using WinForms = System.Windows.Forms;

namespace LyricalWidget.Services;

public class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _lockMenuItem;
    private readonly WinForms.ToolStripMenuItem _visibilityMenuItem;
    private readonly WinForms.ToolStripMenuItem _fullscreenVibeMenuItem;
    private readonly WinForms.ToolStripMenuItem _reconnectMenuItem;
    private readonly WinForms.ToolStripMenuItem _autoHideMenuItem;
    private readonly WinForms.ToolStripMenuItem _idleClockMenuItem;
    private readonly WinForms.ToolStripMenuItem _subLinesMenuItem;
    private readonly WinForms.ToolStripMenuItem _audioReactiveMenuItem;
    private readonly WinForms.ToolStripMenuItem _trackBannerMenuItem;
    private readonly WinForms.ToolStripMenuItem _startWithWindowsMenuItem;
    private readonly WinForms.ToolStripMenuItem _gamingModeMenuItem;
    private readonly WinForms.ToolStripMenuItem _fpsCapMenuItem;

    private readonly WinForms.ToolStripMenuItem _modeLyricsOnly;
    private readonly WinForms.ToolStripMenuItem _modeSideBySide;
    private readonly WinForms.ToolStripMenuItem _modeAlbumOnly;

    private readonly WinForms.ToolStripMenuItem _bgTransparent;
    private readonly WinForms.ToolStripMenuItem _bgGlass;

    private readonly WinForms.ToolStripMenuItem _animFluidWave;
    private readonly WinForms.ToolStripMenuItem _animMoonlight;
    private readonly WinForms.ToolStripMenuItem _vibeMonitorMenu;
    private readonly WinForms.ToolStripMenuItem _browserClock;
    private readonly WinForms.ToolStripMenuItem _browserLyrics;
    private readonly WinForms.ToolStripMenuItem _browserPlayer;
    private readonly WinForms.ToolStripMenuItem _themeColoredLyricsMenuItem;

    private string _currentLockShortcut = "Alt+L";
    private bool _isLocked;
    private bool _disposed;

    public event Action? ToggleLockRequested;
    public event Action? ToggleVisibilityRequested;
    public event Action? ToggleFullscreenVibeRequested;
    public event Action? ForceReconnectRequested;
    public event Action<string>? DisplayModeSelected;
    public event Action<string>? BackgroundStyleSelected;
    public event Action<string>? AnimationStyleSelected;
    public event Action<string>? VibeMonitorSelected;
    public event Action<BrowserMediaBehavior>? BrowserMediaBehaviorSelected;
    public event Action<bool>? ThemeColoredLyricsToggled;
    public event Action<bool>? AutoHideToggled;
    public event Action<bool>? IdleClockToggled;
    public event Action<bool>? SubLinesToggled;
    public event Action<bool>? AudioReactiveGlowToggled;
    public event Action<bool>? TrackBannerToggled;
    public event Action<bool>? GamingModeToggled;
    public event Action<int>? FpsCapToggled;
    public event Action? ShowStartupGuideRequested;
    public event Action? WhatsNewRequested;
    public event Action? CheckForUpdatesRequested;
    public event Action? ResetPositionRequested;
    public event Action? ReportBugRequested;
    public event Action? ClearLyricsCacheRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        int traySize = Math.Clamp(WinForms.SystemInformation.SmallIconSize.Width, 16, 64);
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "LyricalWidget",
            Visible = true,
            Icon = IconHelper.LoadDrawingIcon(traySize)
        };

        var contextMenu = new WinForms.ContextMenuStrip();

        _lockMenuItem = new WinForms.ToolStripMenuItem("Unlock Overlay", null, (s, e) => ToggleLockRequested?.Invoke()) { ShortcutKeyDisplayString = "Alt+L" };
        _visibilityMenuItem = new WinForms.ToolStripMenuItem("Hide Overlay", null, (s, e) => ToggleVisibilityRequested?.Invoke()) { ShortcutKeyDisplayString = "Alt+H" };
        _fullscreenVibeMenuItem = new WinForms.ToolStripMenuItem("Fullscreen Vibe Mode", null, (s, e) => ToggleFullscreenVibeRequested?.Invoke()) { ShortcutKeyDisplayString = "Alt+F" };
        _reconnectMenuItem = new WinForms.ToolStripMenuItem("Refresh Lyrics", null, (s, e) => ForceReconnectRequested?.Invoke()) { ShortcutKeyDisplayString = "Alt+R" };

        var modeSubMenu = new WinForms.ToolStripMenuItem("Display Mode");
        _modeLyricsOnly = new WinForms.ToolStripMenuItem("Lyrics Only", null, (s, e) => DisplayModeSelected?.Invoke("LyricsOnly"));
        _modeSideBySide = new WinForms.ToolStripMenuItem("Album Art + Lyrics", null, (s, e) => DisplayModeSelected?.Invoke("AlbumAndLyrics"));
        _modeAlbumOnly = new WinForms.ToolStripMenuItem("Album Art Only", null, (s, e) => DisplayModeSelected?.Invoke("AlbumOnly"));

        modeSubMenu.DropDownItems.Add(_modeLyricsOnly);
        modeSubMenu.DropDownItems.Add(_modeSideBySide);
        modeSubMenu.DropDownItems.Add(_modeAlbumOnly);

        var bgSubMenu = new WinForms.ToolStripMenuItem("Background Style");
        _bgTransparent = new WinForms.ToolStripMenuItem("Transparent (No Background)", null, (s, e) => BackgroundStyleSelected?.Invoke("Transparent"));
        _bgGlass = new WinForms.ToolStripMenuItem("Glassify (Fluid Glass)", null, (s, e) => BackgroundStyleSelected?.Invoke("Glass"));

        bgSubMenu.DropDownItems.Add(_bgTransparent);
        bgSubMenu.DropDownItems.Add(_bgGlass);

        var styleSubMenu = new WinForms.ToolStripMenuItem("Lyric Animation");
        _animFluidWave = new WinForms.ToolStripMenuItem("Fluid Wave", null, (s, e) => AnimationStyleSelected?.Invoke("Fluid Wave"))
        {
            Checked = true
        };
        _animMoonlight = new WinForms.ToolStripMenuItem(AppSettings.MoonlightBloomStyle, null, (s, e) => AnimationStyleSelected?.Invoke(AppSettings.MoonlightBloomStyle));
        styleSubMenu.DropDownItems.Add(_animFluidWave);
        styleSubMenu.DropDownItems.Add(_animMoonlight);

        var browserSubMenu = new WinForms.ToolStripMenuItem("YouTube / Browser Media");
        _browserClock = new WinForms.ToolStripMenuItem("Show Clock", null, (s, e) => BrowserMediaBehaviorSelected?.Invoke(BrowserMediaBehavior.ShowClock));
        _browserLyrics = new WinForms.ToolStripMenuItem("Lyrics (Clock if none)", null, (s, e) => BrowserMediaBehaviorSelected?.Invoke(BrowserMediaBehavior.YouTubeLyricsWithClockFallback));
        _browserPlayer = new WinForms.ToolStripMenuItem("Always Show Player", null, (s, e) => BrowserMediaBehaviorSelected?.Invoke(BrowserMediaBehavior.AlwaysShowPlayer));

        browserSubMenu.DropDownItems.Add(_browserClock);
        browserSubMenu.DropDownItems.Add(_browserLyrics);
        browserSubMenu.DropDownItems.Add(_browserPlayer);

        _themeColoredLyricsMenuItem = new WinForms.ToolStripMenuItem("Theme-Colored Lyrics")
        {
            Checked = true
        };
        _themeColoredLyricsMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                ThemeColoredLyricsToggled?.Invoke(item.Checked);
            }
        };

        _trackBannerMenuItem = new WinForms.ToolStripMenuItem("Show Track Switch Banner");
        _trackBannerMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                TrackBannerToggled?.Invoke(item.Checked);
            }
        };

        _autoHideMenuItem = new WinForms.ToolStripMenuItem("Auto-hide When Paused");
        _autoHideMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                AutoHideToggled?.Invoke(item.Checked);
            }
        };

        _idleClockMenuItem = new WinForms.ToolStripMenuItem("Idle Clock When Auto-Hidden");
        _idleClockMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                IdleClockToggled?.Invoke(item.Checked);
            }
        };

        _subLinesMenuItem = new WinForms.ToolStripMenuItem("Sub-lines (Romaji/Trans)");
        _subLinesMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                SubLinesToggled?.Invoke(item.Checked);
            }
        };

        _audioReactiveMenuItem = new WinForms.ToolStripMenuItem("Audio-Reactive Glow")
        {
            Checked = true
        };
        _audioReactiveMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                AudioReactiveGlowToggled?.Invoke(item.Checked);
            }
        };

        _startWithWindowsMenuItem = new WinForms.ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupService.IsStartupEnabled()
        };
        _startWithWindowsMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                StartupService.SetStartup(item.Checked);
            }
        };

        _gamingModeMenuItem = new WinForms.ToolStripMenuItem("Gaming Mode (No GPU Stall)")
        {
            Checked = true
        };
        _gamingModeMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                GamingModeToggled?.Invoke(item.Checked);
            }
        };

        _fpsCapMenuItem = new WinForms.ToolStripMenuItem("30 FPS Eco Cap")
        {
            Checked = false
        };
        _fpsCapMenuItem.Click += (s, e) =>
        {
            if (s is WinForms.ToolStripMenuItem item)
            {
                item.Checked = !item.Checked;
                FpsCapToggled?.Invoke(item.Checked ? 30 : 60);
            }
        };

        var openFolderItem = new WinForms.ToolStripMenuItem("Open Data Folder", null, (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppPaths.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open directory: {ex.Message}");
            }
        });

        var clearLyricsCacheItem = new WinForms.ToolStripMenuItem("Clear Lyrics Cache", null, (s, e) =>
        {
            ClearLyricsCacheRequested?.Invoke();
        });

        var supportSubMenu = new WinForms.ToolStripMenuItem("Release && Support");
        var checkForUpdatesItem = new WinForms.ToolStripMenuItem("Check for Updates...", null, (s, e) => CheckForUpdatesRequested?.Invoke());
        var showGuideItem = new WinForms.ToolStripMenuItem("Quick Guide", null, (s, e) => ShowStartupGuideRequested?.Invoke());
        var whatsNewItem = new WinForms.ToolStripMenuItem("What's New", null, (s, e) => WhatsNewRequested?.Invoke());
        var resetPosItem = new WinForms.ToolStripMenuItem("Reset Overlay Position", null, (s, e) => ResetPositionRequested?.Invoke());
        var reportBugItem = new WinForms.ToolStripMenuItem("Report a Bug...", null, (s, e) => ReportBugRequested?.Invoke());
        var versionStatusItem = new WinForms.ToolStripMenuItem(AppVersionInfo.DisplayVersion)
        {
            Enabled = false
        };

        supportSubMenu.DropDownItems.Add(checkForUpdatesItem);
        supportSubMenu.DropDownItems.Add(resetPosItem);
        supportSubMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
        supportSubMenu.DropDownItems.Add(reportBugItem);
        supportSubMenu.DropDownItems.Add(versionStatusItem);

        var exitItem = new WinForms.ToolStripMenuItem("Quit", null, (s, e) => ExitRequested?.Invoke());

        contextMenu.Items.Add(_lockMenuItem);
        contextMenu.Items.Add(_visibilityMenuItem);
        contextMenu.Items.Add(_fullscreenVibeMenuItem);
        contextMenu.Items.Add(_reconnectMenuItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(modeSubMenu);
        contextMenu.Items.Add(bgSubMenu);
        contextMenu.Items.Add(styleSubMenu);
        _vibeMonitorMenu = new WinForms.ToolStripMenuItem("Vibe Display / Monitor") { Visible = false };
        contextMenu.Items.Add(browserSubMenu);
        contextMenu.Items.Add(_vibeMonitorMenu);
        contextMenu.Items.Add(_themeColoredLyricsMenuItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(_trackBannerMenuItem);
        contextMenu.Items.Add(_autoHideMenuItem);
        contextMenu.Items.Add(_idleClockMenuItem);
        contextMenu.Items.Add(_subLinesMenuItem);
        contextMenu.Items.Add(_audioReactiveMenuItem);
        contextMenu.Items.Add(_startWithWindowsMenuItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(_gamingModeMenuItem);
        contextMenu.Items.Add(_fpsCapMenuItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(showGuideItem);
        contextMenu.Items.Add(whatsNewItem);
        contextMenu.Items.Add(supportSubMenu);
        contextMenu.Items.Add(openFolderItem);
        contextMenu.Items.Add(clearLyricsCacheItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        ChromeMenuRenderer.Apply(contextMenu);
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ToggleVisibilityRequested?.Invoke();
    }

    public void UpdateLockShortcutText(string shortcut)
    {
        _currentLockShortcut = shortcut;
        UpdateLockState(_isLocked);
    }

    public void UpdateLockState(bool isLocked)
    {
        _isLocked = isLocked;
        _lockMenuItem.Text = isLocked ? "Unlock Overlay" : "Lock Overlay";
        _lockMenuItem.ShortcutKeyDisplayString = _currentLockShortcut == "(None)" ? string.Empty : _currentLockShortcut;
    }

    public void UpdateVisibilityState(bool isVisible)
    {
        _visibilityMenuItem.Text = isVisible ? "Hide Overlay" : "Show Overlay";
    }

    public void UpdateDisplayModeState(string mode)
    {
        _modeLyricsOnly.Checked = string.Equals(mode, "LyricsOnly", StringComparison.OrdinalIgnoreCase);
        _modeSideBySide.Checked = string.Equals(mode, "AlbumAndLyrics", StringComparison.OrdinalIgnoreCase);
        _modeAlbumOnly.Checked = string.Equals(mode, "AlbumOnly", StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateBackgroundStyleState(string bg)
    {
        _bgTransparent.Checked = string.Equals(bg, "Transparent", StringComparison.OrdinalIgnoreCase);
        _bgGlass.Checked = string.Equals(bg, "Glass", StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateAnimationStyleState(string style)
    {
        _animMoonlight.Checked = style == AppSettings.MoonlightBloomStyle;
        _animFluidWave.Checked = !_animMoonlight.Checked;
    }

    public void UpdateBrowserMediaState(BrowserMediaBehavior behavior)
    {
        _browserClock.Checked = behavior == BrowserMediaBehavior.ShowClock;
        _browserLyrics.Checked = behavior == BrowserMediaBehavior.YouTubeLyricsWithClockFallback;
        _browserPlayer.Checked = behavior == BrowserMediaBehavior.AlwaysShowPlayer;
    }

    public void UpdateVibeMonitors(IReadOnlyList<MonitorTarget> monitors, string? selectedDevice)
    {
        _vibeMonitorMenu.DropDownItems.Clear();
        _vibeMonitorMenu.Visible = monitors.Count > 1;
        if (monitors.Count < 2) return;

        foreach (var monitor in monitors)
        {
            _vibeMonitorMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem(monitor.Label, null, (s, e) => VibeMonitorSelected?.Invoke(monitor.DeviceName))
            {
                Checked = monitor.DeviceName == selectedDevice
            });
        }
        if (_vibeMonitorMenu.DropDown is WinForms.ToolStripDropDownMenu menu) ChromeMenuRenderer.Apply(menu);
    }

    public void UpdateThemeColoredLyricsState(bool enabled)
    {
        _themeColoredLyricsMenuItem.Checked = enabled;
    }

    public void UpdateAutoHideState(bool autoHide)
    {
        _autoHideMenuItem.Checked = autoHide;
    }

    public void UpdateIdleClockState(bool showIdleClock)
    {
        _idleClockMenuItem.Checked = showIdleClock;
    }

    public void UpdateSubLinesState(bool showSubLines)
    {
        _subLinesMenuItem.Checked = showSubLines;
    }

    public void UpdateAudioReactiveGlowState(bool enabled)
    {
        _audioReactiveMenuItem.Checked = enabled;
    }

    public void UpdateTrackBannerState(bool showBanner)
    {
        _trackBannerMenuItem.Checked = showBanner;
    }

    public void UpdateGamingModeState(bool enabled)
    {
        _gamingModeMenuItem.Checked = enabled;
    }

    public void UpdateFpsCapState(int fps)
    {
        _fpsCapMenuItem.Checked = (fps <= 30);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
