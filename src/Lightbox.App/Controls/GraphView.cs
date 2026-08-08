using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// One curve in the graph editor, as plain data. <paramref name="Samples"/>
/// holds one value per frame (NaN = no value there — the spacing series only
/// exists where a drawing was measured); <paramref name="KeyFrames"/> marks
/// the frames wearing a draggable dot when <paramref name="Editable"/>.
/// </summary>
/// <param name="Dashed">
/// Drawn as a dashed line with hollow marks — the treatment for an INTENDED
/// curve laid over a measured one.
/// </param>
public sealed record GraphSeries(
    string Name,
    Color Colour,
    IReadOnlyList<double> Samples,
    IReadOnlyList<int> KeyFrames,
    bool Editable,
    bool Dashed = false);

/// <summary>
/// The graph editor: value over time for the things that interpolate — the
/// camera's framing, and the measured spacing of the drawings themselves.
/// Each series is normalised to its own vertical range, because "is the
/// motion eased" is a question about the curve's shape, not its units.
/// </summary>
public class GraphView : Control
{
    public static readonly StyledProperty<IReadOnlyList<GraphSeries>?> SeriesProperty =
        AvaloniaProperty.Register<GraphView, IReadOnlyList<GraphSeries>?>(nameof(Series));

    public IReadOnlyList<GraphSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public static readonly StyledProperty<double> FrameWidthProperty =
        AvaloniaProperty.Register<GraphView, double>(nameof(FrameWidth), 14);

    public double FrameWidth
    {
        get => GetValue(FrameWidthProperty);
        set => SetValue(FrameWidthProperty, value);
    }

    public static readonly StyledProperty<int> CurrentFrameProperty =
        AvaloniaProperty.Register<GraphView, int>(
            nameof(CurrentFrame), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public int CurrentFrame
    {
        get => GetValue(CurrentFrameProperty);
        set => SetValue(CurrentFrameProperty, value);
    }

    public static readonly StyledProperty<int> FrameCountProperty =
        AvaloniaProperty.Register<GraphView, int>(nameof(FrameCount), 1);

    public int FrameCount
    {
        get => GetValue(FrameCountProperty);
        set => SetValue(FrameCountProperty, value);
    }

    /// <summary>
    /// A key dot finished its drag: series name, the frame it left, the frame
    /// it landed on, and the value at the drop height (denormalised into the
    /// series' own units). The host writes the document.
    /// </summary>
    public event Action<string, int, int, double>? KeyEdited;

    /// <summary>A double-click on the plot: author a key at this frame.</summary>
    public event Action<int>? KeyAddRequested;

    /// <summary>
    /// A right-click landed on a key dot: the host offers the key's menu
    /// (easing, removal) at the given position in this control's coordinates.
    /// </summary>
    public event Action<string, int, Point>? KeyMenuRequested;

    internal const double Gutter = 46;      // the value axis
    internal const double RulerHeight = 20;
    private const double Pad = 12;          // above and below the plot
    private const double DotRadius = 4.5;

    static GraphView()
    {
        AffectsMeasure<GraphView>(SeriesProperty, FrameWidthProperty, FrameCountProperty);
        AffectsRender<GraphView>(SeriesProperty, FrameWidthProperty, CurrentFrameProperty, FrameCountProperty);
    }

    // ---- geometry ---------------------------------------------------------------

    internal static double XAtFrame(int frame, double frameWidth) =>
        Gutter + (frame + 0.5) * frameWidth;

    internal static int FrameAtX(double x, double frameWidth, int frameCount) =>
        Math.Clamp((int)Math.Floor((x - Gutter) / frameWidth), 0, Math.Max(0, frameCount - 1));

    /// <summary>The series' vertical range, padded so a flat line is not glued to an edge.</summary>
    internal static (double Min, double Max) RangeOf(GraphSeries series)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var v in series.Samples)
        {
            if (double.IsNaN(v)) continue;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        if (double.IsInfinity(min)) return (0, 1);
        if (Math.Abs(max - min) < 1e-9) return (min - 0.5, max + 0.5);
        var margin = (max - min) * 0.08;
        return (min - margin, max + margin);
    }

    internal static double YAtValue(double value, (double Min, double Max) range, double plotHeight) =>
        RulerHeight + Pad + (range.Max - value) / (range.Max - range.Min) * plotHeight;

    internal static double ValueAtY(double y, (double Min, double Max) range, double plotHeight) =>
        range.Max - (y - RulerHeight - Pad) / plotHeight * (range.Max - range.Min);

    private double PlotHeight => Math.Max(20, Bounds.Height - RulerHeight - 2 * Pad);

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            Gutter + FrameCount * FrameWidth + 24,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);
    }

    // ---- painting -----------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        var series = Series;
        if (series is null || series.Count == 0) return;

        var text = new SolidColorBrush(Color.Parse("#A6ABB8"));
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        var typeface = new Typeface(Avalonia.Media.FontFamily.Default);

        // The light grid: per-frame verticals when there is room for them, and
        // a few horizontal bands across the plot. Barely-there on purpose —
        // the grid is for judging a curve's shape, never a thing to look at.
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)), 1);
        if (FrameWidth >= 8)
        {
            for (var f = 0; f < FrameCount; f++)
            {
                if (f % 12 == 0) continue;   // the ruler line below already covers it
                var gx = XAtFrame(f, FrameWidth);
                context.DrawLine(grid, new Point(gx, RulerHeight), new Point(gx, Bounds.Height));
            }
        }
        for (var band = 0; band <= 4; band++)
        {
            var gy = RulerHeight + Pad + band / 4.0 * PlotHeight;
            context.DrawLine(grid, new Point(Gutter, gy), new Point(Bounds.Width, gy));
        }

        for (var f = 0; f < FrameCount; f += 12)
        {
            var x = XAtFrame(f, FrameWidth);
            context.DrawLine(faint, new Point(x, RulerHeight), new Point(x, Bounds.Height));
            var label = new FormattedText(
                (f + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, text);
            context.DrawText(label, new Point(x - label.Width / 2, 2));
        }

        // With exactly one series on the plot its units are unambiguous, so
        // the axis says them; with several, every range differs and numbers
        // would lie about all but one curve.
        if (series.Count == 1)
        {
            var only = series[0];
            var range0 = RangeOf(only);
            var axis = new SolidColorBrush(only.Colour);
            foreach (var (v, label) in new[]
            {
                (range0.Max, range0.Max.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                ((range0.Min + range0.Max) / 2, ((range0.Min + range0.Max) / 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                (range0.Min, range0.Min.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
            })
            {
                var t = new FormattedText(
                    label, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 9, axis);
                context.DrawText(t, new Point(
                    Gutter - t.Width - 6, YAtValue(v, range0, PlotHeight) - t.Height / 2));
            }
        }

        foreach (var s in series)
        {
            var range = RangeOf(s);
            var pen = new Pen(new SolidColorBrush(s.Colour), 1.8)
            {
                DashStyle = s.Dashed ? new DashStyle([3, 3], 0) : null,
            };
            Point? last = null;
            for (var f = 0; f < Math.Min(s.Samples.Count, FrameCount); f++)
            {
                var v = s.Samples[f];
                if (double.IsNaN(v))
                {
                    last = null;
                    continue;
                }
                var at = new Point(XAtFrame(f, FrameWidth), YAtValue(v, range, PlotHeight));
                if (last is { } from) context.DrawLine(pen, from, at);
                last = at;
            }

            foreach (var key in s.KeyFrames)
            {
                if (key < 0 || key >= s.Samples.Count || double.IsNaN(s.Samples[key])) continue;
                var at = new Point(XAtFrame(key, FrameWidth), YAtValue(s.Samples[key], range, PlotHeight));
                if (s.Editable)
                {
                    context.DrawEllipse(new SolidColorBrush(s.Colour), new Pen(Brushes.White, 1.5), at, DotRadius, DotRadius);
                }
                else if (s.Dashed)
                {
                    // Hollow: the intent, not a measurement.
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(s.Colour), 1.5), at, 3, 3);
                }
                else
                {
                    context.DrawEllipse(new SolidColorBrush(s.Colour), null, at, 3, 3);
                }
            }
        }

        // The dragged dot's ghost, with the value it would set — dragging
        // blind was the readout gap this round exists to close.
        if (_drag is { } d)
        {
            var s = series[d.SeriesIndex];
            var ghost = new Point(XAtFrame(d.ToFrame, FrameWidth), d.Y);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(0x88, s.Colour.R, s.Colour.G, s.Colour.B)), null,
                ghost, DotRadius, DotRadius);
            var value = ValueAtY(d.Y, RangeOf(s), PlotHeight);
            var chip = new FormattedText(
                value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, Brushes.White);
            var box = new Rect(ghost.X + 8, ghost.Y - chip.Height / 2 - 3, chip.Width + 10, chip.Height + 6);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#CC26292F")), null, new RoundedRect(box, 3));
            context.DrawText(chip, new Point(box.X + 5, box.Y + 3));
        }

        var playX = XAtFrame(CurrentFrame, FrameWidth);
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#B49CFF")), 1.5),
            new Point(playX, 0), new Point(playX, Bounds.Height));
    }

    // ---- interaction ----------------------------------------------------------------

    private (int SeriesIndex, int FromFrame, int ToFrame, double Y)? _drag;
    private bool _scrubbing;

    /// <summary>The editable key dot under the pointer, or null.</summary>
    private (int SeriesIndex, int Frame, Point At)? DotAt(Point p)
    {
        var series = Series;
        if (series is null) return null;
        for (var si = 0; si < series.Count; si++)
        {
            var s = series[si];
            if (!s.Editable) continue;
            var range = RangeOf(s);
            foreach (var key in s.KeyFrames)
            {
                if (key < 0 || key >= s.Samples.Count || double.IsNaN(s.Samples[key])) continue;
                var at = new Point(XAtFrame(key, FrameWidth), YAtValue(s.Samples[key], range, PlotHeight));
                if (Math.Abs(at.X - p.X) <= 7 && Math.Abs(at.Y - p.Y) <= 7) return (si, key, at);
            }
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var series = Series;
        if (series is null || series.Count == 0) return;
        var p = e.GetPosition(this);
        if (p.X < Gutter) return;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (DotAt(p) is { } hit)
            {
                KeyMenuRequested?.Invoke(series[hit.SeriesIndex].Name, hit.Frame, p);
                e.Handled = true;
            }
            return;
        }

        // A double-click authors a key at the clicked frame — the graph can
        // edit keys, so it must be able to make one.
        if (e.ClickCount == 2)
        {
            KeyAddRequested?.Invoke(FrameAtX(p.X, FrameWidth, FrameCount));
            e.Handled = true;
            return;
        }

        if (DotAt(p) is { } grabbed)
        {
            _drag = (grabbed.SeriesIndex, grabbed.Frame, grabbed.Frame, grabbed.At.Y);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _scrubbing = true;
        e.Pointer.Capture(this);
        CurrentFrame = FrameAtX(p.X, FrameWidth, FrameCount);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag is { } d)
        {
            _drag = (d.SeriesIndex, d.FromFrame,
                FrameAtX(p.X, FrameWidth, FrameCount),
                Math.Clamp(p.Y, RulerHeight + Pad, RulerHeight + Pad + PlotHeight));
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            CurrentFrame = FrameAtX(p.X, FrameWidth, FrameCount);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is { } d)
        {
            _drag = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            var s = Series![d.SeriesIndex];
            var value = ValueAtY(d.Y, RangeOf(s), PlotHeight);
            KeyEdited?.Invoke(s.Name, d.FromFrame, d.ToFrame, value);
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            _scrubbing = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
