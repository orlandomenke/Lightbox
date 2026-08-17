using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Weight painting on the posed drawing — Q101. The weights stay rest-pose
/// facts (Q81); what moved is where the brush finds them: heat dots, the
/// dab's hit-test and the bone chrome all follow the playhead's pose, so the
/// armpit being fixed is the armpit on screen rather than a ghost at rest.
/// </summary>
public class LivePoseWeightTests
{
    private static MainViewModel Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        return vm;
    }

    private static Stroke AddStroke(MainViewModel vm, params (double X, double Y)[] points)
    {
        var frame = ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        var stroke = new Stroke { Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 1))] };
        frame.Strokes.Add(stroke);
        return stroke;
    }

    [AvaloniaFact]
    public void TheHeatDotsSitOnThePosedDrawing()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        var stroke = AddStroke(vm, (110, 150), (150, 150));
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();

        // Point the bone straight down: the stroke's points swing with it.
        vm.PosingMode = true;
        vm.PoseBoneTo(bone, 100, 250);
        vm.WeightPainting = true;

        var heat = vm.HeatPoints;
        Assert.Equal(2, heat.Count);
        // (110,150) and (150,150) rotate 90° about the joint at (100,150).
        Assert.Equal(100, heat[0].X, 6);
        Assert.Equal(160, heat[0].Y, 6);
        Assert.Equal(100, heat[1].X, 6);
        Assert.Equal(200, heat[1].Y, 6);
        // The values are unchanged rest-pose facts.
        Assert.Equal(1, heat[0].Weight, 6);
    }

    [AvaloniaFact]
    public void ADabOnThePosedDrawingPaintsThePointTheArtistIsLookingAt()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var carrier = vm.SelectedBoneId!;
        vm.SelectedBoneId = null;
        vm.CreateBoneFromDrag(300, 60, 340, 60);   // the bone being painted
        var painted = vm.SelectedBoneId!;

        var stroke = AddStroke(vm, (110, 150), (150, 150));
        vm.Selection.SelectStroke(stroke.Id);
        vm.SelectedBoneId = carrier;
        vm.AssignSelectedStrokesToBone();

        vm.PosingMode = true;
        vm.PoseBoneTo(carrier, 100, 250);          // stroke now stands at x=100, y=160..200
        vm.WeightPainting = true;
        vm.SelectedBoneId = painted;
        vm.MirrorWeights = false;

        // The artist dabs where they SEE the second point: (100, 200). At
        // rest that point is at (150, 150), 70px from this dab — a rest-space
        // brush would paint nothing, which is what live-pose painting fixes.
        vm.BeginWeightStroke(100, 200, 1);
        vm.EndWeightStroke();

        var binding = stroke.Weights!.FirstOrDefault(b => b.BoneId == painted);
        Assert.NotNull(binding);
        Assert.True(binding!.WeightAt(1) > 0.1,
            $"the dab missed the posed point: {binding.WeightAt(1)}");
        // The first point stands 40px up the limb, outside the default radius.
        Assert.Equal(0, binding.WeightAt(0), 6);
    }

    [AvaloniaFact]
    public void TheMirroredDabFollowsThePairsOwnPose()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(60, 150, 80, 150);
        var leftBone = vm.SelectedBoneId!;
        vm.RenameSelectedBone("hip.l");
        vm.SelectedBoneId = null;
        vm.CreateBoneFromDrag(140, 150, 160, 150);
        vm.RenameSelectedBone("hip.r");

        var leftStroke = AddStroke(vm, (65, 150));
        var rightStroke = AddStroke(vm, (135, 150));
        vm.Selection.SelectStroke(leftStroke.Id);
        vm.SelectedBoneId = leftBone;
        vm.AssignSelectedStrokesToBone();
        vm.Selection.SelectStroke(rightStroke.Id);
        vm.SelectedBoneId = vm.Doc.Armature!.Bones.Single(b => b.Name == "hip.r").Id;
        vm.AssignSelectedStrokesToBone();

        // Pose only the LEFT hip down; the right stays at rest. A rest-space
        // mirror of the dab would still land on the right limb here, but the
        // dab itself is made on the left limb's POSED position — so without
        // the posed round trip the left half of the pair is what gets missed.
        vm.PosingMode = true;
        vm.PoseBoneTo(leftBone, 60, 250);
        vm.WeightPainting = true;
        vm.SelectedBoneId = leftBone;
        vm.MirrorWeights = true;
        vm.WeightBrushMode = WeightBrushMode.Subtract;

        // (65,150) rotated 90° about (60,150) stands at (60,155): dab there.
        vm.BeginWeightStroke(60, 155, 1);
        vm.EndWeightStroke();

        // Both sides took the subtraction: the left at its posed point, the
        // right at the mirrored rest point its identity pose leaves in place.
        Assert.True(leftStroke.Weights![0].WeightAt(0) < 1,
            $"the dab missed the posed left limb: {leftStroke.Weights![0].WeightAt(0)}");
        Assert.True(rightStroke.Weights![0].WeightAt(0) < 1,
            $"the mirror missed the right limb: {rightStroke.Weights![0].WeightAt(0)}");
    }

    [AvaloniaFact]
    public void TheChromeStandsWhereTheDrawingStandsWhileWeightPainting()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        vm.PosingMode = true;
        vm.PoseBoneTo(bone, 100, 250);

        vm.WeightPainting = true;

        // The bone is 60 long and posed straight down, so its tip is drawn at
        // (100, 210) — where a click on it must also be aimed.
        var chrome = Assert.Single(vm.BoneChromes);
        Assert.Equal(100, chrome.X1, 6);
        Assert.Equal(210, chrome.Y1, 6);
    }

    /// <summary>
    /// B246. The window repaints the heat and chrome overlays only when the
    /// view model announces them, and arming the brush changes both — the heat
    /// gates on the flag, the chrome moves from the bind pose to the playhead's.
    /// Without the announcement the heat stayed absent until the first dab
    /// happened to refresh the overlay.
    /// </summary>
    [AvaloniaFact]
    public void ArmingTheWeightBrushAnnouncesTheOverlaysItChanges()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        AddStroke(vm, (110, 150), (150, 150));

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        vm.WeightPainting = true;

        Assert.Contains(nameof(MainViewModel.HeatPoints), fired);
        Assert.Contains(nameof(MainViewModel.BoneChromes), fired);
    }

    /// <summary>B246's other half: disarming must clear the heat, not strand it.</summary>
    [AvaloniaFact]
    public void DisarmingTheWeightBrushAnnouncesTheStaleHeat()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        AddStroke(vm, (110, 150), (150, 150));
        vm.WeightPainting = true;

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        vm.IsBindMode = true;

        Assert.Contains(nameof(MainViewModel.HeatPoints), fired);
        Assert.Empty(vm.HeatPoints);
    }

    /// <summary>
    /// B247. Painting influence toward another bone used to move the posed
    /// points it was painted on — the artist chased a target their own gesture
    /// pushed away. The session's geometry is frozen now: the dots recolor and
    /// stand still, and the new weights deform the drawing when the brush is
    /// disarmed, not under it.
    /// </summary>
    [AvaloniaFact]
    public void PaintingRecolorsTheHeatDotsWithoutMovingThem()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var carrier = vm.SelectedBoneId!;
        vm.SelectedBoneId = null;
        vm.CreateBoneFromDrag(300, 60, 340, 60);
        var painted = vm.SelectedBoneId!;

        var stroke = AddStroke(vm, (110, 150), (150, 150));
        vm.Selection.SelectStroke(stroke.Id);
        vm.SelectedBoneId = carrier;
        vm.AssignSelectedStrokesToBone();

        vm.PosingMode = true;
        vm.PoseBoneTo(carrier, 100, 250);          // points stand at (100,160), (100,200)
        vm.WeightPainting = true;
        vm.SelectedBoneId = painted;
        vm.MirrorWeights = false;

        var before = vm.HeatPoints.Select(p => (p.X, p.Y)).ToList();

        // Paint the unposed bone up on the second point: its weight climbs,
        // which used to pull the point toward that bone's rest transform.
        vm.BeginWeightStroke(100, 200, 1);
        vm.WeightDab(100, 200, 1);
        vm.WeightDab(100, 200, 1);

        var during = vm.HeatPoints;
        Assert.True(during[1].Weight > 0.3,
            $"the dab did not take: weight {during[1].Weight:F3}");
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].X, during[i].X, 6);
            Assert.Equal(before[i].Y, during[i].Y, 6);
        }

        vm.EndWeightStroke();
        var committed = vm.HeatPoints;
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].X, committed[i].X, 6);
            Assert.Equal(before[i].Y, committed[i].Y, 6);
        }
    }

    [AvaloniaFact]
    public void ScrubbingMovesTheRigSurface()
    {
        var vm = Rigged();
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        vm.WeightPainting = true;

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        vm.CurrentFrameIndex = 1;

        // The chrome, the heat and the correctives list all read the
        // playhead's pose; a scrub that does not move them leaves the whole
        // surface describing a frame nobody is looking at.
        Assert.Contains(nameof(MainViewModel.BoneChromes), fired);
        Assert.Contains(nameof(MainViewModel.HeatPoints), fired);
        Assert.Contains(nameof(MainViewModel.CorrectiveRows), fired);
    }
}
