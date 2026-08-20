using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The X-sheet's optional word about what the Timeline carries.
/// </summary>
/// <remarks>
/// Asked for as an opt-in helper: with four drawings on the sheet and the
/// armature posed on frame 9, nothing on the sheet says frame 9 is different.
/// It cannot say it with a cel, because the division is deliberate — ink and
/// the persistent visuals drawn on the paper are the exposure sheet's, and the
/// camera and the armature are animated without being part of the exposure — so
/// it says it beside the frame number instead.
/// <para>
/// <b>Off by default, and empty rather than hidden when off.</b> A mark that
/// appeared by default would put non-drawing tracks back into the grid by the
/// back door, on every document including the ones that have neither.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class OffSheetKeyMarkTests : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    /// <summary>Four drawings on the sheet, a camera key at 2 and a pose at 9 — the reported shape.</summary>
    private static MainViewModel Scene()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < 4; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        vm.AddCameraCommand.Execute(null);
        vm.AddCameraKeyAt(2);
        return vm;
    }

    [AvaloniaFact]
    public void NothingIsMarkedUntilItIsAskedFor()
    {
        var vm = Scene();

        Assert.False(vm.MarkOffSheetKeys);
        Assert.Empty(vm.CameraKeyedFrames);
        Assert.Empty(vm.PoseKeyedFrames);
    }

    [AvaloniaFact]
    public void AskedFor_TheCameraKeyFramesAreMarked()
    {
        var vm = Scene();

        vm.MarkOffSheetKeys = true;

        Assert.Contains(2, vm.CameraKeyedFrames);
        // Only where a key actually is — the sheet must not imply the camera
        // is doing something on every frame it moves across.
        Assert.DoesNotContain(3, vm.CameraKeyedFrames);
    }

    [AvaloniaFact]
    public void ADocumentWithNeitherPaysNothingEvenWhenAskedFor()
    {
        var vm = VmLayers.BareVm();

        vm.MarkOffSheetKeys = true;

        // No camera, no rig: nothing to draw and nothing handed to the ruler.
        Assert.Empty(vm.CameraKeyedFrames);
        Assert.Empty(vm.PoseKeyedFrames);
    }

    [AvaloniaFact]
    public void TurningItOffTakesTheMarksAwayAgain()
    {
        var vm = Scene();
        vm.MarkOffSheetKeys = true;
        Assert.NotEmpty(vm.CameraKeyedFrames);

        vm.MarkOffSheetKeys = false;

        Assert.Empty(vm.CameraKeyedFrames);
    }

    [AvaloniaFact]
    public void TheChoiceSurvivesTheSession()
    {
        var vm = Scene();

        vm.MarkOffSheetKeys = true;

        // It is a preference about how the artist reads the sheet, so it is
        // kept the way the onion settings beside it are.
        Assert.True(vm.Settings.MarkOffSheetKeys);
    }

    [AvaloniaFact]
    public void AKeyAddedAfterwardsShowsWithoutBeingAskedAgain()
    {
        var vm = Scene();
        vm.MarkOffSheetKeys = true;
        Assert.DoesNotContain(7, vm.CameraKeyedFrames);

        vm.AddCameraKeyAt(7);

        Assert.Contains(7, vm.CameraKeyedFrames);
    }

    [AvaloniaFact]
    public void APoseKeyIsMarkedInItsOwnRight()
    {
        var vm = Scene();
        vm.Doc.Armature = new Armature
        {
            Bones = [new Bone { Id = "b", Name = "arm", X = 100, Y = 100, Length = 60 }],
        };
        // The reported shape exactly: four drawings on the sheet, and a pose on
        // a frame none of them key.
        vm.Doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 9 }],
        };

        vm.MarkOffSheetKeys = true;

        Assert.Contains(9, vm.PoseKeyedFrames);
        // The two kinds are reported apart, so the ruler can draw them in the
        // colours the Timeline already gives their tracks rather than in one
        // anonymous mark that needs a legend.
        Assert.DoesNotContain(9, vm.CameraKeyedFrames);
        Assert.Contains(2, vm.CameraKeyedFrames);
    }
}
