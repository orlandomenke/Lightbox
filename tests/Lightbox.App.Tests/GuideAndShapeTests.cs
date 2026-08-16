using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <summary>
/// Guides and shapes through the real drawing path.
/// </summary>
/// <remarks>
/// The geometry is covered without a window in <c>GuideTests</c> and
/// <c>ShapeBuilderTests</c>. This is about the wiring: that a snapped stroke
/// records snapped points, that a shape becomes an ordinary stroke, and that a
/// document with no guides pays nothing.
/// </remarks>
[Collection("BrushState")]
public class GuideAndShapeTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static Stroke Last(MainViewModel vm) =>
        ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1];

    private static int Count(MainViewModel vm) =>
        ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;

    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) < 0.5, $"expected {expected}, got {actual}");

    // ---- absence ------------------------------------------------------------------

    [AvaloniaFact]
    public void ADocumentWithNoGuidesDrawsExactlyAsBefore()
    {
        // Optional means absent: no guides, no snapping, nothing paid for.
        var vm = Vm();
        Assert.False(vm.HasGuides);

        vm.BeginStroke(13, 27, 1);
        vm.MoveStroke(61, 44, 1);
        vm.EndStroke();

        var stroke = Last(vm);
        Near(13, stroke.Points[0].X);
        Near(27, stroke.Points[0].Y);
        Near(61, stroke.Points[^1].X);
    }

    [AvaloniaFact]
    public void TheFirstGuideBringsTheMachineryAndTheLastOneTakesItAway()
    {
        var vm = Vm();

        var guide = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 10);
        Assert.True(vm.HasGuides);
        Assert.NotNull(vm.Doc.Scene.Guides);

        vm.RemoveGuide(guide);

        Assert.False(vm.HasGuides);
        Assert.Null(vm.Doc.Scene.Guides);
    }

    [AvaloniaFact]
    public void AddingAndRemovingAGuideIsUndoable()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 100, 100);

        vm.UndoCommand.Execute(null);

        Assert.False(vm.HasGuides);
    }

    // ---- snapping the record ---------------------------------------------------------

    [AvaloniaFact]
    public void AStrokeOnAGridRecordsTheSnappedPoints()
    {
        // The snapped result is what the record holds, which is why moving the
        // guide later cannot move a line already drawn.
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);

        vm.BeginStroke(23, 17, 1);
        vm.EndStroke();

        Near(20, Last(vm).Points[0].X);
        Near(20, Last(vm).Points[0].Y);
    }

    [AvaloniaFact]
    public void MovingAGuideAfterwardsDoesNotMoveTheArt()
    {
        var vm = Vm();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.BeginStroke(23, 17, 1);
        vm.MoveStroke(63, 57, 1);
        vm.EndStroke();
        var before = Last(vm).Points.Select(p => (p.X, p.Y)).ToList();

        vm.MoveGuide(grid, 7, 7);

        Assert.Equal(before, Last(vm).Points.Select(p => (p.X, p.Y)));
    }

    [AvaloniaFact]
    public void ARulerStraightensTheStrokeDrawnAlongIt()
    {
        // The point of a ruler. The hand wobbles; the line does not.
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 0, 100, angle: 0);

        vm.BeginStroke(20, 100, 1);
        vm.MoveStroke(60, 103, 1);
        vm.MoveStroke(120, 97, 1);
        vm.MoveStroke(200, 104, 1);
        vm.EndStroke();

        Assert.All(Last(vm).Points, p => Near(100, p.Y));
        // And it still goes as far as the hand went: the ruler decides the
        // direction, not the extent.
        Near(200, Last(vm).Points[^1].X);
    }

    [AvaloniaFact]
    public void AStrokeAcrossTheRulerIsLeftFreehand()
    {
        // A guide that grabs strokes you meant to draw freehand is a guide you
        // turn off.
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 0, 100, angle: 0);

        vm.BeginStroke(300, 300, 1);
        vm.MoveStroke(305, 360, 1);
        vm.MoveStroke(312, 420, 1);
        vm.EndStroke();

        Assert.True(Last(vm).Points[^1].Y > 400);
        Assert.True(Last(vm).Points[^1].X > 305);
    }

    [AvaloniaFact]
    public void TurningSnappingOffLeavesTheStrokeAlone()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.SnapToGuides = false;

        vm.BeginStroke(23, 17, 1);
        vm.EndStroke();

        Near(23, Last(vm).Points[0].X);
    }

    [AvaloniaFact]
    public void AStrokeToADeadGuideIsUnconstrained()
    {
        var vm = Vm();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        grid.Snaps = false;

        vm.BeginStroke(23, 17, 1);
        vm.EndStroke();

        Near(23, Last(vm).Points[0].X);
    }

    // ---- shapes ----------------------------------------------------------------------

    [AvaloniaFact]
    public void AShapeIsAnOrdinaryStroke()
    {
        // One pixel path: a shape erases, re-renders and inbetweens like every
        // other mark, and takes the brush that is loaded.
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        vm.ActiveShape = ShapeKind.Rectangle;

        vm.BeginShape(10, 10);
        vm.MoveShape(90, 50);
        vm.EndShape(90, 50);

        var stroke = Last(vm);
        Assert.Equal(ToolKind.Brush, stroke.Tool);
        Assert.Equal(5, stroke.Points.Count);
        Near(10, stroke.Points.Min(p => p.X));
        Near(90, stroke.Points.Max(p => p.X));
    }

    [AvaloniaFact]
    public void AShapeCarriesTheCurrentBrushAndSwatch()
    {
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        var swatch = vm.ActiveSwatchId;
        vm.BrushSize = 17;

        vm.BeginShape(10, 10);
        vm.EndShape(60, 60);

        Assert.Equal(swatch, Last(vm).SwatchId);
        Assert.Equal(17, Last(vm).Brush.Size, 3);
    }

    [AvaloniaFact]
    public void ShiftSquaresItAndAltGrowsItFromTheCentre()
    {
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        vm.ActiveShape = ShapeKind.Rectangle;

        vm.BeginShape(100, 100);
        vm.EndShape(160, 120, fromCentre: true, regular: true);

        var stroke = Last(vm);
        Near(40, stroke.Points.Min(p => p.X));
        Near(160, stroke.Points.Max(p => p.X));
        Near(40, stroke.Points.Min(p => p.Y));
        Near(160, stroke.Points.Max(p => p.Y));
    }

    [AvaloniaFact]
    public void AClickWithNoDragIsNotAShape()
    {
        // Committing it would leave a single dab where a rectangle was meant.
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        var before = Count(vm);

        vm.BeginShape(40, 40);
        vm.EndShape(40, 40);

        Assert.Equal(before, Count(vm));
        Assert.Contains("Drag", vm.AiStatus);
    }

    [AvaloniaFact]
    public void AShapeIsUndoableInOneStep()
    {
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        var before = Count(vm);
        vm.BeginShape(10, 10);
        vm.EndShape(90, 50);
        Assert.Equal(before + 1, Count(vm));

        vm.UndoCommand.Execute(null);

        Assert.Equal(before, Count(vm));
    }

    [AvaloniaFact]
    public void AShapeSnapsToTheGridLikeAnythingElse()
    {
        // Which is most of why anybody turns a grid on.
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);

        vm.BeginShape(23, 17);
        vm.EndShape(83, 57);

        var stroke = Last(vm);
        Near(20, stroke.Points.Min(p => p.X));
        Near(80, stroke.Points.Max(p => p.X));
    }

    [AvaloniaFact]
    public void TheShapeToolDoesNotPaintOnADragWithTheBrush()
    {
        // Tools do not overlap: picking Shape takes the ordinary stroke path
        // out of play, or a drag would produce both.
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;
        var before = Count(vm);

        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(50, 50, 1);
        vm.EndStroke();

        Assert.Equal(before, Count(vm));
    }

    // ---- the chrome --------------------------------------------------------------------

    [AvaloniaFact]
    public void TheCanvasDrawsNoGuidesUntilThereAreSome()
    {
        var window = new Lightbox.App.Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        Assert.Null(canvas.Guides);

        vm.AddGuide(GuideKind.VanishingPoint, 200, 100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var line = Assert.Single(canvas.Guides!);
        Assert.Equal(200, line.X, 3);
        // A vanishing point is flattened into a fan: drawing every direction it
        // constrains would be a filled disc.
        Assert.True(line.Angles.Count > 8);

        vm.ClearGuidesCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(canvas.Guides);
    }

    [AvaloniaFact]
    public void AHiddenGuideIsNotDrawnButStillSnaps()
    {
        var window = new Lightbox.App.Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        grid.Visible = false;
        vm.AddGuide(GuideKind.Line, 0, 0);   // forces a refresh
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Single(canvas.Guides!);        // only the visible one is drawn

        vm.BeginStroke(23, 17, 1);
        vm.EndStroke();
        Near(20, Last(vm).Points[0].X);       // the hidden one still constrains
    }

    // ---- the tools that place a point all snap (B216) --------------------------------
    //
    // Snapping used to be four copied lines that a tool either remembered or
    // did not: the brush, the shape tool and the pen had them, and nine other
    // gestures that place a point did not. These pin the ones that were
    // missing, one per tool, plus the two that must NOT snap.

    /// <summary>The gradient's axis lands on the grid, the way a shape's corner does.</summary>
    /// <remarks>
    /// The clearest of the gaps: the gradient and the shape tool are the same
    /// gesture — an anchor and a dragged far end — and one snapped while the
    /// other did not.
    /// </remarks>
    [AvaloniaFact]
    public void TheGradientAxisSnapsToTheGuides()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.ActiveTool = ToolId.Gradient;

        vm.BeginGradient(23, 17);
        vm.EndGradient(97, 103);

        var axis = Last(vm).Points;
        Near(20, axis[0].X);
        Near(20, axis[0].Y);
        Near(100, axis[1].X);
        Near(100, axis[1].Y);
    }

    /// <summary>
    /// Shift still straightens the ramp, and the guides do not drag it back off.
    /// </summary>
    /// <remarks>
    /// The composition question the fix had to answer rather than dodge: a
    /// guide is a place and Shift is an angle, and snapping after straightening
    /// would undo the straightening. Held Shift means the angle wins.
    /// </remarks>
    [AvaloniaFact]
    public void ShiftStillStraightensTheGradientWithGuidesOn()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.ActiveTool = ToolId.Gradient;

        vm.BeginGradient(0, 0);
        vm.EndGradient(97, 6, snapAngle: true);

        var axis = Last(vm).Points;
        // Straight along the horizontal, which is what Shift asked for.
        Near(0, axis[1].Y);
    }

    /// <summary>A pen node lands on the grid.</summary>
    [AvaloniaFact]
    public void APenNodeSnapsToTheGuides()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.ActiveTool = ToolId.Pen;

        vm.PenPress(23, 17, 0);
        vm.PenPress(97, 103, 0);
        vm.FinishPen();

        var path = Last(vm).Path!;
        Near(20, path.Nodes[0].X);
        Near(20, path.Nodes[0].Y);
    }

    /// <summary>
    /// The pen's rubber band previews the snapped place, not the pointer.
    /// </summary>
    /// <remarks>
    /// Only the press snapped, so with a grid on the band ran to the pointer
    /// and the node then landed somewhere else. A preview that lies about where
    /// the click will land is worse than no preview.
    /// </remarks>
    [AvaloniaFact]
    public void ThePenRubberBandPreviewsWhereTheNodeWillActuallyLand()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.ActiveTool = ToolId.Pen;
        vm.PenPress(0, 0, 0);
        // The press is still shaping until it is released, and a shaping
        // session ignores a hover — it is curving the node it just placed.
        vm.PenRelease();

        vm.PenHover(97, 103);
        var previewed = vm.Pen!.Cursor;
        Assert.NotNull(previewed);

        vm.PenPress(97, 103, 0);
        var placed = vm.Pen!.Path.Nodes[1];

        // The band promised exactly where the node went.
        Near(placed.X, previewed!.Value.X);
        Near(placed.Y, previewed.Value.Y);
        Near(100, placed.X);
    }

    /// <summary>
    /// Dragging one node with the white arrow puts it on the guide, offset and
    /// all.
    /// </summary>
    /// <remarks>
    /// The grab catches a node slightly off its centre, so snapping the pointer
    /// would settle the node that offset away from the guide — near it, never
    /// on it. The destination is what snaps.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingANodeLandsItOnTheGuideDespiteTheGrabOffset()
    {
        var vm = Vm();
        vm.ActiveTool = ToolId.Pen;
        vm.PenPress(0, 0, 0);
        vm.PenPress(50, 0, 0);
        vm.PenPress(97, 6, 0);
        vm.FinishPen();

        // The grid arrives after the line, so only the drag is snapped by it.
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.ActiveTool = ToolId.DirectSelect;

        var stroke = Last(vm);
        Assert.True(vm.BeginPathEditAt(stroke.Points[0].X, stroke.Points[0].Y, 400));

        // Grabbed 3 px off the node's centre. The delta alone would land the
        // node at (103, 97) — deliberately NOT on the lattice, so the assertion
        // below can only pass if something snapped it.
        var grab = vm.GrabPathPart(97, 6, 400, false);
        Assert.True(grab.IsHit);
        vm.DragPathPart(grab, 106, 100, 6, 91);
        vm.CommitPathEdit();

        var moved = Last(vm).Path!.Nodes[^1];
        Near(100, moved.X);
        Near(100, moved.Y);
    }


    /// <summary>A polygon selection's corners snap; it is clicked, not traced.</summary>
    /// <remarks>
    /// The pair to the lasso test below it, and the two together are the whole
    /// rule: a point you <em>aim</em> snaps, a line you <em>trace</em> does not.
    /// </remarks>
    [AvaloniaFact]
    public void APolygonSelectionCornerSnapsToTheGuides()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);

        vm.AddPolygonVertex(23, 17);
        vm.AddPolygonVertex(97, 23);
        vm.AddPolygonVertex(63, 103);

        var placed = vm.PolygonInProgress;
        Near(20, placed[0].X);
        Near(20, placed[0].Y);
        Near(100, placed[1].X);
    }

    /// <summary>A lasso is traced, so it does not snap.</summary>
    /// <remarks>
    /// The deliberate exception, and it is the same rule the brush follows: a
    /// stroke snaps its start and not its middle, because pulling every sample
    /// onto a lattice fights the hand the whole way round. Asserted so that
    /// "snap everything" cannot be applied here by a later tidy-up.
    /// </remarks>
    [AvaloniaFact]
    public void AFreehandLassoIsNotPulledOntoTheGrid()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);

        var traced = new List<StrokePoint>
        {
            new(23, 17, 1), new(63, 21, 1), new(59, 61, 1), new(23, 57, 1),
        };
        vm.ApplySelectionShape(traced, add: false, subtract: false);

        Assert.True(vm.HasSelection);
        var contour = vm.SelectionContours[0];
        // Where the hand went, to within the half-pixel the mask trace lands
        // on. The claim is that nothing was pulled to a multiple of 20 — the
        // grid is 20 and the corner is at 23, so a snapped lasso would read 20.
        Assert.True(
            Math.Abs(23 - contour[0].X) <= 1,
            $"the lasso corner moved to {contour[0].X}, so something snapped it");
        Assert.True(
            Math.Abs(17 - contour[0].Y) <= 1,
            $"the lasso corner moved to {contour[0].Y}, so something snapped it");
    }

    // ---- B225: moving a drawing snaps it to the guides ----------------------------------
    //
    // The last gap the tool audit found: B216 gave the guides to every tool
    // that *places a point*, and left the ones that move a *thing* by a delta,
    // because that needed an answer to "what part of the moved thing lands on
    // the guide". Q99 answered it: the bounds, corners and centre.

    /// <summary>A drawing dragged near a guide lands its edge on it.</summary>
    [AvaloniaFact]
    public void MovingADrawingSnapsItsEdgeToAGuide()
    {
        var vm = Vm();
        // A vertical ruler at x = 300.
        vm.AddGuide(GuideKind.Line, 300, 0, angle: 90);
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();

        var line = Last(vm);
        var rightEdge = line.Points[^1].X + line.Brush.Size / 2;

        vm.ActiveTool = ToolId.Move;
        Assert.True(vm.BeginMove(150, 100, wholeLayer: false));
        // Drag so the right edge lands a few pixels short of the guide.
        var shortfall = 300 - rightEdge - 4;
        vm.UpdateMove(150 + shortfall, 100, axisLock: false);
        vm.EndMove();

        var moved = Last(vm);
        var landedEdge = moved.Points[^1].X + moved.Brush.Size / 2;
        output.WriteLine($"right edge landed at {landedEdge:F2}, guide at 300");
        Near(300, landedEdge);
    }

    /// <summary>Out of reach of every guide, the move is exactly what the hand did.</summary>
    /// <remarks>
    /// The test that stops the one above passing vacuously: a snap that fires
    /// everywhere would satisfy it too, and would make the tool impossible to
    /// place anything with.
    /// </remarks>
    [AvaloniaFact]
    public void AMoveFarFromEveryGuideIsNotCorrected()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 300, 0, angle: 90);
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(140, 100, 1);
        vm.EndStroke();

        var before = Last(vm).Points[0].X;

        vm.ActiveTool = ToolId.Move;
        Assert.True(vm.BeginMove(120, 100, wholeLayer: false));
        vm.UpdateMove(120, 400, axisLock: false);   // straight down, nowhere near it
        vm.EndMove();

        Near(before, Last(vm).Points[0].X);
        Near(400, Last(vm).Points[0].Y);
    }

    /// <summary>Snapping off means the guides do not touch a move either.</summary>
    /// <remarks>
    /// One switch for the whole application: a move goes through the same
    /// SnappedPoint every placed point does, so ⌗ turns this off with
    /// everything else rather than needing its own control.
    /// </remarks>
    [AvaloniaFact]
    public void TurningSnappingOffLeavesAMoveAlone()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 300, 0, angle: 90);
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();
        vm.SnapToGuides = false;

        var line = Last(vm);
        var rightEdge = line.Points[^1].X + line.Brush.Size / 2;
        var shortfall = 300 - rightEdge - 4;

        vm.ActiveTool = ToolId.Move;
        Assert.True(vm.BeginMove(150, 100, wholeLayer: false));
        vm.UpdateMove(150 + shortfall, 100, axisLock: false);
        vm.EndMove();

        var moved = Last(vm);
        var landedEdge = moved.Points[^1].X + moved.Brush.Size / 2;
        // Exactly where the hand put it — four pixels short of the guide.
        Near(296, landedEdge);
    }

    /// <summary>
    /// Held to an axis, the move still snaps along that axis and not off it.
    /// </summary>
    /// <remarks>
    /// The composition question, and the same answer the gradient gives Shift:
    /// the constraint the artist asked for out loud wins, and the guides work
    /// within it. A snap that pulled the move off a held axis would make Shift
    /// and the guides unusable together.
    /// </remarks>
    [AvaloniaFact]
    public void AnAxisLockedMoveStillSnapsAlongThatAxis()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 300, 0, angle: 90);
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();

        var line = Last(vm);
        var startY = line.Points[0].Y;
        var rightEdge = line.Points[^1].X + line.Brush.Size / 2;
        var shortfall = 300 - rightEdge - 4;

        vm.ActiveTool = ToolId.Move;
        Assert.True(vm.BeginMove(150, 100, wholeLayer: false));
        // Mostly sideways with a wobble, Shift held: the wobble is dropped.
        vm.UpdateMove(150 + shortfall, 118, axisLock: true);
        vm.EndMove();

        var moved = Last(vm);
        Near(300, moved.Points[^1].X + moved.Brush.Size / 2);
        Near(startY, moved.Points[0].Y);
    }

    /// <summary>The centre lines up with a vanishing point, not just the corners.</summary>
    /// <remarks>
    /// Why the centre is one of the five candidates: a corner is what you line
    /// up against a ruler, and the middle is what you line up against a point.
    /// </remarks>
    [AvaloniaFact]
    public void TheCentreOfWhatIsMovingCanLandOnAVanishingPoint()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.VanishingPoint, 400, 300);
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(140, 140, 1);
        vm.EndStroke();

        var line = Last(vm);
        var midX = (line.Points[0].X + line.Points[^1].X) / 2;
        var midY = (line.Points[0].Y + line.Points[^1].Y) / 2;

        vm.ActiveTool = ToolId.Move;
        Assert.True(vm.BeginMove(midX, midY, wholeLayer: false));
        // Aim the centre a few pixels off the point.
        vm.UpdateMove(400 - 5, 300 - 5, axisLock: false);
        vm.EndMove();

        var moved = Last(vm);
        var landedMidX = (moved.Points[0].X + moved.Points[^1].X) / 2;
        var landedMidY = (moved.Points[0].Y + moved.Points[^1].Y) / 2;
        output.WriteLine($"centre landed at ({landedMidX:F2}, {landedMidY:F2}), point at (400, 300)");
        Near(400, landedMidX);
        Near(300, landedMidY);
    }
}
