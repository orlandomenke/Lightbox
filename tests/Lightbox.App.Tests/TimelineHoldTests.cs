using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// What a mark on a held cel does, and what playback does at the end.
/// </summary>
[Collection("BrushState")]
public sealed class TimelineHoldTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static void Draw(MainViewModel vm)
    {
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void AFilledHoldBecomesADrawingOfItsOwn()
    {
        // The whole point: the timeline shows a drawing where you made one,
        // instead of a dark cel and a mystery stroke on the frame before.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;

        Draw(vm);

        Assert.NotNull(layer.Cels[1].Frame);
        Assert.True(vm.FrameCells[1].IsKeyed, "the cel still reads as a hold on the timeline");
    }

    [AvaloniaFact]
    public void TheHeldDrawingIsLeftAlone()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;

        Draw(vm);

        Assert.Empty(((Frame)layer.Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void KeyingIsASeparateUndoStepFromTheMark()
    {
        // One undo takes the stroke back and leaves the drawing; a second
        // takes the drawing away and restores the hold.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;
        Draw(vm);

        vm.UndoCommand.Execute(null);
        Assert.NotNull(vm.PaintLayer().Cels[1].Frame);
        Assert.Empty(((Frame)vm.PaintLayer().Cels[1].Frame!).Strokes);

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.PaintLayer().Cels[1].Frame);
    }

    [AvaloniaFact]
    public void TheKeyedCelCarriesTheHeldDrawing()
    {
        // Keying must not change the picture: the new drawing starts as a copy
        // of what the hold was showing, and the mark is the only difference.
        // A cel that went blank under the first touch read as the app losing
        // the drawing.
        var vm = Vm();
        Draw(vm);                       // the held drawing, on frame 0
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;

        Draw(vm);

        var keyed = (Frame)layer.Cels[1].Frame!;
        Assert.Equal(2, keyed.Strokes.Count);   // the carried copy plus the mark
        Assert.Single(((Frame)layer.Cels[0].Frame!).Strokes);
        // A copy, not the same record: editing one must never reach the other.
        Assert.NotEqual(((Frame)layer.Cels[0].Frame!).Strokes[0].Id, keyed.Strokes[0].Id);
    }

    [AvaloniaFact]
    public void AMoveOnAHoldKeysTheCel_AndLeavesTheHeldDrawingAlone()
    {
        // The reported bug: standing on frame 2 (a hold) and moving the
        // drawing rewrote frame 1's strokes, so the edit showed on both
        // frames. A move is an edit like a mark — it keys the cel and edits
        // the copy.
        var vm = Vm();
        Draw(vm);
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;
        var heldBefore = ((Frame)layer.Cels[0].Frame!).Strokes[0].Points
            .Select(p => (p.X, p.Y)).ToList();

        Assert.True(vm.BeginMove(25, 25, wholeLayer: false));
        vm.UpdateMove(65, 25, axisLock: false); // 40 to the right
        vm.EndMove();

        var keyed = (Frame?)layer.Cels[1].Frame;
        Assert.NotNull(keyed);
        Assert.Equal(heldBefore[0].X + 40, keyed!.Strokes[0].Points[0].X, 1);
        // The drawing the hold borrowed has not moved.
        Assert.Equal(heldBefore,
            ((Frame)layer.Cels[0].Frame!).Strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
    }

    [AvaloniaFact]
    public void OpeningATransformOnAHoldAndCancellingKeysNothing()
    {
        // Ctrl+T opens the gizmo before the artist has done anything, so
        // keying there would put a drawing on the timeline for a tool the
        // artist merely looked at. The key belongs to the commit: the preview
        // is not an edit (invariant 1), so an abandoned transform must leave
        // the document exactly as it found it.
        var vm = Vm();
        Draw(vm);
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;
        var undos = vm.RecordedStepCount;

        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SkiaSharp.SKMatrix.CreateTranslation(0, 40)); // drag the gizmo
        vm.CancelTransform();                                             // then Escape

        Assert.Null(layer.Cels[1].Frame);
        Assert.Equal(undos, vm.RecordedStepCount);
    }

    [AvaloniaFact]
    public void AMoveThatGoesNowhereKeysNothing()
    {
        // The other half of the same rule: a press and a release with no
        // movement is a click, and a click must not put a drawing on the
        // timeline. EndMove already refuses to record an identity transform;
        // the key has to be just as reluctant.
        var vm = Vm();
        Draw(vm);
        vm.AddFrameCommand.Execute(null);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;

        Assert.True(vm.BeginMove(25, 25, wholeLayer: false));
        vm.EndMove();

        Assert.Null(layer.Cels[1].Frame);
    }

    [AvaloniaFact]
    public void UnderEditTheHeldDrawing_AMoveStillEditsTheHeldDrawing()
    {
        // The Configure switch means what it says for every edit, not only
        // for marks: touching up a deliberate hold moves the source drawing
        // and keys nothing.
        var vm = Vm();
        vm.DrawingOnAHold = HoldDrawing.EditTheHeldDrawing;
        try
        {
            Draw(vm);
            vm.AddFrameCommand.Execute(null);
            var layer = vm.PaintLayer();
            layer.Cels[1].Frame = null;
            vm.CurrentFrameIndex = 1;
            var beforeX = ((Frame)layer.Cels[0].Frame!).Strokes[0].Points[0].X;

            Assert.True(vm.BeginMove(25, 25, wholeLayer: false));
            vm.UpdateMove(65, 25, axisLock: false);
            vm.EndMove();

            Assert.Null(layer.Cels[1].Frame);
            Assert.Equal(beforeX + 40, ((Frame)layer.Cels[0].Frame!).Strokes[0].Points[0].X, 1);
        }
        finally
        {
            vm.DrawingOnAHold = HoldDrawing.StartANewDrawing;
        }
    }

    [AvaloniaFact]
    public void AnOrdinaryKeyedCelIsStillDrawnOnDirectly()
    {
        // The change is about holds. A cel that is already a drawing must not
        // suddenly grow a second one underneath it.
        var vm = Vm();
        Draw(vm);
        Draw(vm);

        Assert.Equal(2, ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count);
    }

    // ---- playback ------------------------------------------------------------------

    [AvaloniaFact]
    public void PlaybackWrapsByDefault()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = vm.Doc.Scene.FrameCount - 1;

        vm.StepPlayback();

        Assert.Equal(0, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void WithLoopingOffItStopsOnTheLastFrame()
    {
        var vm = Vm();
        try
        {
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            vm.LoopPlayback = false;
            var last = vm.Doc.Scene.FrameCount - 1;
            vm.CurrentFrameIndex = last;

            vm.StepPlayback();

            Assert.Equal(last, vm.CurrentFrameIndex);
            // And it actually stops, rather than sitting there claiming to play.
            Assert.False(vm.IsPlaying);
        }
        finally
        {
            vm.LoopPlayback = true;
        }
    }

    // ---- how big the timeline is ------------------------------------------------------

    [AvaloniaFact]
    public void TheRulerPitchFollowsTheFrameWidth()
    {
        // Two independent constants is how the numbers stop sitting over the
        // cells they name.
        var vm = Vm();
        var before = vm.TimelineRulerCellWidth;

        vm.TimelineFrameWidth += 10;

        Assert.Equal(before + 10, vm.TimelineRulerCellWidth, 3);
        Assert.Equal(vm.TimelineFrameWidth + 2, vm.TimelineRulerCellWidth, 3);
    }

    [AvaloniaFact]
    public void TheFrameWidthStaysWithinReadableBounds()
    {
        var vm = Vm();

        vm.TimelineFrameWidth = 1000;
        Assert.True(vm.TimelineFrameWidth <= 72);

        vm.TimelineFrameWidth = 1;
        Assert.True(vm.TimelineFrameWidth >= 14);
    }
}
