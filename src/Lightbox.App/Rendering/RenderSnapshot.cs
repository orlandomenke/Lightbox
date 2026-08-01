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
/// </summary>
public sealed class RenderSnapshot(SKImage image, int docWidth, int docHeight, long seq = 0)
{
    public SKImage Image { get; } = image;
    public int DocWidth { get; } = docWidth;
    public int DocHeight { get; } = docHeight;

    /// <summary>Monotonic publish counter (0 for snapshots made outside the publish loop).</summary>
    public long Seq { get; } = seq;
}
