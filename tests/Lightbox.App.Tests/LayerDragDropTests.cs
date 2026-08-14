using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Dropping a dragged layer row: the view-model half of the docker's
/// drag-and-drop. The gesture itself (threshold, arming, cancelling) is the
/// same machinery the cel drag uses; what is worth guarding here is where a
/// drop lands and which folder the layer belongs to afterwards.
/// </summary>
public class LayerDragDropTests
{
    private static LayerRow Row(MainViewModel vm, string id) =>
        vm.LayerRows.First(r => r.Layer.Id == id);

    /// <summary>Paper plus three paint layers; returns their ids bottom-first.</summary>
    private static (MainViewModel Vm, string A, string B, string C) ThreeLayers()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        return (vm, layers[1].Id, layers[2].Id, layers[3].Id);
    }

    [AvaloniaFact]
    public void DroppingBelowARow_LandsUnderIt_AndUndoPutsItBack()
    {
        var (vm, a, b, c) = ThreeLayers();

        // Drag the top layer to sit under the bottom paint layer.
        vm.DropLayerOnRow(Row(vm, c), Row(vm, a), above: false);

        var order = vm.Doc.Scene.Layers.Select(l => l.Id).ToList();
        Assert.Equal([c, a, b], order.Skip(1)); // paper stays at the bottom
        Assert.Equal(c, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Id);

        vm.UndoCommand.Execute(null);
        Assert.Equal([a, b, c], vm.Doc.Scene.Layers.Select(l => l.Id).Skip(1));
    }

    [AvaloniaFact]
    public void DroppingAboveARow_LandsOverIt()
    {
        var (vm, a, _, c) = ThreeLayers();

        // Drag the bottom paint layer to sit just above the top one.
        vm.DropLayerOnRow(Row(vm, a), Row(vm, c), above: true);

        Assert.Equal(a, vm.Doc.Scene.Layers[^1].Id);
    }

    [AvaloniaFact]
    public void DroppingOnItself_ChangesNothing_AndIsNotAnUndoStep()
    {
        var (vm, a, b, c) = ThreeLayers();
        var before = vm.Doc.Scene.Layers.Select(l => l.Id).ToList();

        vm.DropLayerOnRow(Row(vm, b), Row(vm, b), above: true);

        Assert.Equal(before, vm.Doc.Scene.Layers.Select(l => l.Id));
        // The undo that follows must take back the layer add, not a no-op move.
        vm.UndoCommand.Execute(null);
        Assert.Equal(3, vm.Doc.Scene.Layers.Count);
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.Id == c);
    }

    [AvaloniaFact]
    public void DroppingBesideAGroupedRow_JoinsItsFolder_AndBesideALooseRow_LeavesIt()
    {
        var (vm, a, b, c) = ThreeLayers();

        // Fold the middle layer into a folder.
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Id == b);
        vm.CreateLayerFolderCommand.Execute(null);
        var group = vm.Doc.Scene.LayerGroups.Single();

        vm.DropLayerOnRow(Row(vm, c), Row(vm, b), above: true);
        Assert.Equal(group.Id, vm.Doc.Scene.Layers.First(l => l.Id == c).GroupId);

        vm.DropLayerOnRow(Row(vm, c), Row(vm, a), above: true);
        Assert.Null(vm.Doc.Scene.Layers.First(l => l.Id == c).GroupId);
    }

    /// <summary>
    /// The paper is the desk the drawings lie on. The ▲/▼ buttons could never
    /// displace it — they step one place and it is already at the end — but a
    /// drag can drop anything anywhere, and an adversarial pass on the first
    /// draft did exactly that: a paint layer dropped below the paper row took
    /// index 0, and the paper itself could be dragged up out of the bottom
    /// slot. Both break the invariant BackgroundLayerTests states.
    /// </summary>
    [AvaloniaFact]
    public void NothingGoesUnderThePaper_AndThePaperItselfDoesNotMove()
    {
        var vm = VmLayers.PaperVm();
        vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        Assert.True(layers[0].IsBackground);
        var paperRow = vm.LayerRows.First(r => r.Layer.IsBackground);
        var paintRow = vm.LayerRows.First(r => !r.Layer.IsBackground);

        // Dragged down, under the paper.
        vm.DropLayerOnRow(paintRow, paperRow, above: false);
        Assert.True(vm.Doc.Scene.Layers[0].IsBackground);

        // And the paper dragged up out of the bottom slot.
        vm.DropLayerOnRow(paperRow, paintRow, above: true);
        Assert.True(vm.Doc.Scene.Layers[0].IsBackground);
    }

    [AvaloniaFact]
    public void ThePaperCannotBeFiledIntoAFolder()
    {
        var vm = VmLayers.PaperVm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.CreateLayerFolderCommand.Execute(null); // folder round the active paint layer
        var group = vm.Doc.Scene.LayerGroups.Single();
        var paper = vm.Doc.Scene.Layers.First(l => l.IsBackground);

        vm.MoveLayerIntoGroup(paper, group);

        Assert.True(vm.Doc.Scene.Layers[0].IsBackground);
        Assert.Null(vm.Doc.Scene.Layers[0].GroupId);
    }

    [AvaloniaFact]
    public void DroppingOnAFolderHeader_FilesTheLayerIntoTheFolder()
    {
        var (vm, _, b, c) = ThreeLayers();

        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Id == b);
        vm.CreateLayerFolderCommand.Execute(null);
        var group = vm.Doc.Scene.LayerGroups.Single();

        vm.MoveLayerIntoGroup(vm.Doc.Scene.Layers.First(l => l.Id == c), group);

        var moved = vm.Doc.Scene.Layers.First(l => l.Id == c);
        Assert.Equal(group.Id, moved.GroupId);
        // Top of the folder's block: directly above the layer already in it.
        var layers = vm.Doc.Scene.Layers;
        Assert.Equal(layers.FindIndex(l => l.Id == b) + 1, layers.FindIndex(l => l.Id == c));
    }
}
