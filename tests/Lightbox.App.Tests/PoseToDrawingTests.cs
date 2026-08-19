using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
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
        var original = Anim(vm).Cels[0].Frame!;
        Assert.NotNull(made);
        Assert.NotEqual(original.Id, made!.Id);
        // One frame becomes a drawing, not the run — stated as what is SHOWN
        // rather than as which cels are holds, which is the distinction B259
        // turned on. The cel before is still a hold of the original; the cel
        // after re-exposes it, so the picture on every later frame is the one
        // that was there before.
        Assert.Null(Anim(vm).Cels[2].Frame);
        Assert.Same(original, ExposureSheet.ExposedFrame(Anim(vm), 2));
        Assert.Same(original, ExposureSheet.ExposedFrame(Anim(vm), 4));
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
    public void TheSheetsRightClickAimsAtTheCelThatWasClicked()
    {
        // Not "move the playhead and run the other one": the pose baked in has
        // to be the pose at the frame under the cursor. The playhead is parked
        // somewhere else entirely to prove it.
        var vm = Rigged();
        var stroke = AddStroke(vm, (110, 150), (150, 150));
        var bone = OneBone(vm);
        vm.PosingMode = false;
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();
        vm.PosingMode = true;
        HoldEverythingAfterTheFirst(vm);

        vm.CurrentFrameIndex = 4;
        vm.PoseBoneTo(bone, 100, 250);       // straight down on frame 4
        vm.CurrentFrameIndex = 0;            // and the artist walks away from it

        var layerIndex = vm.Doc.Scene.Layers.IndexOf(Anim(vm));
        Assert.True(vm.InsertDrawingFromPoseAt(new FrameCell(4) { LayerIndex = layerIndex }));

        var made = Anim(vm).Cels[4].Frame!.Strokes.Single();
        Assert.Equal(100, made.Points[0].X, 4);
        Assert.Equal(160, made.Points[0].Y, 4);
        // Frame 0's own drawing is untouched — the click did not act here.
        Assert.Equal(110, Anim(vm).Cels[0].Frame!.Strokes.Single().Points[0].X, 4);
        // And the playhead follows the work, as every other insert on that menu does.
        Assert.Equal(4, vm.CurrentFrameIndex);
        Assert.Equal(layerIndex, vm.ActiveLayerIndex);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "Lightbox.sln")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }
        return dir;
    }

    [AvaloniaFact]
    public void TheXsheetMenuOffersIt_AndOnlyWhenThereIsARig()
    {
        // The owner asked for it on the sheet because that is where an artist
        // working a cycle is looking. Absent rather than disabled without a
        // rig, the camera's rule — so the binding is part of what is guarded.
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot(), "src/Lightbox.App/Views/MainWindow.axaml"));
        var item = Regex.Match(
            xaml,
            @"<MenuItem Header=""Drawing from pose"" Click=""OnInsertDrawingFromPose""(.*?)/>",
            RegexOptions.Singleline);
        Assert.True(item.Success, "the X-sheet menu has no Drawing from pose entry");
        Assert.Contains("HasArmature", item.Groups[1].Value);
    }

    [AvaloniaFact]
    public void ItIsInTheShortcutRegistry_SoItCanBeBoundAndFound()
    {
        // A command reachable only from a button and a menu cannot be searched
        // or rebound, which is the failure the registry exists for — and this
        // is a command pressed once per drawing across a cycle.
        var entry = new ShortcutMap().Definitions
            .SingleOrDefault(d => d.Id == "armature.insertPoseDrawing");
        Assert.NotNull(entry);
        Assert.Equal("Canvas", entry!.Category);
    }

    [AvaloniaFact]
    public void TheFramesAfterItGoOnHoldingWhatTheyHeld()
    {
        // Reported 2026-08-18: posed over four frames, kept the pose on frame
        // two, and frames three and four stopped following the skeleton. The
        // exposure sheet is why — a hold shows the nearest drawing BEFORE it,
        // so inserting one at frame two silently re-pointed three and four at
        // the new drawing, which is baked and therefore answers to no rig.
        var vm = Rigged();
        var stroke = AddStroke(vm, (110, 150), (150, 150));
        var bone = OneBone(vm);
        vm.PosingMode = false;
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();
        vm.PosingMode = true;
        HoldEverythingAfterTheFirst(vm);

        var original = Anim(vm).Cels[0].Frame!;
        vm.CurrentFrameIndex = 1;
        vm.PoseBoneTo(bone, 100, 250);
        vm.InsertDrawingFromPose();

        // Frames 2 and 3 must still show the drawing they were showing, which
        // is the one that still follows the rig.
        for (var i = 2; i < 5; i++)
        {
            Assert.Same(original, ExposureSheet.ExposedFrame(Anim(vm), i));
        }
        // And they are still posed by the rig rather than frozen.
        Assert.True(original.HasBoundStrokes);
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
