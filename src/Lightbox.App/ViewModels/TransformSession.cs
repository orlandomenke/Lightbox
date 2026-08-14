using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The transform being dragged: which frames are in scope, which strokes it moves,
/// the composite-time matrix showing the drag, and the moving/staying pixel split
/// cached for the length of the gesture.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>MainViewModel</c> under Q78's follow-up, and it is the same
/// shape as <see cref="LivePaintSession"/>: a gesture whose state has to be raised
/// together and dropped together. Four fields spread across four partials
/// (<c>Tools.cs</c> began the session, <c>Transform.cs</c> ran it, <c>Rendering.cs</c>
/// read the preview, and the declarations sat in the hub) become one field with one
/// reset.
/// </para>
/// <para>
/// <b>The disposal contract is why this is worth a class.</b> <see cref="Parts.Owned"/>
/// separates the bitmaps this session rasterised — which it must free — from the
/// frame cache's own bitmap, borrowed when the whole frame moves and freed by the
/// cache. Getting that backwards either leaks a full-canvas bitmap per frame per
/// gesture or hands the compositor a disposed one. It was correct in the view model
/// and it was correct in exactly one place with nothing asserting it; here it is one
/// method with a test on it.
/// </para>
/// <para>
/// Building a split is deliberately <i>not</i> here. It needs the frame cache, the
/// scene size and the rasteriser, which is view-model work; this owns the storage and
/// the lifetime, which is the part that goes wrong.
/// </para>
/// </remarks>
sealed class TransformSession
{
    /// <summary>
    /// A frame's pixels split into the part the transform moves and the part it
    /// leaves behind. With no selection everything moves and <see cref="Static"/> is
    /// null, which is the ordinary case and costs no extra render at all —
    /// <see cref="Moving"/> is the frame's own cached bitmap, borrowed rather than
    /// copied, and <see cref="Owned"/> is false so it is not freed here.
    /// </summary>
    internal sealed record Parts(SKBitmap Moving, SKBitmap? Static, bool Owned);

    private readonly Dictionary<string, Parts> _parts = [];

    /// <summary>The frames the transform applies to, in scope order.</summary>
    internal List<Frame> Frames { get; } = [];

    /// <summary>
    /// Which strokes move, or null when everything does. Null is not merely the
    /// default — it is the fast path, and <see cref="Parts"/> borrows rather than
    /// renders when it holds.
    /// </summary>
    internal Func<Stroke, bool>? Filter { get; private set; }

    /// <summary>
    /// The gizmo's current shape, in document space, or null when nothing is being
    /// previewed.
    /// </summary>
    /// <remarks>
    /// A transform used to show only its outline: the drawing sat still under a
    /// moving box, and the artist did not see the result until they pressed Enter.
    /// Judging a rotation from a rectangle is guesswork, and the same argument the
    /// live-stroke preview settled applies here — what you are making has to be
    /// visible while you make it.
    ///
    /// This is a <b>composite-time</b> matrix and nothing else. The stroke record is
    /// mapped once, on apply, by <c>CommitTransformCore</c>. Re-mapping N frames of
    /// geometry on every pointer event would be both slow and destructive of undo;
    /// moving finished pixels for one frame is neither.
    /// </remarks>
    internal SKMatrix? Preview { get; set; }

    /// <summary>
    /// Doc-space bounds of everything the gesture moves, render reach included
    /// — or null when the moving pixels cannot be bounded from the stroke
    /// record (a raster baseline or a placement moves with the layer), in
    /// which case the preview repaints the whole canvas as it always did.
    /// </summary>
    /// <remarks>
    /// What makes the preview bounded work (invariant 6). The preview used to
    /// invalidate the whole canvas per pointer event, which on a large
    /// document is a full recomposite per event — paced to the present rate,
    /// so the drag read as a slideshow exactly where a brush stroke stays
    /// live. With the bounds known, each event repaints only where the moving
    /// pixels were and where they now are.
    /// </remarks>
    internal SKRect? MovingBounds { get; set; }

    /// <summary>
    /// The id of the drawing this session is <em>borrowing</em> — set when the
    /// gesture opened on a held cel that a commit must key rather than write
    /// through. Null when the cel has a drawing of its own.
    /// </summary>
    /// <remarks>
    /// The record must not change until the commit. Keying when the session
    /// opened was the obvious place and the wrong one: Ctrl+T raises the gizmo
    /// before the artist has done anything, so it put a drawing on the
    /// timeline for a tool that was merely picked up, and Escape left it
    /// there. The preview is not an edit (invariant 1), so an abandoned
    /// transform has to leave the document exactly as it found it.
    /// </remarks>
    internal string? HeldFrameIdToKey { get; set; }

    /// <summary>Take the scope for a new gesture, replacing any previous one.</summary>
    internal void Begin(IEnumerable<Frame> frames, Func<Stroke, bool>? filter)
    {
        Frames.Clear();
        Frames.AddRange(frames);
        Filter = filter;
    }

    /// <summary>Drop the whole session — scope, filter, preview and cached splits.</summary>
    internal void End()
    {
        Frames.Clear();
        Filter = null;
        MovingBounds = null;
        HeldFrameIdToKey = null;
        ClearPreview();
    }

    /// <summary>
    /// Drop the preview and every split cached for it, freeing the bitmaps this
    /// session rendered and leaving the borrowed ones alone.
    /// </summary>
    internal void ClearPreview()
    {
        Preview = null;
        foreach (var parts in _parts.Values)
        {
            if (!parts.Owned) continue;
            parts.Moving.Dispose();
            parts.Static?.Dispose();
        }
        _parts.Clear();
    }

    /// <summary>The split already built for this frame during this gesture, if any.</summary>
    internal Parts? Cached(string frameId) => _parts.TryGetValue(frameId, out var parts) ? parts : null;

    /// <summary>Keep a split for the rest of the gesture, and take over freeing it.</summary>
    internal void Remember(string frameId, Parts parts) => _parts[frameId] = parts;
}
