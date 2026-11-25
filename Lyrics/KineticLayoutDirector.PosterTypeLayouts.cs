using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LyricalWidget.Lyrics;

// Poster layouts built from type alone
public sealed partial class KineticLayoutDirector
{
    private KineticPhraseVisual BuildPosterCenterpieceZoomThrough(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Color nextBackdropColor,
        out Point pivotOrigin, out TextBlock heroTb, out double openLetterRelX)
    {
        var visual = new KineticPhraseVisual();
        var mainPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        int heroIdx = 0;
        int maxLen = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].text.Length > maxLen)
            {
                maxLen = words[i].text.Length;
                heroIdx = i;
            }
        }

        var heroWord = words[heroIdx];
        string upperHero = heroWord.text.ToUpperInvariant().Trim(WordTrimChars);
        if (string.IsNullOrEmpty(upperHero)) upperHero = heroWord.text.ToUpperInvariant().Trim();

        char[] openLetters = new[] { 'O', 'A', 'D', 'P', 'R', 'H', 'M', 'N', 'B', 'Q' };
        int openLetterIdx = -1;
        for (int c = 0; c < upperHero.Length; c++)
        {
            if (Array.IndexOf(openLetters, upperHero[c]) >= 0)
            {
                openLetterIdx = c;
                break;
            }
        }

        if (openLetterIdx >= 0)
        {
            openLetterRelX = (openLetterIdx + 0.5) / Math.Max(1.0, upperHero.Length);
            pivotOrigin = new Point(Math.Clamp(openLetterRelX, 0.15, 0.85), 0.5);
        }
        else
        {
            openLetterRelX = 0.5;
            pivotOrigin = new Point(0.5, 0.5);
        }

        double heroFontSize = Math.Clamp(110.0 - (upperHero.Length * 3.5), 58.0, 115.0);

        if (words.Count > 1)
        {
            var setupRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            for (int i = 0; i < words.Count; i++)
            {
                if (i == heroIdx) continue;
                var w = words[i];
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, 32.0, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                setupRow.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            mainPanel.Children.Add(setupRow);
        }

        var nextBackdropBrush = new SolidColorBrush(nextBackdropColor);
        if (nextBackdropBrush.CanFreeze) nextBackdropBrush.Freeze();

        heroTb = CreatePosterWordElement(upperHero, PosterHeavyFont, heroFontSize, FontWeights.Black, nextBackdropBrush, new Thickness(4, 0, 4, 0), out var heroSt, out var heroTt);
        heroTb.HorizontalAlignment = HorizontalAlignment.Center;
        mainPanel.Children.Add(heroTb);
        visual.Words.Add(new KineticWordItem { Text = heroWord.text, StartTime = heroWord.start, EndTime = heroWord.end, Element = heroTb, Scale = heroSt, Translate = heroTt });

        visual.Container = mainPanel;
        visual.HasPortalLetter = true;
        return visual;
    }

    private KineticPhraseVisual BuildPosterSpatialLow(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush,
        int matchedWordIndex)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double baseFontSize = 48.0;

        var setupStack = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            MaxWidth = 720
        };
        Grid.SetRow(setupStack, 0);

        var lowContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(lowContainer, 1);

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i == matchedWordIndex)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant(), PosterHeavyFont, baseFontSize * 1.6, FontWeights.Black, heroBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                tb.VerticalAlignment = VerticalAlignment.Bottom;
                lowContainer.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, baseFontSize * 0.85, FontWeights.Bold, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                setupStack.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
        }

        if (setupStack.Children.Count > 0) mainGrid.Children.Add(setupStack);
        mainGrid.Children.Add(lowContainer);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterSpatialHigh(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush,
        int matchedWordIndex)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double baseFontSize = 48.0;

        var highContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 18)
        };
        Grid.SetRow(highContainer, 0);

        var setupStack = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720
        };
        Grid.SetRow(setupStack, 1);

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i == matchedWordIndex)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant(), PosterHeavyFont, baseFontSize * 1.6, FontWeights.Black, heroBrush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                highContainer.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, baseFontSize * 0.85, FontWeights.Bold, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                setupStack.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
        }

        mainGrid.Children.Add(highContainer);
        if (setupStack.Children.Count > 0) mainGrid.Children.Add(setupStack);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterGiantPunctuation(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
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
        string punct = line.Text.Contains('?') ? "?" : "!";

        int numRows = words.Count <= 3 ? 1 : 2;
        int perRow = (int)Math.Ceiling(words.Count / (double)numRows);

        var textStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(textStack, 0);

        for (int r = 0; r < numRows; r++)
        {
            var rowWords = words.Skip(r * perRow).Take(perRow).ToList();
            if (rowWords.Count == 0) continue;

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            foreach (var w in rowWords)
            {
                string cleanText = w.text.TrimEnd('?', '!', ' ', ',', '.');
                if (string.IsNullOrWhiteSpace(cleanText)) cleanText = w.text;
                var tb = CreatePosterWordElement(cleanText.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                rowPanel.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            textStack.Children.Add(rowPanel);
        }
        mainGrid.Children.Add(textStack);

        var punctTb = new TextBlock
        {
            Text = punct,
            FontFamily = PosterImpactFont,
            FontSize = baseFontSize * 3.0,
            FontWeight = FontWeights.Black,
            Foreground = heroBrush,
            VerticalAlignment = VerticalAlignment.Center,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            LineHeight = baseFontSize * 2.8,
            Margin = new Thickness(4, -8, 0, -8)
        };
        Grid.SetColumn(punctTb, 1);
        mainGrid.Children.Add(punctTb);

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterVerticalSideAnchor(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        double baseFontSize = 46.0;

        int heroIdx = 0;
        int maxLen = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].text.Length > maxLen)
            {
                maxLen = words[i].text.Length;
                heroIdx = i;
            }
        }

        var heroWord = words[heroIdx];
        var otherWords = words.Where((w, idx) => idx != heroIdx).ToList();

        bool anchorOnRight = (_posterPhraseCounter % 2 == 0);
        int colHorizontal = anchorOnRight ? 0 : 1;
        int colVertical = anchorOnRight ? 1 : 0;

        var horizStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = anchorOnRight ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(horizStack, colHorizontal);

        int numRows = otherWords.Count <= 2 ? 1 : 2;
        int perRow = (int)Math.Ceiling(otherWords.Count / (double)numRows);

        for (int r = 0; r < numRows; r++)
        {
            var rowWords = otherWords.Skip(r * perRow).Take(perRow).ToList();
            if (rowWords.Count == 0) continue;

            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var w in rowWords)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, primaryBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                rowPanel.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            horizStack.Children.Add(rowPanel);
        }
        mainGrid.Children.Add(horizStack);

        var vertTb = CreatePosterWordElement(heroWord.text.ToUpperInvariant(), PosterHeavyFont, baseFontSize * 1.1, FontWeights.Black, heroBrush, new Thickness(2), out var vertSt, out var vertTt);
        vertTb.LayoutTransform = new RotateTransform(anchorOnRight ? 90.0 : -90.0);
        vertTb.VerticalAlignment = VerticalAlignment.Center;
        vertTb.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(vertTb, colVertical);
        mainGrid.Children.Add(vertTb);
        visual.Words.Add(new KineticWordItem { Text = heroWord.text, StartTime = heroWord.start, EndTime = heroWord.end, Element = vertTb, Scale = vertSt, Translate = vertTt });

        visual.Container = mainGrid;
        return visual;
    }

    private KineticPhraseVisual BuildPosterInterlockingHeroGrid(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = 50.0;

        bool hasPunct = line.Text.TrimEnd().EndsWith("?") || line.Text.TrimEnd().EndsWith("!");
        string punct = line.Text.TrimEnd().EndsWith("?") ? "?" : "!";

        var setupCol = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };

        var heroCol = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            bool isHero = heroIndices.Contains(i);

            if (isHero)
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize * 1.75, FontWeights.Black, heroBrush, new Thickness(2, 0, 2, 0), out var st, out var tt);
                heroCol.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
            else
            {
                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterSansBlackFont, baseFontSize * 0.65, FontWeights.Black, primaryBrush, new Thickness(0, 1, 0, 1), out var st, out var tt);
                setupCol.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }
        }

        if (hasPunct)
        {
            var punctTb = new TextBlock
            {
                Text = punct,
                FontFamily = PosterImpactFont,
                FontSize = baseFontSize * 1.85,
                FontWeight = FontWeights.Black,
                Foreground = heroBrush,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            heroCol.Children.Add(punctTb);
        }

        if (_phraseCounter % 2 == 0)
        {
            if (setupCol.Children.Count > 0) mainPanel.Children.Add(setupCol);
            mainPanel.Children.Add(heroCol);
        }
        else
        {
            setupCol.Margin = new Thickness(16, 0, 0, 0);
            mainPanel.Children.Add(heroCol);
            if (setupCol.Children.Count > 0) mainPanel.Children.Add(setupCol);
        }

        visual.Container = mainPanel;
        return visual;
    }

    private KineticPhraseVisual BuildPosterBeatSlam(
        LyricLine line, int lineIndex,
        List<(string text, TimeSpan start, TimeSpan end)> words,
        Brush primaryBrush, Brush heroBrush)
    {
        var visual = new KineticPhraseVisual();
        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroIndices = IdentifyHeroWords(words);
        double baseFontSize = words.Count <= 2 ? 72.0 : 54.0;

        int numRows = words.Count <= 2 ? 1 : 2;
        int perRow = (int)Math.Ceiling(words.Count / (double)numRows);

        for (int r = 0; r < numRows; r++)
        {
            var rowIndices = Enumerable.Range(r * perRow, perRow).Where(i => i < words.Count).ToList();
            if (rowIndices.Count == 0) continue;

            var rowStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };

            foreach (int i in rowIndices)
            {
                var w = words[i];
                bool isHero = heroIndices.Contains(i);
                Brush brush = isHero ? heroBrush : primaryBrush;

                var tb = CreatePosterWordElement(w.text.ToUpperInvariant() + " ", PosterHeavyFont, baseFontSize, FontWeights.Black, brush, new Thickness(4, 0, 4, 0), out var st, out var tt);
                rowStack.Children.Add(tb);
                visual.Words.Add(new KineticWordItem { Text = w.text, StartTime = w.start, EndTime = w.end, Element = tb, Scale = st, Translate = tt });
            }

            mainStack.Children.Add(rowStack);
        }

        visual.Container = mainStack;
        return visual;
    }
}
