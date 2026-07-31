using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The immutable hand-off between the UI thread (which composites the scene)
/// and Avalonia's render thread (which just blits it). The render thread must
/// never touch the mutable document — this object is the only thing that
/// crosses the boundary.
/// </summary>
public sealed class RenderSnapshot(SKImage image, int docWidth, int docHeight)
{
    public SKImage Image { get; } = image;
    public int DocWidth { get; } = docWidth;
    public int DocHeight { get; } = docHeight;
}
