using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// <b>B113.</b> Four unlabelled strokes making a box, moved between two keys.
/// </summary>
/// <remarks>
/// <para>
/// Reported from a real drawing: two boxes, two frames, four lines each — and
/// the top edge animated <em>downwards</em> through the shape instead of
/// travelling with the box. The cause is not the interpolator; it is which
/// stroke got paired with which.
/// </para>
/// <para>
/// A box is the smallest drawing that exposes it, because its top and bottom
/// edges are the same length and differ only in position. That makes the cost
/// function symmetric between them, and a greedy matcher resolves the symmetry
/// by whichever single pair happens to be cheapest — which, once the box has
/// moved by more than half its height, is the wrong one.
/// </para>
/// </remarks>
public class BoxEdgeMatchTests(ITestOutputHelper output)
{
    private static Stroke Line(double x1, double y1, double x2, double y2) => new()
    {
        Points = [new(x1, y1, 0.6), new(x2, y2, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    /// <summary>A box as four unlabelled strokes: top, right, bottom, left.</summary>
    private static List<Stroke> Box(double top) =>
    [
        Line(20, top, 80, top),
        Line(80, top, 80, top + 60),
        Line(20, top + 60, 80, top + 60),
        Line(20, top, 20, top + 60),
    ];

    private static double MeanY(Stroke s) => s.Points.Average(p => p.Y);

    [Fact]
    public void EachEdgePairsWithItsOwnEdge()
    {
        // Moved down 40 — two thirds of its own height, which is where the
        // greedy matcher used to cross the edges over.
        var a = Box(20);
        var b = Box(60);

        var pairs = StrokeMatcher.Match(a, b);
        foreach (var p in pairs)
            output.WriteLine($"A y={MeanY(p.A!):0} -> B y={MeanY(p.B!):0}");

        // Every edge moves down by exactly the box's own travel. Asserted for
        // all four rather than for the top alone: the reported symptom was the
        // top, but the defect is the pairing, and a test that only watched the
        // top would pass on a build that crossed the sides instead.
        Assert.All(pairs, p =>
        {
            Assert.NotNull(p.A);
            Assert.NotNull(p.B);
            Assert.Equal(40, MeanY(p.B!) - MeanY(p.A!), precision: 6);
        });
    }

    [Fact]
    public void TheMidwayFrameIsStillABox()
    {
        // The visible consequence, and the worse half of the bug: with the
        // edges crossed the box does not merely animate oddly, it collapses —
        // at t=0.5 all four strokes landed on the same line and the shape had
        // no height at all.
        var mid = Inbetweener.Inbetween(Box(20), Box(60), 0.5, Easing.Linear);
        var ys = mid.Select(MeanY).Order().ToList();
        output.WriteLine("mid-frame edge Ys: " + string.Join(", ", ys.Select(y => $"{y:0.#}")));

        // Halfway between y=20..80 and y=60..120 is a box spanning 40..100.
        Assert.Equal(40, ys[0], precision: 6);     // top edge
        Assert.Equal(100, ys[^1], precision: 6);   // bottom edge
        Assert.Equal(60, ys[^1] - ys[0], precision: 6);
    }

    [Fact]
    public void TheMatchIsMinimumTotalCostRatherThanCheapestFirst()
    {
        // The property, stated directly, so the reason survives a rewrite of
        // the cost function. Greedy scores 20 + 100; optimal scores 40 + 40.
        var a = new[] { Line(20, 20, 80, 20), Line(20, 80, 80, 80) };
        var b = new[] { Line(20, 60, 80, 60), Line(20, 120, 80, 120) };

        var pairs = StrokeMatcher.Match(a, b);
        var total = pairs.Sum(p => GeometryOps.Dist(
            GeometryOps.Centroid(p.A!.Points), GeometryOps.Centroid(p.B!.Points)));

        output.WriteLine($"total pairing distance {total:0} (greedy would be 120)");
        Assert.Equal(80, total, precision: 6);
    }
}
