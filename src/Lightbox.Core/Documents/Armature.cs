using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Documents;

/// <summary>
/// One bone: a named segment in the armature's hierarchy, with its rest
/// placement relative to its parent.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="X"/>, <see cref="Y"/> and <see cref="RotationDeg"/> are the
/// <b>bind pose</b> — where the bone sits when nothing is posed, expressed in
/// the parent's frame (document coordinates for a root). The bind pose is
/// load-bearing beyond placement: it is the coordinate space dab dynamics will
/// seed from when skinning arrives, which is what keeps a rigged character
/// from boiling (<c>docs/DESIGN-bones.md</c>, "The one trap").
/// </para>
/// <para>
/// A bone's origin is placed relative to its parent's <em>origin</em>, not its
/// tip — <see cref="Length"/> is where the bone's own frame ends, not where a
/// child begins. That is the convention every rigging tool settled on, because
/// it lets a jaw hang off a skull's origin without a zero-length spacer bone.
/// </para>
/// </remarks>
public sealed class Bone
{
    public string Id { get; set; } = Ids.NewId("bone");

    public string Name { get; set; } = "Bone";

    /// <summary>The parent bone's id, or null for a root.</summary>
    public string? ParentId { get; set; }

    /// <summary>Rest length in document pixels — where the tip sits along the bone's own x axis.</summary>
    public double Length { get; set; } = 50;

    /// <summary>Rest x of the bone's origin, in the parent's frame.</summary>
    public double X { get; set; }

    /// <summary>Rest y of the bone's origin, in the parent's frame.</summary>
    public double Y { get; set; }

    /// <summary>Rest rotation relative to the parent, clockwise degrees.</summary>
    public double RotationDeg { get; set; }

    /// <summary>
    /// This bone's origin is its parent's tip, not an offset of its own —
    /// null, and absent from the file, for the ordinary unglued bone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Q86. An extruded child sets it, so re-proportioning a parent drags the
    /// whole chain after it instead of leaving a gap to close by hand. It is
    /// exactly Blender's connected bone, and the reason it is a flag rather
    /// than a convention is that the alternative — writing the parent's length
    /// into the child's <see cref="X"/> at extrude time — is a copy that goes
    /// stale the moment the parent changes.
    /// </para>
    /// <para>
    /// <see cref="X"/> and <see cref="Y"/> are ignored while it is set, rather
    /// than kept in step: one place decides where a connected child sits, and
    /// it is the solve.
    /// </para>
    /// </remarks>
    public bool? Connected { get; set; }

    /// <summary>Whether this bone is glued to its parent's tip. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsConnected => Connected == true;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Bone Clone() => (Bone)MemberwiseClone();
}

/// <summary>
/// The document's bone hierarchy and bind pose, or — in the ordinary case —
/// nothing at all: <see cref="Doc.Armature"/> is null until a rig is authored.
/// </summary>
/// <remarks>
/// <para>
/// The camera's rule, applied to rigging: authored, saved, and never a
/// mutation of a stroke. Posing moves where marks are <em>stamped</em>; the
/// stroke record stays the record (invariant 1). A document that never rigs
/// writes no key and pays no cost.
/// </para>
/// <para>
/// <see cref="Chains"/> is null on a rig that has no IK, for the same reason
/// the armature itself is null on a document that has no rig: an empty list
/// on every armature would be the "present-and-disabled" shape this record
/// exists to refuse. Constraints and spline chains are the rest of phase 3
/// and arrive the same way.
/// </para>
/// </remarks>
public sealed class Armature
{
    /// <summary>The bones. Order is authoring order; hierarchy comes from <see cref="Bone.ParentId"/>.</summary>
    public List<Bone> Bones { get; set; } = [];

    /// <summary>The IK chains, or null — and null is the ordinary rig.</summary>
    public List<IkChain>? Chains { get; set; }

    /// <summary>The bone with this id, or null.</summary>
    public Bone? BoneById(string id) => Bones.FirstOrDefault(b => b.Id == id);

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Armature Clone()
    {
        var copy = (Armature)MemberwiseClone();
        copy.Bones = Bones.Select(b => b.Clone()).ToList();
        copy.Chains = Chains?.Select(c => c.Clone()).ToList();
        return copy;
    }
}

/// <summary>
/// One inverse-kinematics chain: a run of bones ending at <see cref="TipBoneId"/>
/// that reaches for a <em>target bone</em> instead of being rotated joint by
/// joint.
/// </summary>
/// <remarks>
/// <para>
/// <b>The target is a bone, not a point</b> (Q86). It costs one extra bone per
/// limb and buys everything the rig already does for free: the target is
/// keyed by the ordinary pose track, parented to whatever the foot should
/// follow, and moved with the same drag as any other bone. A bare xy target
/// would have needed its own keying, its own parenting and its own gesture,
/// all shadowing machinery that exists.
/// </para>
/// <para>
/// <see cref="PoleBoneId"/> is the bend hint: which side the knee goes. It is
/// a hint rather than a constraint — it seeds the solve's starting shape, and
/// FABRIK stays in the basin it starts in — because a hard pole constraint on
/// a chain of arbitrary length has no closed form, and a solver that sometimes
/// cannot satisfy its own constraint is worse than one that never promised.
/// </para>
/// </remarks>
public sealed class IkChain
{
    public string Id { get; set; } = Ids.NewId("ik");

    public string Name { get; set; } = "IK";

    /// <summary>The last bone in the chain — the one whose tip reaches the target.</summary>
    public string TipBoneId { get; set; } = "";

    /// <summary>The bone whose origin the chain reaches for.</summary>
    public string TargetBoneId { get; set; } = "";

    /// <summary>How many bones, counting up from the tip, the solve is allowed to rotate.</summary>
    public int Bones { get; set; } = 2;

    /// <summary>A bone whose origin says which way the chain should bend, or null.</summary>
    public string? PoleBoneId { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public IkChain Clone() => (IkChain)MemberwiseClone();
}

/// <summary>
/// One bone's influence over a stroke: who moves it, and how much per
/// control point.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PointWeights"/> null means <b>1.0 at every point</b> — the
/// coarse assignment that covers the whole cutout workflow (each body part
/// its own layer, each stroke wholly one bone's) with a single small entry.
/// The weight brush writes per-point values only where an artist actually
/// painted a correction, so most bindings stay coarse forever.
/// </para>
/// <para>
/// Weights across a stroke's bindings are normalised at solve time, never in
/// the record: <see cref="Skinning"/> divides by the sum where it exceeds 1
/// and blends the remainder toward rest where it falls short, so hand-edited
/// JSON cannot make a point fly off.
/// </para>
/// </remarks>
public sealed class BoneBinding
{
    public string BoneId { get; set; } = "";

    /// <summary>Per-control-point influence, or null for 1.0 everywhere.</summary>
    public List<double>? PointWeights { get; set; }

    /// <summary>This binding's weight at one control point.</summary>
    public double WeightAt(int pointIndex) =>
        PointWeights is null ? 1.0
        : pointIndex >= 0 && pointIndex < PointWeights.Count ? PointWeights[pointIndex]
        : 0.0;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public BoneBinding Clone()
    {
        var copy = (BoneBinding)MemberwiseClone();
        copy.PointWeights = PointWeights is null ? null : [.. PointWeights];
        return copy;
    }
}

/// <summary>
/// One bone's departure from its rest pose: offsets, not absolutes, so an
/// empty pose is the bind pose and a bone missing from a key is at rest.
/// </summary>
public sealed class BonePose
{
    /// <summary>Rotation added to the bone's rest rotation, clockwise degrees.</summary>
    public double RotationDeg { get; set; }

    /// <summary>Translation added to the bone's rest x, in the parent's frame.</summary>
    public double X { get; set; }

    /// <summary>Translation added to the bone's rest y, in the parent's frame.</summary>
    public double Y { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public BonePose Clone() => (BonePose)MemberwiseClone();
}

/// <summary>One authored pose at a point on the timeline.</summary>
/// <remarks>
/// Bones absent from <see cref="Bones"/> are at rest on this key — the sparse
/// form matters, because a walk cycle keys legs frame by frame while the skull
/// sits at rest, and a dense key would freeze the skull into every key that
/// never meant to mention it.
/// </remarks>
public sealed class PoseKey
{
    public int Frame { get; set; }

    /// <summary>Bone id → its departure from rest on this key.</summary>
    public Dictionary<string, BonePose> Bones { get; set; } = [];

    /// <summary>
    /// How this key eases into the NEXT one — the camera's vocabulary, and the
    /// inbetweener's, so a pose inbetween and a camera move read the same way.
    /// </summary>
    public Easing Ease { get; set; } = Easing.EaseInOut;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public PoseKey Clone()
    {
        var copy = (PoseKey)MemberwiseClone();
        copy.Bones = Bones.ToDictionary(e => e.Key, e => e.Value.Clone());
        return copy;
    }
}

/// <summary>
/// The keyframed poses over the timeline, or null — and null is the default,
/// exactly like <see cref="Scene.Camera"/>. A document that never poses its
/// rig writes no key.
/// </summary>
public sealed class PoseTrack
{
    /// <summary>Authored poses. Order is not guaranteed; read through <see cref="ArmatureOps"/>.</summary>
    public List<PoseKey> Keys { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public PoseTrack Clone()
    {
        var copy = (PoseTrack)MemberwiseClone();
        copy.Keys = Keys.Select(k => k.Clone()).ToList();
        return copy;
    }
}

/// <summary>A bone's placement in document coordinates, after FK.</summary>
public readonly record struct BonePlacement(double X, double Y, double RotationDeg)
{
    /// <summary>Where a bone of this length ends, in document coordinates.</summary>
    public (double X, double Y) Tip(double length)
    {
        var r = RotationDeg * Math.PI / 180.0;
        return (X + Math.Cos(r) * length, Y + Math.Sin(r) * length);
    }
}

/// <summary>
/// Evaluation over the armature record: pose interpolation and the FK solve.
/// </summary>
/// <remarks>
/// Everything here is deterministic by construction — doubles, a fixed
/// evaluation order, no state but the arguments — because the pose track is
/// replayed on load and read by the inbetweener, and two solves of the same
/// pose must agree to the bit (<c>docs/DESIGN-bones.md</c>, "Determinism
/// rules").
/// </remarks>
public static class ArmatureOps
{
    /// <summary>Keys in timeline order. The stored list is not kept sorted by callers.</summary>
    public static IReadOnlyList<PoseKey> Ordered(PoseTrack? track) =>
        track is null ? [] : track.Keys.OrderBy(k => k.Frame).ToList();

    /// <summary>The pose key exactly on a frame, if there is one.</summary>
    public static PoseKey? KeyAt(PoseTrack? track, int frame) =>
        track?.Keys.FirstOrDefault(k => k.Frame == frame);

    /// <summary>
    /// The interpolated pose at a frame: bone id → departure from rest.
    /// Bones at rest are absent from the result, as they are from a key.
    /// </summary>
    /// <remarks>
    /// Outside the authored range the pose holds rather than extrapolating —
    /// the camera's semantics, for the camera's reason. Between keys, a bone
    /// named by either side interpolates; naming a bone in neither key means
    /// rest on both, so nothing to write.
    /// </remarks>
    public static Dictionary<string, BonePose> PoseAt(PoseTrack? track, int frame)
    {
        var keys = Ordered(track);
        if (keys.Count == 0) return [];
        if (frame <= keys[0].Frame) return CopyPoses(keys[0]);
        if (frame >= keys[^1].Frame) return CopyPoses(keys[^1]);

        for (var i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (frame < a.Frame || frame > b.Frame) continue;

            var span = b.Frame - a.Frame;
            var t = span <= 0 ? 1.0 : (frame - a.Frame) / (double)span;
            var e = EasingOps.Ease(t, a.Ease);
            var result = new Dictionary<string, BonePose>();
            // Union of both keys' bones, in a fixed order (a's then b's new
            // ones, each in insertion order) — a bone absent from one side
            // interpolates from or towards rest, which is what "sparse keys"
            // has to mean for a limb that starts moving mid-track.
            foreach (var id in a.Bones.Keys.Concat(b.Bones.Keys.Where(k => !a.Bones.ContainsKey(k))))
            {
                var pa = a.Bones.GetValueOrDefault(id) ?? Rest;
                var pb = b.Bones.GetValueOrDefault(id) ?? Rest;
                result[id] = new BonePose
                {
                    // Linear on the stored angle, like the camera: an author
                    // who writes 350 means 350.
                    RotationDeg = Lerp(pa.RotationDeg, pb.RotationDeg, e),
                    X = Lerp(pa.X, pb.X, e),
                    Y = Lerp(pa.Y, pb.Y, e),
                };
            }
            return result;
        }
        return CopyPoses(keys[^1]);
    }

    /// <summary>
    /// Every bone's placement in document coordinates, given a pose: forward
    /// kinematics, then any IK chain the rig carries. A <b>null</b> pose is
    /// the bind pose and never runs IK; an <b>empty</b> one is a pose that
    /// happens to key nothing, and does.
    /// </summary>
    /// <remarks>
    /// Each bone's placement is computed exactly once, from its parent chain —
    /// the arithmetic path per bone is fixed by the hierarchy alone, so the
    /// result is bit-identical however the list happens to be ordered. A bone
    /// whose parent id resolves to nothing solves as a root rather than
    /// throwing: a half-deleted hierarchy is a document to repair, not one
    /// that cannot render. A parent cycle is broken the same way, at the bone
    /// where the walk first bites its own tail.
    /// </remarks>
    public static Dictionary<string, BonePlacement> Solve(
        Armature armature, IReadOnlyDictionary<string, BonePose>? pose = null)
    {
        var placements = Forward(armature, pose);
        // A null pose means "the rest", and the rest is what an artist edits
        // in bind mode — so IK stays out of it, or every bind-mode drag would
        // fight the solver for the joint it just moved. Every posed caller
        // passes a dictionary (PoseAt returns an empty one, never null), so
        // this is the bind/pose line rather than a flag to remember.
        if (pose is null || armature.Chains is not { Count: > 0 } chains) return placements;

        // Chains resolve in list order, each against the placements the ones
        // before it left — so a hand chain whose target hangs off an arm chain
        // sees the arm already solved. Order in the file is therefore part of
        // the rig, which is why it is authoring order and never sorted.
        var solved = new Dictionary<string, BonePose>(pose);
        foreach (var chain in chains)
            if (ApplyChain(armature, chain, solved, placements))
                placements = Forward(armature, solved);
        return placements;
    }

    private static Dictionary<string, BonePlacement> Forward(
        Armature armature, IReadOnlyDictionary<string, BonePose>? pose)
    {
        var placements = new Dictionary<string, BonePlacement>();
        foreach (var bone in armature.Bones)
            Place(armature, bone, pose, placements, visiting: []);
        return placements;
    }

    /// <summary>How many FABRIK passes a chain gets, always, whatever it reached.</summary>
    /// <remarks>
    /// Fixed rather than converged-to-tolerance, and that is not a shortcut:
    /// a tolerance loop runs a different number of passes on inputs that
    /// differ in the last bit, so the same pose could solve two ways on two
    /// machines and an inbetween would drift from its own reload
    /// (<c>docs/DESIGN-bones.md</c>, "Determinism rules"). Twenty is well past
    /// visual convergence for the chain lengths a character has.
    /// </remarks>
    public const int IkIterations = 20;

    /// <summary>
    /// Solve one chain, writing the rotations it wants into <paramref name="pose"/>.
    /// Returns whether it changed anything.
    /// </summary>
    private static bool ApplyChain(
        Armature armature, IkChain chain,
        Dictionary<string, BonePose> pose,
        Dictionary<string, BonePlacement> placements)
    {
        var bones = ChainBones(armature, chain);
        if (bones.Count == 0) return false;
        if (armature.BoneById(chain.TargetBoneId) is not { } targetBone) return false;
        if (!placements.TryGetValue(targetBone.Id, out var target)) return false;
        // A target inside the chain would move because the chain moved, and
        // the chain would move because the target did. Refuse rather than
        // iterate a circle: an unsolvable rig should sit still, not vibrate.
        if (bones.Any(b => b.Id == targetBone.Id) || IsDescendantOfAny(armature, targetBone, bones)) return false;

        // Joints, root first: each bone's origin, then the last bone's tip.
        var joints = new (double X, double Y)[bones.Count + 1];
        for (var i = 0; i < bones.Count; i++)
            joints[i] = (placements[bones[i].Id].X, placements[bones[i].Id].Y);
        joints[^1] = placements[bones[^1].Id].Tip(bones[^1].Length);

        var lengths = bones.Select(b => b.Length).ToArray();
        if (chain.PoleBoneId is { } poleId && placements.TryGetValue(poleId, out var pole))
            Prebend(joints, pole);
        Fabrik(joints, lengths, (target.X, target.Y));

        // Positions back to rotations. Each chain bone's parent world rotation
        // is the previous chain bone's NEW one, so no re-solve is needed
        // between them — only the chain root reads its parent from the FK pass.
        var parentWorld = bones[0].ParentId is { } pid && placements.TryGetValue(pid, out var pp)
            ? pp.RotationDeg
            : 0.0;
        for (var i = 0; i < bones.Count; i++)
        {
            var world = Math.Atan2(joints[i + 1].Y - joints[i].Y, joints[i + 1].X - joints[i].X) * 180.0 / Math.PI;
            var existing = pose.GetValueOrDefault(bones[i].Id);
            pose[bones[i].Id] = new BonePose
            {
                RotationDeg = world - parentWorld - bones[i].RotationDeg,
                // A pose translation is the artist's and IK has no opinion on
                // it — only the angle is the solver's to write.
                X = existing?.X ?? 0,
                Y = existing?.Y ?? 0,
            };
            parentWorld = world;
        }
        return true;
    }

    /// <summary>The chain's bones, root-most first, walking up from its tip.</summary>
    private static List<Bone> ChainBones(Armature armature, IkChain chain)
    {
        var count = Math.Max(1, chain.Bones);
        var walk = armature.BoneById(chain.TipBoneId);
        var bones = new List<Bone>();
        var seen = new HashSet<string>();
        while (walk is not null && bones.Count < count && seen.Add(walk.Id))
        {
            bones.Add(walk);
            walk = walk.ParentId is { } p ? armature.BoneById(p) : null;
        }
        bones.Reverse();
        // A zero-length bone has no direction, so a chain containing one has
        // no shape to solve for.
        return bones.Any(b => b.Length <= 0) ? [] : bones;
    }

    private static bool IsDescendantOfAny(Armature armature, Bone bone, List<Bone> ancestors)
    {
        var seen = new HashSet<string>();
        for (var walk = bone.ParentId; walk is not null && seen.Add(walk);)
        {
            if (ancestors.Any(a => a.Id == walk)) return true;
            walk = armature.BoneById(walk)?.ParentId;
        }
        return false;
    }

    /// <summary>
    /// Lean the interior joints toward the pole before solving, so the chain
    /// resolves on that side. FABRIK does not leave the basin it starts in,
    /// which is what makes a seed enough to decide a knee.
    /// </summary>
    private static void Prebend((double X, double Y)[] joints, BonePlacement pole)
    {
        for (var i = 1; i < joints.Length - 1; i++)
            joints[i] = (joints[i].X + (pole.X - joints[i].X) * 0.5,
                         joints[i].Y + (pole.Y - joints[i].Y) * 0.5);
    }

    /// <summary>
    /// FABRIK — forward and backward reaching, in place, a fixed number of
    /// times. Published and patent-free (Aristidou &amp; Lasenby, 2011).
    /// </summary>
    private static void Fabrik((double X, double Y)[] joints, double[] lengths, (double X, double Y) target)
    {
        var root = joints[0];
        var reach = lengths.Sum();
        var span = Dist(root, target);
        if (span > reach)
        {
            // Out of reach: straight at it. Iterating instead would creep
            // toward the same answer and cost ten passes to get there.
            for (var i = 1; i < joints.Length; i++)
                joints[i] = Along(root, target, lengths.Take(i).Sum());
            return;
        }

        for (var pass = 0; pass < IkIterations; pass++)
        {
            joints[^1] = target;
            for (var i = joints.Length - 2; i >= 0; i--)
                joints[i] = Along(joints[i + 1], joints[i], lengths[i]);

            joints[0] = root;
            for (var i = 1; i < joints.Length; i++)
                joints[i] = Along(joints[i - 1], joints[i], lengths[i - 1]);
        }
    }

    /// <summary>A point <paramref name="d"/> from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    private static (double X, double Y) Along((double X, double Y) from, (double X, double Y) to, double d)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var len = Math.Sqrt(dx * dx + dy * dy);
        // Coincident points have no direction; +x is as good as any and, being
        // fixed, keeps the solve reproducible.
        if (len < 1e-12) return (from.X + d, from.Y);
        return (from.X + dx / len * d, from.Y + dy / len * d);
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static BonePlacement Place(
        Armature armature,
        Bone bone,
        IReadOnlyDictionary<string, BonePose>? pose,
        Dictionary<string, BonePlacement> placements,
        HashSet<string> visiting)
    {
        if (placements.TryGetValue(bone.Id, out var done)) return done;

        var parent = bone.ParentId is null || !visiting.Add(bone.Id)
            ? (BonePlacement?)null
            : armature.BoneById(bone.ParentId) is { } p
                ? Place(armature, p, pose, placements, visiting)
                : null;

        var delta = pose?.GetValueOrDefault(bone.Id) ?? Rest;
        // A connected bone's origin IS the parent's tip, so its own offset is
        // not read at all — the parent's length decides, every time it is
        // asked. A pose translation still applies on top, because posing a
        // joint apart from its parent is a legitimate thing to key even on a
        // glued chain.
        var (ox, oy) = bone.IsConnected && bone.ParentId is not null
                       && armature.BoneById(bone.ParentId) is { } glued
            ? (glued.Length, 0.0)
            : (bone.X, bone.Y);
        var local = new BonePlacement(ox + delta.X, oy + delta.Y, bone.RotationDeg + delta.RotationDeg);

        var world = parent is { } w ? Compose(w, local) : local;
        placements[bone.Id] = world;
        return world;
    }

    /// <summary>A child placement expressed in its parent's frame, taken to document coordinates.</summary>
    private static BonePlacement Compose(BonePlacement parent, BonePlacement local)
    {
        var r = parent.RotationDeg * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        return new BonePlacement(
            parent.X + cos * local.X - sin * local.Y,
            parent.Y + sin * local.X + cos * local.Y,
            parent.RotationDeg + local.RotationDeg);
    }

    private static readonly BonePose Rest = new();

    private static Dictionary<string, BonePose> CopyPoses(PoseKey key) =>
        key.Bones.ToDictionary(e => e.Key, e => e.Value.Clone());

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
