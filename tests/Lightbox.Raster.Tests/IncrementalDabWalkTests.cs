using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Stamping a stroke a few dabs at a time must produce the same pixels as stamping it
/// all at once.
/// </summary>
/// <remarks>
/// <para>
/// The engine half of B45, isolated from the view model so a failure says which layer is
/// at fault. It was written as a probe while hunting that bug and kept, because it is the
/// cheapest possible statement of the property and it localises the fault in one run:
/// when the view-model harness went red and this stayed green, the walk was fine and the
/// compositing was not.
/// </para>
/// <para>
/// <b>Sampling density is the parameter that matters, and that was the finding.</b>
/// <see cref="Lightbox.Core.Geometry.GeometryOps.Densify"/> interpolates a span using its
/// neighbours' control points, so the newest span of a growing stroke is provisional — it
/// straightens out once the next point arrives. Below the 2 px chord it never engages and
/// the incremental render was already exact; above it, dabs moved, and because every dab
/// dynamic is seeded from the dab's position, a sub-pixel move re-rolled scatter and threw
/// the dab up to ten pixels elsewhere. A single-density test would have missed the
/// mechanism entirely.
/// </para>
/// </remarks>
public class IncrementalDabWalkTests
{
    private static Stroke Curve(int points, BrushSettings brush)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < points; i++)
        {
            var t = i / (double)(points - 1);
            pts.Add(new StrokePoint(40 + t * 340, 110 + Math.Sin(t * Math.PI * 1.5) * 55, 1));
        }
        return new Stroke { Tool = ToolKind.Brush, Color = "#101010", Brush = brush, Points = pts };
    }

    /// <summary>Scatter and jitter, because they amplify a sub-pixel move into a visible one.</summary>
    private static BrushSettings Scattered() => new()
    {
        Size = 30, Opacity = 1, Flow = 0.25, Hardness = 1, AntiAlias = true,
        Scatter = 0.35, SizeJitter = 0.5,
    };

    /// <summary>
    /// Feed a stroke in one point at a time, the way a drag arrives, holding back the
    /// dabs that are not settled yet.
    /// </summary>
    private static void StampIncrementally(SKCanvas canvas, Stroke full)
    {
        var settled = 0;
        List<BrushEngine.Dab>? previous = null;

        for (var n = 2; n <= full.Points.Count; n++)
        {
            var partial = new Stroke
            {
                Tool = full.Tool,
                Color = full.Color,
                Brush = full.Brush,
                Points = full.Points.Take(n).ToList(),
            };
            var dabs = BrushEngine.WalkDabs(partial);
            var stable = BrushEngine.StableCount(dabs, previous);
            previous = dabs;

            BrushEngine.StampDabRange(canvas, partial, dabs, settled, stable);
            settled = Math.Max(settled, Math.Min(stable, dabs.Count));
        }

        // Pen up: the tail that was still provisional becomes final. The live preview
        // lends this out and takes it back each event; here it is simply the remainder.
        var final = BrushEngine.WalkDabs(full);
        BrushEngine.StampDabRange(canvas, full, final, settled, final.Count);
        canvas.Flush();
    }

    [Theory]
    [InlineData(40)]    // ~8.7 px spans: densification well engaged
    [InlineData(200)]   // ~1.7 px spans: engaged only on the steepest part of the curve
    [InlineData(400)]   // ~0.9 px spans: never engaged
    public void GrowingAStrokeOnePointAtATimeGivesTheSamePixels(int points)
    {
        var info = new SKImageInfo(420, 220, SKColorType.Rgba8888, SKAlphaType.Premul);
        var full = Curve(points, Scattered());

        using var oneShot = new SKBitmap(info);
        using var incremental = new SKBitmap(info);

        using (var canvas = new SKCanvas(oneShot))
        {
            var dabs = BrushEngine.WalkDabs(full);
            BrushEngine.StampDabRange(canvas, full, dabs, 0, dabs.Count);
            canvas.Flush();
        }
        using (var canvas = new SKCanvas(incremental))
        {
            StampIncrementally(canvas, full);
        }

        var worst = 0;
        var differing = 0;
        for (var y = 0; y < info.Height; y++)
        {
            for (var x = 0; x < info.Width; x++)
            {
                var d = Math.Abs(oneShot.GetPixel(x, y).Alpha - incremental.GetPixel(x, y).Alpha);
                if (d > 0) differing++;
                if (d > worst) worst = d;
            }
        }

        Assert.Equal(0, worst);
        Assert.Equal(0, differing);
    }

    [Fact]
    public void TheStableCountGrowsAndNeverExceedsTheDabsThereAre()
    {
        // The contract the rollback depends on: settled dabs stay settled, so the scratch
        // only ever has its newest dabs taken back. If this went backwards, a dab already
        // treated as permanent would need un-stamping and nothing would un-stamp it.
        var full = Curve(40, Scattered());
        var highest = 0;
        List<BrushEngine.Dab>? prior = null;

        for (var n = 2; n <= full.Points.Count; n++)
        {
            var partial = new Stroke
            {
                Tool = full.Tool, Color = full.Color, Brush = full.Brush,
                Points = full.Points.Take(n).ToList(),
            };
            var dabs = BrushEngine.WalkDabs(partial);
            var stable = BrushEngine.StableCount(dabs, prior);
            prior = dabs;

            Assert.True(stable >= highest, $"stable went backwards at {n} points: {highest} → {stable}");
            Assert.True(stable <= dabs.Count, $"stable {stable} exceeds the {dabs.Count} dabs at {n} points");
            highest = stable;
        }
    }

    [Fact]
    public void ASinglePointIsEntirelySettledSoATapInksAtOnce()
    {
        // Nothing that arrives later can move the dab sitting on the first recorded point,
        // and a tap that waited for a second point would look like a dead brush.
        var tap = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#101010",
            Brush = Scattered(),
            Points = [new StrokePoint(100, 100, 1)],
        };

        var dabs = BrushEngine.WalkDabs(tap);
        Assert.NotEmpty(dabs);
        Assert.True(BrushEngine.StableCount(dabs, null) >= 1);
    }

    [Fact]
    public void AWalkedDabCarriesTheStrokeGlobalsThatMadeItPositionDependent()
    {
        // The reason a dab is recorded rather than recomputed: load and heading depend on
        // how far along the stroke the dab is, so they cannot be derived from its position.
        var brush = Scattered();
        brush.AngleFollowsDirection = true;
        brush.Medium.PaintLoad = 0.2;
        var stroke = Curve(40, brush);

        var dabs = BrushEngine.WalkDabs(stroke);

        Assert.True(dabs.Count > 4, $"the fixture needs more than four dabs, it has {dabs.Count}");
        Assert.True(dabs[^1].Load < dabs[0].Load * 0.75,
            $"load did not deplete along the stroke: {dabs[0].Load:F3} → {dabs[^1].Load:F3}");
        Assert.False(double.IsNaN(dabs[^1].Heading), "a direction-following tip got no heading");
    }

    [Fact]
    public void TheBoundsOfADabRangeCoverEveryDabInIt()
    {
        // What the live preview backs up before lending out the tail. Too small and the
        // rollback leaves ink behind; measured against the dabs themselves because scatter
        // can throw one well off the line the points describe.
        var brush = Scattered();
        var stroke = Curve(40, brush);
        var info = new SKImageInfo(420, 220, SKColorType.Rgba8888, SKAlphaType.Premul);

        var dabs = BrushEngine.WalkDabs(stroke);
        var from = dabs.Count - 4;
        var bounds = BrushEngine.RangeBounds(dabs, from, brush, info);
        Assert.NotNull(bounds);

        // Every dab centre in the range is inside, with room for the dab's own radius.
        for (var i = from; i < dabs.Count; i++)
        {
            var pos = dabs[i].Pos;
            Assert.True(
                pos.X >= bounds!.Value.Left && pos.X <= bounds.Value.Right
                && pos.Y >= bounds.Value.Top && pos.Y <= bounds.Value.Bottom,
                $"dab at ({pos.X:F1},{pos.Y:F1}) fell outside {bounds}");
        }
    }

    [Fact]
    public void AnEmptyRangeHasNoBounds()
    {
        var brush = Scattered();
        var stroke = Curve(10, brush);
        var info = new SKImageInfo(420, 220, SKColorType.Rgba8888, SKAlphaType.Premul);

        Assert.Null(BrushEngine.RangeBounds(BrushEngine.WalkDabs(stroke), 10_000, brush, info));
    }
}
