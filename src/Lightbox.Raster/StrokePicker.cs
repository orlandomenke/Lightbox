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
/// <b>Rule two: an erasure is not an object.</b> An eraser stroke is a real
/// record entry with real geometry, and for a while that was taken to mean it
/// must be selectable "or a stray one could never be removed". It is the wrong
/// conclusion, and B232 is what it cost: erase a line, click where the line
/// used to be, press Delete — and the line comes back, because deleting the
/// eraser deletes the erasure. That reads as the application undoing something
/// nobody asked it to undo. An eraser's mark is the <em>absence</em> of ink, so
/// there is nothing there to take hold of: <see cref="ToolKind.Eraser"/> and
/// <see cref="ToolKind.ClearRegion"/> — the path form and the area form of the
/// same act — are never offered by a click or caught by a marquee. Undo is what
/// reverses an erasure, which is where an artist already looks for it.
/// </para>
/// <para>
/// The cost is stated rather than hidden: an eraser stroke that erased nothing
/// can now only be removed by undoing back past it. It leaves no mark, so there
/// is nothing on the canvas to point at and no way to have meant it — which is
/// the same reason it cannot be clicked.
/// </para>
/// <para>
/// <b>Rule three: erased ink is not there either, and this is the half that is
/// easy to miss.</b> Picking reads the <em>record</em>, and the record still
/// says a rubbed-out line runs through the gap the eraser left — so hiding the
/// eraser alone leaves the click landing on a line nobody can see, which is the
/// same complaint wearing the other hat. A point is only offered to a stroke if
/// no <em>later</em> erasure has taken the paint away there; a line erased along
/// its whole length is therefore unreachable by any tool that goes through this
/// class, which is every one of them.
/// </para>
/// <para>
/// Three things bound rule three, all in the safe direction — when in doubt the
/// paint is treated as still there, because failing to offer a mark an artist
/// can plainly see is much worse than offering one they cannot:
/// </para>
/// <list type="bullet">
/// <item><b>Partial erasures do not count.</b> <c>Brush.Opacity</c> is the alpha
/// the whole erasing layer composites at, so an eraser below 1 leaves paint
/// behind by construction — it faded the line rather than removing it, and a
/// faded line is still a line to click on.</item>
/// <item><b>A clipped erasure only erases inside its clip</b>, resolved through
/// <see cref="ClipRegionRegistry"/> the same way the render resolves it. Feather
/// is ignored, so a softened edge counts as not-erased.</item>
/// <item><b>The grab tolerance is not extended to erasures.</b> Tolerance is a
/// courtesy to a click, not paint — lending it to an eraser would eat a sliver
/// of visible ink either side of every rub.</item>
/// </list>
/// <para>
/// A marquee applies rule three per stroke rather than per point: it drops what
/// is <em>wholly</em> erased and keeps what is partly erased, because a
/// half-rubbed line is still on the canvas and boxing any part of it is a
/// reasonable way to ask for it.
/// </para>
/// <para>
/// <b>This is the second place that computes what an erasure left behind, and
/// the two are meant to differ.</b> <c>StrokeRecordCleaner</c> answers the same
/// question for the inbetweener, which resurrects erased artwork if it matches
/// on the raw record — but it answers it as a <em>whole-stroke judgement with a
/// tolerance</em> (85% of 32 resampled points, so a nearly-erased line is close
/// enough to gone for a tween). A click cannot use a tolerance: it needs to know
/// about the one point under the cursor, and "85% erased" says nothing about
/// whether <em>this</em> pixel is ink. Sharing the code would mean one of the
/// two callers getting an answer to a question it did not ask.
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
    /// Record positions of every stroke under the point, topmost first. Erasures
    /// are not among them — see rule two.
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
        var lastErasure = -1;

        var reach = (int)Math.Ceiling(tolerance) + 1;
        var box = new SKRectI((int)x - reach, (int)y - reach, (int)x + reach + 1, (int)y + reach + 1);

        foreach (var position in index.Intersecting(box))
        {
            if (position < 0 || position >= strokes.Count) continue;
            var stroke = strokes[position];
            if (!IsPickable(stroke))
            {
                if (ErasesPoint(stroke, x, y)) lastErasure = position;
                continue;
            }
            if (!Covers(stroke, x, y, tolerance)) continue;
            ink.Add(position);
        }

        // Rule three. Ascending in, so anything earlier than the last erasure to
        // pass through here has had its paint taken away at this point — the
        // record still holds the line, and the canvas does not.
        if (lastErasure >= 0) ink.RemoveAll(position => position < lastErasure);

        // Ascending in, topmost out — see rule one in the class remarks.
        ink.Reverse();
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
        var erasures = ErasurePositions(strokes);
        foreach (var position in index.Intersecting(box))
        {
            if (position < 0 || position >= strokes.Count) continue;
            // Rule two applies to a sweep as much as to a click, and more
            // quietly: a marquee that swallowed the eraser lying under the ink
            // would resurrect what it erased on the next Delete, with nothing
            // on screen to say the eraser had been caught.
            if (!IsPickable(strokes[position])) continue;
            if (!Touches(strokes[position], normalised)) continue;
            // Rule three, per stroke: a wholly rubbed-out line is not on the
            // canvas, so a box drawn over where it used to be catches nothing.
            if (!SurvivesAnywhere(strokes, erasures, position)) continue;
            caught.Add(position);
        }
        return caught;
    }

    /// <summary>
    /// Is this stroke something an artist can take hold of?
    /// </summary>
    /// <remarks>
    /// Rule two, in one place so a click and a marquee cannot disagree about it.
    /// The two erasing kinds are grouped here the same way
    /// <see cref="LayerMerge"/> and <c>MotionTrail</c> already group them:
    /// separate kinds because they are <em>rendered</em> differently — a path
    /// walked laying dabs versus a filled contour — and one thing as far as
    /// anything downstream of the render is concerned.
    /// </remarks>
    private static bool IsPickable(Stroke stroke) =>
        stroke.Tool is not (ToolKind.Eraser or ToolKind.ClearRegion);

    /// <summary>
    /// Has this erasure taken the paint away at this exact point?
    /// </summary>
    /// <remarks>
    /// The geometry is read the same way the render reads it — an eraser is a
    /// path walked laying dabs, a cleared region is an even-odd contour — and
    /// the three limits in rule three are applied here rather than at the two
    /// call sites, so a click and a marquee cannot come to different answers
    /// about the same rub.
    /// </remarks>
    private static bool ErasesPoint(Stroke stroke, double x, double y)
    {
        if (stroke.Points.Count == 0) return false;

        // Below full opacity the erasing layer composites at less than 1, so
        // paint survives underneath it by construction. That is a fade, and a
        // faded line is still a line.
        if (stroke.Brush.Opacity < 1) return false;

        if (stroke.ClipId is { } clip)
        {
            // Unresolvable means unknown, and unknown means not erased — the
            // safe direction. Feather is ignored for the same reason: a softened
            // edge only partly erases, so the hard contour is the honest limit.
            if (ClipRegionRegistry.Resolve(clip) is not { } region) return false;
            if (!GeometryOps.ContainsEvenOdd(region.Contours, x, y)) return false;
        }

        if (stroke.Tool == ToolKind.ClearRegion)
        {
            var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
            if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
            return GeometryOps.ContainsEvenOdd(contours, x, y);
        }

        // No tolerance: half the brush is what the rub reached, and lending it
        // the click's grab distance would eat visible ink either side of it.
        return NearPolyline(stroke.Points, x, y, stroke.Brush.Size / 2, closed: false);
    }

    /// <summary>Where the erasures are in the record, ascending.</summary>
    /// <remarks>
    /// Gathered once per marquee rather than per caught stroke. A frame holds
    /// tens of erasures against hundreds of strokes, so this is the cheap end of
    /// the sweep — and the common case, a frame with none, makes
    /// <see cref="SurvivesAnywhere"/> free.
    /// </remarks>
    private static List<int> ErasurePositions(IReadOnlyList<Stroke> strokes)
    {
        var positions = new List<int>();
        for (var i = 0; i < strokes.Count; i++)
        {
            if (!IsPickable(strokes[i])) positions.Add(i);
        }
        return positions;
    }

    /// <summary>
    /// Is any part of this stroke still on the canvas?
    /// </summary>
    /// <remarks>
    /// Sampled at the stroke's own points, which is the same resolution the
    /// record holds it at, and it exits on the first survivor — so a stroke
    /// nothing has erased costs one point against the erasures that come after
    /// it, and a frame with no erasures costs nothing at all.
    /// </remarks>
    private static bool SurvivesAnywhere(
        IReadOnlyList<Stroke> strokes, List<int> erasures, int position)
    {
        // Ascending, so the first erasure past this stroke starts the range and
        // there is nothing to allocate. Written as loops rather than LINQ for
        // that reason — this runs per caught stroke per point.
        var first = 0;
        while (first < erasures.Count && erasures[first] <= position) first++;
        if (first == erasures.Count) return true;

        foreach (var point in strokes[position].Points)
        {
            var erased = false;
            for (var i = first; i < erasures.Count && !erased; i++)
            {
                erased = ErasesPoint(strokes[erasures[i]], point.X, point.Y);
            }
            if (!erased) return true;
        }
        return false;
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
