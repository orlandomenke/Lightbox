using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>One sample of the fitted arc, for the overlay to draw through.</summary>
public readonly record struct ArcPoint(double X, double Y);

/// <summary>
/// One drawing judged against the fitted arc: where its subject is, where the
/// fit says a ballistic subject would be at that frame, and how far apart the
/// two are.
/// </summary>
public readonly record struct ArcDeviation(
    int Index, string FrameId,
    double X, double Y,
    double FitX, double FitY,
    double Distance, bool OffArc);

/// <summary>
/// The fit over one run: the sampled curve, every drawing's deviation from
/// it, and whether the run reads as ballistic at all.
/// </summary>
/// <param name="Ballistic">
/// True when the fitted vertical acceleration points down (positive, in
/// screen coordinates) — the parabola has an apex. A run that fits best with
/// gravity pulling up is not a jump, and its deviations are judged against a
/// curve that means nothing.
/// </param>
/// <param name="Tolerance">The distance past which a drawing was flagged, for a readout that wants to say so.</param>
public sealed record JumpArcFit(
    IReadOnlyList<ArcPoint> Curve,
    IReadOnlyList<ArcDeviation> Deviations,
    bool Ballistic,
    double Tolerance);

/// <summary>
/// Fits a gravity arc to the run the playhead is in (Q134): horizontal
/// position linear in time, vertical position quadratic — constant velocity
/// and constant acceleration, which is all a thrown thing does. The drawings
/// that sit off the fit are the ones that will read as a bump at speed.
/// </summary>
/// <remarks>
/// <para>
/// Time is the cel index, so a run on 2s is fitted with its real timing —
/// two frames of fall between drawings, not one. The run is
/// <see cref="ExposureSheet.RunAt"/>'s, extreme to extreme; picking the
/// airborne stretch automatically is the separate contact-detection item,
/// and until it lands the artist frames the jump the way they already frame
/// everything else: with extremes.
/// </para>
/// <para>
/// The fit is closed-form least squares, run twice: once over everything,
/// then again without the single worst drawing. A plain fit is dragged
/// toward the drawing that is off, which both shrinks that drawing's
/// residual and smears blame onto its neighbours — measured on six points
/// with one 40 px bump, four of them flagged. Leaving the worst out lets the
/// other drawings say where the arc is, and the bumped one is judged against
/// the arc it actually broke. Deterministic, no iteration beyond that one
/// pass, no seed.
/// </para>
/// <para>
/// Fewer than <see cref="MinDrawings"/> located drawings returns null rather
/// than a verdict: three points fit any parabola exactly, so the first
/// honest judgement needs a fourth.
/// </para>
/// </remarks>
public static class JumpArcAnalyser
{
    /// <summary>The fewest drawings a fit can honestly judge.</summary>
    public const int MinDrawings = 4;

    /// <summary>A drawing is off the arc past this share of the run's extent…</summary>
    public const double FlagShare = 0.04;

    /// <summary>…with the same floor the spacing assistant keeps, for the same noise.</summary>
    public const double FlagFloorPx = 1.0;

    /// <summary>How many curve samples per frame of run — smooth at any zoom without being data.</summary>
    private const int SamplesPerFrame = 4;

    private readonly record struct Coefficients(double P, double Q, double A, double B, double C)
    {
        public double X(double t) => P + Q * t;

        public double Y(double t) => (A * t + B) * t + C;

        public double DistanceTo(double t, double x, double y)
        {
            var dx = x - X(t);
            var dy = y - Y(t);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Fit the run containing <paramref name="index"/>. Null when the run has
    /// too few located drawings or no spread in time to fit against.
    /// </summary>
    public static JumpArcFit? FitRun(Scene scene, Layer layer, int index)
    {
        var run = ExposureSheet.RunAt(layer, index);
        var located = new List<(int Index, string Id, double X, double Y)>(run.Count);
        foreach (var cel in run)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, cel) is not { } frame) continue;
            if (MotionTrail.Locate(scene, frame) is not { } at) continue;
            located.Add((cel, frame.Id, at.X, at.Y));
        }
        located = Airborne(scene, located, index);
        if (located.Count < MinDrawings) return null;

        // Time relative to the run's first drawing keeps the normal-equation
        // sums small; the fit is the same parabola either way.
        var t0 = located[0].Index;

        if (Fit(located, t0, skip: -1) is not { } first) return null;

        // The worst drawing sits out and the rest re-vote on where the arc is.
        var worst = 0;
        double worstDistance = -1;
        for (var j = 0; j < located.Count; j++)
        {
            var d = first.DistanceTo(located[j].Index - t0, located[j].X, located[j].Y);
            if (d > worstDistance) { worstDistance = d; worst = j; }
        }
        var fit = Fit(located, t0, skip: worst) ?? first;

        // Extent-relative tolerance: a hand's width off a screen-wide leap and
        // a pixel off a hop should read the same.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var pt in located)
        {
            minX = Math.Min(minX, pt.X); maxX = Math.Max(maxX, pt.X);
            minY = Math.Min(minY, pt.Y); maxY = Math.Max(maxY, pt.Y);
        }
        var extent = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        var tolerance = Math.Max(FlagShare * extent, FlagFloorPx);

        var deviations = new List<ArcDeviation>(located.Count);
        foreach (var pt in located)
        {
            double t = pt.Index - t0;
            var d = fit.DistanceTo(t, pt.X, pt.Y);
            deviations.Add(new ArcDeviation(
                pt.Index, pt.Id, pt.X, pt.Y, fit.X(t), fit.Y(t), d, d > tolerance));
        }

        var span = located[^1].Index - t0;
        var curve = new List<ArcPoint>(span * SamplesPerFrame + 1);
        for (var k = 0; k <= span * SamplesPerFrame; k++)
        {
            var t = (double)k / SamplesPerFrame;
            curve.Add(new ArcPoint(fit.X(t), fit.Y(t)));
        }

        // Screen Y grows downward, so gravity is a positive quadratic term.
        return new JumpArcFit(curve, deviations, Ballistic: fit.A > 0, tolerance);
    }

    /// <summary>
    /// The airborne stretch (Q135): where the run carries contact markers, a
    /// planted drawing is not ballistic and must not vote on the arc, so the
    /// run splits at the marked frames and the stretch the playhead stands in
    /// is what gets fitted. Authored markers, not re-detection — the artist's
    /// statement (or the detect command's, once accepted into the record) is
    /// what the fit obeys, and correcting a wrong split is editing a marker
    /// rather than arguing with a heuristic. With no contact markers in the
    /// run, the whole run fits as before.
    /// </summary>
    private static List<(int Index, string Id, double X, double Y)> Airborne(
        Scene scene, List<(int Index, string Id, double X, double Y)> located, int index)
    {
        if (located.Count == 0) return located;
        var marked = ContactFrames.MarkedIn(scene, located[0].Index, located[^1].Index);
        if (marked.Count == 0) return located;

        var contacts = marked.ToHashSet();
        var segments = new List<List<(int Index, string Id, double X, double Y)>>();
        var current = new List<(int Index, string Id, double X, double Y)>();
        foreach (var p in located)
        {
            if (contacts.Contains(p.Index))
            {
                if (current.Count > 0) segments.Add(current);
                current = [];
                continue;
            }
            current.Add(p);
        }
        if (current.Count > 0) segments.Add(current);
        if (segments.Count == 0) return [];

        // The stretch the playhead is in; standing on a contact frame itself,
        // the nearest stretch — earlier on a tie, reading order's answer.
        foreach (var segment in segments)
        {
            if (index >= segment[0].Index && index <= segment[^1].Index) return segment;
        }
        return segments
            .OrderBy(s => Math.Min(Math.Abs(index - s[0].Index), Math.Abs(index - s[^1].Index)))
            .ThenBy(s => s[0].Index)
            .First();
    }

    /// <summary>
    /// Ordinary least squares for x = p + q·t and y = a·t² + b·t + c, with one
    /// drawing optionally sat out. Null when the times are too degenerate to
    /// invert — which a run of distinct cel indices never is.
    /// </summary>
    private static Coefficients? Fit(
        List<(int Index, string Id, double X, double Y)> located, int t0, int skip)
    {
        var n = 0;
        double s1 = 0, s2 = 0, s3 = 0, s4 = 0, sx = 0, stx = 0, sy = 0, sty = 0, st2y = 0;
        for (var j = 0; j < located.Count; j++)
        {
            if (j == skip) continue;
            double t = located[j].Index - t0;
            var tt = t * t;
            n++;
            s1 += t; s2 += tt; s3 += tt * t; s4 += tt * tt;
            sx += located[j].X; stx += t * located[j].X;
            sy += located[j].Y; sty += t * located[j].Y; st2y += tt * located[j].Y;
        }

        var xDet = n * s2 - s1 * s1;
        if (Math.Abs(xDet) < 1e-9) return null;
        var q = (n * stx - s1 * sx) / xDet;
        var p = (sx - q * s1) / n;

        var det = Det3(s4, s3, s2, s3, s2, s1, s2, s1, n);
        if (Math.Abs(det) < 1e-9) return null;
        var a = Det3(st2y, s3, s2, sty, s2, s1, sy, s1, n) / det;
        var b = Det3(s4, st2y, s2, s3, sty, s1, s2, sy, n) / det;
        var c = Det3(s4, s3, st2y, s3, s2, sty, s2, s1, sy) / det;
        return new Coefficients(p, q, a, b, c);
    }

    private static double Det3(
        double a1, double a2, double a3,
        double b1, double b2, double b3,
        double c1, double c2, double c3) =>
        a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1) + a3 * (b1 * c2 - b2 * c1);
}
