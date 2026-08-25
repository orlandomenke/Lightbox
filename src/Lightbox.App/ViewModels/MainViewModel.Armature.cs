using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One bone as the options panel lists it: indented by depth, so the
/// hierarchy reads without a tree control.
/// </summary>
/// <remarks>
/// Top-level rather than nested inside the view model, and that is a
/// requirement rather than tidiness: a compiled <c>DataTemplate</c> has to
/// name its type with <c>x:DataType</c>, and a nested one cannot be written
/// in XAML. Nested, every binding in the row template resolved against
/// <c>MainViewModel</c> instead — which compiles, and renders a list of
/// blank rows.
/// </remarks>
public sealed record BoneRow(string Id, string Name, int Depth, bool Selected)
{
    /// <summary>The name with its indent, for a flat list that still shows parentage.</summary>
    public string Label => new string(' ', Depth * 4) + Name;
}

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
    [NotifyPropertyChangedFor(nameof(PointerIntent))]
    private bool _posingMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoneChromes))]
    [NotifyPropertyChangedFor(nameof(HeatPoints))]
    [NotifyPropertyChangedFor(nameof(BoneRows))]
    [NotifyPropertyChangedFor(nameof(SelectedBone))]
    [NotifyPropertyChangedFor(nameof(SelectedBoneName))]
    [NotifyPropertyChangedFor(nameof(SelectedBoneLength))]
    [NotifyPropertyChangedFor(nameof(HasSelectedBone))]
    [NotifyPropertyChangedFor(nameof(PointerIntent))]
    private string? _selectedBoneId;

    /// <summary>
    /// Neither posing nor painting weights: the skeleton itself is what a
    /// drag edits.
    /// </summary>
    /// <remarks>
    /// <b>The three are one switch, and that is a fix rather than a
    /// presentation choice.</b> They were two independent booleans, so
    /// "posing" and "painting weights" could both be on — a state with no
    /// meaning, because weights are painted against the rest pose (Q81) while
    /// the canvas showed a posed one. Making them exclusive removes the state
    /// and lets the panel show one control with three positions, which is the
    /// answer to not being able to find weight painting at all.
    /// </remarks>
    public bool IsBindMode
    {
        get => !PosingMode && !WeightPainting;
        set
        {
            if (!value) return;
            PosingMode = false;
            WeightPainting = false;
        }
    }

    /// <summary>
    /// Whether the skeleton on the canvas is standing at a pose rather than at
    /// its rest position — which is what decides its colour.
    /// </summary>
    /// <remarks>
    /// Posing and weight painting both solve the rig at the playhead (the
    /// latter since painting went live-pose), and bind mode shows the rest
    /// pose. So this is the same condition <see cref="BoneChromes"/> uses to
    /// decide whether to solve a pose at all, named once rather than spelled
    /// out in both places — the two must never disagree, or the bones would be
    /// coloured for a state they are not in.
    /// </remarks>
    public bool BonesShowAPose => PosingMode || WeightPainting;

    /// <summary>
    /// Show the rig while the animation plays, following the playhead, instead
    /// of hiding it.
    /// </summary>
    /// <remarks>
    /// Off, because playback is for watching the animation and a skeleton over
    /// it is furniture. On, because checking that a rigged limb reads through a
    /// movement is a real thing to want — and it is the artist who knows which
    /// of those they are doing.
    /// </remarks>
    public bool AnimateRigDuringPlayback
    {
        get => Settings.AnimateRigDuringPlayback;
        set
        {
            if (Settings.AnimateRigDuringPlayback == value) return;
            Settings.AnimateRigDuringPlayback = value;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoneChromes));
        }
    }

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
            if (!ArmatureEditMode) return [];
            // Playback shows the animation. A rig frozen at the last pose over
            // a moving drawing is worse than no rig — it reads as the drawing
            // having come off its skeleton — and following the playhead costs a
            // solve per tick, which is the cost B152 found nobody had priced
            // when thumbnails did it. So: absent while playing unless asked
            // for. Scrubbing is unaffected; it is not on a clock.
            if (IsPlaying && !AnimateRigDuringPlayback) return [];
            // Mid-gesture the chrome comes from the preview — a scratch clone
            // in bind mode, a provisional pose in posing mode — computed by
            // ArmatureGesture, the same construction the release lands.
            var source = _bonePreviewArmature ?? Doc.Armature;
            if (source is not { Bones.Count: > 0 } armature) return [];

            // Weights joined posing here when painting went live-pose: the
            // brush works on the drawing as it stands at the playhead, so the
            // skeleton has to stand there too or clicking a bone would mean
            // aiming at where it is not.
            var pose = BonesShowAPose
                ? _bonePreviewPose ?? ArmatureOps.EffectivePoseAt(armature, Doc.Scene.PoseTrack, CurrentFrameIndex)
                : null;
            var placements = ArmatureOps.Solve(armature, pose);
            // Everything an artist grabs to drive the rig rather than to BE
            // the rig: IK targets and poles, and every spline handle.
            var handles = new HashSet<string>();
            foreach (var chain in armature.Chains ?? [])
            {
                handles.Add(chain.TargetBoneId);
                if (chain.PoleBoneId is { } pole) handles.Add(pole);
            }
            foreach (var spline in armature.Splines ?? [])
                handles.UnionWith(spline.HandleBoneIds);
            var chrome = new List<BoneChrome>(armature.Bones.Count);

            // The armature's onion skin: the skeleton at the neighbouring
            // pose keys, tinted the way the drawing's ghosts are, so an
            // inbetween pose is judged against where it came from and where
            // it goes. Keys rather than frames, and that is the decision:
            // pose keys are where poses are authored, and a ghost one frame
            // away on an interpolated track is a near-copy that says nothing.
            // First in the list so the live skeleton draws over them.
            // Not while playing, for the reason ScenePassBuilder already
            // suppresses the drawing's onion skin there: ghosts answer "where
            // did this come from and where does it go", and playback answers
            // that by showing it. Measured rather than reasoned — the memo below
            // is keyed on the playhead, so playback misses it on every single
            // tick, and a twenty-bone rig cost **19 ms a tick** with the ghosts
            // in and a fraction of a millisecond without. That is half a 24 fps
            // frame spent drawing context nobody can read at twelve frames a
            // second, and it is exactly the shape B152 found in thumbnails.
            if (PosingMode && Onion.Enabled && !IsPlaying
                && Doc.Scene.PoseTrack is { Keys.Count: > 0 } track)
            {
                // Memoised per playhead position: BoneChromes is re-read on
                // every pointer move of a pose drag, and the ghosts — several
                // spring-window solves each — do not change until the document
                // or the playhead does. The memo is dropped wherever the
                // document-facing notifications fire, so a stale ghost cannot
                // outlive the edit that moved its key.
                if (_ghostChromeCache is not { } cached || cached.Frame != CurrentFrameIndex)
                {
                    var ghosts = new List<BoneChrome>();
                    foreach (var (ghostFrame, kind) in GhostKeyFrames(track))
                    {
                        var at = ArmatureOps.Solve(armature, ArmatureOps.EffectivePoseAt(armature, track, ghostFrame));
                        foreach (var bone in armature.Bones)
                        {
                            // A handle on a pose nobody can grab is clutter.
                            if (handles.Contains(bone.Id)) continue;
                            var g = at[bone.Id];
                            var (gx, gy) = g.Tip(bone.Length);
                            ghosts.Add(new BoneChrome(
                                bone.Id, bone.Name, g.X, g.Y, gx, gy, false, false, kind));
                        }
                    }
                    _ghostChromeCache = cached = (CurrentFrameIndex, ghosts);
                }
                chrome.AddRange(cached.Ghosts);
            }

            foreach (var bone in armature.Bones)
            {
                var p = placements[bone.Id];
                var (tx, ty) = p.Tip(bone.Length);
                // The dashed tether to the parent's tip, for any joint that
                // stands apart from it. Measured on the SOLVED placements, not
                // the record's flags, so it answers what the artist is looking
                // at: a connected joint sits exactly on the tip and shows
                // nothing; an unglued child — or a glued one keyed apart by a
                // pose translation — reads as tethered rather than orphaned.
                double? linkX = null, linkY = null;
                if (bone.ParentId is { } parentId
                    && armature.BoneById(parentId) is { } parentBone
                    && placements.TryGetValue(parentId, out var pp))
                {
                    var (ptx, pty) = pp.Tip(parentBone.Length);
                    if (Math.Abs(ptx - p.X) > 1e-3 || Math.Abs(pty - p.Y) > 1e-3)
                        (linkX, linkY) = (ptx, pty);
                }
                chrome.Add(new BoneChrome(
                    bone.Id, bone.Name, p.X, p.Y, tx, ty,
                    bone.Id == SelectedBoneId, handles.Contains(bone.Id),
                    LinkX: linkX, LinkY: linkY));
            }
            return chrome;
        }
    }

    /// <summary>The ghost skeletons at a playhead position, kept until something moves.</summary>
    private (int Frame, List<BoneChrome> Ghosts)? _ghostChromeCache;

    /// <summary>
    /// The pose-key frames the armature ghosts: up to <c>Onion.Before</c>
    /// keys behind the playhead and <c>Onion.After</c> ahead, nearest first —
    /// the onion bar's own depth controls, read for the skeleton.
    /// </summary>
    private IEnumerable<(int Frame, BoneGhost Kind)> GhostKeyFrames(PoseTrack track)
    {
        var frames = track.Keys.Select(k => k.Frame).Distinct().OrderBy(f => f).ToList();
        foreach (var f in Enumerable.Reverse(frames).Where(f => f < CurrentFrameIndex).Take(Math.Max(0, Onion.Before)))
            yield return (f, BoneGhost.Before);
        foreach (var f in frames.Where(f => f > CurrentFrameIndex).Take(Math.Max(0, Onion.After)))
            yield return (f, BoneGhost.After);
    }

    /// <summary>
    /// The heat view: the selected bone's influence at every control point of
    /// every stroke on the current drawing, drawn where the artist sees the
    /// drawing — the posed positions at the playhead. The <em>weights</em> are
    /// still rest-pose facts (Q81); only where the dots sit follows the pose,
    /// which is what lets the armpit being fixed be the armpit on screen.
    /// Empty when nothing is selected or the weight brush is not armed: the
    /// dots answer "what would this brush touch", so outside Weights mode
    /// they are noise sprinkled over the ink — zero-influence blue on every
    /// line while the artist is only building the skeleton.
    /// </summary>
    public IReadOnlyList<HeatPoint> HeatPoints
    {
        get
        {
            if (!ArmatureEditMode || !WeightPainting || SelectedBoneId is not { } boneId) return [];
            if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return [];

            var posed = WeightSessionPointsFor(frame);
            var points = new List<HeatPoint>();
            foreach (var stroke in frame.Strokes)
            {
                var binding = stroke.Weights?.FirstOrDefault(b => b.BoneId == boneId);
                var at = posed?.GetValueOrDefault(stroke.Id);
                for (var i = 0; i < stroke.Points.Count; i++)
                {
                    var p = at is { } live && i < live.Count ? live[i] : stroke.Points[i];
                    points.Add(new HeatPoint(p.X, p.Y, binding?.WeightAt(i) ?? 0));
                }
            }
            return points;
        }
    }

    /// <summary>
    /// The render pose at the playhead — springs included, so the heat, the
    /// dab and the chrome agree with the pixels — or an empty pose on an
    /// unrigged document.
    /// </summary>
    private Dictionary<string, BonePose> EffectivePoseHere() =>
        Doc.Armature is { } armature
            ? ArmatureOps.EffectivePoseAt(armature, Doc.Scene.PoseTrack, CurrentFrameIndex)
            : [];

    /// <summary>
    /// Where every control point of a drawing sits under a pose — one list per
    /// stroke, one entry per control point, correctives in force. Null when
    /// nothing poses (no keys, no fixes), which is the cue to use the rest
    /// positions without paying for a solve.
    /// </summary>
    /// <remarks>
    /// The construction is the render's (<see cref="Skinning.PoseControlPoints"/>
    /// with the layer's binding as fallback and the resolved correctives), so
    /// where the heat dot sits and where the brush hits is where the ink is
    /// actually drawn — three answers from one arithmetic, which is the only
    /// number of implementations that cannot disagree.
    /// </remarks>
    /// <summary>
    /// The geometry a weight session works against, frozen while the brush is
    /// armed: heat dots sit here, dabs hit here, and painting does not move it.
    /// </summary>
    /// <remarks>
    /// <b>B247.</b> This used to be recomputed per dab, on the argument that
    /// weights move the posed points and the next dab has to find them. The
    /// owner's testing showed what that costs: painting influence toward a bone
    /// <em>drags the drawing under the brush</em> — the artist is aiming at a
    /// target their own gesture pushes away. So the session is the unit now:
    /// the dots recolor and never move, and the deformation the new weights
    /// describe appears when the brush is disarmed, through the same
    /// invalidation every other rig edit runs. Dropped whenever the pose, the
    /// playhead or the document moves; keyed by frame and playhead so a scrub
    /// mid-session re-freezes at the new position.
    /// </remarks>
    private (string FrameId, int FrameIndex, Dictionary<string, List<StrokePoint>>? Points)? _weightSessionGeometry;

    /// <inheritdoc cref="_weightSessionGeometry"/>
    private Dictionary<string, List<StrokePoint>>? WeightSessionPointsFor(Frame frame)
    {
        if (_weightSessionGeometry is { } frozen
            && frozen.FrameId == frame.Id && frozen.FrameIndex == CurrentFrameIndex)
        {
            return frozen.Points;
        }
        var points = PosedPointsFor(frame, EffectivePoseHere());
        _weightSessionGeometry = (frame.Id, CurrentFrameIndex, points);
        return points;
    }

    private Dictionary<string, List<StrokePoint>>? PosedPointsFor(
        Frame frame, IReadOnlyDictionary<string, BonePose> pose)
    {
        if (Doc.Armature is not { Bones.Count: > 0 } armature) return null;
        var corrections = CorrectiveOps.Resolve(frame.Correctives, pose);
        if (pose.Count == 0 && corrections.Count == 0) return null;

        var layerBone = Doc.Scene.RiggedBoneOf(ActiveLayer);
        var named = layerBone is { Length: > 0 }
            ? new List<BoneBinding> { new() { BoneId = layerBone } }
            : null;

        var posed = new Dictionary<string, List<StrokePoint>>(frame.Strokes.Count);
        foreach (var stroke in frame.Strokes)
        {
            posed[stroke.Id] = Skinning.PoseControlPoints(
                stroke, armature, pose, named, corrections.GetValueOrDefault(stroke.Id));
        }
        return posed;
    }

    /// <summary>
    /// Every bone in hierarchy order, parents before children — what the tool
    /// options bar lists so bones can be found and picked by name rather than
    /// only by hitting them on the canvas.
    /// </summary>
    public IReadOnlyList<BoneRow> BoneRows
    {
        get
        {
            if (Doc.Armature is not { Bones.Count: > 0 } armature) return [];
            var rows = new List<BoneRow>(armature.Bones.Count);
            void Walk(string? parentId, int depth)
            {
                foreach (var bone in armature.Bones.Where(b => b.ParentId == parentId))
                {
                    rows.Add(new BoneRow(bone.Id, bone.Name, depth, bone.Id == SelectedBoneId));
                    Walk(bone.Id, depth + 1);
                }
            }
            Walk(null, 0);
            // A bone whose parent id resolves to nothing is a root as far as
            // the solve is concerned, so it has to be listed as one here too —
            // otherwise a half-broken hierarchy renders bones the list cannot
            // show, which is the invisibility bug all over again.
            var seen = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var orphan in armature.Bones.Where(b => !seen.Contains(b.Id)))
                rows.Add(new BoneRow(orphan.Id, orphan.Name, 0, orphan.Id == SelectedBoneId));
            return rows;
        }
    }

    /// <summary>Whether a bone is picked, so the bar can show its controls rather than grey them.</summary>
    public bool HasSelectedBone => SelectedBone is not null;

    /// <summary>The selected bone, or null.</summary>
    public Bone? SelectedBone =>
        SelectedBoneId is { } id ? Doc.Armature?.BoneById(id) : null;

    /// <summary>The selected bone's name, editable — renaming is how X-symmetry pairs are made.</summary>
    public string SelectedBoneName
    {
        get => SelectedBone?.Name ?? "";
        set
        {
            if (SelectedBone is not { } bone || bone.Name == value) return;
            RenameSelectedBone(value);
        }
    }

    /// <summary>
    /// Rename the selected bone. One undo step, and it reaches pixels only
    /// through X-symmetry pairing, so nothing needs re-rendering.
    /// </summary>
    public void RenameSelectedBone(string name)
    {
        if (SelectedBoneId is not { } id || string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        _editor.Perform(doc =>
        {
            if (doc.Armature?.BoneById(id) is { } bone) bone.Name = trimmed;
        });
        OnPropertyChanged(nameof(BoneRows));
        OnPropertyChanged(nameof(SelectedBoneName));
        OnPropertyChanged(nameof(BoneChromes));
    }

    /// <summary>
    /// Delete the selected bone. Its children are re-parented to its own
    /// parent and keep their place on the canvas.
    /// </summary>
    /// <remarks>
    /// <b>Re-parented rather than deleted with it, and the children do not
    /// move.</b> Cascading would take a whole limb with one keystroke and lose
    /// work silently, which is the outcome the repository refuses everywhere
    /// else (a destructive operation is its own deliberate command). Keeping
    /// them where they are costs recomposing each child's local transform
    /// against its new parent — without that a delete teleports the rest of
    /// the chain, which reads as corruption.
    /// </remarks>
    public void DeleteSelectedBone()
    {
        if (SelectedBoneId is not { } id || Doc.Armature is not { } armature) return;
        if (armature.BoneById(id) is not { } doomed) return;

        // Solved before the edit: the placements the children must keep.
        var placements = ArmatureOps.Solve(armature);
        var newParentId = doomed.ParentId;

        _editor.Perform(doc =>
        {
            if (doc.Armature is not { } target) return;
            foreach (var child in target.Bones.Where(b => b.ParentId == id).ToList())
            {
                if (placements.TryGetValue(child.Id, out var world))
                    Rebase(world, newParentId, placements, child);
                child.ParentId = newParentId;
            }
            target.Bones.RemoveAll(b => b.Id == id);
            // Weights pointing at a bone that no longer exists would blend
            // against an identity delta and quietly hold their points at rest;
            // dropping them is the honest record of "this is not bound".
            foreach (var layer in doc.Scene.Layers)
                foreach (var cel in layer.Cels)
                    if (cel.Frame is { } frame)
                        foreach (var stroke in frame.Strokes)
                            if (stroke.Weights is { } weights && weights.RemoveAll(w => w.BoneId == id) > 0
                                && weights.Count == 0)
                                stroke.Weights = null;
            // Pose keys for it go too, or a later bone reusing the id inherits
            // a pose nobody authored for it.
            if (doc.Scene.PoseTrack is { } track)
                foreach (var key in track.Keys) key.Bones.Remove(id);
            // And so do the chains and constraints that named it. The solve
            // already refuses one whose target is missing, so leaving them
            // would not misbehave — it would be worse than that: an entry the
            // panel lists, the artist adjusts, and nothing ever happens.
            target.Chains?.RemoveAll(c => c.TipBoneId == id || c.TargetBoneId == id);
            foreach (var chain in target.Chains ?? [])
                if (chain.PoleBoneId == id) chain.PoleBoneId = null;
            if (target.Chains is { Count: 0 }) target.Chains = null;
            target.Constraints?.RemoveAll(c => c.BoneId == id || c.TargetBoneId == id);
            if (target.Constraints is { Count: 0 }) target.Constraints = null;
            // A spline losing a handle is not necessarily dead — three
            // handles down to two is still a curve — so drop the handle and
            // only drop the chain when it can no longer draw one.
            target.Splines?.RemoveAll(s => s.TipBoneId == id);
            foreach (var spline in target.Splines ?? []) spline.HandleBoneIds.Remove(id);
            target.Splines?.RemoveAll(s => s.HandleBoneIds.Count < 2);
            if (target.Splines is { Count: 0 }) target.Splines = null;
        });

        SelectedBoneId = null;
        OnPropertyChanged(nameof(HasArmature));
        OnPropertyChanged(nameof(BoneRows));
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Re-parent the selected bone without moving it on the canvas.
    /// </summary>
    public void SetSelectedBoneParent(string? parentId)
    {
        if (SelectedBoneId is not { } id || Doc.Armature is not { } armature) return;
        if (armature.BoneById(id) is not { } bone || bone.ParentId == parentId) return;
        // A bone cannot be its own ancestor, or the solve walks in a circle.
        for (var walk = parentId; walk is not null;)
        {
            if (walk == id) return;
            walk = armature.BoneById(walk)?.ParentId;
        }

        var placements = ArmatureOps.Solve(armature);
        _editor.Perform(doc =>
        {
            if (doc.Armature?.BoneById(id) is not { } target) return;
            if (placements.TryGetValue(id, out var world)) Rebase(world, parentId, placements, target);
            target.ParentId = parentId;
        });
        OnPropertyChanged(nameof(BoneRows));
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Move a bone bodily by a canvas delta — the gesture a drag on the shaft
    /// makes, and the one an artist reaches for first.
    /// </summary>
    /// <remarks>
    /// The delta arrives in document space and the bone's offset lives in its
    /// parent's, so it is rotated into that frame before it is added.
    /// Children follow, because their offsets are relative to this one.
    /// </remarks>
    public void MoveBoneBy(string id, double dx, double dy)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(id) is null) return;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;

        // The edit itself lives in ArmatureGesture, shared with the live
        // preview — the drag shows the same construction the release lands.
        _editor.Perform(doc =>
        {
            if (doc.Armature is { } target) ArmatureGesture.ApplyMoveBy(target, id, dx, dy);
        });
        NotifyArmatureSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Rewrite a bone's local offset and rotation so that it keeps the world
    /// placement it already had under a different parent.
    /// </summary>
    private static void Rebase(
        BonePlacement world, string? newParentId,
        IReadOnlyDictionary<string, BonePlacement> placements, Bone bone)
    {
        // The whole point of a rebase is that the bone does not move, and a
        // bone still glued after its parent changed would jump to the NEW
        // parent's tip the next time it is solved. Unglue rather than teleport.
        bone.Connected = null;
        var parent = newParentId is not null && placements.TryGetValue(newParentId, out var p)
            ? p
            : new BonePlacement(0, 0, 0);
        var rad = -parent.RotationDeg * Math.PI / 180.0;
        var (dx, dy) = (world.X - parent.X, world.Y - parent.Y);
        bone.X = Math.Cos(rad) * dx - Math.Sin(rad) * dy;
        bone.Y = Math.Sin(rad) * dx + Math.Cos(rad) * dy;
        bone.RotationDeg = world.RotationDeg - parent.RotationDeg;
    }

    // Unglue moved to ArmatureGesture, whose bind-mode edits are the callers.

    /// <summary>The brush mode as an index, because a ComboBox speaks indices.</summary>
    public int WeightBrushModeIndex
    {
        get => (int)WeightBrushMode;
        set => WeightBrushMode = (WeightBrushMode)Math.Clamp(value, 0, 2);
    }

    [RelayCommand]
    private void DeleteBone() => DeleteSelectedBone();

    [RelayCommand]
    private void AssignStrokesToBone() => AssignSelectedStrokesToBone();

    [RelayCommand]
    private void AutoBindStrokes() => AutoBindSelectedStrokes();

    [RelayCommand]
    private void BakePose() => BakePoseHere();

    [RelayCommand]
    private void InsertPoseDrawing() => InsertDrawingFromPose();

    /// <summary>
    /// Re-read the slice of the rig surface that depends on <em>where the
    /// playhead is</em>: the posed chrome, the heat dots on the posed drawing,
    /// and which correctives are in force. Called on scrub and on playback
    /// stop — never per playback tick (B152).
    /// </summary>
    internal void RefreshArmatureAtPlayhead()
    {
        _ghostChromeCache = null;
        OnPropertyChanged(nameof(BoneChromes));
        OnPropertyChanged(nameof(HeatPoints));
        OnPropertyChanged(nameof(CorrectiveRows));
        OnPropertyChanged(nameof(HasCorrectives));
    }

    /// <summary>
    /// Re-read everything the rig surface derives from the document.
    /// </summary>
    /// <remarks>
    /// <b>Called from <c>OnDocumentChanged</c>, which is where undo lands</b>,
    /// and its absence is what "no undo on the bones" was: the editor restored
    /// the record correctly and nothing told the canvas or the options panel,
    /// so the bone stayed drawn and the panel kept its old answer. Every one of
    /// these is a plain computed property over <c>Doc</c> — they raise nothing
    /// by themselves, exactly like the layer flags <c>RefreshPointerIntent</c>
    /// exists for.
    /// </remarks>
    private void NotifyArmatureSurface()
    {
        _ghostChromeCache = null;
        OnPropertyChanged(nameof(HasArmature));
        OnPropertyChanged(nameof(BoneRows));
        OnPropertyChanged(nameof(BoneChromes));
        OnPropertyChanged(nameof(HeatPoints));
        OnPropertyChanged(nameof(HasSelectedBone));
        OnPropertyChanged(nameof(SelectedBone));
        OnPropertyChanged(nameof(SelectedBoneName));
        OnPropertyChanged(nameof(SelectedBoneLength));
        OnPropertyChanged(nameof(SelectedBoneJiggles));
        OnPropertyChanged(nameof(SelectedBoneJiggleStiffness));
        OnPropertyChanged(nameof(SelectedBoneJiggleDamping));
        OnPropertyChanged(nameof(PointerIntent));
        // A bone that undo took away must not stay selected, or the panel
        // offers rename and delete for something that is gone.
        if (SelectedBoneId is { } id && Doc.Armature?.BoneById(id) is null) SelectedBoneId = null;
        NotifyIkSurface();
        NotifyConstraintSurface();
        NotifySplineSurface();
        NotifyCorrectiveSurface();
    }

    /// <summary>
    /// Grow a child from a bone's tip, reaching to where the pointer let go —
    /// Blender's extrude, which is how a limb is built one bone at a time.
    /// </summary>
    /// <remarks>
    /// <b>It starts at the parent's tip</b>, which is the difference from an
    /// empty-canvas drag: that one parents to the selection but begins
    /// wherever the press landed. Bending the parent afterwards carries the
    /// whole chain, because the offset lives in the parent's frame.
    /// <para>
    /// <b>Glued to the tip, not merely placed at it</b> (Q86): the child is
    /// <see cref="Bone.Connected"/>, so re-proportioning the parent drags the
    /// whole chain after it rather than leaving a gap to close by hand. Any
    /// bind-mode drag that moves the child's origin unglues it again.
    /// </para>
    /// </remarks>
    public void ExtrudeChildFrom(string parentId, double x, double y)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(parentId) is null) return;

        var name = NextBoneName();
        Bone? child = null;
        // The edit itself lives in ArmatureGesture, shared with the live
        // preview — the drag shows the same construction the release lands.
        _editor.Perform(doc =>
        {
            if (doc.Armature is { } target)
                child = ArmatureGesture.ApplyExtrude(target, parentId, name, x, y);
        });
        if (child is null) return;

        SelectedBoneId = child.Id;
        NotifyArmatureSurface();
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// The selected bone's length, as a number rather than only a drag —
    /// "scale them easily", for the times a limb has to match a measurement.
    /// </summary>
    public double SelectedBoneLength
    {
        get => SelectedBone?.Length ?? 0;
        set
        {
            if (SelectedBoneId is not { } id || SelectedBone is not { } bone) return;
            var wanted = Math.Max(ArmatureOverlay.MinimumLength, value);
            if (Math.Abs(bone.Length - wanted) < 1e-9) return;
            _editor.Perform(doc =>
            {
                if (doc.Armature?.BoneById(id) is { } target) target.Length = wanted;
            });
            NotifyArmatureSurface();
            InvalidateRiggedFrames();
        }
    }

    /// <summary>
    /// Whether the selected bone jiggles — phase 5's switch. Turning it on
    /// authors a <see cref="BoneJiggle"/> with its defaults; turning it off
    /// removes the record entirely (absent, not disabled).
    /// </summary>
    public bool SelectedBoneJiggles
    {
        get => SelectedBone?.Jiggle is not null;
        set
        {
            if (SelectedBoneId is not { } id || SelectedBoneJiggles == value) return;
            _editor.Perform(doc =>
            {
                if (doc.Armature?.BoneById(id) is { } bone)
                    bone.Jiggle = value ? new BoneJiggle() : null;
            });
            NotifyArmatureSurface();
            InvalidateRiggedFrames();
        }
    }

    /// <summary>How hard the jiggle is pulled toward the pose, 0.01–1.</summary>
    public double SelectedBoneJiggleStiffness
    {
        get => SelectedBone?.Jiggle?.Stiffness ?? 0.2;
        set => EditJiggle(j => j.Stiffness = Math.Clamp(value, 0.01, 1));
    }

    /// <summary>How quickly the swing dies, 0–1.</summary>
    public double SelectedBoneJiggleDamping
    {
        get => SelectedBone?.Jiggle?.Damping ?? 0.15;
        set => EditJiggle(j => j.Damping = Math.Clamp(value, 0.02, 1));
    }

    private void EditJiggle(Action<BoneJiggle> edit)
    {
        if (SelectedBoneId is not { } id || SelectedBone?.Jiggle is null) return;
        _editor.Perform(doc =>
        {
            if (doc.Armature?.BoneById(id)?.Jiggle is { } jiggle) edit(jiggle);
        });
        NotifyArmatureSurface();
        InvalidateRiggedFrames();
    }

    partial void OnPosingModeChanged(bool value)
    {
        if (value) WeightPainting = false;
        OnPropertyChanged(nameof(IsBindMode));
        // Drawing a fix is a posing gesture — leaving posing mid-capture would
        // strand the baked drawing in the record with no way back to it.
        if (!value && CapturingCorrective) CancelCorrectiveCommand.Execute(null);
        NotifyCorrectiveSurface();
    }

    partial void OnWeightPaintingChanged(bool value)
    {
        // Painting happens against the rest pose, so arming the brush leaves
        // posing rather than sitting on top of it.
        if (value) PosingMode = false;
        OnPropertyChanged(nameof(IsBindMode));
        // The frozen session geometry ends with the session (B247) — and
        // disarming is where the weight strokes' deferred invalidation lands,
        // so the ink takes up its new deformation the moment the brush is
        // put down rather than mid-stroke.
        _weightSessionGeometry = null;
        if (!value) InvalidateRiggedFrames();
    }

    /// <summary>Grow a child of the usual length straight off the selected bone's tip.</summary>
    [RelayCommand]
    private void AddChildBone()
    {
        if (SelectedBoneId is not { } id || Doc.Armature is not { } armature) return;
        if (armature.BoneById(id) is not { } parent) return;
        var at = ArmatureOps.Solve(armature)[id];
        var (tipX, tipY) = at.Tip(parent.Length);
        // Straight on from the parent, its own length — a starting point to
        // drag rather than a guess at where the limb goes.
        var rad = at.RotationDeg * Math.PI / 180.0;
        ExtrudeChildFrom(id, tipX + Math.Cos(rad) * parent.Length, tipY + Math.Sin(rad) * parent.Length);
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
        var parentId = SelectedBoneId;
        var name = NextBoneName();
        Bone? bone = null;

        // The edit itself lives in ArmatureGesture, shared with the live
        // preview — the drag shows the same construction the release lands.
        _editor.Perform(doc =>
        {
            var armature = doc.Armature ??= new Armature();
            bone = ArmatureGesture.ApplyCreate(armature, parentId, name, x0, y0, x1, y1);
        });

        SelectedBoneId = bone?.Id;
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
        if (Doc.Armature is not { } armature || armature.BoneById(id) is null) return;

        // The edit itself lives in ArmatureGesture, shared with the live
        // preview — the drag shows the same construction the release lands.
        _editor.Perform(doc =>
        {
            if (doc.Armature is { } target) ArmatureGesture.ApplyDragBind(target, id, grab, x, y);
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

        // Three bones an FK rotation would silently fail on. A chain-driven
        // bone's rotation is the solver's, so a key on it is overwritten on
        // the very next solve; a handle and a pole are *placed* rather than
        // aimed, so turning them moves nothing at all. All three want a
        // translation, and the handle is the one the artist means.
        if (ChainTouching(id) is { } chain)
        {
            PoseTranslateTo(chain.PoleBoneId == id || chain.TargetBoneId == id ? id : chain.TargetBoneId, x, y);
            return;
        }

        // A spline handle is placed, not aimed, for the same reason — and a
        // bone laid out by a curve has the solver's rotation, so a key on it
        // would be overwritten. Both want a translation of the handle.
        if (SplineTouching(id) is { } spline)
        {
            PoseTranslateTo(
                spline.HandleBoneIds.Contains(id) ? id : NearestHandle(spline, x, y),
                x, y);
            return;
        }

        var frame = CurrentFrameIndex;
        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, frame);
        var placements = ArmatureOps.Solve(armature, pose);
        // The pivot is where the bone is DRAWN — a constraint may have moved
        // it, and the artist drags relative to what they can see. The angle,
        // though, is measured against forward kinematics rather than against
        // the placement: the key means "point here", and a constraint blends
        // from it by its strength. Measuring against the constrained angle
        // instead made the key mean nothing an artist could predict — at half
        // strength, a drag straight right landed the bone straight left.
        // (Unconstrained the two are the same expression, so nothing else
        // changes.) The arithmetic is ArmatureGesture's, shared with the
        // live preview.
        var newDelta = ArmatureGesture.PoseAimDelta(armature, placements, id, x, y);

        KeyPose(frame, id, p => p.RotationDeg = newDelta);
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Land a pose-mode drag under the grab that started it: the tip aims the
    /// bone, the shaft and the joint carry it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pose mode reads a grab exactly the way bind mode does</b>, and that
    /// symmetry is the point (owner's decision, 2026-08-18): the grab decides
    /// what KIND of edit a drag is, the mode decides whether it lands on the
    /// rest pose or on the pose key. Before this, every pose drag aimed —
    /// reported as "in pose mode I am unable to move the entire skeleton …
    /// grabbing the first bone or any bone for that matter, just rotates".
    /// There was no gesture that translated an ordinary bone at all, so a
    /// character could be posed and never moved, and the Transform tool moves
    /// the drawing rather than the rig.
    /// </para>
    /// <para>
    /// Moving the root moves the whole skeleton, because children ride their
    /// parent through FK — the translation is written on one bone and the
    /// hierarchy carries it, which is why this needs no "move the armature"
    /// command beside it.
    /// </para>
    /// <para>
    /// Chain and spline bones keep their existing answer for every grab: they
    /// are <em>placed</em> rather than aimed or nudged, and the bone the drag
    /// moves is often not the bone under the pointer, so a delta would have
    /// nothing to be a delta of.
    /// </para>
    /// </remarks>
    public void PoseDrag(string id, BoneGrab grab, double x0, double y0, double x, double y)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(id) is null) return;

        if (ChainTouching(id) is { } chain)
        {
            PoseTranslateTo(chain.PoleBoneId == id || chain.TargetBoneId == id ? id : chain.TargetBoneId, x, y);
            return;
        }
        if (SplineTouching(id) is { } spline)
        {
            PoseTranslateTo(spline.HandleBoneIds.Contains(id) ? id : NearestHandle(spline, x, y), x, y);
            return;
        }

        switch (grab)
        {
            // The tip is the aiming handle in both modes — bind re-lengths as
            // well, pose only turns, because a pose cannot hold a length.
            case BoneGrab.Tip:
                PoseBoneTo(id, x, y);
                break;
            // The joint handle is under the pointer already, so placing it
            // there is the same gesture bind mode makes of the same grab.
            case BoneGrab.Origin:
                PoseTranslateTo(id, x, y);
                break;
            default:
                PoseMoveBy(id, x - x0, y - y0);
                break;
        }
    }

    /// <summary>
    /// Carry a bone — and everything parented to it — by a world-space delta,
    /// keyed at the playhead. The pose-mode shaft drag.
    /// </summary>
    /// <remarks>
    /// A delta rather than a destination, for <c>PoseMoveDelta</c>'s reason:
    /// the shaft is grabbed anywhere along its length. The base is the pose
    /// the drag started on, read back from the track each call rather than
    /// accumulated, so the preview per pointer event and the one release that
    /// commits arrive at the same place.
    /// </remarks>
    internal void PoseMoveBy(string id, double dx, double dy)
    {
        if (Doc.Armature is not { } armature || armature.BoneById(id) is not { } bone) return;

        var frame = CurrentFrameIndex;
        var pose = ArmatureOps.PoseAt(Doc.Scene.PoseTrack, frame);
        var placements = ArmatureOps.Solve(armature, pose);
        var (tx, ty) = ArmatureGesture.PoseMoveDelta(
            placements, bone, pose.GetValueOrDefault(id), dx, dy);

        KeyPose(frame, id, p =>
        {
            p.X = tx;
            p.Y = ty;
        });
        InvalidateRiggedFrames();
    }

    /// <summary>
    /// Write one bone's departure from rest into the key at a frame, creating
    /// the key — and the track — if there is none.
    /// </summary>
    /// <remarks>
    /// A new key starts from what the artist SAW at this frame, the
    /// interpolated pose, so keying one bone cannot snap its neighbours back
    /// to rest. One undo step, because it is one <c>Perform</c>.
    /// </remarks>
    private void KeyPose(int frame, string boneId, Action<BonePose> edit) =>
        _editor.Perform(doc =>
        {
            var track = doc.Scene.PoseTrack ??= new PoseTrack();
            var key = ArmatureOps.KeyAt(track, frame);
            if (key is null)
            {
                key = new PoseKey { Frame = frame };
                foreach (var (id, p) in ArmatureOps.PoseAt(doc.Scene.PoseTrack, frame))
                    key.Bones[id] = p;
                track.Keys.Add(key);
            }
            edit(key.Bones.TryGetValue(boneId, out var b) ? b : key.Bones[boneId] = new BonePose());
        });

    /// <summary>
    /// The weight brush is armed: Bone-tool presses paint influence for the
    /// selected bone instead of working bones. The arithmetic is
    /// <see cref="WeightPaint"/>; this owns the gesture and its single undo
    /// step.
    /// </summary>
    /// <remarks>
    /// <b>B246.</b> The chrome and heat notifications are what make the switch
    /// visible: both getters gate on this flag, so arming the brush without
    /// announcing them left the heat absent until the first dab happened to
    /// refresh the overlay — and disarming left stale heat on screen.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointerIntent))]
    [NotifyPropertyChangedFor(nameof(PointerBadge))]
    [NotifyPropertyChangedFor(nameof(BoneChromes))]
    [NotifyPropertyChangedFor(nameof(HeatPoints))]
    [NotifyPropertyChangedFor(nameof(BrushCursorDiameter))]
    [NotifyPropertyChangedFor(nameof(BrushCursorTipId))]
    [NotifyPropertyChangedFor(nameof(BrushCursorRoundness))]
    [NotifyPropertyChangedFor(nameof(BrushCursorAngle))]
    private bool _weightPainting;

    /// <summary>Brush radius in document pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrushCursorDiameter))]
    private double _weightBrushRadius = 24;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointerBadge))]
    private WeightBrushMode _weightBrushMode = WeightBrushMode.Add;

    /// <summary>
    /// One press into a weight-paint mode from anywhere: the Bone tool if it
    /// is not in hand, the brush armed, the mode set. What the mode shortcuts
    /// (Shift+1/2/3) run, so "start subtracting" is a keystroke rather than a
    /// tool change, a toggle and a dropdown.
    /// </summary>
    public void ArmWeightBrush(WeightBrushMode mode)
    {
        if (ActiveTool != ToolId.Bone) SelectToolCommand.Execute(ToolId.Bone);
        WeightPainting = true;
        WeightBrushMode = mode;
    }

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
    /// The dab hits the points <em>where the artist sees them</em> — their
    /// posed positions at the playhead — so weights can be corrected while
    /// looking at the deformation being corrected. The weights themselves stay
    /// rest-pose facts on the same indices (Q81).
    /// </summary>
    public void WeightDab(double x, double y, double pressure)
    {
        if (_weightGesture is not { } gesture) return;
        if (SelectedBoneId is not { } boneId || Doc.Armature is not { } armature) return;

        // The session's frozen geometry (B247): the dab hits the points where
        // the heat shows them, and the heat does not move while painting. The
        // per-dab recompute this replaces made each dab drag its own target —
        // weights moved the posed points, so the artist chased the drawing
        // their gesture was pushing away.
        var frame = ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex);
        var posed = frame is not null && frame.Id == _weightGestureFrameId
            ? WeightSessionPointsFor(frame)
            : null;

        // Per-dab rate below 1 so a held brush builds rather than slams —
        // the same reason flow exists on a paint brush.
        var strength = Math.Clamp(pressure, 0.05, 1) * 0.35;
        var changed = false;
        foreach (var (stroke, _) in gesture)
            changed |= WeightPaint.Apply(
                stroke, boneId, x, y, WeightBrushRadius, strength, WeightBrushMode,
                hitPoints: posed?.GetValueOrDefault(stroke.Id));

        if (MirrorWeights
            && WeightPaint.MirroredBone(armature, boneId) is { } pair
            && armature.BoneById(boneId) is { } own)
        {
            // Posed, the mirrored dab has to land on the pair's limb wherever
            // its pose put it — the rest-space mirror would paint thin air.
            var m = posed is null
                ? WeightPaint.Mirror(armature, own, pair, x, y)
                : WeightPaint.MirrorPosed(armature, own, pair, Skinning.Deltas(armature, EffectivePoseHere()), x, y);
            if (m is { } mirrored)
            {
                foreach (var (stroke, _) in gesture)
                    changed |= WeightPaint.Apply(
                        stroke, pair.Id, mirrored.X, mirrored.Y, WeightBrushRadius, strength, WeightBrushMode,
                        hitPoints: posed?.GetValueOrDefault(stroke.Id));
            }
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
        // Deliberately NOT InvalidateRiggedFrames (B247): the record changed
        // but the picture must not. A weight stroke recolors influence — it
        // does not move ink — so the rigged-frame invalidation, and the rig
        // index rebuild that would re-key a newly-bound stroke's frame, both
        // wait for the brush to be disarmed (OnWeightPaintingChanged). The
        // deformation the new weights describe shows up when the artist
        // returns to posing, which is where influence belongs.
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
            baked = Skinning.BakeFrame(
                target, armature, ArmatureOps.EffectivePoseAt(armature, doc.Scene.PoseTrack, index),
                doc.Scene.RiggedBoneOf(doc.Scene.Layers[ActiveLayerIndex]));
        });
        if (baked > 0)
        {
            InvalidateFrameRender(frame.Id);
            AiStatus = baked == 1 ? "Baked the pose into 1 line." : $"Baked the pose into {baked} lines.";
        }
        return baked;
    }

    /// <summary>
    /// Turn the pose at the playhead into a drawing of its own: the hold
    /// breaks, and the new drawing holds exactly what the rig was showing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The bridge between live posing and frame-by-frame</b>, and the
    /// answer to the owner's report of 2026-08-18: bones were being posed
    /// across a run cycle as a construction guide, and playback showed the two
    /// drawings that existed rather than the fifteen poses that had been
    /// authored. Posing writes a pose key and nothing else — deliberately, so
    /// that scrubbing and trying a pose stay free of consequence — which left
    /// no gesture that said "and this one is a drawing".
    /// </para>
    /// <para>
    /// One command covers both ways the rig is used, because it commits what
    /// is on screen rather than a category (owner's decision): bound strokes
    /// arrive baked into their posed positions, ready to touch up; unbound
    /// ones — a pure bone guide over hand-drawn art — arrive copied as they
    /// are, to be redrawn over with the posed skeleton showing through. The
    /// hold is kept intact when the cel already has a drawing of its own;
    /// there the command is <see cref="BakePoseHere"/> and nothing more.
    /// </para>
    /// <para>
    /// One undo step for both halves. Keying the cel and baking into it are
    /// two edits to the record, and an artist who presses this once and undoes
    /// once means both.
    /// </para>
    /// </remarks>
    /// <returns>Whether a drawing was inserted — false when the cel already had one.</returns>
    public bool InsertDrawingFromPose() =>
        InsertDrawingFromPoseAt(ActiveLayerIndex, CurrentFrameIndex);

    /// <summary>
    /// The same command aimed at a cel the artist picked rather than at the
    /// playhead — the X-sheet's right-click.
    /// </summary>
    /// <remarks>
    /// A cel-targeted overload rather than "move the playhead, then run the
    /// other one", for the reason every item in that menu is written this way:
    /// the pose baked in must be the pose at the frame that was <em>clicked</em>,
    /// and driving it through the playhead would make that a side effect of
    /// navigation instead of the point. The playhead does follow afterwards, as
    /// it does for <see cref="InsertFrameAt"/> — the artist is about to draw on
    /// what they just made.
    /// </remarks>
    public bool InsertDrawingFromPoseAt(FrameCell cell) =>
        InsertDrawingFromPoseAt(cell.LayerIndex, cell.Index);

    private bool InsertDrawingFromPoseAt(int layerIndex, int frameIndex)
    {
        if (Doc.Armature is not { Bones.Count: > 0 }) return false;
        if (layerIndex < 0 || layerIndex >= Scene.Layers.Count) return false;
        if (frameIndex < 0) return false;
        // Q103's rule, the same one painting follows: this is an edit landing,
        // so a cel picked past the end of the scene grows it to reach. The
        // X-sheet draws those cells, so the right-click can genuinely land on
        // one.
        if (frameIndex >= Scene.FrameCount && _editor.GrowToInclude(frameIndex))
        {
            AiStatus = $"Scene extended to {frameIndex + 1} frames.";
        }
        if (Scene.Layers[layerIndex] is not { Cels.Count: > 0 } active) return false;

        var index = Math.Clamp(frameIndex, 0, active.Cels.Count - 1);
        var layerId = active.Id;
        var inserted = false;
        var baked = 0;
        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
            if (index >= layer.Cels.Count) return;
            var target = layer.Cels[index].Frame;
            if (target is null)
            {
                // The copy carries strokes, baseline pixels and placements, so
                // the insert alone changes no pixel — every visible difference
                // is the bake below. PaintTargetOrKey's copy, not its policy:
                // `DrawingOnAHold` decides what a MARK on a hold means, and
                // this command has already been told what it means.
                var held = ExposureSheet.ExposedFrame(layer, index);
                target = KeyedCopyOf(held);
                layer.Cels[index].Frame = target;
                inserted = true;
                // B259, and the promise this command makes: **only the frame
                // you are on becomes a drawing.** A hold shows the nearest
                // drawing BEFORE it, so dropping one in re-points every hold
                // after it at the new drawing — which is baked, and a baked
                // drawing answers to no rig. Posing four frames and keeping the
                // second froze the third and fourth at the second's pose.
                //
                // Re-exposing the held drawing on the next cel puts the row
                // back exactly as it read: the same drawing on the same frames,
                // still the object every later hold resolves to, so an edit to
                // it still reaches all of them the way it did when they were
                // one long hold.
                if (held is not null
                    && index + 1 < layer.Cels.Count
                    && layer.Cels[index + 1].Frame is null)
                {
                    layer.Cels[index + 1].Frame = held;
                }
            }
            if (doc.Armature is { } armature)
            {
                baked = Skinning.BakeFrame(
                    target, armature,
                    ArmatureOps.EffectivePoseAt(armature, doc.Scene.PoseTrack, index),
                    doc.Scene.RiggedBoneOf(layer));
            }
        });

        // The index before the invalidation, for RebuildRigIndex's reason: a
        // drawing that has just stopped being a hold is a frame the index has
        // never seen, and a key built against the old one would be the wrong
        // shape.
        RebuildRigIndex();
        InvalidateRiggedFrames();
        // Where the drawing is, is where the artist should be standing —
        // InsertFrameAt's ending, for InsertFrameAt's reason. Only on an
        // insert: a click that turned out to bake into an existing drawing has
        // moved nobody's work anywhere.
        if (inserted)
        {
            ActiveLayerIndex = layerIndex;
            CurrentFrameIndex = Math.Min(index, Scene.FrameCount - 1);
        }
        AiStatus = (inserted, baked) switch
        {
            (true, 0) => $"Drawing inserted at frame {index + 1}, holding the pose.",
            (true, _) => $"Drawing inserted at frame {index + 1}, {Lines(baked)} baked from the pose.",
            (false, 0) => "This frame already has a drawing of its own — nothing to bake into it.",
            _ => $"Baked the pose into {Lines(baked)} on this frame's own drawing.",
        };
        return inserted;

        static string Lines(int n) => n == 1 ? "1 line" : $"{n} lines";
    }

    /// <summary>
    /// Retime a pose key by dragging its dot on the timeline's armature track:
    /// the whole key on the summary row, one bone's entry on a bone row.
    /// </summary>
    /// <remarks>
    /// <paramref name="boneId"/> null means the summary row — the key and
    /// every bone on it. The record ops are <c>ArmatureOps</c>' and are shared
    /// with nothing else yet; they live in Core because retiming a key is a
    /// pure edit of the track and wants testing without a window.
    /// </remarks>
    public bool MovePoseKey(string? boneId, int from, int to)
    {
        if (from == to || to < 0) return false;
        var moved = false;
        // Remembered before the step, so a drag that turns out to move nothing
        // can take its own step back — B236's admission, through the editor's
        // own door for it. An Undo would leave a redo behind, which is the one
        // thing a step nobody authored must not do.
        var revision = _editor.NextRevision;
        _editor.Perform(doc =>
        {
            moved = boneId is null
                ? ArmatureOps.MoveKey(doc.Scene.PoseTrack, from, to)
                : ArmatureOps.MoveBoneKey(doc.Scene.PoseTrack, boneId, from, to);
        });
        if (!moved)
        {
            _editor.DiscardStep(revision);
            return false;
        }
        AfterPoseTrackEdit();
        AiStatus = boneId is null
            ? $"Pose key moved to frame {to + 1}."
            : $"{NameOfBone(boneId)} key moved to frame {to + 1}.";
        return true;
    }

    /// <summary>
    /// Delete a pose key from the timeline: the whole key on the summary row,
    /// one bone's entry on a bone row.
    /// </summary>
    public bool DeletePoseKey(string? boneId, int frame)
    {
        var removed = false;
        var revision = _editor.NextRevision;
        _editor.Perform(doc =>
        {
            removed = boneId is null
                ? ArmatureOps.RemoveKey(doc.Scene.PoseTrack, frame)
                : ArmatureOps.RemoveBoneKey(doc.Scene.PoseTrack, boneId, frame);
        });
        if (!removed)
        {
            _editor.DiscardStep(revision);
            return false;
        }
        AfterPoseTrackEdit();
        AiStatus = boneId is null
            ? $"Pose key at frame {frame + 1} removed."
            : $"{NameOfBone(boneId)} unkeyed at frame {frame + 1}.";
        return true;
    }

    /// <summary>
    /// Author a pose key at a frame with the pose already interpolated there —
    /// <c>AddCameraKeyAt</c>'s rule, applied to the rig: keying what is
    /// already true changes nothing visually, which is what makes it safe to
    /// then drag or edit.
    /// </summary>
    public void AddPoseKeyAt(int frame)
    {
        if (Doc.Armature is not { Bones.Count: > 0 } || frame < 0) return;
        if (ArmatureOps.KeyAt(Scene.PoseTrack, frame) is not null) return;
        _editor.Perform(doc =>
        {
            var track = doc.Scene.PoseTrack ??= new PoseTrack();
            var key = new PoseKey { Frame = frame };
            foreach (var (id, p) in ArmatureOps.PoseAt(track, frame)) key.Bones[id] = p;
            track.Keys.Add(key);
        }, label: "Key pose");
        AfterPoseTrackEdit();
        AiStatus = $"Pose keyed at frame {frame + 1}.";
    }

    /// <summary>The easing a pose key runs into its successor with, for the menu's check mark.</summary>
    public Easing? PoseKeyEaseAt(int frame) => ArmatureOps.KeyAt(Scene.PoseTrack, frame)?.Ease;

    /// <summary>
    /// Set how the pose key at a frame eases into the next one —
    /// <see cref="SetCameraKeyEase"/>'s verb on the rig's row, including
    /// <see cref="Easing.Hold"/>, which is what makes a key a held pose
    /// rather than a tween's endpoint (Q152).
    /// </summary>
    public void SetPoseKeyEase(int frame, Easing ease)
    {
        if (ArmatureOps.KeyAt(Scene.PoseTrack, frame) is not { } key || key.Ease == ease) return;
        _editor.Perform(doc =>
        {
            if (ArmatureOps.KeyAt(doc.Scene.PoseTrack, frame) is { } k) k.Ease = ease;
        }, label: "Ease pose key");
        AfterPoseTrackEdit();
        AiStatus = ease == Easing.Hold
            ? $"Pose holds from frame {frame + 1}."
            : $"Pose key at frame {frame + 1} eases {ease}.";
    }

    /// <summary>The interval the render pose is sampled on — 1 when the track is fluid.</summary>
    public int PoseStep => Scene.PoseTrack?.Step is { } step and > 1 ? step : 1;

    /// <summary>
    /// Animate the rig on Ns (Q152): the pose track keeps its fluid tween and
    /// the render samples it every <paramref name="step"/> frames. 1 turns
    /// stepping off — and removes the track entirely when nothing else is
    /// authored on it, because optional means absent.
    /// </summary>
    public void SetPoseStep(int step)
    {
        var wanted = Math.Max(1, step);
        if (wanted == PoseStep) return;
        _editor.Perform(doc =>
        {
            if (wanted == 1)
            {
                if (doc.Scene.PoseTrack is not { } track) return;
                if (track.Keys.Count == 0) doc.Scene.PoseTrack = null;
                else track.Step = null;
            }
            else
            {
                (doc.Scene.PoseTrack ??= new PoseTrack()).Step = wanted;
            }
        }, label: "Pose step");
        AfterPoseTrackEdit();
        AiStatus = wanted == 1 ? "Pose sampled every frame." : $"Pose on {wanted}s.";
    }

    /// <summary>
    /// What every pose-track edit has to refresh: the pixels the track drives,
    /// and the rows that draw it.
    /// </summary>
    /// <remarks>
    /// Retiming a key changes what every frame between the neighbouring keys
    /// interpolates to, so this is not a one-frame invalidation — which is why
    /// it goes through <see cref="InvalidateRiggedFrames"/> rather than
    /// naming a frame.
    /// </remarks>
    private void AfterPoseTrackEdit()
    {
        NotifyOffSheetKeys();
        InvalidateRiggedFrames();
        RefreshArmatureAtPlayhead();
        OnPropertyChanged(nameof(TimelineTracks));
    }

    private string NameOfBone(string boneId) =>
        Doc.Armature?.BoneById(boneId)?.Name ?? "Bone";

    /// <summary>
    /// A pose or weight edit changes pixels the frame cache cannot key on, so
    /// every frame holding bound strokes is re-rendered on next sight.
    /// </summary>
    private void InvalidateRiggedFrames()
    {
        _ghostChromeCache = null;
        // Anything that moves the world re-freezes the weight session's
        // geometry: a pose, bone or corrective edit means the dots must stand
        // somewhere new (B247).
        _weightSessionGeometry = null;
        // The index first, and that ordering is load-bearing: it decides
        // whether a frame's cache key carries the playhead, so invalidating
        // against a stale one would clear entries under keys that are about to
        // change shape and leave the new ones warm and wrong.
        RebuildRigIndex();
        foreach (var layer in Doc.Scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { } drawing && _cache.Rig.IsPosed(drawing))
                    InvalidateFrameRender(drawing.Id);
        PublishSnapshot();
        OnPropertyChanged(nameof(BoneChromes));
        OnPropertyChanged(nameof(HeatPoints));
    }

    /// <summary>
    /// Re-read which drawings the rig moves, and hand the answer to everything
    /// that gates on it.
    /// </summary>
    /// <remarks>
    /// <b>One place builds it and every consumer is given the same instance.</b>
    /// The bitmap cache uses it to decide whether a key carries the timeline
    /// position, the prewarmer to decide whether a detached render is safe, and
    /// the pose resolver to find the layer whose binding moves a drawing. Two
    /// copies that disagreed would put a posed frame under a position-free key
    /// — the walk cycle rendering as whichever pose arrived first.
    /// </remarks>
    internal void RebuildRigIndex()
    {
        var index = RigIndex.For(Doc);
        _cache.Rig = index;
        _prewarm.Rig = index;
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

}
