using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.ViewModels;

/// <summary>Which part of a node a pointer landed on.</summary>
public enum PathPart
{
    None,
    Node,

    /// <summary>The handle shaping the segment arriving at this node.</summary>
    In,

    /// <summary>The handle shaping the segment leaving it.</summary>
    Out,

    /// <summary>
    /// The curve itself, between this node and the next one.
    /// </summary>
    /// <remarks>
    /// Last in the enum and last in the hit test, because everything else sits
    /// <em>on</em> the curve: a node is always within grabbing distance of the
    /// line through it, so a segment that won would make the nodes unclickable.
    /// </remarks>
    Segment,
}

/// <summary>What is under the pointer, in path terms.</summary>
/// <param name="T">
/// How far along a <see cref="PathPart.Segment"/> the pointer landed, 0 at
/// <see cref="Node"/> and 1 at the next one. Meaningless for the other parts,
/// which are places rather than positions along something.
/// </param>
public readonly record struct PathHit(int Node, PathPart Part, double T = 0)
{
    public static readonly PathHit Miss = new(-1, PathPart.None);

    public bool IsHit => Node >= 0 && Part != PathPart.None;
}

/// <summary>
/// One stroke, isolated and being reshaped: the nodes, what is selected, and
/// the edits a pointer can make to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A second instance of the transform session's shape, not a new
/// mechanism.</b> Modal state that is entered deliberately and left
/// deliberately, a zoom-invariant hit test whose tolerance the caller supplies
/// because only the canvas knows the zoom, and every decision reachable without
/// a window. Q53 chose a mode over modifiers for the reason the research is
/// one-sided about: <b>modes are safe by default, modifiers ask you to remember
/// the antidote.</b>
/// </para>
/// <para>
/// <b>The working path is a copy, and the original is kept.</b> Escape has to
/// put the line back exactly — not approximately, because every dab dynamic is
/// seeded from the bits of a coordinate, so a line restored to visibly the right
/// place with recomputed numbers would come back with a different grain.
/// </para>
/// <para>
/// <b>Nothing here touches the document.</b> The session edits its own copy and
/// hands back points; <see cref="MainViewModel"/> decides when that becomes an
/// undo step. That split is what makes a drag one history entry rather than
/// one per pointer move, and it is why all of this is testable with no canvas
/// attached.
/// </para>
/// </remarks>
public sealed class PathEditSession
{
    /// <summary>
    /// How much longer a handle may be than the distance to its own node's
    /// neighbour.
    /// </summary>
    /// <remarks>
    /// Handles are unconstrained in principle and a wild one is a real
    /// possibility with a pointer — but <see cref="PathFlattener"/>'s sample
    /// count grows with the square root of the handle's reach, so an accidental
    /// fling across the canvas turns one segment into its sample ceiling and a
    /// visibly faceted curve. Three times the neighbour distance is far past any
    /// shape anyone draws deliberately and well short of that.
    /// </remarks>
    public const double MaxHandleReach = 3.0;

    /// <summary>
    /// The smallest brush size a width drag is calibrated against.
    /// </summary>
    /// <remarks>
    /// A one-pixel brush would otherwise make the gesture hair-trigger — a
    /// pointer twitch would take the line from nothing to full — and a stroke
    /// with no brush record at all would divide by zero.
    /// </remarks>
    public const double MinWidthScale = 8.0;

    /// <summary>
    /// How far either side of the pointer a width drag reaches, in document
    /// pixels.
    /// </summary>
    /// <remarks>
    /// <b>A document distance rather than a fraction of the line</b>, because
    /// the artist is pointing at a place on a drawing rather than at a percentage
    /// of a stroke. A fraction would make the same drag affect two centimetres of
    /// a short line and half a metre of a long one, which is the width tool
    /// feeling different depending on what it is pointed at.
    /// </remarks>
    public const double WidthReach = 48.0;

    private readonly HashSet<int> _selected = [];

    private PathEditSession(
        string strokeId, StrokePath working, StrokePath original,
        PressureProfile? weight, double widthScale)
    {
        StrokeId = strokeId;
        Path = working;
        Original = original;
        Weight = weight;
        OriginalWeight = weight;
        WidthScale = widthScale;
    }

    /// <summary>The isolated stroke. Nothing else responds while this is set.</summary>
    public string StrokeId { get; }

    /// <summary>The path being edited — a copy, never the document's own.</summary>
    public StrokePath Path { get; }

    /// <summary>What Escape restores.</summary>
    public StrokePath Original { get; }

    /// <summary>
    /// The weight the stroke was drawn with, kept so reshaping does not flatten
    /// it. Null for a path that was authored rather than drawn.
    /// </summary>
    public PressureProfile? Weight { get; private set; }

    /// <summary>What a revert puts the weight back to.</summary>
    /// <remarks>
    /// Kept beside <see cref="Original"/> for the same reason it exists: the
    /// width tool edits the weight and not the nodes, so a revert that only put
    /// the geometry back would leave a line the right shape and the wrong
    /// thickness — and no undo step would name the difference.
    /// </remarks>
    public PressureProfile? OriginalWeight { get; private set; }

    /// <summary>
    /// How far a width drag has to travel to move the weight from nothing to
    /// full, in document pixels.
    /// </summary>
    /// <remarks>
    /// The stroke's own brush size, so the gesture is calibrated to the mark
    /// being edited rather than to a number in the source: dragging half a
    /// brush-width off a fine line and off a fat one should feel the same, and a
    /// fixed pixel figure makes one of the two useless.
    /// </remarks>
    public double WidthScale { get; }

    /// <summary>Whether anything has actually been moved.</summary>
    public bool Dirty { get; private set; }

    public IReadOnlySet<int> SelectedNodes => _selected;

    public int NodeCount => Path.Nodes.Count;

    /// <summary>
    /// Open a session on a stroke, fitting a path first if it has none.
    /// </summary>
    /// <remarks>
    /// <b>Fitting on demand is Q50, and it is what makes every drawing already
    /// in every document editable.</b> A Lightbox stroke is already a centreline
    /// with a width at every point, so there is no conversion — only a
    /// description to add. Nothing about the drawing changes at this moment: the
    /// points are untouched until an edit actually moves something.
    /// </remarks>
    public static PathEditSession? Open(Stroke stroke)
    {
        var path = stroke.Path is { IsUsable: true } existing
            ? existing.Clone()
            : CurveFitter.Fit(stroke.Points, closed: stroke.Tool == ToolKind.Fill);

        if (path is not { IsUsable: true }) return null;

        return new PathEditSession(
            stroke.Id,
            path,
            path.Clone(),
            PressureProfile.Of(stroke.Points),
            Math.Max(MinWidthScale, stroke.Brush?.Size ?? MinWidthScale));
    }

    // ---- what is under the pointer -------------------------------------------

    /// <summary>
    /// The node or handle within <paramref name="tolerance"/> document units of
    /// a point, or a miss.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Handles win over nodes, and only selected nodes show handles.</b> Both
    /// halves are load-bearing. A handle sits on top of whatever is behind it,
    /// so a node-first order would make the handles of a dense path unclickable;
    /// but showing every handle at once turns a fitted line into a thicket, and
    /// a zero-length handle sits exactly on its own node, where grabbing it
    /// instead of the node would feel like the tool ignoring the click.
    /// </para>
    /// <para>
    /// <b>Nearest wins among equals</b>, rather than first-found. On a path
    /// whose nodes are closer together than the tolerance — which zooming out
    /// guarantees — first-found means the click lands on whichever node happens
    /// to be earlier in the list, and the tool feels like it is guessing.
    /// </para>
    /// </remarks>
    public PathHit HitTest(double x, double y, double tolerance)
    {
        var best = PathHit.Miss;
        var bestDistance = tolerance * tolerance;

        foreach (var index in _selected)
        {
            if (index < 0 || index >= Path.Nodes.Count) continue;
            var node = Path.Nodes[index];
            if (node.InX != 0 || node.InY != 0)
            {
                Consider(index, PathPart.In, node.X + node.InX, node.Y + node.InY);
            }
            if (node.OutX != 0 || node.OutY != 0)
            {
                Consider(index, PathPart.Out, node.X + node.OutX, node.Y + node.OutY);
            }
        }
        if (best.IsHit) return best;

        for (var i = 0; i < Path.Nodes.Count; i++)
        {
            Consider(i, PathPart.Node, Path.Nodes[i].X, Path.Nodes[i].Y);
        }
        if (best.IsHit) return best;

        // The curve itself, last. Every node sits on the line through it, so a
        // segment tested any earlier would swallow the clicks meant for nodes and
        // handles — and a tool where you cannot reliably grab a point is not
        // improved by also being able to grab the line.
        var landing = SegmentDrag.Nearest(Path, x, y, tolerance);
        return landing.IsHit
            ? new PathHit(landing.Segment, PathPart.Segment, landing.T)
            : PathHit.Miss;

        void Consider(int index, PathPart part, double px, double py)
        {
            var dx = px - x;
            var dy = py - y;
            var distance = dx * dx + dy * dy;
            if (distance > bestDistance) return;
            bestDistance = distance;
            best = new PathHit(index, part);
        }
    }

    // ---- selection -----------------------------------------------------------

    public void SelectNode(int index, bool additive = false)
    {
        if (index < 0 || index >= Path.Nodes.Count) return;
        if (!additive) _selected.Clear();
        if (!_selected.Add(index) && additive) _selected.Remove(index);
    }

    public void SelectAllNodes()
    {
        _selected.Clear();
        for (var i = 0; i < Path.Nodes.Count; i++) _selected.Add(i);
    }

    public void ClearNodeSelection() => _selected.Clear();

    public bool IsNodeSelected(int index) => _selected.Contains(index);

    // ---- edits ---------------------------------------------------------------

    /// <summary>Move one node, handles and all.</summary>
    /// <remarks>
    /// The handles need no arithmetic because they are stored as offsets from
    /// the node — which is the whole reason they are. Moving a node with
    /// absolute control points means remembering to move three things, and the
    /// one that gets forgotten is the one nobody looks at.
    /// </remarks>
    public void MoveNode(int index, double dx, double dy)
    {
        if (index < 0 || index >= Path.Nodes.Count) return;
        if (dx == 0 && dy == 0) return;
        var node = Path.Nodes[index];
        Path.Nodes[index] = node with { X = node.X + dx, Y = node.Y + dy };
        Dirty = true;
    }

    /// <summary>Move every selected node by the same offset.</summary>
    public void MoveSelectedNodes(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return;
        foreach (var index in _selected) MoveNode(index, dx, dy);
    }

    /// <summary>
    /// Drag a handle to a place. Returns false if there was nothing there.
    /// </summary>
    /// <param name="breakPair">
    /// Alt: let this handle move without its opposite following, which makes the
    /// node a corner. Illustrator's modifier, so the muscle memory transfers.
    /// </param>
    /// <remarks>
    /// <b>A smooth node's other handle keeps its own length.</b> Mirroring the
    /// dragged handle outright is the easier implementation and it is wrong to
    /// use: an artist pulling one side out to open a curve would find the other
    /// side growing to match, so the shape they were adjusting moves too. Only
    /// the direction is shared, which is what "smooth" actually means — the
    /// curve does not kink here.
    /// </remarks>
    public bool MoveHandleTo(int index, PathPart part, double x, double y, bool breakPair = false)
    {
        if (index < 0 || index >= Path.Nodes.Count) return false;
        if (part is not (PathPart.In or PathPart.Out)) return false;

        var node = Path.Nodes[index];
        var (dx, dy) = Clamp(index, x - node.X, y - node.Y);

        node = part == PathPart.In
            ? node with { InX = dx, InY = dy }
            : node with { OutX = dx, OutY = dy };

        if (breakPair)
        {
            node = node with { Corner = true };
        }
        else if (!node.Corner)
        {
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 1e-9)
            {
                var (ux, uy) = (-dx / length, -dy / length);
                node = part == PathPart.In
                    ? node with
                    {
                        OutX = ux * Math.Sqrt(node.OutX * node.OutX + node.OutY * node.OutY),
                        OutY = uy * Math.Sqrt(node.OutX * node.OutX + node.OutY * node.OutY),
                    }
                    : node with
                    {
                        InX = ux * Math.Sqrt(node.InX * node.InX + node.InY * node.InY),
                        InY = uy * Math.Sqrt(node.InX * node.InX + node.InY * node.InY),
                    };
            }
        }

        Path.Nodes[index] = node;
        Dirty = true;
        return true;
    }

    /// <summary>
    /// Pull the curve between two nodes, without selecting either of them.
    /// </summary>
    /// <remarks>
    /// <b>The nodes do not move — only the handles that govern the segment
    /// do.</b> That is what separates this from dragging a node and it is the
    /// whole reason an artist reaches for it: the shape either side of the bit
    /// you are pulling stays exactly where it was put. A smooth endpoint is the
    /// one exception, and it is not really one: its far handle swings to stay in
    /// line, because that is what smooth means, and a node that kinked the moment
    /// you pulled the line next to it would be worse.
    /// </remarks>
    public bool PinchSegment(int segment, double t, double dx, double dy)
    {
        if (segment < 0 || segment >= Path.Nodes.Count) return false;
        if (dx == 0 && dy == 0) return false;

        var nextIndex = (segment + 1) % Path.Nodes.Count;
        if (nextIndex == segment) return false;
        if (!Path.Closed && nextIndex <= segment) return false;

        var (a, b) = SegmentDrag.Pinch(Path.Nodes[segment], Path.Nodes[nextIndex], t, dx, dy);
        Path.Nodes[segment] = a;
        Path.Nodes[nextIndex] = b;
        Dirty = true;
        return true;
    }

    // ---- width along the line --------------------------------------------------

    /// <summary>
    /// Where a point sits along the whole line, 0 at the start and 1 at the end,
    /// with how far off the line it was.
    /// </summary>
    /// <remarks>
    /// Arc length rather than the segment parameter the pinch uses, because
    /// width is a function of distance along the drawing: two segments of very
    /// different length both run <c>t</c> from 0 to 1, so a reach expressed in
    /// segment parameter would spread over inches on one and millimetres on the
    /// next.
    /// </remarks>
    public (double At, double Distance) AlongAt(double x, double y)
    {
        var points = PathFlattener.Flatten(Path);
        if (points.Count < 2) return (0, double.PositiveInfinity);

        var lengths = new double[points.Count];
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += Hypot(points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y);
            lengths[i] = total;
        }
        if (total <= 0) return (0, double.PositiveInfinity);

        var best = double.PositiveInfinity;
        var at = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var d = GeometryOps.DistToSegment(new StrokePoint(x, y, 1), points[i - 1], points[i]);
            if (d >= best) continue;
            best = d;
            // The nearer end of the segment is close enough: the reach of a width
            // edit is tens of pixels and a flattened segment is a fraction of one.
            var toStart = Hypot(x - points[i - 1].X, y - points[i - 1].Y);
            var toEnd = Hypot(x - points[i].X, y - points[i].Y);
            at = (toStart <= toEnd ? lengths[i - 1] : lengths[i]) / total;
        }
        return (at, best);
    }

    /// <summary>The line's length, so a document distance can become a fraction of it.</summary>
    public double TotalLength()
    {
        var points = PathFlattener.Flatten(Path);
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            total += Hypot(points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y);
        }
        return total;
    }

    /// <summary>
    /// Make the line heavier or lighter around one place on it.
    /// </summary>
    /// <param name="at">Where, as a fraction of the line's length.</param>
    /// <param name="pixels">
    /// How far the pointer moved away from the line. Positive fattens.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>This edits the weight, never the points, and that is what makes it
    /// hold.</b> The flatten regenerates the points from the nodes on every
    /// commit and re-applies the weight afterwards, so an edit written into the
    /// points would be thrown away by the next reshape — the same trap
    /// <see cref="PressureProfile"/> exists to close one operation earlier.
    /// </para>
    /// <para>
    /// <b>A line that was never pressed gets a flat profile first.</b> A pen line
    /// has no pressure to read, and starting it at full weight means the first
    /// drag is an edit rather than a jump from nothing.
    /// </para>
    /// </remarks>
    public bool AdjustWidthAt(double at, double pixels)
    {
        if (pixels == 0) return false;

        var length = TotalLength();
        if (length <= 0) return false;

        var weight = Weight ?? PressureProfile.Uniform();

        // Resampled UP and never down. A straight authored segment flattens to
        // two points and a local change has nowhere to land between them; a
        // drawn line already has hundreds, and passing those through a 64-sample
        // resample would quietly coarsen the taper the artist drew — the width
        // tool taking something away as the price of being picked up.
        if (weight.SampleCount < PressureProfile.DefaultSamples)
        {
            weight = weight.Resampled(PressureProfile.DefaultSamples);
        }

        Weight = weight.Adjusted(at, pixels / WidthScale, WidthReach / length);

        Dirty = true;
        return true;
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    /// <summary>
    /// Make a node a corner or make it smooth again.
    /// </summary>
    /// <remarks>
    /// Turning a corner smooth points both handles along the line between the
    /// node's neighbours, which is the direction that produces no kink. Giving
    /// them zero length instead would technically be smooth and would look like
    /// the tool having deleted the curve.
    /// </remarks>
    public void SetCorner(int index, bool corner)
    {
        if (index < 0 || index >= Path.Nodes.Count) return;
        var node = Path.Nodes[index];
        if (node.Corner == corner) return;

        if (corner)
        {
            Path.Nodes[index] = node with { Corner = true };
            Dirty = true;
            return;
        }

        var previous = Neighbour(index, -1);
        var next = Neighbour(index, +1);
        var tx = next.X - previous.X;
        var ty = next.Y - previous.Y;
        var length = Math.Sqrt(tx * tx + ty * ty);
        if (length > 1e-9)
        {
            // A third of the way to each neighbour is the same heuristic the
            // fitter falls back to, so a node smoothed by hand and one produced
            // by a fit have handles of comparable weight.
            var reach = length / 6.0;
            var (ux, uy) = (tx / length, ty / length);
            node = node with
            {
                InX = -ux * reach,
                InY = -uy * reach,
                OutX = ux * reach,
                OutY = uy * reach,
            };
        }
        Path.Nodes[index] = node with { Corner = false };
        Dirty = true;
    }

    /// <summary>Put the path back exactly as it was found.</summary>
    public void Revert()
    {
        Path.Nodes.Clear();
        Path.Nodes.AddRange(Original.Nodes);
        Path.Closed = Original.Closed;
        Weight = OriginalWeight;
        Dirty = false;
    }

    /// <summary>The committed state becomes what a later revert restores.</summary>
    internal void Keep(StrokePath committed)
    {
        Original.Nodes.Clear();
        Original.Nodes.AddRange(committed.Nodes);
        Original.Closed = committed.Closed;
        OriginalWeight = Weight;
    }

    /// <summary>
    /// The points this path now renders as, carrying the weight the stroke was
    /// drawn with.
    /// </summary>
    public List<StrokePoint> Flatten()
    {
        var points = PathFlattener.Flatten(Path);
        return Weight?.ApplyTo(points) ?? points;
    }

    private PathNode Neighbour(int index, int step)
    {
        var count = Path.Nodes.Count;
        if (Path.Closed) return Path.Nodes[((index + step) % count + count) % count];
        return Path.Nodes[Math.Clamp(index + step, 0, count - 1)];
    }

    private (double X, double Y) Clamp(int index, double dx, double dy)
    {
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-9) return (dx, dy);

        var node = Path.Nodes[index];
        var previous = Neighbour(index, -1);
        var next = Neighbour(index, +1);
        var reach = MaxHandleReach * Math.Max(
            Math.Max(Span(node, previous), Span(node, next)),
            1.0);

        if (length <= reach) return (dx, dy);
        return (dx / length * reach, dy / length * reach);

        static double Span(PathNode a, PathNode b) =>
            Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    }
}
