using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Secondary motion through the app: the panel authors the record, the
/// canvas shows the lag, and a bake writes what was shown.
/// </summary>
public class BoneJiggleAppTests
{
    /// <summary>A parent posed 90° down at frame 1, with a jiggling child.</summary>
    private static (MainViewModel Vm, string Parent, string Child) Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        // A new document is a single frame, and every edit clamps the
        // playhead back inside the timeline — so give the animation room
        // before posing at frame 1, or the pose lands and the playhead
        // silently walks home to 0.
        vm.Doc.Scene.FrameCount = 12;
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 150, 100);
        var parent = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(150, 100, 190, 100);
        var child = vm.SelectedBoneId!;
        vm.SelectedBoneJiggles = true;

        vm.PosingMode = true;
        vm.CurrentFrameIndex = 0;
        vm.PoseBoneTo(parent, 150, 100);      // a key at 0 that asks for rest
        vm.CurrentFrameIndex = 1;
        vm.PoseBoneTo(parent, 100, 200);      // 90° down at frame 1
        return (vm, parent, child);
    }

    [AvaloniaFact]
    public void TheChromeShowsTheChildLeftBehind()
    {
        var (vm, _, child) = Rigged();

        // At the step frame the parent stands straight down; a rigid child
        // would too. The jiggling child's chrome lags visibly.
        var chrome = vm.BoneChromes.First(c => c.Id == child && c.Ghost == BoneGhost.None);
        var droop = Math.Atan2(chrome.Y1 - chrome.Y0, chrome.X1 - chrome.X0) * 180 / Math.PI;
        Assert.True(droop < 89, $"the child snapped with the parent: {droop:F1}°");

        // Turning the jiggle off snaps it rigid again — and that is undoable,
        // because authoring a spring is an edit like any other.
        vm.SelectedBoneId = child;
        vm.SelectedBoneJiggles = false;
        chrome = vm.BoneChromes.First(c => c.Id == child && c.Ghost == BoneGhost.None);
        droop = Math.Atan2(chrome.Y1 - chrome.Y0, chrome.X1 - chrome.X0) * 180 / Math.PI;
        Assert.Equal(90, droop, 4);

        vm.UndoCommand.Execute(null);
        Assert.NotNull(vm.Doc.Armature!.BoneById(child)!.Jiggle);
    }

    [AvaloniaFact]
    public void BakingWritesTheLaggedPoseTheCanvasWasShowing()
    {
        var (vm, _, child) = Rigged();
        var frame = ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        var stroke = new Stroke { Points = [new StrokePoint(170, 100, 1)] };
        frame.Strokes.Add(stroke);
        vm.Selection.SelectStroke(stroke.Id);
        vm.SelectedBoneId = child;
        vm.AssignSelectedStrokesToBone();

        // What the render shows at frame 1: the point ridden by the lagging
        // child, which is NOT where a rigid pose would put it.
        var pose = ArmatureOps.EffectivePoseAt(vm.Doc.Armature!, vm.Doc.Scene.PoseTrack, 1);
        var shown = Skinning.PoseControlPoints(stroke, vm.Doc.Armature!, pose)[0];
        var rigid = Skinning.PoseControlPoints(
            stroke, vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, 1))[0];
        Assert.NotEqual(rigid.Y, shown.Y);

        var baked = vm.BakePoseHere();
        Assert.Equal(1, baked);
        // The record now holds the shown position: what you bake is what you
        // saw. Read back through the frame — the bake writes new strokes.
        var landed = ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!.Strokes[0];
        Assert.Equal(shown.X, landed.Points[0].X, 6);
        Assert.Equal(shown.Y, landed.Points[0].Y, 6);
    }

    [AvaloniaFact]
    public void TheSpringSettingsRoundTripThroughThePanelAndTheFile()
    {
        var (vm, _, child) = Rigged();
        vm.SelectedBoneId = child;

        vm.SelectedBoneJiggleStiffness = 0.45;
        vm.SelectedBoneJiggleDamping = 0.30;

        var json = DocJson.Serialize(vm.Doc);
        var reloaded = DocJson.Deserialize(json);
        var jiggle = reloaded.Armature!.BoneById(child)!.Jiggle;
        Assert.NotNull(jiggle);
        Assert.Equal(0.45, jiggle!.Stiffness, 9);
        Assert.Equal(0.30, jiggle.Damping, 9);
    }
}
