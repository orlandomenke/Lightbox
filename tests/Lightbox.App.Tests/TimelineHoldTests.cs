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
