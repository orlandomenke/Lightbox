using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Q268 from the artist's side: a merged drawing is picked whole, moves whole,
/// and Split group puts a merge back on a layer of its own in one undo step.
/// What a split writes is <c>Lightbox.Raster.Tests.SplitGroupTests</c>' subject.
/// </summary>
public class SplitGroupVmTests
{
    /// <summary>Two drawn layers, the upper at half opacity, merged: one group on the merged layer.</summary>
    private static MainViewModel Merged(out string upperName)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(160, 40, 1);
        vm.EndStroke();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.BeginStroke(40, 120, 1);
        vm.MoveStroke(160, 120, 1);
        vm.EndStroke();
        vm.ActiveLayerOpacity = 50;
        upperName = vm.PaintLayer().Name;
        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);
        Assert.Contains(Drawing(vm).Strokes, s => s.Group is not null);
        return vm;
    }

    private static Frame Drawing(MainViewModel vm, int layer = 0) => (Frame)vm.Doc.Scene.Layers[layer].Cels[0].Frame!;

    [AvaloniaFact]
    public void SplitGroupPutsTheMergeBackOnALayerOfItsOwn()
    {
        var vm = Merged(out var upperName);

        Assert.True(vm.SplitGroupOn(null));

        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        var split = vm.Doc.Scene.Layers[1];
        Assert.Equal(upperName, split.Name);
        Assert.Equal(0.5, split.Opacity);
        Assert.Equal(1, vm.ActiveLayerIndex);
        Assert.DoesNotContain(Drawing(vm).Strokes, s => s.Group is not null);
        Assert.Single(Drawing(vm, 1).Strokes);
        Assert.Contains(upperName, vm.AiStatus);
    }

    [AvaloniaFact]
    public void SplitGroupIsOneUndoStep()
    {
        var vm = Merged(out _);
        var before = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);

        vm.SplitGroupCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.Equal(before, Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));
    }

    [AvaloniaFact]
    public void WithNothingToSplitItSaysSo()
    {
        var vm = VmLayers.BareVm();
        vm.BeginStroke(40, 40, 1);
        vm.EndStroke();

        Assert.False(vm.SplitGroupOn(null));
        Assert.Contains("group", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Single(vm.Doc.Scene.Layers, l => l.Cels.Count > 0);
    }

    [AvaloniaFact]
    public void AClickOnAMergedLinePicksTheWholeGroup()
    {
        var vm = Merged(out _);

        Assert.True(vm.PickStrokeAt(100, 120, 4));

        var picked = Assert.Single(vm.SelectedStrokes);
        Assert.NotNull(picked.Group);
        Assert.Equal("1 group selected", vm.StrokeSelectionSummary);
    }

    [AvaloniaFact]
    public void AMovedGroupComesBackExactlyOnUndo()
    {
        var vm = Merged(out _);
        vm.PickStrokeAt(100, 120, 4);
        var inside = vm.SelectedStrokes[0].Group!.Strokes[0];
        var was = inside.Points.ToList();

        Assert.Equal(1, vm.MoveSelectedStrokes(13.3, -7.1));
        var moved = vm.SelectedStrokes[0].Group!.Strokes[0];
        Assert.Equal(was[0].X + 13.3, moved.Points[0].X, 9);

        vm.UndoCommand.Execute(null);
        var back = Drawing(vm).Strokes.Single(s => s.Group is not null).Group!.Strokes[0];
        Assert.Equal(was, back.Points);   // the same bits, not a + d - d
    }

    [AvaloniaFact]
    public void DeletingASelectedGroupTakesItWholeAndUndoPutsItBack()
    {
        var vm = Merged(out _);
        var before = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);
        vm.PickStrokeAt(100, 120, 4);

        Assert.Equal(1, vm.DeleteSelectedStrokes());
        Assert.DoesNotContain(Drawing(vm).Strokes, s => s.Group is not null);

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));
    }

    [AvaloniaFact]
    public void RecolouringASelectedGroupSaysItCannotYet()
    {
        var vm = Merged(out _);
        vm.PickStrokeAt(100, 120, 4);
        var colour = vm.SelectedStrokes[0].Group!.Strokes[0].Color;

        Assert.Equal(0, vm.RecolourSelectedStrokes("#00ff00"));

        Assert.Equal(colour, Drawing(vm).Strokes.Single(s => s.Group is not null).Group!.Strokes[0].Color);
        Assert.Contains("group", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void SplitGroupIsSomethingAShortcutCanReach()
    {
        Assert.Contains(new Lightbox.App.Services.ShortcutMap().Definitions, c => c.Id == "docker.splitGroup");
    }
}
