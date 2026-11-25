using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pulso;

public sealed class FocusDial : Control
{
    public static readonly StyledProperty<IBrush?> MinorTickProperty =
        AvaloniaProperty.Register<FocusDial, IBrush?>(nameof(MinorTick));
    public static readonly StyledProperty<IBrush?> MajorTickProperty =
        AvaloniaProperty.Register<FocusDial, IBrush?>(nameof(MajorTick));
    public static readonly StyledProperty<IBrush?> ProgressProperty =
        AvaloniaProperty.Register<FocusDial, IBrush?>(nameof(Progress));

    static FocusDial()
    {
        AffectsRender<FocusDial>(MinorTickProperty, MajorTickProperty, ProgressProperty);
    }

    public FocusDial()
    {
        this[!MinorTickProperty] = this.GetResourceObservable("DialTrack").ToBinding();
        this[!MajorTickProperty] = this.GetResourceObservable("HeroMuted").ToBinding();
        this[!ProgressProperty] = this.GetResourceObservable("Mint").ToBinding();
    }

    public IBrush? MinorTick { get => GetValue(MinorTickProperty); set => SetValue(MinorTickProperty, value); }
    public IBrush? MajorTick { get => GetValue(MajorTickProperty); set => SetValue(MajorTickProperty, value); }
    public IBrush? Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    private double fraction = -1;

    public void Set(double value)
    {
        if (Math.Abs(value - fraction) < .00001) return;
        fraction = value;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width, height = Bounds.Height;
        double radius = Math.Min(width, height) / 2 - 5;
        if (radius < 10) return;

        var center = new Point(width / 2, height / 2);
        var dim = MinorTick ?? Brushes.Transparent;
        var bright = MajorTick ?? Brushes.Transparent;
        var needle = Progress ?? Brushes.Transparent;

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
            context.DrawLine(pen, At(angle, radius - (major ? 14 : 8)), At(angle, radius));
        }

        var marker = At(135 + Math.Clamp(fraction, 0, 1) * 270, radius - 21);
        context.DrawEllipse(needle, null, marker, 3, 3);
    }
}
