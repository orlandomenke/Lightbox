using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <summary>
/// The Move tool, and Shift as the one constraint key.
/// </summary>
/// <remarks>
/// Shift used to mean four different things depending on where the pointer
/// was — resize the brush here, add to a selection there. It now means one:
/// <i>hold this to what it is aiming at</i>. These tests are that promise,
/// tool by tool, plus the Move tool that took guide-grabbing off the rulers.
/// </remarks>
[Collection("BrushState")]
public class MoveToolAndModifierTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static List<Stroke> Strokes(MainViewModel vm) =>
        ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes;

    private static Stroke Last(MainViewModel vm) => Strokes(vm)[^1];

    private static void Near(double expected, double actual, double slack = 0.5) =>
        Assert.True(Math.Abs(expected - actual) < slack, $"expected {expected}, got {actual}");

    private static Stroke Draw(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
        return Last(vm);
    }

    // ---- the move ---------------------------------------------------------------

    [AvaloniaFact]
    public void MovingTheDrawingTranslatesItsStrokes()
    {
        var vm = Vm();
        Draw(vm, 100, 100, 140, 120);
        var before = Last(vm).Points[0];

        Assert.True(vm.BeginMove(120, 110, wholeLayer: false));
        vm.UpdateMove(150, 140, axisLock: false);
        vm.EndMove();

        Near(before.X + 30, Last(vm).Points[0].X);
        Near(before.Y + 30, Last(vm).Points[0].Y);
    }

    [AvaloniaFact]
    public void ShiftHoldsTheMoveToOneAxis()
    {
        var vm = Vm();
        Draw(vm, 100, 100, 140, 120);
        var before = Last(vm).Points[0];

        vm.BeginMove(120, 110, wholeLayer: false);
        vm.UpdateMove(200, 118, axisLock: true);   // mostly sideways
        vm.EndMove();

        Near(before.X + 80, Last(vm).Points[0].X);
        Near(before.Y, Last(vm).Points[0].Y);
    }

    [AvaloniaFact]
    public void AWholeMoveIsOneUndoStep()
    {
        // The preview costs nothing and the record is written once, so fifty
        // pointer events must not become fifty steps of history.
        var vm = Vm();
        Draw(vm, 100, 100, 140, 120);
        var before = Last(vm).Points[0].X;

        vm.BeginMove(120, 110, wholeLayer: false);
        vm.UpdateMove(130, 110, axisLock: false);
        vm.UpdateMove(140, 110, axisLock: false);
        vm.UpdateMove(150, 110, axisLock: false);
        vm.EndMove();
        Near(before + 30, Last(vm).Points[0].X);

        vm.UndoCommand.Execute(null);

        Near(before, Last(vm).Points[0].X);
    }

    [AvaloniaFact]
    public void AClickThatGoesNowhereIsNotAnEdit()
    {
        // Otherwise every stray tap with the Move tool puts an identity
        // transform on the undo stack.
        var vm = Vm();
        Draw(vm, 100, 100, 140, 120);
        var before = Last(vm).Points[0].X;

        vm.BeginMove(120, 110, wholeLayer: false);
        vm.EndMove();

        vm.UndoCommand.Execute(null);   // undoes the stroke, not a phantom move

        Assert.Empty(Strokes(vm));
        Near(before, before);
    }

    [AvaloniaFact]
    public void CtrlMovesEveryDrawingOnTheLayer()
    {
        // Shifting a finished cycle sideways, which nobody does a frame at a
        // time.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        Draw(vm, 100, 100, 140, 120);
        vm.CurrentFrameIndex = 1;
        Draw(vm, 200, 200, 240, 220);

        var layer = vm.PaintLayer();
        var first = (PaintedFrame)layer.Cels[0].Frame!;
        var second = (PaintedFrame)layer.Cels[1].Frame!;
        var a = first.Strokes[^1].Points[0].X;
        var b = second.Strokes[^1].Points[0].X;

        vm.BeginMove(210, 210, wholeLayer: true);
        vm.UpdateMove(260, 210, axisLock: false);
        vm.EndMove();

        Near(a + 50, first.Strokes[^1].Points[0].X);
        Near(b + 50, second.Strokes[^1].Points[0].X);
    }

    [AvaloniaFact]
    public void MovingOneDrawingLeavesTheOthersWhereTheyAre()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        Draw(vm, 100, 100, 140, 120);
        vm.CurrentFrameIndex = 1;
        Draw(vm, 200, 200, 240, 220);

        var layer = vm.PaintLayer();
        var first = (PaintedFrame)layer.Cels[0].Frame!;
        var untouched = first.Strokes[^1].Points[0].X;

        vm.BeginMove(210, 210, wholeLayer: false);
        vm.UpdateMove(260, 210, axisLock: false);
        vm.EndMove();

        Near(untouched, first.Strokes[^1].Points[0].X);
    }

    // ---- shift, tool by tool ------------------------------------------------------

    [AvaloniaFact]
    public void ShiftClickWithTheBrushRunsAStraightSegmentFromTheLastStroke()
    {
        // Photoshop's gesture, and the way a long straight gets drawn without
        // reaching for a ruler.
        var vm = Vm();
        Draw(vm, 100, 100, 200, 100);

        vm.BeginStroke(300, 180, 1, eraseWithCurrentBrush: false, joinFromLast: true);
        vm.EndStroke();

        var joined = Last(vm);
        Near(200, joined.Points[0].X);       // where the last one ended
        Near(100, joined.Points[0].Y);
        Near(300, joined.Points[^1].X);      // straight to the click
        Near(180, joined.Points[^1].Y);
    }

    [AvaloniaFact]
    public void ShiftClickChainsIntoAPolyline()
    {
        var vm = Vm();
        Draw(vm, 100, 100, 200, 100);

        vm.BeginStroke(300, 180, 1, false, joinFromLast: true);
        vm.EndStroke();
        vm.BeginStroke(400, 100, 1, false, joinFromLast: true);
        vm.EndStroke();

        Near(300, Last(vm).Points[0].X);
        Near(180, Last(vm).Points[0].Y);
    }

    [AvaloniaFact]
    public void WithNothingToJoinToAShiftClickIsAnOrdinaryStroke()
    {
        var vm = Vm();

        vm.BeginStroke(300, 180, 1, false, joinFromLast: true);
        vm.EndStroke();

        Near(300, Last(vm).Points[0].X);
        Near(180, Last(vm).Points[0].Y);
    }

    [AvaloniaFact]
    public void AnUndoTakesTheJoinAnchorWithIt()
    {
        // Joining to a mark that is no longer there draws a line out of
        // nowhere, which is worse than not joining.
        var vm = Vm();
        Draw(vm, 100, 100, 200, 100);

        vm.UndoCommand.Execute(null);

        Assert.Null(vm.LastStrokeEndForTests);
    }

    [AvaloniaFact]
    public void ChangingFrameOrLayerTakesTheJoinAnchorWithIt()
    {
        var vm = Vm();
        Draw(vm, 100, 100, 200, 100);
        Assert.NotNull(vm.LastStrokeEndForTests);

        vm.AddFrameCommand.Execute(null);

        Assert.Null(vm.LastStrokeEndForTests);
    }

    [AvaloniaFact]
    public void ShiftSnapsAGradientToAnAngleWithoutClampingItsLength()
    {
        // The ruler's division of labour: the modifier decides the direction,
        // the hand decides how far.
        var vm = Vm();
        vm.ActiveTool = ToolId.Gradient;

        vm.BeginGradient(100, 100);
        vm.MoveGradient(200, 108, snapAngle: true);

        var axis = vm.LiveGradientForTests!;
        Near(100, axis.Points[1].Y, 1);                  // pulled level
        Near(Math.Sqrt(100 * 100 + 8 * 8), axis.Points[1].X - 100, 1);   // length kept
    }

    [Fact]
    public void ShiftSnapsALineShapeToFortyFiveDegrees()
    {
        var points = ShapeBuilder.Outline(
            ShapeKind.Line, 0, 0, 100, 10, regular: true);

        Near(0, points[^1].Y, 1);
        Near(Math.Sqrt(100 * 100 + 10 * 10), points[^1].X, 1);
    }

    [Fact]
    public void AnUnconstrainedLineGoesExactlyWhereItWasDragged()
    {
        var points = ShapeBuilder.Outline(ShapeKind.Line, 0, 0, 100, 10);

        Near(100, points[^1].X);
        Near(10, points[^1].Y);
    }

    [AvaloniaFact]
    public void ShiftFlipsTheFillsSamplingForOneClickOnly()
    {
        // A held override, not a settings change: the option is still where it
        // was for the next fill.
        var vm = Vm();
        vm.ActiveTool = ToolId.Fill;
        Assert.True(vm.SmartFill);

        vm.FillAt(10, 10, invertSmart: true);

        Assert.True(vm.SmartFill);
    }

    // ---- the software-rendering fallback -------------------------------------------

    [AvaloniaFact]
    public void ASoftwareBackendTurnsTheCanvasQualityDown()
    {
        // The label alone was a diagnosis with no treatment.
        var vm = Vm();
        try
        {
            Rendering.CanvasControl.ForceBackendForTests(software: true);
            Assert.Equal(CanvasQuality.Display, vm.CanvasQuality);

            vm.NoteGraphicsBackend();

            Assert.Equal(CanvasQuality.Half, vm.CanvasQuality);
            Assert.Contains("software", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Rendering.CanvasControl.ForceBackendForTests(null);
        }
    }

    [AvaloniaFact]
    public void AGpuBackendChangesNothing()
    {
        var vm = Vm();
        try
        {
            Rendering.CanvasControl.ForceBackendForTests(software: false);

            vm.NoteGraphicsBackend();

            Assert.Equal(CanvasQuality.Display, vm.CanvasQuality);
        }
        finally
        {
            Rendering.CanvasControl.ForceBackendForTests(null);
        }
    }

    [AvaloniaFact]
    public void AChosenQualityIsNeverRevised()
    {
        // However slow the machine. The whole point of a preference is that it
        // belongs to the person.
        var vm = Vm();
        try
        {
            vm.ChooseCanvasQuality(CanvasQuality.Full);
            Rendering.CanvasControl.ForceBackendForTests(software: true);

            vm.NoteGraphicsBackend();

            Assert.Equal(CanvasQuality.Full, vm.CanvasQuality);
        }
        finally
        {
            Rendering.CanvasControl.ForceBackendForTests(null);
            vm.Settings.CanvasQualityChosen = false;
        }
    }

    // ---- the shape tool's two missing wires -----------------------------------------

    [AvaloniaFact]
    public void PickingTheShapeToolAnnouncesItSoTheOptionsCanAppear()
    {
        // IsShapeTool was missing from ActiveTool's notify list, so the whole
        // shape options group stayed hidden and there was no way to pick a
        // shape at all.
        var vm = Vm();
        var announced = new List<string>();
        vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? "");

        vm.ActiveTool = ToolId.Shape;

        Assert.Contains(nameof(MainViewModel.IsShapeTool), announced);
        Assert.True(vm.IsShapeTool);
    }

    [AvaloniaFact]
    public void ChoosingAShapeSelectsTheShapeTool()
    {
        var vm = Vm();

        vm.SelectShapeCommand.Execute(ShapeKind.Ellipse);

        Assert.Equal(ShapeKind.Ellipse, vm.ActiveShape);
        Assert.Equal(ToolId.Shape, vm.ActiveTool);
        Assert.Equal("◯", vm.ShapeGlyph);
    }

    [AvaloniaFact]
    public void AShapeInProgressIsShownWhileItIsBeingDragged()
    {
        // It was rendering into the scratch and never being composited, so the
        // rectangle appeared out of nowhere on release.
        var vm = Vm();
        vm.ActiveTool = ToolId.Shape;

        vm.BeginShape(10, 10);
        vm.MoveShape(90, 50);

        Assert.NotNull(vm.LiveShapeForTests);
        Assert.True(vm.LivePreviewIsVisible, "the shape drag renders nothing on screen");
    }

    // ---- the start screen's one button -----------------------------------------------

    [AvaloniaFact]
    public void CreatingASingleFileReusesTheBlankDocumentAlreadyOpen()
    {
        // Create and Skip were the same outcome with two buttons. Now there is
        // one, and it does not leave a tab nobody asked for.
        var vm = Vm();
        Assert.True(vm.OnlyAnUntouchedBlankDocument);

        vm.NewDocument(new NewDocumentSettings("Test", 640, 480, 12, 72, "#FFFFFF", false, null, WorkspaceChoice.Keep), reuseBlank: true);

        Assert.Single(vm.Tabs);
        Assert.Equal(640, vm.Doc.Scene.Width);
        Assert.Equal("Test", vm.Doc.Scene.Name);
    }

    [AvaloniaFact]
    public void ItNeverReusesADocumentSomebodyHasDrawnOn()
    {
        var vm = Vm();
        Draw(vm, 10, 10, 40, 40);
        Assert.False(vm.OnlyAnUntouchedBlankDocument);

        vm.NewDocument(new NewDocumentSettings("Test", 640, 480, 12, 72, "#FFFFFF", false, null, WorkspaceChoice.Keep), reuseBlank: true);

        Assert.Equal(2, vm.Tabs.Count);
    }
}
