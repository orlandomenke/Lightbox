namespace Lightbox.Core.Documents;

/// <summary>
/// Where the existing drawing sits inside the resized canvas — the nine-cell
/// grid every application of this kind offers.
/// </summary>
/// <remarks>
/// The anchor names where the <em>old</em> canvas lands in the new one, so
/// <see cref="TopLeft"/> puts all the new paper on the right and underneath,
/// and <see cref="BottomRight"/> puts it all above and to the left. Cropping
/// reads the same way round: the anchor is the corner that is kept.
/// </remarks>
public enum ResizeAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// Changing the size of the paper without changing what is drawn on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in the document moves.</b> The scene's width, height and origin
/// are the only fields this touches: not one stroke coordinate, clip contour,
/// guide, camera key or symbol placement is rewritten. That is the whole
/// design, and the reason is <c>Hash01</c> — every dab dynamic is seeded from
/// the bits of a dab's position, so translating the drawing to make room on
/// the left would re-roll the grain of every mark in the document. See
/// <see cref="Scene.OriginX"/>.
/// </para>
/// <para>
/// The consequence worth knowing when reading the rest of the codebase: a
/// document rectangle is <c>[Left, Right) × [Top, Bottom)</c> and those are
/// not all zero any more. Anything converting a document coordinate to a
/// pixel in a layer bitmap subtracts the origin; anything that only allocates
/// a bitmap of <see cref="Scene.Width"/> × <see cref="Scene.Height"/> is
/// already correct.
/// </para>
/// </remarks>
public static class CanvasResize
{
    /// <summary>
    /// How far the document's origin moves when a canvas of
    /// <paramref name="oldWidth"/> × <paramref name="oldHeight"/> becomes
    /// <paramref name="newWidth"/> × <paramref name="newHeight"/> around
    /// <paramref name="anchor"/>.
    /// </summary>
    /// <remarks>
    /// Negative means the paper grew leftward or upward. Pure arithmetic and
    /// public so it can be tested and so the preview can draw the new edges
    /// without performing the edit.
    ///
    /// <para>
    /// <b>An odd amount of growth on a centred anchor puts the spare pixel
    /// right and bottom</b>, because the left share is floored. Splitting it
    /// the other way would be equally defensible; what matters is that it is
    /// decided once here rather than differently in the preview and the edit.
    /// </para>
    /// </remarks>
    public static (int Dx, int Dy) OriginShift(
        int oldWidth, int oldHeight, int newWidth, int newHeight, ResizeAnchor anchor)
    {
        var growX = newWidth - oldWidth;
        var growY = newHeight - oldHeight;

        var onLeft = anchor switch
        {
            ResizeAnchor.TopLeft or ResizeAnchor.Left or ResizeAnchor.BottomLeft => 0,
            ResizeAnchor.TopRight or ResizeAnchor.Right or ResizeAnchor.BottomRight => growX,
            _ => Half(growX),
        };

        var onTop = anchor switch
        {
            ResizeAnchor.TopLeft or ResizeAnchor.Top or ResizeAnchor.TopRight => 0,
            ResizeAnchor.BottomLeft or ResizeAnchor.Bottom or ResizeAnchor.BottomRight => growY,
            _ => Half(growY),
        };

        // Paper added on the left moves the left edge left, which is a
        // negative origin — hence the sign.
        return (-onLeft, -onTop);
    }

    /// <summary>
    /// Half, truncated towards zero — which is what makes growing and cropping
    /// by the same odd amount exact inverses of one another.
    /// </summary>
    /// <remarks>
    /// <b>Rounding it any other way puts the drawing back in the wrong
    /// place.</b> Truncation is symmetric (<c>-3 / 2</c> is -1 and <c>3 / 2</c>
    /// is 1), so a grow of 3 that added one pixel on the left is undone by a
    /// crop of 3 that takes one pixel off the left. Flooring is not symmetric
    /// — it would take <em>two</em> off the left — and the result is a
    /// one-pixel drift per round trip that nothing reports and nobody notices
    /// until an export is off by three.
    /// </remarks>
    private static int Half(int n) => n / 2;

    /// <summary>The document rectangle a resize would produce, without doing it.</summary>
    public static (int Left, int Top, int Width, int Height) Preview(
        Scene scene, int newWidth, int newHeight, ResizeAnchor anchor)
    {
        var (dx, dy) = OriginShift(scene.Width, scene.Height, newWidth, newHeight, anchor);
        return (scene.Left + dx, scene.Top + dy, newWidth, newHeight);
    }

    /// <summary>
    /// Resize the paper. Returns false and changes nothing if the request is
    /// not a size (a canvas has to be at least one pixel each way) or if it
    /// asks for exactly what the scene already is.
    /// </summary>
    /// <remarks>
    /// The no-op case returning false is what lets the caller avoid pushing an
    /// undo step for a dialog somebody opened and confirmed without typing
    /// anything — B98's point, that a path which cannot tell whether it
    /// changed something will raise the dirty badge forever.
    /// </remarks>
    public static bool Apply(Scene scene, int newWidth, int newHeight, ResizeAnchor anchor)
    {
        if (newWidth < 1 || newHeight < 1) return false;
        if (newWidth == scene.Width && newHeight == scene.Height) return false;

        var (left, top, width, height) = Preview(scene, newWidth, newHeight, anchor);
        scene.Width = width;
        scene.Height = height;
        scene.OriginX = left == 0 ? null : left;
        scene.OriginY = top == 0 ? null : top;
        return true;
    }
}
