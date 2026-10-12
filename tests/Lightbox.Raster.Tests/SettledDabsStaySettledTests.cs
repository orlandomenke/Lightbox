using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What the live path may stamp for good while a stroke is still being drawn.
/// A dab stamped as settled cannot be taken back, so "settled" has to mean
/// <em>will never change</em> — and if it does not, the mark on screen is not
/// the mark the record replays, and pen-up has to render the stroke again
/// (the stall the paint path exists to remove).
/// </summary>
/// <remarks>
/// <para>
/// The rules this replaced asked whether two consecutive walks of the stroke
/// agreed on a dab, one of them forgiving a quarter of a pixel. The perf lab
/// found what that costs: about one stroke in eight on the flats rendered
/// again at pen-up, and the one capture that logged the detail showed a dab
/// three from the tip, stamped for good, whose pressure then changed. That
/// was not reproduced outside the application — random strokes through the
/// same walk never fooled either old rule — so this file does not claim to
/// hold the old failure. It holds the new rule to its own promise.
/// </para>
/// <para>
/// <see cref="BrushEngine.SettledPrefix"/> asks which dabs are the same
/// whether or not the newest few points exist. Strokes here are degenerate on
/// purpose — long runs where nothing changes, then a sample that does — since
/// a stretch where nothing happens to be changing is where "agrees with last
/// time" and "is finished" come apart.
/// </para>
/// </remarks>
public class SettledDabsStaySettledTests(ITestOutputHelper output)
{
    /// <summary>A stroke of runs: each run holds a heading, a pace and a pressure, then one of them changes.</summary>
    private static List<StrokePoint> Raw(int seed, int count)
    {
        var random = new Random(seed);
        var points = new List<StrokePoint>(count);
        double x = 300, y = 300, heading = random.NextDouble() * Math.PI * 2, pace = 4 + (random.NextDouble() * 10);
        var pressure = 0.3 + (random.NextDouble() * 0.7);
        var left = 0;
        while (points.Count < count)
        {
            if (left-- <= 0)
            {
                left = 2 + random.Next(14);
                switch (random.Next(5))
                {
                    case 0: heading += (random.NextDouble() - 0.5) * 2.5; break;
                    case 1: pressure = 0.15 + (random.NextDouble() * 0.85); break;
                    case 2: pace = 2 + (random.NextDouble() * 16); break;
                    case 3: heading += Math.PI; break; // straight back
                    default: break; // nothing changes: the run simply goes on
                }
            }
            x += Math.Cos(heading) * pace;
            y += Math.Sin(heading) * pace;
            points.Add(new StrokePoint(x, y, pressure));
        }
        return points;
    }

    private static BrushSettings Brush(string kind) => kind switch
    {
        "follows" => new BrushSettings { Size = 40, Hardness = 0.6, Spacing = 0.1, Roundness = 0.6, AngleFollowsDirection = true },
        "jittered" => new BrushSettings { Size = 30, Hardness = 0.7, Spacing = 0.12, SizeJitter = 0.2, Scatter = 0.3 },
        "runs dry" => new BrushSettings { Size = 50, Hardness = 0.4, Spacing = 0.1, Medium = new MediumSettings { PaintLoad = 0.5 } },
        _ => new BrushSettings { Size = 60, Hardness = 0.4, Spacing = 0.1 },
    };

    private static List<BrushEngine.Dab> Walk(
        BrushSettings brush, List<StrokePoint> raw, int count, bool smooth, IncrementalDensify? cache = null)
    {
        if (count <= 0) return [];
        var points = raw.GetRange(0, count);
        if (smooth && points.Count >= 3) points = GeometryOps.Smooth(points, 1);
        return BrushEngine.WalkDabs(new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = points }, cache);
    }

    public static IEnumerable<object[]> Cases() =>
        from kind in new[] { "plain", "follows", "jittered", "runs dry" }
        from smooth in new[] { true, false }
        select new object[] { kind, smooth };

    /// <summary>
    /// Every dab the rule calls settled is in the finished stroke unchanged,
    /// however the samples were batched into events.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void WhatIsCalledSettledNeverChanges(string kind, bool smooth)
    {
        var brush = Brush(kind);
        long settledTotal = 0, dabsTotal = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var raw = Raw(seed, 90);
            var final = Walk(brush, raw, raw.Count, smooth);
            var batch = new Random(seed * 7919);
            for (var n = 2; n <= raw.Count; n += 1 + batch.Next(4))
            {
                var now = Walk(brush, raw, n, smooth);
                // As the live path asks it: the stroke's own points, as smoothed
                // so far, without the newest few.
                var points = raw.GetRange(0, n);
                if (smooth && points.Count >= 3) points = GeometryOps.Smooth(points, 1);
                var settled = BrushEngine.SettledPrefix(
                    now,
                    drop => points.Count - drop <= 0
                        ? []
                        : BrushEngine.WalkDabs(
                            new Stroke { Tool = ToolKind.Brush, Brush = brush, Points = points.GetRange(0, points.Count - drop) }),
                    smooth ? 1 : 0);
                settledTotal += settled;
                dabsTotal += now.Count;
                for (var d = 0; d < settled; d++)
                {
                    Assert.True(
                        d < final.Count && now[d].Equals(final[d]),
                        $"{kind}, seed {seed}, {n} of {raw.Count} points: dab {d} of {settled} called settled is not in the finished stroke");
                }
            }
        }
        output.WriteLine($"{kind}, smooth {smooth}: settled {100.0 * settledTotal / dabsTotal:0.0}% of the dabs walked");
        // And it settles nearly all of them: a rule that settled nothing would
        // pass the loop above and re-stamp the whole stroke on every event.
        Assert.True(settledTotal > dabsTotal * 0.8, $"only {100.0 * settledTotal / dabsTotal:0.0}% of the dabs were ever settled");
    }
}
