using Avalonia.Headless.XUnit;
using Lightbox.App.Docking;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// The Bone tool says what it is about to do, and what it has.
/// </summary>
/// <remarks>
/// <para>
/// Reported in three parts, all one defect: "no option to move bones around,
/// or to select them and do operations on"; "I am able to move and rotate
/// them, but it is not clear when that happens"; "all other options including
/// weight painting is not clear at all". Every one of them is a capability
/// that existed and was invisible — the tool's whole state lived in two
/// shortcuts and a five-pixel handle.
/// </para>
/// <para>
/// So these guard the <em>surfaces</em>: the cursor that says what a press
/// would do, the list that says what the rig contains, and the bar's
/// registration. The operations underneath are guarded here too, because they
/// are new: rename, delete-with-reparent, and the shaft drag that moves a
/// bone.
/// </para>
/// </remarks>
public class BoneOptionsTests
{
    private static MainViewModel Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        return vm;
    }

    // ---- the pointer says what the press would do ---------------------------------

    [AvaloniaFact]
    public void TheCursorTellsMovingApartFromTurning()
    {
        // Binding: the shaft and the joint move it, the tip re-aims it.
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Body, posing: false));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Origin, posing: false));
        Assert.Equal(CanvasCursorKind.Rotate, CanvasCursor.ForBone(BoneGrab.Tip, posing: false));

        // Posing reads the same grabs the same way (owner's decision,
        // 2026-08-18): the shaft and the joint carry the bone, the tip aims
        // it. The mode decides where the edit lands — the rest pose or the
        // pose key — never what kind of edit a grab is.
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Body, posing: true));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.ForBone(BoneGrab.Origin, posing: true));
        Assert.Equal(CanvasCursorKind.Rotate, CanvasCursor.ForBone(BoneGrab.Tip, posing: true));

        // Move and Rotate must never collapse to one cursor: telling them
        // apart is the entire feature.
        Assert.NotEqual(
            PointerCursors.For(CanvasCursorKind.Move),
            PointerCursors.For(CanvasCursorKind.Rotate));
    }

    [Fact]
    public void EmptyCanvasSaysADragWouldDrawABoneOnlyWhereItWould()
    {
        // Bind mode: a drag here makes a bone, so the pointer is a placing one.
        Assert.Equal(CanvasCursorKind.Precise, CanvasCursor.ForBone(BoneGrab.None, posing: false));
        // Posing empty canvas does nothing, so it must not promise a mark.
        Assert.Equal(CanvasCursorKind.PickRecords, CanvasCursor.ForBone(BoneGrab.None, posing: true));
    }

    [Fact]
    public void TheArmedWeightBrushOwnsThePointer()
    {
        // While the brush is armed a press paints wherever it lands, bone or
        // not, so no bone-shaped promise may override it.
        foreach (var grab in Enum.GetValues<BoneGrab>())
        {
            Assert.Equal(
                CanvasCursorKind.Paint,
                CanvasCursor.ForBone(grab, posing: false, weightPainting: true));
            Assert.Equal(
                CanvasCursorKind.Paint,
                CanvasCursor.ForBone(grab, posing: true, weightPainting: true));
        }
    }

    [AvaloniaFact]
    public void ThePointerFollowsWhatIsUnderItOnTheCanvas()
    {
        var vm = Rigged();
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.CreateBoneFromDrag(100, 150, 200, 150);

        // Over the shaft in bind mode.
        vm.UpdatePointerContext(150, 150, Avalonia.Input.KeyModifiers.None, scale: 1);
        Assert.Equal(CanvasCursorKind.Move, vm.PointerIntent);

        // Over the tip.
        vm.UpdatePointerContext(200, 150, Avalonia.Input.KeyModifiers.None, scale: 1);
        Assert.Equal(CanvasCursorKind.Rotate, vm.PointerIntent);

        // The same shaft, once posing: still a move, because posing carries a
        // bone by its shaft just as binding does.
        vm.PosingMode = true;
        vm.UpdatePointerContext(150, 150, Avalonia.Input.KeyModifiers.None, scale: 1);
        Assert.Equal(CanvasCursorKind.Move, vm.PointerIntent);

        // And the tip is where a pose turns from.
        vm.UpdatePointerContext(200, 150, Avalonia.Input.KeyModifiers.None, scale: 1);
        Assert.Equal(CanvasCursorKind.Rotate, vm.PointerIntent);
    }

    // ---- the rig is visible and operable ------------------------------------------

    [AvaloniaFact]
    public void TheBarListsEveryBoneWithItsHierarchy()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);   // root
        vm.CreateBoneFromDrag(160, 150, 200, 150);   // child of it

        var rows = vm.BoneRows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(0, rows[0].Depth);
        Assert.Equal(1, rows[1].Depth);
        // Children are indented under their parent, which is how a flat combo
        // box still shows parentage.
        Assert.StartsWith("    ", rows[1].Label);
        Assert.True(rows[1].Selected, "the newest bone is the selected one");
    }

    [AvaloniaFact]
    public void RenamingIsHowASymmetryPairIsMade()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);

        vm.SelectedBoneName = "hip.l";

        Assert.Equal("hip.l", vm.SelectedBone!.Name);
        Assert.Equal("hip.l", vm.BoneRows[0].Name);
        vm.UndoCommand.Execute(null);
        Assert.NotEqual("hip.l", vm.SelectedBone!.Name);
    }

    [AvaloniaFact]
    public void DeletingABoneKeepsItsChildrenWhereTheyAre()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 160, 100);   // root
        var root = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(160, 100, 220, 100);   // middle
        var middle = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(220, 100, 280, 100);   // tip child
        var child = vm.SelectedBoneId!;

        var before = ArmatureOps.Solve(vm.Doc.Armature!)[child];

        vm.SelectedBoneId = middle;
        vm.DeleteSelectedBone();

        // The middle bone is gone, the child survives re-parented to the root
        // — and has not moved a hair, which is what stops a delete reading as
        // corruption.
        Assert.Null(vm.Doc.Armature!.BoneById(middle));
        var after = ArmatureOps.Solve(vm.Doc.Armature!)[child];
        Assert.Equal(root, vm.Doc.Armature!.BoneById(child)!.ParentId);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(before.RotationDeg, after.RotationDeg, 6);
    }

    [AvaloniaFact]
    public void DeletingABoneTakesItsWeightsAndItsPoseKeysWithIt()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 160, 100);
        var id = vm.SelectedBoneId!;
        var frame = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        var stroke = new Stroke { Points = [new StrokePoint(120, 100, 1), new StrokePoint(150, 100, 1)] };
        frame.Strokes.Add(stroke);
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();
        vm.PosingMode = true;
        vm.PoseBoneTo(id, 100, 160);

        vm.SelectedBoneId = id;
        vm.DeleteSelectedBone();

        // A binding to a bone that does not exist blends against an identity
        // and silently holds its points at rest — an invisible half-state.
        Assert.Null(stroke.Weights);
        Assert.All(vm.Doc.Scene.PoseTrack?.Keys ?? [], k => Assert.DoesNotContain(id, k.Bones.Keys));
    }

    [AvaloniaFact]
    public void DraggingTheShaftMovesTheBoneAndItsChildren()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 160, 100);
        var root = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(160, 100, 200, 100);
        var child = vm.SelectedBoneId!;

        vm.MoveBoneBy(root, 25, -10);

        var placements = ArmatureOps.Solve(vm.Doc.Armature!);
        Assert.Equal(125, placements[root].X, 6);
        Assert.Equal(90, placements[root].Y, 6);
        // The child rides along, because its offset is relative to the parent.
        Assert.Equal(185, placements[child].X, 6);
        Assert.Equal(90, placements[child].Y, 6);
    }

    [AvaloniaFact]
    public void ReparentingNeverMovesTheBoneAndRefusesACycle()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 160, 100);
        var root = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(160, 100, 220, 140);
        var child = vm.SelectedBoneId!;

        var before = ArmatureOps.Solve(vm.Doc.Armature!)[child];
        vm.SelectedBoneId = child;
        vm.SetSelectedBoneParent(null);

        var after = ArmatureOps.Solve(vm.Doc.Armature!)[child];
        Assert.Null(vm.Doc.Armature!.BoneById(child)!.ParentId);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(before.RotationDeg, after.RotationDeg, 6);

        // A bone cannot become its own descendant's child: the solve would
        // walk in a circle.
        vm.SetSelectedBoneParent(root);
        vm.SelectedBoneId = root;
        vm.SetSelectedBoneParent(child);
        Assert.Null(vm.Doc.Armature!.BoneById(root)!.ParentId);
    }

    // ---- the surface keeps up with the record --------------------------------------

    [AvaloniaFact]
    public void UndoTakesTheBoneOffTheCanvasAndOutOfThePanel()
    {
        var vm = Rigged();
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.CreateBoneFromDrag(100, 100, 180, 100);
        Assert.True(vm.HasArmature);
        Assert.Single(vm.BoneChromes);

        vm.UndoCommand.Execute(null);

        // The record undid before this fix too. What did not happen is any of
        // the below — so the bone stayed drawn and the panel kept its answer,
        // which is indistinguishable from undo being broken.
        Assert.False(vm.HasArmature, "the panel still believes there is a rig");
        Assert.Empty(vm.BoneChromes);
        Assert.Empty(vm.BoneRows);
        Assert.Null(vm.SelectedBoneId);
        Assert.False(vm.HasSelectedBone);
    }

    [AvaloniaFact]
    public void RedoBringsItBackToBothOfThem()
    {
        var vm = Rigged();
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.CreateBoneFromDrag(100, 100, 180, 100);
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);

        Assert.True(vm.HasArmature);
        Assert.Single(vm.BoneChromes);
        Assert.Single(vm.BoneRows);
    }

    // ---- building a limb -------------------------------------------------------------

    [AvaloniaFact]
    public void ExtrudingFromATipGrowsAJoinedChild()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);   // a 100px bone along +x
        var parent = vm.SelectedBoneId!;

        vm.ExtrudeChildFrom(parent, 200, 180);

        var child = vm.Doc.Armature!.Bones.Single(b => b.Id != parent);
        Assert.Equal(parent, child.ParentId);
        // Joined: the child starts exactly at the parent's tip, whatever the
        // parent later does. That is what makes bending the parent carry the
        // chain with no gap to close by hand.
        var placements = ArmatureOps.Solve(vm.Doc.Armature!);
        Assert.Equal(200, placements[child.Id].X, 6);
        Assert.Equal(100, placements[child.Id].Y, 6);
        Assert.Equal(80, child.Length, 6);
        Assert.Equal(child.Id, vm.SelectedBoneId);
    }

    [AvaloniaFact]
    public void BendingOrRelengthingTheParentBothCarryTheChain()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);
        var parent = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(parent, 200, 180);
        var child = vm.SelectedBoneId!;

        // Rotating the parent carries the child, because the child's offset
        // lives in the parent's frame. This is the common case and the one
        // posing depends on.
        vm.SelectedBoneId = parent;
        vm.PosingMode = true;
        vm.PoseBoneTo(parent, 100, 200);          // parent swings to point down
        var posed = ArmatureOps.Solve(vm.Doc.Armature!, ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex));
        Assert.Equal(100, posed[child].X, 6);
        Assert.Equal(200, posed[child].Y, 6);

        // Re-lengthening the parent drags the child after it, because an
        // extruded child is glued to the tip rather than placed at it (Q86).
        // Without the connected flag the child would stay at x=200 and the
        // limb would come apart at the joint.
        vm.PosingMode = false;
        vm.SelectedBoneLength = 150;
        var rest = ArmatureOps.Solve(vm.Doc.Armature!);
        Assert.Equal(250, rest[child].X, 6);
        Assert.Equal(100, rest[child].Y, 6);
    }

    [AvaloniaFact]
    public void DraggingAGluedJointUngluesItInsteadOfDoingNothing()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);
        var parent = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(parent, 200, 180);
        var child = vm.SelectedBoneId!;

        vm.DragBoneBind(child, BoneGrab.Origin, 200, 140);
        Assert.False(vm.Doc.Armature!.BoneById(child)!.IsConnected);
        var moved = ArmatureOps.Solve(vm.Doc.Armature!);
        Assert.Equal(200, moved[child].X, 6);
        Assert.Equal(140, moved[child].Y, 6);

        // And it stays put now: unglued means the parent's length no longer
        // decides where the joint sits.
        vm.SelectedBoneId = parent;
        vm.SelectedBoneLength = 150;
        var after = ArmatureOps.Solve(vm.Doc.Armature!);
        Assert.Equal(200, after[child].X, 6);
        Assert.Equal(140, after[child].Y, 6);
    }

    [AvaloniaFact]
    public void AnUngluedBoneWritesNoConnectedKey()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);
        var json = DocJson.Serialize(vm.Doc);

        // The camera's rule: a document whose bones are all ordinary roots
        // pays not one key for the connected flag.
        Assert.DoesNotContain("\"connected\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void LengthIsEditableByNumberAndUndoable()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);
        Assert.Equal(100, vm.SelectedBoneLength, 6);

        vm.SelectedBoneLength = 42;
        Assert.Equal(42, vm.SelectedBone!.Length, 6);

        // Floored rather than accepted: a zero-length bone has no direction,
        // so a pose rotation of it would mean nothing.
        vm.SelectedBoneLength = 0;
        Assert.True(vm.SelectedBone!.Length >= ArmatureOverlay.MinimumLength);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(100, vm.SelectedBoneLength, 6);
    }

    [AvaloniaFact]
    public void AddChildFromThePanelGrowsOneStraightOn()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);
        var parent = vm.SelectedBoneId!;

        vm.AddChildBoneCommand.Execute(null);

        var child = vm.Doc.Armature!.Bones.Single(b => b.Id != parent);
        Assert.Equal(parent, child.ParentId);
        // Straight on from the parent, so it is a starting point to aim rather
        // than a guess about where the limb goes.
        Assert.Equal(0, child.RotationDeg, 6);
    }

    // ---- one switch, three positions -------------------------------------------------

    [AvaloniaFact]
    public void TheThreeModesAreExclusiveSoTheSwitchCanShowThem()
    {
        var vm = Rigged();
        Assert.True(vm.IsBindMode);

        vm.PosingMode = true;
        Assert.False(vm.IsBindMode);
        Assert.False(vm.WeightPainting);

        // Arming the brush leaves posing rather than sitting on top of it:
        // weights are painted against the REST pose (Q81), so "posing and
        // painting" was a state whose canvas disagreed with its edit.
        vm.WeightPainting = true;
        Assert.False(vm.PosingMode);
        Assert.False(vm.IsBindMode);

        // And back, through the switch's own property.
        vm.IsBindMode = true;
        Assert.False(vm.PosingMode);
        Assert.False(vm.WeightPainting);
    }

    [AvaloniaFact]
    public void EachModeAnnouncesTheSwitchMoved()
    {
        var vm = Rigged();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.WeightPainting = true;
        // Without this the radio for Bind stays lit while the brush is armed,
        // which is the invisibility bug in its smallest form.
        Assert.Contains(nameof(MainViewModel.IsBindMode), fired);

        fired.Clear();
        vm.PosingMode = true;
        Assert.Contains(nameof(MainViewModel.IsBindMode), fired);
        Assert.Contains(nameof(MainViewModel.WeightPainting), fired);
    }
}
