using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What one pointer event pays to know where the dabs are, as the stroke grows
/// (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>The live path calls <c>WalkDabs</c> once per pointer event and gets the
/// whole stroke back.</b> `IncrementalDensify` already spares it re-densifying
/// the points — that is what `_live.Densify` is for — but the walk still builds
/// a fresh `List&lt;Dab&gt;` covering every dab laid so far, every event. That is
/// work proportional to the mark on a per-event path, which is what invariant 6
/// forbids.
/// </para>
/// <para>
/// <b>Why this is suspected rather than assumed.</b> The owner's capture of
/// 2026-08-28 00:50 splits an event's stamp into restore, settled, backup and
/// tail: 0.12 + 0.70 + 0.05 + 0.67 = 1.54 ms against a measured median of
/// <b>2.68</b>. Something un-instrumented is holding about 40% of the event, and
/// the walk is the only per-event operation left that touches the whole stroke.
/// </para>
/// <para>
/// <b>Reported, not bounded.</b> A threshold here would be a guess at a number
/// nobody has; the shape is the point. If the cost per event rises with the
/// stroke's length, the fix is an incremental dab list beside the incremental
/// densify. If it is flat, the walk is exonerated and the missing 40% is
/// somewhere else.
/// </para>
/// </remarks>
public class DabWalkGrowsWithTheStrokeTests(ITestOutputHelper output)
{
    /// <summary>The brush the owner draws with when it stalls.</summary>
    private static BrushSettings Heavy() => new()
    {
        Size = 70, Hardness = 0.6, Flow = 0.7, Opacity = 1,
        WetEdge = 0.6, Granulation = 0.4,
    };

    private static Stroke Of(int points)
    {
        var pts = new List<StrokePoint>(points);
        double x = 200, y = 600, heading = -0.15;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.0012;
            x += 4.2 * Math.Cos(heading);
            y += 4.2 * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush, Color = "#203040", Brush = Heavy(), Points = pts,
        };
    }

    /// <summary>
    /// The cost of the walk one pointer event performs, at several stroke
    /// lengths, with the same incremental densify the live path keeps.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void WhatOnePointerEventPaysToWalkTheDabs()
    {
        // The live session keeps one of these for the whole stroke, so the walk
        // is measured with the same help it actually gets.
        var densify = new IncrementalDensify();
        var longest = Of(900);
        BrushEngine.WalkDabs(longest, densify);

        output.WriteLine("  points     dabs   best ms   us per event");
        var costs = new List<(int Points, int Dabs, double Ms)>();
        foreach (var points in new[] { 50, 100, 200, 400, 800 })
        {
            var stroke = Of(points);
            var walkDensify = new IncrementalDensify();
            // Warm it the way a stroke would: the event being measured is never
            // the first, so the densify cache is populated before the clock.
            var dabs = BrushEngine.WalkDabs(stroke, walkDensify);

            var runs = new List<double>();
            for (var i = 0; i < 9; i++)
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                BrushEngine.WalkDabs(stroke, walkDensify);
                runs.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                         * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }

            // The minimum, for the reason recorded in LiveTipDabCostTests:
            // contention only ever adds, so the fastest run is the machine's
            // answer and a median is whatever else it was doing.
            var best = runs.Min();
            costs.Add((points, dabs.Count, best));
            output.WriteLine($"  {points,6} {dabs.Count,8} {best,9:0.###} {best * 1000,14:0}");
        }

        var smallest = costs[0];
        var largest = costs[^1];
        var growth = largest.Ms / Math.Max(1e-9, smallest.Ms);
        var dabGrowth = largest.Dabs / (double)Math.Max(1, smallest.Dabs);
        output.WriteLine("");
        output.WriteLine(
            $"  the walk grew {growth:0.#}x while the stroke grew {dabGrowth:0.#}x");
        output.WriteLine(
            "  a pen event arrives every ~5.7 ms, and the whole stamp measured 2.68 ms");

        Assert.True(
            largest.Ms > 0,
            "the walk did not take measurable time, so nothing was measured");

        // **The claim, and it is a shape rather than a budget.** A per-event
        // operation whose cost tracks the stroke's length is the thing
        // invariant 6 forbids; one that is flat is not this entry's problem.
        Assert.True(
            growth > dabGrowth * 0.5,
            $"the walk grew only {growth:0.#}x while the stroke grew {dabGrowth:0.#}x, so it is "
            + "NOT proportional to the mark and B189's missing 40% is somewhere else — "
            + "look at RangeBounds and the coverage accumulation before the walk");
    }
}
