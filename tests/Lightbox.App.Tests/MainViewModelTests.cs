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

        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        var stroke = Assert.Single(frame.Strokes);
        Assert.Equal(3, stroke.Points.Count);
        Assert.Equal(new StrokePoint(10, 10, 0.5), stroke.Points[0]);
    }

    [AvaloniaFact]
    public void PaintOnHoldFrame_TargetsExposedKey()
    {
        var vm = NewVm();
        vm.AddFrameCommand.Execute(null); // frame 2 keyed
        // Make frame 2 a hold.
        var layer = vm.Doc.Scene.Layers[0];
        layer.Cels[1].Frame = null;
        vm.CurrentFrameIndex = 1;

        vm.BeginStroke(5, 5, 0.5);
        vm.MoveStroke(20, 20, 0.5);
        vm.EndStroke();

        var key = (PaintedFrame)layer.Cels[0].Frame!;
        Assert.Single(key.Strokes);
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

        PaintedFrame Frame() => (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
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
        var layer = vm.Doc.Scene.Layers[0];
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
        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
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
