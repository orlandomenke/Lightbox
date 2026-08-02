using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// A ruler along one edge of the canvas, and the place guides come from.
/// </summary>
/// <remarks>
/// <para>
/// Photoshop's arrangement, deliberately: rulers on the top and left, ticks in
/// document pixels, a marker sliding along both as the pointer moves, and a
/// guide made by dragging out of a ruler into the canvas. Nothing floats over
/// the drawing — the whole reason to build it this way is that an overlay of
/// little buttons sits between the artist and the artwork, which is the thing
/// to avoid.
/// </para>
/// <para>
/// The strip owns no state about the document. It asks the canvas where a
/// document coordinate lands and reports back where a pointer was; deciding
/// what a drag means is the window's job.
/// </para>
/// </remarks>
public sealed class RulerStrip : Control
{
    /// <summary>How thick the strip is. Enough for a label, and no more.</summary>
    public const double Thickness = 18;

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<RulerStrip, Orientation>(nameof(Orientation));

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Where a document coordinate lands along this strip, in strip pixels,
    /// and how many document pixels one strip pixel covers.
    /// </summary>
    /// <remarks>
    /// Supplied by the window rather than read off the canvas, so the strip
    /// has no reference to the renderer and can be tested with two numbers.
    /// </remarks>
    public (double Origin, double Scale) Mapping
    {
        get => _mapping;
        set
        {
            _mapping = value;
            InvalidateVisual();
        }
    }

    private (double Origin, double Scale) _mapping = (0, 1);

    /// <summary>
    /// Where the pointer is, in document pixels along this axis, or null when
    /// it is not over the canvas.
    /// </summary>
    /// <remarks>
    /// The tracking marker is most of what a ruler is for. Reading a tick to
    /// find out where you are is slower than glancing at a line that is
    /// already there.
    /// </remarks>
    public double? Tracking
    {
        get => _tracking;
        set
        {
            if (_tracking.Equals(value)) return;
            _tracking = value;
            InvalidateVisual();
        }
    }

    private double? _tracking;

    /// <summary>Document positions to mark — the guides already placed.</summary>
    public IReadOnlyList<double> Marks
    {
        get => _marks;
        set
        {
            _marks = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<double> _marks = [];

    /// <summary>
    /// A drag has begun out of this ruler. The point is in the strip's own
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// A raw point rather than a document coordinate, because the axis that
    /// matters is the other one: dragging out of the top ruler places a
    /// <i>horizontal</i> guide, and where it goes is the pointer's height, not
    /// the position along the ruler. Only the window knows where the canvas
    /// is, so only the window can turn that into a document coordinate.
    /// </remarks>
    public event Action<RulerStrip, Point>? PullStarted;

    public event Action<RulerStrip, Point>? PullMoved;

    /// <param name="onStrip">
    /// True when the pointer came back onto the ruler — dragging a guide back
    /// where it came from is how Photoshop throws one away, and the same
    /// gesture cancels one you never wanted.
    /// </param>
    public event Action<RulerStrip, Point, bool>? PullEnded;

    private bool _pulling;

    /// <summary>The document coordinate under a point on this strip.</summary>
    public double DocAt(Point p)
    {
        var along = Orientation == Orientation.Horizontal ? p.X : p.Y;
        return Math.Abs(_mapping.Scale) < 1e-9 ? 0 : (along - _mapping.Origin) / _mapping.Scale;
    }

    /// <summary>Where a document coordinate sits on this strip.</summary>
    public double StripAt(double doc) => _mapping.Origin + doc * _mapping.Scale;

    protected override Size MeasureOverride(Size available) =>
        Orientation == Orientation.Horizontal
            ? new Size(available.Width, Thickness)
            : new Size(Thickness, available.Height);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pulling = true;
        e.Pointer.Capture(this);
        PullStarted?.Invoke(this, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_pulling) return;
        // Measured in the strip's own frame even once the pointer has left it:
        // a drag that stopped tracking the moment it crossed onto the canvas
        // would be a guide you could only place on the ruler.
        PullMoved?.Invoke(this, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_pulling) return;
        _pulling = false;
        e.Pointer.Capture(null);
        var p = e.GetPosition(this);
        PullEnded?.Invoke(this, p, IsOnStrip(p));
        e.Handled = true;
    }

    /// <summary>Whether a point in strip coordinates is still on the strip.</summary>
    public bool IsOnStrip(Point p) =>
        Orientation == Orientation.Horizontal
            ? p.Y >= 0 && p.Y <= Bounds.Height
            : p.X >= 0 && p.X <= Bounds.Width;

    /// <summary>
    /// Choose a tick spacing that reads at this zoom.
    /// </summary>
    /// <remarks>
    /// The 1-2-5 sequence every ruler uses: it keeps labels at round numbers
    /// whatever the zoom, which is the only reason to have numbers on a ruler
    /// at all. Public and static so the choice can be checked without a window.
    /// </remarks>
    public static double TickStep(double scale)
    {
        // Aim for a label roughly every 80 screen pixels.
        var target = 80 / Math.Max(1e-6, scale);
        var power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-6, target))));
        foreach (var step in (double[])[1, 2, 5, 10])
        {
            if (power * step >= target) return power * step;
        }
        return power * 10;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)), bounds);

        var horizontal = Orientation == Orientation.Horizontal;
        var length = horizontal ? Bounds.Width : Bounds.Height;
        // A rotated view collapses the scale towards zero and the numbers stop
        // meaning anything; a mirrored one makes it negative, which is fine —
        // the ruler then counts the way the drawing reads.
        if (length <= 0 || Math.Abs(_mapping.Scale) < 0.001) return;

        var step = TickStep(Math.Abs(_mapping.Scale));
        var end = DocAt(horizontal ? new Point(length, 0) : new Point(0, length));
        var lo = Math.Min(DocAt(default), end);
        var hi = Math.Max(DocAt(default), end);
        var first = Math.Floor(lo / step) * step;

        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), 1);
        var text = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

        for (var doc = first; doc <= hi; doc += step)
        {
            var at = Math.Round(StripAt(doc)) + 0.5;
            if (at < 0 || at > length) continue;
            context.DrawLine(
                tickPen,
                horizontal ? new Point(at, Thickness - 6) : new Point(Thickness - 6, at),
                horizontal ? new Point(at, Thickness) : new Point(Thickness, at));

            var label = new FormattedText(
                $"{doc:0}", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 9, text);
            context.DrawText(
                label,
                horizontal ? new Point(at + 2, 1) : new Point(1, at + 2));
        }

        // Guides already placed, so the ruler shows the rig at a glance.
        var markPen = new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x96, 0xf0)), 2);
        foreach (var mark in _marks)
        {
            var at = Math.Round(StripAt(mark)) + 0.5;
            if (at < 0 || at > length) continue;
            context.DrawLine(
                markPen,
                horizontal ? new Point(at, 0) : new Point(0, at),
                horizontal ? new Point(at, Thickness) : new Point(Thickness, at));
        }

        // The pointer, last so it is on top of everything else.
        if (_tracking is not { } tracking) return;
        var here = Math.Round(StripAt(tracking)) + 0.5;
        if (here < 0 || here > length) return;
        var pointerPen = new Pen(new SolidColorBrush(Color.FromRgb(0xf0, 0xa0, 0x50)), 1);
        context.DrawLine(
            pointerPen,
            horizontal ? new Point(here, 0) : new Point(0, here),
            horizontal ? new Point(here, Thickness) : new Point(Thickness, here));
    }
}
