using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

public class PlaybackTransportTests
{
    private static MainViewModel VmWithFrames(int frames)
    {
        var vm = new MainViewModel(null);
        for (var i = 1; i < frames; i++) vm.AddFrameCommand.Execute(null);
        return vm;
    }

    [AvaloniaFact]
    public void StepPlayback_LoopsForward_InsideSelectedRange()
    {
        var vm = VmWithFrames(6);
        vm.PlaybackStartFrame = 1;
        vm.PlaybackEndFrame = 3;
        vm.PlayCommand.Execute(null);

        vm.CurrentFrameIndex = 3;
        vm.StepPlayback();
        Assert.Equal(1, vm.CurrentFrameIndex); // wrapped to range start

        vm.PauseCommand.Execute(null);
        Assert.False(vm.IsPlaying);
    }

    [AvaloniaFact]
    public void StepPlayback_Backwards_WrapsToRangeEnd()
    {
        var vm = VmWithFrames(6);
        vm.PlaybackStartFrame = 1;
        vm.PlaybackEndFrame = 3;
        vm.PlayBackwardsCommand.Execute(null);

        vm.CurrentFrameIndex = 1;
        vm.StepPlayback();
        Assert.Equal(3, vm.CurrentFrameIndex); // wrapped to range end
        vm.StepPlayback();
        Assert.Equal(2, vm.CurrentFrameIndex);
        vm.PauseCommand.Execute(null);
    }

    [AvaloniaFact]
    public void GoToStartAndEnd_UseRangeWhenSet_ElseTimelineBounds()
    {
        var vm = VmWithFrames(6);
        vm.CurrentFrameIndex = 2;

        vm.GoToStartFrameCommand.Execute(null);
        Assert.Equal(0, vm.CurrentFrameIndex);
        vm.GoToEndFrameCommand.Execute(null);
        Assert.Equal(5, vm.CurrentFrameIndex);

        vm.PlaybackStartFrame = 1;
        vm.PlaybackEndFrame = 3;
        vm.GoToStartFrameCommand.Execute(null);
        Assert.Equal(1, vm.CurrentFrameIndex);
        vm.GoToEndFrameCommand.Execute(null);
        Assert.Equal(3, vm.CurrentFrameIndex);

        vm.ClearPlaybackRange();
        vm.GoToEndFrameCommand.Execute(null);
        Assert.Equal(5, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void KeyframeNavigation_SkipsHolds()
    {
        var vm = VmWithFrames(5);
        var layer = vm.PaintLayer();
        layer.Cels[1].Frame = null; // holds at 1 and 3
        layer.Cels[3].Frame = null;

        vm.CurrentFrameIndex = 4;
        vm.PreviousKeyframeCommand.Execute(null);
        Assert.Equal(2, vm.CurrentFrameIndex);
        vm.PreviousKeyframeCommand.Execute(null);
        Assert.Equal(0, vm.CurrentFrameIndex);

        vm.NextKeyframeCommand.Execute(null);
        Assert.Equal(2, vm.CurrentFrameIndex);
        vm.NextKeyframeCommand.Execute(null);
        Assert.Equal(4, vm.CurrentFrameIndex);
        vm.NextKeyframeCommand.Execute(null); // none after — stays put
        Assert.Equal(4, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void RangeSelection_GreysOutCellsOutsideIt()
    {
        var vm = VmWithFrames(5);
        vm.PlaybackStartFrame = 1;
        vm.PlaybackEndFrame = 3;

        var cells = vm.FrameCells;
        Assert.True(cells[0].OutOfRange);
        Assert.False(cells[1].OutOfRange);
        Assert.False(cells[3].OutOfRange);
        Assert.True(cells[4].OutOfRange);

        vm.ClearPlaybackRange();
        Assert.False(cells[0].OutOfRange);
        Assert.False(cells[4].OutOfRange);
    }
}

public class FrameInsertionTests
{
    [AvaloniaFact]
    public void InsertOnVirtualCell_ExtendsTimeline_PaddingAllLayers()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null);

        var virtualCell = vm.LayerRows[1].Cells[4]; // the layer under the new one, beyond frame 1
        Assert.True(virtualCell.IsVirtual);
        vm.InsertFrameAt(virtualCell, FrameRole.Key);

        Assert.Equal(5, vm.Doc.Scene.FrameCount);
        Assert.All(vm.Doc.Scene.Layers, l => Assert.Equal(5, l.Cels.Count));
        Assert.NotNull(vm.PaintLayer().Cels[4].Frame);
        Assert.Null(vm.Doc.Scene.Layers[2].Cels[4].Frame); // the other layer holds
        Assert.Equal(4, vm.CurrentFrameIndex);
        Assert.Equal(1, vm.ActiveLayerIndex); // the cell's layer became active
    }

    [AvaloniaFact]
    public void InsertRoles_ColorTheCells_AndUndoRemovesFrame()
    {
        var vm = new MainViewModel(null);
        var cells = vm.FrameCells;

        vm.InsertFrameAt(cells[1], FrameRole.Breakdown);
        Assert.True(vm.FrameCells[1].IsKeyed);
        Assert.True(vm.FrameCells[1].IsBreakdown);

        vm.InsertFrameAt(vm.FrameCells[2], FrameRole.Inbetween);
        Assert.True(vm.FrameCells[2].IsInbetween);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(1, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void InsertOnExistingFrame_JustRemarksItsRole()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(5, 5, 0.5);
        vm.MoveStroke(20, 20, 0.5);
        vm.EndStroke();

        vm.InsertFrameAt(vm.FrameCells[0], FrameRole.Breakdown);

        var frame = vm.PaintedCel();
        Assert.Equal(FrameRole.Breakdown, frame.Role);
        Assert.Single(frame.Strokes); // artwork untouched
    }

    [AvaloniaFact]
    public void GeneratedInbetweens_AreMarkedAsInbetweens()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(0, 100, 0.5);
        vm.MoveStroke(50, 100, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 2;
        vm.InsertInbetweensCommand.Execute(null);

        var layer = vm.PaintLayer();
        Assert.Equal(FrameRole.Inbetween, layer.Cels[1].Frame!.Role);
        Assert.Equal(FrameRole.Inbetween, layer.Cels[2].Frame!.Role);
        Assert.Equal(FrameRole.Key, layer.Cels[0].Frame!.Role);
    }
}

public class FrameRoleSerializationTests
{
    [AvaloniaFact]
    public void Role_SurvivesSaveAndLoad_DefaultsToKeyForLegacyDocs()
    {
        var vm = new MainViewModel(null);
        vm.InsertFrameAt(vm.FrameCells[1], FrameRole.Breakdown);

        var restored = DocJson.Deserialize(DocJson.Serialize(vm.Doc));
        var drawn = restored.Scene.Layers[vm.ActiveLayerIndex];
        Assert.Equal(FrameRole.Breakdown, drawn.Cels[1].Frame!.Role);
        Assert.Equal(FrameRole.Key, drawn.Cels[0].Frame!.Role);
    }
}
