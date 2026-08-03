using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Placing anchors and collision shapes on the canvas.
/// </summary>
/// <remarks>
/// The overlay's arithmetic is tested in <see cref="RigOverlayTests"/> without a
/// window. What these guard is the half that touches the document: that a drag lands
/// on the <em>drawing</em> rather than on a frame index, that it is one undo step, and
/// that the mode is absent rather than merely inert when it is off.
/// </remarks>
public class RigEditingTests
{
    private static MainViewModel OnOnes(int frames = 4)
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", 200, 160, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < frames) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.RigEditMode = true;
        return vm;
    }

    private static Layer Anim(MainViewModel vm) =>
        vm.Doc.Scene.Layers.First(l => !l.IsBackground);

    // ---- absence ------------------------------------------------------------------

    [AvaloniaFact]
    public void WithTheModeOffThereIsNothingToDraw()
    {
        // Absent, not invisible: the canvas is handed an empty list, so it draws no
        // overlay at all rather than an overlay of nothing.
        var vm = OnOnes();
        vm.AddAnchorAt("hand", 50, 50);
        Assert.Single(vm.RigMarks);

        vm.RigEditMode = false;

        Assert.Empty(vm.RigMarks);
        // And the selection goes with it, so re-entering does not resume a gesture from
        // a session the artist has forgotten.
        Assert.Null(vm.SelectedRigMarkId);
    }

    [AvaloniaFact]
    public void ADocumentWithNoRigHasNothingDeclared()
    {
        var vm = OnOnes();
        Assert.False(vm.HasRig);
        Assert.Empty(vm.RigMarks);

        vm.AddShapeAt("body", 10, 10, 40, 60);
        Assert.True(vm.HasRig);
    }

    // ---- adding -------------------------------------------------------------------

    [AvaloniaFact]
    public void AddingDeclaresAndPlacesInOneStep()
    {
        // Two steps would leave a named anchor that exists nowhere, which is a row in a
        // list an artist cannot see on the canvas.
        var vm = OnOnes();

        var id = vm.AddAnchorAt("leftHand", 80, 40);

        Assert.NotEqual("", id);
        var mark = Assert.Single(vm.RigMarks);
        Assert.Equal(RigMarkKind.Anchor, mark.Kind);
        Assert.Equal("leftHand", mark.Label);
        Assert.Equal(80, mark.X);
        Assert.Equal(40, mark.Y);
        // Declared on the scene and placed on the drawing — both halves.
        Assert.Single(vm.Doc.Scene.Anchors!);
        Assert.NotNull(Anchors.At(Anim(vm).Cels[0].Frame!, id));
        // And selected, because you just made it.
        Assert.Equal(id, vm.SelectedRigMarkId);
    }

    [AvaloniaFact]
    public void AShapeDraggedOutBackwardsArrivesTheRightWayRound()
    {
        // Dragging up-and-left is how half of people draw a rectangle. A negative width
        // is not a shape an engine can read.
        var vm = OnOnes();

        vm.AddShapeAt("body", 100, 100, -40, -60);

        var mark = Assert.Single(vm.RigMarks);
        Assert.Equal(60, mark.X);
        Assert.Equal(40, mark.Y);
        Assert.Equal(40, mark.W);
        Assert.Equal(60, mark.H);
    }

    // ---- the property the whole design rests on -----------------------------------

    [AvaloniaFact]
    public void ADragLandsOnTheDrawingRatherThanOnTheFrameIndex()
    {
        // The reason the record is shaped this way: a re-time moves drawings around the
        // sheet, and an index-keyed edit would point at the wrong one afterwards.
        var vm = OnOnes();
        var layer = Anim(vm);
        var third = layer.Cels[2].Frame!;

        vm.CurrentFrameIndex = 2;
        var id = vm.AddAnchorAt("hand", 60, 60);
        vm.DragRig(id, RigCorner.None, 10, -5);

        Assert.Equal(new AnchorPoint(70, 55), Anchors.At(third, id));

        // Re-time, and the anchor is still on its own drawing wherever that landed.
        ExposureSheet.ApplyTiming(layer, 0, 4, new TimingPreset("On 2s", [2]));
        var moved = layer.Cels.First(c => ReferenceEquals(c.Frame, third));
        Assert.Equal(new AnchorPoint(70, 55), Anchors.At(moved.Frame!, id));
    }

    [AvaloniaFact]
    public void DraggingWhileParkedOnAHoldEditsTheDrawingBeingHeld()
    {
        // The alternative would silently create a drawing out of a hold, which changes
        // the animation to move a socket.
        var vm = OnOnes();
        var layer = Anim(vm);
        var first = layer.Cels[0].Frame!;
        layer.Cels[1].Frame = null;   // frame 1 holds frame 0

        vm.CurrentFrameIndex = 0;
        var id = vm.AddAnchorAt("hand", 30, 30);
        vm.CurrentFrameIndex = 1;
        vm.DragRig(id, RigCorner.None, 5, 5);

        Assert.Equal(new AnchorPoint(35, 35), Anchors.At(first, id));
        Assert.Null(layer.Cels[1].Frame);   // still a hold
    }

    [AvaloniaFact]
    public void ResizingAShapeMovesOnlyTheCornerThatWasGrabbed()
    {
        var vm = OnOnes();
        var id = vm.AddShapeAt("body", 20, 20, 40, 40);

        vm.DragRig(id, RigCorner.TopLeft, 10, 10);

        var mark = Assert.Single(vm.RigMarks);
        Assert.Equal(30, mark.X);
        Assert.Equal(30, mark.Y);
        Assert.Equal(60, mark.Right);     // unmoved
        Assert.Equal(60, mark.Bottom);    // unmoved
    }

    // ---- selection ----------------------------------------------------------------

    [AvaloniaFact]
    public void PressingSelectsWhatItHitAndClearsOnEmptyCanvas()
    {
        var vm = OnOnes();
        var id = vm.AddShapeAt("body", 20, 20, 40, 40);
        vm.SelectedRigMarkId = null;

        var hit = vm.PressRig(30, 30, scale: 1);
        Assert.Equal(id, hit.Id);
        Assert.Equal(id, vm.SelectedRigMarkId);

        var miss = vm.PressRig(180, 150, scale: 1);
        Assert.False(miss.Any);
        Assert.Null(vm.SelectedRigMarkId);
    }

    // ---- undo, and what a status change must not do -------------------------------

    [AvaloniaFact]
    public void EveryRigEditIsOneUndoStep()
    {
        var vm = OnOnes();
        var id = vm.AddShapeAt("body", 20, 20, 40, 40);

        vm.DragRig(id, RigCorner.None, 15, 0);
        Assert.Equal(35, Assert.Single(vm.RigMarks).X);

        vm.UndoCommand.Execute(null);
        Assert.Equal(20, Assert.Single(vm.RigMarks).X);

        // And one more undo takes the declaration away, so adding was a step too.
        vm.UndoCommand.Execute(null);
        Assert.False(vm.HasRig);
    }

    [AvaloniaFact]
    public void ADragOfNothingRecordsNothing()
    {
        // A press that does not move must not fill the undo stack with steps that undo
        // nothing, which is how an undo stack stops being usable.
        var vm = OnOnes();
        var id = vm.AddShapeAt("body", 20, 20, 40, 40);

        vm.DragRig(id, RigCorner.None, 0, 0);
        vm.UndoCommand.Execute(null);

        Assert.False(vm.HasRig);   // the add was the only step
    }

    // ---- clearing and deleting are different intentions ---------------------------

    [AvaloniaFact]
    public void ClearingHereLeavesItDeclaredSoAHitboxCanStopPartwayThroughASwing()
    {
        // Absence is the off state, so this is the whole mechanism for a hitbox that is
        // only active on the contact frames.
        var vm = OnOnes(6);
        vm.CurrentFrameIndex = 0;
        var id = vm.AddShapeAt("sword", 40, 40, 30, 10, ShapeRole.Hitbox);
        vm.PushRigAcross(0, 6);
        Assert.Equal(6, vm.Doc.Scene.Layers.First(l => !l.IsBackground).Cels
            .Count(c => c.Frame is { } f && CollisionShapes.At(f, id) is not null));

        vm.CurrentFrameIndex = 3;
        vm.ClearRigHere();

        Assert.True(vm.HasRig);            // still declared
        Assert.Empty(vm.RigMarks);         // but not on this frame
        vm.CurrentFrameIndex = 2;
        Assert.Single(vm.RigMarks);        // and still on the others
    }

    [AvaloniaFact]
    public void DeletingTakesTheDeclarationAndEveryPlacementOfIt()
    {
        var vm = OnOnes();
        var id = vm.AddAnchorAt("hand", 40, 40);
        vm.PushRigAcross(0, 4);

        vm.DeleteSelectedRigMark();

        Assert.False(vm.HasRig);
        Assert.Null(vm.Doc.Scene.Anchors);
        Assert.All(Anim(vm).Cels, c => Assert.Null(c.Frame?.Anchors));
        Assert.Null(vm.SelectedRigMarkId);
    }

    // ---- the bridge to the multi-frame operations ---------------------------------

    [AvaloniaFact]
    public void PushingAcrossVisitsEveryDrawingOnceEvenOn2s()
    {
        // Place a physics body once and push it across the cycle, rather than dragging
        // it twenty-four times. On 2s each drawing is held twice and must be counted
        // once.
        var vm = OnOnes(4);
        var layer = Anim(vm);
        layer.Cels[1].Frame = null;
        layer.Cels[3].Frame = null;

        vm.CurrentFrameIndex = 0;
        vm.AddShapeAt("body", 10, 10, 20, 20, ShapeRole.Physics);

        var changed = vm.PushRigAcross(0, 4);

        Assert.Equal(2, changed);
    }

    [AvaloniaFact]
    public void PushingWithNothingSelectedDoesNothing()
    {
        var vm = OnOnes();
        vm.AddAnchorAt("hand", 10, 10);
        vm.SelectedRigMarkId = null;

        Assert.Equal(0, vm.PushRigAcross(0, 4));
    }
}
