using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The pen: authoring a path click by click, and the one stroke it becomes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 3 of the vector design.</b> The session owns the geometry and this
/// owns the history — the transform tool's split and phase 2's, for the same
/// reason: a path is a dozen clicks and <em>one</em> undo step. Undoing a pen
/// line removes the line, not its last node.
/// </para>
/// <para>
/// <b>The preview is chrome, not paint.</b> The shape tool stamps its live
/// preview into the scratch surface, which is right for a gesture that lasts one
/// drag; a pen session lasts as long as it takes to place a dozen nodes, and
/// re-stamping the whole path into a full-canvas scratch on every pointer move
/// is precisely the shape invariant 6 forbids. So the canvas traces the
/// flattened path as an overlay while you work and the brush is stamped exactly
/// once, at the commit.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private PenSession? _pen;

    /// <summary>The path being drawn, or null when the pen is idle.</summary>
    public PenSession? Pen => _pen;

    /// <summary>Whether there is a path in progress.</summary>
    public bool PenActive => _pen is { NodeCount: > 0 };

    /// <summary>Raised when the path in progress changes, so the canvas can redraw.</summary>
    public event Action? PenChanged;

    /// <summary>
    /// Press: place a node, or close the path if the press landed on its start.
    /// </summary>
    /// <returns>Whether the press did anything, so the canvas knows to capture.</returns>
    /// <param name="tolerance">
    /// Grab distance in document units, derived by the caller from the zoom so
    /// closing a path stays as easy when zoomed out.
    /// </param>
    public bool PenPress(double x, double y, double tolerance, bool shift = false, bool alt = false)
    {
        if (ActiveTool != ToolId.Pen || IsPlaying) return false;
        if (!CanEdit(ActiveLayer, "draw on it") || PaintTargetOrKey() is null) return false;
        CommitSwatchEdit();

        _pen ??= new PenSession();

        // Snapping before constraining, because a guide is a place and Shift is
        // a direction: an artist who has both on wants the node on the guide.
        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        if (shift) (x, y) = _pen.Constrain(x, y);

        // The press answers the question the hover was asking.
        PenWouldClose = false;

        if (_pen.ClosesAt(x, y, tolerance))
        {
            _pen.Close();
            // Closing finishes the line. Illustrator's behaviour, and the honest
            // one: there is nowhere left to put a node on a path that has joined
            // up, so leaving the session open would be a mode with nothing in it.
            FinishPen();
            return true;
        }

        _pen.Place(x, y);
        AnnouncePen();
        PenChanged?.Invoke();
        return true;
    }

    /// <summary>Drag after a press: curve the node that was just placed.</summary>
    public void PenDrag(double x, double y, bool shift = false, bool alt = false)
    {
        if (_pen is not { Shaping: >= 0 } session) return;
        if (shift) (x, y) = session.Constrain(x, y);
        if (session.PullHandle(x, y, alt)) PenChanged?.Invoke();
    }

    /// <summary>Release: the node keeps whatever curve the drag gave it.</summary>
    public void PenRelease()
    {
        if (_pen is null) return;
        _pen.Release();
        PenChanged?.Invoke();
    }

    /// <summary>
    /// Whether the hovered click would join the path up — the canvas rings the
    /// first node with it, so closing is announced before it happens.
    /// </summary>
    public bool PenWouldClose { get; private set; }

    /// <summary>Move with no button down: the segment that has not been placed yet.</summary>
    /// <param name="tolerance">
    /// The same grab distance the press uses, so the indicator and the click it
    /// predicts cannot disagree. Zero means "don't ask" — a caller with no zoom
    /// to derive it from gets a rubber band and no closing preview.
    /// </param>
    public void PenHover(double x, double y, double tolerance = 0, bool shift = false)
    {
        if (_pen is not { NodeCount: > 0 } session) return;
        if (shift) (x, y) = session.Constrain(x, y);

        // Within closing distance the rubber band snaps onto the first node:
        // the click is previewed rather than described, which is what the
        // rubber band is for everywhere else.
        PenWouldClose = tolerance > 0 && session.ClosesAt(x, y, tolerance);
        if (PenWouldClose)
        {
            var first = session.Path.Nodes[0];
            (x, y) = (first.X, first.Y);
        }

        session.Hover(x, y);
        PenChanged?.Invoke();
    }

    /// <summary>The pointer has left the canvas; stop drawing a segment to nowhere.</summary>
    public void PenLeave()
    {
        if (_pen is null) return;
        _pen.Leave();
        PenWouldClose = false;
        PenChanged?.Invoke();
    }

    /// <summary>
    /// Put the pen down without finishing the path: what a tool switch does.
    /// </summary>
    /// <remarks>
    /// Reaching for the eyedropper mid-path is not "I am done" — committing the
    /// stroke here used to end a path the artist meant to come back to, with no
    /// way to reopen it as nodes-in-progress. So the session survives, its trace
    /// stays on screen, and the pen resumes it. Enter and Escape remain the
    /// deliberate finish; <see cref="CancelPen"/> remains the discard.
    /// </remarks>
    public void ParkPen()
    {
        if (_pen is null) return;
        _pen.Release();
        _pen.Leave();
        PenWouldClose = false;
        PenChanged?.Invoke();
    }

    /// <summary>Take the last node back off. Returns whether there was one.</summary>
    public bool RemoveLastPenNode()
    {
        if (_pen is not { NodeCount: > 0 } session) return false;
        session.RemoveLast();
        if (session.NodeCount == 0) _pen = null;
        AnnouncePen();
        PenChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Finish the path and record it as one stroke. Returns whether anything
    /// was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Enter and Escape both mean "done" here, and neither discards.</b> That
    /// is phase 2's rule and Illustrator's, against the polygon selection's,
    /// which cancels on Escape — and the difference is what is at stake. A
    /// selection in progress is not artwork; a dozen placed nodes are, and a key
    /// that silently throws away a minute of authoring is the wrong default even
    /// if some other tool in the application does it. What is written is one
    /// undo step, so taking it back is Ctrl+Z.
    /// </para>
    /// <para>
    /// <b>Path and points are written together</b>, which is
    /// <see cref="StrokePath"/>'s invariant. A pen line arrives with its nodes
    /// already known, so it is the one stroke in the document that has never
    /// needed a fit — reaching for the white arrow on it opens the path the pen
    /// authored rather than an approximation of it.
    /// </para>
    /// </remarks>
    public bool FinishPen()
    {
        if (_pen is not { } session)
        {
            return false;
        }

        _pen = null;
        PenWouldClose = false;
        var points = PathFlattener.Flatten(session.Path);
        if (!session.IsUsable || points.Count < 2)
        {
            // One click is not a line. Committing it would leave a single dab
            // where the artist expected to keep placing nodes.
            AiStatus = session.NodeCount > 0 ? "A line needs at least two points." : "";
            PenChanged?.Invoke();
            return false;
        }

        if (PaintTargetOrKey() is not { } target)
        {
            PenChanged?.Invoke();
            return false;
        }

        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = ColorHex,
            SwatchId = ActiveSwatchId,
            PaletteId = ActivePaletteId,
            Brush = CurrentToolSettings.Clone(),
            Points = points,
            Path = session.Commit(),
            AlphaLocked = ActiveLayer.AlphaLocked,
            Label = "pen",
        };

        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;
        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();
        AppendToFrameRender(target, stroke);

        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }

        AiStatus = session.Closed
            ? $"Drew a closed line of {session.NodeCount} points."
            : $"Drew a line of {session.NodeCount} points.";
        _dirtyThumbIds.Add(target.Id);
        PenChanged?.Invoke();
        InvalidateWholeCanvas();
        PublishSnapshot();
        return true;
    }

    /// <summary>Throw the path in progress away without recording it.</summary>
    /// <remarks>
    /// Not bound to a key — <see cref="FinishPen"/> owns Escape, for the reason
    /// written there. This is what a document close or a layer that stopped
    /// being editable needs: a way to drop the session without writing a stroke
    /// somewhere it no longer belongs.
    /// </remarks>
    public void CancelPen()
    {
        if (_pen is null) return;
        _pen = null;
        PenWouldClose = false;
        AiStatus = "";
        PenChanged?.Invoke();
        InvalidateWholeCanvas();
    }

    private void AnnouncePen()
    {
        AiStatus = _pen switch
        {
            null or { NodeCount: 0 } => "",
            { NodeCount: 1 } => "Pen: click to place the next point, drag to curve it.",
            { NodeCount: var n } => $"Pen: {n} points. Enter to finish, Backspace to undo a point.",
        };
    }
}
