using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B207 — editing lines on a held cel must key the cel, not rewrite the
/// drawing it borrows.
/// </summary>
/// <remarks>
/// <para>
/// B206 fixed this for the move, the transform and the placement drag. It did
/// not reach the two gates that resolve a <em>stroke</em>: the pen's node
/// reshaping (<c>CommitPathEdit</c>) and the stroke-selection actions
/// (<c>StrokeEditTarget</c>). Both took the frame the cel displays, which on a
/// hold is an earlier drawing, so a reshape or a nudge landed on a frame the
/// artist was not looking at and showed up on both.
/// </para>
/// <para>
/// The distinction the fix rests on: <b>picking must not author a cel, and an
/// edit that lands must</b>. Clicking about is how an artist looks around, so
/// the keying is at the commit rather than at the pick — the same rule that
/// keeps Ctrl+T followed by Escape from leaving a drawing behind.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class StrokeEditOnAHoldTests : BrushStateIsolated
{
    /// <summary>Frame 0 drawn on, frame 1 a hold of it, the playhead on the hold.</summary>
    private static MainViewModel OnAHold(out Layer layer)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 160, 1);
        vm.MoveStroke(300, 100, 1);
        vm.EndStroke();

        vm.AddFrameCommand.Execute(null);
        layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame = null; // a hold of frame 0
        vm.CurrentFrameIndex = 1;
        return vm;
    }

    private static List<(double X, double Y)> PointsOf(Frame frame) =>
        frame.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList();

    [AvaloniaFact]
    public void PathEditOnAHoldKeysTheCel()
    {
        var vm = OnAHold(out var layer);
        var held = (Frame)layer.Cels[0].Frame!;
        var before = PointsOf(held);
        var strokeId = held.Strokes[0].Id;

        Assert.True(vm.BeginPathEdit(strokeId));
        var grab = vm.GrabPathPart(200, 160, 12);
        Assert.NotEqual(PathHit.Miss, grab);
        vm.DragPathPart(grab, 200, 160, 0, 60);
        Assert.True(vm.CommitPathEdit());

        // The cel is a drawing of its own now...
        var keyed = (Frame?)layer.Cels[1].Frame;
        Assert.NotNull(keyed);
        // ...the reshape landed on it...
        Assert.NotEqual(before, PointsOf(keyed!));
        // ...and the drawing the hold was borrowing is untouched.
        Assert.Equal(before, PointsOf(held));
    }

    [AvaloniaFact]
    public void MovingSelectedLinesOnAHoldKeysTheCel()
    {
        // The other gate, StrokeEditTarget — every stroke-selection action
        // goes through it, so one of them standing in is enough.
        var vm = OnAHold(out var layer);
        var held = (Frame)layer.Cels[0].Frame!;
        var before = PointsOf(held);

        Assert.True(vm.PickStrokeAt(200, 160, 12));
        Assert.Equal(1, vm.MoveSelectedStrokes(12, 0));

        var keyed = (Frame?)layer.Cels[1].Frame;
        Assert.NotNull(keyed);
        Assert.Equal(before[0].X + 12, PointsOf(keyed!)[0].X, 3);
        Assert.Equal(before, PointsOf(held));
    }

    [AvaloniaFact]
    public void TheSelectionSurvivesTheKeying()
    {
        // The copy's strokes carry fresh ids (the rule Frame.Clone documents),
        // so a selection that was not re-pointed would come out empty and the
        // next action would silently do nothing.
        var vm = OnAHold(out _);
        Assert.True(vm.PickStrokeAt(200, 160, 12));
        var count = vm.SelectedStrokes.Count;
        Assert.True(count > 0);

        Assert.Equal(count, vm.MoveSelectedStrokes(4, 0));

        Assert.True(vm.HasStrokeSelection);
        // And they name strokes that actually exist on the drawing now in hand.
        Assert.Equal(count, vm.SelectedStrokes.Count);
    }

    [AvaloniaFact]
    public void PickingALineOnAHoldStillAuthorsNothing()
    {
        // The half that was right all along: looking around must not put a
        // drawing on the timeline. Only an edit that lands keys the cel.
        var vm = OnAHold(out var layer);

        Assert.True(vm.PickStrokeAt(200, 160, 12));
        _ = vm.SelectedStrokes;

        Assert.Null(layer.Cels[1].Frame);
    }

    [AvaloniaFact]
    public void OnAnOrdinaryKeyedCelNothingIsCopied()
    {
        // The ordinary case must not grow a second drawing underneath it.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 100, 1);
        vm.EndStroke();
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        var frame = (Frame)layer.Cels[0].Frame!;
        var strokeId = frame.Strokes[0].Id;

        Assert.True(vm.PickStrokeAt(200, 100, 12));
        Assert.Equal(1, vm.MoveSelectedStrokes(5, 0));

        Assert.Single(frame.Strokes);
        // Same stroke record, not a copy of it.
        Assert.Equal(strokeId, frame.Strokes[0].Id);
    }
}
