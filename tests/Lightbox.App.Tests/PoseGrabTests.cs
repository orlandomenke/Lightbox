using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Pose mode reads a grab the way bind mode does: the tip aims, the shaft and
/// the joint carry (owner's decision, 2026-08-18). Before this every pose drag
/// aimed, whatever it had hold of, so there was no gesture that moved a
/// character at all — reported as "in pose mode I am unable to move the entire
/// skeleton … grabbing the first bone or any bone for that matter, just
/// rotates the bones".
/// </summary>
public class PoseGrabTests
{
    private static MainViewModel Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        return vm;
    }

    /// <summary>A two-bone chain: root at (100,150) reaching right, child extruded on down its tip.</summary>
    private static (MainViewModel Vm, string Root, string Child) Chain()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var root = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(root, 220, 150);
        var child = vm.SelectedBoneId!;
        vm.PosingMode = true;
        return (vm, root, child);
    }

    private static BonePlacement Placement(MainViewModel vm, string boneId) =>
        ArmatureOps.Solve(
            vm.Doc.Armature!,
            ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex))[boneId];

    [AvaloniaFact]
    public void AShaftDragInPoseModeCarriesTheBoneInsteadOfTurningIt()
    {
        var (vm, root, _) = Chain();

        // Grab the shaft halfway along and drag down-right by (30, 40).
        vm.EndBoneGesture(root, BoneGrab.Body, 130, 150, 160, 190, extruding: false);

        var placed = Placement(vm, root);
        Assert.Equal(130, placed.X, 6);
        Assert.Equal(190, placed.Y, 6);
        // Carried, not turned: the bone still points the way it did.
        Assert.Equal(0, placed.RotationDeg, 6);
    }

    [AvaloniaFact]
    public void CarryingTheRootCarriesTheWholeSkeleton()
    {
        var (vm, root, child) = Chain();
        var before = Placement(vm, child);

        vm.EndBoneGesture(root, BoneGrab.Body, 130, 150, 160, 190, extruding: false);

        var after = Placement(vm, child);
        Assert.Equal(before.X + 30, after.X, 6);
        Assert.Equal(before.Y + 40, after.Y, 6);
        Assert.Equal(before.RotationDeg, after.RotationDeg, 6);
    }

    [AvaloniaFact]
    public void TheTipStillAimsInPoseMode()
    {
        var (vm, root, _) = Chain();

        // Take the tip and swing it to straight down from the joint.
        vm.EndBoneGesture(root, BoneGrab.Tip, 160, 150, 100, 250, extruding: false);

        var placed = Placement(vm, root);
        Assert.Equal(90, placed.RotationDeg, 6);
        // An aim turns about the joint, which does not move.
        Assert.Equal(100, placed.X, 6);
        Assert.Equal(150, placed.Y, 6);
        // And the rest pose is untouched — a pose is a key, never an edit.
        Assert.Equal(0, vm.Doc.Armature!.BoneById(root)!.RotationDeg, 6);
    }

    [AvaloniaFact]
    public void TheJointGrabPutsTheBoneUnderThePointer()
    {
        var (vm, root, _) = Chain();

        vm.EndBoneGesture(root, BoneGrab.Origin, 100, 150, 140, 120, extruding: false);

        var placed = Placement(vm, root);
        Assert.Equal(140, placed.X, 6);
        Assert.Equal(120, placed.Y, 6);
        Assert.Equal(0, placed.RotationDeg, 6);
    }

    [AvaloniaFact]
    public void ACarriedPoseIsAKeyAtThePlayheadAndLeavesTheRestAlone()
    {
        var (vm, root, _) = Chain();
        vm.CurrentFrameIndex = 3;

        vm.EndBoneGesture(root, BoneGrab.Body, 130, 150, 160, 190, extruding: false);

        var key = ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, 3);
        Assert.NotNull(key);
        Assert.Equal(30, key!.Bones[root].X, 6);
        Assert.Equal(40, key.Bones[root].Y, 6);
        // The record's rest placement is where it was drawn.
        Assert.Equal(100, vm.Doc.Armature!.BoneById(root)!.X, 6);
        Assert.Equal(150, vm.Doc.Armature.BoneById(root)!.Y, 6);
    }

    [AvaloniaFact]
    public void ACarryOnAChildIsInItsParentsFrame()
    {
        var (vm, root, child) = Chain();
        // Turn the root a quarter clockwise first, so the child's parent frame
        // is not the document's — a delta written straight into the key
        // without that rotation would send the child the wrong way.
        vm.PoseBoneTo(root, 100, 250);
        var before = Placement(vm, child);

        vm.EndBoneGesture(child, BoneGrab.Body, before.X, before.Y, before.X + 20, before.Y, extruding: false);

        var after = Placement(vm, child);
        Assert.Equal(before.X + 20, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [AvaloniaFact]
    public void TheShaftPreviewShowsWhereTheReleaseLands()
    {
        var (vm, root, child) = Chain();

        // Several moves, as a real drag makes — the delta is measured from the
        // press each time, so the last one is the whole drag and not the sum.
        for (var i = 1; i <= 4; i++)
            vm.PreviewBoneGesture(root, BoneGrab.Body, 130, 150, 130 + i * 10, 150 + i * 5, extruding: false);
        var previewed = vm.BoneChromes.Single(c => c.Id == child && c.Ghost is BoneGhost.None);

        vm.ClearBoneGesturePreview();
        vm.EndBoneGesture(root, BoneGrab.Body, 130, 150, 170, 170, extruding: false);
        var landed = vm.BoneChromes.Single(c => c.Id == child && c.Ghost is BoneGhost.None);

        Assert.Equal(previewed.X0, landed.X0, 6);
        Assert.Equal(previewed.Y0, landed.Y0, 6);
        Assert.Equal(previewed.X1, landed.X1, 6);
        Assert.Equal(previewed.Y1, landed.Y1, 6);
    }

    [AvaloniaFact]
    public void TheCursorSaysWhichOfTheTwoAGrabWillDo()
    {
        // The tip aims and everything else moves, in both modes — the mode
        // stopped being part of this answer when posing learnt to carry.
        Assert.Equal(CanvasCursorKind.Rotate, CanvasCursor.ForBone(BoneGrab.Tip, posing: true));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Body, posing: true));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Origin, posing: true));
        Assert.Equal(CanvasCursorKind.Rotate, CanvasCursor.ForBone(BoneGrab.Tip, posing: false));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Body, posing: false));
    }
}
