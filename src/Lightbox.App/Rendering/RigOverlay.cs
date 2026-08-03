namespace Lightbox.App.Rendering;

/// <summary>What a rig mark is.</summary>
public enum RigMarkKind
{
    /// <summary>A named point — a socket or an extra pivot.</summary>
    Anchor,

    /// <summary>A collision rectangle — a hurtbox, hitbox or physics body.</summary>
    Shape,
}

/// <summary>
/// One anchor or collision shape, flattened for the overlay.
/// </summary>
/// <remarks>
/// A snapshot rather than the document object, for the same reason
/// <c>CanvasControl.GuideLine</c> is one: the renderer runs off the UI thread and
/// must never read a record the UI thread may be halfway through editing.
///
/// <para>
/// An anchor has zero width and height. That is not a special case to work around
/// — it is what makes one hit-test and one drag serve both kinds, which is the
/// whole reason this overlay is shared rather than written twice.
/// </para>
/// </remarks>
public readonly record struct RigMark(
    string Id,
    RigMarkKind Kind,
    string Label,
    double X,
    double Y,
    double W,
    double H,
    bool Selected)
{
    public double Right => X + W;

    public double Bottom => Y + H;

    public double CentreX => X + W / 2;

    public double CentreY => Y + H / 2;
}

/// <summary>Which corner of a shape a drag grabbed, or none.</summary>
public enum RigCorner
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <param name="Id">The mark that was hit, or null for empty canvas.</param>
/// <param name="Corner">
/// The corner grabbed. <see cref="RigCorner.None"/> means the body was grabbed, so
/// the drag moves rather than resizes.
/// </param>
public readonly record struct RigHit(string? Id, RigCorner Corner = RigCorner.None)
{
    public bool Any => Id is not null;

    public bool IsResize => Corner != RigCorner.None;
}

/// <summary>
/// The overlay's decisions: what a press hit, and what a drag produces.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>CanvasControl</c> on purpose, and the reason is the heatmap:
/// <c>CanvasControl.cs</c> is 2 500 lines with five fix commits behind it, and the
/// riskiest thing about adding a gesture to it is that the gesture's arithmetic
/// becomes untestable without a window. Everything here is a pure function of a
/// point, a mark list and a zoom, so every branch has a test and none of them needs
/// Avalonia.
/// </para>
/// <para>
/// <b>The overlay is chrome and this class knows it.</b> It returns geometry; the
/// view model decides whether that geometry becomes an edit, and the document is
/// what ends up holding it. Nothing here writes anything.
/// </para>
/// </remarks>
public static class RigOverlay
{
    /// <summary>Handle half-size in screen pixels, before the zoom is divided out.</summary>
    /// <remarks>
    /// In screen pixels rather than document pixels, so a handle stays the same size
    /// to the hand at every zoom. A document-space handle is unclickable at 25% and
    /// covers the drawing at 800%.
    /// </remarks>
    public const double HandleScreenRadius = 5;

    /// <summary>How close a press must be to an anchor's cross to count, in screen pixels.</summary>
    /// <remarks>
    /// Larger than the handle radius: an anchor is a point with no body to grab, so
    /// the only way to pick one up is to be forgiving about the distance.
    /// </remarks>
    public const double AnchorScreenRadius = 9;

    /// <summary>The smallest a shape may be dragged to, in document pixels.</summary>
    /// <remarks>
    /// Not zero. A rectangle collapsed to nothing is invisible, unhittable and still
    /// exported — an artist who overshoots a resize would lose the shape with no way
    /// to find it again.
    /// </remarks>
    public const double MinimumShapeSize = 2;

    /// <summary>
    /// What a press at this document point grabbed.
    /// </summary>
    /// <param name="marks">The marks on this frame.</param>
    /// <param name="x">Press position in document pixels.</param>
    /// <param name="y">Press position in document pixels.</param>
    /// <param name="scale">
    /// View scale, so screen-sized targets can be converted to document distances.
    /// </param>
    /// <remarks>
    /// <para>
    /// The order is the whole of it, and each step earns its place:
    /// </para>
    /// <list type="number">
    /// <item><b>A selected shape's corners first.</b> A corner sits on the shape's
    /// own edge, so body-before-corner would make resizing impossible.</item>
    /// <item><b>Anchors next.</b> A point inside a shape would otherwise be
    /// unreachable, and an anchor is almost always placed on the character the
    /// shapes are wrapped around.</item>
    /// <item><b>Shape bodies last, smallest first.</b> A hitbox drawn inside a
    /// hurtbox is the ordinary arrangement; taking the larger one would make the
    /// inner shape unselectable.</item>
    /// </list>
    /// </remarks>
    public static RigHit Hit(IReadOnlyList<RigMark> marks, double x, double y, double scale)
    {
        var handle = HandleScreenRadius / Math.Max(0.01, scale);
        var reach = AnchorScreenRadius / Math.Max(0.01, scale);

        foreach (var mark in marks)
        {
            if (mark.Kind != RigMarkKind.Shape || !mark.Selected) continue;
            if (CornerAt(mark, x, y, handle) is { } corner) return new RigHit(mark.Id, corner);
        }

        foreach (var mark in marks)
        {
            if (mark.Kind != RigMarkKind.Anchor) continue;
            if (Math.Abs(mark.X - x) <= reach && Math.Abs(mark.Y - y) <= reach)
                return new RigHit(mark.Id);
        }

        var bodies = marks
            .Where(m => m.Kind == RigMarkKind.Shape)
            .Where(m => x >= m.X && x <= m.Right && y >= m.Y && y <= m.Bottom)
            .OrderBy(m => Math.Abs(m.W * m.H))
            .ToList();

        return bodies.Count > 0 ? new RigHit(bodies[0].Id) : default;
    }

    private static RigCorner? CornerAt(RigMark mark, double x, double y, double handle)
    {
        bool Near(double cx, double cy) => Math.Abs(cx - x) <= handle && Math.Abs(cy - y) <= handle;

        if (Near(mark.X, mark.Y)) return RigCorner.TopLeft;
        if (Near(mark.Right, mark.Y)) return RigCorner.TopRight;
        if (Near(mark.X, mark.Bottom)) return RigCorner.BottomLeft;
        if (Near(mark.Right, mark.Bottom)) return RigCorner.BottomRight;
        return null;
    }

    /// <summary>
    /// The mark after a drag.
    /// </summary>
    /// <param name="mark">What was grabbed.</param>
    /// <param name="corner">Which corner, or <see cref="RigCorner.None"/> to move it.</param>
    /// <param name="dx">Drag in document pixels.</param>
    /// <param name="dy">Drag in document pixels.</param>
    /// <remarks>
    /// <para>
    /// A resize is applied by moving the grabbed corner and leaving the opposite one
    /// where it is, then normalising — so dragging a corner past its opposite flips
    /// the rectangle rather than producing a negative width. A negative width is not
    /// a shape an engine can read, and refusing the drag at the crossover point makes
    /// the handle feel broken.
    /// </para>
    /// <para>
    /// A move never resizes and a resize never moves the far corner. Those two
    /// sentences are the entire contract, and both have a test.
    /// </para>
    /// </remarks>
    public static RigMark Drag(RigMark mark, RigCorner corner, double dx, double dy)
    {
        if (mark.Kind == RigMarkKind.Anchor || corner == RigCorner.None)
        {
            return mark with { X = mark.X + dx, Y = mark.Y + dy };
        }

        double left = mark.X, top = mark.Y, right = mark.Right, bottom = mark.Bottom;
        switch (corner)
        {
            case RigCorner.TopLeft: left += dx; top += dy; break;
            case RigCorner.TopRight: right += dx; top += dy; break;
            case RigCorner.BottomLeft: left += dx; bottom += dy; break;
            default: right += dx; bottom += dy; break;
        }

        return Normalised(mark, left, top, right, bottom);
    }

    /// <summary>
    /// A rectangle from two opposite corners, the right way round and not degenerate.
    /// </summary>
    public static RigMark Normalised(RigMark mark, double left, double top, double right, double bottom)
    {
        var x = Math.Min(left, right);
        var y = Math.Min(top, bottom);
        var w = Math.Max(MinimumShapeSize, Math.Abs(right - left));
        var h = Math.Max(MinimumShapeSize, Math.Abs(bottom - top));
        return mark with { X = x, Y = y, W = w, H = h };
    }

    /// <summary>
    /// The cursor a hit should show.
    /// </summary>
    /// <remarks>
    /// Returned from here rather than decided in the control, so "what does this
    /// gesture do" has one answer that a test can read. A resize cursor over
    /// something that moves is a small lie an artist notices immediately.
    /// </remarks>
    public static string CursorFor(RigHit hit) => hit switch
    {
        { Any: false } => "default",
        { Corner: RigCorner.TopLeft or RigCorner.BottomRight } => "resize-nwse",
        { Corner: RigCorner.TopRight or RigCorner.BottomLeft } => "resize-nesw",
        _ => "move",
    };
}
