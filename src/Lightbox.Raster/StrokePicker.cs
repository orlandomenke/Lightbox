using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Which stroke is under this point, and which strokes a marquee caught.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one primitive vector tooling needs that the application has never
/// had.</b> Every piece of it already existed and nothing composed them:
/// <see cref="StrokeIndex.Intersecting"/> narrows the candidates,
/// <see cref="BrushEngine.ReachBounds"/> says what a stroke can reach, and
/// <see cref="GeometryOps.DistToSegment"/> measures the last few pixels. This
/// class is the composition and the two rules that make the answer feel right.
/// </para>
/// <para>
/// <b>Rule one: topmost first, which is the reverse of the index's contract.</b>
/// <see cref="StrokeIndex"/> yields ascending record positions on purpose —
/// strokes composite in paint order and a renderer that reordered them would
/// draw a different image. A hit test wants the opposite: the artist clicks on
/// what they can see, and what they can see is the stroke painted <em>last</em>.
/// Getting this backwards does not look like a wrong ordering, it looks like a
/// tolerance bug — the click lands on a line and selects a different line
/// underneath it — so the reversal is explicit here and pinned by a test.
/// </para>
/// <para>
/// <b>Rule two: ink before erasers.</b> An eraser stroke is a real record entry
/// with real geometry, and it has to be selectable or a stray one could never be
/// removed. But its mark is the <em>absence</em> of ink, so if an artist clicks
/// where a line is visible and gets the eraser that cut across it, the
/// application appears to have selected nothing. So ink strokes are offered
/// first — topmost-first among themselves — and erasers only after them. You
/// pick what you can see; if the only thing there is an eraser, you get the
/// eraser.
/// </para>
/// <para>
/// <b>The index carries reach, not repaint bounds — and it matters here.</b>
/// It was once built from <see cref="BrushEngine.CommitBounds"/>, which clamps
/// to the surface because its job is deciding what to repaint — so a stroke
/// lying <em>entirely</em> outside the document reached nothing and could not
/// be picked (B134). <see cref="StrokeIndex.Of"/> now uses the unclamped
/// <see cref="BrushEngine.ReachBounds"/>: where a stroke <em>is</em> does not
/// depend on where the paper ends — a stroke dragged past the edge is still
/// the artist's mark, and it must stay pickable, movable and deletable.
/// </para>
/// <para>
/// <b>What this does not know about, deliberately.</b> Layer visibility and
/// locking are the caller's business: the picker is handed a stroke list, and a
/// list from a hidden layer should never have been assembled. Keeping the filter
/// out here means one place decides what is pickable rather than two that can
/// disagree.
/// </para>
/// </remarks>
public static class StrokePicker
{
    /// <summary>
    /// Record positions of every stroke under the point, nearest-of-the-topmost
    /// first, with erasers after ink.
    /// </summary>
    /// <param name="tolerance">
    /// Extra grab distance in <em>document</em> units, on top of the stroke's own
    /// half-width. A caller derives it from the zoom the same way the transform
    /// gizmo does, so a thin line stays as easy to hit when zoomed out.
    /// </param>
    public static IReadOnlyList<int> At(
        IReadOnlyList<Stroke> strokes,
        StrokeIndex index,
        double x,
        double y,
        double tolerance)
    {
        var ink = new List<int>();
        var erasers = new List<int>();

        var reach = (int)Math.Ceiling(tolerance) + 1;
        var box = new SKRectI((int)x - reach, (int)y - reach, (int)x + reach + 1, (int)y + reach + 1);

        foreach (var position in index.Intersecting(box))
        {
            if (position < 0 || position >= strokes.Count) continue;
            var stroke = strokes[position];
            if (!Covers(stroke, x, y, tolerance)) continue;
            (stroke.Tool == ToolKind.Eraser ? erasers : ink).Add(position);
        }

        // Ascending in, topmost out — see rule one in the class remarks.
        ink.Reverse();
        erasers.Reverse();
        ink.AddRange(erasers);
        return ink;
    }

    /// <summary>The stroke an artist means when they click here, or null.</summary>
    public static int? TopmostAt(
        IReadOnlyList<Stroke> strokes, StrokeIndex index, double x, double y, double tolerance)
    {
        var hits = At(strokes, index, x, y, tolerance);
        return hits.Count > 0 ? hits[0] : null;
    }

    /// <summary>
    /// Record positions of every stroke a marquee caught, ascending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Touched, not enclosed</b> — Illustrator's rule, and the one that matches
    /// what a hand does. Dragging a box across a limb to grab its lines should
    /// grab them, and requiring full containment means dragging a box round the
    /// whole character every time.
    /// </para>
    /// <para>
    /// Ascending here rather than topmost-first, because a marquee produces a
    /// <em>set</em> rather than a choice — and a set that keeps record order can
    /// be re-rendered, re-ordered and diffed without a second sort.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> Within(
        IReadOnlyList<Stroke> strokes, StrokeIndex index, SKRect region)
    {
        var normalised = region.Standardized;
        var box = SKRectI.Round(normalised);
        // A zero-width drag is a click, and a click is not a marquee.
        if (box.Width <= 0 || box.Height <= 0) return [];

        var caught = new List<int>();
        foreach (var position in index.Intersecting(box))
        {
            if (position < 0 || position >= strokes.Count) continue;
            if (Touches(strokes[position], normalised)) caught.Add(position);
        }
        return caught;
    }

    /// <summary>
    /// Does this stroke's mark reach the point?
    /// </summary>
    /// <remarks>
    /// Half the brush size rather than <c>DabReach</c>, which is what the index
    /// used to narrow the candidates. The two want different numbers on purpose:
    /// bounds must not miss a pixel the render could touch, so they include
    /// scatter and bleed and are generous; a hit test wants the line an artist
    /// sees, so being generous there makes a click near a wide soft brush select
    /// something the artist would not say they clicked on.
    /// </remarks>
    private static bool Covers(Stroke stroke, double x, double y, double tolerance)
    {
        if (stroke.Points.Count == 0) return false;

        if (stroke.Tool == ToolKind.Fill)
        {
            // A fill is an area, so being inside it counts — and inside means
            // even-odd, the same rule it was painted under.
            var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
            if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
            if (GeometryOps.ContainsEvenOdd(contours, x, y)) return true;
            // Its edge is grabbable too, so a thin sliver of fill is not
            // unclickable at low zoom.
            return NearPolyline(stroke.Points, x, y, tolerance, closed: true);
        }

        // A gradient covers everything it was drawn over, so "inside it" would
        // mean "anywhere" and it would win every click on the layer. It is picked
        // by its axis instead — the two points the artist dragged, and the same
        // line the canvas already draws a gizmo for.
        var half = stroke.Tool == ToolKind.Gradient ? 0 : stroke.Brush.Size / 2;
        return NearPolyline(stroke.Points, x, y, half + tolerance, closed: false);
    }

    private static bool NearPolyline(
        IReadOnlyList<StrokePoint> points, double x, double y, double reach, bool closed)
    {
        var p = new StrokePoint(x, y, 0);
        if (points.Count == 1) return GeometryOps.Dist(p, points[0]) <= reach;

        for (var i = 1; i < points.Count; i++)
        {
            if (GeometryOps.DistToSegment(p, points[i - 1], points[i]) <= reach) return true;
        }
        return closed
            && points.Count > 2
            && GeometryOps.DistToSegment(p, points[^1], points[0]) <= reach;
    }

    private static bool Touches(Stroke stroke, SKRect region)
    {
        foreach (var point in stroke.Points)
        {
            if (region.Contains((float)point.X, (float)point.Y)) return true;
        }

        // A long stroke can cross the marquee without putting a point in it —
        // two points either side of a small box. Segment-versus-rectangle, so a
        // box dropped on the middle of a straight line still catches it.
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            if (SegmentHitsRect(stroke.Points[i - 1], stroke.Points[i], region)) return true;
        }

        // A fill enclosing the whole marquee has neither a point inside nor an
        // edge crossing it, and is plainly under the box.
        if (stroke.Tool == ToolKind.Fill && stroke.Points.Count >= 3)
        {
            var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
            if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
            if (GeometryOps.ContainsEvenOdd(contours, region.MidX, region.MidY)) return true;
        }

        return false;
    }

    private static bool SegmentHitsRect(StrokePoint a, StrokePoint b, SKRect r)
    {
        // Cheap reject on the segment's own bounds before any crossing maths.
        if (Math.Max(a.X, b.X) < r.Left || Math.Min(a.X, b.X) > r.Right) return false;
        if (Math.Max(a.Y, b.Y) < r.Top || Math.Min(a.Y, b.Y) > r.Bottom) return false;

        var corners = new[]
        {
            new StrokePoint(r.Left, r.Top, 0),
            new StrokePoint(r.Right, r.Top, 0),
            new StrokePoint(r.Right, r.Bottom, 0),
            new StrokePoint(r.Left, r.Bottom, 0),
        };
        for (var i = 0; i < 4; i++)
        {
            if (SegmentsCross(a, b, corners[i], corners[(i + 1) % 4])) return true;
        }
        return false;
    }

    private static bool SegmentsCross(StrokePoint a, StrokePoint b, StrokePoint c, StrokePoint d)
    {
        static double Side(StrokePoint p, StrokePoint q, StrokePoint r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);

        var d1 = Side(c, d, a);
        var d2 = Side(c, d, b);
        var d3 = Side(a, b, c);
        var d4 = Side(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
