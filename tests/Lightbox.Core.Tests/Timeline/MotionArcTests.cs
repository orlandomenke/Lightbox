using Lightbox.Core.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The arc fit and what it reads back (Q133): the fitted curve recovers the
/// arc the ticks sit on, collinear ticks degrade to a line rather than a
/// nonsense circle, an off-arc drawing is named with its foot on the arc, and
/// the predictions continue the artist's own spacing rather than inventing
/// timing. The interesting mistakes are the honest refusals — two ticks are a
/// chord, a stalled subject does not extrapolate.
/// </summary>
public class MotionArcTests(ITestOutputHelper output)
{
    /// <summary>A tick at (x, y): index in order, no flags, anchored.</summary>
    private static TrailPoint At(int index, double x, double y, bool current = false)
        => new(index, x, y, current, Before: !current && index < 3, Anchored: true, $"F{index}");

    /// <summary>Ticks on a known circle — centre (100, 100), radius 60.</summary>
    private static List<TrailPoint> OnCircle(params double[] degrees)
    {
        var points = new List<TrailPoint>();
        for (var i = 0; i < degrees.Length; i++)
        {
            var a = degrees[i] * Math.PI / 180;
            points.Add(At(i, 100 + 60 * Math.Cos(a), 100 + 60 * Math.Sin(a)));
        }
        return points;
    }

    [Fact]
    public void TheFitRecoversTheCircleTheTicksSitOn()
    {
        var arc = MotionArc.Fit(OnCircle(180, 150, 120, 90), playheadIndex: 1, predictNext: false);

        Assert.NotNull(arc);
        Assert.Empty(arc!.OffArc);
        // Every sample of the fitted path lies on the circle the ticks came from.
        var worst = 0.0;
        foreach (var s in arc.Path)
        {
            var r = Math.Sqrt((s.X - 100) * (s.X - 100) + (s.Y - 100) * (s.Y - 100));
            worst = Math.Max(worst, Math.Abs(r - 60));
        }
        output.WriteLine($"path samples {arc.Path.Count}, worst radial error {worst:F4} px");
        Assert.True(worst < 0.01, $"the fitted path strays {worst} px off the ticks' own circle");
        // And the path is a curve, not the chords — its middle bulges out to
        // the radius rather than cutting across.
        Assert.True(arc.Path.Count > 4, "a circle sampled as if it were a line");
    }

    [Fact]
    public void CollinearTicksFitALineNotANonsenseCircle()
    {
        // Straight motion at an angle, and one tick nudged off by less than
        // the tolerance so the matrix is not exactly singular either.
        var points = new List<TrailPoint>
        {
            At(0, 20, 30), At(1, 60, 50.4), At(2, 100, 70), At(3, 140, 90),
        };
        var arc = MotionArc.Fit(points, playheadIndex: 1, predictNext: true);

        Assert.NotNull(arc);
        // A line's path is straight: the midpoint of the sampled path sits on
        // the segment between its ends.
        var first = arc!.Path[0];
        var last = arc.Path[^1];
        var mid = arc.Path[arc.Path.Count / 2];
        var cross = Math.Abs((last.X - first.X) * (mid.Y - first.Y) - (last.Y - first.Y) * (mid.X - first.X))
            / Math.Sqrt((last.X - first.X) * (last.X - first.X) + (last.Y - first.Y) * (last.Y - first.Y));
        output.WriteLine($"midpoint off the chord by {cross:F4} px");
        Assert.True(cross < 0.5, $"collinear ticks produced a curved path ({cross} px bulge)");
        // The prediction continues along the same straight motion.
        Assert.NotNull(arc.Next);
        Assert.True(arc.Next!.Value.X > 140, "the prediction did not continue the motion");
    }

    [Fact]
    public void AnOffArcTickIsFlaggedWithItsFootOnTheArc()
    {
        // Five ticks on the circle, one pushed 14 px outward — well past the
        // 3% tolerance of a ~157 px quarter-arc.
        var points = OnCircle(180, 157.5, 135, 112.5, 90);
        var straying = points[2];
        points[2] = straying with { X = 100 + 74 * Math.Cos(135 * Math.PI / 180), Y = 100 + 74 * Math.Sin(135 * Math.PI / 180) };

        var arc = MotionArc.Fit(points, playheadIndex: 2, predictNext: false);

        Assert.NotNull(arc);
        var off = Assert.Single(arc!.OffArc);
        Assert.Equal("F2", off.FrameId);
        // The foot is on the fitted arc, between the tick and the centre —
        // the pull-line points where the drawing should move.
        var footR = Math.Sqrt((off.FootX - 100) * (off.FootX - 100) + (off.FootY - 100) * (off.FootY - 100));
        output.WriteLine($"foot at radius {footR:F2} (fit of 4 on-circle + 1 pushed tick)");
        Assert.True(Math.Abs(footR - 60) < 4, $"the foot is {footR:F1} px from centre, not near the arc's 60");
        // Sanity the other way: the on-arc ticks are not flagged, and the
        // flag is not "everything" — one drawing broke the arc, one is named.
    }

    [Fact]
    public void ThePredictedNextContinuesTheSpacingTrend()
    {
        // An ease-out on the circle: gaps of 40°, 30°, 22.5° — each ¾ of the
        // one before. The next gap should keep easing: 22.5° × ¾ ≈ 16.9°.
        var arc = MotionArc.Fit(OnCircle(180, 140, 110, 87.5), playheadIndex: 1, predictNext: true);

        Assert.NotNull(arc?.Next);
        var next = arc!.Next!.Value;
        var angle = Math.Atan2(next.Y - 100, next.X - 100) * 180 / Math.PI;
        var r = Math.Sqrt((next.X - 100) * (next.X - 100) + (next.Y - 100) * (next.Y - 100));
        output.WriteLine($"predicted at {angle:F1}°, radius {r:F2}");
        // On the arc, not beside it…
        Assert.True(Math.Abs(r - 60) < 0.5, $"the prediction left the arc (radius {r:F1})");
        // …and the gap eases on: strictly smaller than the last authored gap,
        // strictly larger than a bare repeat scaled by the clamp floor.
        Assert.InRange(87.5 - angle, 22.5 * 0.5, 22.4);
        // The dashed extension runs from the last tick to the prediction.
        Assert.True(arc.Extension.Count >= 2, "no extension to dash");
    }

    [Fact]
    public void AnEmptyCurrentCelGetsAPredictionBetweenItsNeighbours()
    {
        // Ticks at indices 0,1,2 then 5 — the playhead stands on 3, an empty
        // cel two fifths of the way through the 2→5 gap. No tick is Current.
        var points = OnCircle(180, 160, 140, 80);
        points[3] = points[3] with { Index = 5 };

        var arc = MotionArc.Fit(points, playheadIndex: 3, predictNext: false);

        Assert.NotNull(arc?.Current);
        var here = arc!.Current!.Value;
        var angle = Math.Atan2(here.Y - 100, here.X - 100) * 180 / Math.PI;
        output.WriteLine($"empty cel predicted at {angle:F1}° (neighbours at 140° and 80°, a third along)");
        // A third of the 140→80 sweep: 120°, on the arc.
        Assert.True(Math.Abs(angle - 120) < 0.5, $"the inbetween landed at {angle:F1}°, not 120°");
        var r = Math.Sqrt((here.X - 100) * (here.X - 100) + (here.Y - 100) * (here.Y - 100));
        Assert.True(Math.Abs(r - 60) < 0.5, "the inbetween prediction left the arc");

        // A playhead standing on a real tick predicts nothing for it.
        var onTick = OnCircle(180, 150, 120);
        onTick[1] = onTick[1] with { Current = true };
        Assert.Null(MotionArc.Fit(onTick, playheadIndex: 1, predictNext: false)!.Current);
    }

    [Fact]
    public void TwoTicksAreNotAnArc()
    {
        var points = new List<TrailPoint> { At(0, 20, 30), At(1, 60, 50) };
        Assert.Null(MotionArc.Fit(points, playheadIndex: 0, predictNext: true));
        // And a subject that never moved has no arc however many ticks it has.
        var still = new List<TrailPoint> { At(0, 50, 50), At(1, 50, 50), At(2, 50, 50) };
        Assert.Null(MotionArc.Fit(still, playheadIndex: 1, predictNext: true));
    }

    [Fact]
    public void AStalledSubjectPredictsNothing()
    {
        // Motion that arrives and stops: the last two ticks coincide. Where
        // it goes next is a timing decision, not an extrapolation.
        var points = new List<TrailPoint> { At(0, 20, 30), At(1, 60, 50), At(2, 100, 70), At(3, 100, 70) };
        var arc = MotionArc.Fit(points, playheadIndex: 1, predictNext: true);

        Assert.NotNull(arc);
        Assert.Null(arc!.Next);
        Assert.Empty(arc.Extension);
    }
}
