using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Merge layer down (Ctrl+E) from the view model's side: one undo step, the
/// merged layer keeps the lower layer's identity, and layers that refuse
/// edits refuse merges. What the merged drawing contains is
/// <c>Lightbox.Raster.Tests.LayerMergeTests</c>' subject.
/// </summary>
public class LayerMergeVmTests
{
    private static MainViewModel TwoDrawnLayers()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(40, 40, 1);
        vm.EndStroke();
        vm.AddPaintedLayerCommand.Execute(null); // adds on top and activates it
        vm.BeginStroke(120, 120, 1);
        vm.EndStroke();
        return vm;
    }

    [AvaloniaFact]
    public void MergeDown_JoinsTheStrokes_AndLandsOnTheLowerLayer()
    {
        var vm = TwoDrawnLayers();
        var lowerId = vm.Doc.Scene.Layers[0].Id;
        var lowerName = vm.Doc.Scene.Layers[0].Name;

        Assert.False(vm.MergeWouldBake(null));
        vm.MergeActiveLayerDownCommand.Execute(null);

        Assert.Single(vm.Doc.Scene.Layers);
        var merged = vm.Doc.Scene.Layers[0];
        Assert.Equal(lowerId, merged.Id);       // the lower layer's identity survives
        Assert.Equal(lowerName, merged.Name);
        Assert.Equal(0, vm.ActiveLayerIndex);   // the merge is where the artist now works
        Assert.Equal(2, ((Frame)merged.Cels[0].Frame!).Strokes.Count);
    }

    [AvaloniaFact]
    public void MergeDown_IsOneUndoStep()
    {
        var vm = TwoDrawnLayers();
        var upperId = vm.PaintLayer().Id;

        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.Equal(upperId, vm.Doc.Scene.Layers[1].Id);
        Assert.Single(((Frame)vm.Doc.Scene.Layers[1].Cels[0].Frame!).Strokes);
        Assert.Single(((Frame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void MergeWouldBake_SeesTheUpperLayersOpacity()
    {
        var vm = TwoDrawnLayers();
        vm.ActiveLayerOpacity = 50;
        Assert.True(vm.MergeWouldBake(null));

        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);
        Assert.True(((Frame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).HasBaseline);
    }

    [AvaloniaFact]
    public void TheBottomLayer_HasNothingToMergeInto()
    {
        var vm = VmLayers.BareVm(); // one drawing layer, no paper
        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);
    }

    [AvaloniaFact]
    public void MergingIntoTheLockedPaper_Refuses()
    {
        var vm = VmLayers.PaperVm(); // locked Background under the drawing layer
        vm.SmoothStrokes = false;
        vm.BeginStroke(40, 40, 1);
        vm.EndStroke();

        vm.MergeActiveLayerDownCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.Layers.Count); // nothing merged
        Assert.Contains("locked", vm.AiStatus);
    }
}
