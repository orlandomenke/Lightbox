using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// One line that nearly-but-not-quite converges: which stroke, where it is,
/// which vanishing point it almost aims at, and by how many degrees it misses.
/// </summary>
public readonly record struct PerspectiveFinding(
    string StrokeId,
    double MidX, double MidY,
    double VpX, double VpY,
    string VpName,
    bool Inferred,
    double OffByDeg);

/// <summary>
/// What one check read: the lines that miss (worst first), how many straight
/// lines were judged, how many vanishing points they were judged against, and
/// whether those points were the artist's own or read off the ink.
/// </summary>
public sealed record PerspectiveReport(
    IReadOnlyList<PerspectiveFinding> Findings,
    int StraightLines,
    int VanishingPoints,
    bool Inferred);

/// <summary>
/// Measures a drawing's near-straight lines against its vanishing points
/// (Q135): a line that ALMOST aims at one — inside the alignment band but
/// outside the convergence band — is the line that reads as a mistake, where
/// a line well clear of every point is simply about something else and is
/// left alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>The artist's own vanishing points win.</b> Where the document declares
/// <see cref="GuideKind.VanishingPoint"/> guides, the ink is judged against
/// those and nothing else. Only where none are declared does the checker
/// infer candidates by clustering the lines' own pairwise intersections —
/// and the report says so (<see cref="PerspectiveReport.Inferred"/>), because
/// an inferred horizon is a guess judging the artist: its findings must read
/// as suggestions, never as errors (Q135's accepted cost).
/// </para>
/// <para>
/// On demand and read-only: nothing here mutates the record, and nothing runs
/// per stroke — the ruler's snapping already covers the live case.
/// </para>
/// </remarks>
public static class PerspectiveChecker
{
    /// <summary>A stroke is a "line" when its bow stays under this share of its length…</summary>
    public const double StraightnessShare = 0.02;

    /// <summary>…and it is at least this long — a tick mark aims at nothing.</summary>
    public const double MinLengthPx = 24;

    /// <summary>Within this of aiming at a vanishing point, the line MEANT to converge…</summary>
    public const double AlignBandDeg = 8;

    /// <summary>…and within this, it does. Between the two bands is the miss.</summary>
    public const double ConvergeBandDeg = 1.5;

    /// <summary>An inferred vanishing point needs this many lines agreeing on it.</summary>
    public const int MinClusterLines = 3;

    /// <summary>
    /// Two lines whose directions differ by less than this contribute no
    /// intersection to inference — near-parallel pairs cross at a point so
    /// far away that noise decides where.
    /// </summary>
    public const double ParallelBandDeg = 2;

    /// <summary>Check one drawing against the scene's vanishing points, or inferred ones.</summary>
    public static PerspectiveReport Check(Scene scene, Frame frame)
    {
        var lines = StraightLines(frame);

        var declared = (scene.Guides ?? [])
            .Where(g => g.Kind == GuideKind.VanishingPoint)
            .Select((g, i) => (g.X, g.Y, Name: g.Name ?? $"VP {i + 1}"))
            .ToList();
        var inferred = declared.Count == 0;
        var points = inferred ? Infer(lines) : declared;

        var findings = new List<PerspectiveFinding>();
        foreach (var line in lines)
        {
            // The line is judged against the point it best aims at: blame
            // spread over every point would flag a one-point drawing's
            // verticals against a horizon they never meant to meet.
            var best = double.MaxValue;
            var bestAt = -1;
            for (var v = 0; v < points.Count; v++)
            {
                var off = OffBy(line, points[v].X, points[v].Y);
                if (off < best) { best = off; bestAt = v; }
            }
            if (bestAt < 0 || best <= ConvergeBandDeg || best > AlignBandDeg) continue;
            findings.Add(new PerspectiveFinding(
                line.Id, line.MidX, line.MidY,
                points[bestAt].X, points[bestAt].Y, points[bestAt].Name,
                inferred, best));
        }

        findings.Sort(static (a, b) => b.OffByDeg.CompareTo(a.OffByDeg));
        return new PerspectiveReport(findings, lines.Count, points.Count, inferred && points.Count > 0);
    }

    private readonly record struct Line(
        string Id, double MidX, double MidY, double DirX, double DirY);

    /// <summary>Degrees between the line and the ray from its midpoint to the point, direction ignored.</summary>
    private static double OffBy(in Line line, double px, double py)
    {
        var toX = px - line.MidX;
        var toY = py - line.MidY;
        var len = Math.Sqrt(toX * toX + toY * toY);
        if (len < 1e-9) return 0;   // a line through the point converges by definition
        var dot = Math.Abs(line.DirX * toX + line.DirY * toY) / len;
        return Math.Acos(Math.Clamp(dot, 0, 1)) * 180 / Math.PI;
    }

    /// <summary>The drawing's near-straight, long-enough strokes, as lines.</summary>
    private static List<Line> StraightLines(Frame frame)
    {
        var lines = new List<Line>();
        foreach (var stroke in frame.Strokes)
        {
            if (stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion or ToolKind.Fill) continue;
            var pts = stroke.Points;
            if (pts.Count < 2) continue;
            var ax = pts[0].X;
            var ay = pts[0].Y;
            var bx = pts[^1].X;
            var by = pts[^1].Y;
            var dx = bx - ax;
            var dy = by - ay;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < MinLengthPx) continue;

            // Bow: the farthest any point sits from the chord.
            var worst = 0.0;
            foreach (var p in pts)
            {
                var d = Math.Abs((p.X - ax) * dy - (p.Y - ay) * dx) / length;
                if (d > worst) worst = d;
            }
            if (worst > StraightnessShare * length) continue;

            lines.Add(new Line(
                stroke.Id, (ax + bx) / 2, (ay + by) / 2, dx / length, dy / length));
        }
        return lines;
    }

    /// <summary>
    /// Candidate vanishing points read off the ink: pairwise intersections of
    /// the lines, greedily clustered in list order (deterministic — the list
    /// order is the stroke order, which the record fixes), keeping clusters
    /// that at least <see cref="MinClusterLines"/> distinct lines pass through.
    /// </summary>
    private static List<(double X, double Y, string Name)> Infer(List<Line> lines)
    {
        var minCross = Math.Sin(ParallelBandDeg * Math.PI / 180);
        var hits = new List<(double X, double Y, int A, int B)>();
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                var a = lines[i];
                var b = lines[j];
                var cross = a.DirX * b.DirY - a.DirY * b.DirX;
                if (Math.Abs(cross) < minCross) continue;
                var t = ((b.MidX - a.MidX) * b.DirY - (b.MidY - a.MidY) * b.DirX) / cross;
                hits.Add((a.MidX + t * a.DirX, a.MidY + t * a.DirY, i, j));
            }
        }
        if (hits.Count == 0) return [];

        // Cluster radius from the spread of the drawing itself, so the same
        // sketch reads the same at any canvas size.
        var spread = 0.0;
        foreach (var line in lines)
        {
            foreach (var other in lines)
            {
                var dx = line.MidX - other.MidX;
                var dy = line.MidY - other.MidY;
                spread = Math.Max(spread, Math.Sqrt(dx * dx + dy * dy));
            }
        }
        var radius = Math.Max(spread * 0.15, 8);

        var candidates = new List<(double X, double Y, string Name)>();
        var used = new bool[hits.Count];
        for (var i = 0; i < hits.Count; i++)
        {
            if (used[i]) continue;
            double sx = 0, sy = 0;
            var members = new List<int>();
            var linesIn = new HashSet<int>();
            for (var j = i; j < hits.Count; j++)
            {
                if (used[j]) continue;
                var dx = hits[j].X - hits[i].X;
                var dy = hits[j].Y - hits[i].Y;
                if (Math.Sqrt(dx * dx + dy * dy) > radius) continue;
                used[j] = true;
                members.Add(j);
                linesIn.Add(hits[j].A);
                linesIn.Add(hits[j].B);
                sx += hits[j].X;
                sy += hits[j].Y;
            }
            if (linesIn.Count < MinClusterLines) continue;
            candidates.Add((sx / members.Count, sy / members.Count,
                $"inferred point {candidates.Count + 1}"));
        }
        return candidates;
    }
}
