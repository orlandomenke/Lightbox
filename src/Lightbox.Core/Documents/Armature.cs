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
/// IK chains and constraints are phase 3 of <c>docs/DESIGN-bones.md</c> and
/// deliberately not fields yet — an empty list of constraints on every rig
/// would be the "present-and-disabled" shape this record exists to refuse.
/// They arrive nullable when they arrive at all.
/// </para>
/// </remarks>
public sealed class Armature
{
    /// <summary>The bones. Order is authoring order; hierarchy comes from <see cref="Bone.ParentId"/>.</summary>
    public List<Bone> Bones { get; set; } = [];

    /// <summary>The bone with this id, or null.</summary>
    public Bone? BoneById(string id) => Bones.FirstOrDefault(b => b.Id == id);

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Armature Clone()
    {
        var copy = (Armature)MemberwiseClone();
        copy.Bones = Bones.Select(b => b.Clone()).ToList();
        return copy;
    }
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
    /// The FK solve: every bone's placement in document coordinates, given a
    /// pose. An empty pose solves the bind pose.
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
        var placements = new Dictionary<string, BonePlacement>();
        foreach (var bone in armature.Bones)
            Place(armature, bone, pose, placements, visiting: []);
        return placements;
    }

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
        var local = new BonePlacement(bone.X + delta.X, bone.Y + delta.Y, bone.RotationDeg + delta.RotationDeg);

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
