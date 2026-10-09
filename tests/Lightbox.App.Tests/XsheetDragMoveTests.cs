using Avalonia.Headless.XUnit;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Retiming by hand on the X-sheet: a drag that starts on a drawing carries it
/// along its row with no modifier, a drag that starts on a selected drawing
/// carries the whole selection, and a drag that starts on an empty cel still
/// sweeps a block.
/// </summary>
/// <remarks>
/// Q207 had given the plain drag to block selection and put the move under Alt,
/// which made the commonest retiming gesture a two-handed one. What a press
/// means is now read off the cel it lands on rather than off a modifier.
/// </remarks>
[Collection("BrushState")]
public class XsheetDragMoveTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    /// <summary>The frame id at every keyed cel of a layer, by index.</summary>
    private static Dictionary<int, string> Drawn(MainViewModel vm, int sceneLayer) =>
        vm.Doc.Scene.Layers[sceneLayer].Cels
            .Select((c, i) => (c.Frame, i))
            .Where(x => x.Frame is not null)
            .ToDictionary(x => x.i, x => x.Frame!.Id);

    private static string Show(Dictionary<int, string> drawn) =>
        string.Join(",", drawn.Keys.OrderBy(i => i));

    /// <summary>Two drawing layers, each keyed at 0–3, with the hatch after them.</summary>
    private static (MainViewModel Vm, int A, int B) TwoKeyedLayers()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        var a = vm.Doc.Scene.Layers.IndexOf(vm.PaintLayer());
        vm.AddPaintedLayerCommand.Execute(null);
        var b = vm.Doc.Scene.Layers.Count - 1;
        Assert.NotEqual(a, b);
        foreach (var layer in new[] { a, b })
        {
            for (var i = 0; i < 4; i++)
            {
                if (!Drawn(vm, layer).ContainsKey(i)) vm.InsertFrameAt(Cell(vm, layer, i), FrameRole.Key);
            }
        }
        vm.ClearCelRange();
        Assert.Equal([0, 1, 2, 3], Drawn(vm, a).Keys.OrderBy(i => i));
        Assert.Equal([0, 1, 2, 3], Drawn(vm, b).Keys.OrderBy(i => i));
        return (vm, a, b);
    }

    // ---- which drag a press arms ---------------------------------------------------

    [AvaloniaFact]
    public void APressOnADrawingArmsTheMove_WithNoModifier()
    {
        var (vm, a, _) = TwoKeyedLayers();
        Assert.True(Cell(vm, a, 1).IsKeyed);

        Assert.Equal(CelPress.Move, CelPressRouting.For(Cell(vm, a, 1), alt: false));
        Assert.Equal(CelPress.Move, CelPressRouting.For(Cell(vm, a, 1), alt: true));
    }

    [AvaloniaFact]
    public void APressOnAnEmptyCelStillSweepsABlock()
    {
        var (vm, a, _) = TwoKeyedLayers();
        vm.ExtendExposureAt(Cell(vm, a, 3)); // a hold: inside the scene, nothing drawn on it
        var empty = vm.LayerRows.First(r => r.SceneIndex == a).Cells.First(c => !c.IsKeyed && !c.IsVirtual);

        Assert.Equal(CelPress.Select, CelPressRouting.For(empty, alt: false));
    }

    /// <summary>The hatch past the scene's end holds nothing to carry and no cel to start a block from.</summary>
    [Fact]
    public void APressInTheHatchArmsNothing()
    {
        Assert.Equal(CelPress.None, CelPressRouting.For(new FrameCell(9) { IsVirtual = true }, alt: false));
        Assert.Equal(CelPress.None, CelPressRouting.For(new FrameCell(9) { IsVirtual = true, IsKeyed = true }, alt: false));
    }

    // ---- a selected block moves as one ---------------------------------------------

    [AvaloniaFact]
    public void DraggingASelectedDrawingCarriesTheWholeSelection_OnEveryRow()
    {
        var (vm, a, b) = TwoKeyedLayers();
        var beforeA = Drawn(vm, a);
        var beforeB = Drawn(vm, b);
        vm.SelectFrameCommand.Execute(Cell(vm, a, 2));
        vm.RangeSelectTo(Cell(vm, b, 3));

        vm.MoveCel(Cell(vm, a, 2), Cell(vm, a, 4), copy: false);

        var afterA = Drawn(vm, a);
        var afterB = Drawn(vm, b);
        output.WriteLine($"a {Show(beforeA)} -> {Show(afterA)}   b {Show(beforeB)} -> {Show(afterB)}");
        foreach (var (before, after) in new[] { (beforeA, afterA), (beforeB, afterB) })
        {
            Assert.Equal([0, 1, 4, 5], after.Keys.OrderBy(i => i));
            Assert.Equal(before[2], after[4]);
            Assert.Equal(before[3], after[5]);
        }
    }

    [AvaloniaFact]
    public void TheBlockMoveIsOneUndoStep()
    {
        var (vm, a, b) = TwoKeyedLayers();
        var beforeA = Drawn(vm, a);
        var beforeB = Drawn(vm, b);
        vm.SelectFrameCommand.Execute(Cell(vm, a, 2));
        vm.RangeSelectTo(Cell(vm, b, 3));

        vm.MoveCel(Cell(vm, a, 2), Cell(vm, a, 4), copy: false);
        Assert.NotEqual(beforeB, Drawn(vm, b));
        vm.UndoCommand.Execute(null);

        Assert.Equal(beforeA, Drawn(vm, a));
        Assert.Equal(beforeB, Drawn(vm, b));
    }

    [AvaloniaFact]
    public void TheSelectionFollowsTheBlockItMoved()
    {
        var (vm, a, b) = TwoKeyedLayers();
        vm.SelectFrameCommand.Execute(Cell(vm, a, 2));
        vm.RangeSelectTo(Cell(vm, b, 3));

        vm.MoveCel(Cell(vm, a, 2), Cell(vm, a, 4), copy: false);

        // So a second drag picks the same drawings up from where they landed.
        Assert.Equal(
            new HashSet<(int, int)> { (a, 4), (a, 5), (b, 4), (b, 5) },
            vm.CelSelection.ToHashSet());
    }

    [AvaloniaFact]
    public void CtrlCopiesTheBlock_AndLeavesTheOriginalsWhereTheyWere()
    {
        var (vm, a, b) = TwoKeyedLayers();
        var beforeA = Drawn(vm, a);
        vm.SelectFrameCommand.Execute(Cell(vm, a, 2));
        vm.RangeSelectTo(Cell(vm, b, 3));

        vm.MoveCel(Cell(vm, a, 2), Cell(vm, a, 4), copy: true);

        var afterA = Drawn(vm, a);
        output.WriteLine($"a {Show(beforeA)} -> {Show(afterA)}");
        Assert.Equal([0, 1, 2, 3, 4, 5], afterA.Keys.OrderBy(i => i));
        Assert.Equal(beforeA[2], afterA[2]);
        Assert.NotEqual(beforeA[2], afterA[4]); // a copy is its own drawing
        Assert.Equal([0, 1, 2, 3, 4, 5], Drawn(vm, b).Keys.OrderBy(i => i));

        vm.UndoCommand.Execute(null); // and it is one step too
        Assert.Equal(beforeA, Drawn(vm, a));
    }

    [AvaloniaFact]
    public void ADrawingOutsideTheSelectionMovesAlone()
    {
        var (vm, a, b) = TwoKeyedLayers();
        var beforeB = Drawn(vm, b);
        vm.SelectFrameCommand.Execute(Cell(vm, b, 0));
        vm.RangeSelectTo(Cell(vm, b, 1));

        vm.MoveCel(Cell(vm, a, 3), Cell(vm, a, 5), copy: false);

        Assert.Equal([0, 1, 2, 5], Drawn(vm, a).Keys.OrderBy(i => i));
        Assert.Equal(beforeB, Drawn(vm, b));
    }

    /// <summary>Refusing the whole gesture is kinder than moving some of it and clamping the rest.</summary>
    [AvaloniaFact]
    public void ABlockCannotBeDraggedOffTheFrontOfTheSheet()
    {
        var (vm, a, b) = TwoKeyedLayers();
        var beforeA = Drawn(vm, a);
        var beforeB = Drawn(vm, b);
        vm.SelectFrameCommand.Execute(Cell(vm, a, 0));
        vm.RangeSelectTo(Cell(vm, b, 2));

        vm.MoveCel(Cell(vm, a, 2), Cell(vm, a, 0), copy: false);

        Assert.Equal(beforeA, Drawn(vm, a));
        Assert.Equal(beforeB, Drawn(vm, b));
    }
}
