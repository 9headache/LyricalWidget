using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace LyricalWidget.Rendering;

/// <summary>
/// Barrel-distorts a live visual on the GPU by painting it onto a curved mesh. Needs no shader compiler.
/// The mapping sends every point of the source inside the frame, so nothing can be pushed off screen.
/// </summary>
public sealed class FisheyeLens
{
    /// <summary>Barrel strength. The centre is enlarged by 1 / (1 - Strength).</summary>
    public const double Strength = 0.44;

    private const int Columns = 56;
    private const int Rows = 36;

    private readonly Viewport3D _viewport;
    private readonly FrameworkElement _source;
    private readonly OrthographicCamera _camera;
    private readonly ScaleTransform3D _scale = new(1, 1, 1);
    private VisualBrush? _brush;

    public FisheyeLens(Viewport3D viewport, FrameworkElement source)
    {
        _viewport = viewport;
        _source = source;
        _camera = new OrthographicCamera(new Point3D(0, 0, 10), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 2.0);
        _viewport.Camera = _camera;
        _source.SizeChanged += (s, e) => UpdateSize();
    }

    public bool IsActive => _brush != null;

    public void SetActive(bool active)
    {
        if (active == IsActive) return;

        if (!active)
        {
            _viewport.Children.Clear();
            _viewport.Visibility = Visibility.Collapsed;
            _brush = null;
            return;
        }

        _brush = new VisualBrush(_source)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.Linear);

        var model = new GeometryModel3D(BuildMesh(), new DiffuseMaterial(_brush)) { Transform = _scale };
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Colors.White));
        group.Children.Add(model);
        _viewport.Children.Add(new ModelVisual3D { Content = group });
        _viewport.Visibility = Visibility.Visible;
        UpdateSize();
    }

    private void UpdateSize()
    {
        double w = _source.ActualWidth;
        double h = _source.ActualHeight;
        if (_brush == null || w < 1 || h < 1) return;

        _brush.Viewbox = new Rect(0, 0, w, h);
        _camera.Width = w;
        _scale.ScaleX = w / 2.0;
        _scale.ScaleY = h / 2.0;
    }

    /// <summary>Where a source point (each axis -1..1) appears on screen: the inverse of r -> r * (1 - k + k * r²).</summary>
    public static Point Map(double u, double v)
    {
        double target = Math.Sqrt((u * u) + (v * v));
        if (target < 1e-9) return new Point(0, 0);

        double r = target;
        for (int i = 0; i < 8; i++)
        {
            double f = (r * (1.0 - Strength + (Strength * r * r))) - target;
            double slope = 1.0 - Strength + (3.0 * Strength * r * r);
            r -= f / slope;
        }
        double scale = r / target;
        return new Point(u * scale, v * scale);
    }

    private static MeshGeometry3D BuildMesh()
    {
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= Rows; y++)
        {
            for (int x = 0; x <= Columns; x++)
            {
                double tu = (double)x / Columns;
                double tv = (double)y / Rows;
                var p = Map((tu * 2.0) - 1.0, (tv * 2.0) - 1.0);
                double bulge = 1.0 - Math.Min(1.0, ((p.X * p.X) + (p.Y * p.Y)) * 0.5);
                mesh.Positions.Add(new Point3D(p.X, -p.Y, bulge));
                mesh.TextureCoordinates.Add(new Point(tu, tv));
            }
        }

        int stride = Columns + 1;
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                int a = (y * stride) + x;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(a + stride);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + stride);
                mesh.TriangleIndices.Add(a + stride + 1);
            }
        }
        mesh.Freeze();
        return mesh;
    }
}
