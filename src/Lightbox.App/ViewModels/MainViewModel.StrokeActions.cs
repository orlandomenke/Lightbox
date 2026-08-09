using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// V3: doing something to the lines the Arrow picked — move, delete, recolour.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these do not go through the transform session.</b> The Move tool gets
/// live preview and one-undo-step-per-drag from <c>BeginTransform</c> /
/// <c>PreviewTransform</c> / <c>CommitTransformAffine</c>, and reusing that here
/// looked obvious. It is not available yet: every <see cref="TransformScope"/> is
/// a set of <em>cels</em>, and there is no scope meaning "these strokes inside
/// this cel". Teaching the session that is a real change to the riskiest file in
/// <c>HOTSPOTS.md</c> and it is a second objective, so it is a second branch.
/// What lands here is the part that is independent of it — and delete and
/// recolour would never have gone through a transform anyway.
/// </para>
/// <para>
/// <b>Every closure looks its strokes up by id inside the given document.</b>
/// This is not defensiveness, it is B103's lesson: a delta step can be undone
/// <em>after</em> a snapshot undo has replaced the whole instance tree, so a
/// captured <see cref="Stroke"/> reference writes to an object nothing renders.
/// The bug that taught this looked like "undo does nothing".
/// </para>
/// <para>
/// <b>Q26 is why moving a line is allowed to be this simple.</b> Every dab
/// dynamic is seeded from dab position via <c>Hash01</c>, so shifting
/// coordinates re-rolls scatter, size, roundness, rotation and the colour
/// jitters: the line arrives somewhere else with a different grain. That was
/// asked and answered — <b>(a), accept it, "the grain belongs to the canvas"</b>
/// — so there is no seed origin to carry and no new field on the record. The
/// manual says so, and says "move the layer rather than the line" for an artist
/// who needs the mark preserved exactly.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>How far one arrow-key nudge moves a line, in document pixels.</summary>
    /// <remarks>
    /// One pixel, and ten with Shift — the pair every editor uses, and the reason
    /// a nudge exists at all: a drag cannot reliably place a line one pixel over.
    /// </remarks>
    public const double NudgeStep = 1.0;

    /// <summary>The Shift multiple, so "a bigger nudge" is one decision not two.</summary>
    public const double NudgeStepCoarse = 10.0;

    /// <summary>
    /// Delete every selected line. Returns how many went.
    /// </summary>
    /// <remarks>
    /// Undo puts them back <em>where they were in the list</em>, not on the end.
    /// Draw order is list order, so restoring in the wrong place would undo the
    /// delete and silently change which line covers which — an undo that leaves
    /// the document different is worse than one that fails.
    /// </remarks>
    public int DeleteSelectedStrokes()
    {
        if (StrokeEditTarget() is not { } target) return 0;
        var (frameId, strokes) = target;

        // Descending, so removing one does not shift the index of the next.
        var doomed = strokes
            .Select((stroke, index) => (index, stroke))
            .Where(entry => Selection.IsStrokeSelected(entry.stroke.Id))
            .OrderByDescending(entry => entry.index)
            .ToList();
        if (doomed.Count == 0) return 0;

        _editor.PerformDelta(
            apply: doc =>
            {
                foreach (var (_, stroke) in doomed) RemoveStrokeById(doc, frameId, stroke.Id);
            },
            revert: doc =>
            {
                if (StrokeListIn(doc, frameId) is not { } list) return;
                // Ascending on the way back: each insert makes room for the next.
                foreach (var (index, stroke) in Enumerable.Reverse(doomed))
                {
                    list.Insert(Math.Min(index, list.Count), stroke);
                }
            },
            affectedFrameId: frameId);

        ClearStrokeSelection();
        AfterStrokeEdit(frameId);
        AiStatus = doomed.Count == 1 ? "Deleted the line." : $"Deleted {doomed.Count} lines.";
        return doomed.Count;
    }

    /// <summary>
    /// Move every selected line by a document-space offset. Returns how many moved.
    /// </summary>
    /// <remarks>
    /// <b>Undo restores the original coordinates rather than subtracting the
    /// offset</b>, and the difference is not pedantry. <c>a + d - d</c> is not
    /// always <c>a</c> in IEEE-754, and every dab dynamic is seeded from the bits
    /// of a coordinate — so an inexact undo would return the line to visibly the
    /// right place with a quietly different grain. Copying the points costs one
    /// array per selected stroke per completed move, which is user-paced work.
    /// </remarks>
    public int MoveSelectedStrokes(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return 0;
        if (StrokeEditTarget() is not { } target) return 0;
        var (frameId, strokes) = target;

        var moving = strokes.Where(s => Selection.IsStrokeSelected(s.Id)).ToList();
        if (moving.Count == 0) return 0;

        // Snapshotted before the closure runs, so undo has the exact bits back.
        var before = moving.ToDictionary(
            s => s.Id,
            s => (
                Points: s.Points.ToList(),
                Holes: s.Holes?.Select(h => h.ToList()).ToList(),
                // Snapshotted alongside, or an undo would put the points back and
                // leave the nodes where the move left them — a path and its points
                // disagreeing, which is the one state StrokePath forbids.
                Path: s.Path?.Clone()));
        var ids = before.Keys.ToList();

        _editor.PerformDelta(
            apply: doc =>
            {
                foreach (var stroke in StrokesById(doc, frameId, ids)) Offset(stroke, dx, dy);
            },
            revert: doc =>
            {
                foreach (var stroke in StrokesById(doc, frameId, ids))
                {
                    if (!before.TryGetValue(stroke.Id, out var original)) continue;
                    stroke.Points = [.. original.Points];
                    stroke.Holes = original.Holes?.Select(h => h.ToList()).ToList();
                    stroke.Path = original.Path?.Clone();
                }
            },
            affectedFrameId: frameId);

        AfterStrokeEdit(frameId);
        // Re-published so the highlight follows the line rather than staying
        // where it was drawn — the selection is unchanged, its geometry is not.
        OnStrokeSelectionChanged();
        AiStatus = moving.Count == 1
            ? $"Moved the line by {dx:0.#}, {dy:0.#}."
            : $"Moved {moving.Count} lines by {dx:0.#}, {dy:0.#}.";
        return moving.Count;
    }

    /// <summary>Nudge by whole pixels — the keyboard's version of a move.</summary>
    public int NudgeSelectedStrokes(double ex, double ey, bool coarse = false)
    {
        var step = coarse ? NudgeStepCoarse : NudgeStep;
        return MoveSelectedStrokes(ex * step, ey * step);
    }

    /// <summary>
    /// Recolour every selected line to a literal colour. Returns how many changed.
    /// </summary>
    /// <remarks>
    /// <b>The swatch link has to be cut, and forgetting it is the whole trap.</b>
    /// <see cref="Stroke.SwatchId"/> wins at render time — that is what makes live
    /// palette recolour work — so setting <see cref="Stroke.Color"/> on a stroke
    /// that came from a swatch changes a field nothing reads, and the line stays
    /// exactly the colour it was. Asking for a literal colour is asking to stop
    /// following the palette, so the link goes and the swatch itself is untouched.
    /// A test asserts precisely this, because it is invisible otherwise.
    /// </remarks>
    public int RecolourSelectedStrokes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return 0;
        if (StrokeEditTarget() is not { } target) return 0;
        var (frameId, strokes) = target;

        var repainting = strokes
            .Where(s => Selection.IsStrokeSelected(s.Id))
            .Where(s => s.Color != hex || s.SwatchId is not null)
            .ToList();
        if (repainting.Count == 0) return 0;

        var before = repainting.ToDictionary(s => s.Id, s => (s.Color, s.SwatchId, s.PaletteId));
        var ids = before.Keys.ToList();

        _editor.PerformDelta(
            apply: doc =>
            {
                foreach (var stroke in StrokesById(doc, frameId, ids))
                {
                    stroke.Color = hex;
                    stroke.SwatchId = null;
                    stroke.PaletteId = null;
                }
            },
            revert: doc =>
            {
                foreach (var stroke in StrokesById(doc, frameId, ids))
                {
                    if (!before.TryGetValue(stroke.Id, out var was)) continue;
                    (stroke.Color, stroke.SwatchId, stroke.PaletteId) = was;
                }
            },
            affectedFrameId: frameId);

        AfterStrokeEdit(frameId);
        AiStatus = repainting.Count == 1
            ? $"Recoloured the line to {hex}."
            : $"Recoloured {repainting.Count} lines to {hex}.";
        return repainting.Count;
    }

    /// <summary>Recolour to whatever the foreground colour currently is.</summary>
    public int RecolourSelectedStrokesToForeground() => RecolourSelectedStrokes(ForegroundColorHex);

    // ---- the commands the bar and the keyboard reach ---------------------------
    //
    // Thin wrappers on purpose: the methods above return a count, which is what
    // the tests assert on, and a command returns nothing. Wrapping keeps the
    // testable shape and still gives the options bar and `ShortcutMap` something
    // to bind to — a capability with no command is one the configuration system
    // cannot see, which is the failure the registration checklist exists for.

    [RelayCommand]
    private void DeleteSelectedLines() => DeleteSelectedStrokes();

    [RelayCommand]
    private void RecolourSelectedLines() => RecolourSelectedStrokesToForeground();

    /// <summary>
    /// Nudge, as the keyboard reaches it. Returns whether it did anything, so the
    /// key handler knows whether to let the key fall through to the timeline.
    /// </summary>
    /// <remarks>
    /// <b>Why the arrow keys can mean two things safely.</b> ← and → step frames
    /// everywhere else, and taking them would be exactly the ambiguity Q48 exists
    /// to avoid. The condition here is narrow enough to be predictable: the Arrow
    /// tool has to be the active tool <em>and</em> something has to be selected.
    /// That is Illustrator's and Figma's rule, and it means an artist who is not
    /// holding a selection never loses frame stepping. The cost is real and worth
    /// stating: with the Arrow active and a line selected, ← and → do not step
    /// frames — let go of the selection, or use another tool.
    /// </remarks>
    public bool NudgeSelectionFromKeyboard(double ex, double ey, bool coarse)
    {
        if (!IsArrowTool || !HasStrokeSelection) return false;
        return NudgeSelectedStrokes(ex, ey, coarse) > 0;
    }

    /// <summary>
    /// The frame these actions may edit, or null when they may not.
    /// </summary>
    /// <remarks>
    /// One gate for all three, checked in the order an artist would want to hear
    /// about it: nothing selected is silence, a locked layer is a message.
    /// <see cref="PaintTarget"/> rather than <c>PaintTargetOrKey</c> — acting on a
    /// selection must not bring a cel into existence, for the same reason picking
    /// must not.
    /// </remarks>
    private (string FrameId, List<Stroke> Strokes)? StrokeEditTarget()
    {
        if (!HasStrokeSelection) return null;
        if (PaintTarget() is not { } frame) return null;
        if (!CanEdit(ActiveLayer, "change lines on it")) return null;
        return (frame.Id, StrokesOf(frame));
    }

    /// <summary>The named strokes inside a given document instance, in list order.</summary>
    private static IEnumerable<Stroke> StrokesById(Doc doc, string frameId, List<string> ids)
    {
        if (StrokeListIn(doc, frameId) is not { } list) yield break;
        var wanted = ids.ToHashSet();
        foreach (var stroke in list)
        {
            if (wanted.Contains(stroke.Id)) yield return stroke;
        }
    }

    /// <summary>Shift every point of a stroke, contours and holes alike.</summary>
    /// <remarks>
    /// <b>Holes are not optional.</b> A fill is a contour plus its even-odd
    /// holes; moving the contour and leaving the holes puts the gaps somewhere
    /// else entirely, which reads as the fill having been corrupted rather than
    /// moved.
    /// </remarks>
    private static void Offset(Stroke stroke, double dx, double dy)
    {
        stroke.Points = [.. stroke.Points.Select(p => Shift(p, dx, dy))];
        stroke.Holes = stroke.Holes?
            .Select(hole => hole.Select(p => Shift(p, dx, dy)).ToList())
            .ToList();

        // The authored path moves with the points or it is a lie about where the
        // line is (see StrokePath's invariant). A translation is the easy case:
        // handles are offsets from their node, so moving the node moves them and
        // nothing about the curve's shape changes.
        if (stroke.Path is { } path)
        {
            for (var i = 0; i < path.Nodes.Count; i++)
            {
                var n = path.Nodes[i];
                path.Nodes[i] = n with { X = n.X + dx, Y = n.Y + dy };
            }
        }
    }

    private static StrokePoint Shift(StrokePoint p, double dx, double dy) =>
        p with { X = p.X + dx, Y = p.Y + dy };

    /// <summary>
    /// Repaint what a stroke edit touched: this frame, its thumbnail, nothing else.
    /// </summary>
    /// <remarks>
    /// The cached bitmap has to be dropped rather than appended to. An append is
    /// enough for a new stroke, but a moved, recoloured or deleted one changes
    /// pixels that are already down, so the frame is rebuilt from the record —
    /// which is invariant 1 doing its job rather than a cost to be avoided.
    /// </remarks>
    private void AfterStrokeEdit(string frameId)
    {
        _cache.Invalidate(frameId);
        _dirtyThumbIds.Add(frameId);
        PublishSnapshot();
        RefreshThumbnails();
    }
}
