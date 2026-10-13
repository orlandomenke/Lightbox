using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B460: a region repaint replays a mirrored stroke when only its reflection
/// lies in the region.
/// </summary>
/// <remarks>
/// <c>RepaintRegion</c> clears a rectangle and replays the strokes that reach
/// it — and asked "reach" of the stroke as it was drawn. A stroke drawn on the
/// left under a mirror has a copy on the right; a rectangle on the right was
/// cleared, the stroke was passed over because its drawn copy is nowhere near,
/// and the reflection was gone from the drawing until something rebuilt it
/// whole. Found by the adversary reading B450, which did not cause it: undo's
/// region replay (B327) has had it since symmetry arrived.
/// </remarks>
public class RegionRepaintMirroredTests(ITestOutputHelper output)
{
    private const int W = 400, H = 240;

    private static Stroke Line(double x0, double y0, double x1, double y1, SymmetryAxis? axis = null)
    {
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#203040",
            Brush = new BrushSettings { Size = 10, Hardness = 0.9 },
            Symmetry = axis,
        };
        for (var k = 0; k <= 20; k++)
        {
            var t = k / 20.0;
            stroke.Points.Add(new StrokePoint { X = x0 + (x1 - x0) * t, Y = y0 + (y1 - y0) * t, Pressure = 0.8 });
        }
        return stroke;
    }

    private static long Differing(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        long n = 0;
        for (var i = 0; i < sa.Length; i++) if (sa[i] != sb[i]) n++;
        return n;
    }

    [Theory]
    [InlineData(1, true)]    // a plain mirror
    [InlineData(4, false)]   // four turns, no mirror
    [InlineData(3, true)]    // a kaleidoscope
    public void AReflectionAloneInTheRegionIsRepainted(int order, bool mirror)
    {
        var axis = new SymmetryAxis { CenterX = W / 2.0, CenterY = H / 2.0, Order = order, Mirror = mirror };
        var frame = new Frame { Id = "drawing" };
        // Drawn in the top-left corner; its copies land elsewhere on the page.
        frame.Strokes.Add(Line(30, 30, 110, 70, axis));
        // And an ordinary line across the middle, so the region is not empty of other ink.
        frame.Strokes.Add(Line(20, 200, 380, 190));

        using var whole = FrameRasterizer.Materialize(frame, W, H);

        // The right-hand half and the bottom half: neither holds the drawn copy.
        foreach (var region in new[] { new SKRectI(W / 2 + 10, 0, W, H), new SKRectI(0, H / 2 + 10, W, H) })
        {
            using var patched = whole.Copy();
            Assert.True(FrameRasterizer.RepaintRegion(patched, frame, region, W, H));
            var differing = Differing(patched, whole);
            output.WriteLine($"order {order}, mirror {mirror}, region at {region.Left},{region.Top}: {differing} bytes differ");
            Assert.True(differing == 0, $"{differing} bytes differ — a copy was cleared and not put back");
        }
    }
}
