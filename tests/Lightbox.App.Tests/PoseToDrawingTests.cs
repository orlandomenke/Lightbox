using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Keeping a pose as a drawing — the bridge between live posing and drawing
/// frame by frame, and the owner's report of 2026-08-18: a run cycle posed
/// across fifteen frames played back as the two drawings that existed. Posing
/// still writes a pose key and nothing else; this is the one command that says
/// "and this one is a drawing".
/// </summary>
public class PoseToDrawingTests
{
    private static MainViewModel Rigged(int frames = 6)
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < frames) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.ArmatureEditMode = true;
        return vm;
    }

    private static Layer Anim(MainViewModel vm) => vm.Doc.Scene.Layers.First(l => !l.IsBackground);

    /// <summary>Frames 1..n hold frame 0's drawing, which is what an artist on a long exposure has.</summary>
    private static void HoldEverythingAfterTheFirst(MainViewModel vm)
    {
        var layer = Anim(vm);
        for (var i = 1; i < layer.Cels.Count; i++) layer.Cels[i].Frame = null;
    }

    private static Stroke AddStroke(MainViewModel vm, params (double X, double Y)[] points)
    {
        var frame = ExposureSheet.ExposedFrame(Anim(vm), vm.CurrentFrameIndex)!;
        var stroke = new Stroke { Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 1))] };
        frame.Strokes.Add(stroke);
        return stroke;
    }

    /// <summary>A rig with one bone at (100,150) reaching right, and the pose mode on.</summary>
    private static string OneBone(MainViewModel vm)
    {
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        vm.PosingMode = true;
        return bone;
    }

    [AvaloniaFact]
    public void KeepingAPoseAsADrawingBreaksTheHold()
    {
        var vm = Rigged();
        AddStroke(vm, (110, 150), (150, 150));
        var bone = OneBone(vm);
        HoldEverythingAfterTheFirst(vm);

        vm.CurrentFrameIndex = 3;
        vm.PoseBoneTo(bone, 100, 250);
        Assert.Null(Anim(vm).Cels[3].Frame);      // still a hold: posing alone authors no drawing

        Assert.True(vm.InsertDrawingFromPose());

        var made = Anim(vm).Cels[3].Frame;
        Assert.NotNull(made);
        Assert.NotEqual(Anim(vm).Cels[0].Frame!.Id, made!.Id);
        // The neighbours are untouched — one cel becomes a drawing, not the run.
        Assert.Null(Anim(vm).Cels[2].Frame);
        Assert.Null(Anim(vm).Cels[4].Frame);
    }

    [AvaloniaFact]
    public void ABoundDrawingArrivesPosed()
    {
        var vm = Rigged();
        var stroke = AddStroke(vm, (110, 150), (150, 150));
        var bone = OneBone(vm);
        vm.PosingMode = false;
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();
        vm.PosingMode = true;
        HoldEverythingAfterTheFirst(vm);

        vm.CurrentFrameIndex = 3;
        vm.PoseBoneTo(bone, 100, 250);          // the bone swings to point straight down
        vm.InsertDrawingFromPose();

        // The points are where the pose put them, in the record rather than
        // only in the render: (110,150) and (150,150) rotate 90° about (100,150).
        var made = Anim(vm).Cels[3].Frame!.Strokes.Single();
        Assert.Equal(100, made.Points[0].X, 4);
        Assert.Equal(160, made.Points[0].Y, 4);
        Assert.Equal(100, made.Points[1].X, 4);
        Assert.Equal(200, made.Points[1].Y, 4);
        // Baked means ordinary: the drawing no longer answers to the rig.
        Assert.False(Anim(vm).Cels[3].Frame!.HasBoundStrokes);
        // And the drawing it was holding is left exactly as it was drawn.
        var held = Anim(vm).Cels[0].Frame!.Strokes.Single();
        Assert.Equal(110, held.Points[0].X, 4);
        Assert.Equal(150, held.Points[0].Y, 4);
    }

    [AvaloniaFact]
    public void AGuideRigCopiesTheDrawingThroughToDrawOver()
    {
        // Nothing is bound: the bones are a construction guide under hand-drawn
        // art, which is how the owner was using them. The new drawing is the
        // held one, to redraw over with the posed skeleton showing through.
        var vm = Rigged();
        AddStroke(vm, (110, 150), (150, 150));
        var bone = OneBone(vm);
        HoldEverythingAfterTheFirst(vm);

        vm.CurrentFrameIndex = 3;
        vm.PoseBoneTo(bone, 100, 250);
        Assert.True(vm.InsertDrawingFromPose());

        var made = Anim(vm).Cels[3].Frame!.Strokes.Single();
        Assert.Equal(110, made.Points[0].X, 4);
        Assert.Equal(150, made.Points[0].Y, 4);
        // A copy, not the same object — editing it must not reach back through
        // the hold into the drawing every other cel is still showing.
        Assert.NotEqual(Anim(vm).Cels[0].Frame!.Strokes.Single().Id, made.Id);
    }

    [AvaloniaFact]
    public void ACelThatAlreadyHasADrawingIsNotDuplicated()
    {
        var vm = Rigged();
        AddStroke(vm, (110, 150), (150, 150));
        OneBone(vm);
        HoldEverythingAfterTheFirst(vm);

        vm.CurrentFrameIndex = 0;              // the drawing's own cel
        var before = Anim(vm).Cels[0].Frame;

        Assert.False(vm.InsertDrawingFromPose());

        Assert.Same(before, Anim(vm).Cels[0].Frame);
        Assert.Equal(6, Anim(vm).Cels.Count);
    }

    [AvaloniaFact]
    public void TheInsertAndTheBakeAreOneUndoStep()
    {
        var vm = Rigged();
        var stroke = AddStroke(vm, (110, 150), (150, 150));
        var bone = OneBone(vm);
        vm.PosingMode = false;
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();
        vm.PosingMode = true;
        HoldEverythingAfterTheFirst(vm);

        vm.CurrentFrameIndex = 3;
        vm.PoseBoneTo(bone, 100, 250);
        vm.InsertDrawingFromPose();
        Assert.NotNull(Anim(vm).Cels[3].Frame);

        vm.UndoCommand.Execute(null);

        // One press, one undo: the drawing and the bake go together, and the
        // pose key that was there before it is still there.
        Assert.Null(Anim(vm).Cels[3].Frame);
        Assert.NotNull(ArmatureOps.KeyAt(vm.Doc.Scene.PoseTrack, 3));
    }

    [AvaloniaFact]
    public void WithNoArmatureThereIsNothingToKeep()
    {
        var vm = Rigged();
        AddStroke(vm, (110, 150), (150, 150));
        HoldEverythingAfterTheFirst(vm);
        vm.CurrentFrameIndex = 3;

        Assert.False(vm.InsertDrawingFromPose());
        Assert.Null(Anim(vm).Cels[3].Frame);
    }
}
