using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Q197 — a mark on a hold starts a blank drawing by default; a copy is a
/// choice of the same setting; an edit <em>of</em> the held drawing still keys
/// a copy whatever the setting says.
/// </summary>
/// <remarks>
/// <para>
/// The owner's request: <i>"A hold or blank frame, if drawn on it it removes
/// the hold and draws a fresh frame."</i> Paper: the next drawing starts on a
/// clean sheet, and the onion skin is the light table.
/// </para>
/// <para>
/// Asserted on the record — which strokes the keyed frame holds — rather than
/// on pixels, because the question is what the drawing <em>is</em>, and a
/// pixel read of a copied stroke under a new one cannot tell the two apart
/// (see the <c>brush-measurement</c> skill).
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BlankDrawingOnAHoldTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Frame 0 drawn on, frame 1 a hold of it, the playhead on the hold.</summary>
    private static MainViewModel OnAHold(out Layer layer)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        Drag(vm, 100, 200, 400, 200);

        vm.AddFrameCommand.Execute(null);
        layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame = null; // a hold of frame 0
        vm.CurrentFrameIndex = 1;
        return vm;
    }

    private static void Drag(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
    }

    private static Frame Held(Layer layer) => (Frame)layer.Cels[0].Frame!;

    // ---- the default -----------------------------------------------------------

    [AvaloniaFact]
    public void TheDefaultIsABlankDrawing()
    {
        var vm = new MainViewModel(null);
        Assert.Equal(HoldDrawing.StartABlankDrawing, vm.DrawingOnAHold);
        Assert.Equal("StartABlankDrawing", new AppSettings().DrawingOnAHold);
    }

    /// <summary>
    /// The change itself. Before Q197 the keyed frame carried the held line as
    /// well, so this read two strokes.
    /// </summary>
    [AvaloniaFact]
    public void AMarkOnAHoldKeysAFrameHoldingOnlyTheMark()
    {
        var vm = OnAHold(out var layer);
        var heldIds = Held(layer).Strokes.Select(s => s.Id).ToList();

        Drag(vm, 120, 300, 300, 320);

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        output.WriteLine($"keyed strokes: {keyed.Strokes.Count}, held strokes: {Held(layer).Strokes.Count}");
        var mark = Assert.Single(keyed.Strokes);
        Assert.DoesNotContain(mark.Id, heldIds);
        Assert.Equal(120, mark.Points[0].X, 3);
        // The drawing the hold was showing is untouched.
        Assert.Single(Held(layer).Strokes);
        Assert.True(vm.FrameCells[1].IsKeyed);
    }

    [AvaloniaFact]
    public void TheBlankKeyIsItsOwnUndoStep()
    {
        var vm = OnAHold(out var layer);
        Drag(vm, 120, 300, 300, 320);

        vm.UndoCommand.Execute(null);
        Assert.Empty(Assert.IsType<Frame>(vm.PaintLayer().Cels[1].Frame).Strokes);

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.PaintLayer().Cels[1].Frame);
    }

    [AvaloniaFact]
    public void EditTheHeldDrawingIsUnchanged()
    {
        var vm = OnAHold(out var layer);
        vm.DrawingOnAHold = HoldDrawing.EditTheHeldDrawing;

        Drag(vm, 120, 300, 300, 320);

        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(2, Held(layer).Strokes.Count);
    }

    // ---- the eraser: a blank page has nothing to rub out -------------------------

    /// <summary>
    /// B236 meets Q197: the eraser keys a blank page, erases nothing on it, and
    /// the key goes back with the erase — no drawing on the timeline, no step.
    /// </summary>
    [AvaloniaFact]
    public void AnEraserOnAHoldLeavesNoKeyAndNoStep()
    {
        var vm = OnAHold(out var layer);
        var steps = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 60;
        Drag(vm, 150, 200, 350, 200); // right across the held line

        output.WriteLine($"cel 1 keyed: {layer.Cels[1].Frame is not null}, steps: {vm.RecordedStepCount - steps}");
        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(steps, vm.RecordedStepCount);
        Assert.Single(Held(layer).Strokes);
    }

    // ---- gestures that key and then decline hand the key back --------------------

    /// <summary>
    /// A click with the shape tool is not a shape. The press keys (it has to,
    /// to have somewhere to preview), and under the blank default a key that
    /// stayed would empty the frame on screen for a gesture that did nothing.
    /// </summary>
    [AvaloniaFact]
    public void AShapeClickWithNoDragOnAHoldLeavesNoKey()
    {
        var vm = OnAHold(out var layer);
        var steps = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Shape;
        vm.BeginShape(200, 300);
        vm.EndShape(200, 300);

        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(steps, vm.RecordedStepCount);
    }

    [AvaloniaFact]
    public void ADraggedShapeOnAHoldIsABlankDrawingWithTheShape()
    {
        var vm = OnAHold(out var layer);

        vm.ActiveTool = ToolId.Shape;
        vm.BeginShape(200, 300);
        vm.EndShape(300, 380);

        Assert.Single(Assert.IsType<Frame>(layer.Cels[1].Frame).Strokes);
    }

    [AvaloniaFact]
    public void AOneNodePenPathOnAHoldLeavesNoKey()
    {
        var vm = OnAHold(out var layer);
        var steps = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Pen;
        Assert.True(vm.PenPress(200, 300, 6));
        vm.PenRelease();
        Assert.False(vm.FinishPen());

        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(steps, vm.RecordedStepCount);
    }

    /// <summary>
    /// A pen path can be parked by a tool switch and never finished. The pen
    /// keyed on its first node, so the hold stayed keyed — blank, under the
    /// default — for a line that was never written. It keys at the finish now.
    /// </summary>
    [AvaloniaFact]
    public void AParkedPenPathOnAHoldLeavesNoKey()
    {
        var vm = OnAHold(out var layer);

        vm.ActiveTool = ToolId.Pen;
        Assert.True(vm.PenPress(200, 300, 6));
        vm.PenRelease();
        vm.ActiveTool = ToolId.Brush;

        Assert.Null(layer.Cels[1].Frame);
    }

    [AvaloniaFact]
    public void AFinishedPenLineOnAHoldIsABlankDrawingWithTheLine()
    {
        var vm = OnAHold(out var layer);

        vm.ActiveTool = ToolId.Pen;
        Assert.True(vm.PenPress(200, 300, 6));
        vm.PenRelease();
        Assert.True(vm.PenPress(300, 320, 6));
        vm.PenRelease();
        Assert.True(vm.FinishPen());

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        Assert.Single(keyed.Strokes);
    }

    [AvaloniaFact]
    public void ACancelledGradientOnAHoldLeavesNoKey()
    {
        var vm = OnAHold(out var layer);
        var steps = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Gradient;
        vm.BeginGradient(100, 300);
        vm.CancelGradient();

        Assert.Null(layer.Cels[1].Frame);
        // The gradient the first press created for an empty document stays —
        // it is the document's, not the gesture's — and nothing else does.
        Assert.True(vm.RecordedStepCount - steps <= 1);
    }

    [AvaloniaFact]
    public void TypeWithNoLettersOnAHoldLeavesNoKey()
    {
        var vm = OnAHold(out var layer);
        var steps = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Text;
        vm.BeginText(200, 400);
        vm.CommitText();

        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(steps, vm.RecordedStepCount);
    }

    // ---- marks that read what is there start from a copy -------------------------

    /// <summary>
    /// The bucket finds its edges in the drawing it lands on. On a blank page
    /// it has none, so colouring held line art would flood the frame; it keys
    /// a copy and fills inside the held lines on this frame.
    /// </summary>
    [AvaloniaFact]
    public void TheBucketOnAHoldFillsInsideTheHeldLinesOnACopy()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BrushSize = 6;
        // A closed box on frame 0.
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 100, 1);
        vm.MoveStroke(300, 300, 1);
        vm.MoveStroke(100, 300, 1);
        vm.MoveStroke(100, 100, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;

        vm.ActiveTool = ToolId.Fill;
        vm.FillAt(200, 200);

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        Assert.Equal(2, keyed.Strokes.Count); // the copied box, and the fill inside it
        var fill = Assert.Single(keyed.Strokes, s => s.Tool == ToolKind.Fill);
        var maxX = fill.Points.Max(p => p.X);
        output.WriteLine($"fill right edge: {maxX}");
        Assert.True(maxX < 320, "the fill stayed inside the held box rather than flooding the frame");
        Assert.Single(Held(layer).Strokes);
    }

    [AvaloniaFact]
    public void SmudgingOnAHoldStartsFromACopy()
    {
        var vm = OnAHold(out var layer);
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-smudge"));
        Assert.Equal(BrushKind.Smudge, vm.CurrentToolSettingsForTest.Kind);

        Drag(vm, 150, 200, 350, 210);

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        Assert.Equal(2, keyed.Strokes.Count); // the copied line, and the smudge over it
    }

    // ---- edits of the held drawing still copy ------------------------------------

    /// <summary>
    /// B207 under the new default: moving selected lines on a hold must key a
    /// copy, or a blank page would leave the move nothing to move.
    /// </summary>
    [AvaloniaFact]
    public void MovingSelectedLinesOnAHoldStillKeysACopy()
    {
        var vm = OnAHold(out var layer);
        var before = Held(layer).Strokes[0].Points[0].X;

        Assert.True(vm.PickStrokeAt(250, 200, 12));
        Assert.Equal(1, vm.MoveSelectedStrokes(12, 0));

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        var moved = Assert.Single(keyed.Strokes);
        Assert.Equal(before + 12, moved.Points[0].X, 3);
        Assert.Equal(before, Held(layer).Strokes[0].Points[0].X, 3);
    }

    [AvaloniaFact]
    public void AMoveOnAHoldStillKeysACopy()
    {
        var vm = OnAHold(out var layer);

        Assert.True(vm.BeginMove(250, 200, wholeLayer: false));
        vm.UpdateMove(290, 200, axisLock: false);
        vm.EndMove();

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        Assert.Single(keyed.Strokes);
        Assert.Equal(Held(layer).Strokes[0].Points[0].X + 40, keyed.Strokes[0].Points[0].X, 3);
    }

    /// <summary>
    /// Delete with a marquee clears part of what the hold shows — the same act
    /// as a cut — so it keys a copy and clears from it, rather than clearing a
    /// blank page and leaving the frame empty.
    /// </summary>
    [AvaloniaFact]
    public void ClearingASelectionOnAHoldKeysACopyToClearFrom()
    {
        var vm = OnAHold(out var layer);
        vm.SelectAllCommand.Execute(null); // a brush in hand, so "all" is the canvas
        Assert.True(vm.HasSelection);

        vm.DeleteSelectionContentsCommand.Execute(null);

        var keyed = Assert.IsType<Frame>(layer.Cels[1].Frame);
        Assert.Equal(2, keyed.Strokes.Count); // the copied line, and the clear over part of it
        Assert.Contains(keyed.Strokes, s => s.Tool == ToolKind.ClearRegion);
    }

    // ---- reads author nothing ----------------------------------------------------

    /// <summary>
    /// Both of these keyed the cel. Under a copy that was a stray drawing on the
    /// timeline; under the blank default it would empty the frame on screen —
    /// the canvas asks for placements every time it draws a selection.
    /// </summary>
    [AvaloniaFact]
    public void SelectingAllAndReadingPlacementsOnAHoldKeyNothing()
    {
        var vm = OnAHold(out var layer);
        var steps = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Arrow;
        vm.SelectAllCommand.Execute(null);
        _ = vm.GetCurrentFramePlacements();

        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(steps, vm.RecordedStepCount);
    }

    // ---- the stored setting ------------------------------------------------------

    /// <summary>
    /// Every settings file before Q197 says <c>StartANewDrawing</c>, because the
    /// whole object is written defaults and all — a choice and a default are
    /// indistinguishable. It was the default, so it follows the new one.
    /// </summary>
    [AvaloniaFact]
    public void TheRetiredDefaultLoadsAsTheBlankDrawing()
    {
        var loaded = AppSettings.Deserialize("""{ "DrawingOnAHold": "StartANewDrawing" }""");
        Assert.Equal("StartABlankDrawing", loaded.DrawingOnAHold);
        Assert.DoesNotContain("StartANewDrawing", loaded.Serialize());
    }

    [AvaloniaTheory]
    [InlineData("EditTheHeldDrawing")]
    [InlineData("StartFromACopy")]
    [InlineData("StartABlankDrawing")]
    public void AChoiceThatWasMadeLoadsAsItself(string stored)
    {
        Assert.Equal(stored, AppSettings.Deserialize($$"""{ "DrawingOnAHold": "{{stored}}" }""").DrawingOnAHold);
    }

    [AvaloniaTheory]
    [InlineData("Nonsense")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData("startfromacopy")]
    public void AnythingElseReadsAsTheDefault(string stored)
    {
        var vm = new MainViewModel(null);
        vm.Settings.DrawingOnAHold = stored;
        Assert.Equal(HoldDrawing.StartABlankDrawing, vm.DrawingOnAHold);
    }

    [AvaloniaFact]
    public void TheChoiceRoundTripsThroughTheFile()
    {
        var vm = new MainViewModel(null);
        vm.DrawingOnAHold = HoldDrawing.StartFromACopy;
        Assert.Equal(HoldDrawing.StartFromACopy, new MainViewModel(null).DrawingOnAHold);
    }

    [AvaloniaFact]
    public void EveryChoiceHasItsOwnWordsInConfigure()
    {
        var labels = Enum.GetValues<HoldDrawing>().Select(ConfigureWindow.HoldDrawingLabel).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.Equal(3, labels.Count);
    }
}
