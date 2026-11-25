using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LyricalWidget.Common;
using LyricalWidget.Interop;

namespace LyricalWidget;

public enum AppDialogGlyph
{
    Check,
    Info
}

/// <summary>
/// Small themed status dialog used in place of the stock Windows message box.
/// </summary>
public partial class AppDialogWindow : Window
{
    private readonly Action? _secondaryAction;

    public AppDialogWindow(AppDialogGlyph glyph, string headline, string message, string? secondaryText = null, Action? secondaryAction = null)
    {
        _secondaryAction = secondaryAction;

        InitializeComponent();

        var icon = IconHelper.LoadWpfIcon();
        if (icon != null)
        {
            Icon = icon;
        }

        Title = headline;
        TxtHeadline.Text = headline;
        TxtMessage.Text = message;
        GlyphPath.Data = Geometry.Parse(glyph == AppDialogGlyph.Check
            ? "M 0,5 L 5,10 L 14,0"
            : "M 1,6 L 1,13 M 1,1 L 1,1.1");

        if (secondaryText != null)
        {
            BtnSecondary.Content = secondaryText;
            BtnSecondary.Visibility = Visibility.Visible;
        }

        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };
        Loaded += (s, e) =>
        {
            Activate();
            BtnDone.Focus();
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

    private void OnSecondaryClicked(object sender, RoutedEventArgs e)
    {
        Close();
        _secondaryAction?.Invoke();
    }

    private void OnDoneClicked(object sender, RoutedEventArgs e) => Close();
}
