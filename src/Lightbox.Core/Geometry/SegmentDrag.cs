using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// Dragging the curve itself: which segment the pointer is on, and what the
/// handles have to become for the curve to pass through where it was pulled.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 4's first entry, and the design calls it the one artists reach for
/// most.</b> Every other way to change a curve asks you to find the node that
/// governs it and reason about its handles; this asks you to put the line where
/// you want it. Clip Studio's <em>Correct line</em> list opens with it and
/// Illustrator, Inkscape and Figma all have it, which is the usual signal that
/// it is not a nicety.
/// </para>
/// <para>
/// <b>Pure, and in Core rather than beside the session, because it is
/// arithmetic.</b> The interesting part — that there is a whole family of handle
/// adjustments putting the curve through a given point, and which member of it
/// you choose is a design decision — is testable with no document and no window,
/// and a test that had to build either would be testing the wrong thing.
/// </para>
/// </remarks>
public static class SegmentDrag
{
    /// <summary>
    /// How far from an end of a segment the solve is allowed to be applied.
    /// </summary>
    /// <remarks>
    /// <b>Not a nicety: the solve divides by <c>b1² + b2²</c>, and both weights
    /// vanish at the ends.</b> At <c>t = 0</c> the curve is pinned to the node
    /// whatever the handles do, so asking it to move means dividing a finite
    /// displacement by nothing and flinging both control points off the canvas.
    /// Clamping is the right answer rather than refusing, because the correct
    /// gesture that near a node is to drag the node — which the hit test already
    /// gives you, since nodes are tested first.
    /// </remarks>
    public const double EndGuard = 0.15;

    /// <summary>Where a point lands on a path: which segment, how far along, how far away.</summary>
    public readonly record struct Landing(int Segment, double T, double Distance)
    {
        public static readonly Landing Miss = new(-1, 0, double.PositiveInfinity);

        public bool IsHit => Segment >= 0;
    }

    /// <summary>
    /// Samples per segment when looking for the nearest point on the curve.
    /// </summary>
    /// <remarks>
    /// A search rather than a solve. Finding the true nearest point on a cubic
    /// means rooting a quintic, and the answer is then refined by a pointer that
    /// moves again a millisecond later — so the accuracy that matters is "close
    /// enough that the curve does not jump when you grab it", which a coarse
    /// sample plus a local refinement gives for a fraction of the cost. Twenty
    /// four is about a sample every few pixels on an ordinary segment.
    /// </remarks>
    public const int SearchSamples = 24;

    /// <summary>
    /// The closest place on the path to a point, or a miss when nothing is
    /// within <paramref name="tolerance"/>.
    /// </summary>
    public static Landing Nearest(StrokePath path, double x, double y, double tolerance)
    {
        var nodes = path.Nodes;
        if (nodes.Count < 2) return Landing.Miss;

        var segments = path.Closed ? nodes.Count : nodes.Count - 1;
        var best = Landing.Miss;

        for (var i = 0; i < segments; i++)
        {
            var a = nodes[i];
            var b = nodes[(i + 1) % nodes.Count];

            for (var s = 0; s <= SearchSamples; s++)
            {
                var t = (double)s / SearchSamples;
                var (px, py) = PathFlattener.PointOn(a, b, t);
                var d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < best.Distance) best = new Landing(i, t, d);
            }
        }

        if (!best.IsHit || best.Distance > tolerance) return Landing.Miss;

        // One refinement pass over the winning sample's neighbourhood. The coarse
        // search can be half a sample out, which at a shallow angle is several
        // pixels along the curve — enough that the pinch would start by moving
        // the line rather than holding it still under the pointer.
        var (a2, b2) = (nodes[best.Segment], nodes[(best.Segment + 1) % nodes.Count]);
        var span = 1.0 / SearchSamples;
        for (var s = -SearchSamples; s <= SearchSamples; s++)
        {
            var t = best.T + span * s / SearchSamples;
            if (t < 0 || t > 1) continue;
            var (px, py) = PathFlattener.PointOn(a2, b2, t);
            var d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
            if (d < best.Distance) best = new Landing(best.Segment, t, d);
        }

        return best;
    }

    /// <summary>
    /// Move the curve at <paramref name="t"/> by a displacement, and give back
    /// the two nodes with the handles that do it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The curve at <c>t</c> is <c>b0·P0 + b1·P1 + b2·P2 + b3·P3</c>, and only
    /// the middle two may move</b> — the endpoints are nodes and dragging the
    /// line between them must not drag them. So the requirement is
    /// <c>b1·ΔP1 + b2·ΔP2 = d</c>: one equation, two unknowns, an infinite family
    /// of answers.
    /// </para>
    /// <para>
    /// <b>The member chosen is the least-squares one</b> —
    /// <c>ΔP1 = d·b1/(b1²+b2²)</c>, <c>ΔP2 = d·b2/(b1²+b2²)</c> — which moves the
    /// control points as little as possible for the displacement asked, and
    /// splits the work between them in proportion to how much influence each has
    /// where you grabbed. That is what makes the gesture feel local: grab near
    /// one end and mostly that end's handle moves, which is what a hand expects
    /// from pulling on a wire.
    /// </para>
    /// <para>
    /// The obvious alternative — give the whole displacement to whichever control
    /// point is closer — is cheaper and reads as the curve lurching, because the
    /// answer changes discontinuously as you drag past the midpoint.
    /// </para>
    /// </remarks>
    public static (PathNode A, PathNode B) Pinch(PathNode a, PathNode b, double t, double dx, double dy)
    {
        t = Math.Clamp(t, EndGuard, 1 - EndGuard);

        var u = 1 - t;
        var b1 = 3 * u * u * t;
        var b2 = 3 * u * t * t;
        var denominator = b1 * b1 + b2 * b2;
        if (denominator < 1e-12) return (a, b);

        var w1 = b1 / denominator;
        var w2 = b2 / denominator;

        var moved = (
            a with { OutX = a.OutX + dx * w1, OutY = a.OutY + dy * w1 },
            b with { InX = b.InX + dx * w2, InY = b.InY + dy * w2 });

        return (Follow(moved.Item1, outgoing: true), Follow(moved.Item2, outgoing: false));
    }

    /// <summary>
    /// Swing a smooth node's other handle back into line, keeping its own length.
    /// </summary>
    /// <remarks>
    /// <b>Smooth means the curve does not kink here, so the rule has to hold
    /// through a pinch as well as through a handle drag</b> — a node that stayed
    /// smooth only while you dragged its arms would be a lie the first time you
    /// pulled the line beside it. This is <c>PathEditSession.MoveHandleTo</c>'s
    /// rule, deliberately identical including the part that is easy to get wrong:
    /// only the <em>direction</em> is shared. Mirroring the length as well would
    /// grow the neighbouring segment to match the one being pinched, moving a
    /// part of the drawing nobody touched.
    /// </remarks>
    private static PathNode Follow(PathNode node, bool outgoing)
    {
        if (node.Corner) return node;

        var (dx, dy) = outgoing ? (node.OutX, node.OutY) : (node.InX, node.InY);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return node;

        var (ux, uy) = (-dx / length, -dy / length);
        if (outgoing)
        {
            var other = Math.Sqrt(node.InX * node.InX + node.InY * node.InY);
            return node with { InX = ux * other, InY = uy * other };
        }
        else
        {
            var other = Math.Sqrt(node.OutX * node.OutX + node.OutY * node.OutY);
            return node with { OutX = ux * other, OutY = uy * other };
        }
    }
}
