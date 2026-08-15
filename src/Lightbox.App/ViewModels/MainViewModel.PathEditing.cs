using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Isolation mode: one stroke, its nodes, and nothing else responding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 2 of the vector design, and the mode is the feature.</b> Q53 chose a
/// mode you enter deliberately over a modifier you have to remember, because the
/// research is one-sided: Illustrator, Figma and Grease Pencil all isolate, and
/// every tool that reaches into geometry through a held key is the one people
/// describe as mushy. The cost of a mode is that it must be obvious you are in
/// it and trivial to leave, which is what the greying and Escape are for.
/// </para>
/// <para>
/// <b>The session owns the geometry and this owns the history.</b> A node drag
/// is hundreds of pointer moves and exactly one undo step, so the session edits
/// its own copy and this decides when that becomes a delta. The same split as
/// the transform tool, for the same reason.
/// </para>
/// <para>
/// <b>Every commit writes the path and the points together.</b> That is
/// <see cref="StrokePath"/>'s invariant, and here it is not a rule to remember
/// but the only thing <see cref="CommitPathEdit"/> does — there is no way to
/// write one without the other.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private PathEditSession? _pathEdit;

    /// <summary>The stroke being reshaped, or null when not in isolation.</summary>
    public PathEditSession? PathEdit => _pathEdit;

    /// <summary>Whether isolation mode is on.</summary>
    public bool PathEditActive => _pathEdit is not null;

    /// <summary>The isolated stroke's id, for the canvas to grey around.</summary>
    public string? IsolatedStrokeId => _pathEdit?.StrokeId;

    /// <summary>Raised when isolation begins or ends, so the canvas can redraw.</summary>
    public event Action? PathEditChanged;

    /// <summary>
    /// Enter isolation on the stroke under a point. Returns true if something
    /// was there.
    /// </summary>
    /// <remarks>
    /// The double-click route: the black arrow picks a line, and a second click
    /// goes into it. Illustrator's gesture, and the reason it is safe is that a
    /// single click cannot do it — reaching into geometry is never something
    /// that happens by accident.
    /// </remarks>
    public bool BeginPathEditAt(double x, double y, double tolerance)
    {
        var strokes = PickableStrokes();
        if (strokes.Count == 0) return false;

        var hit = Lightbox.Raster.StrokePicker.TopmostAt(
            strokes, Lightbox.Raster.StrokeIndex.Of(strokes), x, y, tolerance);
        return hit is { } index && BeginPathEdit(strokes[index].Id);
    }

    /// <summary>Enter isolation on a named stroke.</summary>
    public bool BeginPathEdit(string strokeId)
    {
        if (!CanEdit(ActiveLayer, "reshape lines on it")) return false;

        var stroke = PickableStrokes().FirstOrDefault(s => s.Id == strokeId);
        if (stroke is null) return false;

        // Q50: a stroke with no path gets one fitted here. Nothing about the
        // drawing changes at this moment — the points are untouched until an
        // edit actually moves a node.
        var session = PathEditSession.Open(stroke);
        if (session is null)
        {
            AiStatus = "That line is too short to reshape.";
            return false;
        }

        _pathEdit?.Revert();
        _pathEdit = session;

        // The whole-stroke selection would otherwise sit underneath the node
        // overlay, drawing a second highlight around a line the artist is now
        // editing point by point. One selection at a time, and in isolation the
        // nodes are it.
        ClearStrokeSelection();

        AiStatus = session.NodeCount == 1
            ? "Reshaping a line — Esc to finish."
            : $"Reshaping a line: {session.NodeCount} points. Esc to finish.";
        PathEditChanged?.Invoke();
        OnPropertyChanged(nameof(PathEditActive));
        OnPropertyChanged(nameof(IsolatedStrokeId));
        PublishSnapshot();
        return true;
    }

    /// <summary>
    /// Leave isolation, keeping whatever was committed.
    /// </summary>
    /// <remarks>
    /// Escape is the way out everywhere this pattern appears, and it keeps
    /// rather than discards: each node drag was already its own undo step, so
    /// "cancel" at this level would have to undo an unknown number of them and
    /// would be the only place in the application where leaving a mode threw
    /// work away. <see cref="AbandonPathEdit"/> is the discard, for the drag
    /// that is still in flight.
    /// </remarks>
    public void EndPathEdit()
    {
        if (_pathEdit is null) return;
        _pathEdit = null;
        AiStatus = "";
        PathEditChanged?.Invoke();
        OnPropertyChanged(nameof(PathEditActive));
        OnPropertyChanged(nameof(IsolatedStrokeId));
        PublishSnapshot();
    }

    /// <summary>Put the path back as it was found and leave.</summary>
    public void AbandonPathEdit()
    {
        _pathEdit?.Revert();
        EndPathEdit();
    }

    // ---- editing -------------------------------------------------------------

    /// <summary>What is under the pointer, in path terms.</summary>
    public PathHit PathEditHitTest(double x, double y, double tolerance) =>
        _pathEdit?.HitTest(x, y, tolerance) ?? PathHit.Miss;

    /// <summary>
    /// Take hold of what is under the pointer. Returns what was grabbed, so the
    /// canvas knows whether a drag has begun.
    /// </summary>
    public PathHit GrabPathPart(double x, double y, double tolerance, bool additive = false)
    {
        // B172. Outside isolation this returned Miss and did nothing, and
        // nothing else in the white arrow's path could enter isolation — so
        // the tool was inert on its own, usable only after the *black* arrow
        // had opened the line by double-clicking it. That is not a tool, it is
        // a second step of another one.
        //
        // A single click, where the Arrow's route needs a double, and the
        // asymmetry is deliberate. Q53's argument for the double-click is that
        // reaching into geometry must never happen by accident — which is about
        // the Arrow, where a click ordinarily means "pick this whole thing".
        // Reaching into geometry is the white arrow's *only* purpose, so there
        // is no accident to protect against and a second click would be a tax.
        //
        // The re-grab is what makes it one gesture rather than two: entering
        // and then falling through to the hit test means the click that opened
        // the line also takes hold of the node under it, so the first drag is
        // not swallowed by the click that made it possible.
        if (_pathEdit is null)
        {
            if (!BeginPathEditAt(x, y, tolerance)) return PathHit.Miss;
        }
        if (_pathEdit is null) return PathHit.Miss;

        var hit = _pathEdit.HitTest(x, y, tolerance);
        if (!hit.IsHit)
        {
            // Clicking empty canvas inside isolation lets the nodes go without
            // leaving the mode — the same "click away" every object selection
            // has, one level down.
            if (!additive) _pathEdit.ClearNodeSelection();
            PathEditChanged?.Invoke();
            return PathHit.Miss;
        }

        // Grabbing the curve selects nothing. That is the point of it — the
        // gesture is "put the line here", and leaving the node selection alone
        // means an artist can pull a segment straight without losing the points
        // they had picked either side of it.
        if (hit.Part == PathPart.Segment)
        {
            PathEditChanged?.Invoke();
            return hit;
        }

        if (hit.Part == PathPart.Node && !_pathEdit.IsNodeSelected(hit.Node))
        {
            _pathEdit.SelectNode(hit.Node, additive);
        }
        else if (hit.Part == PathPart.Node && additive)
        {
            _pathEdit.SelectNode(hit.Node, additive: true);
        }

        PathEditChanged?.Invoke();
        return hit;
    }

    /// <summary>
    /// Move what is being dragged, without touching the document.
    /// </summary>
    /// <remarks>
    /// Live, per pointer move, and deliberately cheap: the session edits its own
    /// path and the canvas redraws an overlay. Re-flattening into the record on
    /// every move would rewrite the stroke and repaint the frame hundreds of
    /// times a drag — invariant 6's exact prohibition.
    /// </remarks>
    public void DragPathPart(PathHit grabbed, double x, double y, double dx, double dy, bool breakPair = false)
    {
        if (_pathEdit is null || !grabbed.IsHit) return;

        if (grabbed.Part == PathPart.Segment)
        {
            // The parameter is the one recorded at the grab, not one re-derived
            // from where the pointer is now. Re-finding it every move would let
            // the grip slide along the curve as the shape changed underneath it,
            // so a straight pull would drift towards whichever end was moving
            // less — and the line would come away from the pointer.
            _pathEdit.PinchSegment(grabbed.Node, grabbed.T, dx, dy);
        }
        else if (grabbed.Part == PathPart.Node)
        {
            if (breakPair)
            {
                // Alt on a point is the pen's drag-to-curve, offered again
                // here: pull mirrored handles out of it instead of moving it.
                // The one gesture that turns a corner into a curve without a
                // menu, and the same muscle memory as placing it curved would
                // have been.
                _pathEdit.PullHandlesTo(grabbed.Node, x, y);
            }
            // Every selected node, so a multi-node drag moves the shape rather
            // than one point of it — and it moves by the raw delta, because a
            // group has no one point that is "the" point to land on a guide.
            else if (_pathEdit.SelectedNodes.Count > 1) _pathEdit.MoveSelectedNodes(dx, dy);
            // B216. One node is a point being placed, so it snaps like every
            // other placed point — the pen puts a node on a guide and the white
            // arrow could not put that same node back on it.
            //
            // The delta is re-derived from where the node should END UP rather
            // than snapping the pointer, because the grab has an offset: you
            // catch a node slightly off its centre, and snapping the pointer
            // would land the node that offset away from the guide.
            //
            // Split on the selection COUNT rather than on whether the grabbed
            // node is selected, which is what the branch above used to ask.
            // `GrabPathPart` selects whatever node it grabs, so by the time a
            // drag runs the answer is always yes and the single-node arm was
            // unreachable — the test for this landed on the lattice by accident
            // and passed against the old code until its numbers were changed.
            else _pathEdit.MoveNodeTo(grabbed.Node, SnappedPoint, dx, dy);
        }
        else
        {
            _pathEdit.MoveHandleTo(grabbed.Node, grabbed.Part, x, y, breakPair);
        }
        PathEditChanged?.Invoke();
    }

    /// <summary>
    /// Write the reshaped line into the document as one undo step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Path and points, together, always.</b> This is the only writer, so the
    /// invariant cannot be broken by forgetting — there is no path here that
    /// writes one without the other, on apply or on revert.
    /// </para>
    /// <para>
    /// <b>Undo restores the original arrays rather than recomputing them.</b>
    /// The same reason a move does: <c>a + d - d</c> is not always <c>a</c> in
    /// IEEE-754, and every dab dynamic is seeded from the bits of a coordinate,
    /// so a recomputed undo returns the line to visibly the right place with a
    /// quietly different grain.
    /// </para>
    /// </remarks>
    public bool CommitPathEdit()
    {
        if (_pathEdit is not { Dirty: true } session) return false;
        if (!CanEdit(ActiveLayer, "reshape lines on it")) return false;

        // B207. The session picked its stroke off whatever the cel displays,
        // which on a hold is the drawing it borrows; committing into that
        // would move the line on the earlier frame too. Keying here rather
        // than when the pen was picked up is the rule B206 settled: the
        // record changes when an edit lands, never because a tool was chosen.
        var ids = new List<string> { session.StrokeId };
        if (KeyHeldCelForStrokeEdit(ids) is not { } frame) return false;
        var strokeId = ids[0];
        var stroke = StrokesOf(frame).FirstOrDefault(s => s.Id == strokeId);
        if (stroke is null) return false;

        var beforePoints = stroke.Points.ToList();
        var beforePath = stroke.Path?.Clone();
        var afterPoints = session.Flatten();
        var afterPath = session.Path.Clone();
        if (afterPoints.Count < 2) return false;

        var frameId = frame.Id;
        _editor.PerformDelta(
            apply: doc =>
            {
                if (StrokeIn(doc, frameId, strokeId) is not { } s) return;
                s.Points = [.. afterPoints];
                s.Path = afterPath.Clone();
            },
            revert: doc =>
            {
                if (StrokeIn(doc, frameId, strokeId) is not { } s) return;
                s.Points = [.. beforePoints];
                s.Path = beforePath?.Clone();
            },
            affectedFrameId: frameId);

        // The session's Original becomes what was just committed, so a later
        // Escape restores this rather than the state the session opened in —
        // the committed edit is history's now, not the session's to take back.
        // Keep takes the weight with it, or a width edit would survive a commit
        // and then be un-done by an Escape that never named it.
        session.Keep(afterPath);

        AfterStrokeEdit(frameId);
        PathEditChanged?.Invoke();
        return true;
    }

    // ---- width along the line (phase 4b) ---------------------------------------

    /// <summary>
    /// Take hold of the line to change its weight. Returns where along it, or -1.
    /// </summary>
    /// <remarks>
    /// The width tool grabs the <em>line</em> rather than a node, so the grab is
    /// a position along it rather than a part of it — and it is recorded once,
    /// at the press, for the reason the pinch records its parameter once: a grip
    /// re-derived every move slides along the curve as the shape changes under
    /// it.
    /// </remarks>
    public double GrabWidthAt(double x, double y, double tolerance)
    {
        if (_pathEdit is not { } session) return -1;
        var (at, distance) = session.AlongAt(x, y);
        if (distance > tolerance) return -1;

        // Captured here rather than on the first move. Taking it from the first
        // drag instead makes the reference the position the pointer had already
        // moved to, so the first millimetre of every drag does nothing and the
        // gesture starts late.
        _widthGrabDistance = distance;
        return at;
    }

    /// <summary>
    /// Make the line heavier or lighter, from how far the pointer has moved off
    /// it.
    /// </summary>
    /// <remarks>
    /// <b>Distance from the line, not a signed side.</b> Dragging out from either
    /// side fattens and dragging back in thins, because a Lightbox stroke is a
    /// centreline with one width — there is no left and right to give different
    /// weights to, and pretending otherwise would put a control in the artist's
    /// hand that the record cannot hold.
    /// </remarks>
    public void DragWidth(double at, double x, double y)
    {
        if (_pathEdit is not { } session || at < 0) return;

        var points = Lightbox.Core.Geometry.PathFlattener.Flatten(session.Path);
        if (points.Count < 2) return;

        var (_, distance) = session.AlongAt(x, y);
        var reference = _widthGrabDistance ??= distance;
        if (session.AdjustWidthAt(at, distance - reference)) PathEditChanged?.Invoke();
        _widthGrabDistance = distance;
    }

    /// <summary>Let go: the whole drag becomes one undo step.</summary>
    public bool EndWidthDrag()
    {
        _widthGrabDistance = null;
        return CommitPathEdit();
    }

    /// <summary>
    /// How far the pointer was from the line when the drag began.
    /// </summary>
    /// <remarks>
    /// The change is measured against where the press landed rather than against
    /// the line itself, so grabbing a line from ten pixels away does not jump its
    /// weight by ten pixels' worth before the hand has moved. Every gesture in
    /// this application that turns a distance into a value does the same.
    /// </remarks>
    private double? _widthGrabDistance;

    // ---- simplify (phase 4c) ---------------------------------------------------

    /// <summary>
    /// Refit the isolated line through fewer points, and say how many are left.
    /// </summary>
    /// <remarks>
    /// <b>The count is the feature as much as the refit is.</b> "Simplify" with
    /// no number is a button an artist presses and then squints at the canvas to
    /// find out what it did; the design asks for a live count for that reason.
    /// Pressing again loosens further, so the control is the repetition rather
    /// than a value to choose up front — and each press is its own undo step, so
    /// one too many costs one Ctrl+Z rather than the whole line.
    /// </remarks>
    [RelayCommand]
    public void SimplifyLine()
    {
        if (_pathEdit is not { } session) return;

        var before = session.NodeCount;
        if (!session.Simplify())
        {
            AiStatus = $"Cannot simplify further — {before} points is as few as this shape takes.";
            return;
        }

        if (!CommitPathEdit())
        {
            session.Revert();
            return;
        }
        AiStatus = $"Simplified: {before} points to {session.NodeCount}.";
        PathEditChanged?.Invoke();
    }

    /// <summary>Make the selected nodes corners, or smooth them.</summary>
    public bool SetSelectedNodesCorner(bool corner)
    {
        if (_pathEdit is not { } session || session.SelectedNodes.Count == 0) return false;
        foreach (var index in session.SelectedNodes.ToList()) session.SetCorner(index, corner);
        var changed = CommitPathEdit();
        if (changed)
        {
            AiStatus = corner ? "Made the point a corner." : "Smoothed the point.";
        }
        return changed;
    }

    /// <summary>
    /// Whether a click on this stroke should do anything at all.
    /// </summary>
    /// <remarks>
    /// <b>Isolation's whole promise, in one method.</b> Illustrator's isolation
    /// mode "automatically locks all other objects", and a mode that only
    /// <em>looks</em> isolating is worse than none — the artist trusts it and
    /// then edits the wrong line. Asked by the picking paths rather than
    /// enforced by the canvas, so it holds for anything driven headlessly too.
    /// </remarks>
    public bool RespondsToPicking(string strokeId) =>
        _pathEdit is null || _pathEdit.StrokeId == strokeId;

    private static Stroke? StrokeIn(Doc doc, string frameId, string strokeId) =>
        StrokeListIn(doc, frameId)?.FirstOrDefault(s => s.Id == strokeId);
}
