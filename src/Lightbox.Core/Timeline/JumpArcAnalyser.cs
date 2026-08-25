using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>One sample of the fitted arc, in world coordinates, for the overlay to draw through.</summary>
public readonly record struct ArcPoint(double X, double Y);

/// <summary>
/// One drawing judged against the fitted arc: where its subject is (world
/// coordinates, so the overlay can ring it), where the fit says a ballistic
/// subject would be at that frame, and how far apart the two are — measured
/// in the space the fit ran in.
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
/// True when the fitted vertical acceleration points at the ground — the
/// parabola has an apex. A run that fits best with gravity pulling up is not
/// a jump, and its deviations are judged against a curve that means nothing.
/// </param>
/// <param name="Tolerance">The distance past which a drawing was flagged, for a readout that wants to say so.</param>
/// <param name="DepthMotion">
/// The subject changes size across the run past the depth band (Q137): the
/// flat fit does not apply, so nothing is flagged and the readout hedges.
/// </param>
/// <param name="ThroughCamera">
/// The fit ran on camera-projected positions — "as the camera sees it"
/// (Q137). The curve is empty in this mode (a between-frames sample has no
/// framing of its own), and the verdict is information about the read, not
/// about the world: correct gravity can legitimately fail to read as an arc
/// under a camera move.
/// </param>
public sealed record JumpArcFit(
    IReadOnlyList<ArcPoint> Curve,
    IReadOnlyList<ArcDeviation> Deviations,
    bool Ballistic,
    double Tolerance,
    bool DepthMotion = false,
    bool ThroughCamera = false);

/// <summary>
/// Fits a gravity arc to the run the playhead is in (Q134): position along
/// the ground linear in time, height above it quadratic — constant velocity
/// and constant acceleration, which is all a thrown thing does. The drawings
/// that sit off the fit are the ones that will read as a bump at speed.
/// </summary>
/// <remarks>
/// <para>
/// Since Q137 the axes are the ground's, not the canvas's: the artist's
/// "ground" line guide at any angle decides which way gravity pulls, with the
/// canvas-horizontal as the fallback — so a jump on a slope or a tilted
/// layout fits the parabola it actually describes. Through the camera, the
/// axes are the camera's instead, per frame: does the motion read as an arc
/// where the audience is looking.
/// </para>
/// <para>
/// Time is the cel index, so a run on 2s is fitted with its real timing —
/// two frames of fall between drawings, not one. The run is
/// <see cref="ExposureSheet.RunAt"/>'s, extreme to extreme, trimmed to the
/// airborne stretch between contact markers when the run carries them (Q135).
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

    /// <summary>Past this ratio between the biggest and smallest drawing, the run reads as depth motion (Q137).</summary>
    public const double DepthBand = WalkCycleAnalyser.DepthBand;

    /// <summary>How many curve samples per frame of run — smooth at any zoom without being data.</summary>
    private const int SamplesPerFrame = 4;

    private readonly record struct Coefficients(double P, double Q, double A, double B, double C)
    {
        public double U(double t) => P + Q * t;

        public double V(double t) => (A * t + B) * t + C;

        public double DistanceTo(double t, double u, double v)
        {
            var du = u - U(t);
            var dv = v - V(t);
            return Math.Sqrt(du * du + dv * dv);
        }
    }

    /// <summary>
    /// Fit the run containing <paramref name="index"/>. Null when the run has
    /// too few located drawings or no spread in time to fit against.
    /// </summary>
    public static JumpArcFit? FitRun(Scene scene, Layer layer, int index, bool throughCamera = false)
    {
        var run = ExposureSheet.RunAt(layer, index);
        var located = new List<(int Index, string Id, double X, double Y, double Size)>(run.Count);
        foreach (var cel in run)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, cel) is not { } frame) continue;
            var b = MotionTrail.InkBounds(frame);
            if (MotionTrail.Locate(scene, frame, b) is not { } at) continue;
            // An anchored drawing with no ink still has a subject; its size is
            // simply unknown (0), which the depth guard treats as "cannot say".
            var size = b is { } bb
                ? Math.Sqrt((bb.MaxX - bb.MinX) * (bb.MaxX - bb.MinX) + (bb.MaxY - bb.MinY) * (bb.MaxY - bb.MinY))
                : 0;
            located.Add((cel, frame.Id, at.X, at.Y, size));
        }
        located = Airborne(scene, located, index);
        if (located.Count < MinDrawings) return null;

        var ground = Ground.Resolve(scene);

        // Every drawing's position in the fit's axes: (along, up) of the
        // ground in the world, or the camera's view per frame — up-positive
        // either way, so an apex is always a maximum and gravity always A < 0.
        var samples = new List<(double T, double U, double V, double Size)>(located.Count);
        var t0 = located[0].Index;
        var camera = false;
        foreach (var p in located)
        {
            double u, v;
            var size = p.Size;
            if (throughCamera && CameraView.FramingAt(scene, p.Index) is { } f)
            {
                var (vx, vy) = CameraView.Project(f, p.X, p.Y);
                u = vx;
                v = -vy;
                size *= f.Zoom;   // apparent size is what depth means on screen
                camera = true;
            }
            else
            {
                u = ground.Along(p.X, p.Y);
                v = ground.Elevation(p.X, p.Y);
            }
            samples.Add((p.Index - t0, u, v, size));
        }

        // Depth first (Q137): a subject changing size is moving toward or
        // away from camera, and a flat parabola does not apply — nothing is
        // flagged against a fit that cannot mean anything.
        var sizes = samples.Select(s => s.Size).ToList();
        var depth = sizes.Min() > 0 && sizes.Max() / sizes.Min() > DepthBand;

        if (Fit(samples, skip: -1) is not { } first) return null;

        // The worst drawing sits out and the rest re-vote on where the arc is.
        var worst = 0;
        double worstDistance = -1;
        for (var j = 0; j < samples.Count; j++)
        {
            var d = first.DistanceTo(samples[j].T, samples[j].U, samples[j].V);
            if (d > worstDistance) { worstDistance = d; worst = j; }
        }
        var fit = Fit(samples, skip: worst) ?? first;

        // Extent-relative tolerance: a hand's width off a screen-wide leap and
        // a pixel off a hop should read the same.
        double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
        foreach (var s in samples)
        {
            minU = Math.Min(minU, s.U); maxU = Math.Max(maxU, s.U);
            minV = Math.Min(minV, s.V); maxV = Math.Max(maxV, s.V);
        }
        var extent = Math.Sqrt((maxU - minU) * (maxU - minU) + (maxV - minV) * (maxV - minV));
        var tolerance = Math.Max(FlagShare * extent, FlagFloorPx);

        var deviations = new List<ArcDeviation>(located.Count);
        for (var j = 0; j < located.Count; j++)
        {
            var s = samples[j];
            var d = fit.DistanceTo(s.T, s.U, s.V);
            var (fx, fy) = FitPointInWorld(scene, ground, located[j].Index, fit, s.T, camera);
            deviations.Add(new ArcDeviation(
                located[j].Index, located[j].Id, located[j].X, located[j].Y,
                fx, fy, d, !depth && d > tolerance));
        }

        // The curve maps back to world through the ground frame — exact, the
        // axes are orthonormal. Through the camera there is nothing to map a
        // between-frames sample with, so the verdict travels without a curve.
        var curve = new List<ArcPoint>();
        if (!camera)
        {
            var span = located[^1].Index - t0;
            for (var k = 0; k <= span * SamplesPerFrame; k++)
            {
                var t = (double)k / SamplesPerFrame;
                var (x, y) = FromGround(ground, fit.U(t), fit.V(t));
                curve.Add(new ArcPoint(x, y));
            }
        }

        // Up-positive axes both ways, so gravity is a negative quadratic term.
        return new JumpArcFit(
            curve, deviations, Ballistic: fit.A < 0, tolerance,
            DepthMotion: depth, ThroughCamera: camera);
    }

    private static (double X, double Y) FromGround(in GroundFrame g, double u, double v) =>
        (g.OriginX + u * g.AlongX + v * g.UpX, g.OriginY + u * g.AlongY + v * g.UpY);

    private static (double X, double Y) FitPointInWorld(
        Scene scene, in GroundFrame ground, int frame, in Coefficients fit, double t, bool camera)
    {
        if (!camera) return FromGround(ground, fit.U(t), fit.V(t));
        // A drawing's own frame has a framing to map back through.
        var f = CameraView.FramingAt(scene, frame)!.Value;
        return CameraView.Unproject(f, fit.U(t), -fit.V(t));
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
    private static List<(int Index, string Id, double X, double Y, double Size)> Airborne(
        Scene scene, List<(int Index, string Id, double X, double Y, double Size)> located, int index)
    {
        if (located.Count == 0) return located;
        var marked = ContactFrames.MarkedIn(scene, located[0].Index, located[^1].Index);
        if (marked.Count == 0) return located;

        var contacts = marked.ToHashSet();
        var segments = new List<List<(int Index, string Id, double X, double Y, double Size)>>();
        var current = new List<(int Index, string Id, double X, double Y, double Size)>();
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
    /// Ordinary least squares for u = p + q·t and v = a·t² + b·t + c, with one
    /// drawing optionally sat out. Null when the times are too degenerate to
    /// invert — which a run of distinct cel indices never is.
    /// </summary>
    private static Coefficients? Fit(
        List<(double T, double U, double V, double Size)> samples, int skip)
    {
        var n = 0;
        double s1 = 0, s2 = 0, s3 = 0, s4 = 0, su = 0, stu = 0, sv = 0, stv = 0, st2v = 0;
        for (var j = 0; j < samples.Count; j++)
        {
            if (j == skip) continue;
            var t = samples[j].T;
            var tt = t * t;
            n++;
            s1 += t; s2 += tt; s3 += tt * t; s4 += tt * tt;
            su += samples[j].U; stu += t * samples[j].U;
            sv += samples[j].V; stv += t * samples[j].V; st2v += tt * samples[j].V;
        }

        var uDet = n * s2 - s1 * s1;
        if (Math.Abs(uDet) < 1e-9) return null;
        var q = (n * stu - s1 * su) / uDet;
        var p = (su - q * s1) / n;

        var det = Det3(s4, s3, s2, s3, s2, s1, s2, s1, n);
        if (Math.Abs(det) < 1e-9) return null;
        var a = Det3(st2v, s3, s2, stv, s2, s1, sv, s1, n) / det;
        var b = Det3(s4, st2v, s2, s3, stv, s1, s2, sv, n) / det;
        var c = Det3(s4, s3, st2v, s3, s2, stv, s2, s1, sv) / det;
        return new Coefficients(p, q, a, b, c);
    }

    private static double Det3(
        double a1, double a2, double a3,
        double b1, double b2, double b3,
        double c1, double c2, double c3) =>
        a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1) + a3 * (b1 * c2 - b2 * c1);
}
