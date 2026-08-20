using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// One tick of the motion trail: where the subject is on one drawing.
/// </summary>
/// <param name="Index">
/// The timeline position the drawing was found at, for ordering and for
/// telling the painter which side of the playhead a tick is on. For a hold
/// this is the position the walk reached it at, not necessarily the keyed cel.
/// </param>
/// <param name="Current">The drawing the playhead is on.</param>
/// <param name="Before">
/// Earlier in the timeline than the playhead, carried from
/// <see cref="OnionGhost.Before"/> rather than re-derived from indices by the
/// painter — which is what makes the tint split survive a playhead standing
/// on a drawing that has no tick of its own (empty, unanchored): the painter
/// has no current index to compare against there, and the first version's
/// fallback painted future segments in the past's colour.
/// </param>
/// <param name="Anchored">
/// True when the position is the artist's own pivot anchor rather than the
/// derived stroke-bounds centre — the painter marks the difference, because an
/// authored point is a statement and a derived one is a guess.
/// </param>
public readonly record struct TrailPoint(
    int Index, double X, double Y, bool Current, bool Before, bool Anchored, string FrameId);

/// <summary>
/// Where the subject is on each drawing around the playhead — the substrate
/// motion path and spacing visualization share (Q98). The polyline through
/// these points is the path; the distance between consecutive ticks is the
/// spacing, exactly the chart animators draw on paper.
/// </summary>
/// <remarks>
/// <para>
/// Pure and view-only: nothing here reaches a pixel or the record, so the
/// invariants are not in play. Depth counts <b>drawings</b>, not cels, by
/// riding <see cref="OnionSkin.Ghosts"/> — a hold is the same drawing standing
/// still, and a coincident tick per held cel would be invisible anyway.
/// </para>
/// <para>
/// The point for a drawing is its authored <see cref="AnchorKind.Pivot"/>
/// anchor when one is placed on it, else the centre of its ink strokes'
/// bounds. <see cref="Scene.Pivot"/> is deliberately <b>not</b> a fallback:
/// it is one point for the whole document, and a trail through a point that
/// cannot move is a dot. A drawing with no ink and no anchor contributes no
/// tick — an empty drawing has no subject to be anywhere.
/// </para>
/// </remarks>
public static class MotionTrail
{
    /// <summary>
    /// The trail through <paramref name="before"/> drawings back and
    /// <paramref name="after"/> drawings forward of the playhead, in timeline
    /// order. Empty when nothing in range has a locatable subject.
    /// </summary>
    public static List<TrailPoint> PointsAround(
        Scene scene, Layer layer, int index, int before, int after)
    {
        var points = new List<TrailPoint>();

        foreach (var ghost in OnionSkin.Ghosts(layer, index, before, after, keysOnly: false))
        {
            if (Locate(scene, ghost.Frame) is { } at)
                points.Add(new TrailPoint(
                    ghost.Index, at.X, at.Y, Current: false, ghost.Before, at.Anchored, ghost.Frame.Id));
        }

        if (ExposureSheet.ExposedFrame(layer, index) is { } current
            && Locate(scene, current) is { } here)
        {
            points.Add(new TrailPoint(
                index, here.X, here.Y, Current: true, Before: false, here.Anchored, current.Id));
        }

        points.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        return points;
    }

    /// <summary>
    /// The subject's position on one drawing: its placed pivot anchor, else
    /// the centre of its ink bounds, else nothing.
    /// </summary>
    public static (double X, double Y, bool Anchored)? Locate(Scene scene, Frame frame) =>
        Locate(scene, frame, InkBounds(frame));

    /// <summary>
    /// <see cref="Locate(Scene, Frame)"/> for a caller that already walked the
    /// ink — the walk analyser needs the bounds anyway, and locating through
    /// the plain overload would walk every point a second time.
    /// </summary>
    public static (double X, double Y, bool Anchored)? Locate(
        Scene scene, Frame frame, (double MinX, double MinY, double MaxX, double MaxY)? bounds)
    {
        if (scene.Anchors is { } declared && frame.Anchors is { } placed)
        {
            foreach (var anchor in declared)
            {
                if (anchor.Kind == AnchorKind.Pivot && placed.TryGetValue(anchor.Id, out var p))
                    return (p.X, p.Y, true);
            }
        }

        return bounds is { } b ? ((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2, false) : null;
    }

    /// <summary>
    /// The bounds of a drawing's ink, or null when there is none. Shared with
    /// the analysers so "where the feet are" and "where the subject is" read
    /// the same strokes by the same rule.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? InkBounds(Frame frame)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var any = false;
        foreach (var stroke in frame.Strokes)
        {
            // Erasing kinds take ink away; where they went says nothing about
            // where the subject is.
            if (stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion) continue;
            foreach (var point in stroke.Points)
            {
                any = true;
                if (point.X < minX) minX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.X > maxX) maxX = point.X;
                if (point.Y > maxY) maxY = point.Y;
            }
        }
        return any ? (minX, minY, maxX, maxY) : null;
    }
}
