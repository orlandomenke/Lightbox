using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B349. A soft brush swept back and forth showed ridges along the sweep and a
/// hard rim, because the footprint ceiling was a running <em>maximum</em> of
/// dab shapes and a maximum of overlapping bumps is bumpy. The ceiling is now
/// the greater of that maximum and the dab's falloff applied to each pixel's
/// distance inside the edge of the stroke's reach — flat where the maximum
/// dipped, identical where it did not. <c>docs/DESIGN-swept-ceiling.md</c>
/// carries the definition and the proof; this holds the measurements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both ceilings are the engine's.</b> Reach zero asks
/// <c>CapToFootprintBand</c> for the shape maximum alone, which is what every
/// caller got before B349; the brush's <c>CeilingReachPx</c> asks for the
/// swept ceiling. The two are compared on the same stamped mark, so the only
/// thing that differs is the definition.
/// </para>
/// <para>
/// <b>Ripple is B349's own metric</b>: the detrended peak-to-trough of a cut
/// through the interior of the sweep, out of 255, with the cut kept clear of
/// the outermost passes' own falloff so it reads the ceiling and not the edge.
/// </para>
/// </remarks>
public class SweptCeilingTests(ITestOutputHelper output)
{
    private const int W = 900, H = 460;

    private static BrushSettings SoftRound() => new()
    {
        Size = 70, Hardness = 0.35, Opacity = 1, Flow = 1, Spacing = 0.15, AntiAlias = true,
    };

    private static BrushSettings Airbrush() => new()
    {
        Size = 70, Hardness = 0.05, Opacity = 1, Flow = 1, Spacing = 0.08, AntiAlias = true,
    };

    private static Stroke Sweep(BrushSettings brush, double pitch, int passes = 8)
    {
        var pts = new List<StrokePoint>();
        for (var k = 0; k < passes; k++)
        {
            var y = 150 + (k * pitch);
            var forward = k % 2 == 0;
            for (double t = 0; t <= 600; t += 4.2)
            {
                var x = forward ? 150 + t : 750 - t;
                pts.Add(new StrokePoint(x, y, 1));
            }
        }
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    private static Stroke Straight(BrushSettings brush)
    {
        var pts = new List<StrokePoint>();
        for (double x = 150; x <= 750; x += 4.2) pts.Add(new StrokePoint(x, H / 2.0, 1));
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    private static Stroke Lone(BrushSettings brush) => new()
    {
        Tool = ToolKind.Brush, Color = "#000000", Brush = brush,
        Points = [new StrokePoint(W / 2.0, H / 2.0, 1)],
    };

    private static SKBitmap Mark(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    private static SKBitmap Footprint(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        BrushEngine.AccumulateFootprint(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    /// <summary>The mark capped by the engine, at the given reach, and how long the cap took.</summary>
    private static (SKBitmap Mark, double Ms) Capped(
        Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs, SKBitmap footprint, int reach)
    {
        // Timed warm and as a minimum of three, on fresh marks: the first call
        // pays the JIT and a cold cache, and contention only ever adds.
        var best = double.MaxValue;
        SKBitmap? keep = null;
        for (var i = 0; i < 3; i++)
        {
            var mark = Mark(stroke, dabs);
            var sw = Stopwatch.StartNew();
            BrushEngine.CapToFootprintBand(mark, footprint, new SKRectI(0, 0, W, H), FootprintSpace.Document, reach);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            if (keep is null) keep = mark; else mark.Dispose();
        }
        return (keep!, best);
    }

    private static double Ripple(SKBitmap mark, int x, int y0, int y1)
    {
        var n = y1 - y0 + 1;
        var ys = new double[n];
        for (var i = 0; i < n; i++) ys[i] = mark.GetPixel(x, y0 + i).Alpha;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++) { sx += i; sy += ys[i]; sxx += i * (double)i; sxy += i * ys[i]; }
        var slope = ((n * sxy) - (sx * sy)) / ((n * sxx) - (sx * sx));
        var intercept = (sy - (slope * sx)) / n;
        double lo = double.MaxValue, hi = double.MinValue;
        for (var i = 0; i < n; i++)
        {
            var r = ys[i] - (intercept + (slope * i));
            lo = Math.Min(lo, r); hi = Math.Max(hi, r);
        }
        return hi - lo;
    }

    public static TheoryData<string> Brushes() => new() { "Soft round", "Airbrush" };

    private static BrushSettings Named(string name) => name == "Airbrush" ? Airbrush() : SoftRound();

    [Theory]
    [MemberData(nameof(Brushes))]
    public void TheSweptInteriorIsFlatWhereTheShapeMaximumRidged(string name)
    {
        var brush = Named(name);
        Assert.True(BrushEngine.NeedsFootprintCap(brush), "this brush is not capped, so the test measures nothing");
        var outer = BrushEngine.RadiusAt(brush, 1);
        var reach = BrushEngine.CeilingReachPx(brush, 1.0);

        // The pitch that ridged worst under the shape maximum, found rather
        // than assumed: a soft dab's flat core covers a tight pitch, and the
        // ridge only appears once the passes are far enough apart for the
        // maximum to dip between them.
        double worstPitch = 0, worstRipple = -1;
        foreach (var pitch in new[] { 18.4, 24, 28, 32, 36, 40 })
        {
            var s = Sweep(brush, pitch);
            var d = BrushEngine.WalkDabs(s);
            using var fp = Footprint(s, d);
            var (m, _) = Capped(s, d, fp, reach: 0);
            using (m)
            {
                int y0 = (int)(150 + outer) + 2, y1 = (int)(150 + (7 * pitch) - outer) - 2;
                var r = y1 - y0 > 8 ? Ripple(m, 450, y0, y1) : -1;
                output.WriteLine($"  {name}, pitch {pitch:0.0}: shape-maximum ripple {r:0.0}/255");
                if (r > worstRipple) { worstRipple = r; worstPitch = pitch; }
            }
        }

        var sweep = Sweep(brush, worstPitch);
        var dabs = BrushEngine.WalkDabs(sweep);
        using var footprint = Footprint(sweep, dabs);
        var (before, msBefore) = Capped(sweep, dabs, footprint, reach: 0);
        var (after, msAfter) = Capped(sweep, dabs, footprint, reach);
        using var uncapped = Mark(sweep, dabs);
        using (before)
        using (after)
        {
            int y0 = (int)(150 + outer) + 2, y1 = (int)(150 + (7 * worstPitch) - outer) - 2;
            var rBefore = Ripple(before, 450, y0, y1);
            var rAfter = Ripple(after, 450, y0, y1);
            var rNone = Ripple(uncapped, 450, y0, y1);
            output.WriteLine(
                $"{name} size 70, pitch {worstPitch:0.0}: ripple shape-maximum {rBefore:0.0}/255, swept {rAfter:0.0}/255, "
                + $"uncapped {rNone:0.0}/255 — cap {msBefore:0.0} ms without the distance term, {msAfter:0.0} ms with");

            // Never lower, pixel by pixel: the one thing B349 showed no repair of
            // the old buffer could promise, and the definition's whole argument.
            var below = 0; var worstBelow = 0;
            for (var y = 0; y < H; y++)
            {
                for (var x = 0; x < W; x++)
                {
                    int b = before.GetPixel(x, y).Alpha, a = after.GetPixel(x, y).Alpha;
                    if (a < b) { below++; worstBelow = Math.Max(worstBelow, b - a); }
                }
            }
            output.WriteLine($"  pixels darker under the swept ceiling than under the shape maximum: {below} (worst by {worstBelow})");
            Assert.True(below == 0, $"the swept ceiling lowered {below} pixels, worst by {worstBelow} — that is the clipping B349 forbids");

            Assert.True(rBefore >= 5, $"the shape maximum did not reproduce the ridge (ripple {rBefore:0.0}) — the probe is not looking at the defect");
            // The floor is the mark itself: where the ceiling does not bind, the
            // capped mark is the uncapped one, ripple included. The swept
            // ceiling may not ADD to that; the shape maximum added tens of levels.
            Assert.True(rAfter <= rNone + 0.5, $"the swept ceiling ripples at {rAfter:0.0}/255 over an uncapped {rNone:0.0}/255");
        }
    }

    /// <summary>
    /// Q157's guard, restated against the engine: a lone dab and the
    /// cross-profile of a straight stroke through a dab's centre are exactly
    /// what the shape maximum gave — the swept ceiling coincides with it there
    /// by construction, and the red channel floors it besides.
    /// </summary>
    [Theory]
    [MemberData(nameof(Brushes))]
    public void ALoneDabAndAStraightStrokesProfileAreUnchanged(string name)
    {
        var brush = Named(name);
        var reach = BrushEngine.CeilingReachPx(brush, 1.0);

        {
            var lone = Lone(brush);
            var dabs = BrushEngine.WalkDabs(lone);
            using var fp = Footprint(lone, dabs);
            var (before, _) = Capped(lone, dabs, fp, 0);
            var (after, _) = Capped(lone, dabs, fp, reach);
            using (before)
            using (after)
            {
                var worst = 0; var at = (0, 0);
                for (var y = 0; y < H; y++)
                {
                    for (var x = 0; x < W; x++)
                    {
                        var d = Math.Abs(before.GetPixel(x, y).Alpha - after.GetPixel(x, y).Alpha);
                        if (d > worst) { worst = d; at = (x, y); }
                    }
                }
                output.WriteLine($"{name}, lone dab: worst difference {worst}/255 at {at}");
                Assert.True(worst <= 2, $"lone dab: the swept ceiling moved a pixel by {worst} at {at}");
            }
        }

        {
            var straight = Straight(brush);
            var dabs = BrushEngine.WalkDabs(straight);
            using var fp = Footprint(straight, dabs);
            var (before, _) = Capped(straight, dabs, fp, 0);
            var (after, _) = Capped(straight, dabs, fp, reach);
            using (before)
            using (after)
            {
                var mid = dabs[dabs.Count / 2];
                var column = (int)Math.Round(mid.Pos.X);
                var worst = 0; var at = 0;
                for (var y = 0; y < H; y++)
                {
                    var d = Math.Abs(before.GetPixel(column, y).Alpha - after.GetPixel(column, y).Alpha);
                    if (d > worst) { worst = d; at = y; }
                }
                output.WriteLine($"{name}, straight stroke, cross-profile through a dab centre: worst difference {worst}/255 at y={at}");
                Assert.True(worst <= 2, $"straight stroke: the cross-profile moved by {worst} at y={at}");

                // ALONG the stroke the two differ, and the difference is the
                // fix: the shape maximum reads F(0) on a dab centre and F(half a
                // pitch) between two — the dab-pitch ripple B349 found finer
                // sampling could not remove — while the stroke's edge is the
                // same distance away all along.
                var cy = (int)Math.Round(mid.Pos.Y);
                int x0 = (int)Math.Ceiling(dabs[0].Pos.X) + 1, x1 = (int)Math.Floor(dabs[^1].Pos.X) - 1;
                double Along(SKBitmap m)
                {
                    int lo = 255, hi = 0;
                    for (var x = x0; x <= x1; x++) { var a = m.GetPixel(x, cy).Alpha; lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
                    return hi - lo;
                }
                double rBefore = Along(before), rAfter = Along(after);
                output.WriteLine($"{name}, straight stroke, along the centreline: ripple shape-maximum {rBefore:0}/255, swept {rAfter:0}/255");
                Assert.True(rAfter <= rBefore, "the swept ceiling ripples more along the stroke than the shape maximum");
            }
        }
    }

    /// <summary>
    /// What the distance term adds to one live event. The live path caps a band
    /// around the newest dabs (B313) against a footprint kept at the compose
    /// scale (B189), so the window the term works over is the band plus one
    /// reach, at that scale: about 90 px square fit-to-window at 4K, 240 px
    /// square zoomed to 100%. The budget is on the <em>added</em> cost, because
    /// the shape-maximum cap the band already pays is a fraction of a
    /// millisecond and a ratio against it would say nothing; the promise this
    /// guards is that nothing here makes the pen wait.
    /// </summary>
    [Theory]
    [InlineData(0.375, 0.5)]
    [InlineData(1.0, 2.0)]
    [Trait("Category", "Performance")]
    public void TheDistanceTermCostsOneLiveEventLessThanItsBudget(double scale, double budgetMs)
    {
        var brush = SoftRound();
        var reach = BrushEngine.CeilingReachPx(brush, scale);
        var sweep = Sweep(brush, 40);
        var dabs = BrushEngine.WalkDabs(sweep);
        var (fw, fh) = FootprintSpace.BufferSize(W, H, scale);
        using var fp = new SKBitmap(new SKImageInfo(fw, fh, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(fp))
        {
            canvas.Clear(SKColors.Black);
            BrushEngine.AccumulateFootprint(canvas, sweep, dabs, 0, dabs.Count, scale);
            canvas.Flush();
        }

        // The band a size-70 brush's event covers in MainViewModel: the dabs
        // laid since the last event plus the pass halo, which the owner's
        // captures put at about 173 px square.
        var band = new SKRectI(380, 200, 553, 373);
        var space = new FootprintSpace(scale, 0, 0);
        double plain = double.MaxValue, swept = double.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            using var a = Mark(sweep, dabs);
            var sw = Stopwatch.StartNew();
            BrushEngine.CapToFootprintBand(a, fp, band, space, 0);
            sw.Stop();
            plain = Math.Min(plain, sw.Elapsed.TotalMilliseconds);

            using var b = Mark(sweep, dabs);
            sw.Restart();
            BrushEngine.CapToFootprintBand(b, fp, band, space, reach);
            sw.Stop();
            swept = Math.Min(swept, sw.Elapsed.TotalMilliseconds);
        }

        var added = Math.Max(0, swept - plain);
        output.WriteLine(
            $"scale {scale:0.000}, band {band.Width}x{band.Height}, reach {reach} buffer px: "
            + $"cap {plain:0.000} ms with the shape maximum, {swept:0.000} ms with the swept ceiling — "
            + $"the term adds {added:0.000} ms against a budget of {budgetMs:0.0}");
        Assert.True(
            added <= budgetMs,
            $"the distance term adds {added:0.000} ms to one live event at scale {scale}, over its {budgetMs} ms budget");
    }

    /// <summary>
    /// The band-local live path reads the same ceiling as the whole mark does:
    /// a band's swept ceiling is exact once the support is known one reach
    /// beyond it, and the engine reads that far from the buffer it is handed.
    /// </summary>
    [Fact]
    public void ABandReadsTheSameSweptCeilingAsTheWholeMark()
    {
        var brush = SoftRound();
        var reach = BrushEngine.CeilingReachPx(brush, 1.0);
        var sweep = Sweep(brush, 40);
        var dabs = BrushEngine.WalkDabs(sweep);
        using var fp = Footprint(sweep, dabs);

        var (whole, _) = Capped(sweep, dabs, fp, reach);
        using var banded = Mark(sweep, dabs);
        var band = new SKRectI(380, 200, 520, 330);
        BrushEngine.CapToFootprintBand(banded, fp, band, FootprintSpace.Document, reach);

        using (whole)
        {
            var worst = 0;
            for (var y = band.Top; y < band.Bottom; y++)
            {
                for (var x = band.Left; x < band.Right; x++)
                {
                    worst = Math.Max(worst, Math.Abs(whole.GetPixel(x, y).Alpha - banded.GetPixel(x, y).Alpha));
                }
            }
            output.WriteLine($"band against whole mark, over the band: worst difference {worst}/255");
            Assert.Equal(0, worst);
        }
    }
}
