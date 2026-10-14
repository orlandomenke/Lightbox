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
    /// Where the line hangs from, in each key: the end that moves least
    /// between them, or for a closed outline its middle.
    /// </summary>
    private static (StrokePoint A, StrokePoint B) Joint(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b, int n)
    {
        var closedA = GeometryOps.Dist(a[0], a[n - 1]) < GeometryOps.PathLength(a) * 0.05;
        var closedB = GeometryOps.Dist(b[0], b[n - 1]) < GeometryOps.PathLength(b) * 0.05;
        if (closedA && closedB) return (GeometryOps.Centroid(a), GeometryOps.Centroid(b));
        return GeometryOps.Dist(a[0], b[0]) <= GeometryOps.Dist(a[n - 1], b[n - 1])
            ? (a[0], b[0])
            : (a[n - 1], b[n - 1]);
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
