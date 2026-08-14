using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Spline chains are reachable in one press, and the handles read as handles.
/// </summary>
/// <remarks>
/// The solve is <c>SplineChainTests</c>' job. These guard the surface, which
/// is where this tool has actually failed: a capability that works and cannot
/// be found is the defect reported three times over.
/// </remarks>
public class SplineToolTests
{
    /// <summary>Three 50px bones along +x from (100,100), the last one selected.</summary>
    private static MainViewModel Tail()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 150, 100);
        var root = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(root, 200, 100);
        vm.ExtrudeChildFrom(vm.SelectedBoneId!, 250, 100);
        return vm;
    }

    [AvaloniaFact]
    public void AddSplineLaysHandlesAlongTheRunAndChangesNothingYet()
    {
        var vm = Tail();
        var tip = vm.SelectedBoneId!;
        var before = ArmatureOps.Solve(vm.Doc.Armature!);

        Assert.True(vm.CanAddSpline);
        vm.AddSplineChainCommand.Execute(null);

        var spline = Assert.Single(vm.Doc.Armature!.Splines!);
        Assert.Equal(tip, spline.TipBoneId);
        Assert.Equal(3, spline.Bones);
        Assert.Equal(3, spline.HandleBoneIds.Count);
        Assert.True(vm.HasSelectedSpline);
        Assert.False(vm.CanAddSpline);
        // The last handle is selected, so the next thing to do is drag it.
        Assert.Equal(spline.HandleBoneIds[^1], vm.SelectedBoneId);

        // The curve starts as the shape already drawn, so adding a spline
        // moves nothing — an artist who presses the button and sees the rig
        // jump has been given a different rig than the one they built.
        var after = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        foreach (var id in before.Keys)
        {
            Assert.Equal(before[id].X, after[id].X, 3);
            Assert.Equal(before[id].Y, after[id].Y, 3);
            Assert.Equal(before[id].RotationDeg, after[id].RotationDeg, 3);
        }
    }

    [AvaloniaFact]
    public void DraggingAHandleBendsTheRun()
    {
        var vm = Tail();
        var tip = vm.SelectedBoneId!;
        vm.AddSplineChainCommand.Execute(null);
        var handle = vm.Doc.Armature!.Splines![0].HandleBoneIds[1];

        vm.PosingMode = true;
        vm.PoseBoneTo(handle, 175, 20);

        var placed = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.NotEqual(0, placed[tip].RotationDeg, 2);
        // Bent, not stretched: the bones are the length they were.
        var bones = vm.Doc.Armature!.Bones;
        foreach (var bone in bones.Where(b => b.ParentId is not null))
        {
            var parent = placed[bone.ParentId!];
            var (px, py) = parent.Tip(bones.First(b2 => b2.Id == bone.ParentId).Length);
            Assert.Equal(px, placed[bone.Id].X, 3);
            Assert.Equal(py, placed[bone.Id].Y, 3);
        }
    }

    [AvaloniaFact]
    public void HandlesAreMarkedAsHandlesSoTheyDrawApart()
    {
        var vm = Tail();
        vm.AddSplineChainCommand.Execute(null);
        var handles = vm.Doc.Armature!.Splines![0].HandleBoneIds.ToHashSet();

        var chrome = vm.BoneChromes;
        Assert.All(chrome.Where(c => handles.Contains(c.Id)), c => Assert.True(c.IsHandle));
        Assert.All(chrome.Where(c => !handles.Contains(c.Id)), c => Assert.False(c.IsHandle));
    }

    [AvaloniaFact]
    public void RemovingTheChainTakesItsHandlesWithIt()
    {
        var vm = Tail();
        var tip = vm.SelectedBoneId!;
        vm.AddSplineChainCommand.Execute(null);
        var handles = vm.Doc.Armature!.Splines![0].HandleBoneIds.ToList();

        vm.RemoveSplineChainCommand.Execute(null);

        Assert.Null(vm.Doc.Armature!.Splines);
        Assert.All(handles, id => Assert.Null(vm.Doc.Armature!.BoneById(id)));
        Assert.Equal(tip, vm.SelectedBoneId);
        Assert.True(vm.CanAddSpline);
    }

    [AvaloniaFact]
    public void AHandleSomethingElseLeansOnIsKept()
    {
        var vm = Tail();
        vm.AddSplineChainCommand.Execute(null);
        var handles = vm.Doc.Armature!.Splines![0].HandleBoneIds.ToList();
        // An IK chain aiming at one of the handles: deleting it would break a
        // second feature to tidy up after the first.
        vm.SelectedBoneId = vm.Doc.Armature!.Splines![0].TipBoneId;
        vm.AddIkChainCommand.Execute(null);
        vm.Doc.Armature!.Chains![0].PoleBoneId = handles[0];

        vm.SelectedBoneId = handles[1];
        vm.RemoveSplineChainCommand.Execute(null);

        Assert.Null(vm.Doc.Armature!.Splines);
        Assert.NotNull(vm.Doc.Armature!.BoneById(handles[0]));
        Assert.Null(vm.Doc.Armature!.BoneById(handles[1]));
    }

    [AvaloniaFact]
    public void DeletingAHandleThinsTheCurveRatherThanKillingIt()
    {
        var vm = Tail();
        vm.AddSplineChainCommand.Execute(null);
        var handles = vm.Doc.Armature!.Splines![0].HandleBoneIds.ToList();

        vm.SelectedBoneId = handles[1];
        vm.DeleteSelectedBone();

        // Three handles down to two is still a curve, so the chain survives.
        Assert.Equal(2, vm.Doc.Armature!.Splines![0].HandleBoneIds.Count);

        // Two down to one is not, so it goes — and absent, not empty.
        vm.SelectedBoneId = handles[0];
        vm.DeleteSelectedBone();
        Assert.Null(vm.Doc.Armature!.Splines);
    }

    [AvaloniaFact]
    public void TheSplineIsUndoableAndThePanelFollows()
    {
        var vm = Tail();
        vm.AddSplineChainCommand.Execute(null);
        Assert.True(vm.HasSelectedSpline);

        vm.UndoCommand.Execute(null);

        Assert.Null(vm.Doc.Armature!.Splines);
        Assert.False(vm.HasSelectedSpline);
    }

    [AvaloniaFact]
    public void BindModeStillEditsTheSkeletonUnderASpline()
    {
        var vm = Tail();
        var tip = vm.SelectedBoneId!;
        vm.AddSplineChainCommand.Execute(null);

        vm.SelectedBoneId = tip;
        vm.SelectedBoneLength = 80;

        Assert.Equal(80, vm.Doc.Armature!.BoneById(tip)!.Length, 6);
        Assert.Equal(280, ArmatureOps.Solve(vm.Doc.Armature!)[tip].Tip(80).X, 6);
    }
}
