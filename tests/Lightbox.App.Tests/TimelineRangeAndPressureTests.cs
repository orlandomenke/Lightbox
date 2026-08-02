using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

public class CelRangeSelectionTests
{
    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    /// <summary>
    /// Bare — one drawing layer, no paper. These tests address cells by scene
    /// layer index, and a locked Background at 0 would shift every one of them
    /// without changing what is being checked.
    /// </summary>
    private static MainViewModel VmWithKeys(int keyed)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < keyed; i++) vm.InsertFrameAt(Cell(vm, 0, i), FrameRole.Key);
        return vm;
    }

    [AvaloniaFact]
    public void ShiftClick_SelectsARange_AndPlainClickClearsIt()
    {
        var vm = VmWithKeys(5);
        vm.SelectFrameCommand.Execute(Cell(vm, 0, 1)); // anchor
        vm.RangeSelectTo(Cell(vm, 0, 3));

        Assert.True(Cell(vm, 0, 1).IsSelected);
        Assert.True(Cell(vm, 0, 2).IsSelected);
        Assert.True(Cell(vm, 0, 3).IsSelected);
        Assert.False(Cell(vm, 0, 0).IsSelected);
        Assert.Equal((0, 1, 3), vm.CelRange);

        vm.SelectFrameCommand.Execute(Cell(vm, 0, 0)); // plain click clears
        Assert.Null(vm.CelRange);
        Assert.False(Cell(vm, 0, 2).IsSelected);
    }

    [AvaloniaFact]
    public void CopyRange_PreservesHolds_AndPasteReplaysThem()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        // key at 0 (default), hold at 1, key at 2
        vm.InsertFrameAt(Cell(vm, 0, 2), FrameRole.Key);
        var layer = vm.PaintLayer();
        Assert.Null(layer.Cels[1].Frame); // sanity: 1 is a hold

        vm.SelectFrameCommand.Execute(Cell(vm, 0, 0));
        vm.RangeSelectTo(Cell(vm, 0, 2));
        vm.CopyCel(Cell(vm, 0, 1)); // inside the range → copies all 3 cels

        vm.PasteCel(Cell(vm, 0, 5));

        layer = vm.PaintLayer();
        Assert.Equal(8, vm.Doc.Scene.FrameCount);
        Assert.NotNull(layer.Cels[5].Frame);
        Assert.Null(layer.Cels[6].Frame);      // the hold pasted as a hold
        Assert.NotNull(layer.Cels[7].Frame);
        Assert.NotEqual(layer.Cels[0].Frame!.Id, layer.Cels[5].Frame!.Id); // fresh ids
    }

    [AvaloniaFact]
    public void CutRange_ClearsEveryCelInTheRange_InOneUndoStep()
    {
        var vm = VmWithKeys(4);
        vm.SelectFrameCommand.Execute(Cell(vm, 0, 1));
        vm.RangeSelectTo(Cell(vm, 0, 3));

        vm.CutCel(Cell(vm, 0, 2));

        var layer = vm.PaintLayer();
        Assert.NotNull(layer.Cels[0].Frame);
        Assert.Null(layer.Cels[1].Frame);
        Assert.Null(layer.Cels[2].Frame);
        Assert.Null(layer.Cels[3].Frame);
        Assert.True(vm.HasCelClipboard);

        vm.UndoCommand.Execute(null); // the clear is one step
        Assert.NotNull(vm.PaintLayer().Cels[2].Frame);
    }

    [AvaloniaFact]
    public void MoveCel_RefusesCrossLayerDrops()
    {
        var vm = VmWithKeys(2);
        vm.AddPaintedLayerCommand.Execute(null);
        var before = vm.PaintLayer().Cels[0].Frame;

        var from = Cell(vm, 0, 0);
        var to = Cell(vm, 1, 3);
        vm.MoveCel(from, to, copy: false);

        Assert.Same(before, vm.PaintLayer().Cels[0].Frame); // unchanged
        Assert.Contains("own layer row", vm.AiStatus);
    }

    [AvaloniaFact]
    public void Markers_AddEditRemove_AreUndoable_AndFeedTheRuler()
    {
        var vm = new MainViewModel(null);
        vm.SetMarkerAt(2, "blink", "#e05555");

        var marker = Assert.Single(vm.MarkersView);
        Assert.Equal(2, marker.Frame);
        Assert.Equal("blink", marker.Label);

        vm.SetMarkerAt(2, "blink END", "#4caf50"); // edit replaces, no duplicate
        Assert.Single(vm.MarkersView);
        Assert.Equal("blink END", vm.MarkersView[0].Label);

        vm.RemoveMarkerAt(2);
        Assert.Empty(vm.MarkersView);

        vm.UndoCommand.Execute(null); // undo the remove
        Assert.Single(vm.MarkersView);
    }
}

public class PressureVmTests
{
    [AvaloniaFact]
    public void MasterSwitch_WritesIntoTheStrokeRecord()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BrushPressureEnabled = false;

        vm.BeginStroke(10, 10, 0.4);
        vm.MoveStroke(60, 10, 0.4);
        vm.EndStroke();

        var stroke = (vm.PaintedCel()).Strokes[0];
        Assert.False(stroke.Brush.PressureEnabled); // provenance: replay ignores pressure too
    }

    [AvaloniaFact]
    public void PerSettingCheckboxes_MapToTheResponseCurves()
    {
        // The brush store is shared across tests — pin a known state first.
        var vm = new MainViewModel(null)
        {
            BrushPressureSizeGamma = 1,
            BrushPressureFlowGamma = 0,
            BrushPressureHardnessGamma = 0,
        };

        Assert.True(vm.BrushPressureAffectsSize);
        vm.BrushPressureAffectsSize = false;
        Assert.Equal(0, vm.BrushPressureSizeGamma);

        Assert.False(vm.BrushPressureAffectsFlow);
        vm.BrushPressureAffectsFlow = true;
        Assert.Equal(1, vm.BrushPressureFlowGamma);

        vm.BrushPressureAffectsHardness = true;
        Assert.Equal(1, vm.BrushPressureHardnessGamma);
        vm.BrushPressureHardnessGamma = 2.5;
        Assert.True(vm.BrushPressureAffectsHardness);
    }
}
