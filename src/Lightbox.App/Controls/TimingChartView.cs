using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// The timing chart editor (Q58): the animator's ladder, drawn the way it is
/// pencilled on the corner of an extreme. A horizontal rail runs from this
/// key (left) to the next (right); each rung is one inbetween, at its
/// fraction of the travel.
///
/// The hand does everything: <b>drag</b> a rung to re-space it, <b>click</b>
/// empty rail to add one where the click landed, <b>right-click</b> a rung
/// to remove it. The host owns the record — every edit raises
/// <see cref="ChartEdited"/> with the whole chart, normalised, and the new
/// list comes back through <see cref="Rungs"/>.
/// </summary>
public class TimingChartView : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> RungsProperty =
        AvaloniaProperty.Register<TimingChartView, IReadOnlyList<double>?>(nameof(Rungs));

    public IReadOnlyList<double>? Rungs
    {
        get => GetValue(RungsProperty);
        set => SetValue(RungsProperty, value);
    }

    /// <summary>The chart after an edit, in rail order. The host writes it to the record.</summary>
    public event Action<IReadOnlyList<double>>? ChartEdited;

    // Ends carry the extremes' posts; rungs live between them.
    private const double Inset = 14;
    private const double HitRadius = 7;

    // A drag ghosts locally and commits once on release: the host writes an
    // undo step per ChartEdited, and a step per pointer move would turn one
    // adjustment into forty undos.
    private int _dragIndex = -1;
    private double _dragValue;

    static TimingChartView()
    {
        AffectsRender<TimingChartView>(RungsProperty);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(availableSize.Width, 260), 56);

    private double XOf(double rung) => Inset + rung * (Bounds.Width - 2 * Inset);

    /// <summary>
    /// The fraction under x, clamped to what <see cref="Lightbox.Core.Inbetween.TimingChart.Normalise"/>
    /// will actually keep — the same bounds, so the ghost the hand follows is
    /// the rung the record gets. A looser clamp here let a drag settle where
    /// the commit then silently dropped it, which reads as the tool eating
    /// the artist's work.
    /// </summary>
    private double RungAt(double x)
    {
        var gap = Lightbox.Core.Inbetween.TimingChart.MinGap;
        return Math.Clamp(
            (x - Inset) / Math.Max(1, Bounds.Width - 2 * Inset), gap, 1 - gap);
    }

    /// <summary>
    /// <paramref name="value"/> pushed off its neighbours by the commit-time
    /// minimum gap, so a drag onto another rung lands beside it rather than
    /// being deduplicated away on release.
    /// </summary>
    private double AwayFromNeighbours(double value, int ignoreIndex)
    {
        var gap = Lightbox.Core.Inbetween.TimingChart.MinGap;
        if (Rungs is not { } rungs) return value;
        for (var i = 0; i < rungs.Count; i++)
        {
            if (i == ignoreIndex) continue;
            if (Math.Abs(rungs[i] - value) < gap)
            {
                value = value < rungs[i] ? rungs[i] - gap : rungs[i] + gap;
            }
        }
        return Math.Clamp(value, gap, 1 - gap);
    }

    private int RungIndexAt(Point p)
    {
        var rungs = Rungs;
        if (rungs is null) return -1;
        for (var i = 0; i < rungs.Count; i++)
        {
            if (Math.Abs(XOf(rungs[i]) - p.X) <= HitRadius) return i;
        }
        return -1;
    }

    public override void Render(DrawingContext context)
    {
        var mid = Bounds.Height / 2;
        var text = new SolidColorBrush(Color.Parse("#A6ABB8"));
        var rail = new Pen(new SolidColorBrush(Color.Parse("#5A5F6E")), 2);
        var post = new SolidColorBrush(Color.Parse("#B49CFF"));
        var rung = new SolidColorBrush(Color.Parse("#E8C55F"));
        var typeface = new Typeface(FontFamily.Default);

        context.DrawLine(rail, new Point(Inset, mid), new Point(Bounds.Width - Inset, mid));

        // The extremes' posts: taller than the rungs, so the ends read as
        // the drawings that already exist.
        foreach (var x in (double[])[Inset, Bounds.Width - Inset])
        {
            context.DrawRectangle(post, null, new Rect(x - 1.5, mid - 14, 3, 28));
        }

        if (Rungs is { } stored)
        {
            // Numbered in display order even mid-drag, so hauling rung 1
            // past rung 2 renumbers them under the hand instead of crossing
            // the labels.
            var shown = stored.Select((r, i) => i == _dragIndex ? _dragValue : r)
                .OrderBy(r => r);
            var number = 1;
            foreach (var r in shown)
            {
                var x = XOf(r);
                context.DrawRectangle(rung, null, new Rect(x - 1.5, mid - 9, 3, 18));
                var label = new FormattedText(
                    number.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 9, text);
                context.DrawText(label, new Point(x - label.Width / 2, mid + 11));
                number++;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var hit = RungIndexAt(p);

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (hit >= 0 && Rungs is { } rungs)
            {
                ChartEdited?.Invoke([.. rungs.Where((_, i) => i != hit)]);
            }
            e.Handled = true;
            return;
        }

        if (hit >= 0)
        {
            _dragIndex = hit;
            _dragValue = Rungs![hit];
            e.Pointer.Capture(this);
        }
        else if (p.X > Inset && p.X < Bounds.Width - Inset && Rungs is { } existing)
        {
            ChartEdited?.Invoke([.. existing, AwayFromNeighbours(RungAt(p.X), ignoreIndex: -1)]);
        }
        else if (p.X > Inset && p.X < Bounds.Width - Inset)
        {
            ChartEdited?.Invoke([RungAt(p.X)]);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragIndex < 0) return;
        _dragValue = AwayFromNeighbours(RungAt(e.GetPosition(this).X), _dragIndex);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragIndex < 0) return;
        var index = _dragIndex;
        _dragIndex = -1;
        e.Pointer.Capture(null);
        if (Rungs is { } rungs)
        {
            var moved = rungs.ToArray();
            moved[index] = _dragValue;
            ChartEdited?.Invoke(moved);
        }
        InvalidateVisual();
        e.Handled = true;
    }
}
