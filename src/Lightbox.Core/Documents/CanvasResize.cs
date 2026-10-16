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
    ///
    /// <para>
    /// <b>New paper on a locked Background layer is filled</b> (Q283, see
    /// <see cref="FillGrownPaper"/>); everywhere else the new margin is left
    /// transparent. That is the one thing this does beyond the three scene
    /// fields, and it adds a stroke rather than moving one.
    /// </para>
    /// </remarks>
    public static bool Apply(Scene scene, int newWidth, int newHeight, ResizeAnchor anchor)
    {
        if (newWidth < 1 || newHeight < 1) return false;
        if (newWidth == scene.Width && newHeight == scene.Height) return false;

        var (oldLeft, oldTop, oldRight, oldBottom) = (scene.Left, scene.Top, scene.Right, scene.Bottom);
        var (left, top, width, height) = Preview(scene, newWidth, newHeight, anchor);
        scene.Width = width;
        scene.Height = height;
        scene.OriginX = left == 0 ? null : left;
        scene.OriginY = top == 0 ? null : top;

        var grew = scene.Left < oldLeft || scene.Top < oldTop
            || scene.Right > oldRight || scene.Bottom > oldBottom;
        if (grew) FillGrownPaper(scene, oldLeft, oldTop, oldRight, oldBottom);
        return true;
    }

    /// <summary>
    /// Give every drawing on a locked Background layer a fill covering the
    /// new margin, beneath its existing marks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Locked only — the owner's rule, which is Photoshop's</b> (Q283). A
    /// locked Background is the paper; an unlocked one is a layer the artist
    /// has taken over, and like every other layer its new margin is empty.
    /// <c>SceneRenderer.BackgroundOf</c> clears to transparent whenever a
    /// Background layer exists, so without this the margin shows checkerboard.
    /// </para>
    /// <para>
    /// <b>A new stroke, never the old one stretched.</b> Rewriting the old
    /// fill's corners would be one coordinate edit, and it is the edit this
    /// type exists to avoid: <c>Hash01</c> seeds every dab dynamic from the
    /// bits of a position.
    /// </para>
    /// <para>
    /// <b>The margin only: the old paper is a hole in it.</b> Covering the
    /// whole new rectangle looks the same on a factory Background, whose fill
    /// already hides the old area — and changes the picture on one that was
    /// unlocked, drawn on without a fill, and locked again, which would turn
    /// opaque behind its lines (the guardian's review). The hole is the old
    /// paper cut to the new: where the paper grew one way and shrank the
    /// other, an uncut hole would fill the strip outside the paper by
    /// even-odd, out of sight until a later grow brought it back.
    /// </para>
    /// <para>
    /// <b>Every drawing, once, and no empty cel.</b> The paper normally lives
    /// on cel 0 with nothing after it; a cel with no drawing shows nothing,
    /// and giving it paper would make a frame the artist left bare opaque. A
    /// drawing exposed on several cels is one object and gets one fill. The
    /// colour and swatch are copied from the drawing's own paper — the lowest
    /// fill that covers the old paper — so a grey sheet grows grey and a
    /// palette recolour still reaches the margin. A smaller fill is a bucket
    /// fill, not the paper, so a drawing without one takes the scene's paper
    /// colour.
    /// </para>
    /// <para>
    /// A crop adds nothing (there is no new paper), and the fill a grow added
    /// is left behind harmlessly by a later crop, like every other mark
    /// outside the edge.
    /// </para>
    /// </remarks>
    private static void FillGrownPaper(Scene scene, int oldLeft, int oldTop, int oldRight, int oldBottom)
    {
        // The old paper as it lies inside the new, or nothing if it does not.
        var (holeL, holeT) = (Math.Max(oldLeft, scene.Left), Math.Max(oldTop, scene.Top));
        var (holeR, holeB) = (Math.Min(oldRight, scene.Right), Math.Min(oldBottom, scene.Bottom));
        var hole = holeL < holeR && holeT < holeB ? Rect(holeL, holeT, holeR, holeB) : null;
        var filled = new HashSet<Frame>(ReferenceEqualityComparer.Instance);
        foreach (var layer in scene.Layers)
        {
            if (!layer.IsBackground || !layer.Locked) continue;
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is not { } drawing || !filled.Add(drawing)) continue;
                var paper = drawing.Strokes.FirstOrDefault(s =>
                    s.Tool == ToolKind.Fill && Covers(s, oldLeft, oldTop, oldRight, oldBottom));
                drawing.Strokes.Insert(0, new Stroke
                {
                    Tool = ToolKind.Fill,
                    Color = paper?.Color ?? scene.BackgroundColor,
                    SwatchId = paper?.SwatchId,
                    PaletteId = paper?.PaletteId,
                    Brush = new BrushSettings { Opacity = paper?.Brush.Opacity ?? 1, AntiAlias = false },
                    Points = Rect(scene.Left, scene.Top, scene.Right, scene.Bottom),
                    Holes = hole is null ? null : [[.. hole]],
                });
            }
        }
    }

    private static List<StrokePoint> Rect(int left, int top, int right, int bottom) =>
    [
        new StrokePoint(left, top, 1),
        new StrokePoint(right, top, 1),
        new StrokePoint(right, bottom, 1),
        new StrokePoint(left, bottom, 1),
    ];

    /// <summary>Whether a fill's outline spans the whole rectangle — what makes it the paper.</summary>
    private static bool Covers(Stroke fill, int left, int top, int right, int bottom) =>
        fill.Points.Count > 0
        && fill.Points.Min(p => p.X) <= left && fill.Points.Min(p => p.Y) <= top
        && fill.Points.Max(p => p.X) >= right && fill.Points.Max(p => p.Y) >= bottom;

    /// <summary>
    /// Put the paper at an explicit document rectangle — what a crop does.
    /// Returns false and changes nothing if the rectangle is not a size, or if
    /// it asks for exactly the rectangle the scene already has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same three fields <see cref="Apply"/> writes, and no others</b>
    /// (Q128). Not one stroke coordinate, clip contour, guide, camera key or
    /// symbol placement is rewritten, so a crop is exactly as lossless as the
    /// resize it is a special case of: marks that fall outside the new edge
    /// stay in the record and come back unchanged if the paper grows again.
    /// A destructive crop would not be the mirror of that — <c>Hash01</c> seeds
    /// every dab dynamic from the bits of its position, so a mark deleted and
    /// drawn again is a <em>different</em> mark.
    /// </para>
    /// <para>
    /// <b>Anchored by rectangle rather than by <see cref="ResizeAnchor"/>,</b>
    /// because a crop knows where it wants the paper: the caller has a
    /// selection or a bounding box, not a corner to keep. <see cref="Apply"/>
    /// stays the route for "make it this many pixels" and this one for "put it
    /// here" — expressing either through the other loses the information the
    /// caller actually has.
    /// </para>
    /// <para>
    /// The no-op case returns false for <see cref="Apply"/>'s reason, B98's:
    /// a path that cannot tell whether it changed anything raises the dirty
    /// badge forever.
    /// </para>
    /// </remarks>
    public static bool CropTo(Scene scene, int left, int top, int width, int height)
    {
        if (width < 1 || height < 1) return false;
        if (left == scene.Left && top == scene.Top
            && width == scene.Width && height == scene.Height) return false;

        scene.Width = width;
        scene.Height = height;
        // Null rather than zero for the reason Scene.OriginX documents: an
        // untouched origin writes no key, so an ordinary document serializes
        // exactly as it did before cropping existed.
        scene.OriginX = left == 0 ? null : left;
        scene.OriginY = top == 0 ? null : top;
        return true;
    }
}
