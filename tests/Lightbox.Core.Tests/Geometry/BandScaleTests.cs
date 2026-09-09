using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Geometry;

/// <summary>
/// Band scaling (Q184): dividers redistribute space between adjacent bands and
/// the outer bounds never move.
/// </summary>
public class BandScaleTests(ITestOutputHelper output)
{
    private static BandScale.Axis Axis(double start, double end, params double[] dividers) =>
        new(start, end, dividers, dividers);

    /// <summary>
    /// <b>The promise the whole feature rests on: the box does not move.</b>
    /// </summary>
    /// <remarks>
    /// The owner's words were "the boundaries of the box are never crossed", and
    /// it is what makes this different from a scale — the figure stays the height
    /// it was. Asserted at the bounds and just inside them, because a map that
    /// pinned only the exact edges would still let the drawing leak past them.
    /// </remarks>
    [Fact]
    public void TheOuterBoundsNeverMove()
    {
        var y = BandScale.Drag(Axis(100, 500, 300), 0, 200);

        Assert.Equal(100, BandScale.MapCoordinate(y, 100), 6);
        Assert.Equal(500, BandScale.MapCoordinate(y, 500), 6);
        // Nothing inside is mapped outside either.
        for (var v = 100.0; v <= 500; v += 7.3)
        {
            var mapped = BandScale.MapCoordinate(y, v);
            Assert.InRange(mapped, 100, 500);
        }
    }

    /// <summary>
    /// <b>The legs example, as arithmetic.</b> Hip at 300 in a box from 100 to
    /// 500; drag it to 200 and the legs get the space the torso gives up.
    /// </summary>
    [Fact]
    public void DraggingTheHipLineUpLengthensTheLegsAndShortensTheTorso()
    {
        var y = BandScale.Drag(Axis(100, 500, 300), 0, 200);

        // Torso: 100..300 becomes 100..200, so a half-scale. Its midpoint follows.
        Assert.Equal(150, BandScale.MapCoordinate(y, 200), 6);
        // Legs: 300..500 becomes 200..500, a 1.5x stretch. The knee at 400
        // (halfway down the legs) lands halfway down the new legs, at 350.
        Assert.Equal(350, BandScale.MapCoordinate(y, 400), 6);
        Assert.Equal(200, BandScale.MapCoordinate(y, 300), 6);

        output.WriteLine($"torso scale {BandScale.BandScaleAt(y, 200):0.00}x");
        output.WriteLine($"leg scale   {BandScale.BandScaleAt(y, 400):0.00}x");
        Assert.Equal(0.5, BandScale.BandScaleAt(y, 200), 6);
        Assert.Equal(1.5, BandScale.BandScaleAt(y, 400), 6);
    }

    /// <summary>
    /// <b>Only the two adjacent bands change — the decision in Q184.</b>
    /// </summary>
    /// <remarks>
    /// Three bands, drag the lower divider, and the top band must be untouched.
    /// This is the assertion that fails if anyone later makes the change spread
    /// proportionally across a side, which was the rejected alternative.
    /// </remarks>
    [Fact]
    public void ABandYouDidNotTouchDoesNotMove()
    {
        var y = Axis(0, 300, 100, 200);
        var dragged = BandScale.Drag(y, 1, 250);

        // The top band, 0..100, is bounded by a divider that did not move.
        for (var v = 0.0; v <= 100; v += 5)
        {
            Assert.Equal(v, BandScale.MapCoordinate(dragged, v), 6);
        }
        Assert.Equal(1, BandScale.BandScaleAt(dragged, 50), 6);

        // The middle stretched and the bottom compressed.
        Assert.True(BandScale.BandScaleAt(dragged, 150) > 1);
        Assert.True(BandScale.BandScaleAt(dragged, 250) < 1);
        output.WriteLine(
            $"top {BandScale.BandScaleAt(dragged, 50):0.00}x, "
            + $"middle {BandScale.BandScaleAt(dragged, 150):0.00}x, "
            + $"bottom {BandScale.BandScaleAt(dragged, 250):0.00}x");
    }

    /// <summary>A divider cannot be dragged past its neighbours or out of the box.</summary>
    [Fact]
    public void ADividerCannotCrossItsNeighbours()
    {
        // Box 0..300, dividers at 100 and 200. Each divider is penned in by
        // whatever is on either side of IT — the divider next door, or the box
        // edge when there is no divider next door.
        var y = Axis(0, 300, 100, 200);

        // The lower divider is held by the box floor and by the upper divider.
        Assert.Equal(BandScale.MinimumBand, BandScale.ClampDivider(y, 0, -9999), 6);
        Assert.Equal(200 - BandScale.MinimumBand, BandScale.ClampDivider(y, 0, 9999), 6);

        // The upper one is held by the lower divider and by the box ceiling —
        // not by its own position, which is the reading that got this wrong the
        // first time round.
        Assert.Equal(100 + BandScale.MinimumBand, BandScale.ClampDivider(y, 1, -9999), 6);
        Assert.Equal(300 - BandScale.MinimumBand, BandScale.ClampDivider(y, 1, 9999), 6);

        // And the clamp is what Drag applies, not merely what it could report.
        var squashed = BandScale.Drag(y, 0, -9999);
        Assert.Equal(BandScale.MinimumBand, squashed.Moved[0], 6);
        Assert.True(squashed.Moved[0] < squashed.Moved[1], "the order survived the drag");
    }

    /// <summary>
    /// <b>A drag is reversible until it is committed.</b>
    /// </summary>
    /// <remarks>
    /// The source dividers stay where they were placed, so dragging out and back
    /// is the identity rather than an accumulation. Get this wrong and a hesitant
    /// artist destroys the drawing by fidgeting.
    /// </remarks>
    [Fact]
    public void DraggingOutAndBackIsTheIdentity()
    {
        var y = Axis(100, 500, 300);
        var there = BandScale.Drag(y, 0, 180);
        var back = BandScale.Drag(there, 0, 300);

        for (var v = 100.0; v <= 500; v += 11)
        {
            Assert.Equal(v, BandScale.MapCoordinate(back, v), 6);
        }
        Assert.False(back.Moves);
    }

    /// <summary>Placing a line changes nothing until it is dragged.</summary>
    [Fact]
    public void AddingADividerIsNotAnEdit()
    {
        var y = BandScale.Add(BandScale.Axis.None(0, 400), 250);

        Assert.Single(y.Moved);
        Assert.False(y.Moves);
        Assert.Equal(123, BandScale.MapCoordinate(y, 123), 6);
    }

    /// <summary>Both axes at once, and neither disturbs the other.</summary>
    [Fact]
    public void TheTwoAxesAreIndependent()
    {
        var x = BandScale.Drag(Axis(0, 200, 100), 0, 150);
        var y = BandScale.Drag(Axis(0, 400, 200), 0, 100);
        var map = BandScale.Map(x, y);

        // The X result is what the X axis alone says, whatever Y is doing.
        var (mx, my) = map(50, 300);
        Assert.Equal(BandScale.MapCoordinate(x, 50), mx, 6);
        Assert.Equal(BandScale.MapCoordinate(y, 300), my, 6);
        output.WriteLine($"(50,300) -> ({mx:0.0},{my:0.0})");
    }

    /// <summary>Anything outside the box is left exactly where it was.</summary>
    /// <remarks>
    /// The same promise a region-limited transform already makes: a stroke
    /// reaching past the box keeps the part that is outside.
    /// </remarks>
    [Fact]
    public void OutsideTheBoxNothingMoves()
    {
        var y = BandScale.Drag(Axis(100, 500, 300), 0, 200);

        Assert.Equal(20, BandScale.MapCoordinate(y, 20), 6);
        Assert.Equal(900, BandScale.MapCoordinate(y, 900), 6);
    }

    // ---- the point insertion, which is the part that is not a preference ----

    private static Stroke Diagonal(params (double X, double Y, double P)[] pts) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Brush = new BrushSettings { Size = 10 },
        Points = [.. pts.Select(p => new StrokePoint(p.X, p.Y, p.P))],
    };

    /// <summary>
    /// <b>A point lands exactly on the divider a segment crosses.</b>
    /// </summary>
    /// <remarks>
    /// Without this the kink appears at the nearest existing sample instead of
    /// at the line the artist dragged. The stroke here is deliberately sampled
    /// as coarsely as a fast diagonal really is — two points, 400 px apart —
    /// because that is the case where the error is largest.
    /// </remarks>
    [Fact]
    public void APointIsInsertedWhereAStrokeCrossesADivider()
    {
        var stroke = Diagonal((0, 0, 1), (400, 400, 1));
        var y = Axis(0, 400, 300);

        var added = BandScale.Insert(stroke, BandScale.Axis.None(0, 400), y);

        Assert.Equal(1, added);
        Assert.Equal(3, stroke.Points.Count);
        var inserted = stroke.Points[1];
        output.WriteLine($"inserted at ({inserted.X:0.0}, {inserted.Y:0.0})");
        Assert.Equal(300, inserted.Y, 6);
        Assert.Equal(300, inserted.X, 6);
    }

    /// <summary>
    /// The inserted point's pressure is the lerp of its neighbours', not a copy.
    /// </summary>
    /// <remarks>
    /// Pressure drives width, so copying a neighbour's value would put a step in
    /// the middle of a line that was tapering smoothly — visible as a nick in
    /// the stroke exactly where the divider is.
    /// </remarks>
    [Fact]
    public void AnInsertedPointInterpolatesPressure()
    {
        var stroke = Diagonal((0, 0, 0.2), (0, 400, 1.0));
        var y = Axis(0, 400, 100);

        BandScale.Insert(stroke, BandScale.Axis.None(0, 400), y);

        var inserted = stroke.Points[1];
        output.WriteLine($"pressure at the crossing {inserted.Pressure:0.000}");
        // A quarter of the way along: 0.2 + 0.25 * 0.8.
        Assert.Equal(0.4, inserted.Pressure, 6);
    }

    /// <summary>Crossing several dividers inserts one point each, in order.</summary>
    [Fact]
    public void EveryCrossingGetsItsOwnPoint()
    {
        var stroke = Diagonal((0, 0, 1), (400, 400, 1));

        var added = BandScale.Insert(stroke, Axis(0, 400, 50, 150), Axis(0, 400, 250, 350));

        Assert.Equal(4, added);
        output.WriteLine(string.Join(", ", stroke.Points.Select(p => $"({p.X:0},{p.Y:0})")));
        // Ascending along the segment, which is what the map needs.
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            Assert.True(
                stroke.Points[i].X >= stroke.Points[i - 1].X,
                "inserted points must keep the stroke's order");
        }
    }

    /// <summary>
    /// Two dividers crossed at the same place get one point, not two.
    /// </summary>
    /// <remarks>
    /// A diagonal through the corner of the grid hits an X divider and a Y
    /// divider at the same parameter. Two coincident points is a zero-length
    /// segment, which is a dab of nothing and a division by zero waiting for
    /// whatever next measures direction along the stroke.
    /// </remarks>
    [Fact]
    public void ACornerCrossingInsertsOnePoint()
    {
        var stroke = Diagonal((0, 0, 1), (400, 400, 1));

        var added = BandScale.Insert(stroke, Axis(0, 400, 200), Axis(0, 400, 200));

        Assert.Equal(1, added);
        Assert.Equal(3, stroke.Points.Count);
        Assert.Equal(200, stroke.Points[1].X, 6);
        Assert.Equal(200, stroke.Points[1].Y, 6);
    }

    /// <summary>A stroke that crosses nothing is left completely alone.</summary>
    [Fact]
    public void AStrokeThatCrossesNothingIsUntouched()
    {
        var stroke = Diagonal((10, 10, 1), (20, 20, 1));
        var before = stroke.Points.ToList();

        var added = BandScale.Insert(stroke, BandScale.Axis.None(0, 400), Axis(0, 400, 300));

        Assert.Equal(0, added);
        Assert.Equal(before, stroke.Points);
    }

    /// <summary>
    /// <b>Insertion then mapping puts the bend on the divider, and that is the
    /// whole point of insertion.</b>
    /// </summary>
    /// <remarks>
    /// The measurement that justifies the work: map the coarse diagonal with and
    /// without inserting, and compare each against a densely sampled version of
    /// the same line put through the same map. Without insertion the rendered
    /// line departs from the truth by tens of pixels; with it, it does not
    /// depart at all.
    /// </remarks>
    [Fact]
    public void InsertingIsWhatKeepsTheBendOnTheDivider()
    {
        var y = BandScale.Drag(Axis(0, 400, 300), 0, 150);
        var map = BandScale.Map(BandScale.Axis.None(0, 400), y);

        // The truth: the same line sampled every pixel, mapped point by point.
        var truth = new List<(double X, double Y)>();
        for (var i = 0; i <= 400; i++) truth.Add(map(i, i));

        double WorstError(Stroke s)
        {
            var worst = 0.0;
            foreach (var (tx, ty) in truth)
            {
                // How far the polyline through s's mapped points sits from the
                // true point at the same x.
                var at = SampleAtX(s.Points, tx);
                if (at is { } y2) worst = Math.Max(worst, Math.Abs(y2 - ty));
            }
            return worst;
        }

        var coarse = Diagonal((0, 0, 1), (400, 400, 1));
        MapPoints(coarse, map);
        var withoutInsert = WorstError(coarse);

        var inserted = Diagonal((0, 0, 1), (400, 400, 1));
        BandScale.Insert(inserted, BandScale.Axis.None(0, 400), y);
        MapPoints(inserted, map);
        var withInsert = WorstError(inserted);

        output.WriteLine($"worst departure from the true line — without inserting {withoutInsert:0.00} px");
        output.WriteLine($"worst departure from the true line — with inserting    {withInsert:0.00} px");

        Assert.True(withoutInsert > 20, $"expected a large error to fix, saw {withoutInsert:0.00} px");
        Assert.True(withInsert < 0.001, $"inserting should be exact, saw {withInsert:0.00} px");
    }

    private static void MapPoints(Stroke stroke, TransformOps.PointMap map)
    {
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var p = stroke.Points[i];
            var (mx, my) = map(p.X, p.Y);
            stroke.Points[i] = p with { X = mx, Y = my };
        }
    }

    /// <summary>Where a polyline sits at a given x, or null if it does not reach it.</summary>
    private static double? SampleAtX(List<StrokePoint> pts, double x)
    {
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            if (x < Math.Min(a.X, b.X) - 1e-9 || x > Math.Max(a.X, b.X) + 1e-9) continue;
            var dx = b.X - a.X;
            if (Math.Abs(dx) < 1e-12) return a.Y;
            var t = (x - a.X) / dx;
            return a.Y + (b.Y - a.Y) * t;
        }
        return null;
    }
}
