using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.App.Tests;

/// <summary>
/// The armature's row on the track timeline. Poses have keyed themselves at
/// the playhead since the Bone tool landed and nothing on screen ever said so,
/// which is how a key that failed to survive a reload stayed invisible until
/// somebody scrubbed onto its frame (owner's report, 2026-08-18).
/// </summary>
public class PoseTrackRowTests
{
    private static (MainViewModel Vm, string Hip, string Knee) Rigged()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < 12) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 100, 150);
        var hip = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(hip, 100, 200);
        var knee = vm.SelectedBoneId!;
        vm.PosingMode = true;
        return (vm, hip, knee);
    }

    private static TrackRow[] Pose(MainViewModel vm) =>
        [.. vm.TimelineTracks.Where(t => t.IsArmature)];

    [AvaloniaFact]
    public void ADocumentWithNoRigHasNoArmatureRow()
    {
        // Absent, not empty — the camera's rule.
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Flat", 200, 200, 12, 72, "#ffffff", false));
        Assert.Empty(Pose(vm));
    }

    [AvaloniaFact]
    public void TheArmatureRowMarksEveryFrameAnyBoneIsKeyedOn()
    {
        var (vm, hip, knee) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.CurrentFrameIndex = 6;
        vm.PoseBoneTo(knee, 60, 190);

        var rows = Pose(vm);
        Assert.Single(rows);                       // collapsed by default
        Assert.Equal("Armature", rows[0].Name);
        Assert.Equal([2, 6], rows[0].Keys);
    }

    [AvaloniaFact]
    public void ExpandingGivesEveryBoneItsOwnRow()
    {
        var (vm, hip, knee) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.CurrentFrameIndex = 6;
        vm.PoseBoneTo(knee, 60, 190);

        vm.PoseRowsExpanded = true;

        var rows = Pose(vm);
        Assert.Equal(3, rows.Length);              // the summary and two bones
        Assert.StartsWith("    ", rows[1].Name);   // indented, the bone picker's device
        // The hip is keyed on 2, and again on 6 because keying the knee there
        // copies the pose the artist was looking at — which is what stops its
        // neighbours snapping back to rest.
        Assert.Contains(2, rows[1].Keys);
        Assert.Equal([6], rows[2].Keys.Where(f => f == 6).ToList());
    }

    [AvaloniaFact]
    public void TheRowIndexAnArmatureKeySitsOnIsWhatTheHostRoutesBy()
    {
        var (vm, hip, _) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.PoseRowsExpanded = true;

        // No camera, so the armature summary is track 0 and the bones follow.
        Assert.True(vm.IsPoseTrack(0));
        Assert.Null(vm.BoneOfTrack(0));            // the summary row
        Assert.Equal(hip, vm.BoneOfTrack(1));
        Assert.False(vm.IsPoseTrack(vm.TracksAboveLayers));  // the first layer
        Assert.Equal(3, vm.TracksAboveLayers);     // summary + two bones
    }

    [AvaloniaFact]
    public void DraggingTheSummaryKeyRetimesTheWholePose()
    {
        var (vm, hip, _) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);

        Assert.True(vm.MovePoseKey(null, 2, 5));

        Assert.Null(ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, 2));
        Assert.NotNull(ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, 5));
        Assert.Equal([5], Pose(vm)[0].Keys);
    }

    [AvaloniaFact]
    public void DraggingABonesKeyMovesOnlyThatBone()
    {
        var (vm, hip, knee) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.PoseBoneTo(knee, 60, 190);

        Assert.True(vm.MovePoseKey(knee, 2, 5));

        Assert.Contains(hip, ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, 2)!.Bones.Keys);
        Assert.Contains(knee, ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, 5)!.Bones.Keys);
    }

    [AvaloniaFact]
    public void RemovingAKeyIsOneUndoStep()
    {
        var (vm, hip, _) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);

        Assert.True(vm.DeletePoseKey(null, 2));
        Assert.Empty(Pose(vm)[0].Keys);

        vm.UndoCommand.Execute(null);
        Assert.Equal([2], Pose(vm)[0].Keys);
    }

    [AvaloniaFact]
    public void ADragThatMovesNothingLeavesNoStepBehind()
    {
        // A step nobody authored must not be undoable, and must not leave a
        // redo — DiscardStep's whole distinction from Undo.
        var (vm, hip, _) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        var depth = vm.UndoDepth;

        Assert.False(vm.MovePoseKey(null, 7, 9));      // no key at 7
        Assert.False(vm.DeletePoseKey(null, 7));

        Assert.Equal(depth, vm.UndoDepth);
        // And the pose that was there is untouched.
        Assert.Equal([2], Pose(vm)[0].Keys);
    }

    [AvaloniaFact]
    public void SettingAPoseKeysEaseIsOneUndoStep()
    {
        var (vm, hip, _) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);

        vm.SetPoseKeyEase(2, Easing.Hold);
        Assert.Equal(Easing.Hold, vm.PoseKeyEaseAt(2));

        vm.UndoCommand.Execute(null);
        Assert.Equal(Easing.EaseInOut, vm.PoseKeyEaseAt(2));

        // No key on the frame, or the ease it already has: no step recorded.
        var depth = vm.UndoDepth;
        vm.SetPoseKeyEase(7, Easing.Hold);
        vm.SetPoseKeyEase(2, Easing.EaseInOut);
        Assert.Equal(depth, vm.UndoDepth);
    }

    [AvaloniaFact]
    public void SteppingTheTrackIsUndoableAndAbsentWhenCleared()
    {
        var (vm, hip, _) = Rigged();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);

        vm.SetPoseStep(2);
        Assert.Equal(2, vm.PoseStep);
        Assert.Equal(2, vm.Doc.Scene.PoseTrack!.Step);

        vm.UndoCommand.Execute(null);
        Assert.Equal(1, vm.PoseStep);
        Assert.Null(vm.Doc.Scene.PoseTrack!.Step);
    }

    [AvaloniaFact]
    public void ClearingTheStepRemovesATrackNothingElseAuthored()
    {
        // Optional means absent: a track that exists only to carry a step
        // goes away with the step, so the document serializes as though the
        // feature was never touched.
        var (vm, _, _) = Rigged();

        vm.SetPoseStep(2);
        Assert.NotNull(vm.Doc.Scene.PoseTrack);

        vm.SetPoseStep(1);
        Assert.Null(vm.Doc.Scene.PoseTrack);
    }
}
