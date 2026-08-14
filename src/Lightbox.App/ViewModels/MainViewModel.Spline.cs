using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Spline chains' editing surface: laying a run of bones along a curve, and
/// taking it back — the last slice of phase 3.
/// </summary>
/// <remarks>
/// <b>One button makes a working tail</b>, for the reason "Add IK" does: the
/// handles are created along the run the artist already built, spaced across
/// it, so the next thing they do — switch to Pose and drag a handle — is the
/// thing a spline chain is for. Handing over an empty chain and a list to
/// fill in would be the same feature that nobody finds.
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>The spline chain the selected bone belongs to or steers, or null.</summary>
    public SplineChain? SelectedBoneSpline =>
        SelectedBoneId is { } id ? SplineTouching(id) : null;

    /// <summary>Whether the selected bone has anything to do with a spline chain.</summary>
    public bool HasSelectedSpline => SelectedBoneSpline is not null;

    /// <summary>Whether "Add spline" would do anything.</summary>
    public bool CanAddSpline => SelectedBoneId is not null && SelectedBoneSpline is null;

    /// <summary>How many bones up from the tip lie along the curve.</summary>
    public int SelectedSplineBoneCount
    {
        get => SelectedBoneSpline?.Bones ?? 3;
        set
        {
            if (SelectedBoneSpline is not { } spline) return;
            var wanted = Math.Clamp(value, 1, 32);
            if (spline.Bones == wanted) return;
            EditSpline(spline.Id, s => s.Bones = wanted);
        }
    }

    /// <summary>How many handles the curve is drawn through.</summary>
    public int SelectedSplineHandleCount => SelectedBoneSpline?.HandleBoneIds.Count ?? 0;

    /// <summary>
    /// Give the selected bone a spline chain: handles laid along the run it
    /// already forms, and the last one selected so it can be dragged.
    /// </summary>
    /// <remarks>
    /// Three handles, because two is a straight line and three is the fewest
    /// that can curve — the point of the feature. They are <em>roots</em> for
    /// the reason an IK target is: a handle carried by the bones it steers is
    /// a circle the solve refuses.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanAddSpline))]
    private void AddSplineChain()
    {
        if (SelectedBoneId is not { } tipId || Doc.Armature is not { } armature) return;
        if (armature.BoneById(tipId) is not { } tip || SplineTouching(tipId) is not null) return;

        var run = RunUp(armature, tipId, 3);
        if (run.Count == 0) return;

        var placements = ArmatureOps.Solve(armature);
        // Along the run as it stands: the curve starts as the shape the artist
        // drew, so adding a spline changes nothing until a handle is moved.
        var points = new List<(double X, double Y)> { (placements[run[0].Id].X, placements[run[0].Id].Y) };
        var total = run.Sum(b => b.Length);
        var walked = 0.0;
        foreach (var bone in run)
        {
            walked += bone.Length;
            var at = placements[bone.Id].Tip(bone.Length);
            if (walked >= total / 2 && points.Count == 1) points.Add(at);
            else if (bone.Id == run[^1].Id) points.Add(at);
        }
        while (points.Count < 3) points.Add((points[^1].X + 40, points[^1].Y));

        var handles = points.Select((p, i) => new Bone
        {
            Name = $"{tip.Name}.spline{i + 1}",
            X = p.X,
            Y = p.Y,
            Length = 12,
        }).ToList();
        var spline = new SplineChain
        {
            Name = tip.Name + " spline",
            TipBoneId = tipId,
            Bones = run.Count,
            HandleBoneIds = handles.Select(h => h.Id).ToList(),
        };
        _editor.Perform(doc =>
        {
            if (doc.Armature is not { } rig) return;
            rig.Bones.AddRange(handles);
            (rig.Splines ??= []).Add(spline);
        });

        SelectedBoneId = handles[^1].Id;
        NotifyArmatureSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Take the chain away, and the handles it created with it.</summary>
    [RelayCommand]
    private void RemoveSplineChain()
    {
        if (SelectedBoneSpline is not { } spline) return;
        var splineId = spline.Id;
        var handleIds = spline.HandleBoneIds.ToList();
        var tipId = spline.TipBoneId;
        var wasOnAHandle = SelectedBoneId is { } sel && handleIds.Contains(sel);

        _editor.Perform(doc =>
        {
            if (doc.Armature is not { } rig) return;
            rig.Splines?.RemoveAll(s => s.Id == splineId);
            if (rig.Splines is { Count: 0 }) rig.Splines = null;
            foreach (var id in handleIds)
            {
                // Kept if anything else leans on it — another spline, a chain,
                // a constraint, or a bone somebody parented under it.
                if (rig.Splines?.Any(s => s.HandleBoneIds.Contains(id)) == true) continue;
                if (rig.Chains?.Any(c => c.TargetBoneId == id || c.PoleBoneId == id) == true) continue;
                if (rig.Constraints?.Any(c => c.BoneId == id || c.TargetBoneId == id) == true) continue;
                if (rig.Bones.Any(b => b.ParentId == id)) continue;
                rig.Bones.RemoveAll(b => b.Id == id);
                if (doc.Scene.PoseTrack is { } track)
                    foreach (var key in track.Keys) key.Bones.Remove(id);
            }
        });

        if (wasOnAHandle) SelectedBoneId = tipId;
        NotifyArmatureSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>The spline chain that lays this bone out, or is steered by it.</summary>
    private SplineChain? SplineTouching(string boneId)
    {
        if (Doc.Armature is not { Splines: { Count: > 0 } splines } armature) return null;
        foreach (var spline in splines)
        {
            if (spline.HandleBoneIds.Contains(boneId)) return spline;
            if (RunUp(armature, spline.TipBoneId, spline.Bones).Any(b => b.Id == boneId)) return spline;
        }
        return null;
    }

    /// <summary>
    /// The handle a drag on a curve-laid bone should move: the closest one to
    /// the pointer.
    /// </summary>
    /// <remarks>
    /// Dragging the middle of a tail is a legitimate thing to try, and it must
    /// not be a dead gesture — the bone's own rotation belongs to the solver.
    /// The nearest handle is the one the artist was reaching for; a fixed
    /// choice (always the last) would send a drag near the base flying off the
    /// far end.
    /// </remarks>
    private string NearestHandle(SplineChain spline, double x, double y)
    {
        if (Doc.Armature is not { } armature) return spline.HandleBoneIds[^1];
        var placements = ArmatureOps.Solve(armature, ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex));
        var best = spline.HandleBoneIds[^1];
        var bestDistance = double.MaxValue;
        foreach (var id in spline.HandleBoneIds)
        {
            if (!placements.TryGetValue(id, out var at)) continue;
            var d = (at.X - x) * (at.X - x) + (at.Y - y) * (at.Y - y);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = id;
        }
        return best;
    }

    /// <summary>A run of bones ending at a tip, root-most first.</summary>
    private static List<Bone> RunUp(Armature armature, string tipId, int count)
    {
        var walk = armature.BoneById(tipId);
        var bones = new List<Bone>();
        var seen = new HashSet<string>();
        while (walk is not null && bones.Count < Math.Max(1, count) && seen.Add(walk.Id))
        {
            bones.Add(walk);
            walk = walk.ParentId is { } p ? armature.BoneById(p) : null;
        }
        bones.Reverse();
        return bones;
    }

    private void EditSpline(string splineId, Action<SplineChain> edit)
    {
        _editor.Perform(doc =>
        {
            if (doc.Armature?.Splines?.FirstOrDefault(s => s.Id == splineId) is { } spline) edit(spline);
        });
        NotifySplineSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>Re-read everything the spline surface derives from the document.</summary>
    private void NotifySplineSurface()
    {
        OnPropertyChanged(nameof(SelectedBoneSpline));
        OnPropertyChanged(nameof(HasSelectedSpline));
        OnPropertyChanged(nameof(CanAddSpline));
        OnPropertyChanged(nameof(SelectedSplineBoneCount));
        OnPropertyChanged(nameof(SelectedSplineHandleCount));
        OnPropertyChanged(nameof(BoneChromes));
        AddSplineChainCommand.NotifyCanExecuteChanged();
    }
}
