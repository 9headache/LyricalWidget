using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LyricalWidget.Lyrics;

namespace LyricalWidget.Rendering;

// Editor rows, typing progress and token chips
public sealed partial class KineticLiveCodeRenderer
{
    private void UpdateTypographyDimensions()
    {
        if (_renderedRows.Count == 0) return;

        double baseFontSize = Math.Clamp(_viewportHeight * 0.046, 30.0, 56.0);
        double lineHeight = Math.Max(56.0, baseFontSize * 1.85);
        double gutterWidth = Math.Max(76.0, baseFontSize * 1.70);
        double gutterFontSize = Math.Max(14.0, baseFontSize * 0.44);

        if (_indentGuideCol0 != null) Canvas.SetLeft(_indentGuideCol0, gutterWidth);
        if (_indentGuideCol4 != null) Canvas.SetLeft(_indentGuideCol4, gutterWidth + (MeasureTextWidth("    ", baseFontSize)));

        for (int i = 0; i < _renderedRows.Count; i++)
        {
            var row = _renderedRows[i];
            row.RowGrid.Height = lineHeight;
            Canvas.SetTop(row.RowGrid, i * lineHeight);

            if (row.RowGrid.ColumnDefinitions.Count > 0)
            {
                row.RowGrid.ColumnDefinitions[0].Width = new GridLength(gutterWidth, GridUnitType.Pixel);
            }
            row.NumberBlock.FontSize = gutterFontSize;
            row.CodeBlock.FontSize = baseFontSize;

            UpdateTokenHighlightChips(row, gutterWidth, baseFontSize, lineHeight);
        }
    }

    private void RebuildVisualRows()
    {
        _renderedRows.Clear();
        if (_linesCanvas == null) return;
        _linesCanvas.Children.Clear();

        double baseFontSize = Math.Clamp(_viewportHeight * 0.046, 30.0, 56.0);
        double lineHeight = Math.Max(56.0, baseFontSize * 1.85);
        double gutterWidth = Math.Max(76.0, baseFontSize * 1.70);
        double gutterFontSize = Math.Max(14.0, baseFontSize * 0.44);

        if (_indentGuideCol0 != null) Canvas.SetLeft(_indentGuideCol0, gutterWidth);
        if (_indentGuideCol4 != null) Canvas.SetLeft(_indentGuideCol4, gutterWidth + (MeasureTextWidth("    ", baseFontSize)));

        for (int i = 0; i < _formattedLines.Count; i++)
        {
            var line = _formattedLines[i];

            var rowGrid = new Grid
            {
                Height = lineHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(rowGrid, 0);
            Canvas.SetTop(rowGrid, i * lineHeight);

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gutterWidth, GridUnitType.Pixel) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var numText = new TextBlock
            {
                Text = line.LineNumber.ToString(CultureInfo.InvariantCulture),
                FontFamily = MonospaceFont,
                FontSize = gutterFontSize,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x56, 0x5F, 0x89)),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };
            Grid.SetColumn(numText, 0);
            rowGrid.Children.Add(numText);

            var textCell = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var chipsCanvas = new Canvas
            {
                IsHitTestVisible = false
            };
            textCell.Children.Add(chipsCanvas);

            var codeText = new TextBlock
            {
                FontFamily = MonospaceFont,
                FontSize = baseFontSize,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(codeText, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(codeText, TextRenderingMode.Auto);

            var runs = new List<(Run run, CodeToken token)>();
            foreach (var token in line.Tokens)
            {
                var run = new Run(token.Text)
                {
                    Foreground = GetTokenBrush(token.Type)
                };
                codeText.Inlines.Add(run);
                runs.Add((run, token));
            }

            textCell.Children.Add(codeText);
            Grid.SetColumn(textCell, 1);
            rowGrid.Children.Add(textCell);

            var visualRow = new CodeLineRowVisual
            {
                RowGrid = rowGrid,
                NumberBlock = numText,
                CodeBlock = codeText,
                ChipsCanvas = chipsCanvas,
                Runs = runs,
                Line = line,
                LineIndex = i
            };

            _renderedRows.Add(visualRow);
            _linesCanvas.Children.Add(rowGrid);

            UpdateTokenHighlightChips(visualRow, gutterWidth, baseFontSize, lineHeight);
        }
    }

    private void UpdateTokenHighlightChips(CodeLineRowVisual row, double gutterWidth, double baseFontSize, double lineHeight)
    {
        if (row.ChipsCanvas == null) return;
        row.ChipsCanvas.Children.Clear();

        var prefixSb = new StringBuilder();

        foreach (var (_, token) in row.Runs)
        {
            if (token.Type is CodeTokenType.Function or CodeTokenType.StringLiteral)
            {
                double tokenOffset = MeasureTextWidth(prefixSb.ToString(), baseFontSize);
                double tokenWidth = MeasureTextWidth(token.Text, baseFontSize);

                Color chipColor = token.Type switch
                {
                    CodeTokenType.Function => _functionColor,
                    CodeTokenType.StringLiteral => _stringColor,
                    _ => _accentColor
                };

                var chip = new Border
                {
                    Width = Math.Max(12.0, tokenWidth + 6.0),
                    Height = baseFontSize * 1.25,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x1E, chipColor.R, chipColor.G, chipColor.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, chipColor.R, chipColor.G, chipColor.B)),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(chip, tokenOffset - 3.0);
                Canvas.SetTop(chip, (lineHeight - (baseFontSize * 1.25)) * 0.5);
                row.ChipsCanvas.Children.Add(chip);
            }

            prefixSb.Append(token.Text);
        }
    }

    private void EnsureLinesPopulated()
    {
        if (_renderedRows.Count == 0) return;
        int activeIdx = _lastActiveLineIndex >= 0 ? _lastActiveLineIndex : 0;
        int minIdx = Math.Max(0, activeIdx - 10);
        int maxIdx = Math.Min(_renderedRows.Count - 1, activeIdx + 8);

        for (int i = 0; i < _renderedRows.Count; i++)
        {
            var row = _renderedRows[i];
            if (i >= minIdx && i <= maxIdx)
            {
                row.RowGrid.Visibility = Visibility.Visible;
                row.RowGrid.Opacity = (i < activeIdx) ? 0.42 : (i == activeIdx ? 1.0 : 0.0);
            }
            else
            {
                row.RowGrid.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateRollingLines(
        int activeLineIdx,
        TimeSpan effectivePos,
        double dt,
        double baseFontSize,
        double lineHeight,
        double gutterWidth,
        out Point caretPoint)
    {
        caretPoint = new Point(gutterWidth + 20, 100);
        if (_linesCanvas == null || _renderedRows.Count == 0) return;

        for (int i = 0; i < _renderedRows.Count; i++)
        {
            var row = _renderedRows[i];

            if (activeLineIdx < 0)
            {
                // Before playback: reveal header comment
                row.RowGrid.Opacity = i == 0 ? 0.70 : 0.0;
                row.RowGrid.Visibility = i <= 2 ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }

            // Rolling buffer of visible lines around active line
            int dist = i - activeLineIdx;
            if (dist < -16 || dist > 8)
            {
                row.RowGrid.Visibility = Visibility.Collapsed;
                continue;
            }
            row.RowGrid.Visibility = Visibility.Visible;

            if (i < activeLineIdx)
            {
                row.RowGrid.Opacity = 0.42;
                foreach (var (run, token) in row.Runs)
                {
                    run.Text = token.Text;
                }
            }
            else if (i == activeLineIdx)
            {
                row.RowGrid.Opacity = 1.0;
                var line = row.Line;
                double elapsedMs = (effectivePos - line.Timestamp).TotalMilliseconds;
                double totalMs = Math.Max(120.0, (line.EndTime - line.Timestamp).TotalMilliseconds);
                double progress = Math.Clamp(elapsedMs / totalMs, 0.0, 1.0);

                int totalChars = 0;
                foreach (var (_, tok) in row.Runs) totalChars += tok.Text.Length;

                double charProgress = totalChars * progress;
                int revealChars = (int)Math.Floor(charProgress);
                double fracChar = charProgress - revealChars;

                var revealedSb = new StringBuilder();
                int runningRevealed = 0;

                foreach (var (run, token) in row.Runs)
                {
                    int tokLen = token.Text.Length;
                    if (revealChars >= runningRevealed + tokLen)
                    {
                        run.Text = token.Text;
                        revealedSb.Append(token.Text);
                    }
                    else if (revealChars > runningRevealed)
                    {
                        int sub = revealChars - runningRevealed;
                        string partial = token.Text[..sub];
                        run.Text = partial;
                        revealedSb.Append(partial);
                    }
                    else
                    {
                        run.Text = string.Empty;
                    }
                    runningRevealed += tokLen;
                }

                string revealedStr = revealedSb.ToString();
                double textWidth = MeasureTextWidth(revealedStr, baseFontSize);

                double approxCharWidth = MeasureTextWidth("m", baseFontSize);
                double targetCaretX = gutterWidth + textWidth + (fracChar * approxCharWidth * 0.45);

                _caretXSpring.Step(ref _caretCurrentX, ref _caretCurrentXVel, targetCaretX, dt);

                double caretHeight = baseFontSize * 1.15;
                double caretY = (i * lineHeight) + ((lineHeight - caretHeight) * 0.5);
                caretPoint = new Point(_caretCurrentX, caretY + (caretHeight * 0.5));

                if (_caretCanvas != null)
                {
                    _caretCanvas.Visibility = Visibility.Visible;

                    if (_caretBlock != null)
                    {
                        _caretBlock.Width = Math.Max(3.5, baseFontSize * 0.08);
                        _caretBlock.Height = caretHeight;
                        Canvas.SetLeft(_caretBlock, _caretCurrentX);
                        Canvas.SetTop(_caretBlock, caretY);
                    }

                    if (_artistPill != null)
                    {
                        double pillFontSize = Math.Clamp(baseFontSize * 0.38, 11.0, 16.0);
                        if (_artistText != null)
                        {
                            _artistText.FontSize = pillFontSize;
                        }

                        double pillY = caretY - 26.0;
                        double pillX = Math.Max(0.0, _caretCurrentX - 4.0);
                        Canvas.SetLeft(_artistPill, pillX);
                        Canvas.SetTop(_artistPill, pillY);
                    }
                }

                UpdateLineDoodle(line, row, gutterWidth, baseFontSize, caretY);
            }
            else
            {
                row.RowGrid.Opacity = 0.0;
            }
        }
    }

    private double MeasureTextWidth(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0.0;

        double dpi = 1.0;
        if (_editorCanvas != null)
        {
            dpi = VisualTreeHelper.GetDpi(_editorCanvas).PixelsPerDip;
        }

        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonospaceTypeface,
            fontSize,
            Brushes.White,
            dpi);

        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private sealed class CodeLineRowVisual
    {
        public Grid RowGrid { get; init; } = null!;
        public TextBlock NumberBlock { get; init; } = null!;
        public TextBlock CodeBlock { get; init; } = null!;
        public Canvas ChipsCanvas { get; init; } = null!;
        public List<(Run run, CodeToken token)> Runs { get; init; } = new();
        public FormattedCodeLine Line { get; init; } = null!;
        public int LineIndex { get; init; }
    }
}
