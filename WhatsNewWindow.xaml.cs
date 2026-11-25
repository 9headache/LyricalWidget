using System;
using System.Collections.Generic;
using System.Threading;
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

public partial class WhatsNewWindow : Window
{
    private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));
    private static readonly Brush BodyBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));

    private readonly CancellationTokenSource _fetchCts = new();

    public WhatsNewWindow()
    {
        InitializeComponent();

        var icon = IconHelper.LoadWpfIcon();
        if (icon != null)
        {
            Icon = icon;
            ImgIcon.Source = icon;
        }

        MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };
        Loaded += OnLoaded;
        Closed += (s, e) => _fetchCts.Cancel();
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        BtnGotIt.Focus();

        var notes = await ReleaseNotesService.GetForCurrentVersionAsync(_fetchCts.Token);
        if (_fetchCts.IsCancellationRequested) return;
        ShowNotes(notes);
    }

    private void ShowNotes(IReadOnlyList<ReleaseNote> notes)
    {
        TxtLoading.Visibility = Visibility.Collapsed;
        NotesList.Children.Clear();

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var item = new StackPanel { Margin = new Thickness(0, i == 0 ? 0 : 14, 0, 0) };
            if (note.Title.Length > 0)
            {
                item.Children.Add(new TextBlock
                {
                    Text = note.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TitleBrush,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (note.Text.Length > 0)
            {
                item.Children.Add(new TextBlock
                {
                    Text = note.Text,
                    FontSize = 12,
                    Foreground = BodyBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, note.Title.Length > 0 ? 3 : 0, 0, 0)
                });
            }
            NotesList.Children.Add(item);
        }

        NotesList.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220)));
    }

    private void OnGotItClicked(object sender, RoutedEventArgs e) => Close();
}
