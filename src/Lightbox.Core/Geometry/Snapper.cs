using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// What the guides do to a stroke as it is drawn.
/// </summary>
/// <remarks>
/// <para>
/// Two different things, and telling them apart is the whole design:
/// </para>
/// <list type="bullet">
/// <item><b>Snapping a point</b> — a grid pulls each point to the nearest
/// intersection. Independent per point, and what you want when you are
/// placing things.</item>
/// <item><b>Constraining a stroke</b> — a ruler or a vanishing point takes the
/// first bit of the drag as a statement of intent, locks to the guide that
/// best matches it, and holds every later point on that line. What you want
/// when you are drawing <i>along</i> something.</item>
/// </list>
/// <para>
/// The second is what makes a perspective ruler usable rather than a fight.
/// Re-choosing the guide on every event means a slightly wobbly hand flicks
/// between two vanishing points mid-stroke and the line kinks; choosing once,
/// from a direction the artist has actually committed to, does not.
/// </para>
/// <para>
/// Pure, and deterministic — no clock, no RNG, no state. The result is written
/// into the stroke record and never recomputed, so invariant 1 holds: moving a
/// guide afterwards cannot move a line that was already drawn.
/// </para>
/// </remarks>
public static class Snapper
{
    /// <summary>
    /// How far off a stroke's direction may be, in degrees, and still count as
    /// meant for a guide.
    /// </summary>
    /// <remarks>
    /// Generous, because the alternative is worse in both directions: too
    /// tight and the ruler ignores you, too loose and it grabs strokes you
    /// meant to draw freehand. Fifteen degrees is about the width of a
    /// deliberate gesture and well inside the gap between two axes of an
    /// isometric grid.
    /// </remarks>
    public const double LockDegrees = 15;

    /// <summary>
    /// How far a stroke must travel before its direction is believed.
    /// </summary>
    /// <remarks>
    /// The first two or three samples of any stroke point wherever the pen
    /// happened to land, so locking on them locks on noise. Document pixels
    /// rather than screen, so a zoomed-in artist does not get a different
    /// ruler from a zoomed-out one.
    /// </remarks>
    public const double LockDistance = 6;

    // ---- snapping one point ------------------------------------------------------

    /// <summary>
    /// Pull a point onto the nearest guide, if one is within
    /// <paramref name="tolerance"/> document pixels. Otherwise leave it alone.
    /// </summary>
    public static (double X, double Y) Point(
        IReadOnlyList<Guide> guides, double x, double y, double tolerance)
    {
        var best = (X: x, Y: y);
        var bestDistance = tolerance;
        foreach (var guide in guides)
        {
            if (!guide.Snaps) continue;
            var candidate = Nearest(guide, x, y);
            if (candidate is not { } point) continue;
            var distance = Math.Sqrt(Sq(point.X - x) + Sq(point.Y - y));
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = point;
        }
        return best;
    }

    /// <summary>The closest point on one guide, or null when it does not pull points.</summary>
    private static (double X, double Y)? Nearest(Guide guide, double x, double y) => guide.Kind switch
    {
        // A grid pulls to its intersections rather than its lines: an
        // intersection is a place, a line is a direction, and "snap to grid"
        // has always meant the first.
        GuideKind.Grid => GridPoint(guide, x, y),
        GuideKind.Line => OntoLine(guide.X, guide.Y, guide.Angle, x, y),
        // A vanishing point pulls to itself, which is what lets you start a
        // line exactly on the horizon.
        GuideKind.VanishingPoint => (guide.X, guide.Y),
        // A height scale pulls to its division lines — "put the chin on the
        // fifth head" — anywhere across the canvas, the way a line does.
        GuideKind.HeightScale => HeightScalePoint(guide, x, y),
        // Isometric axes are directions, not places. Constraining is their job.
        _ => null,
    };

    private static (double X, double Y) HeightScalePoint(Guide guide, double x, double y)
    {
        var unit = Math.Max(1e-6, guide.Spacing);
        // Clamped to the scale's own range: above the top head or below the
        // ground there is no division to mean, so the nearest end pulls.
        var i = Math.Clamp(Math.Round((guide.Y - y) / unit), 0, Math.Max(1, guide.Divisions ?? 1));
        return (x, guide.Y - i * unit);
    }

    private static (double X, double Y) GridPoint(Guide guide, double x, double y)
    {
        var pitch = Math.Max(1e-6, guide.Spacing);
        var (cos, sin) = Direction(guide.Angle);
        // Into the grid's own frame, round, and back out. Rotating the point
        // rather than the lattice is what lets a tilted grid work at all.
        var dx = x - guide.X;
        var dy = y - guide.Y;
        var u = Math.Round((dx * cos + dy * sin) / pitch) * pitch;
        var v = Math.Round((-dx * sin + dy * cos) / pitch) * pitch;
        return (guide.X + u * cos - v * sin, guide.Y + u * sin + v * cos);
    }

    // ---- constraining a stroke -----------------------------------------------------

    /// <summary>
    /// Which guide a stroke that started at the anchor and has reached
    /// (x, y) is being drawn along, or null for none.
    /// </summary>
    /// <remarks>
    /// Returns null until the stroke has travelled <see cref="LockDistance"/>,
    /// so the decision is made on a direction the artist has committed to
    /// rather than on the jitter of the first few samples.
    /// </remarks>
    public static Guide? Lock(
        IReadOnlyList<Guide> guides, double anchorX, double anchorY, double x, double y)
    {
        var dx = x - anchorX;
        var dy = y - anchorY;
        if (Math.Sqrt(Sq(dx) + Sq(dy)) < LockDistance) return null;

        var heading = Degrees(Math.Atan2(dy, dx));
        Guide? best = null;
        var bestOff = LockDegrees;
        foreach (var guide in guides)
        {
            if (!guide.Snaps) continue;
            foreach (var angle in DirectionsAt(guide, anchorX, anchorY))
            {
                var off = Off(heading, angle);
                if (off > bestOff) continue;
                bestOff = off;
                best = guide;
            }
        }
        return best;
    }

    /// <summary>
    /// Hold a point on the guide's line through the anchor.
    /// </summary>
    /// <remarks>
    /// Projected onto the line rather than clamped to a length, so the stroke
    /// still goes as far as the hand went — the ruler decides the direction,
    /// not the extent.
    /// </remarks>
    public static (double X, double Y) Along(
        Guide guide, double anchorX, double anchorY, double x, double y)
    {
        var angle = ClosestDirection(guide, anchorX, anchorY, x, y);
        var (cos, sin) = Direction(angle);
        var t = (x - anchorX) * cos + (y - anchorY) * sin;
        return (anchorX + t * cos, anchorY + t * sin);
    }

    /// <summary>
    /// The direction a guide constrains along at a given place.
    /// </summary>
    /// <remarks>
    /// A vanishing point is the interesting case: its direction is not a
    /// property of the guide but of where you are standing relative to it,
    /// which is exactly what makes perspective work.
    /// </remarks>
    public static IReadOnlyList<double> DirectionsAt(Guide guide, double x, double y) =>
        guide.Kind == GuideKind.VanishingPoint
            ? [DirectionAt(guide, x, y)]
            : guide.Angles;

    /// <summary>The angle from a vanishing point to a place, in degrees.</summary>
    public static double DirectionAt(Guide guide, double x, double y) =>
        Degrees(Math.Atan2(y - guide.Y, x - guide.X));

    private static double ClosestDirection(Guide guide, double ax, double ay, double x, double y)
    {
        var directions = DirectionsAt(guide, ax, ay);
        if (directions.Count == 1) return directions[0];
        var heading = Degrees(Math.Atan2(y - ay, x - ax));
        var best = directions[0];
        var bestOff = double.MaxValue;
        foreach (var angle in directions)
        {
            var off = Off(heading, angle);
            if (off >= bestOff) continue;
            bestOff = off;
            best = angle;
        }
        return best;
    }

    // ---- geometry ------------------------------------------------------------------

    /// <summary>The nearest point on an infinite line, or null if there is none.</summary>
    private static (double X, double Y)? OntoLine(double px, double py, double angle, double x, double y)
    {
        var (cos, sin) = Direction(angle);
        var t = (x - px) * cos + (y - py) * sin;
        return (px + t * cos, py + t * sin);
    }

    private static (double Cos, double Sin) Direction(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return (Math.Cos(radians), Math.Sin(radians));
    }

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    /// <summary>
    /// How far apart two headings are, treating a line as undirected.
    /// </summary>
    /// <remarks>
    /// Drawing right-to-left along a ruler is drawing along it. Measuring the
    /// full 0–360 would make half of every stroke miss its own guide.
    /// </remarks>
    private static double Off(double a, double b)
    {
        var diff = Math.Abs(a - b) % 180;
        return Math.Min(diff, 180 - diff);
    }

    private static double Sq(double v) => v * v;
}
