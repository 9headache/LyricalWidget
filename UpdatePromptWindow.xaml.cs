using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Services;

namespace LyricalWidget;

public partial class UpdatePromptWindow : Window
{
    private readonly UpdateInfo _updateInfo;
    private bool _isUpdating = false;

    public UpdatePromptWindow(UpdateInfo updateInfo)
    {
        _updateInfo = updateInfo;

        InitializeComponent();

        var icon = IconHelper.LoadWpfIcon();
        if (icon != null)
        {
            Icon = icon;
        }

        TxtVersionCompare.Text = $"{AppVersionInfo.VersionTag} → {_updateInfo.TagName}";
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(_updateInfo.Body)
            ? "New features, bug fixes, and performance improvements."
            : _updateInfo.Body;

        KeyDown += (s, e) => { if (e.Key == Key.Escape && !_isUpdating) Close(); };
        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && !_isUpdating) try { DragMove(); } catch (InvalidOperationException) { } };
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

    private async void OnUpdateAndRestartClicked(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        _isUpdating = true;

        BtnUpdate.IsEnabled = false;
        BtnGitHub.IsEnabled = false;
        BtnLater.IsEnabled = false;

        TxtStatus.Text = "Downloading update & preparing restart...";
        TxtStatus.Visibility = Visibility.Visible;

        try
        {
            bool handledInPlace = await UpdateService.DownloadAndApplyUpdateAsync(_updateInfo);
            if (!handledInPlace)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            _isUpdating = false;
            BtnUpdate.IsEnabled = true;
            BtnGitHub.IsEnabled = true;
            BtnLater.IsEnabled = true;
            TxtStatus.Text = $"Update failed: {ex.Message}";
            MessageBox.Show($"Failed to apply update: {ex.Message}\nOpening release page in browser instead.", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            try
            {
                Process.Start(new ProcessStartInfo(_updateInfo.HtmlUrl) { UseShellExecute = true });
            }
            catch { }
            Close();
        }
    }

    private void OnViewOnGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_updateInfo.HtmlUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        if (!_isUpdating)
        {
            Close();
        }
    }
}
