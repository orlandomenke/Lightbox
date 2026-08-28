using Lightbox.Core.Documents;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B341 — the walk inside <see cref="FloodFill.TraceAllContours"/> was rewritten
/// for speed, so these pin what it must still answer and what it must not do to
/// the stack getting there.
/// </summary>
/// <remarks>
/// The rewrite swapped a queue for a stack and a <c>yield return</c> neighbour
/// enumerator for four explicit reads. Component membership does not depend on
/// the order a flood visits pixels, so nothing about the answer may move —
/// which is easy to say and worth asserting, because "the same set" is exactly
/// the kind of claim a rewrite quietly breaks at a boundary.
/// </remarks>
[Collection("Registries")]
public class TraceAllContoursTests(ITestOutputHelper output)
{
    /// <summary>A mask with the given filled rectangles.</summary>
    private static bool[] Mask(int w, int h, params (int X0, int Y0, int X1, int Y1)[] boxes)
    {
        var mask = new bool[w * h];
        foreach (var (x0, y0, x1, y1) in boxes)
        {
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++) mask[(y * w) + x] = true;
            }
        }
        return mask;
    }

    private static string Shape(List<List<StrokePoint>> contours) =>
        string.Join(" | ", contours.Select(c =>
            $"{c.Count}:{c.Min(p => p.X)},{c.Min(p => p.Y)}-{c.Max(p => p.X)},{c.Max(p => p.Y)}"));

    [Fact]
    public void OneRectangleTracesAsOneRing()
    {
        var contours = FloodFill.TraceAllContours(Mask(64, 64, (10, 10, 40, 30)), 64, 64);
        output.WriteLine(Shape(contours));
        Assert.Single(contours);
        // Pixel corners, half a pixel outside the filled cells — the tracer's
        // own convention, recorded here rather than changed.
        Assert.Equal(10.5, contours[0].Min(p => p.X));
        Assert.Equal(39.5, contours[0].Max(p => p.X));
        Assert.Equal(10.5, contours[0].Min(p => p.Y));
        Assert.Equal(29.5, contours[0].Max(p => p.Y));
    }

    /// <summary>
    /// Two components, found in scan order — which is what makes the contour
    /// list stable enough to be part of a content-hashed clip region.
    /// </summary>
    [Fact]
    public void DisjointRegionsComeBackInScanOrder()
    {
        var contours = FloodFill.TraceAllContours(
            Mask(64, 64, (4, 4, 12, 12), (40, 40, 56, 56)), 64, 64);
        output.WriteLine(Shape(contours));
        Assert.Equal(2, contours.Count);
        Assert.Equal(4.5, contours[0].Min(p => p.Y));
        Assert.Equal(40.5, contours[1].Min(p => p.Y));
    }

    /// <summary>A ring gives its outside and its hole, which is what even-odd reads.</summary>
    [Fact]
    public void ARingGivesItsHoleToo()
    {
        var mask = Mask(64, 64, (8, 8, 56, 56));
        for (var y = 20; y < 44; y++)
        {
            for (var x = 20; x < 44; x++) mask[(y * 64) + x] = false;
        }
        var contours = FloodFill.TraceAllContours(mask, 64, 64);
        output.WriteLine(Shape(contours));
        Assert.Equal(2, contours.Count);
        Assert.Equal(8.5, contours[0].Min(p => p.X));    // the outside
        Assert.Equal(20.5, contours[1].Min(p => p.X));   // the hole's own ring
    }

    /// <summary>
    /// A page-sized region, walked without running out of stack.
    /// </summary>
    /// <remarks>
    /// <b>This is the one that caught the first attempt at the rewrite.</b> It
    /// put a <c>stackalloc</c> for the four neighbours inside the flood loop —
    /// which is not freed until the method returns, so a region of a million
    /// pixels took the whole stack down and the test host exited with
    /// <c>0xC00000FD</c> rather than a failure. A crashed process is not a red
    /// test, so it needs a case that walks something page-sized on purpose.
    /// </remarks>
    [Fact]
    public void AWholePageIsWalkedWithoutRunningOutOfStack()
    {
        const int w = 1920, h = 1080;
        // The complement of a selection: everything but a box in the middle,
        // which is the shape the transform tool asks for.
        var mask = Mask(w, h, (0, 0, w, h));
        for (var y = 200; y < 800; y++)
        {
            for (var x = 300; x < 1200; x++) mask[(y * w) + x] = false;
        }
        var contours = FloodFill.TraceAllContours(mask, w, h);
        output.WriteLine(Shape(contours));
        Assert.Equal(2, contours.Count);   // the page, and the box punched out of it
    }
}
