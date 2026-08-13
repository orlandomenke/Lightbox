using Lightbox.Core.Geometry;

namespace Lightbox.Core.Documents;

/// <summary>
/// A rigid map from bind space to posed space: rotation then translation,
/// held as the cosine/sine pair so applying it is four multiplies and no
/// trigonometry — the same arithmetic every time, which is what bit-identical
/// solves are made of.
/// </summary>
public readonly record struct RigidDelta(double Cos, double Sin, double Tx, double Ty)
{
    public static readonly RigidDelta Identity = new(1, 0, 0, 0);

    public (double X, double Y) Apply(double x, double y) =>
        (Cos * x - Sin * y + Tx, Sin * x + Cos * y + Ty);
}

/// <summary>
/// Binding strokes to bones and posing them: the phase 2 core of
/// <c>docs/DESIGN-bones.md</c>. Deforms the <b>control points of strokes</b>
/// and leaves re-stamping to <c>BrushEngine</c> — a bent arm is re-drawn, not
/// warped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here mutates a stroke that is in a document.</b>
/// <see cref="PoseStroke"/> returns a <em>new</em> stroke whose
/// <see cref="Stroke.Points"/> are posed and whose
/// <see cref="Stroke.RestPoints"/> carry the bind-pose path for seeding; the
/// original record is untouched, which is invariant 1 holding by
/// construction. Live posing renders these transients per frame; baking
/// writes them into the record — the <em>same</em> construction, which is
/// what makes live and baked pixels bit-identical.
/// </para>
/// <para>
/// Everything is doubles in a fixed order: bindings blend in list order,
/// points solve in index order, and the FK placements come from
/// <see cref="ArmatureOps.Solve"/>, which is deterministic by the same
/// argument. No tolerance-driven loops anywhere.
/// </para>
/// </remarks>
public static class Skinning
{
    /// <summary>
    /// The per-bone map from bind space to posed space: posed placement
    /// composed with the inverse of the bind placement.
    /// </summary>
    public static Dictionary<string, RigidDelta> Deltas(
        Armature armature, IReadOnlyDictionary<string, BonePose>? pose)
    {
        var bind = ArmatureOps.Solve(armature);
        var posed = ArmatureOps.Solve(armature, pose);
        var deltas = new Dictionary<string, RigidDelta>();
        foreach (var bone in armature.Bones)
        {
            var b = bind[bone.Id];
            var p = posed[bone.Id];
            var rad = (p.RotationDeg - b.RotationDeg) * Math.PI / 180.0;
            var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
            // delta = posed ∘ bind⁻¹, so a point ON the bind placement lands
            // on the posed one: t = p − R·b.
            deltas[bone.Id] = new RigidDelta(
                cos, sin,
                p.X - (cos * b.X - sin * b.Y),
                p.Y - (sin * b.X + cos * b.Y));
        }
        return deltas;
    }

    /// <summary>
    /// Linear-blend one point. Weights above a sum of 1 are normalised;
    /// below it, the remainder holds the point at rest — so a half-bound
    /// point moves half way, and an unbound one does not move at all.
    /// </summary>
    public static StrokePoint Blend(
        StrokePoint p,
        IReadOnlyList<BoneBinding> bindings,
        IReadOnlyList<double> weights,
        IReadOnlyDictionary<string, RigidDelta> deltas)
    {
        double sum = 0, x = 0, y = 0;
        for (var i = 0; i < bindings.Count; i++)
        {
            var w = weights[i];
            if (w <= 0) continue;
            if (!deltas.TryGetValue(bindings[i].BoneId, out var d)) d = RigidDelta.Identity;
            var (px, py) = d.Apply(p.X, p.Y);
            x += w * px;
            y += w * py;
            sum += w;
        }
        if (sum <= 0) return p;
        if (sum < 1)
        {
            x += (1 - sum) * p.X;
            y += (1 - sum) * p.Y;
            sum = 1;
        }
        return p with { X = x / sum, Y = y / sum };
    }

    /// <summary>
    /// A render-ready posed copy of a bound stroke: posed
    /// <see cref="Stroke.Points"/>, the bind-pose path in
    /// <see cref="Stroke.RestPoints"/>, and no <see cref="Stroke.Weights"/> —
    /// posing it again would double the pose. Returns the stroke itself,
    /// untouched, when it is unbound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Densified first, then posed, and the order is the point.</b> The
    /// dab walk curves between sparse pen samples through
    /// <c>GeometryOps.Densify</c>; a posed stroke must bend at the same
    /// granularity or a joint would fold along a chord the artist never drew.
    /// Densifying the rest path here — through the same
    /// <c>AppendSpan</c> the engine uses — also hands the renderer a 1:1
    /// correspondence between rest and posed points, which is what lets a dab
    /// walked on the rest path be placed on the posed one exactly.
    /// </para>
    /// <para>
    /// Keeps its id on purpose: a bake replaces the stroke that was there,
    /// and the transient the live path renders <em>is</em> that stroke for
    /// the duration of a frame.
    /// </para>
    /// </remarks>
    public static Stroke PoseStroke(
        Stroke stroke, Armature armature, IReadOnlyDictionary<string, BonePose>? pose)
    {
        if (stroke.Weights is not { Count: > 0 } bindings || stroke.Points.Count == 0)
            return stroke;

        var deltas = Deltas(armature, pose);
        var (rest, weights) = DensifyWithWeights(stroke.Points, bindings);

        var posed = new List<StrokePoint>(rest.Count);
        for (var i = 0; i < rest.Count; i++)
            posed.Add(Blend(rest[i], bindings, weights[i], deltas));

        var copy = stroke.Clone(newId: false);
        copy.Points = posed;
        copy.RestPoints = [.. rest];
        copy.Weights = null;
        // A path maps points; these points just moved without it. Drop it
        // rather than let the two disagree (see Stroke.Path).
        copy.Path = null;

        if (stroke.Holes is { } holes)
        {
            // A fill's inner contours carry no per-point weights of their
            // own, so each binding contributes its stroke-wide mean — exact
            // for the coarse bindings that fills realistically get, and
            // deterministic for the rest.
            var hole = new double[bindings.Count];
            for (var i = 0; i < bindings.Count; i++) hole[i] = MeanWeight(bindings[i], stroke.Points.Count);
            copy.Holes = holes
                .Select(h => h.Select(p => Blend(p, bindings, hole, deltas)).ToList())
                .ToList();
        }
        return copy;
    }

    /// <summary>
    /// Bake the pose at a frame into the drawing: every bound stroke is
    /// replaced by its posed self, in place. After a bake the strokes are
    /// ordinary — no weights, no link to the rig — because a bake is a
    /// freeze, not a render mode.
    /// </summary>
    /// <returns>How many strokes were baked.</returns>
    public static int BakeFrame(
        Frame frame, Armature armature, IReadOnlyDictionary<string, BonePose>? pose)
    {
        var baked = 0;
        for (var i = 0; i < frame.Strokes.Count; i++)
        {
            var stroke = frame.Strokes[i];
            if (stroke.Weights is not { Count: > 0 }) continue;
            frame.Strokes[i] = PoseStroke(stroke, armature, pose);
            baked++;
        }
        return baked;
    }

    /// <summary>
    /// Bind a stroke wholly to one bone: the cutout workflow's whole gesture,
    /// and the record stays one small entry.
    /// </summary>
    public static void AssignAll(Stroke stroke, string boneId) =>
        stroke.Weights = [new BoneBinding { BoneId = boneId }];

    /// <summary>
    /// Auto-weight a stroke against the armature's bind pose: inverse-square
    /// falloff on the distance from each control point to each bone's
    /// segment, normalised per point, tiny influences dropped for sparsity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately the simple binding, not the last one. Bounded biharmonic
    /// weights are the professional-grade result and a research-sized slice
    /// of work; this covers simple rigs today and hands the weight brush
    /// something to correct, and BBW can replace it later behind this same
    /// call without the record changing shape.
    /// </para>
    /// <para>
    /// Deterministic: distances are pure geometry against the bind pose, and
    /// bones are considered in armature order.
    /// </para>
    /// </remarks>
    public static void AutoBind(Stroke stroke, Armature armature)
    {
        if (armature.Bones.Count == 0 || stroke.Points.Count == 0) return;

        var bind = ArmatureOps.Solve(armature);
        var perBone = new List<(string Id, double[] W)>();
        var sums = new double[stroke.Points.Count];

        foreach (var bone in armature.Bones)
        {
            var placement = bind[bone.Id];
            var a = new StrokePoint(placement.X, placement.Y, 1);
            var (tx, ty) = placement.Tip(bone.Length);
            var b = new StrokePoint(tx, ty, 1);
            var w = new double[stroke.Points.Count];
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                var d = GeometryOps.DistToSegment(stroke.Points[i], a, b);
                // +1 keeps the on-bone case finite and gives the falloff a
                // knee about a pixel out rather than a pole.
                w[i] = 1.0 / ((d + 1) * (d + 1));
                sums[i] += w[i];
            }
            perBone.Add((bone.Id, w));
        }

        var bindings = new List<BoneBinding>();
        foreach (var (id, w) in perBone)
        {
            var any = false;
            for (var i = 0; i < w.Length; i++)
            {
                w[i] = sums[i] > 0 ? w[i] / sums[i] : 0;
                // Influence below a hundredth is inertia, not intent — drop
                // it so a twenty-bone rig does not write twenty weights on
                // every point of every stroke.
                if (w[i] < 0.01) w[i] = 0;
                else any = true;
            }
            if (any) bindings.Add(new BoneBinding { BoneId = id, PointWeights = [.. w] });
        }
        stroke.Weights = bindings.Count > 0 ? bindings : null;
    }

    /// <summary>
    /// Densify the rest path exactly as the dab walk would, carrying the
    /// per-point weights along: inserted curve points take their weights by
    /// arc fraction between the control points either side.
    /// </summary>
    private static (IReadOnlyList<StrokePoint> Points, double[][] Weights) DensifyWithWeights(
        List<StrokePoint> points, List<BoneBinding> bindings)
    {
        double[] WeightsOf(int index)
        {
            var w = new double[bindings.Count];
            for (var b = 0; b < bindings.Count; b++) w[b] = bindings[b].WeightAt(index);
            return w;
        }

        // Same short-circuit as GeometryOps.Densify: nothing to add means the
        // caller's own points, weights straight off the record.
        var dense = GeometryOps.Densify(points);
        if (ReferenceEquals(dense, points))
        {
            var direct = new double[points.Count][];
            for (var i = 0; i < points.Count; i++) direct[i] = WeightsOf(i);
            return (dense, direct);
        }

        // Re-run the same span interpolation the engine uses, recording where
        // each control point's span ends so weights can follow the curve.
        var output = new List<StrokePoint> { points[0] };
        var weights = new List<double[]> { WeightsOf(0) };
        for (var i = 0; i < points.Count - 1; i++)
        {
            var spanStart = output.Count - 1;
            GeometryOps.AppendSpan(output, points, i, maxChord: 2.0);

            var from = WeightsOf(i);
            var to = WeightsOf(i + 1);
            // Arc fraction along the interpolated span, so a weight ramp
            // bends with the curve instead of jumping at its end.
            var total = 0.0;
            for (var k = spanStart; k + 1 < output.Count; k++)
                total += GeometryOps.Dist(output[k], output[k + 1]);
            var run = 0.0;
            for (var k = spanStart + 1; k < output.Count; k++)
            {
                run += GeometryOps.Dist(output[k - 1], output[k]);
                var t = total > 0 ? run / total : 1.0;
                var w = new double[bindings.Count];
                for (var b = 0; b < bindings.Count; b++) w[b] = from[b] + (to[b] - from[b]) * t;
                weights.Add(w);
            }
        }
        return (output, weights.ToArray());
    }

    private static double MeanWeight(BoneBinding binding, int pointCount)
    {
        if (binding.PointWeights is not { Count: > 0 } w) return 1.0;
        var sum = 0.0;
        for (var i = 0; i < pointCount; i++) sum += binding.WeightAt(i);
        return pointCount > 0 ? sum / pointCount : 0.0;
    }
}
