using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The spatial index that makes a tiled render possible rather than quadratic.
/// </summary>
/// <remarks>
/// Two claims are worth more than the speed one and are tested first:
/// <b>a query returns strokes in record order</b>, because compositing order is
/// the document (invariant 1), and <b>a query returns every stroke that reaches
/// the region</b>, because a missed stroke is a missing mark rather than a slow
/// one.
/// </remarks>
public class StrokeIndexTests(ITestOutputHelper output)
{
    private static readonly SKImageInfo Info = new(2000, 2000, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static Stroke MarkAt(double x, double y, double size = 20) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 10, y + 10, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    // ---- the invariant -------------------------------------------------------

    /// <summary>
    /// Compositing order is the document. A query that returned the right strokes
    /// in the wrong order would paint a later mark underneath an earlier one, and
    /// only where two overlap — which is exactly the defect a suite survives.
    /// </summary>
    [Fact]
    public void AQueryReturnsStrokesInRecordOrder()
    {
        var grid = new TileGrid(256);
        var index = new StrokeIndex(grid);

        // Added out of spatial order on purpose: record position 0 is far right,
        // position 3 is far left. A cell-walk that yielded in cell order rather
        // than record order would return these reversed.
        index.Add(0, SKRectI.Create(1500, 10, 40, 40));
        index.Add(1, SKRectI.Create(1000, 10, 40, 40));
        index.Add(2, SKRectI.Create(500, 10, 40, 40));
        index.Add(3, SKRectI.Create(10, 10, 40, 40));

        var hits = index.Intersecting(SKRectI.Create(0, 0, 2000, 100)).ToList();

        output.WriteLine($"returned: {string.Join(", ", hits)}");
        Assert.Equal([0, 1, 2, 3], hits);
    }

    /// <summary>A stroke spanning many cells is returned once, not once per cell.</summary>
    [Fact]
    public void AStrokeSpanningManyCellsIsReturnedOnce()
    {
        var index = new StrokeIndex(new TileGrid(64));
        index.Add(0, SKRectI.Create(0, 0, 1000, 1000)); // ~256 cells

        var hits = index.Intersecting(SKRectI.Create(0, 0, 1000, 1000)).ToList();

        Assert.Single(hits);
        Assert.Equal(0, hits[0]);
    }

    // ---- completeness --------------------------------------------------------

    /// <summary>
    /// The whole point, stated as an equivalence: the index must agree with the
    /// honest answer of checking every stroke's bounds by hand.
    /// </summary>
    /// <remarks>
    /// Written as a comparison against brute force rather than as a list of
    /// expected ids, because a hand-written expectation tests what I believed
    /// when I wrote it and this tests what the index actually does. A missed
    /// stroke is a missing mark, which is a correctness bug wearing a
    /// performance feature's clothes.
    /// </remarks>
    [Fact]
    public void AQueryAgreesWithCheckingEveryStrokeByHand()
    {
        var grid = new TileGrid(128);
        var index = new StrokeIndex(grid);
        var bounds = new List<SKRectI>();

        // A deterministic scatter — no RNG, per invariant 2, and reproducible
        // when this fails.
        for (var i = 0; i < 400; i++)
        {
            var x = (i * 137) % 1900;
            var y = (i * 71) % 1900;
            var w = 10 + (i % 7) * 15;
            var rect = SKRectI.Create(x, y, w, w);
            bounds.Add(rect);
            index.Add(i, rect);
        }

        foreach (var region in new[]
        {
            SKRectI.Create(0, 0, 100, 100),
            SKRectI.Create(700, 300, 250, 250),
            SKRectI.Create(1850, 1850, 200, 200),
            SKRectI.Create(0, 0, 2000, 2000),
            SKRectI.Create(999, 999, 1, 1),
        })
        {
            var expected = Enumerable.Range(0, bounds.Count)
                .Where(i => bounds[i].IntersectsWith(region))
                .ToList();
            var actual = index.Intersecting(region).ToList();

            Assert.Equal(expected, actual);
        }

        output.WriteLine($"{index.Count} strokes over {index.CellCount} cells, five regions agreed");
    }

    [Fact]
    public void AQueryOutsideEverythingReturnsNothing()
    {
        var index = new StrokeIndex();
        index.Add(0, SKRectI.Create(0, 0, 50, 50));

        Assert.Empty(index.Intersecting(SKRectI.Create(5000, 5000, 100, 100)));
        Assert.Empty(index.Intersecting(SKRectI.Create(0, 0, 0, 0)));
    }

    /// <summary>
    /// A stroke that reaches nothing still counts, so the index cannot silently
    /// disagree with the record about how many strokes there are.
    /// </summary>
    [Fact]
    public void AStrokeThatReachesNothingIsRecordedRatherThanSkipped()
    {
        var index = new StrokeIndex();
        index.Add(0, SKRectI.Create(0, 0, 50, 50));
        index.Add(1, null);
        index.Add(2, SKRectI.Create(10, 10, 50, 50));

        Assert.Equal(3, index.Count);
        Assert.True(index.BoundsOf(1).IsEmpty);
        // ...and it is never returned by a query, because it reaches nowhere.
        Assert.Equal([0, 2], index.Intersecting(SKRectI.Create(0, 0, 100, 100)).ToList());
    }

    /// <summary>
    /// B134: <see cref="StrokeIndex.Of"/> records reach, not repaint bounds. It
    /// used to build from the surface-clamped <c>CommitBounds</c>, so a stroke
    /// wholly outside the document was indexed as reaching nothing and could
    /// never be found — however close the click.
    /// </summary>
    [Fact]
    public void TheIndexKeepsUnclampedBoundsForPicking()
    {
        // Off the right edge, off the bottom edge, and off into negative space:
        // the three directions a clamp to [0, size] erases differently.
        List<Stroke> strokes =
        [
            MarkAt(2500, 100),
            MarkAt(100, 2500),
            MarkAt(-300, -300),
        ];

        var index = StrokeIndex.Of(strokes);

        for (var i = 0; i < strokes.Count; i++)
        {
            var bounds = index.BoundsOf(i);
            output.WriteLine($"stroke {i}: {bounds}");
            Assert.False(bounds.IsEmpty, $"stroke {i} lies outside the document and was indexed as reaching nothing");
        }
        Assert.Equal([0], index.Intersecting(SKRectI.Create(2490, 90, 40, 40)).ToList());
        Assert.Equal([1], index.Intersecting(SKRectI.Create(90, 2490, 40, 40)).ToList());
        Assert.Equal([2], index.Intersecting(SKRectI.Create(-310, -310, 40, 40)).ToList());
    }

    [Fact]
    public void NegativeCoordinatesIndexAndQueryTheSameAsPositiveOnes()
    {
        var index = new StrokeIndex(new TileGrid(256));
        index.Add(0, SKRectI.Create(-1000, -1000, 100, 100));
        index.Add(1, SKRectI.Create(500, 500, 100, 100));

        Assert.Equal([0], index.Intersecting(SKRectI.Create(-1050, -1050, 200, 200)).ToList());
        Assert.Equal([1], index.Intersecting(SKRectI.Create(450, 450, 200, 200)).ToList());
    }

    // ---- what it is for ------------------------------------------------------

    /// <summary>
    /// The reason this exists: a region query must look at a small fraction of a
    /// busy drawing, or a tiled render is worse than an untiled one.
    /// </summary>
    /// <remarks>
    /// Asserted as a ratio of strokes examined rather than as a time, because a
    /// millisecond here is a fact about this machine and the ratio is a fact
    /// about the index. B30 measures 800 strokes at roughly 2 ms each; the claim
    /// that matters is that rebuilding one tile of that drawing does not touch
    /// all 800.
    /// </remarks>
    [Fact]
    public void ATileSizedQueryTouchesAFractionOfABusyDrawing()
    {
        var grid = new TileGrid(256);
        var index = new StrokeIndex(grid);

        // 800 strokes over 1920x1080 — B30's "busy drawing", which it notes is
        // "not a busy drawing, it is an ordinary one".
        for (var i = 0; i < 800; i++)
        {
            var x = (i * 97) % 1900;
            var y = (i * 53) % 1060;
            index.Add(i, SKRectI.Create(x, y, 20, 20));
        }

        var inOneTile = index.Intersecting(SKRectI.Create(512, 512, 256, 256)).Count();
        var share = inOneTile / 800.0;

        output.WriteLine($"one 256px tile of an 800-stroke frame: {inOneTile} strokes ({share:P1})");
        Assert.True(
            share < 0.15,
            $"a single tile pulled in {inOneTile} of 800 strokes ({share:P0}) — the index is not narrowing anything");
        Assert.True(inOneTile > 0, "the tile pulled in nothing at all, so this asserts nothing");
    }
}
