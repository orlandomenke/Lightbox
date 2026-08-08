namespace Lightbox.Core.Documents;

/// <summary>
/// One authored node on a <see cref="StrokePath"/>: a position, the pressure
/// there, and the two Bezier handles that shape the segments either side of it.
/// </summary>
/// <param name="InX">
/// The incoming handle, <b>relative to the node</b> — the control point is
/// <c>(X + InX, Y + InY)</c>. Relative rather than absolute so that moving a node
/// moves its handles with it, which is what a hand expects and what stops every
/// caller having to remember to move three things.
/// </param>
/// <param name="Corner">
/// True when the two handles are independent. A smooth node keeps them opposite;
/// a corner node is what <c>Alt</c> on a handle produces, and what a click rather
/// than a click-and-drag places. Stored rather than derived from the geometry,
/// because two handles that happen to line up are not the same as two handles
/// that are <em>kept</em> lined up.
/// </param>
public readonly record struct PathNode(
    double X,
    double Y,
    double Pressure,
    double InX,
    double InY,
    double OutX,
    double OutY,
    bool Corner)
{
    /// <summary>A corner node with no handles — what a plain pen click places.</summary>
    public static PathNode At(double x, double y, double pressure = 1) =>
        new(x, y, pressure, 0, 0, 0, 0, Corner: true);

    /// <summary>Whether either handle actually shapes anything.</summary>
    /// <remarks>
    /// Derived, so <c>[JsonIgnore]</c> — see <see cref="StrokePath.IsUsable"/>.
    /// A node is a record struct and every one of them is written, so a getter
    /// here would cost a key per node rather than per stroke.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasHandles =>
        InX != 0 || InY != 0 || OutX != 0 || OutY != 0;
}

/// <summary>
/// The authored geometry behind a stroke: a handful of nodes with Bezier
/// handles, from which <see cref="Stroke.Points"/> is produced.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a second description of a stroke, never a second kind of stroke.</b>
/// Q47 chose handles on every node, and the obvious implementation — widening
/// <see cref="StrokePoint"/> — would have been a migration and a second curve
/// type in the renderer. It is avoidable because a drawn stroke and an authored
/// path are genuinely different things: a drawn stroke has hundreds of sampled
/// points and wants no handles, a pen path has a dozen authored nodes and wants
/// nothing else. So the path is an optional extra field and
/// <see cref="Stroke.Points"/> stays what renders.
/// </para>
/// <para>
/// <b>The invariant this creates, and it is absolute:</b> a stroke's
/// <see cref="Stroke.Path"/> and <see cref="Stroke.Points"/> must never disagree.
/// Any operation that maps points maps the nodes and handles too, <em>or drops
/// the path</em>. Dropping is always allowed and always safe — the points are the
/// truth and the drawing is unchanged; what is forbidden is leaving behind a path
/// that describes a line the artist can no longer see.
/// </para>
/// </remarks>
public sealed class StrokePath
{
    public List<PathNode> Nodes { get; set; } = [];

    /// <summary>Whether the last node joins the first.</summary>
    public bool Closed { get; set; }

    /// <summary>Whether this path can describe a line at all.</summary>
    /// <remarks>
    /// <para>
    /// A path of fewer than two nodes has no segment, so flattening it produces
    /// no line and the invariant above could not hold whatever the points said.
    /// Callers treat this as "there is no path here".
    /// </para>
    /// <para>
    /// <c>[JsonIgnore]</c> because it is derived, and the omission was caught by
    /// <c>DerivedPropertyTests</c> rather than by review — this is the same trap
    /// <c>BlendOrNormal</c> fell into, where a convenience getter beside a
    /// nullable field wrote a key on every stroke that had one.
    /// </para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUsable => Nodes.Count >= 2;

    public StrokePath Clone() => new() { Nodes = [.. Nodes], Closed = Closed };
}
