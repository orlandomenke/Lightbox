using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The armature's editing surface: the rig mode, bone selection, the chrome
/// the overlay draws, and the edits a bone gesture lands — phase 2 of
/// <c>docs/DESIGN-bones.md</c>, under the Q81 decisions.
/// </summary>
/// <remarks>
/// <para>
/// The mode has two halves and one switch: <b>bind</b> edits the skeleton
/// (create bones, move joints, re-length) against the rest pose, <b>pose</b>
/// rotates bones and writes pose keys at the playhead — auto-key, Q81's
/// second decision. Every edit is one undo step per gesture, landed on
/// release, the way <c>DragRig</c> already works.
/// </para>
/// <para>
/// Pose and weight edits change pixels the frame cache cannot see in its
/// keys, so every one of them walks the bound frames and invalidates — the
/// cost record-driven rigged-ness accepted in Q81.
/// </para>
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Dragging on the canvas edits the armature instead of drawing. A mode
    /// rather than a modifier, for <c>RigEditMode</c>'s reason.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoneChromes))]
    [NotifyPropertyChangedFor(nameof(HeatPoints))]
    private bool _armatureEditMode;

    /// <summary>
    /// True poses the rig (dragging rotates, keys land at the playhead);
    /// false edits the skeleton against its rest pose.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoneChromes))]
    [NotifyPropertyChangedFor(nameof(HeatPoints))]
    private bool _posingMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoneChromes))]
    [NotifyPropertyChangedFor(nameof(HeatPoints))]
    private string? _selectedBoneId;

    /// <summary>Whether this document has a rig at all.</summary>
    public bool HasArmature => Doc.HasArmature;

    /// <summary>
    /// What the overlay draws: every bone, solved for the pose the canvas is
    /// showing — the playhead's pose when posing, the bind pose when binding.
    /// Empty when the mode is off, which keeps the chrome absent rather than
    /// merely invisible.
    /// </summary>
    public IReadOnlyList<BoneChrome> BoneChromes
    {
        get
        {
            if (!ArmatureEditMode || Doc.Armature is not { Bones.Count: > 0 } armature) return [];

            var pose = PosingMode
                ? ArmatureOps.PoseAt(Doc.Scene.PoseTrack, CurrentFrameIndex)
                : null;
            var placements = ArmatureOps.Solve(armature, pose);
            var chrome = new List<BoneChrome>(armature.Bones.Count);
            foreach (var bone in armature.Bones)
            {
                var p = placements[bone.Id];
                var (tx, ty) = p.Tip(bone.Length);
                chrome.Add(new BoneChrome(bone.Id, bone.Name, p.X, p.Y, tx, ty, bone.Id == SelectedBoneId));
            }
            return chrome;
        }
    }

    /// <summary>
    /// The heat view: the selected bone's influence at every control point of
    /// every stroke on the current drawing, at bind positions — weight
    /// painting works at rest (Q81). Empty when nothing is selected or the
    /// mode is off: no bone means no heat, not a cold canvas.
    /// </summary>
    public IReadOnlyList<HeatPoint> HeatPoints
    {
        get
        {
            if (!ArmatureEditMode || SelectedBoneId is not { } boneId) return [];
            if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return [];

            var points = new List<HeatPoint>();
            foreach (var stroke in frame.Strokes)
            {
                var binding = stroke.Weights?.FirstOrDefault(b => b.BoneId == boneId);
                for (var i = 0; i < stroke.Points.Count; i++)
                {
                    points.Add(new HeatPoint(
                        stroke.Points[i].X, stroke.Points[i].Y, binding?.WeightAt(i) ?? 0));
                }
            }
            return points;
        }
    }

    /// <summary>Select what a press hit, or clear the selection on empty canvas.</summary>
    public BoneHit PressArmature(double x, double y, double scale)
    {
        var hit = ArmatureOverlay.Hit(BoneChromes, x, y, scale);
        SelectedBoneId = hit.Id;
        return hit;
    }

    /// <summary>
    /// Create a bone from a drag, parented to the selected bone — the Bone
    /// tool's whole gesture. The first drag on an unrigged document creates
    /// the armature itself, which is how the record stays absent until asked
    /// for while the capability stays reachable.
    /// </summary>
    public void CreateBoneFromDrag(double x0, double y0, double x1, double y1)
    {
        var (length, worldAngle) = ArmatureOverlay.CreateFrom(x0, y0, x1, y1);
        var parentId = SelectedBoneId;
        var name = NextBoneName();
        var bone = new Bone { Name = name, Length = length };

        _editor.Perform(doc =>
        {
            var armature = doc.Armature ??= new Armature();
            var parent = parentId is null ? null : armature.BoneById(parentId);
            if (parent is null)
            {
                bone.X = x0;
                bone.Y = y0;
                bone.RotationDeg = worldAngle;
            }
            else
            {
                // Into the parent's frame, so the record stays hierarchical:
                // the press point relative to the parent's origin, unrotated.
                var placements = ArmatureOps.Solve(armature);
                var p = placements[parent.Id];
                var rad = -p.RotationDeg * Math.PI / 180.0;
                var (dx, dy) = (x0 - p.X, y0 - p.Y);
                bone.ParentId = parent.Id;
                bone.X = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
                bone.Y = Math.Sin(rad) * dx + Math.Cos(rad) * dy;
                bone.RotationDeg = worldAngle - p.RotationDeg;
            }
            armature.Bones.Add(bone);
        });

        SelectedBoneId = bone.Id;
        OnPropertyChanged(nameof(HasArmature));
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Land a bind-mode drag: the origin grab moves the joint, the tip grab
    /// re-aims and re-lengths. Called on release with the final position, one
    /// undo step per gesture.
    /// </summary>
    public void DragBoneBind(string id, BoneGrab grab, double x, double y)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(id) is not { } bone) return;

        var placements = ArmatureOps.Solve(armature);
        var parentRot = bone.ParentId is not null && placements.TryGetValue(bone.ParentId, out var pp)
            ? pp.RotationDeg
            : 0.0;
        var parentPos = bone.ParentId is not null && placements.TryGetValue(bone.ParentId, out var pq)
            ? (pq.X, pq.Y)
            : (0.0, 0.0);
        var own = placements[bone.Id];

        _editor.Perform(doc =>
        {
            var target = doc.Armature?.BoneById(id);
            if (target is null) return;
            if (grab == BoneGrab.Tip)
            {
                var world = ArmatureOverlay.AngleFrom(own.X, own.Y, x, y);
                target.RotationDeg = world - parentRot;
                target.Length = Math.Max(
                    ArmatureOverlay.MinimumLength, Dist(own.X, own.Y, x, y));
            }
            else
            {
                // The origin lands where the pointer is, expressed in the
                // parent's frame — a root's frame is the document's.
                var rad = -parentRot * Math.PI / 180.0;
                var (dx, dy) = (x - parentPos.Item1, y - parentPos.Item2);
                target.X = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
                target.Y = Math.Sin(rad) * dx + Math.Cos(rad) * dy;
            }
        });
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Land a pose-mode drag: rotate the bone so it points at the pointer,
    /// and write the key at the playhead — auto-key, Q81. One undo step per
    /// gesture, called on release.
    /// </summary>
    public void PoseBoneTo(string id, double x, double y)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(id) is null) return;

        var frame = CurrentFrameIndex;
        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, frame);
        var placements = ArmatureOps.Solve(armature, pose);
        var own = placements[id];
        var currentDelta = pose.GetValueOrDefault(id)?.RotationDeg ?? 0;
        var newDelta = currentDelta + ArmatureOverlay.AngleFrom(own.X, own.Y, x, y) - own.RotationDeg;

        _editor.Perform(doc =>
        {
            var track = doc.Scene.PoseTrack ??= new PoseTrack();
            var key = ArmatureOps.KeyAt(track, frame);
            if (key is null)
            {
                key = new PoseKey { Frame = frame };
                // The new key starts from what the artist SAW at this frame —
                // the interpolated pose — so keying one bone cannot snap its
                // neighbours back to rest.
                foreach (var (boneId, p) in ArmatureOps.PoseAt(doc.Scene.PoseTrack, frame))
                    key.Bones[boneId] = p;
                track.Keys.Add(key);
            }
            var existing = key.Bones.TryGetValue(id, out var b) ? b : key.Bones[id] = new BonePose();
            existing.RotationDeg = newDelta;
        });
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// The weight brush is armed: Bone-tool presses paint influence for the
    /// selected bone instead of working bones. The arithmetic is
    /// <see cref="WeightPaint"/>; this owns the gesture and its single undo
    /// step.
    /// </summary>
    [ObservableProperty]
    private bool _weightPainting;

    /// <summary>Brush radius in document pixels.</summary>
    [ObservableProperty]
    private double _weightBrushRadius = 24;

    [ObservableProperty]
    private WeightBrushMode _weightBrushMode = WeightBrushMode.Add;

    /// <summary>
    /// Paint both sides of a name-paired bone at once (hip.l ↔ hip.r),
    /// mirrored across the pair's own axis — Q81.
    /// </summary>
    [ObservableProperty]
    private bool _mirrorWeights = true;

    private List<(Stroke Stroke, List<BoneBinding>? Before)>? _weightGesture;
    private string? _weightGestureFrameId;

    /// <summary>Arm a weight stroke: remember what every stroke's binding was.</summary>
    public void BeginWeightStroke(double x, double y, double pressure)
    {
        _weightGesture = null;
        if (SelectedBoneId is null || Doc.Armature is null) return;
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return;

        _weightGestureFrameId = frame.Id;
        _weightGesture = frame.Strokes
            .Select(s => (s, s.Weights?.Select(b => b.Clone()).ToList()))
            .ToList();
        WeightDab(x, y, pressure);
    }

    /// <summary>
    /// One dab of the weight brush, live on the record for immediate heat
    /// feedback — the undo step is landed whole at <see cref="EndWeightStroke"/>.
    /// Pressure drives strength through the same hand that drives every brush.
    /// </summary>
    public void WeightDab(double x, double y, double pressure)
    {
        if (_weightGesture is not { } gesture) return;
        if (SelectedBoneId is not { } boneId || Doc.Armature is not { } armature) return;

        // Per-dab rate below 1 so a held brush builds rather than slams —
        // the same reason flow exists on a paint brush.
        var strength = Math.Clamp(pressure, 0.05, 1) * 0.35;
        var changed = false;
        foreach (var (stroke, _) in gesture)
            changed |= WeightPaint.Apply(stroke, boneId, x, y, WeightBrushRadius, strength, WeightBrushMode);

        if (MirrorWeights
            && WeightPaint.MirroredBone(armature, boneId) is { } pair
            && armature.BoneById(boneId) is { } own
            && WeightPaint.Mirror(armature, own, pair, x, y) is { } m)
        {
            foreach (var (stroke, _) in gesture)
                changed |= WeightPaint.Apply(stroke, pair.Id, m.X, m.Y, WeightBrushRadius, strength, WeightBrushMode);
        }

        if (changed) OnPropertyChanged(nameof(HeatPoints));
    }

    /// <summary>Land the whole stroke as one undo step, or nothing if nothing moved.</summary>
    public void EndWeightStroke()
    {
        if (_weightGesture is not { } gesture) return;
        _weightGesture = null;

        var steps = gesture
            .Select(g => (g.Stroke, g.Before, After: g.Stroke.Weights?.Select(b => b.Clone()).ToList()))
            .Where(g => !SameWeights(g.Before, g.After))
            .ToList();
        if (steps.Count == 0) return;

        // The record already holds the after-state — the dabs painted it live —
        // so apply is idempotent by construction and PerformDelta's immediate
        // apply(Doc) is a no-op that records the step.
        _editor.PerformDelta(
            apply: _ =>
            {
                foreach (var (stroke, _, after) in steps)
                    stroke.Weights = after?.Select(b => b.Clone()).ToList();
            },
            revert: _ =>
            {
                foreach (var (stroke, before, _) in steps)
                    stroke.Weights = before?.Select(b => b.Clone()).ToList();
            },
            affectedFrameId: _weightGestureFrameId,
            label: "Paint weights");
        InvalidateRiggedFrames();
    }

    private static bool SameWeights(List<BoneBinding>? a, List<BoneBinding>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].BoneId != b[i].BoneId) return false;
            var (wa, wb) = (a[i].PointWeights, b[i].PointWeights);
            if (wa is null || wb is null) { if (wa != wb) return false; continue; }
            if (!wa.SequenceEqual(wb)) return false;
        }
        return true;
    }

    /// <summary>Bind every selected stroke wholly to the selected bone — the cutout gesture.</summary>
    public int AssignSelectedStrokesToBone()
    {
        if (SelectedBoneId is not { } boneId) return 0;
        return EditSelectedStrokes(stroke => Skinning.AssignAll(stroke, boneId));
    }

    /// <summary>Auto-weight every selected stroke against the whole armature.</summary>
    public int AutoBindSelectedStrokes()
    {
        if (Doc.Armature is not { Bones.Count: > 0 } armature) return 0;
        return EditSelectedStrokes(stroke => Skinning.AutoBind(stroke, armature));
    }

    /// <summary>
    /// Bake the playhead's pose into the current drawing: bound strokes become
    /// ordinary ones, posed. The freeze, not a render mode.
    /// </summary>
    public int BakePoseHere()
    {
        if (Doc.Armature is not { Bones.Count: > 0 }) return 0;
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return 0;

        var index = CurrentFrameIndex;
        var baked = 0;
        _editor.Perform(doc =>
        {
            if (doc.Armature is not { } armature) return;
            var target = ExposureSheet.ExposedFrame(
                doc.Scene.Layers[ActiveLayerIndex], index);
            if (target is null) return;
            baked = Skinning.BakeFrame(target, armature, ArmatureOps.PoseAt(doc.Scene.PoseTrack, index));
        });
        if (baked > 0)
        {
            InvalidateFrameRender(frame.Id);
            AiStatus = baked == 1 ? "Baked the pose into 1 line." : $"Baked the pose into {baked} lines.";
        }
        return baked;
    }

    /// <summary>
    /// A pose or weight edit changes pixels the frame cache cannot key on, so
    /// every frame holding bound strokes is re-rendered on next sight.
    /// </summary>
    private void InvalidateRiggedFrames()
    {
        foreach (var layer in Doc.Scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { HasBoundStrokes: true } bound)
                    InvalidateFrameRender(bound.Id);
        PublishSnapshot();
        OnPropertyChanged(nameof(BoneChromes));
        OnPropertyChanged(nameof(HeatPoints));
    }

    /// <summary>Apply one edit to every selected stroke on the current drawing, as one undo step.</summary>
    private int EditSelectedStrokes(Action<Stroke> edit)
    {
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return 0;
        var ids = _selectionManager.SelectedStrokeIds;
        var targets = frame.Strokes.Where(s => ids.Contains(s.Id)).ToList();
        if (targets.Count == 0) return 0;

        _editor.Perform(doc =>
        {
            foreach (var layer in doc.Scene.Layers)
                foreach (var cel in layer.Cels)
                    if (cel.Frame is { } f && f.Id == frame.Id)
                        foreach (var stroke in f.Strokes)
                            if (ids.Contains(stroke.Id)) edit(stroke);
        });
        InvalidateRiggedFrames();
        return targets.Count;
    }

    private string NextBoneName()
    {
        var count = Doc.Armature?.Bones.Count ?? 0;
        return $"bone.{count + 1}";
    }

    private static double Dist(double x0, double y0, double x1, double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
