using Avalonia;

namespace Lightbox.App.Rendering;

/// <summary>
/// Reference chrome on the canvas: the grid gizmos of a sheet being sliced, and
/// the box the Arrow puts round a projected reference it has hold of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Extracted from <c>CanvasControl.cs</c> to pay for the selection box</b>
/// (Q108). The ratchet on that file says a feature does not get to grow it — a
/// partial or a collaborator is what new code is for — and this block was
/// already a self-contained subject sitting in the middle of it: a record, a
/// property, four events and one gesture, touching nothing else in the control
/// except the zoom. Moving it lowered the budget by more than the new work
/// spends.
/// </para>
/// <para>
/// <b>Two gestures, one vocabulary.</b> A grid box and a whole projected sheet
/// are drawn the same way, grabbed the same way and resized from their corners
/// the same way, because to an artist they are the same kind of thing — a
/// rectangle of reference that can be put where it belongs. What differs is
/// what a drag <em>means</em>, and that is the window's to decide: the canvas
/// says which box and how far, exactly as it already did for the grid.
/// </para>
/// </remarks>
public sealed partial class CanvasControl
{
    /// <summary>
    /// One editable box on a reference sheet, in document coordinates.
    /// </summary>
    /// <remarks>
    /// A snapshot, not a live view of the cell. The renderer runs on another
    /// thread and must never read a document object that the UI thread may be
    /// halfway through editing.
    /// </remarks>
    public readonly record struct ReferenceBox(
        float X, float Y, float W, float H, float PivotX, float PivotY, bool Selected);

    /// <summary>
    /// The reference grid's gizmos, or null when the mode is off — in which
    /// case nothing grid-related is drawn at all.
    /// </summary>
    public IReadOnlyList<ReferenceBox>? ReferenceBoxes
    {
        get => _referenceBoxes;
        set
        {
            _referenceBoxes = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<ReferenceBox>? _referenceBoxes;
    // ---- the reference grid gesture -------------------------------------------

    /// <summary>What a press in grid-edit mode turned out to be.</summary>
    private enum GridGesture
    {
        None,
        Move,
        Resize,
        Draw,
    }

    private GridGesture _gridGesture;
    private int _gridIndex = -1;
    private bool _gridLeft, _gridTop;
    private Point _gridLast;
    private Point _gridStart;

    /// <summary>A box was picked, or the selection was cleared with -1.</summary>
    public event Action<int>? ReferenceBoxPicked;

    /// <summary>Move box <c>index</c> by a document-space delta.</summary>
    public event Action<int, double, double>? ReferenceBoxMoved;

    /// <summary>Resize box <c>index</c> from the corner named by the two flags.</summary>
    public event Action<int, bool, bool, double, double>? ReferenceBoxResized;

    /// <summary>A box drawn by hand, in document coordinates.</summary>
    public event Action<double, double, double, double>? ReferenceBoxDrawn;

    /// <summary>
    /// Decide what a press in grid-edit mode means: a corner grabs a resize,
    /// the inside of a box grabs a move, and empty canvas draws a new one.
    /// </summary>
    /// <remarks>
    /// Corners are tested before insides, and the selected box before the
    /// rest. A corner sits inside its own box, so the other order makes the
    /// handles unreachable — they would every one of them be a move.
    /// </remarks>
    private void BeginGridGesture(double x, double y)
    {
        _gridStart = new Point(x, y);
        _gridLast = _gridStart;
        var boxes = ReferenceBoxes ?? [];
        var reach = 6 / Math.Max(0.01, _zoom * FitScale());

        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            if (!boxes[i].Selected) continue;
            if (CornerOf(boxes[i], x, y, reach) is not { } corner) continue;
            (_gridGesture, _gridIndex, _gridLeft, _gridTop) = (GridGesture.Resize, i, corner.Left, corner.Top);
            return;
        }

        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            var b = boxes[i];
            if (x < b.X || x > b.X + b.W || y < b.Y || y > b.Y + b.H) continue;
            _gridGesture = GridGesture.Move;
            _gridIndex = i;
            ReferenceBoxPicked?.Invoke(i);
            return;
        }

        _gridGesture = GridGesture.Draw;
        _gridIndex = -1;
        ReferenceBoxPicked?.Invoke(-1);
    }

    private static (bool Left, bool Top)? CornerOf(ReferenceBox box, double x, double y, double reach)
    {
        foreach (var (cx, cy, left, top) in ((double, double, bool, bool)[])
            [
                (box.X, box.Y, true, true),
                (box.X + box.W, box.Y, false, true),
                (box.X, box.Y + box.H, true, false),
                (box.X + box.W, box.Y + box.H, false, false),
            ])
        {
            if (Math.Abs(x - cx) <= reach && Math.Abs(y - cy) <= reach) return (left, top);
        }
        return null;
    }

    // ---- the projected reference the Arrow has hold of (Q108) --------------------

    /// <summary>
    /// The box round the selected reference, or null when none is selected.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ReferenceBoxes"/> rather than one more entry in
    /// it, because that list is also the switch that says "grid editing is on"
    /// — a press anywhere goes to the grid while it is non-null. Sharing it
    /// would have made selecting a sheet turn every press into a grid gesture
    /// and every empty drag into a new cell.
    /// </remarks>
    public ReferenceBox? ReferenceSheetBox
    {
        get => _referenceSheetBox;
        set
        {
            if (_referenceSheetBox == value) return;
            _referenceSheetBox = value;
            InvalidateVisual();
        }
    }

    private ReferenceBox? _referenceSheetBox;

    /// <summary>
    /// Whether the selected reference refuses to be dragged. The box still
    /// draws; its grips do not, which is what makes a lock legible rather than
    /// a gesture that mysteriously does nothing.
    /// </summary>
    public bool ReferenceSheetLocked
    {
        get => _referenceSheetLocked;
        set
        {
            if (_referenceSheetLocked == value) return;
            _referenceSheetLocked = value;
            InvalidateVisual();
        }
    }

    private bool _referenceSheetLocked;

    /// <summary>The Arrow pressed where a projected reference might be: document x, y.</summary>
    /// <returns>Whether a reference was there — a miss falls through to the marquee.</returns>
    public event Func<double, double, bool>? ReferenceSheetPicked;

    /// <summary>A drag on the selected reference began. False when it cannot move.</summary>
    public event Func<bool>? ReferenceSheetGestureStarted;

    /// <summary>Move the held reference by a document-space delta.</summary>
    public event Action<double, double>? ReferenceSheetMoved;

    /// <summary>Scale it about a fixed point: factor against the gesture's start, then the anchor.</summary>
    public event Action<double, double, double>? ReferenceSheetScaled;

    /// <summary>The gesture is over — one undo step lands here (B192).</summary>
    public event Action? ReferenceSheetGestureEnded;

    private enum SheetGesture
    {
        None,
        Move,
        Scale,
    }

    private SheetGesture _sheetGesture;
    private Point _sheetLast;
    private Point _sheetAnchor;
    private Point _sheetGrabbed;
    private double _sheetStartSpan;

    /// <summary>
    /// Decide what an Arrow press on the selected reference means: a corner
    /// scales, the inside moves, anything else is not ours.
    /// </summary>
    /// <remarks>
    /// <b>Only the already-selected reference is grabbable.</b> Selecting is a
    /// click and moving is the drag after it — the order matters because a
    /// projected plate covers most of the canvas, and a press that both selected
    /// and started dragging would make the marquee unreachable anywhere a
    /// reference is up.
    /// </remarks>
    /// <param name="cornersOnly">
    /// Whether to answer for the grips alone. The press path asks twice: once
    /// before the artwork, where only the grips may win — they are small,
    /// deliberate targets sitting on top of everything — and once after it,
    /// where the inside of the box may. A reference plate covers most of the
    /// canvas, so a box that outranked the drawing would make the art under it
    /// unclickable.
    /// </param>
    public bool TryBeginReferenceSheetGesture(double x, double y, bool cornersOnly)
    {
        if (ReferenceSheetBox is not { } box || ReferenceSheetLocked) return false;

        var reach = 6 / Math.Max(0.01, _zoom * FitScale());
        var corner = CornerOf(box, x, y, reach);
        var inside = !cornersOnly
            && x >= box.X && x <= box.X + box.W && y >= box.Y && y <= box.Y + box.H;
        if (corner is null && !inside) return false;
        if (ReferenceSheetGestureStarted?.Invoke() != true) return false;

        _sheetLast = new Point(x, y);
        if (corner is { } c)
        {
            _sheetGesture = SheetGesture.Scale;
            // The opposite corner is what stays still, so the one in hand is
            // the only thing that appears to move.
            _sheetAnchor = new Point(c.Left ? box.X + box.W : box.X, c.Top ? box.Y + box.H : box.Y);
            _sheetGrabbed = new Point(c.Left ? box.X : box.X + box.W, c.Top ? box.Y : box.Y + box.H);
            _sheetStartSpan = Span(_sheetGrabbed, _sheetAnchor);
        }
        else
        {
            _sheetGesture = SheetGesture.Move;
        }
        return true;
    }

    /// <summary>Carry the gesture on. False when there is none in flight.</summary>
    public bool ContinueReferenceSheetGesture(double x, double y)
    {
        switch (_sheetGesture)
        {
            case SheetGesture.Move:
                ReferenceSheetMoved?.Invoke(x - _sheetLast.X, y - _sheetLast.Y);
                _sheetLast = new Point(x, y);
                return true;
            case SheetGesture.Scale:
                // Against the span the gesture started with, never the last
                // event's: compounding per event drifts, and a corner dragged
                // out and back would not come home.
                var span = Span(new Point(x, y), _sheetAnchor);
                var factor = _sheetStartSpan > 0.0001 ? span / _sheetStartSpan : 1;
                ReferenceSheetScaled?.Invoke(factor, _sheetAnchor.X, _sheetAnchor.Y);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Let go. False when there was no gesture to end.</summary>
    public bool EndReferenceSheetGesture()
    {
        if (_sheetGesture == SheetGesture.None) return false;
        _sheetGesture = SheetGesture.None;
        ReferenceSheetGestureEnded?.Invoke();
        return true;
    }

    /// <summary>
    /// How far a point is from the anchor along the diagonal, as one number.
    /// </summary>
    /// <remarks>
    /// The plain distance rather than a per-axis ratio: the scale is uniform, so
    /// the gesture needs one measurement, and the diagonal is the one that keeps
    /// the pointer on the corner it grabbed.
    /// </remarks>
    private static double Span(Point from, Point anchor) =>
        Math.Sqrt(
            ((from.X - anchor.X) * (from.X - anchor.X)) + ((from.Y - anchor.Y) * (from.Y - anchor.Y)));

    /// <summary>
    /// Put a document point in the middle of the view — what the navigator asks
    /// for (Q110).
    /// </summary>
    /// <remarks>
    /// Through the same reanchor a zoom uses, so panning from the navigator and
    /// panning with the hand end in one place. Zoom, rotation and mirror are
    /// untouched: the navigator says <em>where</em> to look, never how closely,
    /// and it is view-only either way (invariant 5).
    /// </remarks>
    public void CentreOn(double docX, double docY)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        ReanchorAt(new Point(Bounds.Width / 2, Bounds.Height / 2), new Point(docX, docY));
    }

    /// <summary>Ask the window whether a reference is under this point, and select it.</summary>
    public bool PickReferenceSheetAt(double x, double y) =>
        ReferenceSheetPicked?.Invoke(x, y) ?? false;
}
