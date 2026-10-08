using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B406 through the real commit: Ctrl+T and the whole-layer transform carry a
/// drawing's anchors and collision boxes, a selection-limited one leaves them,
/// and undo puts them back. Written before the fix, as the plan for it.
/// </summary>
[Collection("BrushState")]
public class TransformCarriesMarksAppTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        Mark(Drawing(vm, 0));
        return vm;
    }

    private static void Mark(Frame f)
    {
        f.Anchors = new() { ["hand"] = new AnchorPoint(250, 200, 0) };
        f.Shapes = new() { ["hurt"] = new ShapeBox(200, 180, 100, 40) };
    }

    private static Frame Drawing(MainViewModel vm, int cel) =>
        (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[cel].Frame!;

    [AvaloniaFact]
    public void ScalingADrawingScalesItsSocketAndHurtbox_AndUndoPutsThemBack()
    {
        var vm = Vm();
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(250, 200, 2, 2, 0, 0, 0);

        Assert.Equal(new AnchorPoint(250, 200, 0), Drawing(vm, 0).Anchors!["hand"]);       // on the pivot: stays
        Assert.Equal(new ShapeBox(150, 160, 200, 80), Drawing(vm, 0).Shapes!["hurt"]);    // twice the size round it

        vm.UndoCommand.Execute(null);
        Assert.Equal(new ShapeBox(200, 180, 100, 40), Drawing(vm, 0).Shapes!["hurt"]);
    }

    [AvaloniaFact]
    public void MovingADrawingMovesItsSocket()
    {
        var vm = Vm();
        Assert.True(vm.BeginMove(250, 200, wholeLayer: false));
        vm.UpdateMove(290, 230, axisLock: false);
        vm.EndMove();

        var hand = Drawing(vm, 0).Anchors!["hand"];
        Assert.Equal(290, hand.X, 3);
        Assert.Equal(230, hand.Y, 3);
    }

    [AvaloniaFact]
    public void TheWholeLayerTransformCarriesEveryDrawingsMarks()
    {
        // The case that found it: resize a character on every frame.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        Mark(Drawing(vm, 1));
        vm.CurrentFrameIndex = 0;

        vm.TransformScope = TransformScope.ActiveLayerAllFrames;
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 0.5, 0.5, 0, 0, 0);

        Assert.Equal(new ShapeBox(100, 90, 50, 20), Drawing(vm, 0).Shapes!["hurt"]);
        Assert.Equal(new ShapeBox(100, 90, 50, 20), Drawing(vm, 1).Shapes!["hurt"]);
    }

    [AvaloniaFact]
    public void TransformingAHeldFrameKeysADrawingThatKeepsItsMarks()
    {
        // Adversary's repro: Ctrl+T on a hold keys the cel with a copy of the
        // held drawing first, and that copy had lines and pixels only — so the
        // new drawing came out with no socket and no hurtbox at all.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.ClearCelAt(vm.LayerRows[^1].Cells.First(c => c.Index == 1));   // frame 1 holds frame 0
        vm.CurrentFrameIndex = 1;

        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(250, 200, 2, 2, 0, 0, 0);

        var keyed = Drawing(vm, 1);
        Assert.NotNull(keyed.Shapes);
        Assert.Equal(new ShapeBox(150, 160, 200, 80), keyed.Shapes!["hurt"]);   // carried, at the new size
        Assert.True(keyed.Anchors?.ContainsKey("hand"), "the keyed drawing lost its socket");
        Assert.Equal(new ShapeBox(200, 180, 100, 40), Drawing(vm, 0).Shapes!["hurt"]);  // the held original untouched
    }

    [AvaloniaFact]
    public void PaintingOnAHeldFrameKeysADrawingThatKeepsItsMarks()
    {
        // The same copy, the same loss: a mark drawn on a hold (in "edit a
        // copy" mode) keyed the cel with lines and pixels only, so the frame
        // lost the socket and hurtbox it had been showing through the hold.
        var vm = Vm();
        vm.DrawingOnAHold = HoldDrawing.StartFromACopy;
        vm.AddFrameCommand.Execute(null);
        vm.ClearCelAt(vm.LayerRows[^1].Cells.First(c => c.Index == 1));
        vm.CurrentFrameIndex = 1;

        vm.BeginStroke(200, 300, 1);
        vm.MoveStroke(260, 300, 1);
        vm.EndStroke();

        var keyed = Drawing(vm, 1);
        Assert.NotSame(Drawing(vm, 0), keyed);
        Assert.Equal(new ShapeBox(200, 180, 100, 40), keyed.Shapes?["hurt"]);
        Assert.Equal(new AnchorPoint(250, 200, 0), keyed.Anchors?["hand"]);
    }

    [AvaloniaFact]
    public void ASelectionLimitedTransformLeavesThem()
    {
        var vm = Vm();
        vm.ApplySelectionShape(
            [new(150, 150, 1), new(350, 150, 1), new(350, 250, 1), new(150, 250, 1)],
            add: false, subtract: false);
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, -50);

        Assert.Equal(new AnchorPoint(250, 200, 0), Drawing(vm, 0).Anchors!["hand"]);
        Assert.Equal(new ShapeBox(200, 180, 100, 40), Drawing(vm, 0).Shapes!["hurt"]);
    }

    [AvaloniaFact]
    public void TheStatusLineSaysWhenATurnWidenedABox()
    {
        var vm = Vm();
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(250, 200, 1, 1, 0.3, 0, 0);
        Assert.Contains("collision box", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }
}
