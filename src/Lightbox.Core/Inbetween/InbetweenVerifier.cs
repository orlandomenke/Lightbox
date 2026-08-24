using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using static System.FormattableString;

namespace Lightbox.Core.Inbetween;

/// <summary>One proposed inbetween: its timing parameter and its strokes.</summary>
public sealed record CandidateInbetween(double T, IReadOnlyList<Stroke> Strokes);

/// <summary>
/// Which check refused a frame, as a value rather than as prose.
/// </summary>
/// <remarks>
/// <b>The sentence is for a person; this is for the code.</b> The repair loop
/// has to treat one fault differently from the rest — <see cref="Incoherent"/>
/// is defined against the frames beside it, so a re-ask has to carry them —
/// and deciding that by looking for a phrase inside a user-facing sentence
/// would break the first time somebody reworded a refusal. Two halves of one
/// pipeline disagreeing about what a message means is the seam B195 came
/// through, one layer up.
/// </remarks>
public enum InbetweenFault
{
    /// <summary>The frame was accepted.</summary>
    None,

    /// <summary>Its <c>t</c> is not strictly between the keys.</summary>
    Timing,

    /// <summary>Nothing that would mark: no strokes, no second point, or a dot.</summary>
    Unusable,

    /// <summary>A stroke both keys draw is missing from the answer.</summary>
    DroppedStroke,

    /// <summary>A matched stroke sits too far from where the motion puts it.</summary>
    NotBetween,

    /// <summary>A closed shape gained or lost enclosed area.</summary>
    Volume,

    /// <summary>New ink that no reveal, drag or interpretation explains.</summary>
    ForbiddenInk,

    /// <summary>
    /// It zigzags against the frames either side of it, which reads as boiling
    /// at speed. The one fault that is a property of the run rather than the
    /// frame — and therefore the one a repair cannot state without saying what
    /// the neighbours are.
    /// </summary>
    Incoherent,
}

/// <summary>
/// The verdict on one candidate frame. <see cref="Refusal"/> is null when the
/// frame is defensible; otherwise it names the stroke and the check in a
/// sentence an artist can act on, and <see cref="Fault"/> says which check in a
/// form code can branch on. <see cref="Notes"/> are diagnostics that never
/// refuse — "too close to the deterministic answer" lives here, because
/// distance from that answer is evidence about cost, never about correctness.
/// </summary>
/// <param name="MatchesDeterministic">
/// The frame is what the free engine would have drawn. Reported, never
/// thresholded (Q33) — but hoisted out of <see cref="Notes"/> into a value
/// because it is the one signal that says a model <em>added nothing</em>, and
/// something has to be able to act on it without reading prose.
/// </param>
public sealed record InbetweenJudgement(
    double T,
    string? Refusal,
    IReadOnlyList<string> Notes,
    InbetweenFault Fault = InbetweenFault.None,
    bool MatchesDeterministic = false)
{
    public bool Accepted => Refusal is null;
}

/// <summary>The verdict on a whole run of candidate inbetweens.</summary>
public sealed record InbetweenRunJudgement(IReadOnlyList<InbetweenJudgement> Frames)
{
    public int AcceptedCount => Frames.Count(f => f.Accepted);
    public bool AllAccepted => Frames.All(f => f.Accepted);
}

/// <summary>
/// Decides whether AI-proposed inbetweens are defensible, so the pipeline can
/// refuse the ones that are not. The model proposes; Lightbox disposes —
/// reliability is a property of this harness, not of any model, which is what
/// makes an untrusted bring-your-own model safe to plug in at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>It judges a run, not a frame.</b> The only check that catches a sequence
/// boiling — secondary motion invented independently per frame — is temporal
/// coherence, and no single-frame signature can make it. That is decided here,
/// in the API shape, because it is cheap now and an expensive retrofit later
/// (<c>docs/DESIGN-ai-correctness.md</c>).
/// </para>
/// <para>
/// <b>The AI is supposed to invent, so "new" is not the test — "licensed" is.</b>
/// A stroke matching nothing in either key is exactly what disocclusion, overlap
/// and follow-through look like. New ink is licensed when something moved off
/// the region it sits in and it continues a stroke already there (reveal), when
/// it trails behind a nearby stroke's direction of travel (drag), or when it
/// stays close to the drawing (interpretation, under <c>latitude</c>). Only ink
/// explained by nothing is refused.
/// </para>
/// <para>
/// Every band here is deliberately wide. The verifier rejects "not between the
/// keys at all", never "not where I would have put it" — a strict verifier
/// rejects the interesting answers, and the interesting answers are the point
/// of asking a model instead of <see cref="Inbetweener"/>.
/// </para>
/// </remarks>
public static class InbetweenVerifier
{
    /// <summary>
    /// How far invention may stray from the drawing, as a fraction of the
    /// drawing's diagonal. The interpretation dial from the design doc,
    /// defaulting low — "only what the keys state, plus a little".
    /// </summary>
    public const double DefaultLatitude = 0.12;

    // Betweenness: a matched stroke may sit this fraction of its own travel
    // away from where interpolation puts it, plus a share of its size and a
    // floor for strokes that barely move. Calibrated against the connection
    // tester's original band: a copied key on an 80px swing (deviation 40)
    // must refuse, an arc bowing a third of the travel must pass.
    private const double TravelSlack = 0.40;
    private const double ScaleSlack = 0.05;
    private const double FloorSlack = 2.0;

    // Coherence: an interior frame's deviation from the deterministic path may
    // sit this far from the line joining its neighbours' deviations. Smooth
    // overlap drifts; per-frame noise zigzags, and the zigzag is what boils.
    private const double CoherenceSlack = 0.15;
    private const double CoherenceFloor = 3.0;

    // Volume is conserved as area, not length — squash widens what it
    // shortens. A closed shape may halve or double against the interpolated
    // area before it is called a collapse; anything tighter would veto
    // deliberate squash and stretch, which is the principle, not the error.
    private const double AreaSlack = 2.0;
    private const double MinArea = 25.0;

    // Below this travel a stroke is holding still and licenses no reveal or
    // drag — new ink needs actual motion to be explained by.
    private const double MinTravel = 6.0;

    // A reveal must sit mostly where the mover used to be…
    private const double VacatedFraction = 0.6;
    // …and a mean deviation under a pixel means the model redrew the
    // deterministic answer: report it, never reject it (Q33).
    private const double AddedNothing = 1.0;

    public static InbetweenRunJudgement Verify(
        IReadOnlyList<Stroke> keyA,
        IReadOnlyList<Stroke> keyB,
        IReadOnlyList<CandidateInbetween> run,
        Easing easing = Easing.EaseInOut,
        double latitude = DefaultLatitude)
    {
        // Judge the same view the model was sent: erased strokes are not part
        // of either drawing, and a verifier that saw them would demand ink the
        // request never asked for.
        var ea = StrokeRecordCleaner.EffectiveStrokes(keyA);
        var eb = StrokeRecordCleaner.EffectiveStrokes(keyB);

        var moving = new List<PairContext>();
        var fading = new List<Stroke>();
        foreach (var pair in StrokeMatcher.Match(ea, eb))
        {
            if (pair.A is not null && pair.B is not null) moving.Add(new PairContext(pair.A, pair.B));
            else fading.Add((pair.A ?? pair.B)!);
        }

        var verdicts = new FrameState[run.Count];
        for (var i = 0; i < run.Count; i++)
            verdicts[i] = JudgeFrame(run[i], moving, fading, easing, latitude);

        JudgeCoherence(run, verdicts, moving);

        return new InbetweenRunJudgement(
            verdicts.Select((v, i) => new InbetweenJudgement(
                run[i].T, v.Refusal, v.Notes, v.Fault, v.MatchesDeterministic)).ToList());
    }

    /// <summary>A matched key pair and everything the checks derive from it.</summary>
    private sealed class PairContext(Stroke a, Stroke b)
    {
        public Stroke A { get; } = a;
        public Stroke B { get; } = b;
        public StrokePoint CentroidA { get; } = GeometryOps.Centroid(a.Points);
        public StrokePoint CentroidB { get; } = GeometryOps.Centroid(b.Points);
        public double Travel => GeometryOps.Dist(CentroidA, CentroidB);
        public double Scale => Math.Max(GeometryOps.PathLength(A.Points), GeometryOps.PathLength(B.Points));
        public string Name(int index) => A.Label ?? B.Label ?? $"stroke {index + 1}";
    }

    private sealed class FrameState
    {
        public string? Refusal;
        public InbetweenFault Fault;
        public bool MatchesDeterministic;
        public List<string> Notes = [];
        /// <summary>Deviation from the interpolated position, per pair index, for the coherence pass.</summary>
        public Dictionary<int, (double X, double Y)> Deviations = [];
    }

    private static FrameState JudgeFrame(
        CandidateInbetween frame,
        List<PairContext> moving,
        List<Stroke> fading,
        Easing easing,
        double latitude)
    {
        var state = new FrameState();

        if (frame.T is <= 0 or >= 1)
        {
            // Every hand-formatted number below goes through Invariant: a
            // refusal must read the same on every machine, and a locale that
            // writes 0,25 would hand Phase 2's golden-set tooling two
            // spellings of one fault (CultureInvarianceTests' rule).
            state.Refusal = Invariant($"its timing is {frame.T:0.##}, which is not between the keys.");
            state.Fault = InbetweenFault.Timing;
            return state;
        }
        if (Unusable(frame.Strokes) is { } unusable)
        {
            state.Refusal = unusable;
            state.Fault = InbetweenFault.Unusable;
            return state;
        }

        var et = EasingOps.Ease(frame.T, easing);

        // The expected drawing: every matched pair interpolated to this t, plus
        // the fading strokes at their key position. Fades are expected so a
        // model that carries them is not accused of inventing them — but a
        // model that drops one is not refused either, because the deterministic
        // engine itself fades them to nothing.
        var expected = new List<Stroke>(moving.Count + fading.Count);
        var pairOf = new Dictionary<string, int>();
        for (var i = 0; i < moving.Count; i++)
        {
            var e = StrokeInterpolator.Interpolate(moving[i].A, moving[i].B, et);
            pairOf[e.Id] = i;
            expected.Add(e);
        }
        expected.AddRange(fading);

        // The same matcher that pairs the keys pairs expectation with answer:
        // labels first, then minimum-total-cost geometry, so an unlabelled
        // candidate still corresponds without any naming convention.
        var newInk = new List<Stroke>();
        var matchedCandidates = new HashSet<string>();
        foreach (var pair in StrokeMatcher.Match(expected, frame.Strokes))
        {
            if (pair.A is null)
            {
                newInk.Add(pair.B!);
                continue;
            }
            if (!pairOf.TryGetValue(pair.A.Id, out var index))
            {
                if (pair.B is not null) matchedCandidates.Add(pair.B.Id);
                continue; // a fading stroke: carried or dropped, both defensible
            }

            var ctx = moving[index];
            if (pair.B is null)
            {
                state.Refusal = $"the {Quote(ctx.Name(index))} went missing — both keys draw it.";
                state.Fault = InbetweenFault.DroppedStroke;
                return state;
            }
            matchedCandidates.Add(pair.B.Id);

            var eCentroid = GeometryOps.Centroid(pair.A.Points);
            var cCentroid = GeometryOps.Centroid(pair.B.Points);
            var deviation = GeometryOps.Dist(eCentroid, cCentroid);
            var allowed = TravelSlack * ctx.Travel + ScaleSlack * ctx.Scale + FloorSlack;
            if (deviation > allowed)
            {
                state.Refusal = Invariant(
                    $"the {Quote(ctx.Name(index))} did not stay between the keys — it sits {deviation:0}px from where the motion puts it.");
                state.Fault = InbetweenFault.NotBetween;
                return state;
            }
            state.Deviations[index] = (cCentroid.X - eCentroid.X, cCentroid.Y - eCentroid.Y);

            if (VolumeFault(ctx, pair.A, pair.B, index) is { } volume)
            {
                state.Refusal = volume;
                state.Fault = InbetweenFault.Volume;
                return state;
            }
        }

        if (newInk.Count > 0)
        {
            // Licensed ink anchors further ink. A tail is three strokes and
            // only the base touches the body; fur hangs off fur. The worklist
            // licenses from the attachment outwards — each pass may explain
            // ink the previous pass could not — and only what no pass can
            // explain is refused. Floating inventions still license nothing,
            // because the chain has to start from a stroke the keys explain.
            var anchors = new List<Stroke>(expected);
            var anchoredIds = new HashSet<string>(matchedCandidates);
            var pending = new List<Stroke>(newInk);
            var progress = true;
            while (progress)
            {
                progress = false;
                for (var i = 0; i < pending.Count; i++)
                {
                    if (!TryLicenseNewInk(
                            pending[i], moving, expected, anchors, frame.Strokes, anchoredIds, latitude, state.Notes))
                    {
                        continue;
                    }
                    anchors.Add(pending[i]);
                    anchoredIds.Add(pending[i].Id);
                    pending.RemoveAt(i);
                    progress = true;
                    break;
                }
            }
            if (pending.Count > 0)
            {
                state.Refusal = ForbiddenInk(pending[0]);
                state.Fault = InbetweenFault.ForbiddenInk;
                return state;
            }
        }

        if (newInk.Count == 0 && state.Deviations.Count > 0)
        {
            var mean = state.Deviations.Values.Average(d => Math.Sqrt(d.X * d.X + d.Y * d.Y));
            if (mean < AddedNothing)
            {
                state.MatchesDeterministic = true;
                state.Notes.Add(
                    "this frame matches the deterministic answer almost exactly — "
                    + "the model added nothing the free engine would not have.");
            }
        }

        return state;
    }

    /// <summary>
    /// What makes these strokes unusable as a drawing, or null. The connection
    /// tester's checks, generalised here so both callers judge the same way.
    /// </summary>
    public static string? Unusable(IReadOnlyList<Stroke> strokes)
    {
        if (strokes.Count == 0) return "nothing was drawn.";
        if (strokes.All(s => s.Points.Count < 2)) return "no stroke has two points, so nothing would mark.";

        // Every point in the same place: a dot where a drawing was asked for.
        // Small models do this, and it survives both the schema and the clamp.
        var extent = strokes.SelectMany(s => s.Points).ToList();
        var width = extent.Max(p => p.X) - extent.Min(p => p.X);
        var height = extent.Max(p => p.Y) - extent.Min(p => p.Y);
        if (width < 1 && height < 1) return "every point is in the same place, so it is a dot, not a drawing.";

        return null;
    }

    /// <summary>
    /// Volume is conserved as enclosed area: a squash that widens is the
    /// principle, a stretch that also thins is the error, and path length
    /// cannot tell those two apart.
    /// </summary>
    private static string? VolumeFault(PairContext ctx, Stroke expected, Stroke candidate, int index)
    {
        if (!IsClosed(ctx.A) || !IsClosed(ctx.B) || !IsClosed(candidate)) return null;

        var want = Math.Abs(EnclosedArea(expected.Points));
        var got = Math.Abs(EnclosedArea(candidate.Points));
        if (want < MinArea) return null;

        if (got < want / AreaSlack || got > want * AreaSlack)
        {
            var verb = got < want ? "loses" : "gains";
            return Invariant(
                $"the {Quote(ctx.Name(index))} {verb} volume — {got:0}px² of area against an expected {want:0}px².");
        }
        return null;
    }

    /// <summary>
    /// Try to license this new ink under one of the three permissive tiers;
    /// the tier that licensed it lands in <paramref name="notes"/>. Every
    /// proximity here is measured at the ink's own nearest sampled point,
    /// never its centroid: a cape is long and its hem is far, and a centroid
    /// measurement refuses secondary action in proportion to its own length —
    /// the art-director veto that rewrote this method.
    /// </summary>
    private static bool TryLicenseNewInk(
        Stroke ink,
        List<PairContext> moving,
        List<Stroke> expected,
        List<Stroke> anchors,
        IReadOnlyList<Stroke> candidateStrokes,
        HashSet<string> anchoredIds,
        double latitude,
        List<string> notes)
    {
        var name = InkName(ink);
        var samples = SamplesOf(ink);

        // Disocclusion: something moved off the region this ink sits in, and
        // the ink continues a stroke that is already in the frame. The second
        // half is what separates "drew the body behind the arm" from "drew
        // something in the hole".
        for (var m = 0; m < moving.Count; m++)
        {
            var ctx = moving[m];
            if (ctx.Travel < MinTravel) continue;
            // Vacated is judged against where the mover is *now* — expected[m]
            // is its interpolated position at this frame's t. Ink under a
            // region the mover has not left yet reveals nothing.
            if (VacatedShare(ink, ctx, expected[m]) < VacatedFraction) continue;
            if (!ContinuesAnother(ink, candidateStrokes, anchoredIds)) continue;
            notes.Add($"{name} is licensed as revealed ink — the {Quote(ctx.Name(m))} moved off it.");
            return true;
        }

        // Drag: secondary action trails its mover. It must hang off the thing
        // it follows — fur is on the body, cloth is on the figure — so the
        // ink's nearest point has to touch the mover's current position, and
        // its mass must sit backwards along the travel. Fur that leads the
        // jump is not follow-through, it is a different drawing; and ink
        // merely lying somewhere in the mover's wake is disocclusion's
        // question, with the stricter continuation requirement that comes
        // with it.
        for (var m = 0; m < moving.Count; m++)
        {
            var ctx = moving[m];
            if (ctx.Travel < MinTravel) continue;
            var hang = Math.Max(ctx.A.Brush.Size, 4) + 2 + ctx.Scale * 0.25;
            if (MinDistanceToStroke(samples, expected[m]) > hang) continue;
            var inkCentroid = GeometryOps.Centroid(ink.Points);
            var eCentroid = GeometryOps.Centroid(expected[m].Points);
            var offX = inkCentroid.X - eCentroid.X;
            var offY = inkCentroid.Y - eCentroid.Y;
            var ux = (ctx.CentroidB.X - ctx.CentroidA.X) / ctx.Travel;
            var uy = (ctx.CentroidB.Y - ctx.CentroidA.Y) / ctx.Travel;
            if (offX * ux + offY * uy < 0)
            {
                notes.Add($"{name} is licensed as drag behind the {Quote(ctx.Name(m))}.");
                return true;
            }
        }

        // Interpretation: invention that stays close to the drawing — the
        // overlap fold, the turned contour, the strand hanging off the
        // hairline — under the latitude dial. Anchors include ink already
        // licensed this frame, which is what lets a chain grow outwards.
        var diagonal = DrawingDiagonal(expected);
        var distance = double.PositiveInfinity;
        foreach (var anchor in anchors)
            distance = Math.Min(distance, MinDistanceToStroke(samples, anchor));
        if (distance <= latitude * diagonal)
        {
            notes.Add(Invariant($"{name} is licensed as interpretation, {distance:0}px from the drawing."));
            return true;
        }

        return false;
    }

    private static string ForbiddenInk(Stroke ink) =>
        $"new {InkName(ink)} is explained by nothing — nothing moved off that region, "
        + "it does not trail any motion, and it is not near the drawing.";

    private static string InkName(Stroke ink)
    {
        if (ink.Label is { } label) return Quote(label);
        var c = GeometryOps.Centroid(ink.Points);
        return Invariant($"ink near ({c.X:0}, {c.Y:0})");
    }

    /// <summary>The ink's own points, thinned so every check pays the same bounded cost.</summary>
    private static IReadOnlyList<StrokePoint> SamplesOf(Stroke s) =>
        s.Points.Count > 24 ? GeometryOps.Resample(s.Points, 24) : s.Points;

    /// <summary>Nearest approach of any sampled point to the stroke's polyline.</summary>
    private static double MinDistanceToStroke(IReadOnlyList<StrokePoint> samples, Stroke stroke)
    {
        var pts = stroke.Points;
        if (pts.Count == 0 || samples.Count == 0) return double.PositiveInfinity;
        var best = double.PositiveInfinity;
        foreach (var p in samples)
        {
            if (pts.Count == 1)
            {
                best = Math.Min(best, GeometryOps.Dist(p, pts[0]));
                continue;
            }
            for (var i = 1; i < pts.Count; i++)
                best = Math.Min(best, GeometryOps.DistToSegment(p, pts[i - 1], pts[i]));
        }
        return best;
    }

    /// <summary>
    /// The share of this ink lying where <paramref name="ctx"/>'s stroke used
    /// to be and no longer is — the vacated region, computed from geometry
    /// instead of a part reading, so it works on unlabelled art.
    /// </summary>
    private static double VacatedShare(Stroke ink, PairContext ctx, Stroke movedTo)
    {
        var points = ink.Points.Count > 24 ? GeometryOps.Resample(ink.Points, 24) : (IReadOnlyList<StrokePoint>)ink.Points;
        if (points.Count == 0) return 0;

        var radius = Math.Max(ctx.A.Brush.Size, 4) + 2;
        var vacated = 0;
        foreach (var p in points)
        {
            if (NearPolyline(p, ctx.A.Points, radius) && !NearPolyline(p, movedTo.Points, radius)) vacated++;
        }
        return (double)vacated / points.Count;
    }

    /// <summary>
    /// Revealed ink joins the visible ends of the stroke it extends: an
    /// endpoint of the new ink sits at an endpoint of some other stroke in the
    /// same candidate frame.
    /// </summary>
    private static bool ContinuesAnother(
        Stroke ink, IReadOnlyList<Stroke> candidateStrokes, HashSet<string> matchedCandidates)
    {
        if (ink.Points.Count == 0) return false;
        var tolerance = Math.Max(6, 0.15 * GeometryOps.PathLength(ink.Points));
        var ends = new[] { ink.Points[0], ink.Points[^1] };
        foreach (var other in candidateStrokes)
        {
            if (other.Id == ink.Id || other.Points.Count == 0) continue;
            // Only strokes that themselves correspond to the keys can be
            // continued — two floating inventions joining each other license
            // nothing.
            if (!matchedCandidates.Contains(other.Id)) continue;
            foreach (var end in ends)
            {
                if (GeometryOps.Dist(end, other.Points[0]) <= tolerance
                    || GeometryOps.Dist(end, other.Points[^1]) <= tolerance)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Temporal coherence over the run: an interior frame's deviation from the
    /// deterministic path must sit near the line joining its neighbours'.
    /// Smooth overlap drifts through that line; independent per-frame noise
    /// zigzags across it, and the zigzag is what reads as boiling at 12fps.
    /// No single-frame check can catch this, which is why the verifier sees a
    /// run at all.
    /// </summary>
    private static void JudgeCoherence(
        IReadOnlyList<CandidateInbetween> run, FrameState[] verdicts, List<PairContext> moving)
    {
        for (var k = 1; k + 1 < run.Count; k++)
        {
            if (verdicts[k].Refusal is not null) continue;
            // Neighbours are read even when they were themselves refused: a
            // deviation is real geometry for that stroke in that frame,
            // whatever unrelated check later refused the frame, and a frame
            // refused before its deviations were measured simply has no entry
            // and is skipped by the TryGetValue below.

            for (var p = 0; p < moving.Count; p++)
            {
                if (!verdicts[k - 1].Deviations.TryGetValue(p, out var d0)
                    || !verdicts[k].Deviations.TryGetValue(p, out var d1)
                    || !verdicts[k + 1].Deviations.TryGetValue(p, out var d2))
                {
                    continue;
                }

                var span = run[k + 1].T - run[k - 1].T;
                if (span <= 0) continue;
                var w = (run[k].T - run[k - 1].T) / span;
                var lx = GeometryOps.Lerp(d0.X, d2.X, w);
                var ly = GeometryOps.Lerp(d0.Y, d2.Y, w);
                var jitter = Math.Sqrt((d1.X - lx) * (d1.X - lx) + (d1.Y - ly) * (d1.Y - ly));

                var allowed = CoherenceSlack * moving[p].Travel + CoherenceFloor;
                if (jitter > allowed)
                {
                    verdicts[k].Refusal = Invariant(
                        $"the {Quote(moving[p].Name(p))} jitters against the frames beside it — {jitter:0}px off a smooth path, which reads as boiling at speed.");
                    verdicts[k].Fault = InbetweenFault.Incoherent;
                    break;
                }
            }
        }
    }

    private static bool IsClosed(Stroke s) =>
        s.Tool.FillsAContour()
        || (s.Points.Count >= 3
            && GeometryOps.Dist(s.Points[0], s.Points[^1]) <= 0.15 * GeometryOps.PathLength(s.Points));

    /// <summary>Shoelace area of the polygon the points enclose.</summary>
    private static double EnclosedArea(IReadOnlyList<StrokePoint> points)
    {
        if (points.Count < 3) return 0;
        double sum = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            sum += (points[j].X + points[i].X) * (points[j].Y - points[i].Y);
        return sum / 2;
    }

    private static bool NearPolyline(StrokePoint p, IReadOnlyList<StrokePoint> line, double radius)
    {
        if (line.Count == 0) return false;
        if (line.Count == 1) return GeometryOps.Dist(p, line[0]) <= radius;
        for (var i = 1; i < line.Count; i++)
        {
            if (GeometryOps.DistToSegment(p, line[i - 1], line[i]) <= radius) return true;
        }
        return false;
    }

    private static double DrawingDiagonal(List<Stroke> expected)
    {
        var all = expected.SelectMany(s => s.Points).ToList();
        if (all.Count == 0) return 0;
        var b = GeometryOps.BoundsOf(all);
        var w = b.MaxX - b.MinX;
        var h = b.MaxY - b.MinY;
        return Math.Sqrt(w * w + h * h);
    }

    private static string Quote(string name) => name.StartsWith("stroke ", StringComparison.Ordinal)
        ? name
        : $"“{name}”";
}
