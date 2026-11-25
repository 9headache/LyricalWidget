using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricalWidget.Common;
using LyricalWidget.Interop;
using LyricalWidget.Services;

namespace LyricalWidget;

/// <summary>Runs an update. Returns false when the update was handed to the browser instead.</summary>
public delegate Task<bool> UpdateRunner(UpdateInfo update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken);

public partial class UpdatePromptWindow : Window
{
    private static readonly Brush StageDone = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
    private static readonly Brush StageActive = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush StagePending = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));

    private readonly UpdateInfo _updateInfo;
    private readonly UpdateRunner _runner;
    private CancellationTokenSource? _updateCts;
    private bool _isUpdating = false;
    private bool _isClosed = false;

    public UpdatePromptWindow(UpdateInfo updateInfo, UpdateRunner? runner = null)
    {
        _updateInfo = updateInfo;
        _runner = runner ?? UpdateService.DownloadAndApplyUpdateAsync;

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
        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };
        Closed += (s, e) =>
        {
            _isClosed = true;
            _updateCts?.Cancel();
        };
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

    private async void OnUpdateNowClicked(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        _isUpdating = true;

        using var cts = new CancellationTokenSource();
        _updateCts = cts;
        ShowProgressState();

        try
        {
            bool handledInPlace = await _runner(_updateInfo, new Progress<UpdateProgress>(OnProgress), cts.Token);
            if (!handledInPlace && !_isClosed)
            {
                Close();
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _isUpdating = false;
            if (!_isClosed) ShowPromptState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update failed: {ex}");
            _isUpdating = false;
            if (!_isClosed) ShowErrorState(DescribeError(ex));
        }
        finally
        {
            _updateCts = null;
        }
    }

    private void OnProgress(UpdateProgress p)
    {
        // Progress callbacks are posted, so a late one can arrive after a cancel or failure
        if (!_isUpdating || _isClosed) return;

        switch (p.Stage)
        {
            case UpdateStage.Downloading:
                SetStages(active: 0);
                string received = FormatMegabytes(p.BytesReceived);
                if (p.TotalBytes is long total && total > 0)
                {
                    double percent = Math.Clamp(p.BytesReceived * 100.0 / total, 0.0, 100.0);
                    AnimateBar(percent);
                    TxtStatus.Text = $"Downloading update... {received} / {FormatMegabytes(total)} ({(int)percent}%)";
                }
                else
                {
                    TxtStatus.Text = $"Downloading update... {received}";
                }
                break;

            case UpdateStage.Preparing:
                SetStages(active: 1);
                AnimateBar(100);
                TxtStatus.Text = "Extracting & preparing...";
                break;

            case UpdateStage.Restarting:
                SetStages(active: 2);
                AnimateBar(100);
                TxtStatus.Text = "Restarting LyricalWidget...";
                // Past the point where backing out makes sense
                BtnLater.IsEnabled = false;
                BtnClose.IsEnabled = false;
                break;
        }
    }

    private void ShowProgressState()
    {
        TxtHeading.Text = "Updating LyricalWidget";
        TxtStatus.Text = "Downloading update...";
        BarProgress.BeginAnimation(ProgressBar.ValueProperty, null);
        BarProgress.Value = 0;
        SetStages(active: 0);

        BtnUpdate.Visibility = Visibility.Collapsed;
        BtnGitHub.Visibility = Visibility.Collapsed;
        BtnLater.Content = "Cancel";
        ShowPanel(ProgressPanel);
    }

    private void ShowPromptState()
    {
        TxtHeading.Text = "Update available";
        BtnUpdate.Content = "Update Now";
        RestoreButtons();
        ShowPanel(NotesPanel);
    }

    private void ShowErrorState(string message)
    {
        TxtHeading.Text = "Update failed";
        TxtErrorMessage.Text = message;
        BtnUpdate.Content = "Try Again";
        RestoreButtons();
        ShowPanel(ErrorPanel);
    }

    private void RestoreButtons()
    {
        BtnUpdate.Visibility = Visibility.Visible;
        BtnGitHub.Visibility = Visibility.Visible;
        BtnLater.Content = "Later";
        BtnLater.IsEnabled = true;
        BtnClose.IsEnabled = true;
    }

    private void ShowPanel(UIElement panel)
    {
        foreach (UIElement candidate in new UIElement[] { NotesPanel, ProgressPanel, ErrorPanel })
        {
            candidate.Visibility = candidate == panel ? Visibility.Visible : Visibility.Collapsed;
        }
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220)));
    }

    private void SetStages(int active)
    {
        SetStage(0, active, MarkDownload, StageDownload);
        SetStage(1, active, MarkPrepare, StagePrepare);
        SetStage(2, active, MarkRestart, StageRestart);
    }

    private static void SetStage(int index, int active, TextBlock mark, TextBlock label)
    {
        var brush = index < active ? StageDone : index == active ? StageActive : StagePending;
        mark.Text = index < active ? "✓" : index == active ? "●" : "○";
        mark.Foreground = brush;
        label.Foreground = brush;
    }

    private void AnimateBar(double percent)
    {
        BarProgress.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation(percent, TimeSpan.FromMilliseconds(140)));
    }

    private static string FormatMegabytes(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"The download server returned an error ({(int)code}). Try again in a moment or get the update from GitHub.",
        HttpRequestException or OperationCanceledException => "Couldn't reach the download server. Check your internet connection and try again.",
        HttpIOException or IOException { InnerException: System.Net.Sockets.SocketException } => "The download was interrupted. Check your internet connection and try again.",
        FileNotFoundException => ex.Message,
        TimeoutException => "The download stopped responding. Check your internet connection and try again.",
        UnauthorizedAccessException => "Windows blocked writing the update files. Close other copies of LyricalWidget or get the update from GitHub.",
        IOException => $"The update could not be saved: {ex.Message}",
        _ => ex.Message
    };

    private void OnViewOnGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_updateInfo.HtmlUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowErrorState($"Failed to open browser: {ex.Message}");
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
        {
            _updateCts?.Cancel();
            return;
        }
        Close();
    }
}
