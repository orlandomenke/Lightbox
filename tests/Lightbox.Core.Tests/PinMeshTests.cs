using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The puppet mesh (Q239): a grid clipped to a drawing's silhouette. What is
/// under test is that the mesh knows the <em>shape</em> — the inside of an
/// outline, the gap between two limbs — because that knowledge is the whole
/// difference between a pin and a cage handle.
/// </summary>
public class PinMeshTests(ITestOutputHelper output)
{
    [Fact]
    public void AnOutlineDrawnAsALineIsMeshedRightAcrossItsInside()
    {
        // The hard case first: line art has ink on the edge and paper in the
        // middle, and the middle is where the pins go.
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 300, 300)]);

        Assert.False(mesh.IsEmpty);
        Assert.True(mesh.Contains(150, 150), "the centre of a closed outline is inside the puppet");
        Assert.True(mesh.Contains(20, 150), "and so is the part just inside the line");
        Assert.False(mesh.Contains(500, 150), "paper well outside the outline is not");
        Assert.Equal(1, mesh.ComponentCount);
        output.WriteLine($"{mesh.Vertices.Count} vertices, {mesh.Triangles.Count / 3} triangles at cell {mesh.Cell}");
    }

    [Fact]
    public void AGapNarrowerThanACellDoesNotLetTheOutsideIn()
    {
        // Drawn outlines are rarely closed to the pixel. A 6-unit gap in a
        // 24-unit grid must not drain the shape — After Effects calls the
        // equivalent setting Expansion and makes the artist tune it.
        var corners = new (double X, double Y)[] { (0, 0), (300, 0), (300, 300), (0, 300), (0, 6) };
        var open = PinShapes.Outline(4, sides: 4, corners);

        var mesh = PinMesh.Build([open], cell: 24);

        Assert.True(mesh.Contains(150, 150), "the outline leaked through a gap narrower than a cell");
    }

    [Fact]
    public void TwoDrawingsFurtherApartThanACellShareNoTriangle()
    {
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 100, 100), PinShapes.Box(200, 0, 300, 100)], cell: 20);

        Assert.Equal(2, mesh.ComponentCount);
        var left = mesh.ComponentOf(mesh.NearestVertex(50, 50));
        var right = mesh.ComponentOf(mesh.NearestVertex(250, 50));
        Assert.NotEqual(left, right);
        Assert.False(mesh.Contains(150, 50), "the paper between two drawings is not part of either");
    }

    [Fact]
    public void TheProngsOfAForkJoinOnlyThroughItsBase()
    {
        // An arm beside a leg: one piece, and no triangle bridging the gap.
        var mesh = PinMesh.Build([PinShapes.Fork()], cell: 20);

        Assert.Equal(1, mesh.ComponentCount);
        Assert.True(mesh.Contains(30, 100));
        Assert.True(mesh.Contains(270, 100));
        Assert.False(mesh.Contains(150, 100), "the gap between the prongs was meshed over");
        Assert.True(mesh.Contains(150, 230), "the base that joins them was not");
    }

    [Fact]
    public void EveryTriangleIsWoundTheSameWayAndNoneIsFlat()
    {
        var mesh = PinMesh.Build([PinShapes.Fork()], cell: 20);
        Assert.Equal(0, mesh.Triangles.Count % 3);
        for (var t = 0; t < mesh.Triangles.Count; t += 3)
        {
            Assert.True(PinShapes.SignedArea(mesh, mesh.Vertices, t) > 1e-6, $"triangle {t / 3} is flat or flipped");
        }
    }

    [Theory]
    [InlineData(0.5, PinMesh.MinCell)]
    [InlineData(20, 20)]
    [InlineData(9999, PinMesh.MaxCell)]
    public void TheCellSizeIsHonouredAndClamped(double asked, double expected)
    {
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 200, 200)], asked);
        Assert.Equal(expected, mesh.Cell, 9);
    }

    [Fact]
    public void ADrawingTooBigForItsCellGetsACoarserGridNotABiggerSolve()
    {
        // Invariant 6 for the puppet: the cap, not the canvas, bounds the
        // work. A 4K-wide figure at the finest cell would be ~900,000
        // vertices; it must come back under the cap with the cell raised.
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 3800, 2100)], PinMesh.MinCell);

        Assert.InRange(mesh.Vertices.Count, 1, PinMesh.MaxVertices);
        Assert.True(mesh.Cell > PinMesh.MinCell, "the cell was not raised to meet the cap");
        Assert.True(mesh.Contains(1900, 1050));
        output.WriteLine($"3800x2100 at the finest cell: {mesh.Vertices.Count} vertices, cell raised to {mesh.Cell:F1}");
    }

    [Fact]
    public void TheOutlineIsTheSilhouetteAndNothingInside()
    {
        // "Simplified to simple lines": the overlay draws these edges, so
        // none of them may cross the inside of the shape.
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 200, 200)], cell: 20);

        Assert.NotEmpty(mesh.Outline);
        Assert.True(mesh.Outline.Count < mesh.Triangles.Count, "the outline is no simpler than the mesh");
        foreach (var (a, b) in mesh.Outline)
        {
            var (mx, my) = ((mesh.Vertices[a].X + mesh.Vertices[b].X) / 2, (mesh.Vertices[a].Y + mesh.Vertices[b].Y) / 2);
            var fromEdge = Math.Min(Math.Min(mx, 200 - mx), Math.Min(my, 200 - my));
            Assert.True(fromEdge < 20 + 1e-9, $"an outline edge runs through the inside, {fromEdge:F1} from the silhouette");
        }
    }

    [Fact]
    public void APointIsEmbeddedInItsTriangleAndComesBackWhereItWas()
    {
        var mesh = PinMesh.Build([PinShapes.Bar()], cell: 20);
        foreach (var (x, y) in new[] { (200.0, 30.0), (13.7, 41.2), (399.0, 1.0), (0.0, 0.0) })
        {
            var e = mesh.Embed(x, y);
            Assert.Equal(1, e.U + e.V + e.W, 12);
            var a = mesh.Vertices[mesh.Triangles[e.Triangle * 3]];
            var b = mesh.Vertices[mesh.Triangles[e.Triangle * 3 + 1]];
            var c = mesh.Vertices[mesh.Triangles[e.Triangle * 3 + 2]];
            Assert.Equal(x, e.U * a.X + e.V * b.X + e.W * c.X, 9);
            Assert.Equal(y, e.U * a.Y + e.V * b.Y + e.W * c.Y, 9);
        }
    }

    [Fact]
    public void ABrushReachingPastTheSilhouetteStillFollowsTheNearestTriangle()
    {
        // A wide brush paints outside its own centreline; that ink must ride
        // the puppet rather than be left behind at the edge.
        var mesh = PinMesh.Build([PinShapes.Bar()], cell: 20);
        Assert.False(mesh.Contains(200, -15));

        var e = mesh.Embed(200, -15);
        var a = mesh.Vertices[mesh.Triangles[e.Triangle * 3]];
        var b = mesh.Vertices[mesh.Triangles[e.Triangle * 3 + 1]];
        var c = mesh.Vertices[mesh.Triangles[e.Triangle * 3 + 2]];
        Assert.Equal(200, e.U * a.X + e.V * b.X + e.W * c.X, 9);
        Assert.Equal(-15, e.U * a.Y + e.V * b.Y + e.W * c.Y, 9);
        Assert.True(Math.Min(a.Y, Math.Min(b.Y, c.Y)) < PinMesh.DefaultCell, "it took a triangle from the far side of the drawing");
    }

    [Fact]
    public void TheMeshIsTheSameToTheBitEveryTimeItIsBuilt()
    {
        // Derived, never stored — so a reload has to rebuild exactly this.
        var first = PinMesh.Build([PinShapes.Fork()], cell: 20);
        var second = PinMesh.Build([PinShapes.Fork()], cell: 20);

        Assert.Equal(first.Triangles, second.Triangles);
        Assert.Equal(first.Vertices.Count, second.Vertices.Count);
        for (var i = 0; i < first.Vertices.Count; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(first.Vertices[i].X), BitConverter.DoubleToInt64Bits(second.Vertices[i].X));
            Assert.Equal(BitConverter.DoubleToInt64Bits(first.Vertices[i].Y), BitConverter.DoubleToInt64Bits(second.Vertices[i].Y));
        }
    }

    [Fact]
    public void AnEmptyDrawingIsAnEmptyMeshAndNotAnError()
    {
        var none = PinMesh.Build([]);
        Assert.True(none.IsEmpty);
        Assert.Empty(none.Vertices);
        Assert.Empty(none.Triangles);
        Assert.Equal(0, none.ComponentCount);

        var blank = PinMesh.Build([new Stroke()]);
        Assert.True(blank.IsEmpty);
    }

    [Fact]
    public void BuildingReadsTheStrokesAndChangesNothingInThem()
    {
        var stroke = PinShapes.Bar();
        var before = stroke.Points.ToList();
        _ = PinMesh.Build([stroke]);
        Assert.Equal(before, stroke.Points);
        Assert.Null(stroke.RestPoints);
        Assert.Null(stroke.Weights);
    }
}
