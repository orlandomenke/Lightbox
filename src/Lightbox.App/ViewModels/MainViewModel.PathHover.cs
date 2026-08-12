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
}
