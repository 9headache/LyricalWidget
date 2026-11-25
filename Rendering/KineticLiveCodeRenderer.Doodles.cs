using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LyricalWidget.Lyrics;

namespace LyricalWidget.Rendering;

public sealed partial class KineticLiveCodeRenderer
{
    private void UpdateLineDoodle(
        FormattedCodeLine line,
        CodeLineRowVisual row,
        double gutterWidth,
        double baseFontSize,
        double caretY)
    {
        if (_doodleCanvas == null) return;
        _doodleCanvas.Children.Clear();

        CodeToken? matchedToken = null;
        var prefixSb = new StringBuilder();

        foreach (var (_, token) in row.Runs)
        {
            if (!string.IsNullOrEmpty(token.KeywordDoodle))
            {
                matchedToken = token;
                break;
            }
            prefixSb.Append(token.Text);
        }

        if (matchedToken == null) return;

        double wordPrefixWidth = MeasureTextWidth(prefixSb.ToString(), baseFontSize);
        double doodleX = gutterWidth + wordPrefixWidth + 4.0;
        double doodleY = caretY;

        switch (matchedToken.KeywordDoodle)
        {
            case "crown":
                var crown = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0 10 L 3.5 2 L 8 7 L 12.5 2 L 16 10 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                    Width = 16,
                    Height = 10,
                    Stretch = Stretch.Uniform
                };
                Canvas.SetLeft(crown, doodleX);
                Canvas.SetTop(crown, doodleY - 14.0);
                _doodleCanvas.Children.Add(crown);
                break;

            case "mirror":
                var mirrorText = new TextBlock
                {
                    Text = matchedToken.Text.Trim(),
                    FontFamily = MonospaceFont,
                    FontSize = baseFontSize * 0.9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x44, 0x7A, 0xA2, 0xF7)),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1.0, -1.0)
                };
                Canvas.SetLeft(mirrorText, doodleX);
                Canvas.SetTop(mirrorText, doodleY + baseFontSize + 4.0);
                _doodleCanvas.Children.Add(mirrorText);
                break;

            case "strikethrough":
                double tokenWidth = MeasureTextWidth(matchedToken.Text, baseFontSize);
                var strikeLine = new Rectangle
                {
                    Width = Math.Max(24.0, tokenWidth + 6.0),
                    Height = 2.5,
                    Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                };
                Canvas.SetLeft(strikeLine, doodleX - 3.0);
                Canvas.SetTop(strikeLine, doodleY + (baseFontSize * 0.55));
                _doodleCanvas.Children.Add(strikeLine);
                break;

            case "wave":
                var wavePath = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0 0 Q 6 5 12 0 T 24 0 T 36 0"),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                    StrokeThickness = 2.2
                };
                Canvas.SetLeft(wavePath, doodleX);
                Canvas.SetTop(wavePath, doodleY + baseFontSize + 6.0);
                _doodleCanvas.Children.Add(wavePath);
                break;

            case "sleep":
                for (int z = 0; z < 2; z++)
                {
                    var zText = new TextBlock
                    {
                        Text = "z",
                        FontFamily = MonospaceFont,
                        FontSize = (baseFontSize * 0.35) + (z * 4.0),
                        Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xA8, 0x55, 0xF7))
                    };
                    Canvas.SetLeft(zText, doodleX + (z * 12.0));
                    Canvas.SetTop(zText, doodleY - 14.0 - (z * 10.0));
                    _doodleCanvas.Children.Add(zText);
                }
                break;
        }
    }
}
