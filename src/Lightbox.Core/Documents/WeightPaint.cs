namespace Lightbox.Core.Documents;

/// <summary>What a weight-brush dab does to the influence under it.</summary>
public enum WeightBrushMode
{
    Add,
    Subtract,

    /// <summary>Average toward the neighbours along the stroke — the armpit fixer.</summary>
    Smooth,
}

/// <summary>
/// The weight brush's arithmetic: what one dab of the brush does to a
/// stroke's bindings. Pure record edits — the gesture, the pressure curve
/// and the undo step live in the app; every rule an artist would call "how
/// weights behave" lives here, where it can be tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Blender's model, because every alternative makes artists do
/// arithmetic</b> (owner's decision, 2026-08-13): weights at a point sum to
/// at most 1, painting one bone up takes the <em>unlocked</em> others down
/// proportionally, and a locked bone never moves. A sum below 1 is legal and
/// meaningful — the remainder holds the point at rest
/// (<see cref="Skinning.Blend"/>), which is also what lets Subtract simply
/// subtract.
/// </para>
/// <para>
/// Painting happens against the bind pose (Q81): the dab lands on the
/// stroke's own <see cref="Stroke.Points"/>, which <em>are</em> the rest
/// positions, because posing never mutates the record.
/// </para>
/// </remarks>
public static class WeightPaint
{
    /// <summary>
    /// Apply one dab of the weight brush to a stroke. Returns true when any
    /// weight changed — the caller's cue to have pushed an undo step.
    /// </summary>
    /// <param name="strength">
    /// 0..1, pressure already applied by the caller — the brush rides the
    /// same input stack every other brush does.
    /// </param>
    /// <param name="lockedBones">Bone ids whose influence must not move.</param>
    /// <param name="hitPoints">
    /// Where each control point should be hit-tested — the posed positions,
    /// when the artist is painting on a posed drawing — one per
    /// <see cref="Stroke.Points"/> entry. Null (or a count that does not
    /// match, which means the caller's pose is stale) falls back to the rest
    /// positions. The <em>weights</em> land on the same indices either way:
    /// the record never changes shape, only where the brush finds it.
    /// </param>
    /// <param name="fallback">
    /// What binds this stroke while it carries no weights of its own — the
    /// layer's binding, exactly as the render resolves it. The first dab
    /// materialises it into the record before applying, so painting is a
    /// <em>correction</em> of the motion the artist was looking at. Without
    /// this the stroke's own weights started from zero everywhere, which
    /// dropped every unpainted point from "following the layer's bone" to
    /// "held at rest" — the holes that strand parts of the ink at the origin.
    /// </param>
    public static bool Apply(
        Stroke stroke, string boneId, double cx, double cy, double radius,
        double strength, WeightBrushMode mode, IReadOnlySet<string>? lockedBones = null,
        IReadOnlyList<StrokePoint>? hitPoints = null,
        IReadOnlyList<BoneBinding>? fallback = null)
    {
        if (radius <= 0 || strength <= 0 || stroke.Points.Count == 0) return false;
        if (lockedBones?.Contains(boneId) == true) return false;
        if (hitPoints is not null && hitPoints.Count != stroke.Points.Count) hitPoints = null;

        var seeded = false;
        if (stroke.Weights is not { Count: > 0 } && fallback is { Count: > 0 })
        {
            stroke.Weights = fallback.Select(b => b.Clone()).ToList();
            seeded = true;
        }

        var changed = false;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var p = hitPoints?[i] ?? stroke.Points[i];
            var dx = p.X - cx;
            var dy = p.Y - cy;
            var d2 = dx * dx + dy * dy;
            if (d2 > radius * radius) continue;

            // The soft falloff every weight brush uses: full effect at the
            // centre, nothing at the rim, smooth in between.
            var t = 1 - Math.Sqrt(d2) / radius;
            var effect = strength * t * t * (3 - 2 * t);
            if (effect <= 0) continue;

            changed |= ApplyAtPoint(stroke, boneId, i, effect, mode, lockedBones);
        }

        if (changed) Prune(stroke);
        // A dab that hit nothing must leave no trace: the seed only becomes
        // the record when the gesture actually painted this stroke.
        else if (seeded) stroke.Weights = null;
        return changed;
    }

    private static bool ApplyAtPoint(
        Stroke stroke, string boneId, int index, double effect, WeightBrushMode mode,
        IReadOnlySet<string>? lockedBones)
    {
        var bindings = stroke.Weights ??= [];
        var target = bindings.FirstOrDefault(b => b.BoneId == boneId);

        var current = target?.WeightAt(index) ?? 0;
        var wanted = mode switch
        {
            WeightBrushMode.Add => Math.Min(1, current + effect),
            WeightBrushMode.Subtract => Math.Max(0, current - effect),
            _ => Lerp(current, NeighbourAverage(target, index, stroke.Points.Count), effect),
        };
        if (Math.Abs(wanted - current) < 1e-12) return false;

        if (target is null)
        {
            // First touch of this bone on this stroke: an all-zero per-point
            // binding to write into. Never a uniform one — a dab is local.
            target = new BoneBinding { BoneId = boneId, PointWeights = Zeros(stroke.Points.Count) };
            bindings.Add(target);
        }
        Materialise(target, stroke.Points.Count);
        target.PointWeights![index] = wanted;

        // Auto-normalisation: if the point now carries more than 1, the
        // excess comes out of the unlocked other bones, proportionally.
        // Locked bones never move, so what they hold is a floor — and if
        // locked plus painted exceeds 1, the painted bone gives the rest
        // back, which is the only resolution that honours the lock.
        var sum = 0.0;
        var unlockedOthers = 0.0;
        foreach (var b in bindings)
        {
            var w = b.WeightAt(index);
            sum += w;
            if (b != target && lockedBones?.Contains(b.BoneId) != true) unlockedOthers += w;
        }
        if (sum > 1)
        {
            var excess = sum - 1;
            if (unlockedOthers > 0)
            {
                var scale = Math.Max(0, unlockedOthers - excess) / unlockedOthers;
                foreach (var b in bindings)
                {
                    if (b == target || lockedBones?.Contains(b.BoneId) == true) continue;
                    Materialise(b, stroke.Points.Count);
                    b.PointWeights![index] *= scale;
                }
                excess = Math.Max(0, excess - unlockedOthers);
            }
            if (excess > 0) target.PointWeights[index] = Math.Max(0, wanted - excess);
        }
        return true;
    }

    /// <summary>
    /// The bone paired with this one for X-symmetry, by name: <c>hip.l</c> ↔
    /// <c>hip.r</c> (also <c>.L/.R</c> and <c>_l/_r</c>). Null when the name
    /// declares no side or the other side does not exist — Blender's
    /// convention, chosen over a canvas axis because a character is almost
    /// never drawn dead-centre (Q81).
    /// </summary>
    public static Bone? MirroredBone(Armature armature, string boneId)
    {
        var bone = armature.BoneById(boneId);
        if (bone is null) return null;
        foreach (var (left, right) in Suffixes)
        {
            var other =
                bone.Name.EndsWith(left, StringComparison.Ordinal) ? bone.Name[..^left.Length] + right
                : bone.Name.EndsWith(right, StringComparison.Ordinal) ? bone.Name[..^right.Length] + left
                : null;
            if (other is null) continue;
            var match = armature.Bones.FirstOrDefault(b => b.Name == other);
            if (match is not null) return match;
        }
        return null;
    }

    /// <summary>
    /// Reflect a canvas point across the symmetry axis of a bone pair: the
    /// perpendicular bisector of the two bind-pose origins. Derived from the
    /// rig rather than the paper, so an off-centre character mirrors about
    /// its own spine. Null when the pair sits on one spot and defines no
    /// axis.
    /// </summary>
    public static (double X, double Y)? Mirror(Armature armature, Bone a, Bone b, double x, double y)
    {
        var bind = ArmatureOps.Solve(armature);
        var pa = bind[a.Id];
        var pb = bind[b.Id];
        var nx = pb.X - pa.X;
        var ny = pb.Y - pa.Y;
        var len2 = nx * nx + ny * ny;
        if (len2 < 1e-12) return null;

        // Reflect across the line through the midpoint, normal to a→b.
        var mx = (pa.X + pb.X) / 2;
        var my = (pa.Y + pb.Y) / 2;
        var dot = ((x - mx) * nx + (y - my) * ny) / len2;
        return (x - 2 * dot * nx, y - 2 * dot * ny);
    }

    /// <summary>
    /// Where the mirrored dab lands when the drawing on screen is <em>posed</em>:
    /// ride the painted bone's own motion back to rest, mirror across the
    /// pair's bind axis there — the axis is a rest-pose fact (Q81) — and ride
    /// the paired bone's motion out again. Under an identity pose every delta
    /// is the identity and this is exactly <see cref="Mirror"/>.
    /// </summary>
    /// <remarks>
    /// The trip to rest uses the painted bone's rigid delta as the inverse of
    /// the deformation. That is exact wherever the painted bone owns the dab's
    /// neighbourhood — which is where an artist aims a weight brush — and an
    /// approximation in blended regions, where the true inverse differs per
    /// point. The approximation only moves <em>where the mirrored brush
    /// lands</em>, never what a weight means, and the falloff forgives being a
    /// few pixels off the way it forgives an imperfect hand.
    /// </remarks>
    public static (double X, double Y)? MirrorPosed(
        Armature armature, Bone own, Bone pair,
        IReadOnlyDictionary<string, RigidDelta> deltas, double x, double y)
    {
        var (rx, ry) = deltas.GetValueOrDefault(own.Id, RigidDelta.Identity).Unapply(x, y);
        if (Mirror(armature, own, pair, rx, ry) is not { } m) return null;
        return deltas.GetValueOrDefault(pair.Id, RigidDelta.Identity).Apply(m.X, m.Y);
    }

    private static readonly (string Left, string Right)[] Suffixes =
        [(".l", ".r"), (".L", ".R"), ("_l", "_r"), ("_L", "_R")];

    private static double NeighbourAverage(BoneBinding? binding, int index, int count)
    {
        if (binding is null) return 0;
        double sum = 0;
        var n = 0;
        for (var i = Math.Max(0, index - 1); i <= Math.Min(count - 1, index + 1); i++)
        {
            if (i == index) continue;
            sum += binding.WeightAt(i);
            n++;
        }
        return n == 0 ? binding.WeightAt(index) : sum / n;
    }

    /// <summary>A coarse (uniform) binding becomes per-point on first touch.</summary>
    private static void Materialise(BoneBinding binding, int count)
    {
        if (binding.PointWeights is { Count: var c } && c >= count) return;
        var w = new List<double>(count);
        for (var i = 0; i < count; i++) w.Add(binding.WeightAt(i));
        binding.PointWeights = w;
    }

    /// <summary>Bindings painted down to nothing leave the record entirely.</summary>
    private static void Prune(Stroke stroke)
    {
        if (stroke.Weights is not { } bindings) return;
        bindings.RemoveAll(b => b.PointWeights is { } w && w.All(v => v <= 0));
        if (bindings.Count == 0) stroke.Weights = null;
    }

    private static List<double> Zeros(int count)
    {
        var w = new List<double>(count);
        for (var i = 0; i < count; i++) w.Add(0);
        return w;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
