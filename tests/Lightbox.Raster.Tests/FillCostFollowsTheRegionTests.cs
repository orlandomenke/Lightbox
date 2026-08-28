using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B342 — a fill must cost the region it fills, not the page it sits on.
/// </summary>
/// <remarks>
/// <para>
/// The whole of <see cref="FloodFill.Fill"/> was proportional to the canvas:
/// the barrier map classified every pixel against the seed, gap closing and
/// overfill each swept the page twice, the area was counted with a pass over
/// it, and the contour tracer flooded everything the region was <em>not</em>
/// to decide which empty pixels were holes. The measurement that named it is
/// the one this file's first test now guards: on a 1920×1080 canvas a
/// <b>2,556</b>-pixel fill cost <b>170 ms</b> against <b>258 ms</b> for a
/// <b>120,019</b>-pixel one — a region 47× smaller for two thirds of the
/// price, which is a fill that is not measuring what it fills.
/// </para>
/// <para>
/// <b>The bar is a ratio, and it is set by breaking it.</b> Absolute
/// milliseconds mean one thing on the owner's machine and another on a
/// contended runner, and this suite runs beside three others. What has to be
/// true everywhere is the shape: a fill of a small region costs a fraction of
/// a fill of a large one on the same page. Measured at 960×540 — the size
/// these run at, half the size the numbers above were taken at — the old code
/// reads <b>0.87×</b> for 0.5% of the area and the new one <b>0.02×</b>. The
/// bar is 0.5: an order of magnitude of room on the fixed side, and no way for
/// the broken build to reach it.
/// </para>
/// </remarks>
[Collection("Registries")]
public class FillCostFollowsTheRegionTests(ITestOutputHelper output)
{
    // 960×540 rather than the 1920×1080 every number below was measured at:
    // a suite that renders page after page at full size is what tipped the CI
    // runner over on B341's guards. The claim here is a ratio, and a ratio does
    // not care how big the page is.
    private const int W = 960;
    private const int H = 540;

    /// <summary>
    /// A page ruled into <paramref name="cells"/> × <paramref name="cells"/>
    /// boxes by black lines: every box is its own flood region, and the more
    /// boxes there are the smaller each one is.
    /// </summary>
    private static SKBitmap Ruled(int cells)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.White);
        using var canvas = new SKCanvas(bmp);
        using var pen = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = 8,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false,
        };
        for (var i = 0; i <= cells; i++)
        {
            var t = i / (double)cells;
            canvas.DrawLine((float)(t * (W - 20)) + 10, 10, (float)(t * (W - 20)) + 10, H - 10, pen);
            canvas.DrawLine(10, (float)(t * (H - 20)) + 10, W - 10, (float)(t * (H - 20)) + 10, pen);
        }
        canvas.Flush();
        return bmp;
    }

    /// <summary>The shipped defaults — gap closing and overfill both on.</summary>
    private static FloodFill.Options Shipped => new(32, 4, 2);

    private static double Best(int runs, Action a)
    {
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            a();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ASmallFillCostsFarLessThanALargeOneOnTheSamePage()
    {
        using var wide = Ruled(2);     // four big boxes
        using var fine = Ruled(24);    // five hundred small ones

        var big = FloodFill.Fill(wide, 150, 100, Shipped);
        var small = FloodFill.Fill(fine, 24, 16, Shipped);
        Assert.NotNull(big);
        Assert.NotNull(small);
        Assert.True(big!.Area > small!.Area * 10, "the two regions are not different enough to compare");

        var bigMs = Best(3, () => FloodFill.Fill(wide, 150, 100, Shipped));
        var smallMs = Best(3, () => FloodFill.Fill(fine, 24, 16, Shipped));
        output.WriteLine(
            $"{big.Area} px in {bigMs:F1} ms; {small.Area} px in {smallMs:F1} ms "
            + $"— {smallMs / bigMs:F2}× for {(double)small.Area / big.Area:P1} of the area");

        Assert.True(
            smallMs < bigMs * 0.5,
            $"a {small.Area}px fill cost {smallMs:F1} ms against {bigMs:F1} ms for a {big.Area}px one "
            + "— the cost is still the page rather than the region");
    }

    // ---- and it still fills the same thing --------------------------------

    /// <summary>
    /// The on-demand wall answers what the eager map answered.
    /// </summary>
    /// <remarks>
    /// The classification moved from a page-wide array to a memoised
    /// pixel-at-a-time test, and gap closing from <c>gap</c> rounds of dilating
    /// that array to a diamond scan around each pixel asked about. Both are
    /// meant to be the same predicate; a region that came back a different size
    /// or shape would mean they are not. Compared against the eager road by
    /// running the same fill on a bitmap whose colour type the lazy path
    /// declines — <c>Rgb565</c> — which is the fallback it takes when it cannot
    /// read the bytes.
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]      // no gap closing, no overfill
    [InlineData(4, 0)]      // gap closing only
    [InlineData(0, 2)]      // overfill only
    [InlineData(4, 2)]      // the shipped defaults
    [InlineData(0, -2)]     // underfill
    [InlineData(12, 6)]     // more of both than anyone sets
    public void TheOnDemandWallFillsWhatTheWholeMapFilled(double gap, double grow)
    {
        using var fast = Ruled(6);
        using var slow = Slowed(fast);
        var options = new FloodFill.Options(32, gap, grow);

        var quick = FloodFill.Fill(fast, 100, 60, options);
        var plain = FloodFill.Fill(slow, 100, 60, options);

        Assert.NotNull(quick);
        Assert.NotNull(plain);
        output.WriteLine($"gap={gap} grow={grow}: {quick!.Area} px against {plain!.Area} px");
        Assert.Equal(plain.Area, quick.Area);
        Assert.Equal(plain.Outer.Count, quick.Outer.Count);
        Assert.Equal(plain.Holes.Count, quick.Holes.Count);
        for (var i = 0; i < plain.Outer.Count; i++)
        {
            Assert.Equal(plain.Outer[i].X, quick.Outer[i].X, 3);
            Assert.Equal(plain.Outer[i].Y, quick.Outer[i].Y, 3);
        }
    }

    /// <summary>
    /// The same picture in a colour type the fast road declines, so the fill
    /// takes the eager one.
    /// </summary>
    /// <remarks>
    /// Rgb565 has no alpha, and every colour here is opaque black or opaque
    /// white, so the two bitmaps hold the same picture as far as the seed
    /// comparison is concerned — which is what makes them comparable at all.
    /// </remarks>
    private static SKBitmap Slowed(SKBitmap source)
    {
        var slow = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgb565, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(slow);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return slow;
    }

    /// <summary>
    /// Turning the gap slider up costs more, but not catastrophically more.
    /// </summary>
    /// <remarks>
    /// <b>The first attempt at this work made a fill with a large gap into a
    /// hang.</b> It answered "may the walk cross here" by scanning a diamond of
    /// radius <c>gap</c> around every pixel touched — 2·gap²+2·gap+1 reads each,
    /// which is 13 at the shipped setting and 2,113 at the slider's far end. A
    /// test that nudged the gap to 40 stopped coming back.
    /// <para>
    /// Gap closing is <c>gap</c> rounds of a dilate, so its cost is <em>linear</em>
    /// in the gap and always was; a fill that goes quadratic in a number an
    /// artist can drag is the shape of fault this asserts against. The bar is
    /// generous — the far end of the slider may cost several times the default,
    /// and must not cost hundreds.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Performance")]
    public void TurningTheGapUpDoesNotGoQuadratic()
    {
        using var page = Ruled(6);
        var shipped = Best(3, () => FloodFill.Fill(page, 100, 60, new FloodFill.Options(32, 4, 2)));
        var wide = Best(3, () => FloodFill.Fill(page, 100, 60, new FloodFill.Options(32, 64, 2)));
        output.WriteLine(
            $"gap 4 in {shipped:F1} ms, gap 64 in {wide:F1} ms — {wide / shipped:F1}×");
        Assert.True(
            wide < shipped * 40,
            $"the widest gap cost {wide:F1} ms against {shipped:F1} ms for the shipped one");
    }

    /// <summary>A region with a hole in it still reports the hole.</summary>
    /// <remarks>
    /// The tracer no longer floods the page to work out which empty pixels are
    /// holes — it works inside the region's own box, one pixel wider, on the
    /// argument that the ring just outside a region is empty and reaches the
    /// border. This is that argument checked rather than asserted.
    /// </remarks>
    [Fact]
    public void ARegionWithAnIslandInItStillReportsTheHole()
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        {
            using var wall = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            // A box near the bottom-right, so a page-wide scan and a windowed
            // one would disagree about where to start looking.
            canvas.DrawRect(600, 300, 250, 190, new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8,
                IsAntialias = false,
            });
            canvas.DrawRect(690, 360, 60, 60, wall);   // the island
            canvas.Flush();
        }

        using (bmp)
        {
            var result = FloodFill.Fill(bmp, 625, 325, new FloodFill.Options(32, 0, 0));
            Assert.NotNull(result);
            output.WriteLine(
                $"area {result!.Area}, outer {result.Outer.Count} pts, {result.Holes.Count} holes");
            Assert.Single(result.Holes);
        }
    }
}
