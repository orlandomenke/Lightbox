using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Keys on the timeline's tracks select, and a retime moves the selection.
/// </summary>
/// <remarks>
/// Camera keys and bone keys are visible on the timeline and produce no X-sheet
/// cell, so before this the cel selection — which is keyed on (layer, frame) —
/// could not describe them, and <c>TrackView</c> read no modifiers at all: a
/// click started a retime drag and that was the whole vocabulary.
/// <para>
/// <b>One selection spans the kinds; one verb spans it.</b> Retiming is what a
/// drag already did on all three, so it is what a mixed selection can be asked
/// for. The other verbs stay with their kind, which is why the pose menu below
/// deletes pose keys and leaves a camera key in the same selection alone.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TrackKeySelectionTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    /// <summary>A document with drawings, a camera and an armature — all three kinds at once.</summary>
    private static MainViewModel Rigged(int frames = 6)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < frames; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        vm.AddCameraCommand.Execute(null);
        vm.AddCameraKeyAt(1);
        vm.AddCameraKeyAt(4);
        return vm;
    }

    private static List<int> KeyedIndices(MainViewModel vm) =>
        vm.PaintLayer().Cels.Select((c, i) => (c.Frame, i))
            .Where(x => x.Frame is not null).Select(x => x.i).ToList();

    private static List<int> CameraFrames(MainViewModel vm) =>
        CameraOps.Ordered(vm.Doc.Scene.Camera).Select(k => k.Frame).ToList();

    // ---- the rows say what they are ---------------------------------------------

    [AvaloniaFact]
    public void TrackRowsResolveToTheKeyTheyStandFor()
    {
        var vm = Rigged();

        // Row 0 is the camera when there is one; the layers follow whatever
        // sits above them.
        Assert.Equal(TimelineKeyKind.Camera, vm.TrackKeyAt(0, 1)!.Value.Kind);
        var cel = vm.TrackKeyAt(vm.TracksAboveLayers, 2);
        Assert.Equal(TimelineKeyKind.Cel, cel!.Value.Kind);
        Assert.Equal(0, cel.Value.LayerIndex);
        Assert.Null(vm.TrackKeyAt(999, 0)); // not a row
    }

    // ---- selecting ---------------------------------------------------------------

    [AvaloniaFact]
    public void CtrlClickPicksKeysAcrossKinds()
    {
        var vm = Rigged();

        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);

        Assert.Equal(2, vm.KeySelection.Count);
        Assert.True(vm.IsTrackKeySelected(TimelineKey.Camera(1)));
        Assert.True(vm.IsTrackKeySelected(TimelineKey.Cel(0, 2)));
    }

    [AvaloniaFact]
    public void TheCelHalfOfAMixedSelectionShowsInTheXsheet()
    {
        var vm = Rigged();

        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);

        // One selection, two views: the cel lights up on the sheet, and the
        // camera key simply has no cell there to light.
        Assert.True(Cell(vm, 2).IsSelected);
        Assert.False(Cell(vm, 1).IsSelected);
        Assert.Single(vm.CelSelection);
    }

    [AvaloniaFact]
    public void ShiftRangesOverTheKeysOnThatTrackOnly()
    {
        var vm = Rigged();

        vm.SelectTrackKey(TimelineKey.Cel(0, 1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 4), toggle: false, range: true);

        // The keys between, not every frame between — an empty frame cannot be
        // retimed and has no business being highlighted.
        Assert.Equal([1, 2, 3, 4], vm.KeySelection.Select(k => k.Frame).Order());
        Assert.All(vm.KeySelection, k => Assert.Equal(TimelineKeyKind.Cel, k.Kind));
    }

    [AvaloniaFact]
    public void APlainClickOnAnAlreadySelectedKeyKeepsTheSelection()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Cel(0, 1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);

        // The press that starts a drag must not collapse what it is about to move.
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: false, range: false);

        Assert.Equal(2, vm.KeySelection.Count);
    }

    [AvaloniaFact]
    public void APlainClickOutsideTheSelectionNarrowsItBackDown()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Cel(0, 1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);

        vm.SelectTrackKey(TimelineKey.Cel(0, 4), toggle: false, range: false);

        Assert.Single(vm.KeySelection);
        Assert.True(vm.IsTrackKeySelected(TimelineKey.Cel(0, 4)));
    }

    // ---- retiming ------------------------------------------------------------------

    [AvaloniaFact]
    public void ARetimeMovesEveryKindInTheSelectionTogether()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Camera(4), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 5), toggle: true, range: false);

        var moved = vm.RetimeSelection(TimelineKey.Cel(0, 5), +2);

        output.WriteLine($"camera {string.Join(",", CameraFrames(vm))} cels {string.Join(",", KeyedIndices(vm))}");
        Assert.Equal(2, moved);
        Assert.Contains(6, CameraFrames(vm));
        Assert.Contains(7, KeyedIndices(vm));
        Assert.DoesNotContain(5, KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void ARetimeIsOneUndoStep()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Camera(4), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 5), toggle: true, range: false);
        var camerasBefore = CameraFrames(vm);
        var celsBefore = KeyedIndices(vm);

        vm.RetimeSelection(TimelineKey.Cel(0, 5), +2);
        vm.UndoCommand.Execute(null);

        Assert.Equal(camerasBefore, CameraFrames(vm));
        Assert.Equal(celsBefore, KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void ARunShiftedRightMovesFromTheRightmostBack()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Cel(0, 1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 3), toggle: false, range: true);

        var moved = vm.RetimeSelection(TimelineKey.Cel(0, 2), +1);

        // All three, not one: worked left to right each key would find its
        // destination still occupied by the one that had not moved yet.
        Assert.Equal(3, moved);
        Assert.Equal([0, 2, 3, 4, 5], KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void TheSelectionFollowsWhatItMoved()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Cel(0, 4), toggle: false, range: false);

        vm.RetimeSelection(TimelineKey.Cel(0, 4), +1);

        // So a second drag continues from where the first left off.
        Assert.True(vm.IsTrackKeySelected(TimelineKey.Cel(0, 5)));
        Assert.False(vm.IsTrackKeySelected(TimelineKey.Cel(0, 4)));
    }

    [AvaloniaFact]
    public void DraggingAKeyOutsideTheSelectionMovesOnlyThatKey()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Cel(0, 1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);

        var moved = vm.RetimeSelection(TimelineKey.Camera(4), +1);

        Assert.Equal(1, moved);
        Assert.Contains(5, CameraFrames(vm));
        Assert.Equal([0, 1, 2, 3, 4, 5], KeyedIndices(vm)); // the cels stayed put
    }

    [AvaloniaFact]
    public void ARetimeOffTheFrontOfTheSheetIsRefusedWhole()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Cel(0, 0), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 3), toggle: true, range: false);

        var moved = vm.RetimeSelection(TimelineKey.Cel(0, 3), -1);

        // Moving some of it and clamping the rest would silently close the gap
        // the artist was preserving.
        Assert.Equal(0, moved);
        Assert.Equal([0, 1, 2, 3, 4, 5], KeyedIndices(vm));
    }

    [AvaloniaFact]
    public void TheDotsHandedToTheTrackControlMatchTheSelection()
    {
        var vm = Rigged();
        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);

        var dots = vm.TimelineSelectedDots;

        Assert.Contains((0, 1), dots);                          // the camera row
        Assert.Contains((vm.TracksAboveLayers, 2), dots);       // the first layer row
        Assert.Equal(2, dots.Count);
    }
}
