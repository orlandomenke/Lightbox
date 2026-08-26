using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// What the artist sees <em>while</em> a selection is being made, which is the
/// half of the selection tools that had no tests at all.
/// </summary>
/// <remarks>
/// All three bugs here were reported as one thing — "the selection tool does
/// not work" — and none of them touched what the selection eventually was. The
/// committed region was right every time; only the picture of it in progress
/// was missing (B315), stale (B316) or in the wrong place (B317). That is worth
/// naming, because it is why a suite full of green mask assertions said
/// nothing: every one of them measured the answer and none of them measured the
/// working out.
/// </remarks>
public class PolygonSelectionPreviewTests(ITestOutputHelper output)
{
    // ---- B315: the first vertex, and the band between clicks ------------------

    /// <summary>
    /// One vertex placed and the trail is already on screen.
    /// </summary>
    /// <remarks>
    /// It returned null below two vertices, so the entire first click was
    /// invisible — no mark where the vertex landed, no band, nothing. An artist
    /// concluded the click had missed and clicked again, which is how a polygon
    /// ends up starting one vertex late.
    /// </remarks>
    [Fact]
    public void TheFirstVertexIsDrawnBeforeThereIsASecond()
    {
        var path = SelectionAnts.OpenPath([new(100, 100, 1)], cursor: null, vertexRadius: 4);

        Assert.NotNull(path);
        output.WriteLine($"one vertex draws {path!.Bounds}");
        // The ring, so there is something to see at the point itself.
        Assert.Equal(96, path.Bounds.Left, 1);
        Assert.Equal(104, path.Bounds.Right, 1);
    }

    /// <summary>The band from the last vertex to the pointer, between clicks.</summary>
    [Fact]
    public void TheTrailReachesThePointerBetweenClicks()
    {
        var path = SelectionAnts.OpenPath(
            [new(10, 10, 1), new(50, 10, 1)], cursor: (50, 200), vertexRadius: 0);

        Assert.NotNull(path);
        output.WriteLine($"trail with band {path!.Bounds}");
        Assert.Equal(200, path.Bounds.Bottom, 1);
    }

    /// <summary>
    /// No pointer — off the canvas, or a frame before the first move — draws the
    /// trail without a band rather than nothing at all.
    /// </summary>
    [Fact]
    public void NoPointerStillDrawsTheTrail()
    {
        var path = SelectionAnts.OpenPath([new(10, 10, 1), new(50, 10, 1)], cursor: null);

        Assert.NotNull(path);
        Assert.Equal(50, path!.Bounds.Right, 1);
    }

    /// <summary>No vertices is still no path: there is no polygon to preview.</summary>
    [Fact]
    public void NoVerticesIsStillNothing() =>
        Assert.Null(SelectionAnts.OpenPath([], cursor: (5, 5), vertexRadius: 4));

    // ---- B317: the marquee lands under the hand on a moved page ---------------

    /// <summary>
    /// The drag shape and the committed outline agree about where they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves of this path arrive in different spaces: the base is
    /// traced off a surface mask, the drag shape comes from <c>ViewToDoc</c>,
    /// which adds the page origin. The renderer draws in surface pixels, so the
    /// drag shape was one origin out — the rubber band sat away from the
    /// pointer for the length of the drag and the region snapped into place on
    /// release.
    /// </para>
    /// <para>
    /// <b>The assertion is that the two agree</b>, rather than that the drag
    /// shape is at any particular number. A marquee dragged exactly around a
    /// committed selection has to draw on top of it; anything that makes those
    /// two disagree is the bug, whichever of them moved.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnAMovedPageTheDragShapeLandsOnTheOutlineItMatches()
    {
        var origin = (X: 100.0, Y: 60.0);
        // The same square, in each half's own space: surface for the committed
        // contour, document for the shape the canvas reports mid-drag.
        using var basePath = SelectionAnts.BasePath([Square(200, 200, 50)]);
        var drag = Square(200 + origin.X, 200 + origin.Y, 50);

        using var frame = SelectionAnts.FramePath(basePath, null, drag, dragOrigin: origin);

        Assert.NotNull(frame);
        output.WriteLine($"base {basePath!.Bounds}, both together {frame!.Bounds}");
        // One square's worth of bounds, not two squares one origin apart.
        Assert.Equal(200, frame.Bounds.Left, 1);
        Assert.Equal(250, frame.Bounds.Right, 1);
        Assert.Equal(200, frame.Bounds.Top, 1);
        Assert.Equal(250, frame.Bounds.Bottom, 1);
    }

    /// <summary>At origin zero nothing moves — which is why this went unseen.</summary>
    [Fact]
    public void AtOriginZeroTheDragShapeIsWhereItAlwaysWas()
    {
        using var frame = SelectionAnts.FramePath(null, null, Square(3, 4, 6));

        Assert.NotNull(frame);
        Assert.Equal(3, frame!.Bounds.Left, 1);
        Assert.Equal(10, frame.Bounds.Bottom, 1);
    }

    private static List<StrokePoint> Square(double x, double y, double side) =>
    [
        new(x, y, 1), new(x + side, y, 1), new(x + side, y + side, 1), new(x, y + side, 1),
    ];
}

/// <summary>
/// B316: a half-drawn polygon does not outlive the shape that could finish it.
/// </summary>
[Collection("BrushState")]
public sealed class PolygonSurvivesNoShapeSwitchTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.ActiveTool = ToolId.Select;
        vm.ActiveSelectVariant = SelectVariant.Polygon;
        return vm;
    }

    /// <summary>
    /// Reaching for another shape mid-polygon lets the half-drawn one go.
    /// </summary>
    /// <remarks>
    /// A tool change has cancelled it since B147; a <em>variant</em> change is
    /// not a tool change — <c>ActiveTool</c> stays <c>Select</c> throughout — so
    /// nothing fired, and the trail stayed on screen with no gesture left that
    /// could close it. The box does not take vertices, and the double-click
    /// that would have closed it belongs to a tool no longer in hand.
    /// </remarks>
    [AvaloniaFact]
    public void SwitchingShapeDropsAHalfDrawnPolygon()
    {
        var vm = Vm();
        vm.AddPolygonVertex(100, 100);
        vm.AddPolygonVertex(200, 100);
        Assert.Equal(2, vm.PolygonInProgress.Count);

        vm.SelectVariantOfCommand.Execute(SelectVariant.Box);

        Assert.Empty(vm.PolygonInProgress);
    }

    /// <summary>
    /// The guard against over-fixing: a <em>committed</em> selection survives.
    /// </summary>
    /// <remarks>
    /// Reaching for a different shape to add to or subtract from what is
    /// already selected is the whole reason Shift and Alt exist on these tools,
    /// so clearing the region here would be a worse bug than the one being
    /// fixed. Only modal work in progress goes.
    /// </remarks>
    [AvaloniaFact]
    public void SwitchingShapeKeepsACommittedSelection()
    {
        var vm = Vm();
        vm.ApplySelectionShape(
            [new(10, 10, 1), new(60, 10, 1), new(60, 60, 1), new(10, 60, 1)], false, false);
        Assert.True(vm.HasSelection);

        vm.SelectVariantOfCommand.Execute(SelectVariant.Ellipse);

        Assert.True(vm.HasSelection);
    }

    /// <summary>Cycling with the shortcut is the same gesture and does the same.</summary>
    [AvaloniaFact]
    public void CyclingTheVariantDropsItToo()
    {
        var vm = Vm();
        vm.AddPolygonVertex(100, 100);

        vm.CycleSelectVariant();

        Assert.Empty(vm.PolygonInProgress);
    }
}
