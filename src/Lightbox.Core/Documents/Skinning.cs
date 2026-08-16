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
    /// <param name="fallback">
    /// What binds this stroke when it carries no weights of its own — the
    /// layer's binding (Q90). Null for the per-stroke-only behaviour.
    /// </param>
    /// <remarks>
    /// <b>The fallback is read, never written.</b> A stroke on a rigged layer
    /// stays exactly as the artist drew it in the record; only what is
    /// <em>stamped</em> moves. That is the same rule the pose track already
    /// follows, and it is what makes linking a layer retroactive — the
    /// drawings that were there before the link move too — and free of a
    /// per-stroke key on two hundred frames of work.
    /// </remarks>
    /// <param name="correction">
    /// Rest-space offsets from a corrective (Q100), one per control point, or
    /// null. Applied to the rest shape <b>before</b> skinning, which is what
    /// makes a drawn fix compose with the pose, IK, splines and constraints
    /// without any of them knowing correctives exist.
    /// </param>
    public static Stroke PoseStroke(
        Stroke stroke, Armature armature, IReadOnlyDictionary<string, BonePose>? pose,
        IReadOnlyList<BoneBinding>? fallback = null,
        IReadOnlyList<PointOffset>? correction = null)
    {
        var bindings = stroke.Weights is { Count: > 0 } own ? own : fallback;
        if (bindings is not { Count: > 0 } || stroke.Points.Count == 0)
            return stroke;

        var deltas = Deltas(armature, pose);
        var source = Corrected(stroke.Points, correction);
        var (rest, weights) = DensifyWithWeights(source, bindings);

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
    /// <param name="layerBone">
    /// The layer's binding (Q90), for the strokes that carry none of their own
    /// — null when the layer is not rigged, the empty string for the whole
    /// skeleton, otherwise a bone id.
    /// </param>
    /// <remarks>
    /// A bake on a rigged layer is a bake, not an unlink: the strokes come out
    /// ordinary and the LAYER keeps its binding, so drawings made on it
    /// afterwards still follow the rig. Baking is what freezes a drawing, and
    /// unrigging the layer is a separate thing an artist asks for separately.
    /// </remarks>
    public static int BakeFrame(
        Frame frame, Armature armature, IReadOnlyDictionary<string, BonePose>? pose,
        string? layerBone = null)
    {
        var named = layerBone is { Length: > 0 }
            ? new List<BoneBinding> { new() { BoneId = layerBone } }
            : null;
        // Baked with the correctives in force, or a freeze would give back the
        // collapsed joint the artist drew the fix to cure — the live view and
        // the baked result have to be the same picture.
        var corrections = CorrectiveOps.Resolve(frame.Correctives, pose);
        var baked = 0;
        for (var i = 0; i < frame.Strokes.Count; i++)
        {
            var stroke = frame.Strokes[i];
            var own = stroke.Weights is { Count: > 0 };
            if (!own && (layerBone is null || !TakesLayerBinding(stroke))) continue;
            var fallback = own ? null : named ?? AutoBoundFor(stroke, armature);
            var posed = PoseStroke(
                stroke, armature, pose, fallback, corrections.GetValueOrDefault(stroke.Id));
            if (ReferenceEquals(posed, stroke)) continue;
            frame.Strokes[i] = posed;
            baked++;
        }
        return baked;
    }

    /// <summary>
    /// The frame as a render should see it at a timeline position: bound
    /// strokes posed, everything else untouched — or the frame itself,
    /// uncloned, when nothing here is bound. This is the live render's whole
    /// entry point, and it is <em>the same construction bake writes</em>,
    /// which is what keeps live and baked pixels bit-identical.
    /// </summary>
    /// <remarks>
    /// A transient: same id, never serialized, rebuilt on every cache miss.
    /// The document is not touched — invariant 1 — and a caller that renders
    /// the result instead of the original pays only on frames that actually
    /// bind.
    /// </remarks>
    /// <param name="poseOverride">
    /// A pose to render instead of the track's — the pose-drag preview's
    /// provisional key, merged over the playhead's pose by the caller. Going
    /// through this same entry point is what keeps the preview exact: the
    /// pixels mid-drag are the pixels the release lands (Q81 decision 5).
    /// </param>
    /// <param name="ghostOverBudget">
    /// Q81 decision 5's degrade: strokes whose brush is badged
    /// <see cref="BrushCost.Expressive"/> — a simulated medium, a canvas
    /// reader, a layer sampler — render as a thin centreline ghost during the
    /// drag and land exactly on release. Only those: the badge is what the
    /// picker already shows, so the trade the artist accepted is the trade
    /// the drag makes.
    /// </param>
    public static Frame PoseFrameForRender(
        Doc doc, Frame frame, int frameIndex, RigIndex? rig = null,
        IReadOnlyDictionary<string, BonePose>? poseOverride = null,
        bool ghostOverBudget = false)
    {
        var index = rig ?? RigIndex.Empty;
        if (doc.Armature is not { Bones.Count: > 0 } armature || !index.IsPosed(frame))
            return frame;

        var pose = poseOverride ?? ArmatureOps.PoseAt(doc.Scene.PoseTrack, frameIndex);
        // Resolved once for the drawing rather than per stroke: the stops are
        // sorted and bracketed each time, and doing that per line would make a
        // corrective's cost scale with the drawing instead of with itself.
        var corrections = CorrectiveOps.Resolve(frame.Correctives, pose);
        // The layer's binding, resolved ONCE for the frame rather than per
        // stroke: a whole cutout limb is one binding, and building it four
        // hundred times for four hundred lines would be the cost that makes a
        // rigged layer feel slower than a rigged stroke.
        var layerBone = index.LayerOf(frame) is { } layer ? doc.Scene.RiggedBoneOf(layer) : null;
        var named = layerBone is { Length: > 0 }
            ? new List<BoneBinding> { new() { BoneId = layerBone } }
            : null;

        var copy = frame.Clone();
        for (var i = 0; i < frame.Strokes.Count; i++)
        {
            var stroke = frame.Strokes[i];
            var own = stroke.Weights is { Count: > 0 };
            if (!own && (layerBone is null || !TakesLayerBinding(stroke))) continue;
            // "The whole skeleton" has no single answer to share, so it is
            // auto-bound per stroke — the same arithmetic the Auto-bind button
            // does, on a copy, so the record still carries no weights. It runs
            // on the cache's miss path beside a full rasterization of the same
            // frame, which is where its cost belongs.
            var fallback = own ? null : named ?? AutoBoundFor(stroke, armature);
            var posed = PoseStroke(
                stroke, armature, pose, fallback, corrections.GetValueOrDefault(stroke.Id));
            if (ghostOverBudget && BrushCostOf.Settings(stroke.Brush) == BrushCost.Expressive)
                GhostCentreline(posed);
            copy.Strokes[i] = posed;
        }
        return copy;
    }

    /// <summary>
    /// Re-brush a posed transient as its own thin centreline: same colour,
    /// same path, none of the passes that blow the frame budget. Mutates the
    /// posed copy only — <see cref="PoseStroke"/> returned a fresh stroke,
    /// and the brush written here is a fresh clone, so the record's own
    /// settings are never touched.
    /// </summary>
    private static void GhostCentreline(Stroke posed)
    {
        var ghost = posed.Brush.Clone();
        ghost.Kind = BrushKind.Paint;
        ghost.Medium = new MediumSettings();
        ghost.SampleSource = SampleSource.ThisLayer;
        ghost.WetEdge = 0;
        ghost.Granulation = 0;
        ghost.Size = Math.Min(ghost.Size, 2.5);
        // Half strength, so the ghost reads as provisional rather than as the
        // mark suddenly thinning.
        ghost.Opacity = Math.Min(ghost.Opacity, 0.5);
        posed.Brush = ghost;
    }


    /// <summary>
    /// Whether a layer's binding should move this stroke — it carries no
    /// weights of its own, and it has not already been posed.
    /// </summary>
    /// <remarks>
    /// <b>The second half is what stops a bake from being applied twice.</b>
    /// Baking replaces a stroke with its posed self and clears its weights, so
    /// on a rigged LAYER a baked stroke would otherwise look exactly like an
    /// unbaked one and be swung again on the next render — the drawing walking
    /// further from the rig every time somebody froze it.
    /// <para>
    /// <see cref="Stroke.RestPoints"/> is the honest marker rather than a
    /// coincidence being exploited: it is set by <see cref="PoseStroke"/> and
    /// by nothing else, so "has a rest path" and "has been posed" are the same
    /// statement. A stroke an artist drew has none.
    /// </para>
    /// </remarks>
    private static bool TakesLayerBinding(Stroke stroke) =>
        stroke.Weights is not { Count: > 0 } && stroke.RestPoints is null;

    /// <summary>
    /// The stroke's control points with a corrective applied, or the points
    /// themselves when nothing corrects them.
    /// </summary>
    /// <remarks>
    /// <b>Matched by count, and dropped outright when it does not match.</b> A
    /// stroke that has been re-shaped since the fix was drawn no longer
    /// describes the same points, and sliding the offsets onto whichever
    /// points happen to line up would move the wrong part of the line — a
    /// wrong drawing rather than an uncorrected one. The list is returned
    /// uncloned when there is nothing to do, so an ordinary drawing allocates
    /// nothing.
    /// </remarks>
    private static List<StrokePoint> Corrected(
        List<StrokePoint> points, IReadOnlyList<PointOffset>? correction)
    {
        if (correction is not { Count: > 0 } || correction.Count != points.Count) return points;

        var moved = new List<StrokePoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            moved.Add(p with { X = p.X + correction[i].X, Y = p.Y + correction[i].Y });
        }
        return moved;
    }

    /// <summary>Weights for a stroke that follows the whole skeleton, without touching it.</summary>
    private static IReadOnlyList<BoneBinding>? AutoBoundFor(Stroke stroke, Armature armature)
    {
        var scratch = stroke.Clone(newId: false);
        AutoBind(scratch, armature);
        return scratch.Weights;
    }

    /// <summary>
    /// The stroke's control points, posed — no densification, one output per
    /// input.
    /// </summary>
    /// <remarks>
    /// <b>This is the authoring path, not the drawing path.</b> Rendering
    /// densifies first so a joint bends along the curve rather than along a
    /// chord; capturing a corrective must not, because the artist edits
    /// control points and the offsets are stored against them. One in, one
    /// out is what makes the diff at the end of a capture line up.
    /// </remarks>
    public static List<StrokePoint> PoseControlPoints(
        Stroke stroke, Armature armature, IReadOnlyDictionary<string, BonePose>? pose,
        IReadOnlyList<BoneBinding>? fallback = null,
        IReadOnlyList<PointOffset>? correction = null)
    {
        var bindings = stroke.Weights is { Count: > 0 } own ? own : fallback;
        var source = Corrected(stroke.Points, correction);
        if (bindings is not { Count: > 0 }) return [.. source];

        var deltas = Deltas(armature, pose);
        var posed = new List<StrokePoint>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var weights = new double[bindings.Count];
            for (var b = 0; b < bindings.Count; b++) weights[b] = bindings[b].WeightAt(i);
            posed.Add(Blend(source[i], bindings, weights, deltas));
        }
        return posed;
    }

    /// <summary>
    /// Turn a displacement the artist made in POSED space into the rest-space
    /// offset that would produce it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Linear-blend skinning is <b>affine in the rest point</b>: posed =
    /// (Σ wᵢRᵢ)·rest + Σ wᵢtᵢ. So each point has a 2×2 linear part M, and the
    /// rest offset that lands a point where the artist dragged it is
    /// M⁻¹·(what they dragged).
    /// </para>
    /// <para>
    /// M is <b>probed rather than rebuilt</b>: pose the stroke once with every
    /// point nudged by (1,0) and once by (0,1), and the two displacements are
    /// M's columns exactly. That is three poses instead of a second
    /// implementation of the blend that could drift from the first — the same
    /// reason live and bake share one construction.
    /// </para>
    /// <para>
    /// A singular M — every bone's weight zero at that point, so nothing moves
    /// it — gives back the displacement unchanged rather than dividing by
    /// zero: an unbound point is in rest space already.
    /// </para>
    /// </remarks>
    public static PointOffset[] RestOffsetsFor(
        Stroke stroke, Armature armature, IReadOnlyDictionary<string, BonePose>? pose,
        IReadOnlyList<StrokePoint> posedTarget,
        IReadOnlyList<BoneBinding>? fallback = null)
    {
        var count = stroke.Points.Count;
        var offsets = new PointOffset[count];
        if (posedTarget.Count != count) return offsets;

        var at = PoseControlPoints(stroke, armature, pose, fallback);
        var byX = PoseControlPoints(stroke, armature, pose, fallback, Uniform(count, 1, 0));
        var byY = PoseControlPoints(stroke, armature, pose, fallback, Uniform(count, 0, 1));

        for (var i = 0; i < count; i++)
        {
            // M's columns, read off the probes.
            var (a, c) = (byX[i].X - at[i].X, byX[i].Y - at[i].Y);
            var (b, d) = (byY[i].X - at[i].X, byY[i].Y - at[i].Y);
            var (dx, dy) = (posedTarget[i].X - at[i].X, posedTarget[i].Y - at[i].Y);

            var det = a * d - b * c;
            offsets[i] = Math.Abs(det) < 1e-12
                ? new PointOffset(dx, dy)
                : new PointOffset((d * dx - b * dy) / det, (a * dy - c * dx) / det);
        }
        return offsets;
    }

    private static PointOffset[] Uniform(int count, double x, double y)
    {
        var offsets = new PointOffset[count];
        Array.Fill(offsets, new PointOffset(x, y));
        return offsets;
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
        List<StrokePoint> points, IReadOnlyList<BoneBinding> bindings)
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
