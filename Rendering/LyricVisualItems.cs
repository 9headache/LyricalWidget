using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LyricalWidget.Lyrics;

namespace LyricalWidget.Rendering;

public class LyricWordVisualItem
{
    public TextBlock Element { get; set; } = null!;
    public TranslateTransform Transform { get; set; } = null!;
    public ScaleTransform ScaleTransform { get; set; } = null!;
    public DropShadowEffect GlowEffect { get; set; } = null!;
    public LyricWord? Model { get; set; }
    public int WordIndex { get; set; }

    public LinearGradientBrush WordBrush { get; set; } = null!;
    public GradientStop StopActive1 { get; set; } = null!;
    public GradientStop StopActive2 { get; set; } = null!;
    public GradientStop StopInactive1 { get; set; } = null!;
    public GradientStop StopInactive2 { get; set; } = null!;

    public double CurrentTranslateY { get; set; }
    public double VelocityY { get; set; }
    public double CurrentScale { get; set; } = 1.0;
    public double VelocityScale { get; set; }
    public double CurrentGlowOpacity { get; set; } = 0.0;
    public double VelocityGlow { get; set; }

    public bool IsSettled { get; set; } = true;
}

public class LyricLineVisualItem
{
    public FrameworkElement Container { get; set; } = null!;
    public TextBlock? LineTextBlock { get; set; }
    public TextBlock? SubTextBlock { get; set; }
    public TranslateTransform TranslateTransform { get; set; } = null!;
    public ScaleTransform ScaleTransform { get; set; } = null!;
    public DropShadowEffect? LineGlowEffect { get; set; }
    public BlurEffect? InactiveBlurEffect { get; set; }
    public Border? IndicatorBar { get; set; }
    public DropShadowEffect? IndicatorGlow { get; set; }
    public double CurrentTranslateX { get; set; }
    public LyricLine Model { get; set; } = null!;
    public List<LyricWordVisualItem>? Words { get; set; }

    public double MeasuredHeight { get; set; }
    public double StaticTop { get; set; }
    public double StaticCenter { get; set; }

    public bool HasPrecedingInterlude { get; set; }
    public TimeSpan InterludeStart { get; set; }
    public TimeSpan InterludeEnd { get; set; }
    public double LineEndMs { get; set; }

    public double TargetY { get; set; }
    public double CurrentY { get; set; }
    public double VelocityY { get; set; }
    public double StartTimeOffset { get; set; }

    public double CurrentOpacity { get; set; } = 0.35;
    public double CurrentScale { get; set; } = 0.95;

    public bool IsSettled { get; set; } = false;
}

public class InterludeSlotItem
{
    public FrameworkElement Container { get; set; } = null!;
    public Ellipse Dot1 { get; set; } = null!;
    public Ellipse Dot2 { get; set; } = null!;
    public Ellipse Dot3 { get; set; } = null!;
    public TranslateTransform Dot1Transform { get; set; } = null!;
    public TranslateTransform Dot2Transform { get; set; } = null!;
    public TranslateTransform Dot3Transform { get; set; } = null!;
    public ScaleTransform Dot1Scale { get; set; } = null!;
    public ScaleTransform Dot2Scale { get; set; } = null!;
    public ScaleTransform Dot3Scale { get; set; } = null!;
    public ScaleTransform ContainerScale { get; set; } = null!;
    public double StaticTop { get; set; }
    public double CurrentY { get; set; }
    public double VelocityY { get; set; }
    public double CurrentScale { get; set; } = 1.0;
    public double CurrentOpacity { get; set; } = 0.0;
    public bool IsActive { get; set; } = false;
}
