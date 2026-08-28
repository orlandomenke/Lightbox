using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// How many times the live path draws the same dab, and how far that dab
/// actually moved between the drawings (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where this comes from.</b> <c>WhatOneEventCostsAtEachBrushSizeTests</c>
/// puts the colour stamp at <b>81%</b> of a pointer event at size 500, and shows
/// the cost is clean: <b>6.3 ns a pixel</b> at 500 against 12.5 at 70, so a big
/// dab is if anything more efficient per pixel than a small one and there is no
/// per-dab waste to shave. A single dab at size 500 costs <b>1245 us</b>, and a
/// fast stroke needs about one new one per event. That alone would fit inside
/// the pen's ~5 ms.
/// </para>
/// <para>
/// <b>So the multiplier has to be the re-stamping, and that is what this
/// measures rather than infers.</b> The live path holds a provisional tail: dabs
/// after the stable cut are taken back and stamped again on every pointer event,
/// because <c>Densify</c> looks one point ahead and the newest dabs move as the
/// next point arrives.
/// </para>
/// <para>
/// <b>The reason that rule is strict is worth reading before proposing to relax
/// it.</b> <c>StableCount</c> compares positions for <em>exact</em> equality
/// because every dab dynamic is seeded from the dab's position through
/// <c>Hash01</c> — with scatter at 0.35 on a 30 px brush, B45 measured a
/// sub-pixel move throwing a dab up to ten pixels somewhere else. That is not
/// negotiable for a brush with scatter. Whether it binds for a brush with
/// <em>none</em> is a different question, and it is the one worth having a
/// number for.
/// </para>
/// <para>
/// <b>Reported, not bounded.</b> Two quantities decide the next move and neither
/// has a budget: how many dabs an event re-stamps, and how far those dabs
/// actually moved. If they move meaningfully, the tail is doing necessary work
/// and the fix is elsewhere. If they move by a fraction of a pixel on a brush
/// with nothing seeded from position, the re-stamp is buying a difference nobody
/// can see, at 1245 us a dab.
/// </para>
/// </remarks>
public class HowOftenABigDabIsDrawnAgainTests(ITestOutputHelper output)
{
    /// <summary>Travel between pointer events on a fast stroke, from the captures.</summary>
    private const double TravelPerEvent = 60;

    private static BrushSettings Soft(double size, double scatter = 0) => new()
    {
        Size = size, Hardness = 0.35, Flow = 0.7, Opacity = 1, Scatter = scatter,
    };

    /// <summary>
    /// The stroke as it looked after this many pointer events, with a little
    /// curvature — a dead-straight stroke would let Densify predict the next
    /// span exactly and understate how much the tail moves.
    /// </summary>
    private static Stroke After(BrushSettings brush, int events)
    {
        var pts = new List<StrokePoint>();
        double x = 400, y = 700, heading = -0.2;
        for (var i = 0; i <= events; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.03;
            x += TravelPerEvent * Math.Cos(heading);
            y += TravelPerEvent * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush, Color = "#203040", Brush = brush, Points = pts,
        };
    }

    /// <summary>
    /// Which field of a dab moves first, when the stroke grows by one event.
    /// </summary>
    /// <remarks>
    /// Written because a tolerance on position alone made the settled prefix
    /// SHORTER, not longer — the extra caution of also requiring pressure,
    /// heading and load to be identical broke the comparison at the first dab.
    /// Today's shipping rule compares position and nothing else, so whatever
    /// this names is already being accepted on settled dabs.
    /// </remarks>
    [Fact]
    public void WhichPartOfADabActuallyChanges()
    {
        var brush = Soft(500);
        var densify = new IncrementalDensify();
        var a = BrushEngine.WalkDabs(After(brush, 10), densify);
        var b = BrushEngine.WalkDabs(After(brush, 11), densify);

        int posSame = 0, pressureSame = 0, headingSame = 0, loadSame = 0;
        var shared = Math.Min(a.Count, b.Count);
        while (posSame < shared && a[posSame].Pos == b[posSame].Pos) posSame++;
        while (pressureSame < shared && a[pressureSame].Pressure == b[pressureSame].Pressure) pressureSame++;
        while (headingSame < shared && a[headingSame].Heading == b[headingSame].Heading) headingSame++;
        while (loadSame < shared && a[loadSame].Load == b[loadSame].Load) loadSame++;

        output.WriteLine($"  {shared} dabs shared between the two walks:");
        output.WriteLine($"    position agrees for   {posSame}");
        output.WriteLine($"    pressure agrees for   {pressureSame}");
        output.WriteLine($"    heading  agrees for   {headingSame}");
        output.WriteLine($"    load     agrees for   {loadSame}");
        if (headingSame < shared)
        {
            output.WriteLine(
                $"    first heading disagreement at {headingSame}: "
                + $"{a[headingSame].Heading:0.########} vs {b[headingSame].Heading:0.########}");
        }

        Assert.True(shared > 0, "the two walks share no dabs, so nothing was compared");
    }

    [Fact]
    public void WhatAnEventReStampsAndHowFarThoseDabsMoved()
    {
        output.WriteLine(
            "   size  scatter  tolerance   new/event  restamped/event   worst move   1st-restamp move");
        foreach (var scatter in new[] { 0.0, 0.35 })
        {
            foreach (var size in new double[] { 70, 500 })
            foreach (var useTolerance in new[] { false, true })
            {
                var brush = Soft(size, scatter);
                var tolerance = useTolerance ? BrushEngine.SettleTolerance(brush) : 0;
                var densify = new IncrementalDensify();

                List<BrushEngine.Dab>? previous = null;
                var stableSoFar = 0;
                int totalNew = 0, totalRestamped = 0, events = 0;
                double worstMove = 0, firstRestampMove = 0;

                for (var e = 2; e <= 30; e++)
                {
                    var stroke = After(brush, e);
                    var dabs = BrushEngine.WalkDabs(stroke, densify);
                    var stable = BrushEngine.StableCount(dabs, previous, tolerance);

                    if (previous is not null)
                    {
                        events++;
                        var settledCut = Math.Max(stableSoFar, Math.Min(stable, dabs.Count));

                        // Exactly the live path's own split: everything from the
                        // previous stable cut to the new one is stamped once and
                        // never again; everything after it is on loan and comes
                        // back next event.
                        totalNew += Math.Max(0, settledCut - stableSoFar);
                        var restamped = Math.Max(0, previous.Count - settledCut);
                        totalRestamped += restamped;

                        // How far the dabs that will be re-stamped actually went.
                        // The first of them is the interesting one: it is the
                        // dab the stable cut stopped at, so it is the cheapest
                        // possible thing to have kept.
                        for (var i = settledCut; i < Math.Min(previous.Count, dabs.Count); i++)
                        {
                            var moved = Math.Sqrt(
                                Math.Pow(dabs[i].Pos.X - previous[i].Pos.X, 2)
                                + Math.Pow(dabs[i].Pos.Y - previous[i].Pos.Y, 2));
                            if (moved > worstMove) worstMove = moved;
                            if (i == settledCut && moved > firstRestampMove) firstRestampMove = moved;
                        }

                        stableSoFar = settledCut;
                    }

                    previous = [.. dabs];
                }

                output.WriteLine(
                    $"  {size,5:0} {scatter,8:0.00} {tolerance,10:0.00}"
                    + $" {totalNew / (double)events,11:0.##}"
                    + $" {totalRestamped / (double)events,16:0.##}"
                    + $" {worstMove,12:0.###} px {firstRestampMove,15:0.###} px");
            }
        }

        output.WriteLine("");
        output.WriteLine("  a dab costs 48 us at size 70 and 1245 us at size 500");
        output.WriteLine("  (WhatOneEventCostsAtEachBrushSizeTests), and the pen delivers every ~5 ms");

        // **The claim: an event re-stamps more dabs than it adds.** If it did
        // not, the provisional tail would be a rounding error and the size cost
        // would have to be somewhere else entirely.
        var check = Soft(500);
        var checkDensify = new IncrementalDensify();
        var a = BrushEngine.WalkDabs(After(check, 10), checkDensify);
        var b = BrushEngine.WalkDabs(After(check, 11), checkDensify);
        var strictCut = BrushEngine.StableCount(b, a);
        var tolerantCut = BrushEngine.StableCount(b, a, BrushEngine.SettleTolerance(check));
        var reStamped = Math.Max(0, a.Count - strictCut);
        var stillReStamped = Math.Max(0, a.Count - tolerantCut);
        var added = b.Count - a.Count;

        output.WriteLine("");
        output.WriteLine(
            $"  one event at size 500: {added} dab(s) added, {reStamped} re-stamped under the "
            + $"exact rule and {stillReStamped} under the tolerance");

        Assert.True(
            reStamped >= added,
            $"an event adds {added} dabs and re-stamps only {reStamped}, so the provisional "
            + "tail is not the multiplier and the size cost is in the new dabs themselves — "
            + "which no amount of avoiding rework would help");

        Assert.True(
            stillReStamped < reStamped,
            $"the tolerance settles nothing extra ({stillReStamped} re-stamped against "
            + $"{reStamped}), so either the dabs move further than measured or something "
            + "other than position is holding the cut back — look at pressure and heading");

        // **The gate, and it is the half that must not regress.** A brush with a
        // dynamic seeded from its position gets no tolerance at all, because B45
        // measured a sub-pixel move throwing such a dab ten pixels away.
        Assert.Equal(0, BrushEngine.SettleTolerance(Soft(500, scatter: 0.35)));
        Assert.True(BrushEngine.SettleTolerance(Soft(500)) > 0);
    }
}
