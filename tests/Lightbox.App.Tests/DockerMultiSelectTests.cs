using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Ctrl and Shift in the dockers that let an artist pick items: the layers
/// docker's rows and the timeline's cels.
/// </summary>
/// <remarks>
/// The two are tested together because they are one promise — the same pair of
/// modifiers meaning the same pair of things wherever a docker has a list —
/// and a change that keeps one and drops the other is exactly the inconsistency
/// this file exists to catch.
/// </remarks>
public class DockerMultiSelectTests
{
    private static MainViewModel VmWithLayers(int count)
    {
        var vm = VmLayers.BareVm();
        while (vm.Doc.Scene.Layers.Count < count) vm.AddPaintedLayerCommand.Execute(null);
        return vm;
    }

    /// <summary>The docker row for a scene layer index (0 = bottom of the stack).</summary>
    private static LayerRow Row(MainViewModel vm, int sceneIndex) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneIndex);

    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    /// <summary>
    /// A document that is <paramref name="frames"/> frames long. Cells past the
    /// end are virtual, and a virtual cel is not selectable — so a timeline test
    /// that skips this selects nothing and passes for the wrong reason.
    /// </summary>
    private static MainViewModel VmWithFrames(int frames, int layers = 1)
    {
        var vm = VmWithLayers(layers);
        vm.SmoothStrokes = false;
        for (var i = 1; i < frames; i++) vm.InsertFrameAt(Cell(vm, 0, i), FrameRole.Key);
        return vm;
    }

    // ---- layers docker ---------------------------------------------------------

    [AvaloniaFact]
    public void PlainClick_SelectsOneLayer_AndMakesItActive()
    {
        var vm = VmWithLayers(3);

        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);

        Assert.Equal(0, vm.ActiveLayerIndex);
        Assert.Equal(1, vm.SelectedLayerCount);
        Assert.True(Row(vm, 0).IsSelected);
        Assert.False(Row(vm, 1).IsSelected);
    }

    [AvaloniaFact]
    public void CtrlClick_AddsSingleLayers_AndClickingOneAgainDropsIt()
    {
        var vm = VmWithLayers(3);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);

        vm.SelectLayer(Row(vm, 2), toggle: true, range: false);

        Assert.Equal(2, vm.SelectedLayerCount);
        Assert.True(Row(vm, 0).IsSelected);
        Assert.True(Row(vm, 2).IsSelected);
        Assert.False(Row(vm, 1).IsSelected); // the one in between is untouched
        Assert.Equal(2, vm.ActiveLayerIndex); // the newly added one is where the brush goes

        vm.SelectLayer(Row(vm, 2), toggle: true, range: false);

        Assert.Equal(1, vm.SelectedLayerCount);
        Assert.False(Row(vm, 2).IsSelected);
        Assert.Equal(0, vm.ActiveLayerIndex); // active followed the layer that left
    }

    [AvaloniaFact]
    public void CtrlClick_WillNotEmptyTheSelection()
    {
        var vm = VmWithLayers(2);
        vm.SelectLayer(Row(vm, 1), toggle: false, range: false);

        vm.SelectLayer(Row(vm, 1), toggle: true, range: false);

        // There is always somewhere for the next stroke to land.
        Assert.Equal(1, vm.SelectedLayerCount);
        Assert.True(Row(vm, 1).IsSelected);
    }

    [AvaloniaFact]
    public void ShiftClick_TakesTheRunFromTheAnchor()
    {
        var vm = VmWithLayers(4);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);

        vm.SelectLayer(Row(vm, 2), toggle: false, range: true);

        Assert.Equal(3, vm.SelectedLayerCount);
        Assert.True(Row(vm, 0).IsSelected);
        Assert.True(Row(vm, 1).IsSelected);
        Assert.True(Row(vm, 2).IsSelected);
        Assert.False(Row(vm, 3).IsSelected);
    }

    [AvaloniaFact]
    public void ShiftClick_ReplacesTheRunRatherThanGrowingIt()
    {
        var vm = VmWithLayers(5);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 4), toggle: false, range: true);

        vm.SelectLayer(Row(vm, 2), toggle: false, range: true); // pulled back

        Assert.Equal(3, vm.SelectedLayerCount);
        Assert.False(Row(vm, 3).IsSelected);
        Assert.False(Row(vm, 4).IsSelected);
    }

    [AvaloniaFact]
    public void ActivatingALayerAnyOtherWay_StartsTheSelectionAgain()
    {
        var vm = VmWithLayers(3);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 2), toggle: true, range: false);
        Assert.Equal(2, vm.SelectedLayerCount);

        vm.ActiveLayerIndex = 1; // an arrow-key walk, a cel click, a document load

        Assert.Equal(1, vm.SelectedLayerCount);
        Assert.True(Row(vm, 1).IsSelected);
    }

    [AvaloniaFact]
    public void DeletingASelection_TakesThemAllInOneUndoStep()
    {
        var vm = VmWithLayers(4);
        var doomed = new[] { vm.Doc.Scene.Layers[0].Id, vm.Doc.Scene.Layers[2].Id };
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 2), toggle: true, range: false);

        vm.DeleteLayer(vm.Doc.Scene.Layers[2]);

        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => doomed.Contains(l.Id));

        vm.UndoCommand.Execute(null); // one step, not two

        Assert.Equal(4, vm.Doc.Scene.Layers.Count);
    }

    [AvaloniaFact]
    public void DeletingARowOutsideTheSelection_TakesOnlyThatRow()
    {
        var vm = VmWithLayers(4);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 1), toggle: true, range: false);
        var other = vm.Doc.Scene.Layers[3];

        vm.DeleteLayer(other);

        Assert.Equal(3, vm.Doc.Scene.Layers.Count);
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.Id == other.Id);
    }

    [AvaloniaFact]
    public void HidingASelection_HidesEveryLayerInItAsOneStep()
    {
        var vm = VmWithLayers(3);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 2), toggle: false, range: true);

        Row(vm, 2).Visible = false; // the row's eye, which writes through the view model

        Assert.All(vm.Doc.Scene.Layers, l => Assert.False(l.Visible));

        vm.UndoCommand.Execute(null);

        Assert.All(vm.Doc.Scene.Layers, l => Assert.True(l.Visible));
    }

    [AvaloniaFact]
    public void TheOnionToggle_StaysOnOneLayer_EvenWithSeveralSelected()
    {
        var vm = VmWithLayers(3);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 2), toggle: false, range: true);

        Row(vm, 2).OnionEnabled = false;

        // Which layers are in the onion stack is an arrangement tuned per layer
        // while working, not a property of the drawing like the eye or the lock.
        Assert.False(vm.Doc.Scene.Layers[2].OnionEnabled);
        Assert.True(vm.Doc.Scene.Layers[1].OnionEnabled);
        Assert.True(vm.Doc.Scene.Layers[0].OnionEnabled);
    }

    [AvaloniaFact]
    public void TheActiveLayerCommands_MeanTheActiveLayer_NotTheSelection()
    {
        var vm = VmWithLayers(3);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 2), toggle: false, range: true);

        vm.ToggleActiveLayerLockedCommand.Execute(null);
        vm.ToggleActiveLayerAlphaLockedCommand.Execute(null);

        // A control that says "the active layer" has to mean it, whatever is
        // selected — otherwise the shortcut bar and the docker row disagree
        // about what one click does.
        Assert.True(vm.Doc.Scene.Layers[2].Locked);
        Assert.True(vm.Doc.Scene.Layers[2].AlphaLocked);
        Assert.False(vm.Doc.Scene.Layers[0].Locked);
        Assert.False(vm.Doc.Scene.Layers[0].AlphaLocked);
    }

    [AvaloniaFact]
    public void MovingASelectionUp_KeepsItTogether_AndStopsAtTheTop()
    {
        var vm = VmWithLayers(4);
        var names = vm.Doc.Scene.Layers.Select(l => l.Id).ToList();
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 1), toggle: false, range: true); // the bottom two

        vm.MoveLayer(Row(vm, 1), +1);

        Assert.Equal([names[2], names[0], names[1], names[3]], vm.Doc.Scene.Layers.Select(l => l.Id));
        // The selection survives, so a second press moves the same pair again.
        Assert.Equal(2, vm.SelectedLayerCount);

        vm.MoveLayer(vm.LayerRows.First(r => r.Layer.Id == names[1]), +1);
        vm.MoveLayer(vm.LayerRows.First(r => r.Layer.Id == names[1]), +1);

        // Pinned against the ceiling in the order they started in, not scrambled.
        Assert.Equal([names[2], names[3], names[0], names[1]], vm.Doc.Scene.Layers.Select(l => l.Id));
    }

    [AvaloniaFact]
    public void AFolderMadeFromASelection_TakesEveryLayerInIt()
    {
        var vm = VmWithLayers(3);
        vm.SelectLayer(Row(vm, 0), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 1), toggle: false, range: true);

        vm.CreateLayerFolderCommand.Execute(null);

        var group = Assert.Single(vm.Doc.Scene.LayerGroups);
        Assert.Equal(2, vm.Doc.Scene.Layers.Count(l => l.GroupId == group.Id));
    }

    // ---- timeline cels ---------------------------------------------------------

    [AvaloniaFact]
    public void CtrlClickingCels_PicksThemOneAtATime_WithHoles()
    {
        var vm = VmWithFrames(5);
        vm.SelectFrameCommand.Execute(Cell(vm, 0, 0));

        vm.ToggleCelSelection(Cell(vm, 0, 1));
        vm.ToggleCelSelection(Cell(vm, 0, 3));

        Assert.True(Cell(vm, 0, 1).IsSelected);
        Assert.True(Cell(vm, 0, 3).IsSelected);
        Assert.False(Cell(vm, 0, 2).IsSelected);
        // A holed selection is not a range, and says so rather than lying about
        // the run between its ends.
        Assert.Null(vm.CelRange);

        vm.ToggleCelSelection(Cell(vm, 0, 3));

        Assert.False(Cell(vm, 0, 3).IsSelected);
        Assert.Equal((0, 1, 1), vm.CelRange);
    }

    [AvaloniaFact]
    public void CtrlClickingCels_ReachesAcrossLayers()
    {
        var vm = VmWithFrames(3, layers: 2);

        vm.ToggleCelSelection(Cell(vm, 0, 1));
        vm.ToggleCelSelection(Cell(vm, 1, 1));

        Assert.True(Cell(vm, 0, 1).IsSelected);
        Assert.True(Cell(vm, 1, 1).IsSelected);
        Assert.Null(vm.CelRange); // two rows is not one run
        Assert.Equal(2, vm.CelSelection.Count);
    }

    [AvaloniaFact]
    public void ShiftClick_StillLaysAContiguousRun()
    {
        var vm = VmWithFrames(5);
        vm.SelectFrameCommand.Execute(Cell(vm, 0, 1));

        vm.RangeSelectTo(Cell(vm, 0, 3));

        Assert.Equal((0, 1, 3), vm.CelRange);
        Assert.True(Cell(vm, 0, 2).IsSelected);
    }

    [AvaloniaFact]
    public void ClearingAHoledSelection_LeavesTheCelsBetweenItAlone()
    {
        var vm = VmWithFrames(4);
        var layer = vm.PaintLayer();
        foreach (var cel in layer.Cels)
        {
            cel.Frame?.Strokes.Add(new Stroke { Points = [new StrokePoint(1, 1, 1)] });
        }

        vm.ToggleCelSelection(Cell(vm, 0, 1));
        vm.ToggleCelSelection(Cell(vm, 0, 3));
        vm.ClearCelAt(Cell(vm, 0, 1));

        // A cleared cel becomes a hold — the drawing goes, the timing stays.
        layer = vm.PaintLayer();
        Assert.Null(layer.Cels[1].Frame);
        Assert.Null(layer.Cels[3].Frame);
        Assert.NotEmpty(layer.Cels[2].Frame!.Strokes); // the hole is a hole
        Assert.NotEmpty(layer.Cels[0].Frame!.Strokes);
    }

    [AvaloniaFact]
    public void DeletingAHoledSelection_PullsTheRowBackByExactlyTheCelsPicked()
    {
        var vm = VmWithFrames(5);
        var ids = vm.PaintLayer().Cels.Take(5).Select(c => c.Frame?.Id).ToList();

        vm.ToggleCelSelection(Cell(vm, 0, 1));
        vm.ToggleCelSelection(Cell(vm, 0, 3));
        vm.DeleteCelAt(Cell(vm, 0, 3));

        // 1 and 3 gone, and 3 was not misread as "one further along" by the
        // earlier removal shifting the row.
        var left = vm.PaintLayer().Cels.Select(c => c.Frame?.Id).Where(id => id is not null).ToList();
        Assert.DoesNotContain(ids[1], left);
        Assert.DoesNotContain(ids[3], left);
        Assert.Contains(ids[0], left);
        Assert.Contains(ids[2], left);
        Assert.Contains(ids[4], left);
    }

    [AvaloniaFact]
    public void CopyingAHoledSelection_PastesAsTheCelsPicked()
    {
        var vm = VmWithFrames(4);

        vm.ToggleCelSelection(Cell(vm, 0, 0));
        vm.ToggleCelSelection(Cell(vm, 0, 2));
        vm.CopyCel(Cell(vm, 0, 0));
        vm.PasteCel(Cell(vm, 0, 6));

        var cels = vm.PaintLayer().Cels;
        // Two cels laid consecutively — the cel a Ctrl+click left out is not in
        // the clipboard, so it is not in the paste either.
        Assert.NotNull(cels[6].Frame);
        Assert.NotNull(cels[7].Frame);
        Assert.Equal(6, cels.Count(c => c.Frame is not null)); // the original four plus these two
    }
}
