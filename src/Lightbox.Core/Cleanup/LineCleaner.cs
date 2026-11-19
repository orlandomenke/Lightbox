using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Cleanup;

/// <summary>How hard a per-frame clean smooths. Guided cleans choose their own.</summary>
public enum CleanupStrength { Light, Medium, Strong }

/// <summary>What a cleanup did to one stroke.</summary>
public enum CleanupKind
{
    /// <summary>Wobble and small kinks taken out; the line moved by at most its own width.</summary>
    Smoothed,
    /// <summary>An end that ran past the line it meets was cut back to it.</summary>
    Trimmed,
    /// <summary>An end that stopped short of the line it meets was carried onto it.</summary>
    Joined,
    /// <summary>A speck or a faint orphan mark was taken out.</summary>
    Removed,
}

/// <summary>One thing a cleanup changed, for the preview's list and count.</summary>
/// <param name="Moved">The furthest any point of the stroke moved, in document pixels.</param>
public sealed record CleanupChange(string StrokeId, CleanupKind Kind, double Moved);

/// <summary>
/// A proposed clean: the whole record as it would be, and what changed. The
/// record handed in is never modified (Q292) — only applying this result does.
/// </summary>
public sealed record CleanupResult(List<Stroke> Strokes, List<CleanupChange> Changes)
{
    public bool Changed => Changes.Count > 0;

    /// <summary>The changes of one kind.</summary>
    public int Count(CleanupKind kind) => Changes.Count(c => c.Kind == kind);
}

/// <summary>
/// Tidies the lines of one drawing without redrawing it: wobble and small
/// kinks smoothed, overshoots trimmed and small gaps closed where lines meet,
/// specks and faint orphan marks removed (Q293).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not drastic, by construction.</b> No point moves further than its
/// stroke's own line width; strokes are never merged, split or added; pressure,
/// brush and colour are kept, so the line is the same mark in the same place.
/// A doubled sketch pass stays doubled — merging is deliberately not here.
/// </para>
/// <para>
/// <b>Two modes.</b> <see cref="Clean"/> tidies one drawing at a chosen
/// strength. <see cref="CleanGuided"/> cleans a drawing — in practice a
/// generated inbetween — toward the hand-drawn frames around it: as smooth as
/// they are, its lines meeting the way theirs meet, its corners kept where
/// theirs are. That is what repairs what the inbetweener leaves behind.
/// </para>
/// <para>
/// <b>Deterministic (Q295).</b> A pure function of the strokes; no model, no
/// randomness, and the same input gives the same bits. Moving a point re-seeds
/// the grain of the dabs near it, which Q26 accepted for every edit that moves
/// a line.
/// </para>
/// <para>
/// <b>Only plain drawn lines are touched.</b> Erasers, fills, groups, lines
/// with an authored path (moving their points would break the path's agreement
/// with them), lines bound to bones (their rest pose counts points) and baked
/// or text strokes pass through untouched.
/// </para>
/// </remarks>
public static class LineCleaner
{
    /// <summary>Spacing of the dense copy every measurement is taken on, in pixels.</summary>
    private const double Step = 1.0;

    /// <summary>A turn this sharp, measured over a long arm, is a corner the artist meant.</summary>
    private const double CornerDeg = 50;

    /// <summary>A corner this sharp is kept even when no reference frame has one nearby.</summary>
    private const double AlwaysCornerDeg = 100;

    /// <summary>Lines crossing more shallowly than this are converging passes, not a junction.</summary>
    private const double ShallowDeg = 20;

    /// <summary>Points moving less than this are not reported as a change.</summary>
    private const double Noticeable = 0.25;

    /// <summary>Clean one drawing on its own (per-frame mode).</summary>
    public static CleanupResult Clean(IReadOnlyList<Stroke> record, CleanupStrength strength = CleanupStrength.Medium)
    {
        var style = new Style(
            Passes: [new Pass(strength switch { CleanupStrength.Light => 2.5, CleanupStrength.Strong => 8, _ => 5 }, 0)],
            Opacity: StrongestOpacity(Lines(record)),
            TrimOvershoots: true,
            CloseGaps: true,
            ReferenceCorners: null);
        return Run(record, style);
    }

    /// <summary>
    /// Clean a drawing toward the hand-drawn frames around it (guided mode).
    /// With no usable reference it does nothing rather than guess.
    /// </summary>
    public static CleanupResult CleanGuided(IReadOnlyList<Stroke> record, IReadOnlyList<IReadOnlyList<Stroke>> references)
    {
        var refs = references.Select(r => Lines(StrokeRecordCleaner.EffectiveStrokes(r))).Where(r => r.Count > 0).ToList();
        if (refs.Count == 0) return new CleanupResult([.. record], []);

        var refLines = refs.SelectMany(r => r).ToList();
        var meeting = refs.Select(JunctionHabits).Aggregate((x, y) => (x.Overshoot + y.Overshoot, x.Gap + y.Gap, x.Near + y.Near));
        var corners = refs.SelectMany(r => r.Where(s => s.Points.Count > 1).SelectMany(s => CornersOf(new Course(s.Points).Dense).Select(c => (c.P, c.Turn)))).ToList();
        var style = new Style(
            Passes: [.. KinkSigmas.Select(sigma => new Pass(sigma, Stray(refLines, sigma, 0.95)))],
            Opacity: StrongestOpacity(refLines),
            TrimOvershoots: meeting.Near == 0 || meeting.Overshoot * 2 < meeting.Near,
            CloseGaps: meeting.Near == 0 || meeting.Gap * 2 < meeting.Near,
            ReferenceCorners: corners,
            ReferenceLines: refLines);
        return Run(record, style);
    }

    /// <summary>
    /// The scales a guided clean looks for kinks at, in pixels of arc: a step
    /// sideways over a few pixels, then a bend where two pieces meet that
    /// spans a dozen or more. One scale cannot see both — the fine one follows
    /// a seam's bend, the coarse one hides a step inside the keys' own curves.
    /// </summary>
    private static readonly double[] KinkSigmas = [12, 3];

    /// <summary>One smoothing pass.</summary>
    /// <param name="Sigma">The scale, in pixels of arc.</param>
    /// <param name="Tolerance">
    /// How far a point may stray from its line's smooth course before it is
    /// pulled in, in pixels. Zero smooths everything (per-frame); guided, it is
    /// how far the references' own lines stray, so only what they never do —
    /// a kink, a jog — is corrected and the line keeps the keys' character.
    /// </param>
    private sealed record Pass(double Sigma, double Tolerance);

    /// <summary>How a clean behaves: the measured or chosen character it cleans toward.</summary>
    /// <param name="Passes">The smoothing passes, in order; the total move is capped once, after all of them.</param>
    private sealed record Style(
        IReadOnlyList<Pass> Passes, double Opacity, bool TrimOvershoots, bool CloseGaps,
        List<(StrokePoint P, double Turn)>? ReferenceCorners,
        List<Stroke>? ReferenceLines = null);

    private static CleanupResult Run(IReadOnlyList<Stroke> record, Style style)
    {
        var changes = new List<CleanupChange>();
        var effective = StrokeRecordCleaner.EffectiveStrokes(record);
        var visible = effective.Select(s => s.Id).ToHashSet();

        // 1. Specks and faint orphans. Only what is actually on the drawing
        //    counts as a neighbour, and an erased line is not removed twice.
        var removed = new HashSet<string>();
        var neighbours = effective.Where(s => s.Tool == ToolKind.Brush && s.Points.Count > 0).ToList();
        foreach (var s in record)
        {
            if (!Cleanable(s) || !visible.Contains(s.Id)) continue;
            if (IsOrphan(s, neighbours, style))
            {
                removed.Add(s.Id);
                changes.Add(new CleanupChange(s.Id, CleanupKind.Removed, 0));
            }
        }

        // 2. Smooth each line — and each run of lines that continue one
        //    another end to end, as one line. An inbetween is rebuilt from
        //    pieces, and its kinks sit where two pieces meet: smoothing each
        //    piece alone holds both ends still and never sees them.
        var candidates = record.Where(s => Cleanable(s) && !removed.Contains(s.Id) && s.Points.Count > 1).ToList();
        var (chains, linked) = Chains(candidates);
        var working = new Dictionary<string, List<StrokePoint>>();
        foreach (var chain in chains)
        {
            foreach (var (id, points, seamClosed) in SmoothChain(chain, style))
            {
                var before = candidates.First(c => c.Id == id).Points;
                var moved = MaxMove(before, points);
                if (moved < Noticeable) continue;
                working[id] = points;
                changes.Add(new CleanupChange(id, seamClosed ? CleanupKind.Joined : CleanupKind.Smoothed, moved));
            }
        }

        // 3. Where lines meet — measured against the smoothed drawing, with
        //    every end decided from the same snapshot so order cannot matter.
        //    An end already joined into a run is not a junction.
        var lines = effective
            .Where(s => s.Tool == ToolKind.Brush && !removed.Contains(s.Id) && s.Points.Count > 0)
            .Select(s => (s, Pts: working.TryGetValue(s.Id, out var w) ? w : s.Points))
            .ToList();
        var ends = new Dictionary<string, List<StrokePoint>>();
        foreach (var (s, pts) in lines)
        {
            if (!Cleanable(s) || pts.Count < 2) continue;
            var others = lines.Where(l => l.s.Id != s.Id).ToList();
            var cap = Cap(s);
            var result = pts;
            var kind = (CleanupKind?)null;
            foreach (var atStart in new[] { false, true })
            {
                if (linked.Contains((s.Id, atStart))) continue;
                var oriented = atStart ? result.AsEnumerable().Reverse().ToList() : result;
                var (fixedEnd, k) = FixEnd(oriented, s, others, cap, style);
                if (k is null) continue;
                result = atStart ? fixedEnd.AsEnumerable().Reverse().ToList() : fixedEnd;
                kind ??= k;
            }
            if (kind is { } done)
            {
                ends[s.Id] = result;
                changes.Add(new CleanupChange(s.Id, done, MaxEndMove(pts, result)));
            }
        }

        var output = new List<Stroke>(record.Count);
        foreach (var s in record)
        {
            if (removed.Contains(s.Id)) continue;
            var pts = ends.TryGetValue(s.Id, out var e) ? e : working.TryGetValue(s.Id, out var w) ? w : null;
            if (pts is null)
            {
                output.Add(s);
                continue;
            }
            var copy = s.Clone(newId: false);
            copy.Points = pts;
            output.Add(copy);
        }
        return new CleanupResult(output, changes);
    }

    /// <summary>
    /// A plain drawn line whose points can move without breaking anything that
    /// is stored beside them.
    /// </summary>
    internal static bool Cleanable(Stroke s) =>
        s.Tool == ToolKind.Brush
        && s.Group is null && s.Path is null
        && s.Weights is null && s.RestPoints is null
        && s.Baked is null && s.SimId is null && s.TextId is null
        && s.Points.Count > 0;

    private static List<Stroke> Lines(IReadOnlyList<Stroke> strokes) =>
        strokes.Where(s => s.Tool == ToolKind.Brush && s.Points.Count > 0).ToList();

    /// <summary>No point moves further than the line is wide.</summary>
    private static double Cap(Stroke s) => Math.Clamp(s.Brush.Size, 1.5, 12);

    // ---------------------------------------------------------------- orphans

    /// <summary>
    /// A speck — a blip of ink stuck to another line, a pen tap at a junction
    /// — or a faint short mark. An isolated dot is not one: it may be a pupil
    /// or a star. Guided, a short mark is also an orphan when neither
    /// reference has a mark like it anywhere near.
    /// </summary>
    private static bool IsOrphan(Stroke s, List<Stroke> neighbours, Style style)
    {
        var length = GeometryOps.PathLength(s.Points);
        var size = s.Brush.Size;
        var shortest = Math.Max(4 * size, 16);
        if (length >= shortest) return false;
        if (s.Brush.Opacity < 0.5 * style.Opacity) return true;
        if (length <= Math.Max(size, 3) && Near(s, neighbours.Where(n => n.Id != s.Id), 2 * size)) return true;
        if (style.ReferenceLines is { } refs)
        {
            var similar = refs.Where(r => GeometryOps.PathLength(r.Points) <= Math.Max(2 * length, shortest));
            return !Near(s, similar, OrphanReach);
        }
        return false;
    }

    /// <summary>How far from a short mark a reference must have one like it, in pixels.</summary>
    private const double OrphanReach = 60;

    private static bool Near(Stroke s, IEnumerable<Stroke> others, double within)
    {
        foreach (var o in others)
        foreach (var p in s.Points)
        {
            if (o.Points.Count == 1 && GeometryOps.Dist(p, o.Points[0]) <= within) return true;
            for (var j = 1; j < o.Points.Count; j++)
            {
                if (GeometryOps.DistToSegment(p, o.Points[j - 1], o.Points[j]) <= within) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The opacity of the strongest line: a mark is faint against what the
    /// drawing can be, not against a middle that faint marks themselves drag down.
    /// </summary>
    private static double StrongestOpacity(IReadOnlyList<Stroke> strokes) =>
        strokes.Count == 0 ? 1 : strokes.Max(s => s.Brush.Opacity);

    // -------------------------------------------------------------- smoothing

    /// <summary>
    /// The stroke's own points, each pulled toward a Gaussian average of the
    /// line around it — never across a corner, never further than the cap,
    /// ends fixed. Pressure, tilt and speed stay with their points.
    /// </summary>
    private static List<StrokePoint> Smooth(List<StrokePoint> pts, double cap, Style style)
    {
        if (pts.Count < 3) return pts;
        var current = pts;
        foreach (var pass in style.Passes) current = SmoothPass(current, pass, style);
        if (ReferenceEquals(current, pts)) return pts;

        // However many passes, no point ends up further than the cap from
        // where it was drawn.
        var output = new List<StrokePoint>(pts.Count);
        for (var i = 0; i < pts.Count; i++)
        {
            var dx = current[i].X - pts[i].X;
            var dy = current[i].Y - pts[i].Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            var k = d > cap ? cap / d : 1;
            output.Add(pts[i] with { X = pts[i].X + dx * k, Y = pts[i].Y + dy * k });
        }
        return output;
    }

    private static List<StrokePoint> SmoothPass(List<StrokePoint> pts, Pass pass, Style style)
    {
        if (pass.Sigma <= 0) return pts;
        var course = new Course(pts);
        if (course.Length < 2 * Step) return pts;

        // Segment bounds, as distances along the line: the ends and every
        // corner that is kept.
        var bounds = new List<double> { 0 };
        bounds.AddRange(CornersOf(course.Dense)
            .Where(c => Keeps(c, pass, style))
            .Select(c => c.Index * course.DenseStep));
        bounds.Add(course.Length);

        // Where each point's smooth course is, and how far off it the point is.
        var n = pts.Count;
        var cx = new double[n];
        var cy = new double[n];
        var weight = new double[n];
        for (var i = 0; i < n; i++)
        {
            var at = course.ArcOf(i);
            var segment = Math.Clamp(bounds.FindLastIndex(x => x <= at), 0, bounds.Count - 2);
            var lo = bounds[segment];
            var hi = bounds[segment + 1];
            if (i == 0 || i == n - 1 || at - lo < 1e-9 || hi - at < 1e-9)
            {
                (cx[i], cy[i]) = (pts[i].X, pts[i].Y);
                continue;
            }
            (cx[i], cy[i]) = course.SmoothAt(at, pass.Sigma, lo, hi);
            if (pass.Tolerance <= 0)
            {
                weight[i] = 1;
                continue;
            }
            // Strays the references also make are left alone; strays past
            // twice that are corrected fully, with a ramp between.
            var d = Math.Sqrt(Math.Pow(cx[i] - pts[i].X, 2) + Math.Pow(cy[i] - pts[i].Y, 2));
            var x = Math.Clamp((d - pass.Tolerance) / pass.Tolerance, 0, 1);
            weight[i] = x * x * (3 - 2 * x);
        }

        // A jog's own middle sits on the course — the course runs through it —
        // so judged point by point the middle of a step would stay and the
        // step with it. So a correction reaches along the line from wherever
        // a stray was found, fully within half the pass's scale and fading to
        // nothing at its full scale. Widening is safe: a point near the course
        // has only a little to move.
        var reached = weight;
        if (pass.Tolerance > 0)
        {
            reached = new double[n];
            var reach = pass.Sigma;
            for (var i = 0; i < n; i++)
            {
                if (weight[i] <= 0) continue;
                var at = course.ArcOf(i);
                for (var j = i; j >= 0 && at - course.ArcOf(j) <= reach; j--) reached[j] = Math.Max(reached[j], weight[i] * Fade((at - course.ArcOf(j)) / reach));
                for (var j = i + 1; j < n && course.ArcOf(j) - at <= reach; j++) reached[j] = Math.Max(reached[j], weight[i] * Fade((course.ArcOf(j) - at) / reach));
            }
        }

        var output = new List<StrokePoint>(n);
        for (var i = 0; i < n; i++)
        {
            var w = reached[i];
            output.Add(w <= 0 ? pts[i] : pts[i] with { X = pts[i].X + (cx[i] - pts[i].X) * w, Y = pts[i].Y + (cy[i] - pts[i].Y) * w });
        }
        return output;
    }

    /// <summary>1 up to half way, easing to 0 at 1.</summary>
    private static double Fade(double u)
    {
        if (u <= 0.5) return 1;
        if (u >= 1) return 0;
        var x = (1 - u) * 2;
        return x * x * (3 - 2 * x);
    }

    /// <summary>
    /// Move the points from <paramref name="from"/> (the end) inward by
    /// <paramref name="dx"/>,<paramref name="dy"/>, fully at the end and fading
    /// to nothing <paramref name="over"/> pixels in, never past the cap from
    /// where they were drawn.
    /// </summary>
    private static void Ease(
        List<StrokePoint> points, List<StrokePoint> drawn, int from, int step, int stop,
        double dx, double dy, double over, double cap)
    {
        var walked = 0.0;
        for (var i = from; step < 0 ? i >= stop : i <= stop; i += step)
        {
            if (i != from) walked += GeometryOps.Dist(points[i], points[i - step]);
            if (walked >= over) break;
            var x = 1 - walked / over;
            var w = x * x * (3 - 2 * x);
            var mx = points[i].X + dx * w - drawn[i].X;
            var my = points[i].Y + dy * w - drawn[i].Y;
            var d = Math.Sqrt(mx * mx + my * my);
            var k = d > cap ? cap / d : 1;
            points[i] = points[i] with { X = drawn[i].X + mx * k, Y = drawn[i].Y + my * k };
        }
    }

    /// <summary>
    /// A line measured by distance along it: where any distance falls, and a
    /// copy at even spacing for finding corners.
    /// </summary>
    private sealed class Course
    {
        private readonly IReadOnlyList<StrokePoint> _points;
        private readonly double[] _arc;

        public Course(IReadOnlyList<StrokePoint> points)
        {
            _points = points;
            _arc = new double[points.Count];
            for (var i = 1; i < points.Count; i++)
            {
                var d = GeometryOps.Dist(points[i - 1], points[i]);
                _arc[i] = _arc[i - 1] + (double.IsFinite(d) ? d : 0);
            }
            Length = _arc[^1];
            var n = Math.Max(1, (int)Math.Min(Math.Round(Length / Step), MaxSamples));
            DenseStep = Length / n;
            Dense = new List<StrokePoint>(n + 1);
            for (var k = 0; k <= n; k++) Dense.Add(At(k * DenseStep));
        }

        /// <summary>The most even samples one line is measured with.</summary>
        private const int MaxSamples = 1 << 16;

        public double Length { get; }
        public double DenseStep { get; }
        public List<StrokePoint> Dense { get; }

        public double ArcOf(int index) => _arc[index];

        public StrokePoint At(double u)
        {
            if (u <= 0) return _points[0];
            if (u >= Length) return _points[^1];
            var hi = Array.BinarySearch(_arc, u);
            if (hi >= 0) return _points[hi];
            hi = ~hi;
            var lo = hi - 1;
            var span = _arc[hi] - _arc[lo];
            return span <= 0 ? _points[hi] : GeometryOps.LerpPoint(_points[lo], _points[hi], (u - _arc[lo]) / span);
        }

        /// <summary>
        /// Where the line's smooth course passes at <paramref name="at"/>: a
        /// parabola fitted through the line around it, Gaussian-weighted by
        /// distance along it, and evaluated at the point itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A parabola rather than an average</b>, because an average is
        /// pulled to the inside of every curve: a drawn bend would read as a
        /// stray, the references' own curves would inflate the tolerance, and
        /// a jog sitting on a bend would hide inside it. The fit follows a
        /// curve exactly and leaves wobble and jogs as what stands off it.
        /// </para>
        /// <para>
        /// Sampled symmetrically by distance along the line, so the odd
        /// moments cancel and the fit at the point is
        /// <c>(S4·Σwp − S2·Σwt²p) / (S0·S4 − S2²)</c>; a straight run comes back
        /// exactly as itself and nothing slides along it.
        /// </para>
        /// </remarks>
        public (double X, double Y) SmoothAt(double at, double sigma, double lo, double hi)
        {
            var reach = (int)Math.Ceiling(3 * sigma / Step);
            double s0 = 0, s2 = 0, s4 = 0, px = 0, py = 0, qx = 0, qy = 0;
            for (var k = -reach; k <= reach; k++)
            {
                var t = k * Step;
                var t2 = t * t;
                var w = Math.Exp(-0.5 * t2 / (sigma * sigma));
                var (x, y) = Reflected(at + t, lo, hi);
                s0 += w; s2 += w * t2; s4 += w * t2 * t2;
                px += w * x; py += w * y;
                qx += w * t2 * x; qy += w * t2 * y;
            }
            var det = s0 * s4 - s2 * s2;
            if (det <= 1e-12) return (px / s0, py / s0);
            return ((s4 * px - s2 * qx) / det, (s4 * py - s2 * qy) / det);
        }

        /// <summary>
        /// The line at <paramref name="u"/>, reflected through the segment's end
        /// when it lies past it — so a run stays straight up to a held end
        /// instead of being pulled inward by a one-sided average.
        /// </summary>
        public (double X, double Y) Reflected(double u, double lo, double hi)
        {
            if (u < lo)
            {
                var end = At(lo);
                var m = At(Math.Min(2 * lo - u, hi));
                return (2 * end.X - m.X, 2 * end.Y - m.Y);
            }
            if (u > hi)
            {
                var end = At(hi);
                var m = At(Math.Max(2 * hi - u, lo));
                return (2 * end.X - m.X, 2 * end.Y - m.Y);
            }
            var p = At(u);
            return (p.X, p.Y);
        }
    }

    /// <summary>Two ends this close, heading on into each other, are one line drawn in two strokes.</summary>
    private const double SeamDeg = 45;

    /// <summary>
    /// Runs of strokes that continue one another end to end, each stroke in
    /// exactly one run (most runs are one stroke), and which ends were joined.
    /// </summary>
    /// <remarks>
    /// Two ends link when they are close and point at each other: the line
    /// leaving one end carries on into the other. Two passes of a doubled line
    /// end side by side pointing the same way and do not link, so a doubled
    /// pass is never fused. Each end links at most once, closest first, ties
    /// broken by the record's order — so the runs never depend on luck.
    /// </remarks>
    private static (List<List<(Stroke S, bool Reversed)>> Chains, HashSet<(string, bool)> Linked) Chains(List<Stroke> strokes)
    {
        var endsOf = new List<(int Stroke, bool AtStart, StrokePoint P, double Dx, double Dy)>();
        for (var i = 0; i < strokes.Count; i++)
        {
            var pts = strokes[i].Points;
            var length = GeometryOps.PathLength(pts);
            if (length <= 0) continue;
            foreach (var atStart in new[] { false, true })
            {
                var oriented = atStart ? pts.AsEnumerable().Reverse().ToList() : pts;
                var back = PointBack(oriented, Math.Min(6, length / 2));
                var end = oriented[^1];
                var len = GeometryOps.Dist(back, end);
                if (len <= 0) continue;
                endsOf.Add((i, atStart, end, (end.X - back.X) / len, (end.Y - back.Y) / len));
            }
        }

        var pairs = new List<(double Score, int A, int B)>();
        for (var a = 0; a < endsOf.Count; a++)
        for (var b = a + 1; b < endsOf.Count; b++)
        {
            var ea = endsOf[a];
            var eb = endsOf[b];
            if (ea.Stroke == eb.Stroke) continue;
            var reach = Math.Max(2 * Math.Max(strokes[ea.Stroke].Brush.Size, strokes[eb.Stroke].Brush.Size), 8);
            var d = GeometryOps.Dist(ea.P, eb.P);
            if (d > reach) continue;
            // Leaving one end must be arriving at the other: the two outward
            // directions point at each other.
            var dot = -(ea.Dx * eb.Dx + ea.Dy * eb.Dy);
            var angle = Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI;
            if (angle > SeamDeg) continue;
            pairs.Add((d + angle / 10, a, b));
        }

        var link = new Dictionary<int, int>();
        foreach (var (_, a, b) in pairs.OrderBy(p => p.Score).ThenBy(p => p.A).ThenBy(p => p.B))
        {
            if (link.ContainsKey(a) || link.ContainsKey(b)) continue;
            link[a] = b;
            link[b] = a;
        }

        // Find each end's index from (stroke, which end).
        var endIndex = new Dictionary<(int, bool), int>();
        for (var e = 0; e < endsOf.Count; e++) endIndex[(endsOf[e].Stroke, endsOf[e].AtStart)] = e;
        int? Partner(int stroke, bool atStart) =>
            endIndex.TryGetValue((stroke, atStart), out var e) && link.TryGetValue(e, out var other) ? other : null;

        var chains = new List<List<(Stroke, bool)>>();
        var linked = new HashSet<(string, bool)>();
        var seen = new HashSet<int>();
        for (var i = 0; i < strokes.Count; i++)
        {
            if (seen.Contains(i)) continue;
            // Walk back to the head of the run: across this stroke's start,
            // and on while there is a stroke before it. A closed loop is
            // opened at the stroke the walk began from.
            var head = i;
            var headReversed = false;
            var guard = new HashSet<int> { i };
            while (true)
            {
                var entering = headReversed ? false : true; // the end the run enters this stroke by
                if (Partner(head, entering) is not { } p) break;
                var prev = endsOf[p];
                if (!guard.Add(prev.Stroke)) break;
                head = prev.Stroke;
                // We arrive at prev's end p; the run passes through prev toward p,
                // so prev is reversed when p is its start.
                headReversed = prev.AtStart;
            }

            var chain = new List<(Stroke, bool)>();
            var current = head;
            var reversed = headReversed;
            while (seen.Add(current))
            {
                chain.Add((strokes[current], reversed));
                var leaving = reversed; // reversed: leaves by its start
                if (Partner(current, leaving) is not { } p) break;
                var next = endsOf[p];
                if (seen.Contains(next.Stroke)) break;
                linked.Add((strokes[current].Id, leaving));
                linked.Add((strokes[next.Stroke].Id, next.AtStart));
                current = next.Stroke;
                reversed = !next.AtStart; // entered by its finish: run goes backwards through it
            }
            chains.Add(chain);
        }
        return (chains, linked);
    }

    /// <summary>
    /// Smooth one run as a single line, then hand each stroke its own points
    /// back in its own direction. Ends inside the run are free to move, and
    /// where two meet across a seam and can reach each other within their cap
    /// they are brought together.
    /// </summary>
    private static IEnumerable<(string Id, List<StrokePoint> Points, bool SeamClosed)> SmoothChain(
        List<(Stroke S, bool Reversed)> chain, Style style)
    {
        var all = new List<StrokePoint>();
        var spans = new List<(int Start, int Count)>();
        foreach (var (s, reversed) in chain)
        {
            spans.Add((all.Count, s.Points.Count));
            all.AddRange(reversed ? s.Points.AsEnumerable().Reverse() : s.Points);
        }
        var cap = chain.Min(c => Cap(c.S));
        var smoothed = Smooth(all, cap, style);
        if (ReferenceEquals(smoothed, all)) smoothed = [.. all];

        var closed = new bool[chain.Count];
        for (var k = 1; k < chain.Count; k++)
        {
            var last = spans[k].Start - 1;
            var first = spans[k].Start;
            var a = smoothed[last];
            var b = smoothed[first];
            var mid = (X: (a.X + b.X) / 2, Y: (a.Y + b.Y) / 2);
            var reachA = Math.Sqrt(Math.Pow(mid.X - all[last].X, 2) + Math.Pow(mid.Y - all[last].Y, 2));
            var reachB = Math.Sqrt(Math.Pow(mid.X - all[first].X, 2) + Math.Pow(mid.Y - all[first].Y, 2));
            if (GeometryOps.Dist(a, b) < Noticeable || reachA > cap || reachB > cap) continue;
            // Each side is eased onto the meeting point over its last few
            // pixels rather than having its end point alone moved, which would
            // trade the gap for a spike.
            var blend = Math.Max(3 * cap, 6);
            Ease(smoothed, all, last, -1, spans[k - 1].Start, mid.X - a.X, mid.Y - a.Y, blend, cap);
            Ease(smoothed, all, first, +1, spans[k].Start + spans[k].Count - 1, mid.X - b.X, mid.Y - b.Y, blend, cap);
            closed[k - 1] = closed[k] = true;
        }

        for (var k = 0; k < chain.Count; k++)
        {
            var (s, reversed) = chain[k];
            var part = smoothed.GetRange(spans[k].Start, spans[k].Count);
            if (reversed) part.Reverse();
            yield return (s.Id, part, closed[k]);
        }
    }

    /// <summary>
    /// Whether a corner is kept sharp. On its own, every corner is. Guided, a
    /// corner is kept only where a reference frame has one nearby — a knee the
    /// keys bend at — or when it is too sharp to be a slip; elsewhere it is a
    /// kink the inbetween made, and is smoothed like any wobble.
    /// </summary>
    private static bool Keeps((int Index, StrokePoint P, double Turn) c, Pass pass, Style style)
    {
        if (style.ReferenceCorners is not { } refs) return true;
        if (c.Turn >= AlwaysCornerDeg) return true;
        var radius = Math.Max(40, 8 * pass.Sigma);
        return refs.Any(r => r.Turn >= 0.5 * c.Turn && GeometryOps.Dist(r.P, c.P) <= radius);
    }

    /// <summary>Arm over which a corner is measured, in dense samples (~24px).</summary>
    private const int CornerArm = 24;

    /// <summary>
    /// Corners measured over a long arm: a turn the line keeps, not a jog it
    /// recovers from. Each is the sharpest point of its own turn.
    /// </summary>
    private static List<(int Index, StrokePoint P, double Turn)> CornersOf(List<StrokePoint> dense)
    {
        var found = new List<(int, StrokePoint, double)>();
        var n = dense.Count;
        if (n < 2 * CornerArm + 1) return found;
        var turn = new double[n];
        for (var k = CornerArm; k < n - CornerArm; k++) turn[k] = TurnDeg(dense[k - CornerArm], dense[k], dense[k + CornerArm]);
        for (var k = CornerArm; k < n - CornerArm; k++)
        {
            if (turn[k] < CornerDeg) continue;
            var peak = true;
            for (var j = Math.Max(CornerArm, k - CornerArm); j <= Math.Min(n - CornerArm - 1, k + CornerArm) && peak; j++)
            {
                if (turn[j] > turn[k] || (turn[j] == turn[k] && j < k)) peak = false;
            }
            if (!peak) continue;
            // A corner keeps its turn when looked at from further away; a jog
            // turns back, so over a longer arm it hardly turns at all.
            var far = Math.Min(2 * CornerArm, Math.Min(k, n - 1 - k));
            if (far > CornerArm && TurnDeg(dense[k - far], dense[k], dense[k + far]) < 0.6 * turn[k]) continue;
            found.Add((k, dense[k], turn[k]));
        }
        return found;
    }

    private static double TurnDeg(StrokePoint a, StrokePoint b, StrokePoint c)
    {
        var before = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var after = Math.Atan2(c.Y - b.Y, c.X - b.X);
        var d = Math.Abs(after - before);
        if (d > Math.PI) d = 2 * Math.PI - d;
        return d * 180 / Math.PI;
    }

    // ---------------------------------------------------------- where lines meet

    /// <summary>
    /// Fix the last end of <paramref name="pts"/>: cut it back if it runs a
    /// little past a line it crosses, or carry it on if it stops just short of
    /// one it is heading for.
    /// </summary>
    private static (List<StrokePoint> Points, CleanupKind? Kind) FixEnd(
        List<StrokePoint> pts, Stroke s, List<(Stroke s, List<StrokePoint> Pts)> others, double cap, Style style)
    {
        var size = s.Brush.Size;
        var overshootMax = Math.Max(3 * size, 8);
        var length = GeometryOps.PathLength(pts);

        // Overshoot: the nearest crossing to the end, within reach of it.
        if (style.TrimOvershoots)
        {
            var walked = 0.0;
            for (var i = pts.Count - 1; i > 0 && walked <= overshootMax; i--)
            {
                var a = pts[i - 1];
                var b = pts[i];
                foreach (var (o, op) in others)
                {
                    for (var j = 1; j < op.Count; j++)
                    {
                        if (Cross(a, b, op[j - 1], op[j]) is not { } hit) continue;
                        if (Angle(a, b, op[j - 1], op[j]) < ShallowDeg) continue;
                        var tail = walked + GeometryOps.Dist(hit.P, b);
                        if (tail <= o.Brush.Size * 0.5 + 0.5 || tail > overshootMax) continue;
                        if (length - tail < 3 * tail) continue;
                        var cut = pts.GetRange(0, i);
                        cut.Add(GeometryOps.LerpPoint(a, b, hit.T) with { TiltX = b.TiltX, TiltY = b.TiltY, Speed = b.Speed });
                        return (cut, CleanupKind.Trimmed);
                    }
                }
                walked += GeometryOps.Dist(a, b);
            }
        }

        // Gap: the nearest other line ahead of the end, close enough to mean it.
        if (style.CloseGaps)
        {
            var end = pts[^1];
            var back = PointBack(pts, Math.Min(6, length / 2));
            var gapMax = Math.Min(Math.Max(2 * size, 6), cap * 2);
            StrokePoint? best = null;
            var bestD = double.MaxValue;
            var bestSize = 0.0;
            foreach (var (o, op) in others)
            {
                for (var j = 1; j < op.Count; j++)
                {
                    var q = Nearest(end, op[j - 1], op[j]);
                    var d = GeometryOps.Dist(end, q);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = q;
                        bestSize = o.Brush.Size;
                    }
                }
            }
            if (best is { } target && bestD <= gapMax)
            {
                var touching = 0.5 * (size * Math.Max(end.Pressure, 0.3) + bestSize) * 0.5;
                var heading = HeadingDeg(back, end, target);
                if (bestD > touching && heading <= 60)
                {
                    var joined = new List<StrokePoint>(pts) { end with { X = target.X, Y = target.Y } };
                    return (joined, CleanupKind.Joined);
                }
            }
        }
        return (pts, null);
    }

    /// <summary>
    /// How a set of lines tends to meet: of the ends that come near another
    /// line, how many run past it and how many stop short.
    /// </summary>
    private static (int Overshoot, int Gap, int Near) JunctionHabits(List<Stroke> lines)
    {
        int over = 0, gap = 0, near = 0;
        var style = new Style([], 1, true, true, null);
        foreach (var s in lines)
        {
            if (s.Points.Count < 2) continue;
            var others = lines.Where(o => o.Id != s.Id).Select(o => (o, o.Points)).ToList();
            foreach (var atStart in new[] { false, true })
            {
                var pts = atStart ? s.Points.AsEnumerable().Reverse().ToList() : s.Points;
                var (_, kind) = FixEnd(pts, s, others, Cap(s), style);
                if (kind == CleanupKind.Trimmed) over++;
                if (kind == CleanupKind.Joined) gap++;
                if (kind is not null || TouchesAnother(pts[^1], others)) near++;
            }
        }
        return (over, gap, near);
    }

    private static bool TouchesAnother(StrokePoint end, List<(Stroke s, List<StrokePoint> Points)> others)
    {
        foreach (var (o, op) in others)
        {
            for (var j = 1; j < op.Count; j++)
            {
                if (GeometryOps.DistToSegment(end, op[j - 1], op[j]) <= Math.Max(o.Brush.Size, 2)) return true;
            }
        }
        return false;
    }

    private static (StrokePoint P, double T)? Cross(StrokePoint a, StrokePoint b, StrokePoint c, StrokePoint d)
    {
        var rx = b.X - a.X; var ry = b.Y - a.Y;
        var sx = d.X - c.X; var sy = d.Y - c.Y;
        var den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-12) return null;
        var t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
        var u = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / den;
        if (t < 0 || t > 1 || u < 0 || u > 1) return null;
        return (GeometryOps.LerpPoint(a, b, t), t);
    }

    private static double Angle(StrokePoint a, StrokePoint b, StrokePoint c, StrokePoint d)
    {
        var u = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var v = Math.Atan2(d.Y - c.Y, d.X - c.X);
        var diff = Math.Abs(u - v) % Math.PI;
        if (diff > Math.PI / 2) diff = Math.PI - diff;
        return diff * 180 / Math.PI;
    }

    private static StrokePoint Nearest(StrokePoint p, StrokePoint a, StrokePoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return GeometryOps.LerpPoint(a, b, t);
    }

    private static double HeadingDeg(StrokePoint back, StrokePoint end, StrokePoint target)
    {
        var u = Math.Atan2(end.Y - back.Y, end.X - back.X);
        var v = Math.Atan2(target.Y - end.Y, target.X - end.X);
        var d = Math.Abs(u - v);
        if (d > Math.PI) d = 2 * Math.PI - d;
        return d * 180 / Math.PI;
    }

    /// <summary>The point <paramref name="distance"/> back along the line from its end.</summary>
    private static StrokePoint PointBack(List<StrokePoint> pts, double distance)
    {
        var walked = 0.0;
        for (var i = pts.Count - 1; i > 0; i--)
        {
            var seg = GeometryOps.Dist(pts[i - 1], pts[i]);
            if (walked + seg >= distance && seg > 0) return GeometryOps.LerpPoint(pts[i], pts[i - 1], (distance - walked) / seg);
            walked += seg;
        }
        return pts[0];
    }

    // ---------------------------------------------------------------- measures

    /// <summary>
    /// How far a set of lines strays from its own smooth course: the given
    /// quantile of every dense point's distance from a Gaussian-smoothed copy,
    /// on lines long enough to have a course. A guided clean leaves strays up
    /// to this alone.
    /// </summary>
    internal static double Stray(IReadOnlyList<Stroke> lines, double sigma, double quantile)
    {
        var all = new List<double>();
        foreach (var s in lines)
        {
            if (s.Points.Count < 2) continue;
            var course = new Course(s.Points);
            if (course.Length < 20 * Step) continue;
            // The body of the line only: a pen's flick at either end is
            // measured against an end held still, and would pass every hook
            // off as the line's ordinary character.
            var margin = Math.Min(2 * sigma, course.Length / 4);
            foreach (var p in course.Dense.Select((q, k) => (q, At: k * course.DenseStep)))
            {
                if (p.At < margin || p.At > course.Length - margin) continue;
                var (cx, cy) = course.SmoothAt(p.At, sigma, 0, course.Length);
                all.Add(Math.Sqrt(Math.Pow(cx - p.q.X, 2) + Math.Pow(cy - p.q.Y, 2)));
            }
        }
        if (all.Count == 0) return 0.5;
        all.Sort();
        return Math.Max(all[(int)Math.Min(all.Count - 1, quantile * all.Count)], 0.25);
    }

    private static double MaxMove(IReadOnlyList<StrokePoint> before, IReadOnlyList<StrokePoint> after)
    {
        var m = 0.0;
        for (var i = 0; i < Math.Min(before.Count, after.Count); i++) m = Math.Max(m, GeometryOps.Dist(before[i], after[i]));
        return m;
    }

    private static double MaxEndMove(IReadOnlyList<StrokePoint> before, IReadOnlyList<StrokePoint> after) =>
        Math.Max(GeometryOps.Dist(before[0], after[0]), GeometryOps.Dist(before[^1], after[^1]));
}
