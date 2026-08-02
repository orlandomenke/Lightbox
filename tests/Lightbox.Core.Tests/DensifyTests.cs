using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests;

/// <summary>
/// Filling in the curve between recorded points, so a fast stroke is walked as
/// the arc it was drawn as rather than the chords the pen happened to sample.
/// </summary>
/// <remarks>
/// The geometry half of M16b (stamping arcs). A pen reports at a fixed rate,
/// so a quick bend arrives as a handful of widely spaced points; walking
/// straight lines between them puts every dab inside the curve, and on a thick
/// brush the resulting facet is wider than the line and reads as the tops of
/// the individual stamps showing through the outside of the bend.
/// </remarks>
public class DensifyTests
{
    private const double R = 180, Cx = 300, Cy = 300;

    /// <summary>Points on a circle, <paramref name="stepDeg"/> apart — a stroke drawn at some speed.</summary>
    private static List<StrokePoint> Arc(double stepDeg, double from = 200, double to = 340)
    {
        var pts = new List<StrokePoint>();
        for (var a = from; a <= to; a += stepDeg)
        {
            var rad = a * Math.PI / 180;
            pts.Add(new StrokePoint(Cx + Math.Cos(rad) * R, Cy + Math.Sin(rad) * R, 1));
        }
        return pts;
    }

    /// <summary>Worst distance from the true circle, sampled along every segment of a polyline.</summary>
    private static double WorstRadialError(IReadOnlyList<StrokePoint> path, int skipEnds = 0)
    {
        double worst = 0;
        for (var i = skipEnds; i + 1 < path.Count - skipEnds; i++)
        {
            for (double t = 0; t <= 1; t += 0.05)
            {
                var x = path[i].X + (path[i + 1].X - path[i].X) * t;
                var y = path[i].Y + (path[i + 1].Y - path[i].Y) * t;
                var r = Math.Sqrt((x - Cx) * (x - Cx) + (y - Cy) * (y - Cy));
                worst = Math.Max(worst, Math.Abs(r - R));
            }
        }
        return worst;
    }

    [Fact]
    public void ACurveIsFollowedRatherThanCutAcross()
    {
        // The headline. Sampled every 20°, the raw chords sit 2.7 px inside a
        // 180 px arc — wider than the soft edge of any brush, and half the
        // width of a thin one. Through the middle of the densified path, where
        // every span has a real neighbour on both sides, that is gone.
        var raw = Arc(20);

        var dense = GeometryOps.Densify(raw);

        Assert.True(WorstRadialError(raw) > 2, $"the raw chords were only {WorstRadialError(raw):0.00} px off — pick a coarser sample");
        var interior = WorstRadialError(dense, dense.Count / 5);
        Assert.True(interior < 0.2, $"the densified path was still {interior:0.00} px off the curve");
    }

    [Fact]
    public void TheEndsAreTheOnePlaceItCannotHelp()
    {
        // Written down rather than left as a surprise: the first and last span
        // have no neighbour beyond them, so nothing says how the curve was
        // continuing and they run straighter. It is the correct answer — the
        // record genuinely does not contain that information — and it is why
        // the test above measures the interior.
        var dense = GeometryOps.Densify(Arc(30));

        Assert.True(WorstRadialError(dense) > WorstRadialError(dense, dense.Count / 3));
    }

    [Fact]
    public void EveryRecordedPointIsStillOnThePath()
    {
        // The record says where the pen was. This decides only what happened
        // between two of those places, and must never move one of them.
        var raw = Arc(20);

        var dense = GeometryOps.Densify(raw);

        foreach (var p in raw)
        {
            Assert.Contains(dense, d =>
                Math.Abs(d.X - p.X) < 1e-9 && Math.Abs(d.Y - p.Y) < 1e-9);
        }
    }

    [Fact]
    public void ADrawnCornerStaysSharp()
    {
        // A rectangle is four long spans and four right angles, and rounding
        // them would be this feature quietly eating the shape tools. The turn
        // threshold is what separates a corner the artist meant from a bend
        // the pen under-sampled.
        List<StrokePoint> box =
        [
            new(100, 100, 1), new(300, 100, 1), new(300, 260, 1), new(100, 260, 1), new(100, 100, 1),
        ];

        var dense = GeometryOps.Densify(box);

        // Nothing may stray outside the box the corners define.
        foreach (var p in dense)
        {
            Assert.InRange(p.X, 99.99, 300.01);
            Assert.InRange(p.Y, 99.99, 260.01);
        }
        // And the corner itself is still a corner: a point right at it.
        Assert.Contains(dense, p => Math.Abs(p.X - 300) < 1e-9 && Math.Abs(p.Y - 100) < 1e-9);
    }

    [Fact]
    public void AStraightLineIsNotBent()
    {
        List<StrokePoint> line = [new(0, 50, 1), new(100, 50, 1), new(200, 50, 1), new(300, 50, 1)];

        var dense = GeometryOps.Densify(line);

        Assert.All(dense, p => Assert.Equal(50, p.Y, 9));
    }

    [Fact]
    public void PressureRidesTheSameCurve()
    {
        // A taper is a curve in pressure as much as in position, and
        // interpolating one smoothly while stepping the other would put a
        // visible ledge in the width of the line.
        List<StrokePoint> line =
        [
            new(0, 50, 1.0), new(100, 50, 0.7), new(200, 50, 0.4), new(300, 50, 0.1),
        ];

        var dense = GeometryOps.Densify(line);

        for (var i = 1; i < dense.Count; i++)
        {
            Assert.True(dense[i].Pressure <= dense[i - 1].Pressure + 1e-9,
                $"pressure went back up at {i}: {dense[i - 1].Pressure} -> {dense[i].Pressure}");
        }
        Assert.Equal(0.1, dense[^1].Pressure, 9);
    }

    [Fact]
    public void ATwoPointStrokeIsLeftExactlyAsItIs()
    {
        // Two points are a straight line by definition and there is no curve
        // to infer. Also the guard that keeps a tap from allocating.
        List<StrokePoint> tap = [new(10, 10, 1), new(90, 40, 0.5)];

        Assert.Equal(tap, GeometryOps.Densify(tap));
    }

    [Fact]
    public void PointsAlreadyCloserThanTheChordAreNotMultiplied()
    {
        // A slow, carefully drawn stroke arrives dense. Subdividing it further
        // would be pure cost for a path that is already smooth — and this runs
        // on every pointer event.
        var slow = new List<StrokePoint>();
        // Steps well under the 2 px chord in both axes, so no span qualifies.
        for (var i = 0; i < 200; i++) slow.Add(new StrokePoint(i * 1.0, 50 + Math.Sin(i / 40.0) * 20, 1));

        var dense = GeometryOps.Densify(slow);

        // The same list, not an equal one: this is the live-painting path, and
        // copying every point of the stroke on every pointer event is the
        // whole cost worth avoiding here.
        Assert.Same(slow, dense);
    }

    [Fact]
    public void AStalledPenDoesNotBreakTheCurve()
    {
        // A pen held still reports the same point twice. The tangent there is
        // undefined, and treating a zero-length step as a corner would snap
        // the curve straight every time the artist paused.
        List<StrokePoint> stalled =
        [
            new(0, 100, 1), new(100, 40, 1), new(100, 40, 1), new(200, 100, 1),
        ];

        var dense = GeometryOps.Densify(stalled);

        Assert.All(dense, p =>
        {
            Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y), $"produced ({p.X},{p.Y})");
        });
        Assert.True(dense.Count > stalled.Count);
    }

    [Fact]
    public void TheSamePointsAlwaysGiveTheSamePath()
    {
        // Invariant 2. Dab positions seed every jitter through Hash01, so a
        // path that varied between runs would make re-renders and AI
        // inbetweens diverge.
        var raw = Arc(17, 5, 300);

        var a = GeometryOps.Densify(raw);
        var b = GeometryOps.Densify(raw);

        Assert.Equal(a, b);
    }
}
