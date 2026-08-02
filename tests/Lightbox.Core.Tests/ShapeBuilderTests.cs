using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests;

/// <summary>
/// Shapes, as strokes.
/// </summary>
/// <remarks>
/// A rectangle is a polyline round a rectangle, stamped with whatever brush is
/// loaded — one pixel path, so a shape erases, re-renders and inbetweens like
/// every other mark. These are about the geometry being right and, more
/// importantly, being the same every time.
/// </remarks>
public class ShapeBuilderTests
{
    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) < 0.001, $"expected {expected}, got {actual}");

    [Fact]
    public void ALineIsItsTwoEnds()
    {
        var points = ShapeBuilder.Outline(ShapeKind.Line, 10, 20, 90, 60);

        Assert.Equal(2, points.Count);
        Near(10, points[0].X);
        Near(60, points[1].Y);
    }

    [Fact]
    public void AClosedShapeRepeatsItsFirstPoint()
    {
        // A stroke is stamped along its points, so without the repeat the last
        // corner has a notch in it — visible on anything but the hardest round
        // tip, and the kind of thing that only shows on the drawing.
        var rect = ShapeBuilder.Outline(ShapeKind.Rectangle, 0, 0, 40, 20);

        Assert.Equal(5, rect.Count);
        Near(rect[0].X, rect[^1].X);
        Near(rect[0].Y, rect[^1].Y);
    }

    [Fact]
    public void ARectangleIsItsFourCornersWhicheverWayYouDragged()
    {
        var forwards = ShapeBuilder.Outline(ShapeKind.Rectangle, 0, 0, 40, 20);
        var backwards = ShapeBuilder.Outline(ShapeKind.Rectangle, 40, 20, 0, 0);

        Assert.Equal(
            forwards.Select(p => (p.X, p.Y)),
            backwards.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void DraggingFromTheCentreGrowsBothWays()
    {
        var rect = ShapeBuilder.Outline(ShapeKind.Rectangle, 50, 50, 70, 60, fromCentre: true);

        Near(30, rect.Min(p => p.X));
        Near(70, rect.Max(p => p.X));
        Near(40, rect.Min(p => p.Y));
        Near(60, rect.Max(p => p.Y));
    }

    [Fact]
    public void HoldingItRegularSquaresItUp()
    {
        var square = ShapeBuilder.Outline(ShapeKind.Rectangle, 0, 0, 40, 12, regular: true);

        Near(40, square.Max(p => p.X));
        Near(40, square.Max(p => p.Y));
    }

    [Fact]
    public void AnEllipseFitsItsBoxAndCloses()
    {
        var ellipse = ShapeBuilder.Outline(ShapeKind.Ellipse, 0, 0, 100, 50);

        Near(0, ellipse.Min(p => p.X));
        Near(100, ellipse.Max(p => p.X));
        Near(0, ellipse.Min(p => p.Y));
        Near(50, ellipse.Max(p => p.Y));
        Near(ellipse[0].X, ellipse[^1].X);
        Near(ellipse[0].Y, ellipse[^1].Y);
    }

    [Fact]
    public void ABigEllipseGetsMoreSegmentsThanASmallOne()
    {
        // A small circle should not be over-tessellated and a large one should
        // not be a polygon.
        var small = ShapeBuilder.Outline(ShapeKind.Ellipse, 0, 0, 12, 12);
        var large = ShapeBuilder.Outline(ShapeKind.Ellipse, 0, 0, 2000, 2000);

        Assert.True(large.Count > small.Count);
        Assert.True(small.Count >= 25);      // the floor, plus the closing point
        Assert.True(large.Count <= 513);     // and the ceiling
    }

    [Fact]
    public void APolygonStartsAtTheTop()
    {
        // Which is what makes a pentagon look like a pentagon rather than a
        // rotated one.
        var pentagon = ShapeBuilder.Outline(ShapeKind.Polygon, 0, 0, 100, 100, sides: 5);

        Assert.Equal(6, pentagon.Count);
        Near(50, pentagon[0].X);
        Near(0, pentagon[0].Y);
    }

    [Fact]
    public void APolygonCannotHaveFewerThanThreeSides()
    {
        var degenerate = ShapeBuilder.Outline(ShapeKind.Polygon, 0, 0, 100, 100, sides: 1);

        Assert.Equal(4, degenerate.Count);   // a triangle, closed
    }

    [Fact]
    public void TheSameCornersAlwaysGiveTheSamePoints()
    {
        // A shape that re-tessellated differently on reload would be a
        // different mark — invariant 2 seen from the geometry side.
        var first = ShapeBuilder.Outline(ShapeKind.Ellipse, 3.5, -7.25, 190.75, 44);
        var second = ShapeBuilder.Outline(ShapeKind.Ellipse, 3.5, -7.25, 190.75, 44);

        Assert.Equal(
            first.Select(p => (p.X, p.Y, p.Pressure)),
            second.Select(p => (p.X, p.Y, p.Pressure)));
    }

    [Fact]
    public void EveryPointIsAtFullPressure()
    {
        // A shape is not a gesture: there is no hand travelling along the edge
        // to have a pressure, and tapering it from the drag would put the thin
        // part wherever the artist happened to release.
        var rect = ShapeBuilder.Outline(ShapeKind.Rectangle, 0, 0, 40, 20);

        Assert.All(rect, p => Near(1, p.Pressure));
    }
}
