using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.ViewModels;

/// <summary>
/// What the white arrow is about to reach into, shown before it is clicked.
/// </summary>
/// <remarks>
/// Roadmap: <i>Vector selection that matches the hand it was learned with</i>,
/// first piece. It is first because it is the one that makes the others
/// discoverable — until the geometry is visible before a click, every gesture
/// in that item is guesswork with a mouse, and an artist cannot tell a line
/// with two points from one with twenty until they are already inside it.
/// </remarks>
public sealed record PathHoverPreview(string StrokeId, StrokePath Path);

public partial class MainViewModel
{
    private PathHoverPreview? _pathHover;

    /// <summary>The line under the pointer and its geometry, or null.</summary>
    public PathHoverPreview? PathHover => _pathHover;

    /// <summary>
    /// Preview whatever the white arrow would enter at a point. Returns true
    /// when the answer changed, so the canvas repaints on the frames that
    /// matter rather than on every pointer move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fitted once per line, not once per pointer move.</b> A hover fires
    /// continuously, and <see cref="PathEditSession.Open"/> runs a curve fit
    /// over every point of the stroke — so refitting per event would put work
    /// proportional to a stroke's length in a per-event path, which is the
    /// shape invariant 6 rules out. Holding the last answer and comparing ids
    /// makes the common case (the pointer moving along a line it is already
    /// previewing) free.
    /// </para>
    /// <para>
    /// <b>Isolation wins outright.</b> The overlay is one channel and only one
    /// thing can own it — the same reason the pen and isolation share it rather
    /// than each keeping a list. Previewing a neighbouring line while its nodes
    /// are being dragged would draw two sets of points and say nothing about
    /// which the next click belongs to.
    /// </para>
    /// </remarks>
    public bool HoverPathAt(double x, double y, double tolerance)
    {
        if (PathEditActive) return ClearPathHover();

        var strokes = PickableStrokes();
        if (strokes.Count == 0) return ClearPathHover();

        var hit = Lightbox.Raster.StrokePicker.TopmostAt(
            strokes, Lightbox.Raster.StrokeIndex.Of(strokes), x, y, tolerance);
        if (hit is not { } index) return ClearPathHover();

        var stroke = strokes[index];
        if (_pathHover?.StrokeId == stroke.Id) return false;

        // Open rather than a fit of our own: it is the same geometry the click
        // will produce, so the preview cannot promise points the tool then
        // fails to offer. A line too short to reshape previews as nothing,
        // which is the honest answer — clicking it would refuse too.
        var session = PathEditSession.Open(stroke);
        if (session is null) return ClearPathHover();

        _pathHover = new PathHoverPreview(stroke.Id, session.Path);
        PathEditChanged?.Invoke();
        return true;
    }

    /// <summary>Stop previewing — the pointer left, or the tool did.</summary>
    public bool ClearPathHover()
    {
        if (_pathHover is null) return false;
        _pathHover = null;
        PathEditChanged?.Invoke();
        return true;
    }

    // ---- the whole-line hover, for the two tools that take a whole line (B217) ----

    private string? _hoveredStrokeId;

    /// <summary>The outline of the line under the pointer, or null.</summary>
    /// <remarks>
    /// The geometry the canvas draws, published through its own event so the
    /// window pushes it exactly the way it pushes the selection's — one channel
    /// per kind of chrome, refreshed when it changes rather than polled.
    /// </remarks>
    public event Action<(IReadOnlyList<StrokePoint> Points, bool Closed)?>? HoveredLineChanged;

    /// <summary>The id of the line being previewed, or null. For tests.</summary>
    public string? HoveredStrokeId => _hoveredStrokeId;

    /// <summary>
    /// Preview the whole line the Arrow or the Width tool would take hold of.
    /// Returns whether the answer changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the same picker the press uses.</b> <c>StrokePicker.TopmostAt</c>
    /// is what a click resolves with, so the preview cannot light a line the
    /// click would then miss — the same reason the white arrow's preview opens
    /// a real <c>PathEditSession</c> rather than fitting a curve of its own.
    /// </para>
    /// <para>
    /// <b>An already-picked line previews as nothing.</b> It is drawn selected
    /// already, and a hover on top of that would be two outlines saying the
    /// same thing at two brightnesses. Isolation wins outright for the reason
    /// the node preview gives: one line is being worked on, and lighting its
    /// neighbours says nothing about what the next click belongs to.
    /// </para>
    /// <para>
    /// <b>Bounded.</b> The comparison is by id, so travelling along a line
    /// already previewed returns false and nothing is rebuilt or repainted —
    /// a hover fires on every pointer event, which is invariant 6's exact
    /// concern.
    /// </para>
    /// </remarks>
    public bool HoverStrokeAt(double x, double y, double tolerance)
    {
        if (PathEditActive) return ClearStrokeHover();

        var strokes = PickableStrokes();
        if (strokes.Count == 0) return ClearStrokeHover();

        var hit = Lightbox.Raster.StrokePicker.TopmostAt(
            strokes, Lightbox.Raster.StrokeIndex.Of(strokes), x, y, tolerance);
        if (hit is not { } index) return ClearStrokeHover();

        var stroke = strokes[index];
        if (Selection.IsStrokeSelected(stroke.Id)) return ClearStrokeHover();
        if (_hoveredStrokeId == stroke.Id) return false;

        _hoveredStrokeId = stroke.Id;
        HoveredLineChanged?.Invoke((stroke.Points, IsClosedStroke(stroke)));
        return true;
    }

    /// <summary>Stop previewing — the pointer left, or the tool did.</summary>
    public bool ClearStrokeHover()
    {
        if (_hoveredStrokeId is null) return false;
        _hoveredStrokeId = null;
        HoveredLineChanged?.Invoke(null);
        return true;
    }

    /// <summary>
    /// Whether a stroke's outline should be drawn closed.
    /// </summary>
    /// <remarks>
    /// Fills, cleared regions and glyphs are contours rather than paths — the
    /// same distinction <c>ToolKind.ClearRegion</c> exists to make — so their
    /// preview has to close or a filled shape previews as an open horseshoe.
    /// </remarks>
    private static bool IsClosedStroke(Stroke stroke) => stroke.Tool.FillsAContour();
}
