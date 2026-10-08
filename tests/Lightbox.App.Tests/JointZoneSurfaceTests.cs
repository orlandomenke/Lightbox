using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Joint weighting (Q217) from the artist's side: binding a layer to the
/// whole skeleton marks it as a new bind, and each bone's joint zone is a
/// field with a default, an undo, and a reset.
/// </summary>
public class JointZoneSurfaceTests
{
    private static MainViewModel Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        return vm;
    }

    private static Layer Active(MainViewModel vm) => vm.Doc.Scene.Layers[vm.ActiveLayerIndex];

    [AvaloniaFact]
    public void BindingALayerToTheWholeSkeletonNowWeighsByJoint()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);

        vm.RigLayerToSkeletonCommand.Execute(null);
        Assert.Equal("", Active(vm).BoneId);
        Assert.True(Active(vm).JointWeights);
        Assert.True(vm.Doc.Scene.UsesJointWeights(Active(vm)));

        // One bone is one rigid map: no joint, no marker.
        vm.RigLayerToSelectedBoneCommand.Execute(null);
        Assert.Null(Active(vm).JointWeights);

        vm.UnrigLayerCommand.Execute(null);
        Assert.Null(Active(vm).BoneId);
        Assert.Null(Active(vm).JointWeights);
    }

    [AvaloniaFact]
    public void ALayerBoundBeforeIsUpgradedOnlyByBindingItAgain()
    {
        // Q217 answer 2: a layer bound before keeps the old weighting until
        // the artist binds it again — re-choosing "the whole skeleton" is
        // what moves it to joints, never opening the file.
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);
        Active(vm).BoneId = "";   // as a file from before Q217 holds it
        Assert.False(vm.Doc.Scene.UsesJointWeights(Active(vm)));

        vm.RigLayerToSkeletonCommand.Execute(null);
        Assert.True(Active(vm).JointWeights);
        vm.UndoCommand.Execute(null);
        Assert.Null(Active(vm).JointWeights);
        Assert.Equal("", Active(vm).BoneId);
    }

    [AvaloniaFact]
    public void TheJointZoneShowsItsDefaultAndCanBeSetAndReset()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 100, 200, 100);       // 100 long
        var parent = vm.SelectedBoneId!;
        Assert.False(vm.SelectedBoneHasJoint);            // a root has no joint
        vm.ExtrudeChildFrom(parent, 260, 100);            // 60 long
        var child = vm.SelectedBoneId!;

        Assert.True(vm.SelectedBoneHasJoint);
        Assert.Equal(15, vm.SelectedBoneJointZone, 6);    // a quarter of the shorter, 60
        Assert.False(vm.SelectedBoneJointZoneIsSet);

        vm.SelectedBoneJointZone = 40;
        Assert.Equal(40, vm.Doc.Armature!.BoneById(child)!.JointZone);
        Assert.True(vm.SelectedBoneJointZoneIsSet);
        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Doc.Armature!.BoneById(child)!.JointZone);

        vm.SelectedBoneJointZone = 0;                     // a hinge
        Assert.Equal(0, vm.Doc.Armature!.BoneById(child)!.JointZone);
        vm.ResetSelectedBoneJointZoneCommand.Execute(null);
        Assert.Null(vm.Doc.Armature!.BoneById(child)!.JointZone);
        Assert.Equal(15, vm.SelectedBoneJointZone, 6);
    }
}
