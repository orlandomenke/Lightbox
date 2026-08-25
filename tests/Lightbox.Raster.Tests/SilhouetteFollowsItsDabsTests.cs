using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A hard round mark lands where its own dabs are (B311).
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes is the reason the bug survived.</b>
/// <c>LiveMatchesCommittedTests</c> compares the live preview with the commit,
/// and both take the silhouette route — so they agreed with each other while
/// both standing away from the record. The determinism fingerprints then pinned
/// the displaced render as the reference. Nothing in the suite asked the only
/// question that could have caught it: does the mark sit on its dabs?
/// </para>
/// <para>
/// <b>Measured against the per-dab path rather than against a remembered
/// number.</b> Both routes are handed the same dab list, so the comparison is
/// two renderers of one geometry and cannot drift as brushes change. The per-dab
/// path is the reference because it stamps each dab at its own centre, which is
/// the record's own answer.
/// </para>
/// <para>
/// <b>The curve is gentle on purpose, because that is the failing case and it
/// is counter-intuitive.</b> A stroke that turns hard breaks the runs the old
/// simplifier built and stays close to its dabs; a stroke that barely curves
/// lets a run grow long, and the chord it collapses to cuts the corner. The arc
/// here has a radius of curvature near 3,800 px — an ordinary sweep across a 4K
/// canvas — and the old code put the mark up to 21 px from where its dabs were.
/// </para>
/// </remarks>
public class SilhouetteFollowsItsDabsTests
{
    private const int Width = 3200;
    private const int Height = 2400;

    /// <summary>A gentle arc: 15 px a step, turning 0.004 rad a step.</summary>
    private static Stroke Arc(BrushSettings brush, int points = 400)
    {
        var pts = new List<StrokePoint>(points);
        double x = 200, y = 300, heading = 0;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.004;
            x += 15.0 * Math.Cos(heading);
            y += 15.0 * Math.Sin(heading);
        }

        return new Stroke { Tool = ToolKind.Brush, Color = "#101010", Brush = brush, Points = pts };
    }

    private static BrushSettings Hard() => new()
    {
        Size = 5,
        Hardness = 1,
        Opacity = 1,
        Flow = 1,
        Spacing = 0.1,
        AntiAlias = true,
    };

    /// <summary>The same settings, nudged just under the silhouette predicate.</summary>
    private static BrushSettings NearlyHard()
    {
        var b = Hard();
        b.Hardness = 0.99;
        return b;
    }

    private static SKBitmap Render(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs)
    {
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    /// <summary>The first row down a column that carries ink, or -1.</summary>
    private static int FirstInk(SKBitmap bitmap, int x)
    {
        for (var y = 0; y < Height; y++)
        {
            if (bitmap.GetPixel(x, y).Alpha > 8) return y;
        }

        return -1;
    }

    [Fact]
    public void AGentleArcLandsOnItsOwnDabs()
    {
        var hard = Hard();
        Assert.True(BrushEngine.DrawsAsOneSilhouette(hard), "the hard brush must take the silhouette route");
        var soft = NearlyHard();
        Assert.False(BrushEngine.DrawsAsOneSilhouette(soft), "the control must take the per-dab route");

        var stroke = Arc(hard);
        var dabs = BrushEngine.WalkDabs(stroke);
        var control = new Stroke
        {
            Tool = stroke.Tool, Color = stroke.Color, Brush = soft, Points = stroke.Points,
        };

        using var silhouette = Render(stroke, dabs);
        using var perDab = Render(control, dabs);

        var worst = 0;
        var worstAt = 0;
        foreach (var x in new[] { 400, 700, 1000, 1300, 1600, 1900, 2200 })
        {
            int a = FirstInk(silhouette, x), b = FirstInk(perDab, x);
            Assert.True(a >= 0, $"the silhouette put no ink at x={x}");
            Assert.True(b >= 0, $"the per-dab render put no ink at x={x}");
            var error = Math.Abs(a - b);
            if (error > worst)
            {
                worst = error;
                worstAt = x;
            }
        }

        // Two pixels of slack, not zero: the two routes rasterise the same
        // geometry by different means, so a boundary row may round either way.
        // The defect this guards was an order of magnitude past that — 21 px —
        // and the assertion is deliberately far below it so a partial
        // regression cannot slip through as "close enough".
        Assert.True(
            worst <= 2,
            $"the silhouette stood {worst} px off its own dabs at x={worstAt}");
    }

    /// <summary>
    /// The sensitivity half: the measurement can see a displacement at all.
    /// </summary>
    /// <remarks>
    /// Without this, the assertion above would pass just as happily on a build
    /// where both routes drew nothing, or where <c>FirstInk</c> always answered
    /// the same row — the shape of mistake <c>.claude/skills/brush-measurement</c>
    /// exists for. Shifting the control stroke by a known amount must move the
    /// number by that amount.
    /// </remarks>
    [Fact]
    public void TheComparisonCanSeeADisplacement()
    {
        var hard = Hard();
        var stroke = Arc(hard);
        var dabs = BrushEngine.WalkDabs(stroke);

        var moved = new Stroke
        {
            Tool = stroke.Tool,
            Color = stroke.Color,
            Brush = hard,
            Points = stroke.Points.Select(p => new StrokePoint(p.X, p.Y + 9, p.Pressure)).ToList(),
        };
        var movedDabs = BrushEngine.WalkDabs(moved);

        using var here = Render(stroke, dabs);
        using var there = Render(moved, movedDabs);

        var a = FirstInk(here, 1000);
        var b = FirstInk(there, 1000);
        Assert.InRange(b - a, 8, 10);
    }
}
