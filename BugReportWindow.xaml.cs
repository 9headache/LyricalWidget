using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Media;

namespace LyricalWidget;

public partial class BugReportWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TrackInfo _currentTrack;
    private readonly bool _hasSyncedLyrics;

    public BugReportWindow(AppSettings settings, TrackInfo currentTrack, bool hasSyncedLyrics)
    {
        _settings = settings;
        _currentTrack = currentTrack;
        _hasSyncedLyrics = hasSyncedLyrics;

        InitializeComponent();

        var icon = IconHelper.LoadWpfIcon();
        if (icon != null)
        {
            Icon = icon;
        }

        Loaded += OnLoaded;
        KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.EnableToolWindowStyle(hwnd);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TxtDiagnostics.Text = BuildDiagnosticsText();
        TxtDescription.Focus();
    }

    private string BuildDiagnosticsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"App Version: {AppVersionInfo.DisplayVersion}");
        sb.AppendLine($"OS Version:  {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"Display Mode: {_settings.DisplayMode} | Background: {_settings.BackgroundStyle} | Motion: {_settings.AnimationStyle}");
        sb.AppendLine($"Gaming Mode: {(_settings.GamingMode ? "Enabled (SoftwareOnly)" : "Disabled (Default)")} | FPS Cap: {_settings.FpsCap} FPS");
        sb.AppendLine($"Current Song: {(_currentTrack.IsValid ? $"{_currentTrack.Title} - {_currentTrack.Artist}" : "None / No Media Detected")}");
        sb.AppendLine($"Media App:    {(string.IsNullOrWhiteSpace(_currentTrack.SourceAppId) ? "None" : _currentTrack.SourceAppId)}");
        sb.AppendLine($"Lyrics Sync:  {(_hasSyncedLyrics ? "Synced lyrics active" : "No synced lyrics / Album mode")}");
        return sb.ToString().TrimEnd();
    }

    private string BuildFullReport()
    {
        string desc = string.IsNullOrWhiteSpace(TxtDescription.Text) ? "(No additional description provided)" : TxtDescription.Text.Trim();
        var sb = new StringBuilder();
        sb.AppendLine($"=== LyricalWidget Bug Report ({AppVersionInfo.VersionTag}) ===");
        sb.AppendLine();
        sb.AppendLine("--- User Description ---");
        sb.AppendLine(desc);
        sb.AppendLine();
        sb.AppendLine("--- Diagnostic Information ---");
        sb.AppendLine(TxtDiagnostics.Text);
        sb.AppendLine();
        sb.AppendLine($"Reported on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    private void OnOpenGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppVersionInfo.SupportUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyReportClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string fullReport = $"Issue Report ({AppVersionInfo.SupportUrl})\n\n{BuildFullReport()}";
            Clipboard.SetText(fullReport);

            var textBlock = FindVisualChild<TextBlock>(BtnCopyReport);
            if (textBlock != null)
            {
                string originalText = textBlock.Text;
                textBlock.Text = "Copied!";
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

                Task.Delay(1800).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        textBlock.Text = originalText;
                        textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));
                    });
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var subChild = FindVisualChild<T>(child);
            if (subChild != null) return subChild;
        }
        return null;
    }
}
