using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// IK is reachable, one button wide, and never a dead gesture.
/// </summary>
/// <remarks>
/// The Bone tool has been reported three times for shipping a capability with
/// no surface — "no option to move bones around", "all other options
/// including weight painting is not clear at all", "how do I switch to weight
/// painting". So the tests that matter most here are not the solve (that is
/// <c>InverseKinematicsTests</c>) but the surface: the button exists and does
/// the whole job, the panel says what the chain is, and a pose drag on an
/// IK-driven bone moves the limb rather than writing a key nobody sees.
/// </remarks>
public class IkToolTests
{
    private static MainViewModel Limb()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 150, 100);   // upper
        var upper = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(upper, 200, 100);        // fore, glued at the elbow
        return vm;
    }

    [AvaloniaFact]
    public void AddIkMakesAWorkingLimbInOneClick()
    {
        var vm = Limb();
        var fore = vm.SelectedBoneId!;

        Assert.True(vm.CanAddIk);
        vm.AddIkChainCommand.Execute(null);

        var chain = Assert.Single(vm.Doc.Armature!.Chains!);
        Assert.Equal(fore, chain.TipBoneId);
        Assert.Equal(2, chain.Bones);
        // The target exists, sits at the limb's tip, and is what is selected —
        // so the next thing the artist does is the thing IK is for.
        var target = vm.Doc.Armature!.BoneById(chain.TargetBoneId)!;
        Assert.Equal(200, target.X, 6);
        Assert.Equal(100, target.Y, 6);
        Assert.Null(target.ParentId);
        Assert.Equal(target.Id, vm.SelectedBoneId);
        Assert.True(vm.HasSelectedChain);
        Assert.False(vm.CanAddIk);
    }

    [AvaloniaFact]
    public void DraggingTheTargetReachesTheWholeLimb()
    {
        var vm = Limb();
        vm.AddIkChainCommand.Execute(null);
        var chain = vm.Doc.Armature!.Chains![0];

        vm.PosingMode = true;
        vm.PoseBoneTo(chain.TargetBoneId, 140, 160);

        var pose = ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex);
        var placed = ArmatureOps.Solve(vm.Doc.Armature!, pose);
        var fore = vm.Doc.Armature!.BoneById(chain.TipBoneId)!;
        var (tx, ty) = placed[fore.Id].Tip(fore.Length);
        Assert.Equal(140, tx, 2);
        Assert.Equal(160, ty, 2);
    }

    [AvaloniaFact]
    public void APoseDragOnADrivenBoneMovesTheTargetInsteadOfDoingNothing()
    {
        var vm = Limb();
        var fore = vm.SelectedBoneId!;
        vm.AddIkChainCommand.Execute(null);
        var chain = vm.Doc.Armature!.Chains![0];

        vm.PosingMode = true;
        vm.PoseBoneTo(fore, 120, 170);

        // Without the redirect this would write a rotation key on `fore` that
        // the very next solve overwrites — a drag that visibly does nothing.
        var pose = ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex);
        Assert.False(pose.ContainsKey(fore) && pose[fore].RotationDeg != 0);
        var placed = ArmatureOps.Solve(vm.Doc.Armature!, pose);
        var bone = vm.Doc.Armature!.BoneById(fore)!;
        var (tx, ty) = placed[fore].Tip(bone.Length);
        Assert.Equal(120, tx, 2);
        Assert.Equal(170, ty, 2);
    }

    [AvaloniaFact]
    public void RemovingTheChainTakesItsHandleWithIt()
    {
        var vm = Limb();
        var fore = vm.SelectedBoneId!;
        vm.AddIkChainCommand.Execute(null);
        var targetId = vm.Doc.Armature!.Chains![0].TargetBoneId;

        vm.RemoveIkChainCommand.Execute(null);

        // Absent, not empty — a rig with no IK writes no chains key.
        Assert.Null(vm.Doc.Armature!.Chains);
        Assert.Null(vm.Doc.Armature!.BoneById(targetId));
        // And the selection lands somewhere real rather than on the bone that
        // was just deleted.
        Assert.Equal(fore, vm.SelectedBoneId);
        Assert.True(vm.CanAddIk);
    }

    [AvaloniaFact]
    public void AHandleSomethingElseHangsOffIsKept()
    {
        var vm = Limb();
        vm.AddIkChainCommand.Execute(null);
        var targetId = vm.Doc.Armature!.Chains![0].TargetBoneId;
        // A prop bone parented under the handle: deleting it would silently
        // take the prop's parentage with it.
        vm.CreateBoneFromDrag(200, 100, 240, 100);

        vm.SelectedBoneId = targetId;
        vm.RemoveIkChainCommand.Execute(null);

        Assert.Null(vm.Doc.Armature!.Chains);
        Assert.NotNull(vm.Doc.Armature!.BoneById(targetId));
    }

    [AvaloniaFact]
    public void TheChainIsUndoableAndThePanelFollows()
    {
        var vm = Limb();
        vm.AddIkChainCommand.Execute(null);
        Assert.True(vm.HasSelectedChain);

        vm.UndoCommand.Execute(null);

        // The record went back, and so did the panel: the "no undo on the
        // bones" report was the panel staying stale, not the record.
        Assert.Null(vm.Doc.Armature!.Chains);
        Assert.False(vm.HasSelectedChain);
    }

    [AvaloniaFact]
    public void ThePoleIsPickableAndNullable()
    {
        var vm = Limb();
        vm.AddIkChainCommand.Execute(null);
        vm.CreateBoneFromDrag(150, 40, 170, 40);      // something to point at
        var pole = vm.SelectedBoneId!;
        vm.SelectedBoneId = vm.Doc.Armature!.Chains![0].TargetBoneId;

        vm.SelectedChainPoleKey = pole;
        Assert.Equal(pole, vm.Doc.Armature!.Chains![0].PoleBoneId);

        // "(no pole)" is a real choice and has to survive the round trip
        // through a combo box that only speaks strings.
        Assert.Equal("", vm.PoleRows[0].Id);
        vm.SelectedChainPoleKey = "";
        Assert.Null(vm.Doc.Armature!.Chains![0].PoleBoneId);
    }

    [AvaloniaFact]
    public void TheHandleIsMarkedAsOneAndDrawnApartFromTheBody()
    {
        var vm = Limb();
        vm.AddIkChainCommand.Execute(null);
        var targetId = vm.Doc.Armature!.Chains![0].TargetBoneId;

        var chrome = vm.BoneChromes;
        Assert.True(chrome.Single(c => c.Id == targetId).IsHandle);
        Assert.All(chrome.Where(c => c.Id != targetId), c => Assert.False(c.IsHandle));

        // And it reads differently on the canvas: amber against the rig's
        // green, so the thing you grab to pose a limb is findable without
        // already knowing where it is.
        using var bmp = new SKBitmap(new SKImageInfo(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        ArmatureOverlayPainter.Paint(
            canvas,
            [
                new BoneChrome("bone", "arm", 20, 40, 120, 40, false),
                new BoneChrome("handle", "arm.ik", 20, 120, 120, 120, false, IsHandle: true),
            ],
            scale: 1);
        canvas.Flush();

        var body = bmp.GetPixel(70, 40);
        var handle = bmp.GetPixel(70, 120);
        Assert.True(body.Green > body.Red, $"body bone not green: {body}");
        Assert.True(handle.Red > handle.Green, $"handle not amber: {handle}");
    }

    [AvaloniaFact]
    public void BindModeStillEditsTheSkeletonUnderAChain()
    {
        var vm = Limb();
        var fore = vm.SelectedBoneId!;
        vm.AddIkChainCommand.Execute(null);

        // Re-lengthening a chain bone in bind mode must land on the record,
        // not be pulled back by the solver — bind mode is the rest, and the
        // rest has no IK in it.
        vm.SelectedBoneId = fore;
        vm.SelectedBoneLength = 70;
        Assert.Equal(70, vm.Doc.Armature!.BoneById(fore)!.Length, 6);
        Assert.Equal(220, ArmatureOps.Solve(vm.Doc.Armature!)[fore].Tip(70).X, 6);
    }
}
