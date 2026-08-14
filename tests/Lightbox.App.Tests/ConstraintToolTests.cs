using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Constraints are reachable, one gesture wide, and never offer a choice that
/// does nothing.
/// </summary>
/// <remarks>
/// The Bone tool's history is capabilities with no surface, so the surface is
/// what these guard: picking a target creates the constraint, the target list
/// omits the bones the solve would refuse, and the panel follows undo.
/// </remarks>
public class ConstraintToolTests
{
    private static MainViewModel Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 140, 100);   // head
        return vm;
    }

    /// <summary>Head plus an unrelated bone at (100,200) to look at.</summary>
    private static (MainViewModel Vm, string Head, string Look) Pair()
    {
        var vm = Rigged();
        var head = vm.SelectedBoneId!;
        vm.SelectedBoneId = null;                    // or the next drag parents to the head
        vm.CreateBoneFromDrag(100, 200, 120, 200);
        var look = vm.SelectedBoneId!;
        vm.SelectedBoneId = head;
        return (vm, head, look);
    }

    [AvaloniaFact]
    public void PickingATargetIsTheWholeGesture()
    {
        var (vm, head, look) = Pair();

        vm.ConstrainToKey = look;

        var c = Assert.Single(vm.Doc.Armature!.Constraints!);
        Assert.Equal(head, c.BoneId);
        Assert.Equal(look, c.TargetBoneId);
        Assert.Equal(BoneConstraintKind.Aim, c.Kind);
        // Selected for editing, and the picker snapped back so a second one
        // can be made against the same bone.
        Assert.True(vm.HasSelectedConstraint);
        Assert.Equal("", vm.ConstrainToKey);

        // And it reads on the canvas straight away: (100,100) looking at
        // (100,200) is straight down.
        vm.PosingMode = true;
        var placed = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(90, placed[head].RotationDeg, 4);
    }

    [AvaloniaFact]
    public void TheTargetListOmitsWhatTheSolveWouldRefuse()
    {
        var vm = Rigged();
        var head = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(140, 100, 180, 100);   // a child of the head
        var child = vm.SelectedBoneId!;
        vm.SelectedBoneId = head;

        var offered = vm.ConstraintTargetRows.Select(r => r.Id).ToList();

        // Itself and its own descendant both make a circle the solve refuses,
        // so offering them would produce a constraint that does nothing.
        Assert.DoesNotContain(head, offered);
        Assert.DoesNotContain(child, offered);
        Assert.Equal("", offered[0]);                 // the "(constrain to…)" row
    }

    [AvaloniaFact]
    public void TheKindIsChangedAfterwardsOnSomethingVisible()
    {
        var (vm, head, look) = Pair();
        vm.ConstrainToKey = look;

        vm.ConstraintKindIndex = (int)BoneConstraintKind.CopyPosition;

        Assert.Equal(BoneConstraintKind.CopyPosition, vm.Doc.Armature!.Constraints![0].Kind);
        vm.PosingMode = true;
        var placed = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(100, placed[head].X, 4);
        Assert.Equal(200, placed[head].Y, 4);
    }

    [AvaloniaFact]
    public void OffsetAndStrengthGoBackToAbsentAtTheirOrdinaryValues()
    {
        var (vm, _, look) = Pair();
        vm.ConstrainToKey = look;

        vm.ConstraintOffsetDeg = 15;
        vm.ConstraintInfluencePercent = 40;
        Assert.Equal(15, vm.Doc.Armature!.Constraints![0].OffsetDeg);
        Assert.Equal(0.4, vm.Doc.Armature!.Constraints![0].Influence!.Value, 6);

        // Back to ordinary means back to absent, not a stored zero and a
        // stored one — however the artist got there.
        vm.ConstraintOffsetDeg = 0;
        vm.ConstraintInfluencePercent = 100;
        Assert.Null(vm.Doc.Armature!.Constraints![0].OffsetDeg);
        Assert.Null(vm.Doc.Armature!.Constraints![0].Influence);
    }

    [AvaloniaFact]
    public void AFullStrengthConstraintOwnsTheTurnAndAPartialOneBlends()
    {
        var (vm, head, look) = Pair();
        vm.ConstrainToKey = look;
        vm.PosingMode = true;

        // At full strength the bone's angle is the constraint's, so an FK drag
        // on it lands a key that the solve then ignores. That is the rule, not
        // a defect — but it has to be true, or "strength" means nothing.
        vm.PoseBoneTo(head, 200, 100);
        var full = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(90, full[head].RotationDeg, 4);

        // Halve it and the key matters again: halfway between the drag and
        // the aim.
        vm.ConstraintInfluencePercent = 50;
        var half = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(45, half[head].RotationDeg, 4);
    }

    [AvaloniaFact]
    public void UnconstrainingLeavesTheBoneWhereItWas()
    {
        var (vm, head, look) = Pair();
        vm.ConstrainToKey = look;

        vm.RemoveConstraintCommand.Execute(null);

        // Absent, not empty.
        Assert.Null(vm.Doc.Armature!.Constraints);
        Assert.False(vm.HasSelectedConstraint);
        vm.PosingMode = true;
        var placed = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(0, placed[head].RotationDeg, 6);
    }

    [AvaloniaFact]
    public void TwoConstraintsStackAndThePanelListsBoth()
    {
        var (vm, head, look) = Pair();
        vm.ConstrainToKey = look;
        vm.ConstrainToKey = look;                     // a second one, same target
        vm.ConstraintKindIndex = (int)BoneConstraintKind.CopyPosition;

        Assert.Equal(2, vm.Doc.Armature!.Constraints!.Count);
        Assert.Equal(2, vm.ConstraintRows.Count);
        // The row says what it does, so a stack of two is legible without
        // opening each one.
        Assert.Contains("Aim at", vm.ConstraintRows[0].Name);
        Assert.Contains("Copy place of", vm.ConstraintRows[1].Name);

        vm.PosingMode = true;
        var placed = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(100, placed[head].X, 4);
        Assert.Equal(200, placed[head].Y, 4);
    }

    [AvaloniaFact]
    public void TheConstraintIsUndoableAndThePanelFollows()
    {
        var (vm, _, look) = Pair();
        vm.ConstrainToKey = look;
        Assert.True(vm.HasSelectedConstraint);

        vm.UndoCommand.Execute(null);

        // The record went back and so did the panel — the same notification
        // gap that was "no undo on the bones".
        Assert.Null(vm.Doc.Armature!.Constraints);
        Assert.False(vm.HasSelectedConstraint);
        Assert.Empty(vm.ConstraintRows);
    }

    [AvaloniaFact]
    public void DeletingTheTargetBoneDoesNotLeaveAConstraintPointingAtNothing()
    {
        var (vm, head, look) = Pair();
        vm.ConstrainToKey = look;

        vm.SelectedBoneId = look;
        vm.DeleteSelectedBone();

        // A constraint whose target is gone resolves to nothing and shows a
        // "?" in the panel — the honest record is that it is not there.
        Assert.Null(vm.Doc.Armature!.Constraints);
        vm.SelectedBoneId = head;
        Assert.Empty(vm.ConstraintRows);
    }
}
