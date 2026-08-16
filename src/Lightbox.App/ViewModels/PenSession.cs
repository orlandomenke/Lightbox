using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.ViewModels;

/// <summary>
/// A path being drawn node by node: the pen tool's document-free half.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 3 of the vector design, and the first tool here that authors a path
/// rather than finding one.</b> Phase 1 gave a stroke an optional
/// <see cref="StrokePath"/>, phase 2 let you reshape one that was fitted from a
/// drawn line, and this one starts from nothing: click for a corner, click and
/// drag for a curve, and what lands in the document is an ordinary stroke whose
/// <c>Points</c> are the flattened path. There is no second renderer and no
/// second record — invariant 1 needs no argument here for the same reason it
/// needed none in phase 1.
/// </para>
/// <para>
/// <b>Nothing here touches the document.</b> The session accumulates nodes and
/// answers "what does this look like so far"; <see cref="MainViewModel"/>
/// decides when that becomes a stroke, and the whole path is one undo step
/// rather than one per click. That split is what makes every decision in the pen
/// — where a node lands, whether Shift moved it, whether this click closed the
/// path — reachable by a test with no window attached.
/// </para>
/// <para>
/// <b>The modifiers are Illustrator's, deliberately.</b> Alt breaks the handle
/// pair, Shift constrains the direction, and clicking the first node closes the
/// path. A pen is the one tool in this application whose muscle memory an artist
/// certainly already has, and inventing a different set would spend that for
/// nothing.
/// </para>
/// </remarks>
public sealed class PenSession
{
    /// <summary>
    /// How far apart the constrained directions are, in degrees.
    /// </summary>
    /// <remarks>
    /// Forty-five, which is what the design says and what every pen does — not
    /// the fifteen the gradient and the guide lock use. A gradient's angle is a
    /// continuous quantity being nudged onto a nice value; a pen's Shift is
    /// asking for a horizontal, a vertical or a diagonal, and offering eight
    /// more directions in between makes it miss.
    /// </remarks>
    public const double ConstrainDegrees = 45;

    /// <summary>The path so far. Never the document's own — it has none yet.</summary>
    public StrokePath Path { get; } = new();

    /// <summary>Whether the last click joined the path back to its first node.</summary>
    public bool Closed => Path.Closed;

    public int NodeCount => Path.Nodes.Count;

    /// <summary>Whether there is enough here to be a line.</summary>
    public bool IsUsable => Path.Nodes.Count >= 2;

    /// <summary>
    /// The node whose handles the pointer is currently pulling out, or -1.
    /// </summary>
    /// <remarks>
    /// A press places a node and <em>may</em> become a drag; there is no way to
    /// know which at the moment of the click. So the node is placed as a corner
    /// and the drag, if it comes, curves it — which is exactly the gesture
    /// Illustrator taught everybody and the reason a pen never needs a separate
    /// "curve" mode.
    /// </remarks>
    public int Shaping { get; private set; } = -1;

    /// <summary>
    /// Where the pointer is, for the segment that has not been placed yet.
    /// </summary>
    /// <remarks>
    /// Null while a handle is being pulled: during that gesture the pointer is
    /// the handle, not a future node, and drawing a rubber band to it as well
    /// would show a segment the next click is not going to make.
    /// </remarks>
    public (double X, double Y)? Cursor { get; private set; }

    // ---- placing ---------------------------------------------------------------

    /// <summary>Put a corner node down and take hold of its handles.</summary>
    public int Place(double x, double y)
    {
        if (Path.Closed) return -1;
        Path.Nodes.Add(PathNode.At(x, y));
        Shaping = Path.Nodes.Count - 1;
        Cursor = null;
        return Shaping;
    }

    /// <summary>
    /// Pull the handles of the node just placed. Returns false when no press is
    /// in flight.
    /// </summary>
    /// <param name="breakPair">
    /// Alt: only the outgoing handle, leaving the node a corner. Illustrator's
    /// modifier, and what you reach for when a curve has to arrive smoothly and
    /// leave at an angle.
    /// </param>
    /// <remarks>
    /// <b>A new node's handles start mirrored, unlike a reshape's.</b>
    /// <see cref="PathEditSession.MoveHandleTo"/> deliberately keeps the far
    /// handle's own length, because an artist adjusting one side of an existing
    /// curve must not have the other side grow. Here there is no other side yet
    /// — both handles are being created by this one gesture — so mirroring is
    /// what "drag out a smooth node" means.
    /// </remarks>
    public bool PullHandle(double x, double y, bool breakPair = false)
    {
        if (Shaping < 0 || Shaping >= Path.Nodes.Count) return false;

        var node = Path.Nodes[Shaping];
        var (dx, dy) = Clamp(x - node.X, y - node.Y);

        Path.Nodes[Shaping] = breakPair
            ? node with { OutX = dx, OutY = dy, Corner = true }
            : node with { InX = -dx, InY = -dy, OutX = dx, OutY = dy, Corner = false };
        return true;
    }

    /// <summary>Let go of the node's handles; the next click places a new node.</summary>
    public void Release() => Shaping = -1;

    /// <summary>
    /// Take the last node back off. Returns false when there was nothing to take.
    /// </summary>
    /// <remarks>
    /// Backspace, which is what every pen binds it to. The alternative — undo —
    /// cannot serve here, because the path is not in the document yet and there
    /// is nothing for history to hold.
    /// </remarks>
    public bool RemoveLast()
    {
        if (Path.Nodes.Count == 0) return false;
        Path.Nodes.RemoveAt(Path.Nodes.Count - 1);
        Path.Closed = false;
        Shaping = -1;
        return true;
    }

    // ---- closing ---------------------------------------------------------------

    /// <summary>
    /// Whether a click here would join the path back to its start.
    /// </summary>
    /// <remarks>
    /// <b>Two nodes can close, but only if one of them curves.</b> Three
    /// straight nodes make a triangle and two make a line drawn there and back
    /// — a closed flag that changes no pixel, recorded in the file for nothing.
    /// Two nodes with handles make a lens, which is a shape somebody means.
    /// </remarks>
    public bool ClosesAt(double x, double y, double tolerance)
    {
        if (Path.Closed || Path.Nodes.Count < 2) return false;
        if (Path.Nodes.Count == 2 && !Path.Nodes.Any(n => n.HasHandles)) return false;

        var first = Path.Nodes[0];
        var dx = first.X - x;
        var dy = first.Y - y;
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    /// <summary>Join the path back to its first node.</summary>
    public void Close()
    {
        if (Path.Nodes.Count < 2) return;
        Path.Closed = true;
        Shaping = -1;
        Cursor = null;
    }

    // ---- the pointer -----------------------------------------------------------

    /// <summary>Where the next node would land, for the rubber band.</summary>
    public void Hover(double x, double y)
    {
        if (Shaping >= 0 || Path.Closed) return;
        Cursor = (x, y);
    }

    /// <summary>The pointer has left; stop drawing a segment to nowhere.</summary>
    public void Leave() => Cursor = null;

    /// <summary>
    /// Shift: the nearest multiple of <see cref="ConstrainDegrees"/> from the
    /// node the gesture is working against.
    /// </summary>
    /// <remarks>
    /// <b>The angle is constrained and the distance is not</b>, the same
    /// division of labour a ruler has: the constraint decides the direction, the
    /// hand decides how far. The anchor is the last node either way — while
    /// placing it is the node you came from, and while pulling a handle it is
    /// the node the handle belongs to, which is the same node.
    /// </remarks>
    public (double X, double Y) Constrain(double x, double y)
    {
        if (Path.Nodes.Count == 0) return (x, y);

        // The arithmetic is Snapper.OnNearestAngle's (B218); the forty-five is
        // this tool's, and the constant above says why it is not the gradient's
        // fifteen.
        var anchor = Path.Nodes[^1];
        return Lightbox.Core.Geometry.Snapper.OnNearestAngle(
            anchor.X, anchor.Y, x, y, ConstrainDegrees);
    }

    // ---- what it looks like ----------------------------------------------------

    /// <summary>
    /// The path so far as a polyline, including the segment to the pointer.
    /// </summary>
    /// <remarks>
    /// <b>The rubber band is the real curve, not a straight line to the
    /// cursor.</b> A pen whose preview is straight and whose result is curved
    /// makes you place a node to find out what you drew, which is the whole
    /// thing a pen is supposed to spare you. Adding the cursor as a temporary
    /// corner node and flattening costs one extra segment of samples.
    /// </remarks>
    public List<StrokePoint> Preview()
    {
        if (Path.Nodes.Count == 0) return [];
        if (Cursor is not { } cursor || Path.Closed || Shaping >= 0)
        {
            return Path.Nodes.Count >= 2 ? PathFlattener.Flatten(Path) : [];
        }

        var reaching = Path.Clone();
        reaching.Nodes.Add(PathNode.At(cursor.X, cursor.Y));
        return PathFlattener.Flatten(reaching);
    }

    /// <summary>The finished path, for the stroke that is about to be recorded.</summary>
    public StrokePath Commit() => Path.Clone();

    /// <summary>
    /// Keep a handle inside what the flattener can sample.
    /// </summary>
    /// <remarks>
    /// <see cref="PathEditSession.MaxHandleReach"/>'s reason, one tool along:
    /// <see cref="PathFlattener"/>'s sample count grows with the square root of
    /// the handle's reach and stops at a ceiling, so a fling across the canvas
    /// turns one segment into a visibly faceted curve. The first node has no
    /// neighbour to measure against and is left alone — there is no segment yet
    /// for a wild handle to spoil, and the next node's own clamp catches it.
    /// </remarks>
    private (double X, double Y) Clamp(double dx, double dy)
    {
        if (Shaping <= 0) return (dx, dy);

        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-9) return (dx, dy);

        var node = Path.Nodes[Shaping];
        var previous = Path.Nodes[Shaping - 1];
        var span = Math.Sqrt(
            (node.X - previous.X) * (node.X - previous.X) +
            (node.Y - previous.Y) * (node.Y - previous.Y));
        var reach = PathEditSession.MaxHandleReach * Math.Max(span, 1.0);

        return length <= reach ? (dx, dy) : (dx / length * reach, dy / length * reach);
    }
}
