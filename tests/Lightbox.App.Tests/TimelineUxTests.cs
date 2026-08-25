using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The timeline's key clipboard, the bone rows' folding, and the key
/// inspector — the owner's timeline round of 2026-08-21.
/// </summary>
/// <remarks>
/// <para>
/// <b>The clipboard crosses kinds for retiming's reason</b>: "this beat,
/// there too" means the camera key, the pose and the drawing together, at
/// their distances. Paste lands per kind — replace a camera or whole pose
/// key, join a bone into the key already there, set a cel — and the lot is
/// one undo step.
/// </para>
/// <para>
/// <b>The bone rows fold by parentage</b>, so a twenty-bone character shows
/// the limb in hand rather than twenty rows or none — the Bones toggle's
/// all-or-nothing was the previous vocabulary.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TimelineUxTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    /// <summary>All three kinds at once: drawings, a camera, a two-bone rig.</summary>
    private static (MainViewModel Vm, string Hip, string Knee) Rig(int frames = 12)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < frames; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        vm.AddCameraCommand.Execute(null);
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 100, 150);
        var hip = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(hip, 100, 200);
        var knee = vm.SelectedBoneId!;
        vm.PosingMode = true;
        return (vm, hip, knee);
    }

    private static PoseKey? PoseKeyAt(MainViewModel vm, int frame) =>
        ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, frame);

    // ---- the key clipboard -----------------------------------------------------

    [AvaloniaFact]
    public void CopyPasteLandsTheBeatWithItsOffsetsKept()
    {
        var (vm, hip, _) = Rig();
        vm.AddCameraKeyAt(1);
        vm.EditCameraKey("Zoom", 1, 1, 2.0);
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);

        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Pose(2), toggle: true, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 3), toggle: true, range: false);
        Assert.Equal(3, vm.CopySelectedTimelineKeys());

        Assert.Equal(3, vm.PasteTimelineKeysAt(6));

        // The earliest key lands on the aim; the rest keep their distances.
        var pasted = CameraOps.KeyAt(vm.Doc.Scene.Camera, 6);
        Assert.NotNull(pasted);
        output.WriteLine($"zoom copied {pasted!.Zoom:F2}");
        Assert.Equal(2.0, pasted.Zoom, 3);
        Assert.NotNull(PoseKeyAt(vm, 7));
        Assert.Contains(hip, PoseKeyAt(vm, 7)!.Bones.Keys);
        Assert.NotNull(vm.Doc.Scene.Layers[0].Cels[8].Frame);
    }

    [AvaloniaFact]
    public void PasteIsOneUndoStep()
    {
        var (vm, hip, _) = Rig();
        vm.AddCameraKeyAt(1);
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Pose(2), toggle: true, range: false);
        vm.CopySelectedTimelineKeys();

        vm.PasteTimelineKeysAt(6);
        vm.UndoCommand.Execute(null);

        Assert.Null(CameraOps.KeyAt(vm.Doc.Scene.Camera, 6));
        Assert.Null(PoseKeyAt(vm, 7));
        // And the originals never moved.
        Assert.NotNull(CameraOps.KeyAt(vm.Doc.Scene.Camera, 1));
        Assert.NotNull(PoseKeyAt(vm, 2));
    }

    [AvaloniaFact]
    public void CopyingABoneRowKeyCarriesThatBonesValues()
    {
        var (vm, hip, knee) = Rig();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.PoseBoneTo(knee, 60, 190);
        var source = PoseKeyAt(vm, 2)!.Bones[knee].RotationDeg;

        vm.SelectTrackKey(TimelineKey.Pose(2, knee), toggle: false, range: false);
        Assert.Equal(1, vm.CopySelectedTimelineKeys());
        Assert.Equal(1, vm.PasteTimelineKeysAt(5));

        var landed = PoseKeyAt(vm, 5)!;
        output.WriteLine($"knee rotation copied {source:F2}, pasted {landed.Bones[knee].RotationDeg:F2}");
        Assert.Equal(source, landed.Bones[knee].RotationDeg, 6);
    }

    [AvaloniaFact]
    public void CutRemovesExactlyWhatItCopied()
    {
        var (vm, hip, _) = Rig();
        vm.AddCameraKeyAt(1);
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);

        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.SelectTrackKey(TimelineKey.Cel(0, 3), toggle: true, range: false);
        Assert.Equal(2, vm.CutTimelineKeys(TimelineKey.Camera(1)));

        Assert.Null(CameraOps.KeyAt(vm.Doc.Scene.Camera, 1));
        Assert.Null(vm.Doc.Scene.Layers[0].Cels[3].Frame);
        // The pose key beside them was not in the selection and stands.
        Assert.NotNull(PoseKeyAt(vm, 2));

        // And the beat comes back where it is aimed.
        Assert.Equal(2, vm.PasteTimelineKeysAt(1));
        Assert.NotNull(CameraOps.KeyAt(vm.Doc.Scene.Camera, 1));
        Assert.NotNull(vm.Doc.Scene.Layers[0].Cels[3].Frame);
    }

    [AvaloniaFact]
    public void PastingKeysWhoseHomeIsGoneSkipsThemAndSaysSo()
    {
        var (vm, _, _) = Rig();
        vm.AddCameraKeyAt(1);
        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        vm.CopySelectedTimelineKeys();

        vm.RemoveCameraCommand.Execute(null);

        Assert.Equal(0, vm.PasteTimelineKeysAt(6));
    }

    // ---- the bone rows fold by parentage ----------------------------------------

    [AvaloniaFact]
    public void FoldingABoneHidesItsSubtree()
    {
        var (vm, hip, knee) = Rig();
        vm.PoseRowsExpanded = true;

        // Rows: 0 camera, 1 armature summary, 2 hip, 3 knee.
        Assert.Equal(hip, vm.BoneOfTrack(2));
        Assert.Equal(knee, vm.BoneOfTrack(3));

        vm.ToggleTrackFold(2);

        var pose = vm.TimelineTracks.Where(t => t.IsArmature).ToList();
        Assert.Equal(2, pose.Count);               // the summary and the hip
        Assert.True(pose[1].HasChildren);
        Assert.True(pose[1].Folded);
        Assert.Equal(hip, vm.BoneOfTrack(2));
        Assert.Null(vm.BoneOfTrack(3));            // folded away
        Assert.Equal(3, vm.TracksAboveLayers);     // camera + summary + hip

        vm.ToggleTrackFold(2);
        Assert.Equal(knee, vm.BoneOfTrack(3));
    }

    [AvaloniaFact]
    public void TheSummaryRowsChevronIsTheBonesToggle()
    {
        var (vm, _, _) = Rig();
        Assert.False(vm.PoseRowsExpanded);

        vm.ToggleTrackFold(1);
        Assert.True(vm.PoseRowsExpanded);
        vm.ToggleTrackFold(1);
        Assert.False(vm.PoseRowsExpanded);
    }

    [AvaloniaFact]
    public void AChildsRowIndentsBelowItsParent()
    {
        var (vm, _, _) = Rig();
        vm.PoseRowsExpanded = true;

        var pose = vm.TimelineTracks.Where(t => t.IsArmature).ToList();
        Assert.StartsWith("    ", pose[1].Name);
        Assert.StartsWith("        ", pose[2].Name);  // one level deeper
        Assert.True(pose[1].HasChildren);
        Assert.False(pose[2].HasChildren);
    }

    // ---- the key inspector -------------------------------------------------------

    [AvaloniaFact]
    public void TheInspectorShowsExactlyOneSelectedKey()
    {
        var (vm, _, _) = Rig();
        vm.AddCameraKeyAt(1);
        Assert.False(vm.TimelineInspectorVisible);

        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);
        Assert.True(vm.TimelineInspectorVisible);
        Assert.True(vm.InspectorIsCamera);

        // Plural means absent — writing one number into five keys is a
        // different feature.
        vm.SelectTrackKey(TimelineKey.Cel(0, 2), toggle: true, range: false);
        Assert.False(vm.TimelineInspectorVisible);
    }

    [AvaloniaFact]
    public void WritingZoomLandsInTheCameraKey()
    {
        var (vm, _, _) = Rig();
        vm.AddCameraKeyAt(1);
        vm.SelectTrackKey(TimelineKey.Camera(1), toggle: false, range: false);

        vm.InspectorZoom = 2.0;

        Assert.Equal(2.0, CameraOps.KeyAt(vm.Doc.Scene.Camera, 1)!.Zoom, 3);
    }

    [AvaloniaFact]
    public void WritingRotationLandsInTheBoneKeyAndUndoes()
    {
        var (vm, hip, _) = Rig();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.SelectTrackKey(TimelineKey.Pose(2, hip), toggle: false, range: false);
        Assert.True(vm.InspectorIsBone);

        vm.InspectorRotation = 30;
        Assert.Equal(30, PoseKeyAt(vm, 2)!.Bones[hip].RotationDeg, 6);

        vm.UndoCommand.Execute(null);
        Assert.NotEqual(30, PoseKeyAt(vm, 2)!.Bones[hip].RotationDeg, 6);
    }

    [AvaloniaFact]
    public void DepthEditsTheSelectedCelsLayer()
    {
        var (vm, _, _) = Rig();
        vm.SelectTrackKey(TimelineKey.Cel(0, 3), toggle: false, range: false);
        Assert.True(vm.InspectorIsCel);

        vm.InspectorDepth = 500;

        Assert.Equal(500, vm.Doc.Scene.Layers[0].Depth ?? 0, 3);
    }

    [AvaloniaFact]
    public void TheSummaryKeyOffersAHintRatherThanNumbers()
    {
        var (vm, hip, _) = Rig();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        vm.SelectTrackKey(TimelineKey.Pose(2), toggle: false, range: false);

        Assert.True(vm.InspectorIsPoseSummary);
        Assert.False(vm.InspectorHasPosition);
        Assert.NotEqual("", vm.InspectorHint);
    }

    // ---- keying from the empty-track menu -----------------------------------------

    [AvaloniaFact]
    public void KeyingThePoseHereKeysWhatIsAlreadyThere()
    {
        var (vm, hip, _) = Rig();
        vm.CurrentFrameIndex = 2;
        vm.PoseBoneTo(hip, 140, 130);
        var before = PoseKeyAt(vm, 2)!.Bones[hip].RotationDeg;

        vm.AddPoseKeyAt(6);

        var key = PoseKeyAt(vm, 6);
        Assert.NotNull(key);
        // Seeded from the interpolated pose — past the last key that is the
        // held pose, so nothing jumps.
        output.WriteLine($"held {before:F2}, keyed {key!.Bones[hip].RotationDeg:F2}");
        Assert.Equal(before, key.Bones[hip].RotationDeg, 6);
    }
}
