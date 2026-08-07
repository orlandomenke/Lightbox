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
public sealed class RenderSnapshot(SKImage image, int docWidth, int docHeight, long seq = 0, SKRectI? docViewport = null)
{
    public SKImage Image { get; } = image;
    public int DocWidth { get; } = docWidth;
    public int DocHeight { get; } = docHeight;

    /// <summary>Monotonic publish counter (0 for snapshots made outside the publish loop).</summary>
    public long Seq { get; } = seq;

    /// <summary>
    /// The visible document rectangle in document space, or null if the whole document is visible.
    /// Used by the compositor to cull work to what CanvasControl can actually show.
    /// Set by MainViewModel.SetViewport() based on CanvasControl's zoom/pan/rotation.
    /// </summary>
    public SKRectI? DocViewport { get; } = docViewport;
}
