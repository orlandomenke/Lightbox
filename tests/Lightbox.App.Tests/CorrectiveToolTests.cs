using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Drawing a joint fix: bake, edit with the ordinary tools, diff (Q100).
/// </summary>
/// <remarks>
/// The gesture is the whole feature here — the arithmetic is
/// <c>CorrectiveTests</c>' job. What these guard is that the artist gets the
/// posed shape to work on, that leaving without keeping writes nothing, and
/// that what is kept reproduces exactly what they drew.
/// </remarks>
public class CorrectiveToolTests
{
    private static (MainViewModel Vm, Frame Frame, Stroke Stroke) Limb()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 150, 100);
        var upper = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(upper, 200, 100);
        var fore = vm.SelectedBoneId!;

        var frame = ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers[vm.ActiveLayerIndex], vm.CurrentFrameIndex)!;
        var stroke = new Stroke { Points = [new StrokePoint(150, 100, 1), new StrokePoint(190, 100, 1)] };
        Skinning.AssignAll(stroke, fore);
        frame.Strokes.Add(stroke);

        vm.PosingMode = true;
        vm.SelectedBoneId = fore;
        vm.PoseBoneTo(fore, 150, 200);        // bend it down
        return (vm, frame, stroke);
    }

    [AvaloniaFact]
    public void BeginningACaptureGivesTheArtistThePosedShapeToWorkOn()
    {
        var (vm, frame, stroke) = Limb();
        var restX = stroke.Points[1].X;

        vm.BeginCorrectiveCommand.Execute(null);

        Assert.True(vm.CapturingCorrective);
        // The drawing IS the posed drawing now, so the pen, the transform tool
        // and point editing all work on the shape that is wrong.
        Assert.NotEqual(restX, frame.Strokes[0].Points[1].X, 3);
        // And it is marked as already posed, so nothing poses it a second time
        // while the artist works on it.
        Assert.Null(frame.Strokes[0].Weights);
        Assert.NotNull(frame.Strokes[0].RestPoints);
    }

    [AvaloniaFact]
    public void ItRefusesOutsidePosingBecauseThereWouldBeNoAngleToDrawAt()
    {
        var (vm, _, _) = Limb();
        vm.PosingMode = false;

        Assert.False(vm.CanBeginCorrective);
        vm.BeginCorrectiveCommand.Execute(null);
        Assert.False(vm.CapturingCorrective);
    }

    [AvaloniaFact]
    public void DiscardingPutsTheDrawingBackAndKeepsNothing()
    {
        var (vm, frame, stroke) = Limb();
        var before = stroke.Points.Select(p => (p.X, p.Y)).ToList();
        vm.BeginCorrectiveCommand.Execute(null);
        frame.Strokes[0].Points[1] = frame.Strokes[0].Points[1] with { X = 999 };

        vm.CancelCorrectiveCommand.Execute(null);

        Assert.False(vm.CapturingCorrective);
        Assert.Null(frame.Correctives);
        Assert.Equal(before, frame.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
    }

    [AvaloniaFact]
    public void WhatWasDrawnComesBackWhenTheJointReturnsToThatAngle()
    {
        var (vm, frame, _) = Limb();
        vm.BeginCorrectiveCommand.Execute(null);

        // The artist pulls the far end of the line out to fix the collapse.
        var posed = frame.Strokes[0].Points[1];
        var wanted = (X: posed.X + 12, Y: posed.Y - 5);
        frame.Strokes[0].Points[1] = posed with { X = wanted.X, Y = wanted.Y };

        vm.CaptureCorrectiveCommand.Execute(null);

        Assert.False(vm.CapturingCorrective);
        var fix = Assert.Single(frame.Correctives!);
        Assert.Single(fix.Stops);
        // The record went back to what the artist drew — the fix is a
        // departure, not a replacement.
        Assert.Equal(190, frame.Strokes[0].Points[1].X, 6);

        // And the posed render at that angle now lands where they dragged.
        var pose = ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex);
        var corrections = CorrectiveOps.Resolve(frame.Correctives, pose);
        var again = Skinning.PoseControlPoints(
            frame.Strokes[0], vm.Doc.Armature!, pose, null,
            corrections.GetValueOrDefault(frame.Strokes[0].Id));
        Assert.Equal(wanted.X, again[1].X, 4);
        Assert.Equal(wanted.Y, again[1].Y, 4);
    }

    [AvaloniaFact]
    public void MovingNothingKeepsNothingRatherThanStoringZeroes()
    {
        var (vm, frame, _) = Limb();
        vm.BeginCorrectiveCommand.Execute(null);

        vm.CaptureCorrectiveCommand.Execute(null);

        // A stop full of zeroes would flatten the ramp between the fixes
        // either side of it, silently.
        Assert.Null(frame.Correctives);
        Assert.Contains("Nothing was moved", vm.AiStatus);
    }

    [AvaloniaFact]
    public void DrawingAtTheSameAngleTwiceReplacesTheStop()
    {
        var (vm, frame, _) = Limb();

        for (var pass = 0; pass < 2; pass++)
        {
            vm.BeginCorrectiveCommand.Execute(null);
            var p = frame.Strokes[0].Points[1];
            frame.Strokes[0].Points[1] = p with { X = p.X + 10 * (pass + 1) };
            vm.CaptureCorrectiveCommand.Execute(null);
        }

        // Two stops at one angle would make the ramp depend on list order.
        var fix = Assert.Single(frame.Correctives!);
        Assert.Single(fix.Stops);
    }

    [AvaloniaFact]
    public void ASecondFixIsDrawnOnTopOfTheFirstRatherThanAgainstTheRawShape()
    {
        var (vm, frame, _) = Limb();
        var fore = vm.SelectedBoneId!;

        vm.BeginCorrectiveCommand.Execute(null);
        var p = frame.Strokes[0].Points[1];
        frame.Strokes[0].Points[1] = p with { X = p.X + 10 };
        vm.CaptureCorrectiveCommand.Execute(null);

        // Bend further and open a second capture: what the artist sees must
        // already include the first fix, or every stop would be drawn against
        // a shape they have not looked at since.
        vm.PoseBoneTo(fore, 120, 210);
        vm.BeginCorrectiveCommand.Execute(null);

        var pose = ArmatureOps.PoseAt(vm.Doc.Scene.PoseTrack, vm.CurrentFrameIndex);
        var withFix = CorrectiveOps.Resolve(frame.Correctives, pose);
        Assert.NotEmpty(withFix);
        vm.CancelCorrectiveCommand.Execute(null);
    }

    [AvaloniaFact]
    public void TheFixIsUndoableAndThePanelFollows()
    {
        var (vm, frame, _) = Limb();
        vm.BeginCorrectiveCommand.Execute(null);
        var p = frame.Strokes[0].Points[1];
        frame.Strokes[0].Points[1] = p with { X = p.X + 10 };
        vm.CaptureCorrectiveCommand.Execute(null);
        Assert.True(vm.HasCorrectives);

        vm.UndoCommand.Execute(null);

        Assert.False(vm.HasCorrectives);
        Assert.Empty(vm.CorrectiveRows);
    }

    [AvaloniaFact]
    public void LeavingPosingMidCaptureDiscardsRatherThanStrandingTheBakedDrawing()
    {
        var (vm, frame, stroke) = Limb();
        var before = stroke.Points.Select(p => (p.X, p.Y)).ToList();
        vm.BeginCorrectiveCommand.Execute(null);

        vm.PosingMode = false;

        // Otherwise the record keeps the posed drawing with no way back to it,
        // which is a lost drawing rather than a lost fix.
        Assert.False(vm.CapturingCorrective);
        Assert.Equal(before, frame.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
    }

    [AvaloniaFact]
    public void RemovingAFixLeavesTheDrawingAlone()
    {
        var (vm, frame, _) = Limb();
        vm.BeginCorrectiveCommand.Execute(null);
        var p = frame.Strokes[0].Points[1];
        frame.Strokes[0].Points[1] = p with { X = p.X + 10 };
        vm.CaptureCorrectiveCommand.Execute(null);
        var id = frame.Correctives![0].Id;

        vm.RemoveCorrective(id);

        // Absent, not empty.
        Assert.Null(frame.Correctives);
        Assert.Equal(190, frame.Strokes[0].Points[1].X, 6);
    }
}
