using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

public class LayerDeletionTests
{
    [AvaloniaFact]
    public void DeleteLayer_RemovesIt_AndKeepsAValidActiveIndex()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        Assert.Equal(4, vm.Doc.Scene.Layers.Count); // paper + the opening layer + two
        Assert.Equal(3, vm.ActiveLayerIndex);

        vm.DeleteActiveLayerCommand.Execute(null); // delete the top layer
        Assert.Equal(3, vm.Doc.Scene.Layers.Count);
        Assert.Equal(2, vm.ActiveLayerIndex);
    }

    [AvaloniaFact]
    public void DeletingTheLastLayer_RegrowsABlankOne()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(10, 10, 1);
        vm.EndStroke();
        var oldId = vm.PaintLayer().Id;

        vm.DeleteActiveLayerCommand.Execute(null);

        // The paper survives; the drawing layer is replaced by a blank one,
        // because a document left with nothing but locked paper has nothing
        // to draw on.
        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        var layer = vm.PaintLayer();
        Assert.NotEqual(oldId, layer.Id);
        Assert.NotNull(layer.Cels[0].Frame);
        Assert.Empty(((Frame)layer.Cels[0].Frame!).Strokes);

        vm.UndoCommand.Execute(null); // deletion is one undo step
        Assert.Equal(oldId, vm.PaintLayer().Id);
        Assert.Single((vm.PaintedCel()).Strokes);
    }

    [AvaloniaFact]
    public void ClearLayer_BlanksEveryDrawing_ButKeepsTheTiming()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(10, 10, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(30, 30, 1);
        vm.EndStroke();
        var layer = vm.PaintLayer();
        Assert.Single(((Frame)layer.Cels[0].Frame!).Strokes);
        Assert.Single(((Frame)layer.Cels[1].Frame!).Strokes);

        vm.ClearActiveLayerCommand.Execute(null);

        layer = vm.PaintLayer();
        Assert.NotNull(layer.Cels[0].Frame);              // cels stay keyed (timing intact)
        Assert.NotNull(layer.Cels[1].Frame);
        Assert.Empty(((Frame)layer.Cels[0].Frame!).Strokes);
        Assert.Empty(((Frame)layer.Cels[1].Frame!).Strokes);

        vm.UndoCommand.Execute(null);
        Assert.Single((vm.PaintedCel()).Strokes);
    }

    [AvaloniaFact]
    public void DockerVisibilityToggles_RoundTrip()
    {
        var vm = new MainViewModel(null);
        Assert.True(vm.ColorDockerVisible);
        vm.ToggleColorDockerCommand.Execute(null);
        Assert.False(vm.ColorDockerVisible);
        vm.ToggleColorDockerCommand.Execute(null);
        Assert.True(vm.ColorDockerVisible);

        vm.ToggleSheetsDockerCommand.Execute(null);
        Assert.False(vm.SheetsDockerVisible);
    }
}
