using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The playback range stays on the drawings it was set on: across a save and
/// a reopen, and across the edits that move cels.
/// </summary>
/// <remarks>
/// Reported as two wishes — "on save the start and end are saved as well",
/// and "when a range is set and we extend exposure on a frame, the end should
/// move with it". The owner widened the second to every edit that shifts a
/// row or a column, so the range frames the same drawings whichever verb moved
/// them.
/// </remarks>
[Collection("BrushState")]
public class PlaybackRangeKeptTests : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex).Cells.First(c => c.Index == index);

    /// <summary>Six drawings, one per frame, and a range over frames 1–3.</summary>
    private static MainViewModel Ranged()
    {
        var vm = VmLayers.BareVm();
        for (var i = 1; i < 6; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        vm.CurrentFrameIndex = 0;
        vm.SetPlaybackStartAt(1);
        vm.SetPlaybackEndAt(3);
        return vm;
    }

    // ---- saved with the document ------------------------------------------------

    [AvaloniaFact]
    public void TheRangeIsWrittenToTheDocumentAndReadBack()
    {
        var vm = Ranged();

        var json = DocJson.Serialize(vm.Doc);
        var restored = DocJson.Deserialize(json);

        Assert.Equal(1, restored.Scene.PlaybackStart);
        Assert.Equal(3, restored.Scene.PlaybackEnd);
        var reopened = VmLayers.BareVm();
        reopened.ReplaceDocument(restored);
        Assert.Equal(1, reopened.PlaybackStartFrame);
        Assert.Equal(3, reopened.PlaybackEndFrame);
    }

    [AvaloniaFact]
    public void ADocumentWithNoRangeWritesNoKey()
    {
        var vm = Ranged();
        vm.ClearPlaybackRange();

        var json = DocJson.Serialize(vm.Doc);

        Assert.DoesNotContain("\"playbackStart\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"playbackEnd\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void OpeningADocumentWithoutARangeClearsTheOneShowing()
    {
        var vm = Ranged();

        vm.ReplaceDocument(VmLayers.BareVm().Doc);

        Assert.Equal(-1, vm.PlaybackStartFrame);
        Assert.Equal(-1, vm.PlaybackEndFrame);
    }

    // ---- not history (Q280) -------------------------------------------------------

    [AvaloniaFact]
    public void SettingTheRangeIsNotAnUndoStep()
    {
        var vm = Ranged();
        var depth = vm.ActiveTab!.Editor.Depth;

        vm.SetPlaybackEndAt(4);
        vm.ClearPlaybackRange();

        Assert.Equal(depth, vm.ActiveTab!.Editor.Depth);
    }

    [AvaloniaFact]
    public void SettingARangeAfterUndoingKeepsTheRedo()
    {
        // The guardian's case: undo some work to compare, set a range to play
        // that part, and the work must still be there to redo.
        var vm = Ranged();
        vm.ExtendExposureAt(Cell(vm, 5));
        vm.UndoCommand.Execute(null);

        vm.SetPlaybackEndAt(2);

        Assert.True(vm.ActiveTab!.Editor.Depth.Redo > 0);
        vm.RedoCommand.Execute(null);
        Assert.Equal(7, vm.Doc.Scene.FrameCount);
        Assert.Equal(2, vm.PlaybackEndFrame); // the redo did not take the range back either
    }

    [AvaloniaFact]
    public void UndoingAnEditThatDidNotMoveTheRangeKeepsTheOneYouSet()
    {
        var vm = Ranged();
        vm.ExtendExposureAt(Cell(vm, 5)); // after the range: it does not move
        vm.SetPlaybackEndAt(4);

        vm.UndoCommand.Execute(null);

        Assert.Equal((1, 4), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
        Assert.Equal(4, vm.Doc.Scene.PlaybackEnd);
    }

    [AvaloniaFact]
    public void SettingTheRangeMarksTheFileUnsaved()
    {
        var vm = Ranged();
        vm.ActiveTab!.MarkSaved();
        Assert.False(vm.ActiveTab.IsDirty);

        vm.SetPlaybackEndAt(4);

        Assert.True(vm.ActiveTab!.IsDirty);
    }

    // ---- follows the edits that move cels ---------------------------------------

    [AvaloniaFact]
    public void ExtendingTheLastDrawingInTheRangeMovesTheEnd()
    {
        var vm = Ranged();

        vm.ExtendExposureAt(Cell(vm, 3));

        Assert.Equal(1, vm.PlaybackStartFrame);
        Assert.Equal(4, vm.PlaybackEndFrame);
    }

    [AvaloniaFact]
    public void ExtendingInsideTheRangeMovesTheEnd()
    {
        var vm = Ranged();

        vm.ExtendExposureAt(Cell(vm, 1));

        Assert.Equal((1, 4), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void ExtendingBeforeTheRangeMovesBothBounds()
    {
        var vm = Ranged();

        vm.ExtendExposureAt(Cell(vm, 0));

        Assert.Equal((2, 4), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void ExtendingAfterTheRangeMovesNothing()
    {
        var vm = Ranged();

        vm.ExtendExposureAt(Cell(vm, 4));

        Assert.Equal((1, 3), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void ReducingTheExposureBringsTheEndBack()
    {
        var vm = Ranged();
        vm.ExtendExposureAt(Cell(vm, 2));
        Assert.Equal(4, vm.PlaybackEndFrame);

        vm.ReduceExposureAt(Cell(vm, 2));

        Assert.Equal((1, 3), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void UndoingAnExtendPutsTheEndBack()
    {
        var vm = Ranged();
        vm.ExtendExposureAt(Cell(vm, 3));

        vm.UndoCommand.Execute(null);

        Assert.Equal((1, 3), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void DeleteAndPullInsideTheRangePullsTheEndBack()
    {
        var vm = Ranged();

        vm.DeleteCelAt(Cell(vm, 2));

        Assert.Equal((1, 2), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void InsertingAnEmptyCellInsideTheRangeMovesTheEnd()
    {
        var vm = Ranged();

        vm.InsertBlankFrameAt(Cell(vm, 2));

        Assert.Equal((1, 4), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
    }

    [AvaloniaFact]
    public void AnEditWithNoRangeSetLeavesItUnset()
    {
        var vm = Ranged();
        vm.ClearPlaybackRange();

        vm.ExtendExposureAt(Cell(vm, 2));

        Assert.Equal((-1, -1), (vm.PlaybackStartFrame, vm.PlaybackEndFrame));
        Assert.Null(vm.Doc.Scene.PlaybackStart);
    }
}
