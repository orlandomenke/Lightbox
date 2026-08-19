using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Builds the marching-ants outline: the committed selection contours, the
/// marquee being dragged, and — when a transform session is live — the
/// preview matrix the moving pixels are already drawn through, so the
/// outline moves with the pixels it outlines instead of freezing at the
/// pre-drag position until the commit.
/// </summary>
/// <remarks>
/// Pure on purpose, following <c>RigOverlay</c>'s discipline: the canvas
/// control owns caching and invalidation, this class owns the geometry, and
/// the geometry is what a test can hold still. The base/frame split is the
/// ants animation's doing — the dash phase advances at display rate, so the
/// path is asked for every frame while its shape changes only when the
/// selection or the drag does. Re-walking every contour point per tick was
/// UI-thread work for chrome that had not moved (a flood-fill trace is
/// thousands of points across many contours); a native path copy is not.
/// </remarks>
public static class SelectionAnts
{
    /// <summary>
    /// The committed contours as one even-odd path, doc space. Null when
    /// there are none. Built once per selection change and cached by the
    /// caller; <see cref="FramePath"/> never mutates it.
    /// </summary>
    public static SKPath? BasePath(IReadOnlyList<List<StrokePoint>> contours)
        => contours.Count == 0 ? null : BrushEngine.PathFromContours(contours);

    /// <summary>
    /// The path one frame actually draws. The base rides the live transform
    /// preview; the in-progress drag shape is appended untransformed — it is
    /// the artist's hand, not the session's content. The caller owns the
    /// returned path.
    /// </summary>
    /// <param name="gizmoUp">
    /// A transform gizmo is on screen, so the committed outline is not drawn.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Why the outline hides rather than riding along under a gizmo.</b> It
    /// still rides — the matrix is applied everywhere else, and the commit
    /// moves the contours so the selection ends up in its new position — but
    /// while the gizmo is up there are two sets of chrome describing the same
    /// operation, and they do not agree in shape: the gizmo boxes the ink it is
    /// moving, the ants trace the region that was selected, and a lasso round a
    /// character is nothing like the box round it. Two outlines that disagree
    /// read as two tools fighting rather than one tool working.
    /// </para>
    /// <para>
    /// <b>Only under a gizmo</b>, which is the whole of the distinction. The
    /// Move tool opens a session with no gizmo, and there the ants are the only
    /// feedback there is — hiding them would leave a drag with nothing to show
    /// where the pixels are going, which is the bug this file was written to
    /// fix. The drag shape is unaffected either way: a marquee cannot be drawn
    /// while a transform owns the canvas.
    /// </para>
    /// </remarks>
    public static SKPath? FramePath(
        SKPath? basePath, SKMatrix? preview, IReadOnlyList<StrokePoint> dragShape,
        bool gizmoUp = false)
    {
        SKPath? path = null;
        if (basePath is not null && !gizmoUp)
        {
            path = new SKPath(basePath);
            if (preview is { IsIdentity: false } m) path.Transform(m);
        }
        if (dragShape.Count >= 3)
        {
            path ??= new SKPath { FillType = SKPathFillType.EvenOdd };
            path.MoveTo((float)dragShape[0].X, (float)dragShape[0].Y);
            for (var i = 1; i < dragShape.Count; i++)
            {
                path.LineTo((float)dragShape[i].X, (float)dragShape[i].Y);
            }
            path.Close();
        }
        return path;
    }

    /// <summary>
    /// The open polygon-in-progress trail, or null below two vertices. Open
    /// rather than closed because the polygon is not a region yet — closing
    /// it is the double-click's job.
    /// </summary>
    public static SKPath? OpenPath(IReadOnlyList<StrokePoint> polygon)
    {
        if (polygon.Count < 2) return null;
        var path = new SKPath();
        path.MoveTo((float)polygon[0].X, (float)polygon[0].Y);
        for (var i = 1; i < polygon.Count; i++)
        {
            path.LineTo((float)polygon[i].X, (float)polygon[i].Y);
        }
        return path;
    }
}
