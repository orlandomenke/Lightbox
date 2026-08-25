using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// An action aimed at a selected cel covers the selection.
/// </summary>
/// <remarks>
/// Reported as "multiselect in timeline and xsheet seems to work but the actions
/// are not applied across the selection" — and it was half true, which is why
/// the highlight looked right. Clear, delete, copy and cut had been made
/// selection-aware; extend and reduce exposure, and insert frame, had not, and
/// those are the ones sitting at the top of the cel's right-click menu.
/// <para>
/// The re-timing three were worse than not covering the selection: they took
/// <c>OpRangeFor</c>, which answered "first selected to last selected" and so
/// swallowed the cels a Ctrl+click had deliberately left out. That helper is
/// gone rather than fixed — a range is not what a selection is, and leaving it
/// available would invite the next operation to make the same mistake.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class CelSelectionActionsTests : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    /// <summary>A bare document with <paramref name="frames"/> keyed drawings on one layer.</summary>
    private static MainViewModel Vm(int frames)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < frames; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        return vm;
    }

    private static List<int> KeyedIndices(MainViewModel vm) =>
        vm.PaintLayer().Cels
            .Select((c, i) => (c.Frame, i))
            .Where(x => x.Frame is not null)
            .Select(x => x.i)
            .ToList();

    // ---- exposure ---------------------------------------------------------------

    [AvaloniaFact]
    public void ExtendExposureCoversEverySelectedCel()
    {
        var vm = Vm(4); // keys at 0,1,2,3
        vm.SelectFrameCommand.Execute(Cell(vm, 1));
        vm.RangeSelectTo(Cell(vm, 2));

        vm.ExtendExposureAt(Cell(vm, 1));

        // Both selected drawings are held a frame longer, so the two after them
        // move down by two in total rather than by one.
        Assert.Equal([0, 1, 3, 5], KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void ExtendExposureWorksFromTheEndOfTheRow()
    {
        var vm = Vm(4);
        vm.SelectFrameCommand.Execute(Cell(vm, 1));
        vm.RangeSelectTo(Cell(vm, 2));

        vm.ExtendExposureAt(Cell(vm, 1));

        // The point of going backwards: had cel 1 been extended first, cel 2
        // would already have shifted to 3 and the second extend would have
        // landed on the wrong drawing. 3 and 5 is what "both, once each" means;
        // 3 and 4 would be the bug.
        Assert.DoesNotContain(4, KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void ReduceExposureCoversEverySelectedCel()
    {
        var vm = Vm(3);
        foreach (var i in new[] { 0, 1, 2 }) vm.ExtendExposureAt(Cell(vm, i * 2));
        var before = KeyedIndices(vm);
        vm.SelectFrameCommand.Execute(Cell(vm, before[0]));
        vm.RangeSelectTo(Cell(vm, before[1]));

        vm.ReduceExposureAt(Cell(vm, before[0]));

        // Two holds removed, so the third drawing comes back up by two.
        Assert.Equal(before[2] - 2, KeyedIndices(vm)[2]);
    }

    // ---- insert -----------------------------------------------------------------

    [AvaloniaFact]
    public void InsertFrameMarksEverySelectedCel()
    {
        var vm = Vm(5);
        vm.SelectFrameCommand.Execute(Cell(vm, 1));
        vm.RangeSelectTo(Cell(vm, 3));

        vm.InsertFrameAt(Cell(vm, 1), FrameRole.Breakdown);

        var layer = vm.PaintLayer();
        Assert.All(new[] { 1, 2, 3 }, i => Assert.Equal(FrameRole.Breakdown, layer.Cels[i].Frame!.Role));
        Assert.Equal(FrameRole.Key, layer.Cels[0].Frame!.Role); // outside the selection
        Assert.Equal(FrameRole.Key, layer.Cels[4].Frame!.Role);
    }

    // ---- a Ctrl+click selection has holes, and they are honoured -----------------

    [AvaloniaFact]
    public void AHoledSelectionDoesNotSwallowTheCelsBetween()
    {
        var vm = Vm(5);
        vm.ToggleCelSelection(Cell(vm, 1));
        vm.ToggleCelSelection(Cell(vm, 3));

        vm.InsertFrameAt(Cell(vm, 1), FrameRole.Breakdown);

        var layer = vm.PaintLayer();
        Assert.Equal(FrameRole.Breakdown, layer.Cels[1].Frame!.Role);
        Assert.Equal(FrameRole.Breakdown, layer.Cels[3].Frame!.Role);
        // The cel in the hole was deselected on purpose. Spanning it is what
        // OpRangeFor did, and why it is gone.
        Assert.Equal(FrameRole.Key, layer.Cels[2].Frame!.Role);
    }

    [AvaloniaFact]
    public void RetimingAHoledSelectionTreatsItAsRunsRatherThanOneSpan()
    {
        var vm = Vm(6);
        vm.ExposureStep = 2;
        vm.ToggleCelSelection(Cell(vm, 0));
        vm.ToggleCelSelection(Cell(vm, 1));
        vm.ToggleCelSelection(Cell(vm, 4)); // a run of two, then a hole, then one

        vm.StretchExposureAt(Cell(vm, 0));

        // Two runs stretched to 2s: 0-1 grows by two frames and 4 by one. The
        // cels in the hole keep their timing, which a single 0..4 span would
        // have re-timed along with everything else.
        var keyed = KeyedIndices(vm);
        Assert.Equal(6, keyed.Count);          // nothing lost
        Assert.Equal([0, 2, 4, 5], keyed.Take(4));
    }

    // ---- an action on a cel outside the selection is still one cel ---------------

    [AvaloniaFact]
    public void AnActionOnACelOutsideTheSelectionTakesOnlyThatCel()
    {
        var vm = Vm(5);
        vm.SelectFrameCommand.Execute(Cell(vm, 0));
        vm.RangeSelectTo(Cell(vm, 1));

        vm.InsertFrameAt(Cell(vm, 4), FrameRole.Breakdown);

        var layer = vm.PaintLayer();
        Assert.Equal(FrameRole.Breakdown, layer.Cels[4].Frame!.Role);
        Assert.Equal(FrameRole.Key, layer.Cels[0].Frame!.Role);
        Assert.Equal(FrameRole.Key, layer.Cels[1].Frame!.Role);
    }
}
