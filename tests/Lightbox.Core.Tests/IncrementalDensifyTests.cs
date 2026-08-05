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

    /// <summary>
    /// A prefix of one array, without copying it.
    /// </summary>
    /// <remarks>
    /// <b>This exists because the first version of the timing test below measured the wrong thing.</b>
    /// It built each prefix with <c>points.Take(k).ToList()</c> <em>inside</em> the timed region — an
    /// O(k) copy that grows linearly by construction — and then reported the growth as the cache
    /// re-densifying. It flaked at a 13.7× ratio on a loaded machine, and every millisecond of that
    /// was the test's own allocation. The number was real and the attribution was not, which is the
    /// lesson this repository keeps paying for; here it cost a red suite rather than a wrong fix.
    /// </remarks>
    private sealed class Prefix(List<StrokePoint> all, int length) : IReadOnlyList<StrokePoint>
    {
        public StrokePoint this[int index] => all[index];
        public int Count { get; } = length;
        public IEnumerator<StrokePoint> GetEnumerator() => all.Take(Count).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// An append at the end of a long stroke costs a fraction of re-densifying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The comparison is against a reference measured in the same breath, not against this test's
    /// own earlier self.</b> The first version compared the last fifty appends of a 600-point stroke
    /// to the first fifty and allowed a 6× spread. Both numbers are around 0.02–0.07 ms, and the four
    /// test assemblies run in parallel, so under full-solution load the spread reached <b>14.6×</b> on
    /// a branch whose only source change was a bench file — red for reasons that had nothing to do
    /// with the code. Widening the threshold was the wrong repair: it would have kept a measurement
    /// whose noise is the same size as its signal.
    /// </para>
    /// <para>
    /// What the cache actually claims is that an append is much cheaper than the
    /// <see cref="GeometryOps.Densify"/> it replaces — 0.067 ms against 0.84 ms as measured when B46
    /// landed. Timing both on the same machine at the same moment makes CPU contention a common
    /// factor that cancels in the ratio, and it is the claim in the class's own remarks rather than a
    /// proxy for it. A cache that regressed into rebuilding lands at a ratio near 1, nowhere near the
    /// 4× allowed here.
    /// </para>
    /// <para>
    /// The clock-free half of the same property — <em>how many spans</em> an append recomputes — is
    /// <see cref="OnlyTheTailIsRecomputedWhenAPointIsAppended"/>, and that is the assertion that
    /// catches a regression precisely. This one exists for the part that test cannot see: the linear
    /// scan in <c>FirstDifference</c> and the linear copy into <c>_seen</c> are real work outside the
    /// interpolation, and only a clock notices if one of them grows a constant.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Performance")]
    public void AnAppendCostsAFractionOfReDensifying()
    {
        var points = Arc(600);
        var cache = new IncrementalDensify();
        cache.Of(new Prefix(points, 3));

        double Appends(int from, int to)
        {
            // Prefixes built outside the clock. Timing an allocation that grows with the stroke and
            // calling the result "the cache" is how the first version of this test lied.
            var views = Enumerable.Range(from, to - from).Select(k => new Prefix(points, k)).ToList();
            var sw = Stopwatch.StartNew();
            foreach (var view in views) cache.Of(view);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / views.Count;
        }

        double FullDensifies(int n)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < n; i++) GeometryOps.Densify(points);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / n;
        }

        var early = Appends(4, 54);
        Appends(54, 550);       // walk the middle so the cache is warm at the far end
        var late = Appends(550, 600);
        var reference = FullDensifies(50);

        output.WriteLine(
            $"append cost: first 50 events {early:F4} ms, last 50 {late:F4} ms; "
            + $"a full Densify of the same 600 points {reference:F4} ms "
            + $"({reference / Math.Max(late, 1e-6):F1}× the late append)");

        // Not vacuous: the reference has to be doing real work, or "much cheaper than nothing" would
        // pass on a Densify that had been turned into a no-op.
        Assert.True(reference > 0.05, $"a full Densify measured {reference:F4} ms — the reference is not real work");

        Assert.True(
            late * 4 < reference,
            $"an append late in a 600-point stroke cost {late:F4} ms against {reference:F4} ms to "
            + "re-densify the whole thing — the cache is rebuilding rather than extending");
    }
}
