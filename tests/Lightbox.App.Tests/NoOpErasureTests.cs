using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B237: an erasure that rubbed nothing out is not recorded — not as a stroke,
/// and not as a step in the history.
/// </summary>
/// <remarks>
/// <para>
/// The owner's rule, in their words: <i>"Erasing something that wasn't there to
/// begin with should not be kept. Not as a stroke, not in the undo history. As
/// it did nothing to nothing."</i> It is the other half of B232 — that one made
/// an erasure impossible to take hold of, which left a stray one impossible to
/// remove; this one stops the stray existing.
/// </para>
/// <para>
/// Everything here drives the real begin/move/end pipeline rather than building
/// strokes by hand, because the decision is made across the commit and a test
/// that assembled a <see cref="Stroke"/> itself would never reach it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class NoOpErasureTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Fresh() => new(null) { SmoothStrokes = false };

    private static List<Stroke> Strokes(MainViewModel vm) =>
        ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes;

    /// <summary>Frame 0 drawn on, frame 1 a hold of it, the playhead on the hold.</summary>
    private static MainViewModel OnAHold(out Layer layer)
    {
        var vm = Fresh();
        Drag(vm, 100, 200, 400, 200);

        vm.AddFrameCommand.Execute(null);
        layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame = null;                  // a hold of frame 0
        vm.CurrentFrameIndex = 1;
        return vm;
    }

    /// <summary>Draw a line with whatever tool is in hand.</summary>
    private static void Drag(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
    }

    // ---- the reported case ---------------------------------------------------

    /// <summary>
    /// An eraser swept across blank canvas leaves nothing behind: no stroke on
    /// the drawing, and no step for Ctrl+Z to find.
    /// </summary>
    [AvaloniaFact]
    public void ErasingBlankCanvasRecordsNothing()
    {
        var vm = Fresh();
        var stepsBefore = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Eraser;
        Drag(vm, 100, 100, 300, 300);

        output.WriteLine($"strokes: {Strokes(vm).Count}, steps: {vm.RecordedStepCount - stepsBefore}");
        Assert.Empty(Strokes(vm));
        Assert.Equal(stepsBefore, vm.RecordedStepCount);
    }

    /// <summary>
    /// The test that stops the one above passing vacuously: an eraser that
    /// <em>does</em> rub something out is recorded exactly as it always was.
    /// Without this, a build where the eraser never records anything at all
    /// looks perfect.
    /// </summary>
    [AvaloniaFact]
    public void ErasingOverInkIsRecordedAsBefore()
    {
        var vm = Fresh();
        Drag(vm, 100, 200, 300, 200);
        Assert.Single(Strokes(vm));
        var stepsAfterInk = vm.RecordedStepCount;

        vm.ActiveTool = ToolId.Eraser;
        Drag(vm, 150, 200, 250, 200);

        Assert.Equal(2, Strokes(vm).Count);
        Assert.Equal(ToolKind.Eraser, Strokes(vm)[^1].Tool);
        Assert.True(vm.RecordedStepCount > stepsAfterInk);
    }

    /// <summary>
    /// The case that decided pixels over geometry, and exactness over a cheap
    /// scan (Q102): an eraser drawn down the gap between two lines has ink well
    /// inside its bounding box and touches none of it. A bounding-box test would
    /// keep this stroke; a record-geometry test would too, once the brush is
    /// narrow enough to miss both.
    /// </summary>
    [AvaloniaFact]
    public void ErasingBetweenTwoLinesWithoutTouchingThemRecordsNothing()
    {
        var vm = Fresh();
        Drag(vm, 100, 100, 400, 100);   // above
        Drag(vm, 100, 400, 400, 400);   // below
        var before = Strokes(vm).Count;
        var stepsBefore = vm.RecordedStepCount;

        // Straight down the empty middle. Its bounding box spans both lines.
        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 20;
        Drag(vm, 250, 200, 250, 300);

        output.WriteLine($"strokes {before} -> {Strokes(vm).Count}");
        Assert.Equal(before, Strokes(vm).Count);
        Assert.Equal(stepsBefore, vm.RecordedStepCount);
    }

    // ---- the cel a no-op erase must not leave behind -------------------------

    /// <summary>
    /// Q102: erasing on a hold keys the cel before the stroke lands, so an erase
    /// that turns out to have done nothing has to take the key back with it.
    /// Otherwise a sweep over blank canvas breaks the hold and adds a drawing to
    /// the exposure sheet — a change to the timing, from a gesture that changed
    /// no pixels, and much harder to notice than a stray stroke.
    /// </summary>
    [AvaloniaFact]
    public void ErasingNothingOnAHoldLeavesTheHoldAlone()
    {
        var vm = OnAHold(out var layer);
        var stepsBefore = vm.RecordedStepCount;

        // Well clear of the held line, which runs along y=200 from x=100.
        vm.ActiveTool = ToolId.Eraser;
        Drag(vm, 600, 420, 750, 500);

        output.WriteLine($"cel 1 keyed: {layer.Cels[1].Frame is not null}");
        Assert.Null(layer.Cels[1].Frame);            // still a hold
        Assert.Equal(stepsBefore, vm.RecordedStepCount);
    }

    /// <summary>
    /// And the same gesture over ink still keys, or the no-op rule would have
    /// quietly broken erasing on a hold altogether.
    /// </summary>
    [AvaloniaFact]
    public void ErasingSomethingOnAHoldStillKeysTheCel()
    {
        var vm = OnAHold(out var layer);

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 60;
        Drag(vm, 150, 200, 350, 200);                // right across the held line

        Assert.NotNull(layer.Cels[1].Frame);
        Assert.Contains(((Frame)layer.Cels[1].Frame!).Strokes, s => s.Tool == ToolKind.Eraser);
    }

    // ---- undo still means what it meant --------------------------------------

    /// <summary>
    /// The consequence, asserted rather than left implicit: because the erase
    /// was never recorded, Ctrl+Z after it takes back the stroke <em>before</em>
    /// it. That is what "not in the undo history" costs, and it is the answer
    /// the rule asked for.
    /// </summary>
    [AvaloniaFact]
    public void UndoAfterANoOpEraseTakesBackTheStrokeBeforeIt()
    {
        var vm = Fresh();
        Drag(vm, 100, 200, 300, 200);
        Assert.Single(Strokes(vm));

        vm.ActiveTool = ToolId.Eraser;
        Drag(vm, 600, 450, 700, 500);                // nowhere near the line

        vm.UndoCommand.Execute(null);

        Assert.Empty(Strokes(vm));
    }

    // ---- the area form: clearing a selection that held nothing ---------------

    /// <summary>
    /// Q102 said the rule is the act, not the tool: clearing an empty selection
    /// is erasing nothing with a box instead of a brush, and is recorded the
    /// same way — which is to say not at all.
    /// </summary>
    [AvaloniaFact]
    public void ClearingAnEmptySelectionRecordsNothing()
    {
        var vm = Fresh();
        vm.SelectAllCommand.Execute(null);
        Assert.True(vm.HasSelection);
        var stepsBefore = vm.RecordedStepCount;

        vm.DeleteSelectionContentsCommand.Execute(null);

        output.WriteLine($"strokes: {Strokes(vm).Count}, steps: {vm.RecordedStepCount - stepsBefore}");
        Assert.Empty(Strokes(vm));
        Assert.Equal(stepsBefore, vm.RecordedStepCount);
    }

    /// <summary>
    /// And clearing a selection that held something is recorded exactly as it
    /// was — the guard that stops the rule quietly disabling Delete.
    /// </summary>
    [AvaloniaFact]
    public void ClearingASelectionWithInkInItStillRecords()
    {
        var vm = Fresh();
        Drag(vm, 100, 200, 300, 200);
        vm.SelectAllCommand.Execute(null);
        var stepsBefore = vm.RecordedStepCount;

        vm.DeleteSelectionContentsCommand.Execute(null);

        Assert.Equal(ToolKind.ClearRegion, Strokes(vm)[^1].Tool);
        Assert.True(vm.RecordedStepCount > stepsBefore);
    }
}
