using Avalonia.Headless.XUnit;
using Lightbox.App.Docking;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

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

        // Posing turns whatever it takes hold of, including the shaft — which
        // is the pair the report was actually about.
        Assert.Equal(CanvasCursorKind.Rotate, CanvasCursor.ForBone(BoneGrab.Body, posing: true));
        Assert.Equal(CanvasCursorKind.Rotate, CanvasCursor.ForBone(BoneGrab.Origin, posing: true));

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

        // The same shaft, once posing.
        vm.PosingMode = true;
        vm.UpdatePointerContext(150, 150, Avalonia.Input.KeyModifiers.None, scale: 1);
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
}
