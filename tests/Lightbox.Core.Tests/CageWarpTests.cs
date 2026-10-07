using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The cage warp's geometry (Q199): a lattice that is the identity until a
/// point is dragged, bends smoothly and locally when one is, inserts the
/// points a straight line needs to bend, and tessellates into the mesh the
/// preview and the baseline resample share.
/// </summary>
public class CageWarpTests(ITestOutputHelper output)
{
    private static CageWarp.Lattice Box(int grid = 3) => CageWarp.Lattice.Identity(100, 100, 400, 300, grid, grid);

    [Fact]
    public void AnUntouchedLatticeIsTheIdentityToTheLastBit()
    {
        var lattice = Box();
        Assert.False(lattice.Moves);
        var map = CageWarp.Map(lattice);
        foreach (var (x, y) in new[] { (100.0, 100.0), (400.0, 300.0), (137.5, 212.25), (250, 200), (399.9, 100.1) })
        {
            var (mx, my) = CageWarp.MapPoint(lattice, x, y);
            Assert.Equal(x, mx, 9);
            Assert.Equal(y, my, 9);
            var (fx, fy) = map(x, y);
            Assert.Equal(x, fx, 12);
            Assert.Equal(y, fy, 12);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void TheGridSizeIsHonouredAndClamped(int grid)
    {
        var lattice = CageWarp.Lattice.Identity(0, 0, 100, 100, grid, grid);
        Assert.Equal((grid + 1) * (grid + 1), lattice.Points.Count);
        var tooMany = CageWarp.Lattice.Identity(0, 0, 100, 100, 99, 0);
        Assert.Equal(CageWarp.MaxGrid, tooMany.Cols);
        Assert.Equal(CageWarp.MinGrid, tooMany.Rows);
    }

    /// <summary>
    /// The owner's gesture: drag the middle and the middle moves; the corners
    /// of the box, two cells away, stay exactly where they are; and outside
    /// the box nothing moves at all.
    /// </summary>
    [Fact]
    public void DraggingTheCentreMovesTheCentreAndNotTheCorners()
    {
        var lattice = Box(2);                                   // 3×3 points; index 4 is the centre
        var centre = lattice.IndexOf(1, 1);
        Assert.Equal((250, 200), lattice.Points[centre]);
        lattice = CageWarp.Drag(lattice, centre, 280, 230);
        Assert.True(lattice.Moves);

        var (cx, cy) = CageWarp.MapPoint(lattice, 250, 200);
        Assert.Equal(280, cx, 9);
        Assert.Equal(230, cy, 9);
        // A control point is interpolated through, so the corners are pinned.
        foreach (var (x, y) in new[] { (100.0, 100.0), (400.0, 100.0), (100.0, 300.0), (400.0, 300.0) })
        {
            var (mx, my) = CageWarp.MapPoint(lattice, x, y);
            Assert.Equal(x, mx, 9);
            Assert.Equal(y, my, 9);
        }
        // Outside the box: untouched.
        Assert.Equal((50, 50), CageWarp.MapPoint(lattice, 50, 50));
        Assert.Equal((450, 320), CageWarp.MapPoint(lattice, 450, 320));
        // Between: moved, but less than the centre.
        var (hx, hy) = CageWarp.MapPoint(lattice, 175, 150);
        var pull = Math.Sqrt((hx - 175) * (hx - 175) + (hy - 150) * (hy - 150));
        output.WriteLine($"halfway to the corner the pull is {pull:F2} px against 42.43 at the centre");
        Assert.InRange(pull, 1, 40);
    }

    /// <summary>
    /// A corner dragged alone bends only the cells that touch it: the far
    /// half of a 4×4 lattice does not move, which is what makes the cage
    /// local enough to adjust one limb.
    /// </summary>
    [Fact]
    public void ACornerDragStaysLocal()
    {
        var lattice = Box(4);
        lattice = CageWarp.Drag(lattice, lattice.IndexOf(0, 0), 60, 60);
        // Two cells away and further, nothing: the Catmull-Rom support is two
        // control points either side.
        var (x, y) = CageWarp.MapPoint(lattice, 300, 250);
        Assert.Equal(300, x, 9);
        Assert.Equal(250, y, 9);
        // Next to it, something.
        var (nx, ny) = CageWarp.MapPoint(lattice, 120, 120);
        Assert.NotEqual(120, nx, 3);
        Assert.NotEqual(120, ny, 3);
    }

    /// <summary>The map is continuous across a cell boundary — no seam where the cells meet.</summary>
    [Fact]
    public void TheMapIsContinuousAcrossCellBoundaries()
    {
        var lattice = Box(3);
        lattice = CageWarp.Drag(lattice, lattice.IndexOf(1, 1), 230, 190);
        lattice = CageWarp.Drag(lattice, lattice.IndexOf(2, 2), 320, 240);
        // The boundary between cells 0 and 1 is at x = 200.
        var (lx, ly) = CageWarp.MapPoint(lattice, 199.9999, 180);
        var (rx, ry) = CageWarp.MapPoint(lattice, 200.0001, 180);
        Assert.Equal(lx, rx, 2);
        Assert.Equal(ly, ry, 2);
    }

    [Fact]
    public void ADragOffTheGridOrToNowhereChangesNothing()
    {
        var lattice = Box();
        Assert.Equal(lattice, CageWarp.Drag(lattice, -1, 0, 0));
        Assert.Equal(lattice, CageWarp.Drag(lattice, 99, 0, 0));
        Assert.Equal(lattice, CageWarp.Drag(lattice, 0, double.NaN, 0));
        Assert.False(CageWarp.Drag(lattice, 0, double.PositiveInfinity, 1).Moves);
    }

    /// <summary>
    /// A two-point line across the box gains the points it needs to bend,
    /// every one of them interpolated; a segment outside the box gains none.
    /// </summary>
    [Fact]
    public void InsertDividesSegmentsInsideTheBoxAndLeavesTheRestAlone()
    {
        var lattice = Box(3);
        var inside = new Stroke
        {
            Points = [new StrokePoint(110, 200, 0.2), new StrokePoint(390, 200, 1.0)],
        };
        var added = CageWarp.Insert(inside, lattice);
        output.WriteLine($"a 280 px line across a {lattice.Cols}×{lattice.Rows} cage gained {added} points (chord {CageWarp.ChordFor(lattice):F1} px)");
        Assert.True(added >= 10);
        Assert.Equal(added + 2, inside.Points.Count);
        // Collinear, monotone, and pressure ramps between the ends.
        for (var i = 1; i < inside.Points.Count; i++)
        {
            Assert.Equal(200, inside.Points[i].Y, 9);
            Assert.True(inside.Points[i].X > inside.Points[i - 1].X);
            Assert.True(inside.Points[i].Pressure >= inside.Points[i - 1].Pressure);
        }

        var outside = new Stroke { Points = [new StrokePoint(10, 10, 1), new StrokePoint(90, 10, 1)] };
        Assert.Equal(0, CageWarp.Insert(outside, lattice));
        Assert.Equal(2, outside.Points.Count);
    }

    /// <summary>
    /// A long line that only passes through a small cage gains points along
    /// the part inside it and nowhere else — perf-warden's case, where the
    /// old bounding-box test divided 4000 px to a 4 px chord.
    /// </summary>
    [Fact]
    public void ALongLineThroughASmallCageGainsPointsOnlyInsideIt()
    {
        var lattice = CageWarp.Lattice.Identity(100, 100, 200, 200, 6, 6);     // chord ≈ 4.2 px
        var stroke = new Stroke { Points = [new StrokePoint(-2000, 150, 1), new StrokePoint(2000, 150, 1)] };

        var added = CageWarp.Insert(stroke, lattice);

        var chord = CageWarp.ChordFor(lattice);
        var inside = stroke.Points.Count(p => p.X >= 100 && p.X <= 200);
        output.WriteLine($"{added} points added for 100 px inside a {chord:F1} px chord; {inside} of them inside the box");
        Assert.InRange(added, (int)Math.Floor(100 / chord), (int)Math.Ceiling(100 / chord) + 2);
        // Every inserted point is inside the box (its edges included).
        Assert.Equal(added, inside);
        // And the entry and exit points sit exactly on the box's edges.
        Assert.Contains(stroke.Points, p => Math.Abs(p.X - 100) < 1e-9);
        Assert.Contains(stroke.Points, p => Math.Abs(p.X - 200) < 1e-9);
    }

    [Fact]
    public void ASegmentWhoseBoxOverlapsButWhichMissesTheCageGainsNothing()
    {
        var lattice = CageWarp.Lattice.Identity(100, 100, 200, 200, 3, 3);
        // A diagonal whose bounding box covers the cage but which passes above-left of it.
        var stroke = new Stroke { Points = [new StrokePoint(0, 150, 1), new StrokePoint(150, 0, 1)] };
        Assert.Equal(0, CageWarp.Insert(stroke, lattice));
        Assert.Equal(2, stroke.Points.Count);
    }

    /// <summary>Inserted, then mapped: the line now curves through the bulge rather than cutting across it.</summary>
    [Fact]
    public void AnInsertedLineFollowsTheWarpBetweenItsEnds()
    {
        var lattice = Box(2);
        lattice = CageWarp.Drag(lattice, lattice.IndexOf(1, 1), 250, 240);   // centre pushed down 40
        var stroke = new Stroke { Points = [new StrokePoint(100, 200, 1), new StrokePoint(400, 200, 1)] };
        CageWarp.Insert(stroke, lattice);
        TransformOps.TransformStroke(stroke, CageWarp.Map(lattice));

        var lowest = stroke.Points.Max(p => p.Y);
        output.WriteLine($"the line's middle now sits at y={lowest:F1} (ends at 200, centre pushed to 240)");
        Assert.Equal(200, stroke.Points[0].Y, 9);
        Assert.Equal(200, stroke.Points[^1].Y, 9);
        Assert.InRange(lowest, 235, 240.0001);
    }

    [Fact]
    public void TheMeshCoversTheBoxAndLandsWhereTheMapSays()
    {
        var lattice = Box(3);
        lattice = CageWarp.Drag(lattice, lattice.IndexOf(1, 1), 230, 190);
        var mesh = CageWarp.MeshOf(lattice, 4);

        Assert.Equal(13, mesh.Across);                       // 3 cells × 4 quads + 1
        Assert.Equal(13, mesh.Down);
        Assert.Equal(13 * 13, mesh.Source.Length);
        Assert.Equal(12 * 12 * 6, mesh.Triangles.Length);
        Assert.Equal((100, 100), mesh.Source[0]);
        Assert.Equal((400, 300), mesh.Source[^1]);
        for (var k = 0; k < mesh.Source.Length; k++)
        {
            var (x, y) = mesh.Source[k];
            var (mx, my) = CageWarp.MapPoint(lattice, x, y);
            Assert.Equal(mx, mesh.Target[k].X, 12);
            Assert.Equal(my, mesh.Target[k].Y, 12);
        }
        Assert.All(mesh.Triangles, i => Assert.InRange(i, 0, mesh.Source.Length - 1));
    }

    /// <summary>Same lattice, same answer — twice, and after a round trip through a copy.</summary>
    [Fact]
    public void TheWarpIsDeterministic()
    {
        var a = CageWarp.Drag(Box(), 5, 231.5, 188.25);
        var b = CageWarp.Drag(Box(), 5, 231.5, 188.25);
        for (var k = 0; k < 50; k++)
        {
            var x = 100 + k * 6.1;
            var y = 100 + k * 3.7;
            Assert.Equal(CageWarp.MapPoint(a, x, y), CageWarp.MapPoint(b, x, y));
        }
    }
}
