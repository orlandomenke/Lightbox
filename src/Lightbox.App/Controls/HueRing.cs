using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// The hue ring from the design reference: an annulus of hues with the SV
/// square sitting inside it (the square is its own control — this draws and
/// answers for the ring only). View-only colour picking; nothing here
/// touches a document.
/// </summary>
public class HueRing : Control
{
    /// <summary>
    /// The colour whose hue this ring shows and sets. The whole HSV value
    /// rather than a bare hue, so the ring and the SV square inside it bind
    /// the same view-model property and neither can drift from the other —
    /// the ring writes only the H component and passes S, V and alpha
    /// through untouched.
    /// </summary>
    public static readonly StyledProperty<HsvColor> HsvColorProperty =
        AvaloniaProperty.Register<HueRing, HsvColor>(
            nameof(HsvColor), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public HsvColor HsvColor
    {
        get => GetValue(HsvColorProperty);
        set => SetValue(HsvColorProperty, value);
    }

    private double Hue
    {
        get => HsvColor.H;
        set => HsvColor = new HsvColor(HsvColor.A, ((value % 360) + 360) % 360, HsvColor.S, HsvColor.V);
    }

    /// <summary>How thick the annulus is.</summary>
    public static readonly StyledProperty<double> RingWidthProperty =
        AvaloniaProperty.Register<HueRing, double>(nameof(RingWidth), 16);

    public double RingWidth
    {
        get => GetValue(RingWidthProperty);
        set => SetValue(RingWidthProperty, value);
    }

    static HueRing()
    {
        AffectsRender<HueRing>(HsvColorProperty, RingWidthProperty);
    }

    private IBrush? _ring;

    /// <summary>
    /// The full hue sweep as a conic gradient, stops every 30° so the
    /// interpolation error against true HSV stays invisible. Deterministic,
    /// so built once — the ring never changes, only the marker on it.
    /// </summary>
    private IBrush Ring()
    {
        if (_ring is not null) return _ring;
        var stops = new GradientStops();
        for (var h = 0; h <= 360; h += 30)
        {
            stops.Add(new GradientStop(HueRgb.ToRgb(h % 360, 1, 1), h / 360.0));
        }
        return _ring = new ConicGradientBrush { GradientStops = stops, Angle = 0 };
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - RingWidth) / 2;

        context.DrawEllipse(null, new Pen(Ring(), RingWidth), centre, radius, radius);

        // The marker: a dot riding the ring, the same vocabulary as the
        // sliders' thumb. White ring over the hue it names, so it reads on
        // every colour.
        var at = MarkerAt(Hue, centre, radius);
        var r = RingWidth / 2 + 1;
        context.DrawEllipse(
            new SolidColorBrush(HueRgb.ToRgb(((Hue % 360) + 360) % 360, 1, 1)),
            new Pen(Brushes.White, 2), at, r, r);
    }

    /// <summary>Where hue <paramref name="hue"/> sits on the ring.</summary>
    internal static Point MarkerAt(double hue, Point centre, double radius)
    {
        var rad = hue * Math.PI / 180;
        return new Point(centre.X + radius * Math.Sin(rad), centre.Y - radius * Math.Cos(rad));
    }

    /// <summary>The hue under a pointer position — the inverse of <see cref="MarkerAt"/>.</summary>
    internal static double HueFromPoint(Point p, Point centre)
    {
        var deg = Math.Atan2(p.X - centre.X, centre.Y - p.Y) * 180 / Math.PI;
        return (deg + 360) % 360;
    }

    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Only the annulus grips: the SV square sits in the middle, and a
        // press in a corner (inside this control's bounds, outside the ring)
        // should fall through to whatever is really there.
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var p = e.GetPosition(this);
        var radius = (Math.Min(Bounds.Width, Bounds.Height) - RingWidth) / 2;
        var d = Math.Sqrt(Math.Pow(p.X - centre.X, 2) + Math.Pow(p.Y - centre.Y, 2));
        if (Math.Abs(d - radius) > RingWidth) return;

        _dragging = true;
        e.Pointer.Capture(this);
        Hue = HueFromPoint(p, centre);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        // No band check while dragging: a hue drag is circular, and losing
        // the grip because the hand drifted inward is the classic wheel
        // frustration every real picker avoids.
        Hue = HueFromPoint(e.GetPosition(this), centre);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}

/// <summary>HSV→RGB for the ring's own drawing. S and V are fixed at 1 there.</summary>
internal static class HueRgb
{
    public static Color ToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
