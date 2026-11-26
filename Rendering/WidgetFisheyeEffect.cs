using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LyricalWidget.Rendering;

/// <summary>
/// The widget's wide-angle lens as a pixel shader (source: Shaders/WidgetFisheye.fx). It runs only when the
/// element under it repaints, and WPF can run it in software too, so it works in Game Mode.
/// </summary>
public sealed class WidgetFisheyeEffect : ShaderEffect
{
    private static readonly PixelShader Shader = CreateShader();

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(WidgetFisheyeEffect), 0, SamplingMode.Bilinear);

    public static readonly DependencyProperty StrengthProperty =
        DependencyProperty.Register(nameof(Strength), typeof(double), typeof(WidgetFisheyeEffect),
            new UIPropertyMetadata(FisheyeLens.Strength, PixelShaderConstantCallback(0)));

    public WidgetFisheyeEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(StrengthProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    private static PixelShader CreateShader()
    {
        var shader = new PixelShader { UriSource = new Uri("pack://application:,,,/LyricalWidget;component/Shaders/WidgetFisheye.ps", UriKind.Absolute) };
        shader.Freeze();
        return shader;
    }
}
