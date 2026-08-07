using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The immutable hand-off between the UI thread (which composites the scene)
/// and Avalonia's render thread (which just blits it). The render thread must
/// never touch the mutable document — this object is the only thing that
/// crosses the boundary.
///
/// <see cref="Seq"/> orders snapshots so the canvas can free old images as
/// soon as a newer one has actually been rendered: holding them longer forces
/// the compositor's back-buffers into copy-on-write duplication, which on a
/// 4K document costs more than the drawing itself.
///
/// <see cref="DocViewport"/> carries the visible document rectangle (in document space),
/// enabling culled compositing for infinite canvas and performance. The viewport is
/// the rectangle the view can show at the current zoom/pan/rotation state — passed from
/// CanvasControl to enable TileCompositor to cull its work to what is visible.
/// For unbounded canvases or when compositing only a region, this prevents allocating
/// and compositing pixels the user cannot see. Null means the whole document is visible
/// (ordinary case when compose surface is full document size).
/// </summary>
public sealed class RenderSnapshot(
    SKImage image,
    int docWidth,
    int docHeight,
    long seq = 0,
    SKRectI? docViewport = null,
    SKRectI? changedInImage = null)
{
    public SKImage Image { get; } = image;

    /// <summary>
    /// The region of <see cref="Image"/> this publish actually repainted, in
    /// <em>image pixel</em> space, or null when the whole image was repainted.
    /// </summary>
    /// <remarks>
    /// This is what lets <see cref="PresentedFrame"/> upload a dab instead of a
    /// canvas (B122). Image space rather than document space on purpose: the
    /// consumer copies pixels, and asking every consumer to redo the scale and
    /// the viewport offset is how the two get out of step. Null is the safe
    /// value — it costs a full repaint and can never show stale pixels — so
    /// anything unsure should leave it null rather than guess a rectangle.
    /// </remarks>
    public SKRectI? ChangedInImage { get; } = changedInImage;

    /// <summary>
    /// The document's size — <b>always</b>, whatever the compositor chose to
    /// render. The canvas derives its fit scale and its pointer mapping from
    /// these two numbers, so reporting the image's size here instead moves the
    /// cursor off the mark it makes. <c>CursorAlignmentTests</c> holds the
    /// numbers from when it did. Use <c>Image.Width</c> for the image's size.
    /// </summary>
    public int DocWidth { get; } = docWidth;

    /// <inheritdoc cref="DocWidth"/>
    public int DocHeight { get; } = docHeight;

    /// <summary>Monotonic publish counter (0 for snapshots made outside the publish loop).</summary>
    public long Seq { get; } = seq;

    /// <summary>
    /// The document rectangle <see cref="Image"/> covers, or null when it covers
    /// the whole document.
    /// </summary>
    /// <remarks>
    /// This describes the <em>image</em>, not the canvas's current view — the
    /// painter needs to know where to put these pixels, and by the time a frame
    /// is drawn the view may already have moved on. It is deliberately not an
    /// input to the pointer mapping; see the remarks on
    /// <c>CanvasControl.ViewMatrix</c> for why that is a cycle rather than a
    /// convenience.
    /// </remarks>
    public SKRectI? DocViewport { get; } = docViewport;
}
