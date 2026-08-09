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

        if (grabbed.Part == PathPart.Node)
        {
            // Every selected node, so a multi-node drag moves the shape rather
            // than one point of it.
            if (_pathEdit.IsNodeSelected(grabbed.Node)) _pathEdit.MoveSelectedNodes(dx, dy);
            else _pathEdit.MoveNode(grabbed.Node, dx, dy);
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
        if (PaintTarget() is not { } frame) return false;
        if (!CanEdit(ActiveLayer, "reshape lines on it")) return false;

        var strokeId = session.StrokeId;
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
        session.Original.Nodes.Clear();
        session.Original.Nodes.AddRange(afterPath.Nodes);
        session.Original.Closed = afterPath.Closed;

        AfterStrokeEdit(frameId);
        PathEditChanged?.Invoke();
        return true;
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
