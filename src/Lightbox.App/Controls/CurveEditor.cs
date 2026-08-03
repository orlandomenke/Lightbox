using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lightbox.Core.Documents;

namespace Lightbox.App.Controls;

/// <summary>
/// A pressure response, drawn and dragged. Pressure runs left to right, the
/// multiplier bottom to top.
/// </summary>
/// <remarks>
/// <para>
/// A gamma spinner is a description of a response; this is the response. An
/// artist deciding how a brush should answer the pen is making a shape
/// judgement — where it should bite, where it should level off — and a single
/// exponent cannot express most of the shapes they mean. It also cannot express
/// any shape that goes up and then down, which is what an ink brush that
/// spreads and then floods actually does.
/// </para>
/// <para>
/// The control draws and reports; it does not decide. Every edit goes out as a
/// whole new curve on <see cref="CurveChanged"/>, so the change lands through
/// the view model with the rest of the brush rather than mutating a record the
/// view model believes it owns.
/// </para>
/// </remarks>
public class CurveEditor : Control
{
    public static readonly StyledProperty<ResponseCurve?> CurveProperty =
        AvaloniaProperty.Register<CurveEditor, ResponseCurve?>(nameof(Curve));

    /// <summary>Draw greyed and ignore the pointer — for a dynamic that is switched off.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<CurveEditor, bool>(nameof(IsActive), defaultValue: true);

    public ResponseCurve? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>The artist changed the shape. The argument is a fresh curve.</summary>
    public event Action<ResponseCurve>? CurveChanged;

    /// <summary>How close a click has to be to a handle, in pixels.</summary>
    private const double Grab = 8;

    private const double Pad = 6;

    static CurveEditor()
    {
        AffectsRender<CurveEditor>(CurveProperty, IsActiveProperty);
        FocusableProperty.OverrideDefaultValue<CurveEditor>(true);
    }

    public CurveEditor()
    {
        Height = 96;
        MinWidth = 96;
    }

    // ---- geometry -----------------------------------------------------------

    private Rect Plot => new(
        Pad, Pad, Math.Max(1, Bounds.Width - Pad * 2), Math.Max(1, Bounds.Height - Pad * 2));

    private Point ToScreen(CurvePoint p) =>
        new(Plot.X + p.In * Plot.Width, Plot.Bottom - p.Out * Plot.Height);

    private CurvePoint ToCurve(Point p) => new(
        Math.Clamp((p.X - Plot.X) / Plot.Width, 0, 1),
        Math.Clamp((Plot.Bottom - p.Y) / Plot.Height, 0, 1));

    private List<CurvePoint> Handles() => Curve?.Points is { Count: > 0 } points
        ? points.OrderBy(q => q.In).ToList()
        : [.. ResponseCurve.Linear().Points];

    private int HandleAt(Point p)
    {
        var handles = Handles();
        for (var i = 0; i < handles.Count; i++)
        {
            var s = ToScreen(handles[i]);
            if (Math.Abs(s.X - p.X) <= Grab && Math.Abs(s.Y - p.Y) <= Grab) return i;
        }
        return -1;
    }

    // ---- input --------------------------------------------------------------

    private int _dragging = -1;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsActive) return;

        var p = e.GetPosition(this);
        var handles = Handles();
        var hit = HandleAt(p);
        var point = e.GetCurrentPoint(this);

        if (hit >= 0)
        {
            // Middle-click removes, matching the gradient ramp — no dialog for
            // a small destructive act that undo reverses. The first and last
            // handles stay: a curve with no end is not a response, and their
            // heights are still draggable, which is what somebody wanting to
            // "remove" an end actually means.
            if (point.Properties.IsMiddleButtonPressed)
            {
                if (hit > 0 && hit < handles.Count - 1)
                {
                    handles.RemoveAt(hit);
                    Publish(handles);
                }
                e.Handled = true;
                return;
            }

            _dragging = hit;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;
        if (handles.Count >= ResponseCurve.MaxPoints) return;

        // Clicking empty space adds a handle there and starts dragging it, so
        // placing a point and putting it where you want it is one gesture.
        var added = ToCurve(p);
        var at = handles.FindIndex(q => q.In > added.In);
        if (at < 0) at = handles.Count;
        handles.Insert(at, added);
        _dragging = at;
        e.Pointer.Capture(this);
        Publish(handles);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging < 0) return;

        var handles = Handles();
        if (_dragging >= handles.Count) { _dragging = -1; return; }

        var moved = ToCurve(e.GetPosition(this));

        // The ends keep their pressure. Dragging the handle at full pressure
        // sideways would leave the curve saying nothing about the pen's top
        // end, and what it says there is flat — so the brush would appear to
        // stop responding rather than to have been edited.
        if (_dragging == 0) moved = moved with { In = 0 };
        else if (_dragging == handles.Count - 1) moved = moved with { In = 1 };
        else
        {
            // Kept between its neighbours, so a drag past one cannot fold the
            // curve back on itself.
            var low = handles[_dragging - 1].In + 0.01;
            var high = handles[_dragging + 1].In - 0.01;
            moved = moved with { In = high <= low ? low : Math.Clamp(moved.In, low, high) };
        }

        handles[_dragging] = moved;
        Publish(handles);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging < 0) return;
        _dragging = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = -1;
    }

    private void Publish(List<CurvePoint> handles)
    {
        var curve = new ResponseCurve { Points = handles };
        Curve = curve;
        CurveChanged?.Invoke(curve);
        InvalidateVisual();
    }

    /// <summary>Drive an edit from a test, where synthetic pointer input is unreliable.</summary>
    internal void DragHandleForTest(int index, double input, double output)
    {
        var handles = Handles();
        if (index < 0 || index >= handles.Count) return;
        handles[index] = new CurvePoint(Math.Clamp(input, 0, 1), Math.Clamp(output, 0, 1));
        Publish(handles);
    }

    // ---- drawing ------------------------------------------------------------

    private static readonly IBrush Frame = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));
    private static readonly IBrush Field = new SolidColorBrush(Color.FromRgb(0x1b, 0x1b, 0x1b));
    private static readonly IBrush Grid = new SolidColorBrush(Color.FromRgb(0x2c, 0x2c, 0x2c));
    private static readonly IBrush Live = new SolidColorBrush(Color.FromRgb(0x7f, 0xd0, 0xff));
    private static readonly IBrush Dead = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    public override void Render(DrawingContext context)
    {
        var plot = Plot;
        context.FillRectangle(Field, new Rect(Bounds.Size));
        context.DrawRectangle(null, new Pen(Frame), new Rect(Bounds.Size));

        var grid = new Pen(Grid);
        for (var i = 1; i < 4; i++)
        {
            var x = plot.X + plot.Width * i / 4;
            var y = plot.Y + plot.Height * i / 4;
            context.DrawLine(grid, new Point(x, plot.Y), new Point(x, plot.Bottom));
            context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
        }

        // The identity, so the shape is read against "pressure straight
        // through" rather than against nothing.
        context.DrawLine(
            new Pen(Grid, dashStyle: DashStyle.Dash),
            new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Y));

        var ink = IsActive ? Live : Dead;
        var curve = Curve ?? ResponseCurve.Linear();

        // Sampled rather than drawn as beziers: the interpolant the engine uses
        // is the monotone cubic, and reproducing it in Avalonia's curve
        // primitives would be a second implementation to keep in step. One
        // sample per pixel is exact enough at this size and cannot drift.
        var steps = Math.Max(16, (int)plot.Width);
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            for (var i = 0; i <= steps; i++)
            {
                var x = i / (double)steps;
                var p = new Point(plot.X + x * plot.Width, plot.Bottom - curve.Evaluate(x) * plot.Height);
                if (i == 0) sink.BeginFigure(p, isFilled: false);
                else sink.LineTo(p);
            }
            sink.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(ink, 1.6), geometry);

        if (!IsActive) return;

        foreach (var handle in Handles())
        {
            var s = ToScreen(handle);
            context.DrawEllipse(Field, new Pen(ink, 1.4), s, 3.5, 3.5);
        }
    }
}
