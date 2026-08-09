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
}

/// <summary>What is under the pointer, in path terms.</summary>
public readonly record struct PathHit(int Node, PathPart Part)
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

    private readonly HashSet<int> _selected = [];

    private PathEditSession(string strokeId, StrokePath working, StrokePath original, PressureProfile? weight)
    {
        StrokeId = strokeId;
        Path = working;
        Original = original;
        Weight = weight;
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
    public PressureProfile? Weight { get; }

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
            PressureProfile.Of(stroke.Points));
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
        return best;

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
        Dirty = false;
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
