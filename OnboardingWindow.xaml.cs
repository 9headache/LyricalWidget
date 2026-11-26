using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using LyricalWidget.Common;
using LyricalWidget.Interop;

namespace LyricalWidget;

/// <summary>First-launch guide. Either button marks onboarding as done; any other way of closing leaves it for next time.</summary>
public partial class OnboardingWindow : Window
{
    private bool _closing;

    /// <summary>True once the user answered with one of the two buttons.</summary>
    public bool Completed { get; private set; }

    public OnboardingWindow(string lockShortcut)
    {
        InitializeComponent();

        var icon = IconHelper.LoadWpfIcon();
        if (icon != null)
        {
            Icon = icon;
            ImgIcon.Source = icon;
        }
        if (lockShortcut != "(None)") TxtLockKey.Text = lockShortcut.Replace("+", " + ");

        Opacity = 0.0;
        Loaded += (s, e) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Activate();
            BtnThanks.Focus();
        };
        MouseDown += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) NativeMethods.EnableToolWindowStyle(hwnd);
    }

    private void OnThanksClicked(object sender, RoutedEventArgs e) => Finish();

    private void OnNotRequiredClicked(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        if (_closing) return;
        _closing = true;
        Completed = true;

        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (s, e) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
