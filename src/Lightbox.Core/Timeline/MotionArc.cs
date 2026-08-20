namespace Lightbox.Core.Timeline;

/// <summary>A point on or beside the fitted arc, in document space.</summary>
public readonly record struct ArcSample(double X, double Y);

/// <summary>
/// A tick the fit says is off the arc: where the drawing's subject is, and the
/// nearest point on the arc — the foot — so the overlay can show not just that
/// a drawing breaks the arc but where it would sit if it did not.
/// </summary>
public readonly record struct OffArcTick(double X, double Y, double FootX, double FootY, string FrameId);

/// <summary>
/// Everything the arc overlay draws, computed once per refresh. The parts are
/// separable on purpose: the path and the off-arc ticks are the *motion arcs*
/// item, the extension and the two predicted samples are *arc prediction*, and
/// the view model strips whichever half the artist has not asked for.
/// </summary>
/// <param name="Path">The fitted curve sampled across the ticks' span.</param>
/// <param name="Extension">
/// The curve continued from the last tick to <paramref name="Next"/>, for the
/// painter to dash — empty when there is no next prediction.
/// </param>
/// <param name="OffArc">Ticks whose distance from the fit exceeds the tolerance.</param>
/// <param name="Next">
/// Where the drawing after the last tick should land, or null — when a real
/// drawing already exists there, when the subject has stalled, or when the
/// caller did not ask.
/// </param>
/// <param name="Current">
/// Where the playhead's own drawing should put its subject, when the playhead
/// stands between ticks with no tick of its own — the inbetweening moment.
/// </param>
public sealed record MotionArcOverlay(
    IReadOnlyList<ArcSample> Path,
    IReadOnlyList<ArcSample> Extension,
    IReadOnlyList<OffArcTick> OffArc,
    ArcSample? Next,
    ArcSample? Current);

/// <summary>
/// Fits a smooth arc to the motion trail's ticks and reads it back to the
/// artist: the arc itself, which drawings sit off it, and where the next or
/// missing drawing belongs on it (Q133).
/// </summary>
/// <remarks>
/// <para>
/// The model is a least-squares circle degrading to a total-least-squares
/// line — Q133's answer. Classic animation arcs (swings, pendulums, turns)
/// are near-circular, and a low-order fit is what lets "off-arc" mean
/// something: a spline through every tick can never disagree with one. The
/// parabola for ballistic motion belongs to the jump arc analyzer, and a
/// model picker arrives with the second model.
/// </para>
/// <para>
/// Pure, view-only and deterministic: same ticks in, same overlay out, with
/// nothing read from the clock or an RNG. Invariant 2 is not in play — nothing
/// here reaches a pixel of the record — but the inbetweener may one day read
/// the same prediction, and a suggestion that moves between asks is not a
/// suggestion.
/// </para>
/// </remarks>
public static class MotionArc
{
    /// <summary>
    /// Off-arc tolerance as a fraction of the trail's travelled length. A
    /// wobble reads relative to the size of the motion: three pixels off is
    /// the whole arc on a settle and nothing on a screen-wide throw.
    /// </summary>
    private const double ToleranceOfSpan = 0.03;

    /// <summary>
    /// The floor under the tolerance, in document pixels — beneath this the
    /// derived stroke-bounds centre wobbles more than the drawing does, and
    /// flagging it would send the artist chasing the fallback, not the arc.
    /// </summary>
    private const double ToleranceFloor = 1.0;

    /// <summary>
    /// How much faster or slower than the previous gap the predicted spacing
    /// may continue — an ease carries on, a cliff does not extrapolate.
    /// </summary>
    private const double TrendClamp = 2.0;

    /// <summary>
    /// Fit the arc through the trail's ticks and read the predictions off it.
    /// Null when fewer than three ticks — two positions are a chord, and a
    /// chord agrees with every arc through its ends.
    /// </summary>
    /// <param name="playheadIndex">
    /// The timeline position the playhead stands on, for the empty-current
    /// prediction. Ignored when some tick is already Current.
    /// </param>
    /// <param name="predictNext">
    /// Whether the drawing after the last tick is missing. The caller knows
    /// the layer; the fit only knows the ticks, and a predicted tick must
    /// never cover a real drawing — where one exists, the off-arc judgement
    /// speaks instead.
    /// </param>
    public static MotionArcOverlay? Fit(
        IReadOnlyList<TrailPoint> points, int playheadIndex, bool predictNext)
    {
        if (points.Count < 3) return null;

        // The tolerance is read off the ticks' own polyline, not the fit —
        // a tolerance that moved with the curve would judge a drawing by how
        // much it managed to drag the judge.
        var travelled = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            travelled += Math.Sqrt(dx * dx + dy * dy);
        }
        var tolerance = Math.Max(ToleranceFloor, ToleranceOfSpan * travelled);

        // A least-squares fit pulls toward an outlier and spreads the blame:
        // one drawing 14 px off can put every tick past the tolerance, and it
        // bends the fit enough that the *largest* deviator is often an
        // innocent tick at the trail's edge. So while the kept ticks disagree
        // with their own arc, the tick whose absence most improves the fit
        // leaves — not the one the bent fit happens to blame — and every tick
        // is judged against the arc the rest agree on.
        var kept = new List<TrailPoint>(points);
        if (Curve.Fit(kept) is not { } curve) return null;
        while (kept.Count > 3 && WorstOf(curve, kept) > tolerance)
        {
            var bestAt = -1;
            var bestWorst = double.MaxValue;
            var bestCurve = curve;
            for (var i = 0; i < kept.Count; i++)
            {
                var candidate = new List<TrailPoint>(kept);
                candidate.RemoveAt(i);
                if (Curve.Fit(candidate) is not { } refit) continue;
                var worst = WorstOf(refit, candidate);
                if (worst >= bestWorst) continue;
                bestWorst = worst;
                bestAt = i;
                bestCurve = refit;
            }
            if (bestAt < 0) break;
            kept.RemoveAt(bestAt);
            curve = bestCurve;
        }

        // Signed arc-length parameter per tick, in tick (= timeline) order.
        var s = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
            s[i] = curve.ParamOf(points[i].X, points[i].Y, i > 0 ? s[i - 1] : (double?)null);

        var path = curve.Sample(s[0], s[^1]);

        var offArc = new List<OffArcTick>();
        foreach (var p in points)
        {
            if (curve.DistanceTo(p.X, p.Y) <= tolerance) continue;
            var foot = curve.FootOf(p.X, p.Y);
            offArc.Add(new OffArcTick(p.X, p.Y, foot.X, foot.Y, p.FrameId));
        }

        ArcSample? next = null;
        IReadOnlyList<ArcSample> extension = [];
        if (predictNext && NextParam(s) is { } sNext)
        {
            next = curve.PointAt(sNext);
            extension = curve.Sample(s[^1], sNext);
        }

        var hasCurrent = false;
        foreach (var p in points) hasCurrent |= p.Current;
        ArcSample? current = null;
        if (!hasCurrent && CurrentParam(points, s, playheadIndex) is { } sHere)
            current = curve.PointAt(sHere);

        return new MotionArcOverlay(path, extension, offArc, next, current);
    }

    /// <summary>The largest distance from the curve among the given ticks.</summary>
    private static double WorstOf(in Curve curve, List<TrailPoint> points)
    {
        var worst = 0.0;
        foreach (var p in points)
            worst = Math.Max(worst, curve.DistanceTo(p.X, p.Y));
        return worst;
    }

    /// <summary>
    /// Continue the spacing past the last tick: the last gap, scaled by the
    /// trend of the two gaps before it so an ease keeps easing, clamped so a
    /// spike does not launch the prediction off the canvas. Null when the
    /// subject has stalled — a stopped subject's next position is a timing
    /// decision, not an extrapolation — or when the motion just reversed,
    /// where the trend ratio would compare travel in opposite directions.
    /// </summary>
    private static double? NextParam(double[] s)
    {
        var last = s[^1] - s[^2];
        if (Math.Abs(last) < 1e-9) return null;
        var trend = 1.0;
        var previous = s[^2] - s[^3];
        if (Math.Sign(previous) == Math.Sign(last))
            trend = Math.Clamp(Math.Abs(last / previous), 1.0 / TrendClamp, TrendClamp);
        return s[^1] + last * trend;
    }

    /// <summary>
    /// Where along the arc the playhead's missing drawing belongs: between its
    /// bracketing ticks, proportional to timeline position — even cels get an
    /// even split, a cel close to its neighbour lands close to it. Reading an
    /// authored timing chart instead belongs to the spacing assistant.
    /// </summary>
    private static double? CurrentParam(IReadOnlyList<TrailPoint> points, double[] s, int playheadIndex)
    {
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i - 1].Index >= playheadIndex || points[i].Index <= playheadIndex) continue;
            var t = (playheadIndex - points[i - 1].Index)
                / (double)(points[i].Index - points[i - 1].Index);
            return s[i - 1] + (s[i] - s[i - 1]) * t;
        }
        return null;
    }

    /// <summary>
    /// The fitted curve: a circle, or a line where the ticks are too straight
    /// for one. One type rather than two so every consumer above asks for a
    /// parameter, a point, a foot or a distance and never which case it is in.
    /// </summary>
    private readonly struct Curve
    {
        // Circle: centre (Cx, Cy), radius R; param is signed arc length from
        // angle zero. Line: origin (Cx, Cy), unit direction (Dx, Dy), R = 0;
        // param is the projection onto the direction.
        private readonly double Cx, Cy, R, Dx, Dy;
        private bool IsCircle => R > 0;

        private Curve(double cx, double cy, double r, double dx, double dy)
            => (Cx, Cy, R, Dx, Dy) = (cx, cy, r, dx, dy);

        /// <summary>
        /// Kåsa least-squares circle, falling back to the principal-axis line
        /// when the ticks are collinear or the fitted radius is so large the
        /// arc is straight at the trail's own scale. Null only when every
        /// tick is one point — a subject that never moved has no arc.
        /// </summary>
        public static Curve? Fit(IReadOnlyList<TrailPoint> points)
        {
            double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
            var n = points.Count;
            foreach (var p in points)
            {
                var z = p.X * p.X + p.Y * p.Y;
                sx += p.X; sy += p.Y;
                sxx += p.X * p.X; syy += p.Y * p.Y; sxy += p.X * p.Y;
                sxz += p.X * z; syz += p.Y * z; sz += z;
            }

            // Span for the degeneracy thresholds: the ticks' bounding diagonal.
            double minX = double.MaxValue, minY = double.MaxValue,
                maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            var span = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
            if (span < 1e-9) return null;

            // Solve  [sxx sxy sx][a]   [sxz]
            //        [sxy syy sy][b] = [syz]   for x²+y² = ax + by + c.
            //        [sx  sy  n ][c]   [sz ]
            var det = sxx * (syy * n - sy * sy) - sxy * (sxy * n - sy * sx) + sx * (sxy * sy - syy * sx);
            // The determinant scales with coordinate^6; normalise by span^4·n²
            // so "collinear" means the same thing at every canvas size.
            if (Math.Abs(det) > 1e-6 * span * span * span * span * n * n)
            {
                var a = (sxz * (syy * n - sy * sy) - sxy * (syz * n - sy * sz) + sx * (syz * sy - syy * sz)) / det;
                var b = (sxx * (syz * n - sy * sz) - sxz * (sxy * n - sy * sx) + sx * (sxy * sz - syz * sx)) / det;
                var c = (sxx * (syy * sz - syz * sy) - sxy * (sxy * sz - syz * sx) + sxz * (sxy * sy - syy * sx)) / det;
                var cx = a / 2;
                var cy = b / 2;
                var r = Math.Sqrt(Math.Max(0, c + cx * cx + cy * cy));
                // A radius dwarfing the trail is a straight line wearing a
                // circle's numbers, and its angles are too small to carry
                // parameters honestly.
                if (r > 1e-9 && r < 50 * span) return new Curve(cx, cy, r, 0, 0);
            }

            var mx = sx / n;
            var my = sy / n;
            var cxx = sxx / n - mx * mx;
            var cyy = syy / n - my * my;
            var cxy = sxy / n - mx * my;
            var angle = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
            return new Curve(mx, my, 0, Math.Cos(angle), Math.Sin(angle));
        }

        /// <summary>
        /// The tick's signed arc-length parameter. On the circle the angle is
        /// unwrapped against the previous tick's parameter so a trail crossing
        /// the ±π seam stays monotone instead of jumping a full turn.
        /// </summary>
        public double ParamOf(double x, double y, double? previous)
        {
            if (!IsCircle) return (x - Cx) * Dx + (y - Cy) * Dy;
            var s = Math.Atan2(y - Cy, x - Cx) * R;
            if (previous is { } prior)
            {
                var turn = 2 * Math.PI * R;
                while (s - prior > turn / 2) s -= turn;
                while (prior - s > turn / 2) s += turn;
            }
            return s;
        }

        public ArcSample PointAt(double s)
        {
            if (!IsCircle) return new ArcSample(Cx + Dx * s, Cy + Dy * s);
            var angle = s / R;
            return new ArcSample(Cx + R * Math.Cos(angle), Cy + R * Math.Sin(angle));
        }

        public double DistanceTo(double x, double y)
        {
            if (!IsCircle) return Math.Abs((x - Cx) * Dy - (y - Cy) * Dx);
            var dx = x - Cx;
            var dy = y - Cy;
            return Math.Abs(Math.Sqrt(dx * dx + dy * dy) - R);
        }

        public ArcSample FootOf(double x, double y)
        {
            if (!IsCircle) return PointAt((x - Cx) * Dx + (y - Cy) * Dy);
            var dx = x - Cx;
            var dy = y - Cy;
            var d = Math.Sqrt(dx * dx + dy * dy);
            // A tick exactly on the centre has no nearest point; every one is
            // equally near. It cannot be off-arc anyway at any sane radius.
            if (d < 1e-9) return new ArcSample(x, y);
            return new ArcSample(Cx + dx / d * R, Cy + dy / d * R);
        }

        /// <summary>
        /// The curve between two parameters as a polyline. Enough segments
        /// that a circle reads as a curve at any trail size; a line is two
        /// points however it is asked.
        /// </summary>
        public IReadOnlyList<ArcSample> Sample(double from, double to)
        {
            var segments = IsCircle ? Math.Clamp((int)Math.Ceiling(Math.Abs(to - from) / R * 16), 4, 96) : 1;
            var samples = new ArcSample[segments + 1];
            for (var i = 0; i <= segments; i++)
                samples[i] = PointAt(from + (to - from) * i / segments);
            return samples;
        }
    }
}
