using System.Windows;
using System.Windows.Media;
using BoostParaPc.Services;

namespace BoostParaPc.Controls;

/// <summary>Gráfico de linha compacto. Os pontos vêm de <see cref="SparklineMath.ToPoints"/>.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values),
        typeof(IReadOnlyList<double>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum),
        typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke),
        typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight - 2;
        var points = SparklineMath.ToPoints(Values ?? [], width, height, Maximum);
        if (points.Count < 2) return;
        var figure = new PathFigure { StartPoint = new Point(points[0].X, points[0].Y + 1), IsClosed = false };
        foreach (var (x, y) in points.Skip(1)) figure.Segments.Add(new LineSegment(new Point(x, y + 1), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, geometry);
    }
}
