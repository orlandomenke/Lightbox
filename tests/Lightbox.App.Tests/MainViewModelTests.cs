using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

public class MainViewModelTests
{
    private static MainViewModel NewVm() => new();

    [AvaloniaFact]
    public void PaintStroke_LandsInDocument()
    {
        var vm = NewVm();
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(50, 50, 0.6);
        vm.MoveStroke(90, 20, 0.7);
        vm.EndStroke();

        var frame = vm.PaintedCel();
        var stroke = Assert.Single(frame.Strokes);
        Assert.Equal(3, stroke.Points.Count);
        Assert.Equal(new StrokePoint(10, 10, 0.5), stroke.Points[0]);
    }

    [AvaloniaFact]
    public void PaintOnAHoldStartsANewDrawing()
    {
        // The default, and the animator's answer. This test used to assert the
        // opposite — that the mark went onto the drawing being held — which is
        // true to the exposure sheet and wrong to look at: the stroke appeared
        // on the earlier frame too, and the cel you drew on stayed empty and
        // dark. Changed deliberately, on request.
        var vm = NewVm();
        vm.AddFrameCommand.Execute(null); // frame 2 keyed
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null;       // make frame 2 a hold
        vm.CurrentFrameIndex = 1;

        vm.BeginStroke(5, 5, 0.5);
        vm.MoveStroke(20, 20, 0.5);
        vm.EndStroke();

        Assert.NotNull(layer.Cels[1].Frame);
        Assert.Single(((PaintedFrame)layer.Cels[1].Frame!).Strokes);
        // And the drawing that was being held is untouched.
        Assert.Empty(((PaintedFrame)layer.Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void PaintOnAHoldCanStillEditTheHeldDrawing()
    {
        // The other honest reading, for touching up a held pose without
        // breaking the hold. Edit ▸ Configure ▸ Timeline.
        var vm = NewVm();
        vm.DrawingOnAHold = HoldDrawing.EditTheHeldDrawing;
        try
        {
            vm.AddFrameCommand.Execute(null);
            var layer = vm.PaintLayer();
            layer.Cels[1].Frame = null;
            vm.CurrentFrameIndex = 1;

            vm.BeginStroke(5, 5, 0.5);
            vm.MoveStroke(20, 20, 0.5);
            vm.EndStroke();

            Assert.Null(layer.Cels[1].Frame);     // still a hold
            Assert.Single(((PaintedFrame)layer.Cels[0].Frame!).Strokes);
        }
        finally
        {
            vm.DrawingOnAHold = HoldDrawing.StartANewDrawing;
        }
    }

    [AvaloniaFact]
    public void FrameCommands_KeepDocumentAndCellsConsistent()
    {
        var vm = NewVm();
        vm.AddFrameCommand.Execute(null);
        vm.DuplicateFrameCommand.Execute(null);
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Equal(3, vm.FrameCells.Count(c => !c.IsVirtual));
        Assert.Equal(2, vm.CurrentFrameIndex);

        vm.DeleteFrameCommand.Execute(null);
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        Assert.Equal(2, vm.FrameCells.Count(c => !c.IsVirtual));
        Assert.True(vm.CurrentFrameIndex <= 1);
    }

    [AvaloniaFact]
    public void UndoRedo_RoundTripsPaint()
    {
        var vm = NewVm();
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 30, 0.5);
        vm.EndStroke();

        PaintedFrame Frame() => vm.PaintedCel();
        Assert.Single(Frame().Strokes);

        vm.UndoCommand.Execute(null);
        Assert.Empty(Frame().Strokes);

        vm.RedoCommand.Execute(null);
        Assert.Single(Frame().Strokes);
    }

    [AvaloniaFact]
    public void InsertInbetweens_FillsTimeline()
    {
        var vm = NewVm();

        // Key A with one stroke.
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();

        // Key B with the stroke moved down.
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(0, 100, 0.5);
        vm.MoveStroke(50, 100, 0.5);
        vm.EndStroke();

        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 3;
        vm.InsertInbetweensCommand.Execute(null);

        Assert.Equal(5, vm.Doc.Scene.FrameCount);
        var layer = vm.PaintLayer();
        for (var i = 1; i <= 3; i++)
        {
            var tween = Assert.IsType<PaintedFrame>(layer.Cels[i].Frame);
            var s = Assert.Single(tween.Strokes);
            // t = i/4 → y = 100 * i/4 under EaseInOut (mid tween exactly 50).
            if (i == 2) Assert.Equal(50, s.Points[0].Y, 3);
        }
    }

    [AvaloniaFact]
    public void SnapshotPublished_OnPaintAndNavigation()
    {
        var vm = NewVm();
        var count = 0;
        vm.SnapshotChanged += _ => count++;

        vm.PublishSnapshot();
        Assert.Equal(1, count);

        vm.BeginStroke(1, 1, 0.5);
        vm.MoveStroke(20, 20, 0.5);
        vm.EndStroke();
        Assert.True(count > 1);

        var before = count;
        vm.AddFrameCommand.Execute(null);
        Assert.True(count > before);
    }

    [AvaloniaFact]
    public void ReplaceDocument_ResetsState()
    {
        var vm = NewVm();
        vm.AddFrameCommand.Execute(null);
        Assert.Equal(1, vm.CurrentFrameIndex);

        var doc = DocumentFactory.CreateDoc(100, 100, 24);
        vm.ReplaceDocument(DocJson.Clone(doc));

        Assert.Equal(0, vm.CurrentFrameIndex);
        Assert.Equal(1, vm.Doc.Scene.FrameCount);
        Assert.Equal(100, vm.Doc.Scene.Width);
        Assert.Single(vm.FrameCells, c => !c.IsVirtual);
    }

    [AvaloniaFact]
    public void TogglePlayback_FlipsState()
    {
        var vm = NewVm();
        Assert.False(vm.IsPlaying);
        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        vm.TogglePlaybackCommand.Execute(null);
        Assert.False(vm.IsPlaying);
    }

    [AvaloniaFact]
    public void PaintWhilePlaying_IsIgnored()
    {
        var vm = NewVm();
        vm.TogglePlaybackCommand.Execute(null);
        vm.BeginStroke(1, 1, 0.5);
        vm.EndStroke();
        var frame = vm.PaintedCel();
        Assert.Empty(frame.Strokes);
    }
}

public class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_ConstructsAndShows()
    {
        var window = new Views.MainWindow();
        window.Show();
        Assert.NotNull(window.DataContext);
        Assert.IsType<MainViewModel>(window.DataContext);
        window.Close();
    }
}
