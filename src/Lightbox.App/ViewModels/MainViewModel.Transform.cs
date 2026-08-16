using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 13,628 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- move tool ---------------------------------------------------------------

    /// <summary>
    /// A move in progress: where it started and how far it has come.
    /// </summary>
    /// <remarks>
    /// A move is a transform session with the handles left off. That is not a
    /// shortcut — it is the same operation, so it gets the same live preview
    /// (composite-time, one matrix, no geometry touched until the release),
    /// the same selection filter, the same scopes and the same single undo
    /// step. Re-implementing translation next to it would be a second way for
    /// the drawing to move, and the two would drift.
    /// </remarks>
    private (double X, double Y)? _moveAnchor;

    private (double X, double Y) _moveDelta;

    public bool MoveActive => _moveAnchor is not null;

    /// <summary>
    /// Pick the drawing up.
    /// </summary>
    /// <param name="wholeLayer">
    /// Move every drawing on the layer by the same amount, rather than the one
    /// under the playhead. Shifting a finished cycle sideways is a real job
    /// and doing it a frame at a time is not a job anybody finishes.
    /// </param>
    public bool BeginMove(double x, double y, bool wholeLayer)
    {
        // A placed symbol under the cursor wins, and only when the grab is on
        // the drawing rather than on the whole layer. Moving a placement is an
        // edit to two numbers on the placement; moving the drawing rewrites
        // stroke coordinates. They are different operations that happen to
        // share a gesture, and which one you get is decided by what you
        // grabbed — the same way it is in Photoshop.
        if (!wholeLayer && BeginPlacementMove(x, y)) return true;

        var scope = wholeLayer ? TransformScope.ActiveLayerAllFrames : TransformScope.ActiveCel;
        // Assigned before the session starts, so the property's change handler
        // has no live session to restart and simply records the scope.
        TransformScope = scope;
        if (!BeginTransform(gizmo: false)) return false;
        _moveAnchor = (x, y);
        _moveDelta = default;
        AiStatus = wholeLayer
            ? $"Moving every drawing on {ActiveLayer.Name}"
            : "Moving this drawing — hold Ctrl to move the whole layer";
        return true;
    }

    /// <summary>
    /// Pick up the picked lines. Returns whether a session started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B223, and it is the paragraph <c>MainViewModel.StrokeActions</c> has
    /// carried since the Arrow shipped.</b> That file says, in as many words,
    /// that the move "would get live preview and one-undo-step-per-drag from
    /// BeginTransform / PreviewTransform / CommitTransformAffine, and reusing
    /// that here looked obvious. It is not available yet: every TransformScope
    /// is a set of cels, and there is no scope meaning 'these strokes inside
    /// this cel'." The scope was never the obstacle — <c>TransformSession</c>
    /// has taken a stroke filter all along, and the marquee has used it. What
    /// was missing was a filter built from the line selection, which is one
    /// method.
    /// </para>
    /// <para>
    /// <b>The filter is passed explicitly rather than derived</b>, and that is
    /// the one place this departs from Q97's precedence. A menu command asks
    /// "what is selected"; a drag asks "what am I holding", and those differ
    /// when a marquee is up somewhere else. Letting the marquee outrank the
    /// line under the pointer would make a direct-manipulation drag move
    /// something the artist is not touching.
    /// </para>
    /// </remarks>
    public bool BeginLineMove(double x, double y)
    {
        if (StrokeSelectionFilter() is not { } lines) return false;

        // A line lives on one drawing, so the scope is that drawing —
        // ScopeIsPinnedToThisCel makes CollectTransformFrames agree without
        // this having to set the property and restart anything.
        if (!BeginTransform(gizmo: false, filter: lines)) return false;
        _moveAnchor = (x, y);
        _moveDelta = default;
        AiStatus = $"Moving {TransformSubject}";
        return true;
    }

    /// <param name="axisLock">
    /// Shift: hold the move to one axis, whichever it has gone furthest along.
    /// The same thing Shift means on every other tool here.
    /// </param>
    public void UpdateMove(double x, double y, bool axisLock)
    {
        if (PlacementMoveActive)
        {
            UpdatePlacementMove(x, y, axisLock);
            return;
        }
        if (_moveAnchor is not { } anchor) return;
        var dx = x - anchor.X;
        var dy = y - anchor.Y;
        if (axisLock)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        // B225. The guides after the axis lock, so a constrained move still
        // snaps along the axis it was held to rather than being pulled off it.
        // Same order the gradient uses for Shift, and for the same reason: the
        // constraint the artist asked for out loud wins.
        (dx, dy) = SnappedMove(dx, dy);
        _moveDelta = (dx, dy);
        PreviewTransform(SKMatrix.CreateTranslation((float)dx, (float)dy));
    }

    /// <summary>
    /// Correct a move so the moving artwork lands on a guide, if one is near.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B225: the <em>bounds</em> snap, not the grab point.</b> Photoshop's
    /// Move tool does it this way and the reflex transfers, but the reason is
    /// independent of that: "line this up with the guide" is a claim about the
    /// edge of the artwork, and snapping where you happened to take hold would
    /// mean grabbing exactly the edge to align it. The grab point is the
    /// version that is simpler to build and harder to use.
    /// </para>
    /// <para>
    /// <b>Five candidates — four corners and the centre.</b> Corners are what
    /// an artist lines up against a ruler; the centre is what they line up
    /// against a vanishing point or the middle of a grid cell. Edge midpoints
    /// were left out because a corner already covers each edge on one axis, and
    /// nine candidates competing for one drag is how a snap starts feeling
    /// like it is arguing with you.
    /// </para>
    /// <para>
    /// <b>The smallest correction wins.</b> Every candidate is offered to
    /// <c>SnappedPoint</c> — B216's shared helper, so this obeys the same
    /// guides, the same tolerance and the same on/off switch as everything
    /// else, rather than inventing a second kind of snapping. The one that
    /// moves least is the one the hand was closest to meaning.
    /// </para>
    /// <para>
    /// <b>Bounded.</b> <see cref="TransformSession.SnapBounds"/> is computed
    /// once when the session opens, so a pointer move costs five point-snaps
    /// against the guide list and no geometry — not work proportional to the
    /// drawing, which is what invariant 6 forbids in a per-event path.
    /// </para>
    /// </remarks>
    private (double X, double Y) SnappedMove(double dx, double dy)
    {
        if (!SnapToGuides || Scene.Guides is not { Count: > 0 }) return (dx, dy);
        if (_transform.SnapBounds is not { } bounds) return (dx, dy);

        (double X, double Y)[] candidates =
        [
            (bounds.Left, bounds.Top), (bounds.Right, bounds.Top),
            (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom),
            (bounds.MidX, bounds.MidY),
        ];

        var bestDistance = double.MaxValue;
        (double X, double Y) best = (dx, dy);
        foreach (var (cx, cy) in candidates)
        {
            var landedX = cx + dx;
            var landedY = cy + dy;
            var (snappedX, snappedY) = SnappedPoint(landedX, landedY);
            var correctionX = snappedX - landedX;
            var correctionY = snappedY - landedY;
            // Nothing was near enough to pull this candidate anywhere.
            if (correctionX == 0 && correctionY == 0) continue;

            var distance = correctionX * correctionX + correctionY * correctionY;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = (dx + correctionX, dy + correctionY);
        }
        return best;
    }

    /// <summary>Put it down. One undo step for the whole drag, or nothing at all.</summary>
    public void EndMove()
    {
        if (PlacementMoveActive)
        {
            EndPlacementMove();
            return;
        }
        if (_moveAnchor is null) return;
        var (dx, dy) = _moveDelta;
        _moveAnchor = null;
        _moveDelta = default;
        // A click that went nowhere is a click, not an edit. Committing it
        // would put an identity transform in the history for every stray tap.
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            CancelMove();
            return;
        }
        CommitTransformAffine(0, 0, 1, 1, 0, dx, dy);
        TransformActive = false;
    }

    public void CancelMove()
    {
        if (PlacementMoveActive)
        {
            CancelPlacementMove();
            return;
        }
        _moveAnchor = null;
        _moveDelta = default;
        if (TransformActive) CancelTransform();
    }

    /// <summary>Begin moving selected guides.</summary>
    public void BeginGuidesMove()
    {
        if (_selectionManager.SelectedGuideIds.Count == 0) return;
        _guidesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedGuideIds.Count} guide(s)";
    }

    /// <summary>
    /// Move the selected guides by the delta since the last pointer event.
    /// </summary>
    /// <remarks>
    /// Live on the record and accumulated, which is <see cref="DragGuide"/> for
    /// a group — the same reason it gives: a pointer move arrives every few
    /// milliseconds, so recording each one would bury the last real edit under
    /// fifty identical nudges. It has to be added rather than assigned; the
    /// canvas reports the change since the previous event and advances its own
    /// anchor, and assigning keeps only the final increment (B109).
    /// </remarks>
    public void UpdateGuidesMove(double dx, double dy)
    {
        foreach (var guide in SelectedGuides())
        {
            guide.X += dx;
            guide.Y += dy;
        }
        _guidesMoveDelta = (_guidesMoveDelta.X + dx, _guidesMoveDelta.Y + dy);
        NotifyGuides();
    }

    /// <summary>Close a group guide drag: the whole of it becomes one undo step.</summary>
    public void EndGuidesMove()
    {
        var (dx, dy) = _guidesMoveDelta;
        _guidesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveGuidesBy(dx, dy);
    }

    /// <summary>
    /// The selected guides that may actually be moved.
    /// </summary>
    /// <remarks>
    /// A locked guide is skipped, the way <see cref="MoveGuide"/> and
    /// <see cref="DragGuide"/> both skip one. Locking exists so a perspective
    /// set can be leaned on without being knocked out of place, and a group
    /// move that ignored it would be the one gesture that could.
    ///
    /// <para>
    /// <b>B215 removed the index lookup this used to do</b>, and with it the
    /// case it could not defend against: an id that names no guide resolves to
    /// nothing, where an index that no longer names the guide it was taken from
    /// resolves to a different one and moves it.
    /// </para>
    /// </remarks>
    private List<Guide> SelectedGuides()
    {
        var guides = Scene.Guides;
        if (guides is null || guides.Count == 0) return [];
        var picked = new List<Guide>();
        foreach (var guide in guides)
        {
            if (!_selectionManager.IsGuideSelected(guide.Id)) continue;
            if (guide.Locked) continue;
            picked.Add(guide);
        }
        return picked;
    }

    private (double X, double Y) _guidesMoveDelta;

    /// <summary>Record a finished group guide move as one step.</summary>
    /// <remarks>
    /// Rewound first and then replayed forward, which is what
    /// <see cref="EndGuideDrag"/> does and for the same reason: the guides are
    /// already sitting at the end of the drag, so undo has to return them to
    /// where they were picked up rather than to the last pointer event. The
    /// guides are held by reference rather than by index, because an undo can
    /// replace the list and leave an index pointing at a different guide.
    /// </remarks>
    private void MoveGuidesBy(double dx, double dy)
    {
        var moved = SelectedGuides();
        if (moved.Count == 0) return;

        foreach (var guide in moved)
        {
            guide.X -= dx;
            guide.Y -= dy;
        }
        _editor.PerformDelta(
            _ =>
            {
                foreach (var guide in moved)
                {
                    guide.X += dx;
                    guide.Y += dy;
                }
                NotifyGuides();
            },
            _ =>
            {
                foreach (var guide in moved)
                {
                    guide.X -= dx;
                    guide.Y -= dy;
                }
                NotifyGuides();
            });
        NotifyGuides();
    }

    /// <summary>Begin moving selected reference boxes.</summary>
    public void BeginRefBoxesMove()
    {
        if (_selectionManager.SelectedRefBoxIndices.Count == 0) return;
        _refBoxesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedRefBoxIndices.Count} reference box(es)";
    }

    /// <summary>
    /// Move the selected reference boxes by the delta since the last pointer
    /// event.
    /// </summary>
    /// <remarks>
    /// Live and accumulated, for the reason on <see cref="UpdateGuidesMove"/>.
    /// </remarks>
    public void UpdateRefBoxesMove(double dx, double dy)
    {
        foreach (var cell in SelectedRefBoxes())
        {
            cell.Dx += dx;
            cell.Dy += dy;
        }
        _refBoxesMoveDelta = (_refBoxesMoveDelta.X + dx, _refBoxesMoveDelta.Y + dy);
        AfterReferenceChange();
    }

    /// <summary>Close a group box drag: the whole of it becomes one undo step.</summary>
    public void EndRefBoxesMove()
    {
        var (dx, dy) = _refBoxesMoveDelta;
        _refBoxesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveRefBoxesBy(dx, dy);
    }

    private (double X, double Y) _refBoxesMoveDelta;

    /// <summary>The selected boxes on the active sheet.</summary>
    private List<ReferenceCell> SelectedRefBoxes()
    {
        if (ActiveReference?.Cells is not { Count: > 0 } cells) return [];
        var picked = new List<ReferenceCell>();
        foreach (var index in _selectionManager.SelectedRefBoxIndices)
        {
            if (index < 0 || index >= cells.Count) continue;
            picked.Add(cells[index]);
        }
        return picked;
    }

    /// <summary>Record a finished group box move as one step.</summary>
    /// <remarks>
    /// <para>
    /// <c>Dx</c>/<c>Dy</c>, which is what <see cref="MoveReferenceCell"/>
    /// moves, and the distinction is the whole of this method. A cell's
    /// <c>X</c>/<c>Y</c> are the window onto the sheet in sheet pixels — moving
    /// those scrolls the photograph inside the box and leaves the box where it
    /// was, which is a different operation and destroys the registration the
    /// pivot exists to hold. They are also <c>int</c>, so a drag reported in
    /// fractions of a pixel would be rounded away a frame at a time.
    /// </para>
    /// <para>
    /// Rewound and replayed like the guides, for the reason on
    /// <see cref="MoveGuidesBy"/>, and holding the cells by reference for the
    /// same one.
    /// </para>
    /// </remarks>
    private void MoveRefBoxesBy(double dx, double dy)
    {
        var moved = SelectedRefBoxes();
        if (moved.Count == 0) return;

        foreach (var cell in moved)
        {
            cell.Dx -= dx;
            cell.Dy -= dy;
        }
        _editor.PerformDelta(
            _ =>
            {
                foreach (var cell in moved)
                {
                    cell.Dx += dx;
                    cell.Dy += dy;
                }
                NotifyReference();
            },
            _ =>
            {
                foreach (var cell in moved)
                {
                    cell.Dx -= dx;
                    cell.Dy -= dy;
                }
                NotifyReference();
            });
        AfterReferenceChange();
    }

    /// <summary>Begin moving selected anchors.</summary>
    public void BeginAnchorsMove()
    {
        if (_selectionManager.SelectedAnchorIds.Count == 0) return;
        _anchorsMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedAnchorIds.Count} anchor(s)";
    }

    /// <summary>Update anchor move by the delta since the last pointer event.</summary>
    public void UpdateAnchorsMove(double dx, double dy)
    {
        _anchorsMoveDelta = (_anchorsMoveDelta.X + dx, _anchorsMoveDelta.Y + dy);
        RequestSnapshot();
    }

    /// <summary>Commit anchor moves.</summary>
    public void EndAnchorsMove()
    {
        var (dx, dy) = _anchorsMoveDelta;
        _anchorsMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveAnchorsBy(dx, dy);
    }

    private (double X, double Y) _anchorsMoveDelta;

    /// <summary>Apply movement delta to selected anchors.</summary>
    private void MoveAnchorsBy(double dx, double dy)
    {
        var selectedAnchorIds = _selectionManager.SelectedAnchorIds.ToList();
        if (selectedAnchorIds.Count == 0) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.PerformDelta(
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var anchors = Anchors.ResolvedAt(doc.Scene, frame);
                foreach (var anchorId in selectedAnchorIds)
                {
                    if (anchors.TryGetValue(anchorId, out var point))
                    {
                        Anchors.SetAcross(layer, frame, 1, anchorId, new Core.Documents.AnchorPoint(point.X + dx, point.Y + dy));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            },
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var anchors = Anchors.ResolvedAt(doc.Scene, frame);
                foreach (var anchorId in selectedAnchorIds)
                {
                    if (anchors.TryGetValue(anchorId, out var point))
                    {
                        Anchors.SetAcross(layer, frame, 1, anchorId, new Core.Documents.AnchorPoint(point.X - dx, point.Y - dy));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            });
    }

    /// <summary>Begin moving selected collision shapes.</summary>
    public void BeginShapesMove()
    {
        if (_selectionManager.SelectedShapeIds.Count == 0) return;
        _shapesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedShapeIds.Count} shape(s)";
    }

    /// <summary>Update collision shape move by the delta since the last pointer event.</summary>
    public void UpdateShapesMove(double dx, double dy)
    {
        _shapesMoveDelta = (_shapesMoveDelta.X + dx, _shapesMoveDelta.Y + dy);
        RequestSnapshot();
    }

    /// <summary>Commit collision shape moves.</summary>
    public void EndShapesMove()
    {
        var (dx, dy) = _shapesMoveDelta;
        _shapesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveShapesBy(dx, dy);
    }

    private (double X, double Y) _shapesMoveDelta;

    /// <summary>Apply movement delta to selected collision shapes.</summary>
    private void MoveShapesBy(double dx, double dy)
    {
        var selectedShapeIds = _selectionManager.SelectedShapeIds.ToList();
        if (selectedShapeIds.Count == 0) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.PerformDelta(
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var shapes = CollisionShapes.ResolvedAt(doc.Scene, frame);
                foreach (var shapeId in selectedShapeIds)
                {
                    if (shapes.TryGetValue(shapeId, out var box))
                    {
                        CollisionShapes.SetAcross(layer, frame, 1, shapeId, new Core.Documents.ShapeBox(box.X + dx, box.Y + dy, box.W, box.H));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            },
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var shapes = CollisionShapes.ResolvedAt(doc.Scene, frame);
                foreach (var shapeId in selectedShapeIds)
                {
                    if (shapes.TryGetValue(shapeId, out var box))
                    {
                        CollisionShapes.SetAcross(layer, frame, 1, shapeId, new Core.Documents.ShapeBox(box.X - dx, box.Y - dy, box.W, box.H));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            });
    }

    partial void OnTransformScopeChanged(TransformScope value)
    {
        // Changing scope mid-session re-collects under the new scope.
        if (TransformActive) BeginTransform();
    }

    /// <summary>
    /// Whether the scope is pinned to this cel because a line selection is
    /// what is being transformed.
    /// </summary>
    /// <remarks>
    /// <b>B223.</b> A line selection is a set of stroke ids, and a stroke lives
    /// in exactly one drawing — so "all layers at this frame" or "the whole
    /// animation" would collect a hundred cels and find the picked lines in one
    /// of them. That is not a wider transform, it is the same transform with
    /// ninety-nine frames that resolve to nothing, and a scope control offering
    /// it would be describing work it cannot do.
    ///
    /// <para>
    /// Only when the lines are actually the subject: a marquee outranks them
    /// (Q97), and a marquee legitimately spans cels, so the scope stays live.
    /// </para>
    /// </remarks>
    public bool ScopeIsPinnedToThisCel => !HasSelection && HasStrokeSelection;

    /// <summary>Why the scope control is not offering its other options.</summary>
    public string ScopePinnedReason =>
        ScopeIsPinnedToThisCel
            ? "Scope is this drawing while lines are picked — a line lives on one drawing."
            : "";

    /// <summary>Distinct drawings in scope (holds share Frame instances — dedupe by id).</summary>
    private List<Frame> CollectTransformFrames()
    {
        var frames = new List<Frame>();
        var seen = new HashSet<string>();
        void Add(Frame? f)
        {
            if (f is not null && seen.Add(f.Id)) frames.Add(f);
        }
        // B223: the picked lines are on one drawing, so that is the scope
        // whatever the control last said. Asked before the switch rather than
        // as a sixth case, because it is not a scope an artist chooses — it is
        // the others becoming unavailable.
        if (ScopeIsPinnedToThisCel)
        {
            Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex));
            return frames;
        }
        switch (TransformScope)
        {
            case TransformScope.ActiveCel:
                // The frame the drag SHOWS, which on a hold is the drawing the
                // cel borrows. Where the commit WRITES is a different question,
                // answered at commit time by HeldCelNeedingKey — see
                // KeyHeldCelForCommit.
                Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex));
                break;
            case TransformScope.AllLayersAtFrame:
                foreach (var layer in Scene.Layers)
                {
                    if (Scene.IsLayerVisible(layer)) Add(ExposureSheet.ExposedFrame(layer, CurrentFrameIndex));
                }
                break;
            case TransformScope.ActiveLayerAllFrames:
                foreach (var cel in ActiveLayer.Cels) Add(cel.Frame);
                break;
            case TransformScope.CelRange:
                // Every selected cel, not the run between the first and the
                // last: a Ctrl+click selection has holes in it on purpose, and
                // transforming what an artist deselected is the one reading of
                // this scope they cannot undo by deselecting harder.
                if (_celSelection.Count > 0)
                {
                    foreach (var (layerIndex, index) in _celSelection)
                    {
                        if (layerIndex < 0 || layerIndex >= Scene.Layers.Count) continue;
                        Add(ExposureSheet.ExposedFrame(Scene.Layers[layerIndex], index));
                    }
                }
                else
                {
                    Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex)); // nothing marked
                }
                break;
            case TransformScope.EntireAnimation:
                foreach (var layer in Scene.Layers)
                {
                    foreach (var cel in layer.Cels) Add(cel.Frame);
                }
                break;
        }
        return frames;
    }

    public void CancelTransform()
    {
        if (!TransformActive) return;
        EndTransformSession();
        // The preview was composited into every published frame, so the revert
        // has to be published too. The commit path gets this for free from the
        // editor's document-changed publish; a cancel changes no document and
        // therefore publishes here — without it, Escape left the screen showing
        // the abandoned preview until something else happened to repaint
        // (found by the adversarial pass on B189's publish pacing, which made
        // the stale window longer; the hole predates it).
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
    }

    private void EndTransformSession()
    {
        TransformActive = false;
        _transform.End();
        // Commit and cancel both land here, so the chrome's preview matrix
        // cannot outlive the session however it ends. On a commit the raise
        // and the contour move happen in one synchronous chain, so no frame
        // can render between them with the transform applied twice.
        TransformPreviewChanged?.Invoke(null);
        TransformEnded?.Invoke();
    }

    // ---- live transform preview -------------------------------------------------

    /// <summary>
    /// The matrix the live preview is compositing through, null when it
    /// clears. The canvas subscribes so chrome that outlines the moving
    /// pixels — the marching ants — rides the same matrix the pixels do,
    /// instead of freezing at the pre-drag position until the commit.
    /// </summary>
    public event Action<SKMatrix?>? TransformPreviewChanged;

    /// <summary>
    /// Show the drag. Null clears the preview and puts the pixels back where
    /// the record says they are.
    /// </summary>
    public void PreviewTransform(SKMatrix? matrix)
    {
        if (!TransformActive)
        {
            if (_transform.Preview is null) return;
            matrix = null;
        }
        // Identity is "no preview": it renders the same and skips the split.
        if (matrix is { } m && m.IsIdentity) matrix = null;
        if (_transform.Preview is null && matrix is null) return;
        var before = _transform.Preview;
        _transform.Preview = matrix;
        TransformPreviewChanged?.Invoke(matrix);
        // Repaint where the moving pixels were and where they now are — the
        // stroke path's bounded-work rule (invariant 6), which this preview
        // used to break with a full-canvas invalidation per pointer event.
        // Paced to the present rate, that read as a slideshow on any document
        // where a full recomposite is slow, exactly where a brush stroke
        // stays live. Whole-canvas remains the fallback when the moving
        // pixels cannot be bounded from the stroke record.
        if (PreviewDirtyRegion(before, matrix) is { } dirty)
        {
            _publish.MarkDirty(dirty);
        }
        else
        {
            _publish.InvalidateWholeCanvas();
        }
        RequestSnapshot();
    }

    /// <summary>
    /// The doc-space region one preview step changes: the session's moving
    /// bounds through the previous matrix, unioned with the same bounds
    /// through the new one. Null when the moving pixels cannot be bounded and
    /// the caller must fall back to the whole canvas.
    /// </summary>
    /// <remarks>
    /// Not clamped to the document here: <c>ComposePlan</c> already clamps,
    /// and a region that leaves the canvas entirely simply patches nothing.
    /// The two-pixel inflation is the antialias seam, the same allowance
    /// <c>SnapshotGeometry</c> makes.
    /// </remarks>
    private SKRectI? PreviewDirtyRegion(SKMatrix? before, SKMatrix? after)
    {
        if (_transform.MovingBounds is not { } bounds) return null;
        var was = before is { } b ? b.MapRect(bounds) : bounds;
        var now = after is { } a ? a.MapRect(bounds) : bounds;
        was.Union(now);
        was.Inflate(2, 2);
        return SKRectI.Ceiling(was);
    }

    /// <summary>
    /// Doc-space bounds of what a preview moves, render reach included — or
    /// null when a frame's moving pixels are not all strokes (a raster
    /// baseline or a placement rides the layer bitmap), which is the honest
    /// "cannot bound this" answer.
    /// </summary>
    /// <remarks>
    /// The reach is <see cref="BrushEngine.ReachOf"/> per stroke rather than
    /// half the brush size (what the gizmo's <c>TransformOps.Bounds</c> uses):
    /// scatter and a medium's bleed put paint past the point geometry, and a
    /// repaint region that missed it would leave crumbs of the mark behind at
    /// the old position.
    /// </remarks>
    private static SKRect? PreviewMovingBounds(List<Frame> frames, Func<Stroke, bool>? filter)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        var any = false;
        foreach (var frame in frames)
        {
            // With a selection filter only strokes move and the baseline
            // stays put; without one the whole layer bitmap moves, baseline
            // and placements included, and strokes no longer bound it.
            if (filter is null && (frame.HasBaseline || frame.HasPlacements)) return null;
            foreach (var stroke in frame.Strokes)
            {
                if (filter is not null && !filter(stroke)) continue;
                var reach = BrushEngine.ReachOf(stroke.Brush);
                foreach (var p in stroke.Points)
                {
                    any = true;
                    if (p.X - reach < minX) minX = (float)(p.X - reach);
                    if (p.Y - reach < minY) minY = (float)(p.Y - reach);
                    if (p.X + reach > maxX) maxX = (float)(p.X + reach);
                    if (p.Y + reach > maxY) maxY = (float)(p.Y + reach);
                }
            }
        }
        return any ? new SKRect(minX, minY, maxX, maxY) : null;
    }

    /// <summary>
    /// The moving/static split for one in-scope frame, built once per session
    /// and reused for every pointer event of the drag. Null when this frame
    /// cannot be previewed honestly — a partial selection over a frame that is
    /// not a stroke record — in which case the gizmo alone stands in, as it
    /// did before.
    /// </summary>
    private TransformSession.Parts? PartsFor(Frame frame)
    {
        if (_transform.Filter is null)
        {
            // Everything moves, so the layer bitmap already IS the moving part
            // and no render is needed. It is deliberately re-fetched rather
            // than remembered: the cache owns and disposes these, and a
            // remembered one becomes a dangling pointer the moment anything
            // invalidates the frame.
            return new TransformSession.Parts(_cache.Get(frame, Scene.Width, Scene.Height), null, Owned: false);
        }

        if (_transform.Cached(frame.Id) is { } cached) return cached;

        TransformSession.Parts? parts;
        if (frame is Frame painted && _transform.Filter is { } filter)
        {
            var moving = painted.Strokes.Where(filter).ToList();
            var rest = painted.Strokes.Where(s => !filter(s)).ToList();
            SKBitmap stay;
            if (painted.PngBase64 is { Length: > 0 })
            {
                // The baseline stays put under a region-limited transform,
                // exactly as the commit leaves it — and it goes underneath the
                // strokes that stay, because that is the order it renders in.
                stay = new SKBitmap(new SKImageInfo(
                    Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                using (var canvas = new SKCanvas(stay))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var baseline = Lightbox.Raster.PngCodec.Decode(painted.PngBase64);
                    canvas.DrawBitmap(baseline, 0, 0);
                }
                foreach (var s in rest) FrameRasterizer.Append(stay, s);
            }
            else
            {
                stay = FrameRasterizer.Rasterize(rest, Scene.Width, Scene.Height);
            }
            parts = new TransformSession.Parts(
                FrameRasterizer.Rasterize(moving, Scene.Width, Scene.Height), stay, Owned: true);
        }
        else
        {
            parts = null;
        }

        if (parts is not null) _transform.Remember(frame.Id, parts);
        return parts;
    }

    /// <summary>Commit a move/scale/rotate/mirror (mirror = negative scale) around a pivot.</summary>
    public void CommitTransformAffine(
        double pivotX, double pivotY,
        double scaleX, double scaleY,
        double angleRadians,
        double offsetX, double offsetY)
    {
        if (!TransformActive) return;
        var map = TransformOps.Affine(pivotX, pivotY, scaleX, scaleY, angleRadians, offsetX, offsetY);
        var sizeScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        var m = SKMatrix.CreateTranslation((float)-pivotX, (float)-pivotY);
        m = m.PostConcat(SKMatrix.CreateScale((float)scaleX, (float)scaleY));
        m = m.PostConcat(SKMatrix.CreateRotation((float)angleRadians));
        m = m.PostConcat(SKMatrix.CreateTranslation((float)(pivotX + offsetX), (float)(pivotY + offsetY)));
        CommitTransformCore(map, sizeScale, m);
    }

    /// <summary>Commit a four-corner perspective transform.</summary>
    public void CommitTransformPerspective(double[] srcQuad, double[] dstQuad)
    {
        if (!TransformActive) return;
        double[] h;
        try
        {
            h = TransformOps.PerspectiveCoefficients(srcQuad, dstQuad);
        }
        catch (InvalidOperationException)
        {
            AiStatus = "That corner arrangement collapses the drawing — adjust the corners and try again.";
            return;
        }
        var map = TransformOps.Perspective(srcQuad, dstQuad);
        var m = new SKMatrix(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);
        CommitTransformCore(map, 1, m);
    }

    /// <summary>
    /// The drawing a single-cel gesture is borrowing, when a commit here must
    /// key the cel instead of writing through to it. Null when the cel has a
    /// drawing of its own, when the scope covers more than this cel, or when
    /// the artist has asked to edit the held drawing.
    /// </summary>
    /// <remarks>
    /// Asked when the session opens, because that is when the answer is true
    /// of what the artist is looking at; acted on at the commit, because that
    /// is when the record is allowed to change.
    /// </remarks>
    private string? HeldCelNeedingKey()
    {
        if (TransformScope is not (TransformScope.ActiveCel or TransformScope.CelRange)) return null;
        if (TransformScope is TransformScope.CelRange && _celSelection.Count > 0) return null;
        if (DrawingOnAHold == HoldDrawing.EditTheHeldDrawing) return null;
        if (ActiveLayer is not { } layer || layer.Cels.Count == 0) return null;
        var here = Math.Clamp(CurrentFrameIndex, 0, layer.Cels.Count - 1);
        if (layer.Cels[here].Frame is not null) return null; // a drawing of its own
        return ExposureSheet.ExposedFrame(layer, here)?.Id;
    }

    /// <summary>
    /// Key the held cel this gesture opened on and hand back the drawing the
    /// commit should write to — the copy — or null when nothing needs keying.
    /// </summary>
    /// <remarks>
    /// Re-checked rather than trusted: the playhead can move while a gizmo
    /// session is open, and keying a cel the artist has since left would put a
    /// drawing where they are standing now and still transform the one they
    /// are not.
    /// </remarks>
    private Frame? KeyHeldCelForCommit()
    {
        if (_transform.HeldFrameIdToKey is not { } heldId) return null;
        if (ActiveLayer is not { } layer || layer.Cels.Count == 0) return null;
        var here = Math.Clamp(CurrentFrameIndex, 0, layer.Cels.Count - 1);
        if (layer.Cels[here].Frame is not null) return null;                    // keyed since
        if (ExposureSheet.ExposedFrame(layer, here)?.Id != heldId) return null; // a different hold
        return PaintTargetOrKey();
    }

    private void CommitTransformCore(TransformOps.PointMap map, double sizeScale, SKMatrix baselineMatrix)
    {
        var frames = _transform.Frames.ToList();
        // The one point where a held cel becomes a drawing of its own. Before
        // this line the gesture has changed nothing — which is what lets Ctrl+T
        // followed by Escape leave the timeline as it was.
        if (KeyHeldCelForCommit() is { } keyed)
        {
            for (var i = 0; i < frames.Count; i++)
            {
                if (frames[i].Id == _transform.HeldFrameIdToKey) frames[i] = keyed;
            }
        }
        var filter = _transform.Filter;
        // The preview goes first, and not only for tidiness: it borrows the
        // cache's own bitmaps, and the invalidation below disposes them.
        _transform.ClearPreview();
        // Invalidate before the edit so the Changed refresh re-renders from
        // the transformed record.
        foreach (var frame in frames) InvalidateFrameRender(frame.Id);
        _editor.Perform(_ =>
        {
            foreach (var frame in frames)
            {
                TransformOps.TransformFrame(frame, map, sizeScale, filter);
                // Raster baselines resample once per commit; a region-limited
                // transform moves strokes only (baseline pixels stay put).
                if (filter is null && frame is Frame { PngBase64.Length: > 0 } painted)
                {
                    ResampleBaseline(painted, baselineMatrix);
                }
            }
        });
        // The selection outline rides along so a follow-up transform lines up.
        if (HasSelection)
        {
            foreach (var contour in _selectionContours)
            {
                for (var i = 0; i < contour.Count; i++)
                {
                    var pt = contour[i];
                    var (x, y) = map(pt.X, pt.Y);
                    contour[i] = pt with { X = x, Y = y };
                }
            }
            NotifySelection();
        }
        // B223: and so does the line highlight, for the same reason one line
        // along. The outline is rebuilt from the strokes rather than mapped
        // like the contours above, because the strokes have just been rewritten
        // and re-reading them cannot disagree with what was written.
        if (HasStrokeSelection) OnStrokeSelectionChanged();
        EndTransformSession();
        AiStatus = HasStrokeSelection && !HasSelection
            ? $"Transformed {TransformSubject}."
            : $"Transformed {frames.Count} drawing{(frames.Count == 1 ? "" : "s")}.";
    }

    private SKSamplingOptions SamplingFor(TransformSampling mode) => mode switch
    {
        TransformSampling.Nearest => new SKSamplingOptions(SKFilterMode.Nearest),
        TransformSampling.Bicubic => new SKSamplingOptions(SKCubicResampler.Mitchell),
        _ => new SKSamplingOptions(SKFilterMode.Linear),
    };

    /// <summary>
    /// Re-render a frame's baseline pixels through a matrix. Does nothing when the
    /// frame has no baseline.
    /// </summary>
    /// <remarks>
    /// The early return rather than a <c>!</c> at the one call site: the baseline
    /// is nullable now, every caller already tests it, and a method that is correct
    /// on its own does not need the next caller to remember. Nothing to resample is
    /// not an error.
    /// </remarks>
    private void ResampleBaseline(Frame frame, SKMatrix matrix)
    {
        if (frame.PngBase64 is not { Length: > 0 } encoded) return;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            using var src = SKBitmap.Decode(bytes);
            if (src is null) return;
            var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface is null) return;
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            surface.Canvas.SetMatrix(matrix);
            using var image = SKImage.FromBitmap(src);
            surface.Canvas.DrawImage(image, 0, 0, SamplingFor(TransformSampling));
            using var snap = surface.Snapshot();
            using var data = snap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            frame.PngBase64 = Convert.ToBase64String(data.ToArray());
        }
        catch (FormatException)
        {
            // Corrupt baseline: leave it untouched rather than destroy pixels.
        }
    }
}
