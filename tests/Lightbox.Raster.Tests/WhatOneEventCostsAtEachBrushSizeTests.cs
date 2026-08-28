using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What one pointer event costs as the brush gets bigger, split into the parts
/// that could each be the reason (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>The owner's report is specific and it is the thing to explain.</b>
/// 2026-08-28, after the footprint was moved to preview scale: <em>"everything up
/// to 200 size feels responsive and fast"</em>, and at large sizes the jump is
/// still there. Brush size runs to 500. So there is a cost that grows with SIZE,
/// and the costs already characterised in this entry do not obviously do that —
/// the dab walk grows with the stroke's LENGTH, and a bigger brush lays FEWER
/// dabs over the same distance because spacing is a fraction of size.
/// </para>
/// <para>
/// <b>Three candidates, and they recommend opposite fixes</b>, which is why this
/// measures rather than reasons:
/// </para>
/// <list type="number">
/// <item><b>The colour stamp.</b> A dab's cost is its area, so one dab is
/// O(size²) — but a stroke lays O(1/size) of them per pixel travelled, so the
/// work per unit travel should come out roughly linear in size. If this is the
/// one, the fix is about how a dab is rasterised.</item>
/// <item><b>The tail rectangle.</b> It is copied out and copied back every
/// event, and its bounds are the pen's travel plus <c>DabReach</c> on all four
/// sides — which is about half the brush size. At 500 that is 254 px of margin
/// in every direction, so the rectangle is dominated by the brush rather than by
/// the travel and its AREA goes as size². If this is the one, the fix is about
/// what gets copied, not about dabs at all.</item>
/// <item><b>The footprint.</b> Already at preview scale as of this branch's
/// parent, so it should be the flattest of the three. If it is not, the scaling
/// is not doing what the last capture said it was.</item>
/// </list>
/// <para>
/// <b>Reported with the growth exponent, not a budget.</b> A threshold here
/// would be a guess at a number nobody has. What decides the next move is which
/// column grows fastest, so that is what is asserted.
/// </para>
/// </remarks>
public class WhatOneEventCostsAtEachBrushSizeTests(ITestOutputHelper output)
{
    // Deliberately not 4K. A 3840x2160 render is ~33 MB a surface and has twice
    // killed the CI runner with MSB4166, an out-of-memory wearing an
    // infrastructure failure's clothes. The quantity under test is the tail
    // RECTANGLE and the dabs in it, neither of which is a property of the
    // canvas — only the one-off allocation is, and that is not timed.
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>The scale the footprint now runs at, from this branch's parent.</summary>
    private const double FootprintScale = 0.375;

    /// <summary>
    /// How far the pen travels between two events at the speed that hurts.
    /// </summary>
    /// <remarks>
    /// From the owner's captures: the pen delivers every ~5 ms, and a fast
    /// stroke covers on the order of 60 px in that time. Held FIXED across the
    /// sizes, because the question is what the brush costs and not what the
    /// pen does — varying both would answer neither.
    /// </remarks>
    private const double TravelPerEvent = 60;

    private static BrushSettings Soft(double size) => new()
    {
        Size = size, Hardness = 0.35, Flow = 0.7, Opacity = 1,
    };

    /// <summary>A stroke of <paramref name="events"/> pointer events' worth of travel.</summary>
    private static Stroke Of(double size, int events)
    {
        var pts = new List<StrokePoint>();
        double x = Width / 2.0, y = Height / 2.0;
        // Centred and short, so a big brush's reach is not clipped by the canvas
        // edge — a clipped rectangle would flatter the very column under test.
        x -= events * TravelPerEvent / 2.0;
        for (var i = 0; i <= events * 6; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            x += TravelPerEvent / 6.0;
        }

        return new Stroke
        {
            Tool = ToolKind.Brush, Color = "#203040", Brush = Soft(size), Points = pts,
        };
    }

    private static double Ms(long since) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0
        / System.Diagnostics.Stopwatch.Frequency;

    [Fact]
    [Trait("Category", "Performance")]
    public void OneEventsWorkAsTheBrushGrows()
    {
        var sizes = new double[] { 70, 150, 300, 500 };
        var colour = new double[sizes.Length];
        var footprint = new double[sizes.Length];
        var copies = new double[sizes.Length];
        var tailMpx = new double[sizes.Length];
        var dabCount = new int[sizes.Length];

        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var (fw, fh) = FootprintSpace.BufferSize(Width, Height, FootprintScale);

        for (var s = 0; s < sizes.Length; s++)
        {
            var stroke = Of(sizes[s], 8);
            var brush = stroke.Brush;
            Assert.True(
                BrushEngine.NeedsFootprintCap(brush),
                $"size {sizes[s]} does not take the capping path, so its footprint column is "
                + "measuring nothing and the comparison across sizes is not like for like");

            var dabs = BrushEngine.WalkDabs(stroke);

            // The event under test is the LAST one: the settled prefix is
            // everything before it, the tail is what is still on loan. That is
            // the live path's own split, and taking the first event instead
            // would measure a stroke that has no settled prefix to sit on.
            var tailFrom = Math.Max(0, dabs.Count - (int)Math.Ceiling(dabs.Count / 8.0));
            dabCount[s] = dabs.Count - tailFrom;

            using var scratch = new SKBitmap(info);
            using var scratchCanvas = new SKCanvas(scratch);
            using var coverage = new SKBitmap(
                new SKImageInfo(fw, fh, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using var coverageCanvas = new SKCanvas(coverage);
            coverageCanvas.Clear(SKColors.Black);

            var tail = BrushEngine.RangeBounds(dabs, tailFrom, brush, info);
            Assert.NotNull(tail);
            var rect = tail!.Value;
            tailMpx[s] = rect.Width * (double)rect.Height / 1_000_000.0;

            using var backup = new SKBitmap(new SKImageInfo(
                Math.Max(rect.Width, 64), Math.Max(rect.Height, 64),
                SKColorType.Rgba8888, SKAlphaType.Premul));

            // Warm every path before the clock, so none of them pays for jitting
            // inside a measurement the whole point of which is a ratio.
            BrushEngine.StampDabRange(scratchCanvas, stroke, dabs, tailFrom, dabs.Count);
            BrushEngine.AccumulateFootprint(
                coverageCanvas, stroke, dabs, tailFrom, dabs.Count, FootprintScale);
            CopyOutAndBack(scratch, scratchCanvas, backup, rect);

            double bestColour = double.MaxValue, bestFootprint = double.MaxValue,
                bestCopies = double.MaxValue;
            for (var run = 0; run < 5; run++)
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                BrushEngine.StampDabRange(scratchCanvas, stroke, dabs, tailFrom, dabs.Count);
                scratchCanvas.Flush();
                bestColour = Math.Min(bestColour, Ms(t0));

                var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                BrushEngine.AccumulateFootprint(
                    coverageCanvas, stroke, dabs, tailFrom, dabs.Count, FootprintScale);
                coverageCanvas.Flush();
                bestFootprint = Math.Min(bestFootprint, Ms(t1));

                var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                CopyOutAndBack(scratch, scratchCanvas, backup, rect);
                bestCopies = Math.Min(bestCopies, Ms(t2));
            }

            // The minimum of several runs, for the reason LiveTipDabCostTests
            // records: contention only ever ADDS, so the fastest run is what the
            // machine can do and a median is whatever else it was doing.
            colour[s] = bestColour;
            footprint[s] = bestFootprint;
            copies[s] = bestCopies;
        }

        output.WriteLine(
            $"  one event of {TravelPerEvent} px of travel on {Width}x{Height}, best of 5:");
        output.WriteLine("");
        output.WriteLine("   size   dabs    colour  footprint    copies    tail Mpx     TOTAL");
        for (var s = 0; s < sizes.Length; s++)
        {
            var total = colour[s] + footprint[s] + copies[s];
            output.WriteLine(
                $"  {sizes[s],5:0} {dabCount[s],6} {colour[s],9:0.##} {footprint[s],10:0.##}"
                + $" {copies[s],9:0.##} {tailMpx[s],11:0.###} {total,9:0.##} ms");
        }

        var growth = new (string Name, double Factor)[]
        {
            ("colour", colour[^1] / Math.Max(1e-9, colour[0])),
            ("footprint", footprint[^1] / Math.Max(1e-9, footprint[0])),
            ("copies", copies[^1] / Math.Max(1e-9, copies[0])),
        };

        output.WriteLine("");
        output.WriteLine($"  from size {sizes[0]} to {sizes[^1]} — {sizes[^1] / sizes[0]:0.#}x the brush:");
        foreach (var (name, factor) in growth)
        {
            output.WriteLine($"    {name,-10} grew {factor,7:0.#}x");
        }

        // **The share, not the growth factor — the first version of this line
        // got it wrong and said so confidently.** Ranking by growth named the
        // tail copies, which grew 6.8x and went from 0.01 ms to 0.07: the
        // fastest-growing column and a rounding error either end of it. What
        // decides where a fix goes is what an event actually spends, so the
        // growth is reported and the SHARE is what the verdict reads.
        var big = new (string Name, double Ms)[]
        {
            ("colour", colour[^1]), ("footprint", footprint[^1]), ("copies", copies[^1]),
        };
        var dominant = big.OrderByDescending(c => c.Ms).First();
        var totalAtMax = big.Sum(c => c.Ms);
        output.WriteLine("");
        output.WriteLine(
            $"  >> at size {sizes[^1]} the {dominant.Name} is {100 * dominant.Ms / totalAtMax:0}%"
            + " of the event. THAT is where the fix goes — not whichever column");
        output.WriteLine(
            "     grew fastest, which is the tail copies and is 3% of the event.");

        output.WriteLine("");
        output.WriteLine("  the colour stamp, per dab and per pixel of dab:");
        for (var s = 0; s < sizes.Length; s++)
        {
            var perDab = colour[s] * 1000 / Math.Max(1, dabCount[s]);
            var area = Math.PI * sizes[s] * sizes[s] / 4;
            output.WriteLine(
                $"  {sizes[s],5:0} {perDab,10:0} us a dab   {perDab * 1000 / area,7:0.##} ns a pixel");
        }

        var totalSmall = colour[0] + footprint[0] + copies[0];
        var totalBig = colour[^1] + footprint[^1] + copies[^1];
        output.WriteLine(
            $"  >> one event costs {totalSmall:0.##} ms at size {sizes[0]} and {totalBig:0.##} ms at"
            + $" {sizes[^1]} — {totalBig / totalSmall:0.#}x, against a pen delivering every ~5 ms");

        // **The claim, and it is a shape rather than a budget.** If a big brush
        // did not cost more per event there would be nothing to explain and the
        // owner's report would be about something else entirely.
        Assert.True(
            totalBig > totalSmall,
            $"one event costs {totalBig:0.##} ms at size {sizes[^1]} against {totalSmall:0.##} at "
            + $"{sizes[0]}, so brush size is NOT what makes a big brush jump and the cause is "
            + "somewhere this does not look — the dab walk, the publish cycle, or the tip");

        // The footprint was moved to preview scale on this branch's parent, so
        // it is the one column that should NOT be leading. If it is, that change
        // is not doing in this shape of stroke what the capture said it did.
        Assert.True(
            footprint[^1] < colour[^1],
            $"the footprint costs {footprint[^1]:0.##} ms at size {sizes[^1]} against the colour "
            + $"stamp's {colour[^1]:0.##}, so preview-scaling it has not survived to large brushes "
            + "and THAT is the thing to fix before anything here");
    }

    /// <summary>
    /// The tail rectangle out to a backup and back again — the two copies every
    /// pointer event performs, timed as the pair they always occur as.
    /// </summary>
    private static void CopyOutAndBack(
        SKBitmap scratch, SKCanvas scratchCanvas, SKBitmap backup, SKRectI rect)
    {
        using (var region = new SKBitmap())
        {
            if (scratch.ExtractSubset(region, rect))
            {
                using var px = region.PeekPixels();
                using var view = px is null ? null : SKImage.FromPixels(px);
                if (view is not null)
                {
                    using var into = new SKCanvas(backup);
                    using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                    into.DrawImage(view, 0, 0, src);
                    into.Flush();
                }
            }
        }

        using var restorePx = backup.PeekPixels();
        using var restore = restorePx is null ? null : SKImage.FromPixels(restorePx);
        if (restore is null) return;
        using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
        scratchCanvas.DrawImage(
            restore,
            new SKRect(0, 0, rect.Width, rect.Height),
            new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            replace);
        scratchCanvas.Flush();
    }
}
