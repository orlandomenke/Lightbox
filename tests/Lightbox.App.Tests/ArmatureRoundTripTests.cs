using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// The rig must stand exactly where it was saved after a reload — the owner's
/// report was bones "sometimes" loading in a different spot. The record side
/// of that report: a rig built through the real gestures, serialized, parsed
/// back, and solved on both sides, bit-exact, at rest and posed.
/// </summary>
public class ArmatureRoundTripTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void TheRigStandsWhereItWasSavedAfterAReload()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;

        // A rig with every joint flavour: a root, an offset child, a connected
        // (extruded) grandchild, and drags that rewrite rotation, length and
        // origin — the exact edits that would expose a lossy save.
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var root = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(200, 220, 260, 240);          // parented to root, offset
        var child = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(child, 300, 260);               // glued to child's tip
        var grand = vm.SelectedBoneId!;
        vm.SelectedBoneJiggles = true;                      // springs on the tail bone

        vm.DragBoneBind(root, BoneGrab.Tip, 171.5, 183.25); // re-aim + re-length
        vm.DragBoneBind(child, BoneGrab.Origin, 213.7, 228.1); // move the joint
        vm.EndBoneGesture(grand, BoneGrab.None, 0, 0, 7.3, -4.9, false); // shaft drag (unglues)

        // Pose keys on two frames, so the posed solve interpolates on reload.
        vm.PosingMode = true;
        vm.PoseBoneTo(root, 120, 260);
        vm.CurrentFrameIndex = 6;
        vm.PoseBoneTo(child, 180, 120);

        var loaded = DocJson.Deserialize(DocJson.Serialize(vm.Doc));

        var savedRig = vm.Doc.Armature!;
        var loadedRig = loaded.Armature!;
        Assert.Equal(savedRig.Bones.Count, loadedRig.Bones.Count);

        // Bind pose, then the render pose at rest, keyed and between-key
        // frames — springs included, since that is what a reload replays.
        foreach (var frame in new int?[] { null, 0, 3, 6, 9 })
        {
            var a = frame is { } fa
                ? ArmatureOps.Solve(savedRig, ArmatureOps.EffectivePoseAt(savedRig, vm.Doc.Scene.PoseTrack, fa))
                : ArmatureOps.Solve(savedRig);
            var b = frame is { } fb
                ? ArmatureOps.Solve(loadedRig, ArmatureOps.EffectivePoseAt(loadedRig, loaded.Scene.PoseTrack, fb))
                : ArmatureOps.Solve(loadedRig);
            foreach (var bone in savedRig.Bones)
            {
                var (pa, pb) = (a[bone.Id], b[bone.Id]);
                output.WriteLine(
                    $"frame {frame?.ToString() ?? "bind"} {bone.Name}: saved ({pa.X:F6},{pa.Y:F6},{pa.RotationDeg:F6}) loaded ({pb.X:F6},{pb.Y:F6},{pb.RotationDeg:F6})");
                // Bit-exact, no tolerance: the file carries the same doubles
                // the session held, or a reload is a different drawing.
                Assert.Equal(pa.X, pb.X);
                Assert.Equal(pa.Y, pb.Y);
                Assert.Equal(pa.RotationDeg, pb.RotationDeg);
            }
        }

        // The glue itself survives: the shaft drag unglued the grandchild,
        // and a reload must agree about which joints are welded.
        foreach (var bone in savedRig.Bones)
            Assert.Equal(bone.IsConnected, loadedRig.BoneById(bone.Id)!.IsConnected);
    }
}
