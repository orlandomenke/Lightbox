using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The box round what a transform can actually move — the drawing as it looks,
/// not as the record remembers it.
/// </summary>
/// <remarks>
/// <para>
/// <b>B302, and the hole underneath it.</b> The gizmo used to be sized by
/// <c>TransformOps.Bounds</c>, which walks stroke points and nothing else. Two
/// consequences, filed as one cosmetic bug and one silence:
/// </para>
/// <list type="bullet">
/// <item>
/// A stroke record remembers ink the artist rubbed out, so on a reworked
/// drawing the handles sat well outside anything visible. Cosmetic — everything
/// moved together, so no pixel landed wrong.
/// </item>
/// <item>
/// It never looked at <see cref="Frame.PngBase64"/> at all, so a frame that is
/// <em>nothing but imported pixels</em> measured as empty and
/// <c>BeginTransform</c> reported <em>"Nothing to transform in this scope."</em>
/// An imported drawing could not be transformed. That is not cosmetic, and it
/// was invisible because it wore the same message as an honestly empty layer.
/// </item>
/// </list>
/// <para>
/// <b>Why not simply wrap the strokes in <c>StrokeRecordCleaner</c>.</b> That
/// was tried and is worse: it drops a stroke once an eraser covers 85% of it,
/// so a frame whose strokes are <em>all</em> erased measures as empty — and a
/// baseline-plus-erased-strokes drawing would stop being transformable, trading
/// a real loss of function for a tidier box. The two problems above want the
/// same answer and it is this one: bound the <b>visible drawing</b>, baseline
/// included, ink judged per point rather than per stroke.
/// </para>
/// <para>
/// In Raster rather than beside <c>TransformOps</c> in Core because the fact it
/// needs lives here: whether a later erasure took a given point away
/// (<see cref="StrokePicker"/>). Core cannot see that, which is the reason
/// <c>Bounds</c> measured the raw record in the first place.
/// </para>
/// <para>
/// <b>The margin is deliberately the one <c>TransformOps.Bounds</c> used —
/// half the brush size — and not <see cref="BrushEngine.ReachBounds"/>.</b>
/// Reach is the truer answer to "where can this mark land", and swapping it in
/// here moved the box by 4 px on an ordinary brush, which moved <b>guide
/// snapping</b> with it: B225 ties <c>SnapBounds</c> to the box an artist lines
/// up against a guide, and two tests pinning exact snap positions caught it.
/// Tightening the box to the paint may well be right, but it is a change to
/// where a drawing lands, not to which strokes are counted, and it is not this
/// bug.
/// </para>
/// </remarks>
public static class VisibleDrawingBounds
{
    /// <summary>
    /// The box round everything <paramref name="filter"/> would move across
    /// <paramref name="frames"/>, or null when that is nothing.
    /// </summary>
    /// <param name="filter">
    /// What moves, or null for the whole drawing. A region-limited transform
    /// passes one, and then the baseline is left out: baseline pixels stay put
    /// under a region, exactly as the commit leaves them.
    /// </param>
    /// <param name="page">
    /// The paper, in stroke coordinates. What a baseline is bounded by — the
    /// record cannot say where a baseline's ink is, only that it covers the
    /// page, and over-approximating a box is the harmless direction.
    /// </param>
    public static SKRect? Of(IEnumerable<Frame> frames, Func<Stroke, bool>? filter, SKRect page)
    {
        var ink = new Box();
        var rubs = new Box();

        foreach (var frame in frames)
        {
            // The baseline moves only when everything does. Under a region the
            // commit leaves it where it is, so a gizmo drawn round it would box
            // pixels the drag is not going to take.
            if (filter is null && frame.HasBaseline)
            {
                ink.Take(page.Left, page.Top, page.Right, page.Bottom);
            }

            var strokes = frame.Strokes;
            var erasures = StrokePicker.ErasurePositions(strokes);
            for (var i = 0; i < strokes.Count; i++)
            {
                var stroke = strokes[i];
                if (filter is not null && !filter(stroke)) continue;

                // A gradient covers the layer wherever its axis points sit, so
                // the axis is not its extent. The same special case it gets
                // everywhere else that asks a gradient where it is.
                if (stroke.Tool == ToolKind.Gradient)
                {
                    ink.Take(page.Left, page.Top, page.Right, page.Bottom);
                    continue;
                }

                // An erasure is kept apart rather than counted or dropped —
                // see the fallback below for why it is neither.
                if (IsErasure(stroke))
                {
                    Pad(stroke, stroke.Points, ref rubs);
                    continue;
                }

                var surviving = SurvivingPoints(strokes, erasures, i);
                if (surviving.Count > 0) Pad(stroke, surviving, ref ink);
            }
        }

        // The box wraps what you can see. When the only thing moving is a rub,
        // it wraps the rub instead — otherwise a gesture that plainly changes
        // the drawing (a marquee over an eraser stroke, which un-erases at the
        // origin and erases at the destination) would come back "nothing to
        // transform" and offer no handles at all. B290's own tests move exactly
        // that and are what caught the first version of this, which dropped
        // erasures unconditionally.
        if (ink.Any) return ink.Rect;
        return rubs.Any ? rubs.Rect : null;
    }

    /// <summary>
    /// The points a later erasure has <em>not</em> taken away.
    /// </summary>
    /// <remarks>
    /// Per point rather than per stroke, which is the whole difference from
    /// <c>StrokeRecordCleaner</c>: a line rubbed out along half its length
    /// keeps a box around the half you can still see, instead of either the
    /// whole line (too big) or nothing (gone).
    /// </remarks>
    private static List<StrokePoint> SurvivingPoints(
        IReadOnlyList<Stroke> strokes, List<int> erasures, int position)
    {
        var stroke = strokes[position];
        var surviving = new List<StrokePoint>(stroke.Points.Count);
        foreach (var point in stroke.Points)
        {
            if (!ErasedAt(strokes, erasures, position, point)) surviving.Add(point);
        }
        return surviving;
    }

    /// <summary>
    /// Take these points inflated by half the brush size — the margin
    /// <c>TransformOps.Bounds</c> has always used, kept for the reason the
    /// class remarks give.
    /// </summary>
    private static void Pad(Stroke stroke, IReadOnlyList<StrokePoint> points, ref Box box)
    {
        var reach = stroke.Brush.Size / 2;
        foreach (var p in points)
        {
            box.Take(
                (float)(p.X - reach), (float)(p.Y - reach),
                (float)(p.X + reach), (float)(p.Y + reach));
        }
    }

    /// <summary>
    /// Has a <em>later</em> erasure taken this point's paint away? Earlier ones
    /// cannot have: the render stamps in record order.
    /// </summary>
    private static bool ErasedAt(
        IReadOnlyList<Stroke> strokes, List<int> erasures, int position, StrokePoint p)
    {
        foreach (var erasure in erasures)
        {
            if (erasure <= position) continue;
            if (StrokePicker.ErasesPoint(strokes[erasure], p.X, p.Y)) return true;
        }
        return false;
    }

    private static bool IsErasure(Stroke stroke) =>
        stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion;

    /// <summary>A growing rectangle that knows whether anything has been put in it.</summary>
    private struct Box()
    {
        private float _minX = float.MaxValue;
        private float _minY = float.MaxValue;
        private float _maxX = float.MinValue;
        private float _maxY = float.MinValue;

        public bool Any { get; private set; }

        public readonly SKRect Rect => new(_minX, _minY, _maxX, _maxY);

        public void Take(float left, float top, float right, float bottom)
        {
            if (left < _minX) _minX = left;
            if (top < _minY) _minY = top;
            if (right > _maxX) _maxX = right;
            if (bottom > _maxY) _maxY = bottom;
            Any = true;
        }
    }
}
