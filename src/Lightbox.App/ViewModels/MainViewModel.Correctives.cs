using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Authoring a corrective: bake the pose, let the artist fix the drawing with
/// the tools they already have, diff what they did — phase 4 of
/// <c>docs/DESIGN-bones.md</c>, under the Q100 decisions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bake, edit, diff</b> is the whole trick, and it is why this feature
/// needs no new canvas machinery. Entering capture replaces the drawing with
/// its posed self, so the pen, the transform tool and point editing all work
/// on the shape the artist is looking at. Capture compares that against a
/// fresh pose of the original, converts the difference into rest space and
/// stores it. The alternative — teaching the canvas to edit a posed preview of
/// a record at rest — is a much larger change to the pen and transform paths
/// for exactly the same result.
/// </para>
/// <para>
/// <b>The whole session is one undo step</b>, because the record is untouched
/// until capture: entering and leaving without capturing writes nothing at
/// all, and capture is a single <c>Perform</c>.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>The drawing as recorded, put back when the capture ends.</summary>
    private List<Stroke>? _correctiveRest;

    /// <summary>What the pose alone produced, so the diff sees only the artist's edit.</summary>
    private Dictionary<string, List<StrokePoint>>? _correctiveBaseline;

    private string? _correctiveFrameId;
    private string? _correctiveDriverId;
    private double _correctiveAngle;

    /// <summary>Whether a corrective is being drawn right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCaptureCorrective))]
    private bool _capturingCorrective;

    /// <summary>Whether "Draw a fix" would do anything.</summary>
    public bool CanBeginCorrective =>
        !CapturingCorrective
        && PosingMode
        && SelectedBoneId is not null
        && Doc.Armature is { Bones.Count: > 0 }
        && ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not null;

    /// <summary>Whether there is a capture in progress to keep.</summary>
    public bool CanCaptureCorrective => CapturingCorrective;

    /// <summary>What the panel says while a capture is open.</summary>
    public string CorrectiveStatus =>
        !CapturingCorrective
            ? ""
            : $"drawing the fix for {DriverName} at {_correctiveAngle:0}°";

    private string DriverName =>
        _correctiveDriverId is { } id ? Doc.Armature?.BoneById(id)?.Name ?? id : "";

    /// <summary>The correctives on the drawing at the playhead, for the panel to list.</summary>
    public IReadOnlyList<BoneRow> CorrectiveRows
    {
        get
        {
            if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { Correctives: { } all }) return [];
            return all
                .Select(c => new BoneRow(
                    c.Id,
                    $"{Doc.Armature?.BoneById(c.DriverBoneId)?.Name ?? c.DriverBoneId}"
                    + $" · {string.Join("°, ", c.Stops.OrderBy(s => s.AngleDeg).Select(s => s.AngleDeg.ToString("0")))}°",
                    0,
                    false))
                .ToList();
        }
    }

    /// <summary>Whether this drawing has any fixes at all.</summary>
    public bool HasCorrectives => CorrectiveRows.Count > 0;

    /// <summary>
    /// Start drawing a fix: bake the pose into the drawing so every existing
    /// tool works on the shape that is wrong.
    /// </summary>
    /// <remarks>
    /// The baked strokes carry their originals in <see cref="Stroke.RestPoints"/>
    /// and no weights, which is exactly what a posed stroke looks like
    /// everywhere else — so nothing poses them a second time while the artist
    /// is working on them.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanBeginCorrective))]
    private void BeginCorrective()
    {
        if (!CanBeginCorrective) return;
        if (Doc.Armature is not { } armature) return;
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return;
        if (SelectedBoneId is not { } driver) return;

        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex);
        var layerBone = Doc.Scene.RiggedBoneOf(ActiveLayer);
        var named = layerBone is { Length: > 0 }
            ? new List<BoneBinding> { new() { BoneId = layerBone } }
            : null;

        _correctiveRest = frame.Strokes.Select(s => s.Clone(newId: false)).ToList();
        _correctiveBaseline = [];
        _correctiveFrameId = frame.Id;
        _correctiveDriverId = driver;
        _correctiveAngle = pose.GetValueOrDefault(driver)?.RotationDeg ?? 0;

        var existing = CorrectiveOps.Resolve(frame.Correctives, pose);
        foreach (var stroke in frame.Strokes)
        {
            // Posed WITH the fixes already in force, so a second stop is drawn
            // on top of the first rather than against the uncorrected shape.
            var posed = Skinning.PoseControlPoints(
                stroke, armature, pose, named, existing.GetValueOrDefault(stroke.Id));
            _correctiveBaseline[stroke.Id] = posed;
        }

        var frameId = frame.Id;
        _editor.Perform(doc =>
        {
            if (FrameById(doc, frameId) is not { } target) return;
            foreach (var stroke in target.Strokes)
            {
                if (_correctiveBaseline.GetValueOrDefault(stroke.Id) is not { } posed) continue;
                stroke.RestPoints = [.. stroke.Points];
                stroke.Points = [.. posed];
                stroke.Weights = null;
                stroke.Path = null;
            }
        });

        CapturingCorrective = true;
        NotifyCorrectiveSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Keep what was drawn as a stop on this joint's corrective.</summary>
    [RelayCommand(CanExecute = nameof(CanCaptureCorrective))]
    private void CaptureCorrective()
    {
        if (!CapturingCorrective || _correctiveRest is null || _correctiveBaseline is null) return;
        if (Doc.Armature is not { } armature || _correctiveDriverId is not { } driver) return;
        if (_correctiveFrameId is not { } frameId) return;
        if (FrameById(Doc, frameId) is not { } edited) return;

        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex);
        var layerBone = Doc.Scene.RiggedBoneOf(ActiveLayer);
        var named = layerBone is { Length: > 0 }
            ? new List<BoneBinding> { new() { BoneId = layerBone } }
            : null;

        var corrections = new List<StrokeCorrection>();
        foreach (var rest in _correctiveRest)
        {
            var now = edited.Strokes.FirstOrDefault(s => s.Id == rest.Id);
            // A line the artist deleted, added or re-pointed during the
            // capture cannot be diffed against what it was. Skipped rather
            // than guessed at — a corrective built from a mismatched line
            // moves the wrong part of it, silently, forever after.
            if (now is null || now.Points.Count != rest.Points.Count) continue;
            if (_correctiveBaseline.GetValueOrDefault(rest.Id) is not { } baseline) continue;
            if (!Moved(baseline, now.Points)) continue;

            var offsets = Skinning.RestOffsetsFor(rest, armature, pose, now.Points, named);
            corrections.Add(new StrokeCorrection { StrokeId = rest.Id, Offsets = [.. offsets] });
        }

        var angle = _correctiveAngle;
        var restored = _correctiveRest;
        _editor.Perform(doc =>
        {
            if (FrameById(doc, frameId) is not { } target) return;
            target.Strokes = restored.Select(s => s.Clone(newId: false)).ToList();
            if (corrections.Count == 0) return;

            var fix = target.Correctives?.FirstOrDefault(c => c.DriverBoneId == driver);
            if (fix is null)
            {
                fix = new Corrective { Name = DriverName + " fix", DriverBoneId = driver };
                (target.Correctives ??= []).Add(fix);
            }
            // Re-drawing at an angle already authored replaces that stop
            // rather than stacking a second one at the same place, which would
            // make the ramp depend on list order.
            fix.Stops.RemoveAll(s => Math.Abs(s.AngleDeg - angle) < 1e-6);
            fix.Stops.Add(new CorrectiveStop { AngleDeg = angle, Strokes = corrections });
        });

        AiStatus = corrections.Count == 0
            ? "Nothing was moved, so no fix was kept."
            : $"Kept a fix for {DriverName} at {angle:0}°.";
        EndCapture();
    }

    /// <summary>Put the drawing back and keep nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanCaptureCorrective))]
    private void CancelCorrective()
    {
        if (!CapturingCorrective || _correctiveRest is null || _correctiveFrameId is not { } frameId) return;
        var restored = _correctiveRest;
        _editor.Perform(doc =>
        {
            if (FrameById(doc, frameId) is { } target)
                target.Strokes = restored.Select(s => s.Clone(newId: false)).ToList();
        });
        EndCapture();
    }

    /// <summary>Take a fix off this drawing.</summary>
    public void RemoveCorrective(string correctiveId)
    {
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return;
        var frameId = frame.Id;
        _editor.Perform(doc =>
        {
            if (FrameById(doc, frameId) is not { } target) return;
            target.Correctives?.RemoveAll(c => c.Id == correctiveId);
            // Absent, not empty.
            if (target.Correctives is { Count: 0 }) target.Correctives = null;
        });
        NotifyCorrectiveSurface();
        InvalidateRiggedFrames();
    }

    private void EndCapture()
    {
        _correctiveRest = null;
        _correctiveBaseline = null;
        _correctiveFrameId = null;
        _correctiveDriverId = null;
        CapturingCorrective = false;
        NotifyCorrectiveSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Whether the artist actually moved anything on this line.</summary>
    private static bool Moved(IReadOnlyList<StrokePoint> baseline, IReadOnlyList<StrokePoint> now)
    {
        if (baseline.Count != now.Count) return false;
        for (var i = 0; i < now.Count; i++)
            if (Math.Abs(now[i].X - baseline[i].X) > 1e-9 || Math.Abs(now[i].Y - baseline[i].Y) > 1e-9)
                return true;
        return false;
    }

    private static Frame? FrameById(Doc doc, string id)
    {
        foreach (var layer in doc.Scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { } frame && frame.Id == id)
                    return frame;
        return null;
    }

    /// <summary>Re-read everything the corrective surface derives from the document.</summary>
    internal void NotifyCorrectiveSurface()
    {
        OnPropertyChanged(nameof(CanBeginCorrective));
        OnPropertyChanged(nameof(CanCaptureCorrective));
        OnPropertyChanged(nameof(CorrectiveStatus));
        OnPropertyChanged(nameof(CorrectiveRows));
        OnPropertyChanged(nameof(HasCorrectives));
        BeginCorrectiveCommand.NotifyCanExecuteChanged();
        CaptureCorrectiveCommand.NotifyCanExecuteChanged();
        CancelCorrectiveCommand.NotifyCanExecuteChanged();
    }
}
