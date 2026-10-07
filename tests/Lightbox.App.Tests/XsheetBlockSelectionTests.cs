using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Selecting a block of X-sheet cels (Q207): Shift+click ranges across rows as
/// well as frames, and a plain drag selects the block it sweeps. Alt+drag is
/// the gesture that moves a cel.
/// </summary>
/// <remarks>
/// Before, Shift+click ranged along one row and dropped its anchor on any
/// other, and a plain drag picked a drawing up — so a block over several layers
/// could only be built one Ctrl+click at a time, which is the selection the
/// X-sheet's deletes and the animation-aware tools act on.
/// </remarks>
[Collection("BrushState")]
public class XsheetBlockSelectionTests : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    /// <summary>Paper and three drawing layers, five frames long.</summary>
    private static MainViewModel ThreeLayers()
    {
        var vm = VmLayers.PaperVm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i < 5; i++) vm.AddFrameCommand.Execute(null);
        Assert.Equal(4, vm.Doc.Scene.Layers.Count);
        Assert.Equal(5, vm.Doc.Scene.FrameCount);
        return vm;
    }

    /// <summary>The selection as (row position on screen, frame) pairs, sorted.</summary>
    private static List<(int Row, int Index)> Picked(MainViewModel vm)
    {
        var rows = vm.LayerRows.Select(r => r.SceneIndex).ToList();
        return vm.CelSelection.Select(c => (rows.IndexOf(c.Layer), c.Index)).OrderBy(p => p).ToList();
    }

    private static List<(int Row, int Index)> Block(int row0, int row1, int i0, int i1) =>
        [.. from r in Enumerable.Range(row0, row1 - row0 + 1)
            from i in Enumerable.Range(i0, i1 - i0 + 1)
            select (r, i)];

    [AvaloniaFact]
    public void ShiftClickOnAnotherRowSelectsTheBlockBetween()
    {
        var vm = ThreeLayers();
        var rows = vm.LayerRows.Select(r => r.SceneIndex).ToList();
        vm.SelectFrameCommand.Execute(Cell(vm, rows[0], 1)); // the anchor
        vm.RangeSelectTo(Cell(vm, rows[2], 3));

        Assert.Equal(Block(0, 2, 1, 3), Picked(vm));
    }

    /// <summary>Shift resizes the block from the same anchor — it never adds a second one.</summary>
    [AvaloniaFact]
    public void ASecondShiftClickResizesTheBlock()
    {
        var vm = ThreeLayers();
        var rows = vm.LayerRows.Select(r => r.SceneIndex).ToList();
        vm.SelectFrameCommand.Execute(Cell(vm, rows[2], 4));
        vm.RangeSelectTo(Cell(vm, rows[0], 0));
        vm.RangeSelectTo(Cell(vm, rows[1], 3));

        Assert.Equal(Block(1, 2, 3, 4), Picked(vm));
    }

    [AvaloniaFact]
    public void ADragSelectsTheBlockItSweeps_AndMakesThePressCelTheAnchor()
    {
        var vm = ThreeLayers();
        var rows = vm.LayerRows.Select(r => r.SceneIndex).ToList();

        vm.DragSelectTo(Cell(vm, rows[1], 2), Cell(vm, rows[0], 0));
        Assert.Equal(Block(0, 1, 0, 2), Picked(vm));

        // Shift+click afterwards ranges from where the drag began.
        vm.RangeSelectTo(Cell(vm, rows[2], 4));
        Assert.Equal(Block(1, 2, 2, 4), Picked(vm));
    }

    /// <summary>The hatch past the end of the scene holds no cels, so a block stops at its edge.</summary>
    [AvaloniaFact]
    public void ABlockDraggedIntoTheHatchStopsAtTheEndOfTheScene()
    {
        var vm = ThreeLayers();
        var rows = vm.LayerRows.Select(r => r.SceneIndex).ToList();
        var past = vm.LayerRows[0].Cells.FirstOrDefault(c => c.IsVirtual);
        Assert.NotNull(past);

        vm.DragSelectTo(Cell(vm, rows[0], 3), past!);

        Assert.Equal(Block(0, 0, 3, 4), Picked(vm));
    }

    /// <summary>And the deletes take the whole block: the point of selecting one.</summary>
    [AvaloniaFact]
    public void DeleteTakesTheWholeBlock()
    {
        var vm = ThreeLayers();
        var rows = vm.LayerRows.Select(r => r.SceneIndex).ToList();
        vm.DragSelectTo(Cell(vm, rows[0], 1), Cell(vm, rows[1], 2));

        vm.ClearCelAtPlayhead();

        foreach (var row in rows.Take(2))
        {
            var layer = vm.Doc.Scene.Layers[row];
            Assert.NotNull(layer.Cels[0].Frame);
            Assert.Null(layer.Cels[1].Frame);
            Assert.Null(layer.Cels[2].Frame);
            Assert.NotNull(layer.Cels[3].Frame);
        }
        var untouched = vm.Doc.Scene.Layers[rows[2]];
        Assert.All(untouched.Cels.Take(5), c => Assert.NotNull(c.Frame));
    }

    // ---- the gesture -------------------------------------------------------------

    private static FrameCell AnyCell() => ThreeLayers().LayerRows[0].Cells[0];

    [AvaloniaFact]
    public void TheGestureWaitsForTheThreshold_SoAClickStaysAClick()
    {
        var g = new CelBlockSelectGesture();
        g.Press(AnyCell(), new Point(10, 10), leftButton: true);

        Assert.False(g.Moved(new Point(13, 12), leftButton: true, out _));
        Assert.False(g.Selecting);
        Assert.True(g.Moved(new Point(30, 10), leftButton: true, out var started));
        Assert.True(started);
        Assert.True(g.Moved(new Point(60, 10), leftButton: true, out started));
        Assert.False(started); // only the first move starts it
    }

    /// <summary>The block is rebuilt once per cel reached, not once per pointer event.</summary>
    [AvaloniaFact]
    public void TheBlockIsRebuiltOnlyWhenTheCornerReachesANewCel()
    {
        var vm = ThreeLayers();
        var a = vm.LayerRows[0].Cells[1];
        var b = vm.LayerRows[1].Cells[2];
        var g = new CelBlockSelectGesture();
        g.Press(a, new Point(10, 10), leftButton: true);

        Assert.True(g.CornerMovedTo(a));
        Assert.False(g.CornerMovedTo(a));
        Assert.True(g.CornerMovedTo(b));
        Assert.False(g.CornerMovedTo(b));
        Assert.True(g.CornerMovedTo(a));

        g.Cancel();
        g.Press(a, new Point(10, 10), leftButton: true);
        Assert.True(g.CornerMovedTo(a)); // a new press starts fresh
    }

    [AvaloniaFact]
    public void AMoveWithTheButtonUpDisarms()
    {
        var g = new CelBlockSelectGesture();
        g.Press(AnyCell(), new Point(10, 10), leftButton: true);
        Assert.False(g.Moved(new Point(60, 10), leftButton: false, out _));
        Assert.Null(g.From);
    }

    [AvaloniaFact]
    public void AHatchedCelDoesNotArm()
    {
        var vm = ThreeLayers();
        var past = vm.LayerRows[0].Cells.First(c => c.IsVirtual);
        var g = new CelBlockSelectGesture();
        g.Press(past, new Point(10, 10), leftButton: true);
        Assert.Null(g.From);
    }
}
