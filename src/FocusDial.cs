using System.Windows;
using System.Windows.Media;

namespace Pulso;

public sealed class FocusDial : FrameworkElement
{
    private static readonly DependencyProperty MinorTick = BrushProperty("MinorTick");
    private static readonly DependencyProperty MajorTick = BrushProperty("MajorTick");
    private static readonly DependencyProperty Progress = BrushProperty("Progress");
    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush),
        typeof(FocusDial), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public FocusDial()
    {
        SetResourceReference(MinorTick, "DialTrack");
        SetResourceReference(MajorTick, "HeroMuted");
        SetResourceReference(Progress, "Mint");
    }
    private double fraction = -1;
    public void Set(double value)
    {
        if (Math.Abs(value - fraction) < .00001) return;
        fraction = value;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 5;
        if (radius < 10) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var dim = (Brush)GetValue(MinorTick);
        var bright = (Brush)GetValue(MajorTick);
        var needle = (Brush)GetValue(Progress);
        Point At(double angle, double r)
        {
            double a = angle * Math.PI / 180;
            return new(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r);
        }
        const int slots = 50;
        for (int i = 0; i <= slots; i++)
        {
            bool major = i % 5 == 0;
            double angle = 135 + i * 270d / slots;
            var pen = new Pen(i <= fraction * slots ? needle : major ? bright : dim, major ? 2.6 : 1.35);
            dc.DrawLine(pen, At(angle, radius - (major ? 14 : 8)), At(angle, radius));
        }
        var marker = At(135 + Math.Clamp(fraction, 0, 1) * 270, radius - 21);
        dc.DrawEllipse(needle, null, marker, 3, 3);
    }
}
