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
public class GuideAndShapeTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static Stroke Last(MainViewModel vm) =>
        ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1];

    private static int Count(MainViewModel vm) =>
        ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;

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
}
