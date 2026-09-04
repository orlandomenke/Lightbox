using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// With several characters on a drawing, one of them is in hand — Q182 step 2.
/// </summary>
/// <remarks>
/// <para>
/// The rig in hand is <b>view state</b>, for the reason the layer index is:
/// which character you happen to be working on is not a fact about the
/// drawing, and saving it would put you in somebody else's rig on opening
/// their file.
/// </para>
/// <para>
/// The two rules with teeth are here. An edit targets the rig it was made
/// against even if the artist has since switched — otherwise undo edits the
/// wrong character. And a press on another character <em>takes it in hand and
/// stops</em>, because a mannequin stands on the drawing you are making and a
/// stray drag there is expensive.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TheRigYouAreEditingTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static Armature Rig(string name, string boneId, double y) => new()
    {
        Name = name,
        Bones = [new Bone { Id = boneId, Name = $"{name} spine", X = 100, Y = y, Length = 80 }],
    };

    private static MainViewModel TwoCharacters(out Armature knight, out Armature dog)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        knight = Rig("Knight", "knight-spine", 100);
        dog = Rig("Dog", "dog-spine", 400);
        vm.Doc.Armatures = [knight, dog];
        vm.ArmatureEditMode = true;
        return vm;
    }

    [AvaloniaFact]
    public void TheFirstRigIsInHandUntilAnotherIsChosen()
    {
        var vm = TwoCharacters(out var knight, out var dog);

        Assert.Equal(knight.Id, vm.EditingRig?.Id);
        Assert.True(vm.HasManyRigs);
        Assert.Equal(["Knight", "Dog"], vm.RigRows.Select(r => r.Name));
        Assert.True(vm.RigRows[0].Editing);

        vm.EditRig(dog.Id);

        Assert.Equal(dog.Id, vm.EditingRig?.Id);
        Assert.True(vm.RigRows[1].Editing);
        Assert.False(vm.RigRows[0].Editing);
    }

    [AvaloniaFact]
    public void SwitchingRigsDropsABoneSelectionThatIsNotInTheNewOne()
    {
        var vm = TwoCharacters(out _, out var dog);
        vm.SelectedBoneId = "knight-spine";

        vm.EditRig(dog.Id);

        // Otherwise rename and delete would offer a bone from the character
        // you just left.
        Assert.Null(vm.SelectedBoneId);
    }

    [AvaloniaFact]
    public void TheOtherCharactersAreDrawnAndMarkedAsNotInHand()
    {
        var vm = TwoCharacters(out _, out _);

        var chrome = vm.BoneChromes;

        Assert.Equal(2, chrome.Count);
        var inHand = Assert.Single(chrome, c => c.Editing);
        var other = Assert.Single(chrome, c => !c.Editing);
        Assert.Equal("knight-spine", inHand.Id);
        Assert.Equal("dog-spine", other.Id);
        // Under the rig in hand, so the one being edited draws over it.
        Assert.Equal("dog-spine", chrome[0].Id);
        output.WriteLine($"{chrome.Count} bones drawn, {chrome.Count(c => !c.Editing)} dimmed");
    }

    [AvaloniaFact]
    public void ADrawingWithOneRigOffersNoPickerAndDimsNothing()
    {
        var vm = VmLayers.PaperVm();
        vm.Doc.Armature = Rig("Knight", "knight-spine", 100);
        vm.ArmatureEditMode = true;

        Assert.False(vm.HasManyRigs);
        Assert.All(vm.BoneChromes, c => Assert.True(c.Editing));
    }

    /// <summary>
    /// The other characters animate. That is the whole reason the reference
    /// had to stop being a ghost: a mannequin is only useful standing where
    /// the frame wants it.
    /// </summary>
    [AvaloniaFact]
    public void TheOtherCharactersArePosedAtThePlayheadRatherThanLeftAtRest()
    {
        var vm = TwoCharacters(out _, out _);
        vm.Doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey
                {
                    Frame = 0,
                    Bones = new Dictionary<string, BonePose> { ["dog-spine"] = new() { X = 250 } },
                },
            ],
        };
        vm.PosingMode = true;

        var dog = Assert.Single(vm.BoneChromes, c => c.Id == "dog-spine");

        Assert.Equal(350, dog.X0, 6);   // 100 at rest, 250 of pose on top
        Assert.False(dog.Editing);
    }

    [AvaloniaFact]
    public void PressingAnotherCharacterTakesItInHandAndGrabsNothing()
    {
        var vm = TwoCharacters(out _, out var dog);

        // Straight at the dog's origin, which is nowhere near the knight's.
        var hit = vm.PressArmature(100, 400, scale: 1);

        Assert.Equal("dog-spine", hit.Id);
        Assert.Equal(BoneGrab.None, hit.Grab);   // selected, not seized
        Assert.Equal(dog.Id, vm.EditingRig?.Id);
        Assert.Equal("dog-spine", vm.SelectedBoneId);
        output.WriteLine("one press to take a character in hand, a second to move it");
    }

    [AvaloniaFact]
    public void PressingTheCharacterAlreadyInHandGrabsItAsItAlwaysDid()
    {
        var vm = TwoCharacters(out var knight, out _);

        var hit = vm.PressArmature(100, 100, scale: 1);

        Assert.Equal("knight-spine", hit.Id);
        Assert.NotEqual(BoneGrab.None, hit.Grab);
        Assert.Equal(knight.Id, vm.EditingRig?.Id);
    }

    /// <summary>
    /// The rule that stops undo editing the wrong character: a step targets
    /// the rig it was made against, not whichever is in hand when it replays.
    /// </summary>
    [AvaloniaFact]
    public void AnEditUndoneAfterSwitchingCharactersStillEditsTheOneItWasMadeOn()
    {
        var vm = TwoCharacters(out var knight, out var dog);
        // Deliberately the SECOND rig: an edit aimed at "the document's first
        // rig" would land on the knight and this test would not notice.
        vm.EditRig(dog.Id);
        vm.SelectedBoneId = "dog-spine";

        vm.SelectedBoneLength = 200;
        Assert.Equal(200, dog.Bones[0].Length, 6);
        Assert.Equal(80, knight.Bones[0].Length, 6);

        // The artist moves on to the knight, then undoes and redoes. Undo
        // replaces the document, so the rigs are read back off it by name.
        vm.EditRig(knight.Id);
        vm.UndoCommand.Execute(null);
        Assert.Equal(80, vm.Doc.Rigs.Single(r => r.Name == "Dog").Bones[0].Length, 6);
        Assert.Equal(80, vm.Doc.Rigs.Single(r => r.Name == "Knight").Bones[0].Length, 6);

        vm.RedoCommand.Execute(null);
        Assert.Equal(200, vm.Doc.Rigs.Single(r => r.Name == "Dog").Bones[0].Length, 6);
        Assert.Equal(80, vm.Doc.Rigs.Single(r => r.Name == "Knight").Bones[0].Length, 6);
    }

    [AvaloniaFact]
    public void DrawingABoneAddsItToTheCharacterInHandRatherThanStartingANewOne()
    {
        var vm = TwoCharacters(out _, out var dog);
        vm.EditRig(dog.Id);

        vm.CreateBoneFromDrag(120, 420, 180, 420);

        Assert.Equal(2, vm.Doc.Rigs.Count);
        Assert.Equal(2, vm.Doc.Rigs.Single(r => r.Name == "Dog").Bones.Count);
        Assert.Single(vm.Doc.Rigs.Single(r => r.Name == "Knight").Bones);
    }
}
