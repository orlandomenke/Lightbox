using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The dirty-region clip covers a plane's actual paint position. Found by
/// adversarial review of the multiplane branch: a stroke's dirty rect is
/// document-space on the layer being painted, the clip machinery maps rects
/// through the camera matrix alone, and a layer with depth lands its pixels
/// somewhere else — so an incremental publish repainted a rectangle the
/// stroke was not in. <see cref="ParallaxTransform.CoverPlane"/> is the fix;
/// this holds it.
/// </summary>
public class ParallaxDirtyRegionTests
{
    [Fact]
    public void TheWidenedRectCoversWhereThePlaneActuallyPaints()
    {
        const int w = 800, h = 600;
        // A camera panned well off centre, over a layer far behind the plane —
        // the adversarial probe's own numbers, kept because they are the
        // strongest parallax an ordinary shot produces.
        var home = CameraFraming.Centred(w, h);
        var framing = home with { X = home.X + 400 };

        var frame = ParallaxTransform.Prepare(framing, home, w, h);
        Assert.NotNull(frame);
        var parallax = frame!.Value.MatrixFor(3000); // factor 0.25
        Assert.NotNull(parallax);

        var cameraM = CameraTransform.Matrix(framing, w, h, renderScale: 1.0);
        var p = new SKPoint(100, 100);
        // What ComposeInto actually draws: cameraM(parallax(p)).
        var actual = cameraM.MapPoint(parallax!.Value.MapPoint(p));

        var dirty = SKRectI.Round(SKRect.Create(p.X - 5, p.Y - 5, 10, 10));

        // Un-widened, the camera-only clip misses the true position — the
        // hole, asserted so this test fails loudly if the geometry ever stops
        // demonstrating it and the case silently tests nothing.
        var blind = CameraTransform.DeviceBounds(dirty, scale: 1.0, transform: cameraM);
        Assert.False(blind.Contains(actual.X, actual.Y));

        // Widened, it contains both the plane's position and the original.
        var covered = ParallaxTransform.CoverPlane(dirty, parallax.Value);
        var clip = CameraTransform.DeviceBounds(covered, scale: 1.0, transform: cameraM);
        Assert.True(clip.Contains(actual.X, actual.Y),
            $"widened clip {clip} misses the parallaxed paint position {actual}");
        var flat = cameraM.MapPoint(p);
        Assert.True(clip.Contains(flat.X, flat.Y),
            "widening must union, not replace: the un-parallaxed region still repaints");
    }

    [Fact]
    public void AFlatLayersRectIsUntouched()
    {
        // Depth 0 produces no matrix at all upstream, so CoverPlane is never
        // reached — but an identity matrix must also change nothing, because
        // "widen" may never mean "grow for free".
        var dirty = SKRectI.Create(40, 40, 20, 20);
        Assert.Equal(dirty, ParallaxTransform.CoverPlane(dirty, SKMatrix.CreateIdentity()));
    }
}
