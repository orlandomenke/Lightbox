using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

/// <summary>
/// One line between two keys, swung on an arc (Q272): it turns about its joint
/// — the end that moves least — while that end travels, and whatever else
/// changes between the keys is blended on top.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Moving every point in a straight line from key A to key B puts a
/// swinging limb on the chord of its arc, and the chord is shorter: a thigh
/// swung through a right angle came out 141px long halfway, against 200 in
/// both keys, and the motion read as sliding rather than swinging.
/// </para>
/// <para>
/// <b>How.</b> The two lines, already resampled to corresponding points, are
/// laid over each other at the joint and turned to fit (least squares, no
/// scale). Point <c>i</c> at <c>t</c> is the joint's straight-line position, plus
/// key A's point turned by <c>t</c> of the angle, plus <c>t</c> of what the turn
/// does not explain. At <c>t = 0</c> that is key A exactly, at <c>t = 1</c> key B
/// exactly, and a line that only moves, without turning, moves as it always
/// did. A closed outline has no end to hang from and turns about its middle.
/// </para>
/// <para>
/// Deterministic, no randomness (invariant 2): the joint and the angle are
/// pure functions of the two keys.
/// </para>
/// </remarks>
public static class Arc
{
    /// <summary>
    /// The points between <paramref name="a"/> and <paramref name="b"/> at
    /// <paramref name="t"/>, swung about their joint. The two lists must
    /// correspond point for point and be the same length.
    /// </summary>
    public static List<StrokePoint> Lerp(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b, double t)
    {
        var n = Math.Min(a.Count, b.Count);
        var result = new List<StrokePoint>(n);
        if (n < 2)
        {
            for (var i = 0; i < n; i++) result.Add(GeometryOps.LerpPoint(a[i], b[i], t));
            return result;
        }

        var (pa, pb) = Joint(a, b, n);
        var angle = Turn(a, b, n, pa, pb);
        var (cos1, sin1) = (Math.Cos(angle), Math.Sin(angle));
        var (cosT, sinT) = (Math.Cos(angle * t), Math.Sin(angle * t));
        var joint = GeometryOps.LerpPoint(pa, pb, t);
        for (var i = 0; i < n; i++)
        {
            var (dx, dy) = (a[i].X - pa.X, a[i].Y - pa.Y);
            // What turning key A's point all the way does not explain of key B's.
            var rx = b[i].X - (pb.X + cos1 * dx - sin1 * dy);
            var ry = b[i].Y - (pb.Y + sin1 * dx + cos1 * dy);
            result.Add(GeometryOps.LerpPoint(a[i], b[i], t) with
            {
                X = joint.X + cosT * dx - sinT * dy + t * rx,
                Y = joint.Y + sinT * dx + cosT * dy + t * ry,
            });
        }
        return result;
    }

    /// <summary>
    /// Where the line hangs from, in each key: a blend of its two ends and its
    /// middle, each weighted by how little it moves between the keys — a still
    /// end wins (a thigh from its hip), a still middle wins (a propeller, a
    /// spoke), and where they move alike the joint is between them (a club
    /// thrown across turns about its middle). A closed outline hangs from its
    /// middle.
    /// </summary>
    /// <remarks>
    /// A blend, not a choice: picking the end that moves least was a hard
    /// switch, so two takes of a key with one end moving a hair more pivoted at
    /// opposite ends (90px apart halfway), and a line turning about its middle
    /// hung from an end and its still middle drifted 20px (the art-director
    /// and the ai-engineer, 2026-10-14).
    /// </remarks>
    private static (StrokePoint A, StrokePoint B) Joint(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b, int n)
    {
        var (ca, cb) = (GeometryOps.Centroid(a), GeometryOps.Centroid(b));
        var length = Math.Max(GeometryOps.PathLength(a), GeometryOps.PathLength(b));
        var closedA = GeometryOps.Dist(a[0], a[n - 1]) < GeometryOps.PathLength(a) * 0.05;
        var closedB = GeometryOps.Dist(b[0], b[n - 1]) < GeometryOps.PathLength(b) * 0.05;
        if (closedA && closedB) return (ca, cb);

        // Inverse-square in how far each moves, softened by a sliver of the
        // line's length so that "still" is not a division by zero.
        var soft = Math.Max(1, length * 0.02);
        (StrokePoint A, StrokePoint B)[] candidates = [(a[0], b[0]), (a[n - 1], b[n - 1]), (ca, cb)];
        double sx = 0, sy = 0, tx = 0, ty = 0, sum = 0;
        foreach (var (pa, pb) in candidates)
        {
            var d = GeometryOps.Dist(pa, pb) + soft;
            var w = 1 / (d * d);
            sx += w * pa.X; sy += w * pa.Y;
            tx += w * pb.X; ty += w * pb.Y;
            sum += w;
        }
        return (new StrokePoint(sx / sum, sy / sum, 0), new StrokePoint(tx / sum, ty / sum, 0));
    }

    /// <summary>
    /// Lines of one drawing that meet at both keys — a thigh's knee end on a
    /// shin's knee end — kept meeting between them. Each line turns about its
    /// own joint, so the two ends of a knee travel on two different arcs and
    /// part halfway (10px on a planted-foot leg, the art-director, 2026-10-14).
    /// Each shared end is moved to where the two would meet, half way between
    /// them, and the move fades along the line to nothing at its other end, so
    /// a hip or a planted ankle stays where its arc put it.
    /// </summary>
    /// <param name="lines">Each drawn line, with its ends in key A and in key B, in the line's own point order.</param>
    /// <param name="reach">How close two ends must be in both keys to count as meeting.</param>
    public static void Weld(IReadOnlyList<(Stroke Line, StrokePoint A0, StrokePoint AN, StrokePoint B0, StrokePoint BN)> lines, double reach)
    {
        var shifts = new (double X, double Y)[lines.Count, 2];
        var any = false;
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                for (var ei = 0; ei < 2; ei++)
                {
                    for (var ej = 0; ej < 2; ej++)
                    {
                        var (ai, bi) = ei == 0 ? (lines[i].A0, lines[i].B0) : (lines[i].AN, lines[i].BN);
                        var (aj, bj) = ej == 0 ? (lines[j].A0, lines[j].B0) : (lines[j].AN, lines[j].BN);
                        if (GeometryOps.Dist(ai, aj) > reach || GeometryOps.Dist(bi, bj) > reach) continue;
                        var pi = End(lines[i].Line, ei);
                        var pj = End(lines[j].Line, ej);
                        if (pi is null || pj is null) continue;
                        var (mx, my) = ((pi.Value.X + pj.Value.X) / 2, (pi.Value.Y + pj.Value.Y) / 2);
                        shifts[i, ei] = (mx - pi.Value.X, my - pi.Value.Y);
                        shifts[j, ej] = (mx - pj.Value.X, my - pj.Value.Y);
                        any = true;
                    }
                }
            }
        }
        if (!any) return;
        for (var i = 0; i < lines.Count; i++)
        {
            var points = lines[i].Line.Points;
            var count = points.Count;
            if (count == 0 || (shifts[i, 0] == default && shifts[i, 1] == default)) continue;
            var moved = new List<StrokePoint>(count);
            for (var k = 0; k < count; k++)
            {
                var f = count == 1 ? 0 : (double)k / (count - 1);
                moved.Add(points[k] with
                {
                    X = points[k].X + shifts[i, 0].X * (1 - f) + shifts[i, 1].X * f,
                    Y = points[k].Y + shifts[i, 0].Y * (1 - f) + shifts[i, 1].Y * f,
                });
            }
            lines[i].Line.Points = moved;
        }

        static StrokePoint? End(Stroke s, int which) =>
            s.Points.Count == 0 ? null : which == 0 ? s.Points[0] : s.Points[^1];
    }

    /// <summary>The turn, about the joint, that best lays key A's line on key B's: least squares, in (-π, π].</summary>
    private static double Turn(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b, int n, StrokePoint pa, StrokePoint pb)
    {
        double dot = 0, cross = 0;
        for (var i = 0; i < n; i++)
        {
            var (ax, ay, bx, by) = (a[i].X - pa.X, a[i].Y - pa.Y, b[i].X - pb.X, b[i].Y - pb.Y);
            dot += ax * bx + ay * by;
            cross += ax * by - ay * bx;
        }
        return dot == 0 && cross == 0 ? 0 : Math.Atan2(cross, dot);
    }
}
