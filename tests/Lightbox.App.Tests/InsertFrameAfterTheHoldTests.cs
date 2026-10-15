using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>Insert frame</b> on a drawing adds a new one after it; <b>Mark as</b>
/// re-marks the drawings already there.
/// </summary>
/// <remarks>
/// Reported as "Insert Frame on a frame does nothing": on a cel that already
/// had a drawing it only re-marked the role, which for a key marked as a key
/// changes nothing anyone can see. The owner's answers: the new drawing goes
/// in <em>after the hold</em> (A A B B → A A new B B, so A keeps its timing),
/// it is a blank drawing, and re-marking moves to its own submenu rather than
/// staying behind the same item.
/// </remarks>
[Collection("BrushState")]
public class InsertFrameAfterTheHoldTests : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex).Cells.First(c => c.Index == index);

    /// <summary>Drawings A at 0 and B at 2, each held for two: A A B B.</summary>
    private static MainViewModel OnTwos()
    {
        var vm = VmLayers.BareVm();
        vm.InsertFrameAt(Cell(vm, 2), FrameRole.Key);
        vm.ExtendExposureAt(Cell(vm, 2));
        Assert.Equal([0, 2], KeyedIndices(vm));
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        return vm;
    }

    private static List<int> KeyedIndices(MainViewModel vm) =>
        vm.PaintLayer().Cels
            .Select((c, i) => (c.Frame, i))
            .Where(x => x.Frame is not null)
            .Select(x => x.i)
            .ToList();

    [AvaloniaFact]
    public void InsertOnADrawingAddsABlankOneAfterItsHold()
    {
        var vm = OnTwos();
        var a = vm.PaintLayer().Cels[0].Frame!;
        var b = vm.PaintLayer().Cels[2].Frame!;

        vm.InsertFrameAt(Cell(vm, 0), FrameRole.Breakdown);

        var cels = vm.PaintLayer().Cels;
        Assert.Equal([0, 2, 3], KeyedIndices(vm));
        Assert.Same(a, cels[0].Frame);
        Assert.Null(cels[1].Frame); // A is still on twos
        Assert.Equal(FrameRole.Breakdown, cels[2].Frame!.Role);
        Assert.Empty(cels[2].Frame!.Strokes);
        Assert.Same(b, cels[3].Frame);
        Assert.Equal(5, vm.Doc.Scene.FrameCount);
        Assert.Equal(2, vm.CurrentFrameIndex); // the playhead is on the new drawing
    }

    [AvaloniaFact]
    public void InsertOnTheHoldOfADrawingLandsInTheSamePlace()
    {
        var vm = OnTwos();

        vm.InsertFrameAt(Cell(vm, 1), FrameRole.Key);

        // A hold cel already keys in place — that is how a drawing is started
        // on an empty cel, and it stays that way.
        Assert.Equal([0, 1, 2], KeyedIndices(vm));
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void InsertOnTheLastDrawingGrowsTheScene()
    {
        var vm = OnTwos();

        vm.InsertFrameAt(Cell(vm, 2), FrameRole.Key);

        Assert.Equal([0, 2, 4], KeyedIndices(vm));
        Assert.Equal(5, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void InsertIsOneUndoStep()
    {
        var vm = OnTwos();

        vm.InsertFrameAt(Cell(vm, 0), FrameRole.Key);
        vm.UndoCommand.Execute(null);

        Assert.Equal([0, 2], KeyedIndices(vm));
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void TheAnimationMenuTwinInsertsAfterTheHoldToo()
    {
        var vm = OnTwos();
        vm.CurrentFrameIndex = 0;

        vm.InsertFrameAtPlayhead(FrameRole.Key);

        Assert.Equal([0, 2, 3], KeyedIndices(vm));
        Assert.Equal(2, vm.CurrentFrameIndex);
    }

    // ---- what the review found (adversary, 2026-10-15) --------------------------

    [AvaloniaFact]
    public void ASelectionOfDrawingsIsOneUndoStep()
    {
        var vm = OnTwos();
        vm.ToggleCelSelection(Cell(vm, 0));
        vm.ToggleCelSelection(Cell(vm, 2));

        vm.InsertFrameAt(Cell(vm, 0), FrameRole.Key);
        Assert.Equal([0, 2, 3, 5], KeyedIndices(vm)); // A A new B B new
        vm.UndoCommand.Execute(null);

        Assert.Equal([0, 2], KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void ARunOverDrawingsAndHoldsInsertsOnePerDrawing()
    {
        var vm = OnTwos();
        vm.SelectFrameCommand.Execute(Cell(vm, 0));
        vm.RangeSelectTo(Cell(vm, 3));

        vm.InsertFrameAt(Cell(vm, 0), FrameRole.Key);

        // The holds stay holds: two drawings picked, two new ones, each after
        // its drawing's hold — not four interleaved through the run.
        Assert.Equal([0, 2, 3, 5], KeyedIndices(vm));
        Assert.Null(vm.PaintLayer().Cels[1].Frame);
        Assert.Null(vm.PaintLayer().Cels[4].Frame);
    }

    [AvaloniaFact]
    public void TheIKeyRefusesALockedLayer()
    {
        var vm = OnTwos();
        vm.CurrentFrameIndex = 0;
        vm.PaintLayer().Locked = true;

        vm.InsertKeyframeAtPlayhead();

        Assert.Equal([0, 2], KeyedIndices(vm));
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void MarkAsReMarksTheDrawingInPlace()
    {
        var vm = OnTwos();

        vm.MarkCelAs(Cell(vm, 2), FrameRole.Breakdown);

        Assert.Equal([0, 2], KeyedIndices(vm));
        Assert.Equal(FrameRole.Breakdown, vm.PaintLayer().Cels[2].Frame!.Role);
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void MarkAsOnAHoldMarksNothing()
    {
        var vm = OnTwos();

        vm.MarkCelAs(Cell(vm, 1), FrameRole.Breakdown);

        // A hold has no drawing to mark; starting one is Insert's job.
        Assert.Equal([0, 2], KeyedIndices(vm));
        Assert.Equal(FrameRole.Key, vm.PaintLayer().Cels[0].Frame!.Role);
    }
}
