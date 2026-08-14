using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The Bone tool's decisions and chrome: hit order, gesture arithmetic, the
/// view-model edits a gesture lands, and the pixels the painter puts up —
/// none of it needing a window, which is the point of the overlay pattern.
/// </summary>
public class ArmatureToolTests
{
    private static List<BoneChrome> TwoBones(string? selected = null) =>
    [
        new BoneChrome("a", "upper", 100, 100, 180, 100, selected == "a"),
        new BoneChrome("b", "fore", 180, 100, 240, 100, selected == "b"),
    ];

    [Fact]
    public void ATipBeatsTheBodyAndAChildsOriginBeatsTheParentsShaft()
    {
        // Bone b's origin sits exactly on bone a's tip; the tip grab wins for
        // whichever is selected, and tips beat bodies outright.
        var hit = ArmatureOverlay.Hit(TwoBones("a"), 180, 100, scale: 1);
        Assert.Equal("a", hit.Id);
        Assert.Equal(BoneGrab.Tip, hit.Grab);

        var hitB = ArmatureOverlay.Hit(TwoBones("b"), 180, 100, scale: 1);
        Assert.Equal("b", hitB.Id);

        var body = ArmatureOverlay.Hit(TwoBones(), 140, 102, scale: 1);
        Assert.Equal("a", body.Id);
        Assert.Equal(BoneGrab.Body, body.Grab);
    }

    [Fact]
    public void HandlesAreScreenSizedSoZoomDoesNotChangeTheHand()
    {
        // 10 document px from the tip: a miss at 100%, a hit at 25%.
        Assert.False(ArmatureOverlay.Hit(TwoBones(), 250, 100, scale: 1).Any);
        Assert.True(ArmatureOverlay.Hit(TwoBones(), 250, 100, scale: 0.25).Any);
    }

    [AvaloniaFact]
    public void TheBoneToolsFirstDragCreatesTheArmature()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        Assert.Null(vm.Doc.Armature);

        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 180, 100);

        var armature = vm.Doc.Armature;
        Assert.NotNull(armature);
        var bone = Assert.Single(armature!.Bones);
        Assert.Equal(100, bone.X);
        Assert.Equal(100, bone.Y);
        Assert.Equal(80, bone.Length, 9);
        Assert.Null(bone.ParentId);
        Assert.Equal(bone.Id, vm.SelectedBoneId);

        // A second drag parents to the selection, in the parent's own frame.
        vm.CreateBoneFromDrag(180, 100, 180, 160);
        Assert.Equal(2, armature.Bones.Count);
        Assert.Equal(bone.Id, armature.Bones[1].ParentId);
        Assert.Equal(80, armature.Bones[1].X, 9);
        Assert.Equal(90, armature.Bones[1].RotationDeg, 9);
    }

    [AvaloniaFact]
    public void APoseDragWritesAKeyAtThePlayheadAndUndoTakesItBack()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 180, 100);
        var boneId = vm.SelectedBoneId!;
        vm.PosingMode = true;

        // Point the bone straight down from its origin: a 90° pose.
        vm.PoseBoneTo(boneId, 100, 200);

        var track = vm.Doc.Scene.PoseTrack;
        Assert.NotNull(track);
        var key = Assert.Single(track!.Keys);
        Assert.Equal(vm.CurrentFrameIndex, key.Frame);
        Assert.Equal(90, key.Bones[boneId].RotationDeg, 6);

        vm.UndoCommand.Execute(null);
        Assert.True(vm.Doc.Scene.PoseTrack is null or { Keys.Count: 0 });
    }

    [AvaloniaFact]
    public void KeyingOneBoneKeepsTheInterpolatedPoseOfItsNeighbours()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 180, 100);
        var upper = vm.SelectedBoneId!;
        vm.CreateBoneFromDrag(180, 100, 240, 100);
        var fore = vm.SelectedBoneId!;
        vm.PosingMode = true;

        vm.PoseBoneTo(upper, 100, 200);   // upper at 90°
        vm.PoseBoneTo(fore, 0, 100);      // then the forearm elsewhere

        // Keying the forearm must not have snapped the upper arm back.
        var key = Assert.Single(vm.Doc.Scene.PoseTrack!.Keys);
        Assert.Equal(90, key.Bones[upper].RotationDeg, 6);
    }

    [Fact]
    public void ThePainterPutsBonesAndHeatOnTheCanvas()
    {
        var info = new SKImageInfo(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);

        ArmatureOverlayPainter.Paint(canvas, TwoBones("a"), scale: 1);
        ArmatureOverlayPainter.PaintHeat(
            canvas, [new HeatPoint(50, 150, 0), new HeatPoint(70, 150, 1)], scale: 1);
        canvas.Flush();

        // The selected bone reads white-ish along its shaft, the other green.
        // The kite's fill is deliberately translucent (alpha 0x50), so white
        // over black reads ~80 — present is the claim, not opaque.
        Assert.True(bmp.GetPixel(140, 100).Red > 60, $"selected bone missing: {bmp.GetPixel(140, 100)}");
        var other = bmp.GetPixel(210, 100);
        Assert.True(other.Green > other.Red, $"unselected bone not green: {other}");
        // Heat: cold point blue, hot point red.
        var cold = bmp.GetPixel(50, 150);
        var hot = bmp.GetPixel(70, 150);
        Assert.True(cold.Blue > cold.Red, $"cold heat not blue: {cold}");
        Assert.True(hot.Red > hot.Blue, $"hot heat not red: {hot}");

        // And nothing at all is drawn for an empty list — absent, not invisible.
        using var empty = new SKBitmap(info);
        using var emptyCanvas = new SKCanvas(empty);
        emptyCanvas.Clear(SKColors.Black);
        ArmatureOverlayPainter.Paint(emptyCanvas, null, 1);
        ArmatureOverlayPainter.PaintHeat(emptyCanvas, [], 1);
        emptyCanvas.Flush();
        Assert.Equal(SKColors.Black, empty.GetPixel(140, 100));
    }

    [AvaloniaFact]
    public void AWeightStrokeIsOneUndoStepAndMirrorsAcrossTheNamedPair()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var left = vm.SelectedBoneId!;
        vm.SelectedBoneId = null;
        vm.CreateBoneFromDrag(300, 150, 360, 150);
        var right = vm.SelectedBoneId!;
        vm.Doc.Armature!.BoneById(left)!.Name = "hip.l";
        vm.Doc.Armature!.BoneById(right)!.Name = "hip.r";

        var frame = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        var stroke = new Stroke
        {
            Points =
            [
                new StrokePoint(110, 150, 1),   // near hip.l
                // Exactly the mirror of the painted point across the pair's
                // axis (x = 200, the bones' midline) — where the mirrored dab
                // must land in full.
                new StrokePoint(290, 150, 1),
            ],
        };
        frame.Strokes.Add(stroke);

        vm.SelectedBoneId = left;
        vm.WeightPainting = true;
        vm.BeginWeightStroke(110, 150, pressure: 1);
        for (var i = 0; i < 12; i++) vm.WeightDab(110, 150, pressure: 1);
        vm.EndWeightStroke();

        // The painted side got the selected bone, the mirrored side its pair —
        // the axis is the pair's own midline (x = 230), not the canvas's.
        var painted = stroke.Weights!.First(b => b.BoneId == left);
        var mirrored = stroke.Weights!.First(b => b.BoneId == right);
        Assert.True(painted.WeightAt(0) > 0.9, $"painted side {painted.WeightAt(0):F3}");
        Assert.True(mirrored.WeightAt(1) > 0.9, $"mirrored side {mirrored.WeightAt(1):F3}");

        // One undo step takes the whole stroke back.
        vm.UndoCommand.Execute(null);
        Assert.Null(stroke.Weights);
    }

    [AvaloniaFact]
    public void CoarseAssignmentBindsTheSelectedStrokesToTheSelectedBone()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 300, 200, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 180, 100);

        // Draw a stroke into the current frame the blunt way.
        var frame = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex);
        Assert.NotNull(frame);
        var stroke = new Stroke { Points = [new StrokePoint(120, 100, 1), new StrokePoint(160, 100, 1)] };
        frame!.Strokes.Add(stroke);
        vm.Selection.SelectStroke(stroke.Id);

        Assert.Equal(1, vm.AssignSelectedStrokesToBone());
        var binding = Assert.Single(frame.Strokes[0].Weights!);
        Assert.Equal(vm.SelectedBoneId, binding.BoneId);
        Assert.Null(binding.PointWeights);
    }
}
