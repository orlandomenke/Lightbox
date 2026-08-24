using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A hard brush's outline is kept between pointer events instead of derived from
/// every dab each time (B292). These say the kept one is the same shape.
/// </summary>
/// <remarks>
/// <b>Compared against the uncached build rather than against recorded pixels.</b>
/// The claim is not "the outline looks like this", it is "reusing a prefix and
/// deriving the whole thing agree" — and a fingerprint would go stale the next
/// time the outline legitimately changes, while this would not.
/// </remarks>
public class SilhouetteCacheTests(ITestOutputHelper output)
{
    private const int W = 420, H = 260;

    private static BrushSettings Ink() => new()
    {
        Size = 5, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1,
        PressureSizeGamma = 1.4, AntiAlias = true,
    };

    /// <summary>A curve with turns and a pressure ramp, so the walk keeps breaking runs.</summary>
    private static Stroke Curve(int points)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i <= points; i++)
        {
            var t = i / (double)points;
            pts.Add(new StrokePoint(
                30 + t * 360,
                130 + Math.Sin(t * Math.PI * 2.5) * 90,
                0.35 + 0.65 * Math.Abs(Math.Sin(t * Math.PI))));
        }
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = Ink(), Points = pts };
    }

    private static SKBitmap Render(Stroke stroke, BrushEngine.SilhouetteCache? cache, int settled)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var dabs = BrushEngine.WalkDabs(stroke);
        BrushEngine.StampDabRange(
            canvas, stroke, dabs, 0, dabs.Count, cache: cache, settled: settled);
        canvas.Flush();
        return bitmap;
    }

    private static int WorstDifference(SKBitmap a, SKBitmap b)
    {
        var worst = 0;
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
        {
            worst = Math.Max(worst, Math.Abs(a.GetPixel(x, y).Alpha - b.GetPixel(x, y).Alpha));
        }
        return worst;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(200)]
    [InlineData(900)]
    public void AnyAmountOfCachedPrefixGivesTheSameOutline(int settled)
    {
        var stroke = Curve(120);
        using var uncached = Render(stroke, cache: null, settled: 0);
        using var cache = new BrushEngine.SilhouetteCache();
        using var cached = Render(stroke, cache, settled);

        var worst = WorstDifference(uncached, cached);
        output.WriteLine($"settled={settled}: worst alpha difference {worst}");
        Assert.Equal(0, worst);
    }

    [Fact]
    public void GrowingAStrokeThroughTheCacheMatchesOneShot()
    {
        // What the live preview actually does: the same cache, asked for a longer
        // stroke each event, with the settled cut creeping forward behind it.
        var full = Curve(120);
        using var oneShot = Render(full, cache: null, settled: 0);

        using var cache = new BrushEngine.SilhouetteCache();
        SKBitmap? last = null;
        List<BrushEngine.Dab>? previous = null;
        var settled = 0;
        // Every fourth point, and then the last one — the stroke compared against
        // is the whole thing, so the loop has to actually reach it.
        var steps = Enumerable.Range(1, full.Points.Count / 4).Select(i => i * 4).ToList();
        if (steps[^1] != full.Points.Count) steps.Add(full.Points.Count);
        foreach (var n in steps)
        {
            var partial = new Stroke
            {
                Tool = full.Tool, Color = full.Color, Brush = full.Brush,
                Points = full.Points.Take(n).ToList(),
            };
            var dabs = BrushEngine.WalkDabs(partial);
            // **StableCount, not a guessed margin, and the difference is not
            // academic.** The first cut of this test assumed the last 24 dabs
            // were the only ones that could move and got a 191/255 disagreement:
            // adding points to the end of a stroke re-densifies further back
            // than that. Measuring which dabs actually held still is the whole
            // reason StableCount exists, and the live path uses it here.
            settled = Math.Max(settled, Math.Min(BrushEngine.StableCount(dabs, previous), dabs.Count));
            previous = dabs;
            last?.Dispose();
            last = Render(partial, cache, settled);
        }

        Assert.NotNull(last);
        var worst = WorstDifference(oneShot, last!);
        output.WriteLine($"worst alpha difference after growing through the cache: {worst}");
        last!.Dispose();
        Assert.Equal(0, worst);
    }

    [Fact]
    public void ACacheAskedForLessThanItHoldsStartsAgain()
    {
        // A new stroke reuses the session's cache, and a shorter one must not be
        // drawn with the previous stroke's outline still in it.
        var longer = Curve(120);
        using var cache = new BrushEngine.SilhouetteCache();
        using var _ = Render(longer, cache, settled: 600);

        var shorter = Curve(20);
        using var reused = Render(shorter, cache, settled: 30);
        using var fresh = Render(shorter, cache: null, settled: 0);

        var worst = WorstDifference(fresh, reused);
        output.WriteLine($"worst alpha difference after reusing the cache: {worst}");
        Assert.Equal(0, worst);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheCacheIsCheaperThanRebuilding()
    {
        // Loose on purpose: this catches the cache being wired up backwards or
        // silently never hit, not drift. The measured win on a 2000 px arc at
        // size 5 is 3.45 -> 2.50 ms per pointer event.
        var stroke = Curve(400);
        var dabs = BrushEngine.WalkDabs(stroke);
        var settled = Math.Max(0, dabs.Count - 24);
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

        double Time(bool cached)
        {
            using var cache = cached ? new BrushEngine.SilhouetteCache() : null;
            using var surface = SKSurface.Create(info);
            // Warm, so the cached run is not paying for its first build.
            BrushEngine.StampDabRange(
                surface!.Canvas, stroke, dabs, 0, dabs.Count, cache: cache, settled: settled);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 40; i++)
            {
                BrushEngine.StampDabRange(
                    surface.Canvas, stroke, dabs, 0, dabs.Count, cache: cache, settled: settled);
            }
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / 40;
        }

        var rebuilt = Time(cached: false);
        var reused = Time(cached: true);
        output.WriteLine($"{dabs.Count} dabs: rebuilding {rebuilt:0.000} ms, reusing {reused:0.000} ms");
        // **A margin, not just "less than".** The equivalence tests above pass
        // whether or not the cache is ever consulted, so this is the only one
        // that can notice it being wired up and then ignored — and against an
        // ignored cache "cheaper" is a coin flip. Measured at roughly 3x on this
        // stroke, so a fifth is loose enough not to drift and strict enough to
        // fail on a no-op.
        Assert.True(
            reused < rebuilt * 0.8,
            $"reusing the outline saved nothing ({reused:0.000} ms against {rebuilt:0.000} ms rebuilding)");
    }
}
