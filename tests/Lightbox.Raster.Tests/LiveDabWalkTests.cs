using Lightbox.Core.Documents;
using Lightbox.Raster;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The walk the live path keeps for a stroke being drawn
/// (<see cref="LiveDabWalk"/>): the same dabs as the reference walk, to the
/// bit, for work that does not grow with the stroke.
/// </summary>
/// <remarks>
/// <para>
/// A pointer event used to walk every dab of the mark, twice, and compare the
/// lists from the first dab. On a line drawn for ten seconds without lifting,
/// the application stopped showing the line part way and showed all of it at
/// pen-up (the owner's report, 2026-10-12). Every stroke in the suite and in
/// the perf lab was a second or two long, so nothing saw it.
/// </para>
/// <para>
/// Two things are held here. <b>Faithfulness</b>: these dabs decide where the
/// mark lands and every dynamic is seeded from where a dab is, so a walk that
/// drifted from the reference by one dab would draw a different mark live from
/// the one the record replays. <b>Bounded work</b>: counted in densified
/// points walked, not timed, so the guard does not depend on what else the
/// machine is doing.
/// </para>
/// </remarks>
public class LiveDabWalkTests(ITestOutputHelper output)
{
    public static TheoryData<string> Brushes => new() { "ink", "pressure", "soft", "turning", "loaded" };

    private static BrushSettings Brush(string name) => name switch
    {
        "ink" => new BrushSettings { Size = 5, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1 },
        "pressure" => new BrushSettings { Size = 24, Hardness = 0.9, Spacing = 0.08, PressureEnabled = true, PressureSizeGamma = 1.4 },
        // Soft, even and unjittered: the walk subdivides its intervals for fidelity.
        "soft" => new BrushSettings { Size = 60, Hardness = 0.3, Flow = 0.4, Spacing = 0.1, AntiAlias = true },
        "turning" => new BrushSettings
        {
            Size = 30, Hardness = 0.8, Spacing = 0.12, TipId = "tip-builtin-paintbrush", AngleFollowsDirection = true,
        },
        "loaded" => new BrushSettings
        {
            Size = 30, Hardness = 0.8, Spacing = 0.1, Medium = new MediumSettings { PaintLoad = 0.5 },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>A wandering stroke: bends, reversals, changes of pace and of pressure.</summary>
    private static List<StrokePoint> Wander(int seed, int count)
    {
        var random = new Random(seed);
        var points = new List<StrokePoint>(count);
        double x = 400, y = 400, heading = random.NextDouble() * 6.28, pace = 6;
        var pressure = 0.6;
        for (var i = 0; i < count; i++)
        {
            if (random.Next(9) == 0) heading += (random.NextDouble() - 0.5) * 2.4;
            if (random.Next(40) == 0) heading += Math.PI;
            if (random.Next(12) == 0) pace = 0.5 + (random.NextDouble() * 22);
            pressure = Math.Clamp(pressure + ((random.NextDouble() - 0.5) * 0.12), 0.05, 1);
            heading += 0.03;
            x += Math.Cos(heading) * pace;
            y += Math.Sin(heading) * pace;
            points.Add(new StrokePoint(x, y, pressure));
        }
        return points;
    }

    /// <summary>
    /// The stroke as the live path sees it after <paramref name="n"/> samples:
    /// the newest few points are not where they will end up, as with smoothing
    /// on, so each event rewrites the tail of the last one.
    /// </summary>
    private static List<StrokePoint> AsSeenAt(List<StrokePoint> final, int n, int unsettled)
    {
        var seen = final.Take(n).ToList();
        for (var back = 0; back < unsettled && back < n - 1; back++)
        {
            var i = n - 1 - back;
            var pull = (unsettled - back) / (double)(unsettled + 1);
            var p = seen[i];
            var q = seen[i - 1];
            seen[i] = new StrokePoint(p.X + ((q.X - p.X) * pull * 0.4), p.Y + ((q.Y - p.Y) * pull * 0.4), p.Pressure);
        }
        return seen;
    }

    [Theory]
    [MemberData(nameof(Brushes))]
    public void EveryEventIsTheReferenceWalk(string name)
    {
        var brush = Brush(name);
        var walked = 0;
        foreach (var unsettled in new[] { 0, 3 })
        {
            var final = Wander(seed: 11 + unsettled, count: 260);
            var walk = new LiveDabWalk();
            var before = new List<BrushEngine.Dab>();
            for (var n = 1; n <= final.Count; n++)
            {
                var stroke = new Stroke
                {
                    Tool = ToolKind.Brush, Color = "#101010", Brush = brush, Points = AsSeenAt(final, n, unsettled),
                };
                var live = walk.Of(stroke);
                var reference = BrushEngine.WalkDabs(stroke);

                Assert.True(
                    reference.SequenceEqual(live),
                    $"{name}, {unsettled} unsettled, {n} points: the kept walk has {live.Count} dabs and the reference {reference.Count}, "
                    + $"first difference at {FirstDifference(reference, live)}");

                // What it says changed is what changed: the list before was the
                // kept dabs followed by the replaced ones.
                Assert.True(walk.Kept <= live.Count, $"{name}: kept {walk.Kept} of {live.Count}");
                Assert.True(
                    before.SequenceEqual(live.Take(walk.Kept).Concat(walk.Replaced)),
                    $"{name}, {n} points: kept {walk.Kept} and replaced {walk.Replaced.Count} do not make up the {before.Count} dabs before");

                // And the settle rules asked of the tail answer as they do asked of the whole.
                foreach (var tolerance in new[] { 0.0, 0.25 })
                {
                    var whole = BrushEngine.StableCount(live, n == 1 ? null : before, tolerance, out var wholeExact);
                    if (tolerance <= 0)
                    {
                        whole = Math.Min(whole, BrushEngine.SettledCount(live, n == 1 ? null : before));
                        wholeExact = whole;
                    }
                    if (n == 1) continue;
                    var tail = BrushEngine.StableCount(live, walk.Kept, walk.Replaced, tolerance, out var tailExact);
                    Assert.True(
                        whole == tail && wholeExact == tailExact,
                        $"{name}, {n} points, tolerance {tolerance}: settled {whole} (exact {wholeExact}) asked of the whole list, "
                        + $"{tail} (exact {tailExact}) asked of the tail");
                }

                before = [.. live];
                walked++;
            }
        }
        output.WriteLine($"{name}: {walked} events agreed with the reference walk");
    }

    private static int FirstDifference(IReadOnlyList<BrushEngine.Dab> a, IReadOnlyList<BrushEngine.Dab> b)
    {
        var shared = Math.Min(a.Count, b.Count);
        for (var i = 0; i < shared; i++)
        {
            if (!a[i].Equals(b[i])) return i;
        }
        return shared;
    }

    /// <summary>
    /// The guard for the owner's report. A pointer event walks the end of the
    /// stroke, however long the stroke has become.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void AnEventWalksTheEndOfTheStrokeNotTheStroke(int unsettled)
    {
        // Ten seconds of pen and more: 3000 samples.
        var final = Wander(seed: 5, count: 3000);
        var brush = Brush("ink");
        var walk = new LiveDabWalk();
        var worstEarly = 0;
        var worstLate = 0;
        var dabs = 0;
        for (var n = 1; n <= final.Count; n++)
        {
            var live = walk.Of(new Stroke
            {
                Tool = ToolKind.Brush, Color = "#101010", Brush = brush, Points = AsSeenAt(final, n, unsettled),
            });
            dabs = live.Count;
            if (n is > 100 and <= 300) worstEarly = Math.Max(worstEarly, walk.LastWalked);
            if (n > 2700) worstLate = Math.Max(worstLate, walk.LastWalked);
            if (n > 100)
            {
                Assert.True(
                    live.Count - walk.Kept < 400,
                    $"at {n} points an event replaced {live.Count - walk.Kept} of {live.Count} dabs: the walk started over");
            }
        }

        output.WriteLine(
            $"{unsettled} unsettled: {dabs} dabs after 3000 points; an event walked at most {worstEarly} densified points "
            + $"early in the stroke and {worstLate} at its end");

        // The whole densified path at the end is tens of thousands of points.
        // The bound is the tail a pace of 22 px and three unsettled points can
        // be, with room: what matters is that it is nowhere near the stroke.
        Assert.True(worstLate <= 200, $"an event at the end of the stroke walked {worstLate} densified points");
        Assert.True(
            worstLate <= (worstEarly * 2) + 20,
            $"an event walked {worstEarly} densified points early in the stroke and {worstLate} at its end: the walk grows with the stroke");
    }

    /// <summary>The comparison with the walk that lacks the newest points resumes too, and answers the same.</summary>
    [Fact]
    public void TheSettledPrefixResumedIsTheSettledPrefix()
    {
        var final = Wander(seed: 23, count: 400);
        var brush = Brush("pressure");
        var whole = new LiveDabWalk();
        var shorter = new LiveDabWalk();
        var known = 0;
        const int Drop = 3;
        for (var n = Drop + 1; n <= final.Count; n++)
        {
            var seen = AsSeenAt(final, n, 2);
            var now = whole.Of(new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = seen });
            var without = shorter.Of(new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = seen.Take(n - Drop).ToList() });

            var fromTheStart = BrushEngine.SettledPrefix(now, without, 0, out var agreedFromTheStart);
            var resumed = BrushEngine.SettledPrefix(
                now, without, Math.Min(known, Math.Min(whole.Kept, shorter.Kept)), out var agreed);
            Assert.True(
                fromTheStart == resumed && agreedFromTheStart == agreed,
                $"at {n} points: {fromTheStart} settled comparing from the first dab, {resumed} resuming from {known}");
            known = agreed;
        }
    }

    /// <summary>A second stroke on the same walk, and one that shrinks, are walked as themselves.</summary>
    [Fact]
    public void ANewOrShorterStrokeIsWalkedAfresh()
    {
        var brush = Brush("turning");
        var walk = new LiveDabWalk();
        var first = Wander(seed: 3, count: 120);
        walk.Of(new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = first });

        var shorter = new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = first.Take(40).ToList() };
        Assert.True(BrushEngine.WalkDabs(shorter).SequenceEqual(walk.Of(shorter)), "a stroke that lost points");

        var other = new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = Wander(seed: 4, count: 90) };
        Assert.True(BrushEngine.WalkDabs(other).SequenceEqual(walk.Of(other)), "another stroke on the same walk");

        var otherBrush = new Stroke { Tool = ToolKind.Brush, Brush = Brush("ink"), Points = other.Points };
        Assert.True(BrushEngine.WalkDabs(otherBrush).SequenceEqual(walk.Of(otherBrush)), "the same points with another brush");

        walk.Reset();
        var one = new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = first.Take(1).ToList() };
        Assert.True(BrushEngine.WalkDabs(one).SequenceEqual(walk.Of(one)), "a single point");
        var two = new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = first.Take(2).ToList() };
        Assert.True(BrushEngine.WalkDabs(two).SequenceEqual(walk.Of(two)), "two points");
    }
}
