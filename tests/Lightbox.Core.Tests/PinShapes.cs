using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests;

/// <summary>
/// The drawings the puppet-pin tests share: outlines drawn as line art, which
/// is the hard case — nothing here is a filled shape, so a mesh that covers
/// the inside has had to work out where the inside is.
/// </summary>
internal static class PinShapes
{
    /// <summary>A closed polygon drawn as one stroke, sampled every few units like a pen would.</summary>
    public static Stroke Outline(params (double X, double Y)[] corners) => Outline(4, corners.Length, corners);

    /// <summary>The same polygon left open: the last <paramref name="sides"/> … corners are joined, the rest is a gap.</summary>
    public static Stroke Outline(double spacing, int sides, (double X, double Y)[] corners)
    {
        var points = new List<StrokePoint>();
        for (var i = 0; i < sides; i++)
        {
            var (ax, ay) = corners[i];
            var (bx, by) = corners[(i + 1) % corners.Length];
            var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var steps = Math.Max(1, (int)Math.Ceiling(length / spacing));
            for (var s = 0; s < steps; s++)
            {
                var t = (double)s / steps;
                points.Add(new StrokePoint(ax + (bx - ax) * t, ay + (by - ay) * t, 1));
            }
        }
        var (ex, ey) = corners[sides % corners.Length];
        points.Add(new StrokePoint(ex, ey, 1));
        return new Stroke { Points = points };
    }

    /// <summary>A rectangle outline.</summary>
    public static Stroke Box(double left, double top, double right, double bottom) =>
        Outline((left, top), (right, top), (right, bottom), (left, bottom));

    /// <summary>A limb: 400 long, 60 thick, lying along x from the origin.</summary>
    public static Stroke Bar() => Box(0, 0, 400, 60);

    /// <summary>
    /// Two prongs joined by a base — an arm beside a leg. The prong tips are
    /// 180 apart in a straight line and 580 apart through the drawing, which
    /// is the difference a silhouette mesh exists to know.
    /// </summary>
    public static Stroke Fork() => Outline(
        (0, 0), (60, 0), (60, 200), (240, 200), (240, 0), (300, 0), (300, 260), (0, 260));

    /// <summary>Area of the whole mesh at the given vertex positions.</summary>
    public static double Area(PinMesh mesh, IReadOnlyList<(double X, double Y)> at)
    {
        double sum = 0;
        for (var t = 0; t < mesh.Triangles.Count; t += 3) sum += Math.Abs(SignedArea(mesh, at, t));
        return sum;
    }

    /// <summary>Twice-signed-area halved for the triangle starting at index <paramref name="t"/>.</summary>
    public static double SignedArea(PinMesh mesh, IReadOnlyList<(double X, double Y)> at, int t)
    {
        var a = at[mesh.Triangles[t]];
        var b = at[mesh.Triangles[t + 1]];
        var c = at[mesh.Triangles[t + 2]];
        return ((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
    }

    /// <summary>
    /// How far a set of vertices is from having moved rigidly: the RMS
    /// distance left over after the best rotation and translation from their
    /// rest positions is taken out. Zero for a part that only turned.
    /// </summary>
    public static double RigidResidual(
        IReadOnlyList<(double X, double Y)> rest, IReadOnlyList<(double X, double Y)> moved, IReadOnlyList<int> which)
    {
        double rx = 0, ry = 0, mx = 0, my = 0;
        foreach (var i in which)
        {
            rx += rest[i].X; ry += rest[i].Y;
            mx += moved[i].X; my += moved[i].Y;
        }
        rx /= which.Count; ry /= which.Count; mx /= which.Count; my /= which.Count;
        double dot = 0, cross = 0;
        foreach (var i in which)
        {
            var (ax, ay) = (rest[i].X - rx, rest[i].Y - ry);
            var (bx, by) = (moved[i].X - mx, moved[i].Y - my);
            dot += ax * bx + ay * by;
            cross += ax * by - ay * bx;
        }
        var angle = Math.Atan2(cross, dot);
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        double sum = 0;
        foreach (var i in which)
        {
            var (ax, ay) = (rest[i].X - rx, rest[i].Y - ry);
            var ex = cos * ax - sin * ay + mx - moved[i].X;
            var ey = sin * ax + cos * ay + my - moved[i].Y;
            sum += ex * ex + ey * ey;
        }
        return Math.Sqrt(sum / which.Count);
    }
}
