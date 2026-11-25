using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LyricalWidget.Lyrics;

// Poster layouts built on ribbons, wedges and splits
public sealed partial class KineticLayoutDirector
{
    private KineticPhraseVisual BuildPosterHorizonSplit(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double baseFontSize = 50.0;
        int mid = Math.Max(1, (words.Count + 1) / 2);

        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2)
        };
        Grid.SetRow(topRow, 0);

        for (int i = 0; i < mid; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, primaryBrush, new Thickness(3, 0, 3, 0), out var st, out var tt);
            tb.VerticalAlignment = VerticalAlignment.Bottom;
            topRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        mainGrid.Children.Add(topRow);

        var bottomContainer = new Border
        {
            Background = ribbonBg,
            ClipToBounds = true,
            Padding = new Thickness(24, 6, 24, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(bottomContainer, 1);

        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };

        for (int i = mid; i < words.Count; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, ribbonFg, new Thickness(3, 0, 3, 0), out var st, out var tt);
            tb.VerticalAlignment = VerticalAlignment.Top;
            bottomRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }

        bottomContainer.Child = bottomRow;
        mainGrid.Children.Add(bottomContainer);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterRotatingRibbon(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        double baseFontSize = 48.0;
        int leadCount = Math.Max(1, Math.Min(3, words.Count / 2));

        var leadWords = words.Take(leadCount).ToList();
        var tailWords = words.Skip(leadCount).ToList();

        var ribbonRotate = new RotateTransform(0.0);
        var ribbonBorder = new Border
        {
            Background = ribbonBg,
            Padding = new Thickness(16, 4, 16, 4),
            CornerRadius = new CornerRadius(0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = ribbonRotate,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(ribbonBorder, 0);

        var leadStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var w in leadWords)
        {
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, ribbonFg, new Thickness(2, 0, 2, 0), out var st, out var tt);
            leadStack.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        ribbonBorder.Child = leadStack;
        mainGrid.Children.Add(ribbonBorder);

        var slideTranslate = new TranslateTransform(-50.0, 0.0);
        var tailContainer = new Border
        {
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tailContainer, 1);

        var tailStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            RenderTransform = slideTranslate,
            Opacity = 0.0,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var w in tailWords)
        {
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
            tailStack.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        tailContainer.Child = tailStack;
        mainGrid.Children.Add(tailContainer);

        int transIdx = Math.Min(visual.Words.Count - 1, leadCount - 1);
        visual.TemplateCustomUpdate = (pos, dt, bass) =>
        {
            bool triggered = transIdx >= 0 && transIdx < visual.Words.Count && visual.Words[transIdx].Triggered && visual.Words[transIdx].Progress >= 0.7;
            double targetAngle = triggered ? 90.0 : 0.0;
            double targetSlide = triggered ? 0.0 : -50.0;
            double targetOpacity = triggered ? 1.0 : 0.0;

            ribbonRotate.Angle += (targetAngle - ribbonRotate.Angle) * Math.Clamp(14.0 * dt, 0.0, 1.0);
            slideTranslate.X += (targetSlide - slideTranslate.X) * Math.Clamp(14.0 * dt, 0.0, 1.0);
            tailStack.Opacity += (targetOpacity - tailStack.Opacity) * Math.Clamp(14.0 * dt, 0.0, 1.0);
        };

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterDiagonalWedgeWipe(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Color wedgeColor)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };

        double baseFontSize = 52.0;
        var textStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize, FontWeights.Black, primaryBrush, new Thickness(3, 0, 3, 0), out var st, out var tt);
            textStack.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        mainGrid.Children.Add(textStack);

        var wedgeTranslate = new TranslateTransform(-300.0, 0.0);
        var wedgeBrush = new SolidColorBrush(wedgeColor);
        if (wedgeBrush.CanFreeze) wedgeBrush.Freeze();

        var wedgePoly = new Polygon
        {
            Fill = wedgeBrush,
            Points = new PointCollection
            {
                new Point(0, 0),
                new Point(200, 0),
                new Point(100, 240),
                new Point(-100, 240)
            },
            RenderTransform = wedgeTranslate,
            IsHitTestVisible = false,
            Opacity = 0.95
        };
        mainGrid.Children.Add(wedgePoly);

        visual.TemplateCustomUpdate = (pos, dt, bass) =>
        {
            double elapsedMs = (pos - visual.LineStartTime).TotalMilliseconds;
            if (elapsedMs < 220.0 && elapsedMs >= 0.0)
            {
                double t = Math.Clamp(elapsedMs / 200.0, 0.0, 1.0);
                double eased = t * (2.0 - t);
                double totalWidth = visual.VisualSize.Width > 0 ? visual.VisualSize.Width + 400.0 : 1200.0;
                wedgeTranslate.X = -300.0 + (totalWidth * eased);
                wedgePoly.Opacity = 0.95 * (1.0 - (t * 0.5));
                wedgePoly.Visibility = Visibility.Visible;
            }
            else
            {
                wedgePoly.Visibility = Visibility.Collapsed;
            }
        };

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterInvertedRibbonBar(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = 50.0;

        int firstHeroIdx = heroIndices.Count > 0 ? heroIndices.First() : 0;

        var row0 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            bool isHero = heroIndices.Contains(i);

            if (isHero)
            {
                var ribbonBorder = new Border
                {
                    Background = ribbonBg,
                    Padding = new Thickness(12, 2, 12, 2),
                    CornerRadius = new CornerRadius(0),
                    Margin = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.25, FontWeights.Black, ribbonFg, new Thickness(2, 0, 2, 0), out var st, out var tt);
                ribbonBorder.Child = tb;
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });

                if (i <= firstHeroIdx && words.Count > 5) row0.Children.Add(ribbonBorder);
                else row1.Children.Add(ribbonBorder);
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.05, FontWeights.Bold, primaryBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });

                if (i <= firstHeroIdx && words.Count > 5) row0.Children.Add(tb);
                else row1.Children.Add(tb);
            }
        }

        if (row0.Children.Count > 0) mainStack.Children.Add(row0);
        if (row1.Children.Count > 0) mainStack.Children.Add(row1);

        visual.Container = mainStack;
        return visual;
    }

    private KineticPhraseVisual BuildPosterSplitScreenContrastCut(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush, Brush ribbonBg, Brush ribbonFg)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        double baseFontSize = 50.0;
        int mid = Math.Max(1, (words.Count + 1) / 2);

        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };

        for (int i = 0; i < mid; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, primaryBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
            topRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }
        mainStack.Children.Add(topRow);

        var bottomBand = new Border
        {
            Background = ribbonBg,
            Padding = new Thickness(24, 6, 24, 6),
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        for (int i = mid; i < words.Count; i++)
        {
            var w = words[i];
            var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.15, FontWeights.Black, ribbonFg, new Thickness(4, 0, 4, 0), out var st, out var tt);
            bottomRow.Children.Add(tb);
            visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
        }

        bottomBand.Child = bottomRow;
        mainStack.Children.Add(bottomBand);

        visual.Container = mainStack;
        return visual;
    }
}
