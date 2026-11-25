using System;
using System.Diagnostics;
using LyricalWidget.Common;
using LyricalWidget.Services;

namespace LyricalWidget;

// Guide, What's New and update windows
public partial class MainWindow
{
    private WelcomeWindow? _guideWindow;
    private UpdatePromptWindow? _updatePrompt;
    private AppDialogWindow? _updateStatusDialog;
    private WhatsNewWindow? _whatsNewWindow;
    private bool _isCheckingForUpdates = false;

    public void ShowStartupGuide()
    {
        if (_guideWindow != null && _guideWindow.IsVisible)
        {
            _guideWindow.Activate();
            return;
        }

        _guideWindow = new WelcomeWindow(isFirstBoot: true, isReplay: true);
        _guideWindow.Show();
    }

    public void ShowBugReporter()
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
            System.Diagnostics.Debug.WriteLine($"Failed to open support link: {ex.Message}");
        }
    }

    private void ShowUpdatePrompt(UpdateInfo update, UpdateRunner? runner = null)
    {
        // One prompt at a time: two downloads would share the same temp folder
        if (_updatePrompt != null && _updatePrompt.IsVisible)
        {
            _updatePrompt.Activate();
            return;
        }

        _updatePrompt = new UpdatePromptWindow(update, runner) { Owner = this };
        _updatePrompt.Show();
    }

    public void ShowWhatsNew()
    {
        if (_whatsNewWindow != null && _whatsNewWindow.IsVisible)
        {
            _whatsNewWindow.Activate();
            return;
        }

        _whatsNewWindow = new WhatsNewWindow();
        _whatsNewWindow.Show();
    }

    private void ShowUpdateStatus(AppDialogWindow dialog)
    {
        _updateStatusDialog?.Close();
        _updateStatusDialog = dialog;
        dialog.Show();
    }

    private async void OnCheckForUpdatesRequested()
    {
        if (_isCheckingForUpdates) return;
        _isCheckingForUpdates = true;
        try
        {
            var result = await UpdateService.CheckForUpdateAsync();
            if (_isClosing) return;
            if (result.Update != null)
            {
                ShowUpdatePrompt(result.Update);
            }
            else if (result.IsUpToDate)
            {
                ShowUpdateStatus(new AppDialogWindow(
                    AppDialogGlyph.Check,
                    "You're running the latest build.",
                    $"LyricalWidget v{AppVersionInfo.Version} is already up to date — nothing to install.",
                    "View What's New",
                    ShowWhatsNew));
            }
            else
            {
                ShowUpdateStatus(new AppDialogWindow(
                    AppDialogGlyph.Info,
                    "Couldn't check for updates.",
                    $"Check your internet connection and try again.\n\n{result.ErrorMessage}"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check error: {ex.Message}");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }
}
