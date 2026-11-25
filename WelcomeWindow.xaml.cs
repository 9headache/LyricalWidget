using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LyricalWidget.Common;
using LyricalWidget.Interop;

namespace LyricalWidget;

public partial class WelcomeWindow : Window
{
    private readonly bool _isFirstBoot;
    private readonly bool _isReplay;
    private readonly Action? _closedCallback;
    private bool _isClosing = false;
    private BlurEffect? _blurEffect;

    public WelcomeWindow(bool isFirstBoot, bool isReplay = false, Action? closedCallback = null)
    {
        _isFirstBoot = isFirstBoot;
        _isReplay = isReplay;
        _closedCallback = closedCallback;

        InitializeComponent();

        var icon = IconHelper.LoadWpfIcon();
        if (icon != null)
        {
            Icon = icon;
            ImgFullIcon.Source = icon;
            ImgSplashIcon.Source = icon;
        }

        Loaded += OnLoaded;
        // Runs for every close path (button, Esc, Alt+F4) so the overlay is always revealed
        Closed += (s, e) => _closedCallback?.Invoke();
        KeyDown += OnKeyDown;
        MouseDown += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && (_isFirstBoot || _isReplay))
            {
                try { DragMove(); } catch { }
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.EnableToolWindowStyle(hwnd);

            // Returning quick splash is non-interactive click-through
            if (!_isFirstBoot && !_isReplay)
            {
                NativeMethods.SetClickThrough(hwnd, true);
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isFirstBoot && !_isReplay)
        {
            // Mode B: Returning-User Quick Splash (1.6s auto-dismiss)
            FullGuideCard.Visibility = Visibility.Collapsed;
            QuickSplashCard.Visibility = Visibility.Visible;

            CardScale.ScaleX = 0.95;
            CardScale.ScaleY = 0.95;
            Opacity = 0.0;

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);

            Task.Delay(1600).ContinueWith(_ =>
            {
                Dispatcher.InvokeAsync(CloseWithAnimation);
            });
        }
        else
        {
            // Mode A: First-Boot Full Guide or Replay
            FullGuideCard.Visibility = Visibility.Visible;
            QuickSplashCard.Visibility = Visibility.Collapsed;

            CardScale.ScaleX = 0.93;
            CardScale.ScaleY = 0.93;
            CardTranslate.Y = 14;
            Opacity = 0.0;

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleIn = new DoubleAnimation(0.93, 1.0, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideIn = new DoubleAnimation(14.0, 0.0, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            CardTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);

            Activate();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseWithAnimation();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        CloseWithAnimation();
    }

    public void CloseWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;

        _blurEffect = new BlurEffect { Radius = 0.0 };
        CardHost.Effect = _blurEffect;

        double targetScale = (!_isFirstBoot && !_isReplay) ? 0.95 : 0.94;
        double maxBlur = (!_isFirstBoot && !_isReplay) ? 10.0 : 12.0;

        var blurAnim = new DoubleAnimation(0.0, maxBlur, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var scaleOut = new DoubleAnimation(1.0, targetScale, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) => Close();

        _blurEffect.BeginAnimation(BlurEffect.RadiusProperty, blurAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
