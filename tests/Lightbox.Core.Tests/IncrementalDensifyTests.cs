using System.Diagnostics;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The densify cache the live preview walks through (B46).
/// </summary>
/// <remarks>
/// <para>
/// The saving is worth having — 0.84 ms of a 1.15 ms walk at 600 points was pure recomputation —
/// but it is the <b>faithfulness</b> that these tests are mostly about. These points decide where
/// dabs land, and every dab dynamic is seeded from where its dab is, so a cache that drifted from
/// <see cref="GeometryOps.Densify"/> by one interpolated point would move a dab, re-roll its
/// scatter from a different position, and change the mark. Value-identical or it is not usable.
/// </para>
/// </remarks>
public class IncrementalDensifyTests(ITestOutputHelper output)
{
    /// <summary>A fast arc: points far enough apart that densification actually fires.</summary>
    private static List<StrokePoint> Arc(int n) =>
        [.. Enumerable.Range(0, n).Select(i =>
            new StrokePoint(40 + i * 11.0, 200 + Math.Sin(i * 0.22) * 90, 0.4 + (i % 7) * 0.08))];

    /// <summary>A slow stroke: every span already shorter than the chord, so Densify passes through.</summary>
    private static List<StrokePoint> Crawl(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new StrokePoint(40 + i * 0.7, 200 + i * 0.3, 0.8))];

    /// <summary>Sharp turns, which Densify deliberately refuses to round off.</summary>
    private static List<StrokePoint> Zigzag(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new StrokePoint(40 + i * 14.0, i % 2 == 0 ? 120 : 260, 0.9))];

    public static TheoryData<string, int> Shapes => new()
    {
        { "arc", 40 },
        { "crawl", 40 },
        { "zigzag", 25 },
    };

    private static List<StrokePoint> Shape(string name, int n) => name switch
    {
        "arc" => Arc(n),
        "crawl" => Crawl(n),
        _ => Zigzag(n),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryPrefixMatchesDensifyExactly(string shape, int n)
    {
        var points = Shape(shape, n);
        var cache = new IncrementalDensify();

        // Every prefix, because that is what a drag is: the cache is asked for the answer once per
        // pointer event and has to be right every single time, not just at the end.
        for (var k = 1; k <= points.Count; k++)
        {
            var prefix = points.Take(k).ToList();
            var reference = GeometryOps.Densify(prefix);
            var incremental = cache.Of(prefix);

            Assert.Equal(reference.Count, incremental.Count);
            for (var i = 0; i < reference.Count; i++)
            {
                Assert.Equal(reference[i], incremental[i]);
            }
        }
        output.WriteLine($"{shape}: {points.Count} points → {cache.Of(points).Count} densified");
    }

    [Fact]
    public void OnlyTheTailIsRecomputedWhenAPointIsAppended()
    {
        // The saving, stated as the property it rests on rather than as a duration: a span is
        // interpolated from one point behind and two ahead, so appending a point can only disturb
        // the last few spans however long the stroke is. A timing assertion here would measure the
        // machine; this measures the algorithm.
        var points = Arc(200);
        var cache = new IncrementalDensify();
        cache.Of(points.Take(3).ToList());

        var worst = 0;
        for (var k = 4; k <= points.Count; k++)
        {
            cache.Of(points.Take(k).ToList());
            worst = Math.Max(worst, cache.LastRecomputedSpans);
        }

        output.WriteLine($"worst case {worst} spans recomputed on an append, out of {points.Count - 1}");
        Assert.True(worst <= 3, $"an append recomputed {worst} spans — the cache is not incremental");
    }

    [Fact]
    public void ARewrittenTailInvalidatesFarEnoughBack()
    {
        // Stabilisers move points that have already been seen. The cache must not assume growth: it
        // locates the first difference and drops two spans before it, because that is the earliest
        // span whose control points saw the changed value.
        var points = Arc(30);
        var cache = new IncrementalDensify();
        cache.Of(points);

        var moved = points.ToList();
        moved[20] = new StrokePoint(moved[20].X + 9, moved[20].Y - 13, moved[20].Pressure);

        var reference = GeometryOps.Densify(moved);
        var incremental = cache.Of(moved);
        Assert.Equal(reference.Count, incremental.Count);
        for (var i = 0; i < reference.Count; i++) Assert.Equal(reference[i], incremental[i]);
    }

    [Fact]
    public void AShorterListIsHandledRatherThanIndexedPast()
    {
        // Undo, and a second stroke on the same instance. Both look like the list shrinking.
        var cache = new IncrementalDensify();
        cache.Of(Arc(30));

        var shorter = Arc(8);
        var reference = GeometryOps.Densify(shorter);
        var incremental = cache.Of(shorter);
        Assert.Equal(reference.Count, incremental.Count);
        for (var i = 0; i < reference.Count; i++) Assert.Equal(reference[i], incremental[i]);

        // And a different stroke entirely, which shares no prefix at all.
        var other = Zigzag(12);
        var otherReference = GeometryOps.Densify(other);
        var otherIncremental = cache.Of(other);
        Assert.Equal(otherReference.Count, otherIncremental.Count);
        for (var i = 0; i < otherReference.Count; i++) Assert.Equal(otherReference[i], otherIncremental[i]);
    }

    [Fact]
    public void UnderThreePointsBehavesLikeDensify()
    {
        var cache = new IncrementalDensify();
        var two = new List<StrokePoint> { new(10, 10, 1), new(200, 140, 1) };

        // Densify hands a short list straight back, and matching that keeps the two substitutable
        // in the one place it matters: a tap, which is a one-point stroke.
        Assert.Same(two, cache.Of(two));
        var one = new List<StrokePoint> { new(10, 10, 1) };
        Assert.Same(one, cache.Of(one));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheWalkStopsGrowingWithTheStroke()
    {
        // Loose on purpose: this catches the order-of-magnitude regression of somebody making the
        // cache rebuild, not drift. The ratio is what matters — a full re-densify is linear in the
        // stroke, so the last events of a long stroke cost many times the first ones.
        var points = Arc(600);
        var cache = new IncrementalDensify();
        cache.Of(points.Take(3).ToList());

        double Cost(int from, int to)
        {
            var sw = Stopwatch.StartNew();
            for (var k = from; k < to; k++) cache.Of(points.Take(k).ToList());
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / (to - from);
        }

        var early = Cost(4, 54);
        // Walk the middle so the cache is warm at the far end, then measure there.
        Cost(54, 550);
        var late = Cost(550, 600);

        output.WriteLine($"append cost: first 50 events {early:F4} ms, last 50 {late:F4} ms");
        Assert.True(
            late < early * 6 + 0.05,
            $"an append late in the stroke cost {late:F4} ms against {early:F4} ms early on — "
            + "the cache is re-densifying rather than extending");
    }
}
