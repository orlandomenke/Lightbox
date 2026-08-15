using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The bone tool previews per pointer move. It used to consume no move at
/// all — chrome and pixels both jumped on release, B112's un-fixed sibling —
/// because "one editor step per gesture" was met by suppressing the preview
/// instead of separating it from the commit. These tests pin the separation:
/// the drag shows the release's exact chrome, and the record never changes
/// until the release.
/// </summary>
public class BoneGesturePreviewTests(ITestOutputHelper output)
{
    private static MainViewModel RiggedVm()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 180, 100);
        return vm;
    }

    [AvaloniaFact]
    public void TheFirstDragPreviewsTheBoneItWouldCreateWithoutCreatingIt()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        Assert.Null(vm.Doc.Armature);

        vm.PreviewBoneGesture(null, BoneGrab.None, 100, 100, 180, 100, extruding: false);

        var chrome = Assert.Single(vm.BoneChromes);
        output.WriteLine($"preview bone {chrome.X0},{chrome.Y0} -> {chrome.X1},{chrome.Y1}");
        Assert.Equal(100, chrome.X0, 6);
        Assert.Equal(180, chrome.X1, 6);
        // The record is untouched: the preview is chrome, not an edit.
        Assert.Null(vm.Doc.Armature);

        vm.ClearBoneGesturePreview();
        Assert.Empty(vm.BoneChromes);
    }

    [AvaloniaFact]
    public void ThePreviewChromeIsTheCommitChrome()
    {
        // The strong promise: preview and release share one construction,
        // so the chrome mid-drag equals the chrome after the release lands,
        // for every bind-mode gesture.
        var gestures = new (string Name, Action<MainViewModel, string> Preview, Action<MainViewModel, string> Commit)[]
        {
            ("tip re-aim", (vm, id) => vm.PreviewBoneGesture(id, BoneGrab.Tip, 180, 100, 180, 160, false),
                           (vm, id) => vm.EndBoneGesture(id, BoneGrab.Tip, 180, 100, 180, 160, false)),
            ("origin move", (vm, id) => vm.PreviewBoneGesture(id, BoneGrab.Origin, 100, 100, 120, 130, false),
                            (vm, id) => vm.EndBoneGesture(id, BoneGrab.Origin, 100, 100, 120, 130, false)),
            ("shaft move", (vm, id) => vm.PreviewBoneGesture(id, BoneGrab.Body, 140, 100, 170, 145, false),
                           (vm, id) => vm.EndBoneGesture(id, BoneGrab.Body, 140, 100, 170, 145, false)),
            ("extrude", (vm, id) => vm.PreviewBoneGesture(id, BoneGrab.Tip, 180, 100, 240, 140, true),
                        (vm, id) => vm.EndBoneGesture(id, BoneGrab.Tip, 180, 100, 240, 140, true)),
        };

        foreach (var (name, preview, commit) in gestures)
        {
            var vm = RiggedVm();
            var id = vm.Doc.Armature!.Bones[0].Id;

            preview(vm, id);
            var previewed = vm.BoneChromes.Select(c => (c.Id, c.X0, c.Y0, c.X1, c.Y1)).ToList();
            var recordDuringDrag = vm.Doc.Armature.Bones.Count;

            commit(vm, id);
            var committed = vm.BoneChromes.Select(c => (c.Id, c.X0, c.Y0, c.X1, c.Y1)).ToList();

            output.WriteLine($"{name}: previewed {previewed.Count} bones, committed {committed.Count}");
            Assert.Equal(1, recordDuringDrag); // the drag edited nothing
            Assert.Equal(previewed.Count, committed.Count);
            for (var i = 0; i < previewed.Count; i++)
            {
                // Ids differ for created bones (a preview clone mints its own),
                // so compare the geometry, which is what the artist sees.
                Assert.Equal(previewed[i].X0, committed[i].X0, 6);
                Assert.Equal(previewed[i].Y0, committed[i].Y0, 6);
                Assert.Equal(previewed[i].X1, committed[i].X1, 6);
                Assert.Equal(previewed[i].Y1, committed[i].Y1, 6);
            }
        }
    }

    [AvaloniaFact]
    public void APoseDragPreviewsTheAimWithoutWritingAKey()
    {
        var vm = RiggedVm();
        var id = vm.Doc.Armature!.Bones[0].Id;
        vm.PosingMode = true;

        vm.PreviewBoneGesture(id, BoneGrab.Tip, 180, 100, 100, 180, extruding: false);

        var chrome = Assert.Single(vm.BoneChromes);
        output.WriteLine($"posed preview tip {chrome.X1:F1},{chrome.Y1:F1}");
        // The bone aims down from its origin at (100,100) towards the pointer.
        Assert.True(chrome.Y1 > 150, $"tip did not follow the pose drag: {chrome.Y1:F1}");
        // No key was written: the preview is not an edit.
        Assert.Null(vm.Doc.Scene.PoseTrack);

        // And the release writes exactly what the preview showed.
        vm.EndBoneGesture(id, BoneGrab.Tip, 180, 100, 100, 180, extruding: false);
        var committed = Assert.Single(vm.BoneChromes);
        Assert.NotNull(vm.Doc.Scene.PoseTrack);
        Assert.Equal(chrome.X1, committed.X1, 6);
        Assert.Equal(chrome.Y1, committed.Y1, 6);
    }

    [AvaloniaFact]
    public void TheReleaseIsStillOneUndoStep()
    {
        var vm = RiggedVm();
        var id = vm.Doc.Armature!.Bones[0].Id;
        var lengthBefore = vm.Doc.Armature.Bones[0].Length;

        // A drag with many preview steps…
        for (var i = 0; i < 12; i++)
        {
            vm.PreviewBoneGesture(id, BoneGrab.Tip, 180, 100, 180 + i * 5, 100, false);
        }
        vm.EndBoneGesture(id, BoneGrab.Tip, 180, 100, 240, 100, false);
        Assert.Equal(140, vm.Doc.Armature.Bones[0].Length, 6);

        // …lands one editor step: a single undo restores the bone.
        vm.UndoCommand.Execute(null);
        output.WriteLine($"after undo: length {vm.Doc.Armature!.Bones[0].Length}");
        Assert.Equal(lengthBefore, vm.Doc.Armature.Bones[0].Length, 6);
    }
}
