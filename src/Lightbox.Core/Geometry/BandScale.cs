namespace Lightbox.Core.Geometry;

using Lightbox.Core.Documents;

/// <summary>
/// Band scaling: the transform box divided by draggable lines, where moving a
/// line redistributes space between the two bands it separates and the outer
/// bounds never move.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q184, from a real drawing.</b> The legs are too short and the figure's
/// overall height is right. A uniform scale is the wrong tool — it changes the
/// whole sketch — and moving the legs by hand loses the join. What is wanted is
/// to put a line at the hip and drag it up: the legs lengthen, the torso
/// shortens, the figure stays the height it was.
/// </para>
/// <para>
/// <b>The map is separable and piecewise-linear, which is the whole design.</b>
/// A horizontal divider sits at a Y and rescales Y; a vertical one sits at an X
/// and rescales X; both can be present and neither knows about the other. So
/// the map is <c>(mapX(x), mapY(y))</c> with each axis independent, and within
/// any single band it is a plain scale-and-offset — affine. That last fact is
/// what makes the live preview cheap: one pass per band through its own matrix,
/// no mesh, no resampling.
/// </para>
/// <para>
/// <b>Only the two adjacent bands ever change.</b> With the outer bounds pinned
/// and one divider moving, the bands further out are held by dividers that did
/// not move — so this is forced rather than chosen, and it is also what the
/// owner asked for: nothing you did not touch moves.
/// </para>
/// <para>
/// <b>Nothing here reaches the document until a commit.</b> A band layout lives
/// in the transform session; committing moves stroke points through
/// <see cref="TransformOps.TransformStroke"/> and writes no new keys. There is
/// no serialized form to make optional because there is no serialized form.
/// </para>
/// </remarks>
public static class BandScale
{
    /// <summary>
    /// The narrowest a band may be squeezed to, in document pixels.
    /// </summary>
    /// <remarks>
    /// Not a matter of taste: a band of zero extent maps every coordinate in it
    /// to a single value, and the scale factor out of it is infinite. One pixel
    /// keeps the arithmetic finite while still letting an artist squash a band
    /// to nothing visible if that is what they want — and a drag is only a
    /// preview until it is committed, so squashing and dragging back costs
    /// nothing.
    /// </remarks>
    public const double MinimumBand = 1.0;

    /// <summary>
    /// One axis of a band layout: where the box starts and ends, where the
    /// dividers were placed, and where they are now.
    /// </summary>
    /// <param name="Source">
    /// The dividers as placed, ascending. These are the coordinates the map
    /// reads <em>from</em>, and they must not change while a drag is in
    /// progress — otherwise dragging a line out and back would not return the
    /// drawing to where it started.
    /// </param>
    /// <param name="Moved">
    /// Where those same dividers are now, ascending, same length as
    /// <paramref name="Source"/>. Equal to <paramref name="Source"/> means the
    /// identity.
    /// </param>
    public readonly record struct Axis(
        double Start, double End, IReadOnlyList<double> Source, IReadOnlyList<double> Moved)
    {
        /// <summary>An axis with no dividers — the identity.</summary>
        public static Axis None(double start, double end) => new(start, end, [], []);

        /// <summary>Whether this axis would move anything.</summary>
        public bool Moves
        {
            get
            {
                if (Source.Count != Moved.Count) return false;
                for (var i = 0; i < Source.Count; i++)
                {
                    if (Math.Abs(Source[i] - Moved[i]) > 1e-9) return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Where a divider may be dragged to: between its neighbours, never past
    /// them and never outside the box.
    /// </summary>
    /// <remarks>
    /// <b>This is "the boundaries of the box are never crossed", enforced in one
    /// place.</b> The neighbours are the <em>moved</em> positions rather than
    /// the placed ones, because what a line may not cross is where the line
    /// beside it is now.
    /// </remarks>
    public static double ClampDivider(Axis axis, int index, double wanted)
    {
        var lower = index == 0 ? axis.Start : axis.Moved[index - 1];
        var upper = index == axis.Moved.Count - 1 ? axis.End : axis.Moved[index + 1];
        var low = lower + MinimumBand;
        var high = upper - MinimumBand;
        // A box too small to hold two minimum bands cannot honour both limits.
        // Pinning to the middle keeps the map finite and the drag inert, which
        // is the honest behaviour for a box that has run out of room.
        if (high < low) return (lower + upper) / 2;
        return Math.Clamp(wanted, low, high);
    }

    /// <summary>
    /// Move one divider and return the axis that results, with the drag clamped.
    /// </summary>
    public static Axis Drag(Axis axis, int index, double wanted)
    {
        if (index < 0 || index >= axis.Moved.Count) return axis;
        var moved = axis.Moved.ToArray();
        moved[index] = ClampDivider(axis, index, wanted);
        return axis with { Moved = moved };
    }

    /// <summary>
    /// Add a divider at <paramref name="at"/>, keeping the list ascending.
    /// </summary>
    /// <remarks>
    /// Placed and moved both take the new coordinate, so adding a line changes
    /// nothing until it is dragged — which is what makes placing one a free
    /// action rather than an edit.
    /// </remarks>
    public static Axis Add(Axis axis, double at)
    {
        if (at <= axis.Start + MinimumBand || at >= axis.End - MinimumBand) return axis;
        var source = axis.Source.ToList();
        var moved = axis.Moved.ToList();
        var i = 0;
        while (i < moved.Count && moved[i] < at) i++;
        source.Insert(i, at);
        moved.Insert(i, at);
        return axis with { Source = source, Moved = moved };
    }

    /// <summary>Remove the divider at <paramref name="index"/>.</summary>
    public static Axis Remove(Axis axis, int index)
    {
        if (index < 0 || index >= axis.Moved.Count) return axis;
        var source = axis.Source.ToList();
        var moved = axis.Moved.ToList();
        source.RemoveAt(index);
        moved.RemoveAt(index);
        return axis with { Source = source, Moved = moved };
    }

    /// <summary>
    /// Map one coordinate through an axis's bands.
    /// </summary>
    /// <remarks>
    /// Outside the box the coordinate is returned untouched. That matters for a
    /// transform limited to a selection: a stroke reaching past the box keeps
    /// the part that is outside exactly where it was, which is the same promise
    /// the region-limited transform already makes.
    /// </remarks>
    public static double MapCoordinate(Axis axis, double v)
    {
        var n = axis.Source.Count;
        if (n == 0 || n != axis.Moved.Count) return v;
        if (v <= axis.Start || v >= axis.End) return v;

        // Which band v is in: the edges are Start, each divider, then End.
        var band = 0;
        while (band < n && v > axis.Source[band]) band++;

        var srcLow = band == 0 ? axis.Start : axis.Source[band - 1];
        var srcHigh = band == n ? axis.End : axis.Source[band];
        var dstLow = band == 0 ? axis.Start : axis.Moved[band - 1];
        var dstHigh = band == n ? axis.End : axis.Moved[band];

        var span = srcHigh - srcLow;
        // A band that was placed with no extent has no interior to interpolate
        // across; everything in it collapses to where it now begins.
        if (Math.Abs(span) < 1e-12) return dstLow;
        var t = (v - srcLow) / span;
        return dstLow + t * (dstHigh - dstLow);
    }

    /// <summary>
    /// The point map for a band layout: each axis piecewise-linear, independent
    /// of the other.
    /// </summary>
    public static TransformOps.PointMap Map(Axis x, Axis y) =>
        (px, py) => (MapCoordinate(x, px), MapCoordinate(y, py));

    /// <summary>
    /// The scale a band applies, for carrying brush size or reporting the drag.
    /// </summary>
    /// <remarks>
    /// One number per band rather than one for the whole map, because that is
    /// what a band scale <em>is</em> — the reason a single <c>sizeScale</c>
    /// argument cannot describe it, and the reason brush size is deliberately
    /// left alone (see <see cref="Insert"/>'s remarks).
    /// </remarks>
    public static double BandScaleAt(Axis axis, double v)
    {
        var n = axis.Source.Count;
        if (n == 0 || n != axis.Moved.Count) return 1;
        var band = 0;
        while (band < n && v > axis.Source[band]) band++;
        var srcLow = band == 0 ? axis.Start : axis.Source[band - 1];
        var srcHigh = band == n ? axis.End : axis.Source[band];
        var dstLow = band == 0 ? axis.Start : axis.Moved[band - 1];
        var dstHigh = band == n ? axis.End : axis.Moved[band];
        var span = srcHigh - srcLow;
        return Math.Abs(span) < 1e-12 ? 1 : (dstHigh - dstLow) / span;
    }

    /// <summary>
    /// One cell of the band grid: the source rectangle it reads and the
    /// destination rectangle it fills. Within a cell the map is affine.
    /// </summary>
    /// <remarks>
    /// Plain doubles rather than a rectangle type because <c>Lightbox.Core</c>
    /// carries no rendering dependency — the caller turns these into whatever
    /// its rasterizer wants.
    /// </remarks>
    public readonly record struct Cell(
        double SrcLow, double SrcHigh, double SrcTop, double SrcBottom,
        double DstLow, double DstHigh, double DstTop, double DstBottom)
    {
        /// <summary>The horizontal scale this cell applies.</summary>
        public double ScaleX =>
            Math.Abs(SrcHigh - SrcLow) < 1e-12 ? 1 : (DstHigh - DstLow) / (SrcHigh - SrcLow);

        /// <summary>The vertical scale this cell applies.</summary>
        public double ScaleY =>
            Math.Abs(SrcBottom - SrcTop) < 1e-12 ? 1 : (DstBottom - DstTop) / (SrcBottom - SrcTop);
    }

    /// <summary>
    /// The band grid as a list of affine cells — what a live preview draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is why the preview needs no mesh and no resampler.</b> Inside one
    /// cell the map is a scale and an offset per axis, so a cell can be drawn by
    /// blitting its source rectangle of the already-composited moving bitmap
    /// into its destination rectangle. <c>ScenePassBuilder.PassSpec</c> already
    /// carries a <c>Source</c> sub-rect and a <c>Matrix</c>, which is exactly
    /// this pair — so the preview is N passes instead of one and the rendering
    /// code does not change at all.
    /// </para>
    /// <para>
    /// <b>With no dividers there is exactly one cell and it is the identity</b>,
    /// which is the behaviour that exists today. A generalisation that collapses
    /// back onto the current code at N=0 is the sign it is the right one.
    /// </para>
    /// <para>
    /// Cells are emitted in reading order, top band first, and the count is
    /// <c>(x.Count + 1) * (y.Count + 1)</c> — two or three in practice, which is
    /// why no attempt is made to skip the ones that did not move.
    /// </para>
    /// </remarks>
    public static List<Cell> Cells(Axis x, Axis y)
    {
        var xs = Edges(x);
        var ys = Edges(y);
        var cells = new List<Cell>((xs.Src.Count - 1) * (ys.Src.Count - 1));
        for (var j = 0; j < ys.Src.Count - 1; j++)
        {
            for (var i = 0; i < xs.Src.Count - 1; i++)
            {
                cells.Add(new Cell(
                    xs.Src[i], xs.Src[i + 1], ys.Src[j], ys.Src[j + 1],
                    xs.Dst[i], xs.Dst[i + 1], ys.Dst[j], ys.Dst[j + 1]));
            }
        }
        return cells;
    }

    /// <summary>The band edges of an axis: start, every divider, end.</summary>
    private static (List<double> Src, List<double> Dst) Edges(Axis axis)
    {
        var src = new List<double>(axis.Source.Count + 2) { axis.Start };
        var dst = new List<double>(axis.Moved.Count + 2) { axis.Start };
        var n = Math.Min(axis.Source.Count, axis.Moved.Count);
        for (var i = 0; i < n; i++)
        {
            src.Add(axis.Source[i]);
            dst.Add(axis.Moved[i]);
        }
        src.Add(axis.End);
        dst.Add(axis.End);
        return (src, dst);
    }

    /// <summary>
    /// Insert a point wherever a stroke crosses a divider, before the map is
    /// applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not an optimisation — without it the drawing bends in the wrong
    /// place.</b> The map is piecewise-linear, so a line crossing a divider
    /// gains a kink there. A stroke is a list of sampled points, and the kink
    /// can only appear at a point: with no point at the crossing, the bend lands
    /// at whichever sample happened to be nearest, which on a sparsely sampled
    /// diagonal can be tens of pixels away from the line the artist dragged.
    /// </para>
    /// <para>
    /// <b>Every attribute is interpolated, not copied.</b> An inserted point
    /// stands between two real ones, so its pressure, tilt and speed are the
    /// lerp of theirs — copying either neighbour's would put a pressure step in
    /// the middle of a smooth line, and pressure drives width.
    /// </para>
    /// <para>
    /// <b>Brush size is left alone, deliberately.</b> An affine scale can carry
    /// <c>sizeScale</c> because there is one factor for the whole stroke; a band
    /// scale has a different factor per band and along one axis only, so there
    /// is no single number to apply. A stretched leg keeps the line weight it
    /// was drawn with, which is what an artist correcting proportion wants —
    /// they are moving the drawing, not redrawing it heavier.
    /// </para>
    /// </remarks>
    public static int Insert(Stroke stroke, Axis x, Axis y)
    {
        var added = InsertInto(stroke.Points, x, y);
        if (stroke.Holes is not null)
        {
            foreach (var hole in stroke.Holes) added += InsertInto(hole, x, y);
        }
        return added;
    }

    /// <summary>Every divider coordinate that a segment from a to b crosses, as t in (0,1).</summary>
    private static void Crossings(
        List<double> into, IReadOnlyList<double> dividers, double from, double to)
    {
        var delta = to - from;
        if (Math.Abs(delta) < 1e-12) return;
        foreach (var d in dividers)
        {
            var t = (d - from) / delta;
            // Strictly inside: a crossing exactly at an existing point needs no
            // new one, and admitting t=0 or t=1 would duplicate that point.
            if (t > 1e-9 && t < 1 - 1e-9) into.Add(t);
        }
    }

    private static int InsertInto(List<StrokePoint> points, Axis x, Axis y)
    {
        if (points.Count < 2) return 0;
        var xs = x.Source;
        var ys = y.Source;
        if (xs.Count == 0 && ys.Count == 0) return 0;

        var result = new List<StrokePoint>(points.Count + 8);
        var ts = new List<double>(8);
        var added = 0;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            result.Add(a);

            ts.Clear();
            Crossings(ts, xs, a.X, b.X);
            Crossings(ts, ys, a.Y, b.Y);
            if (ts.Count == 0) continue;
            ts.Sort();

            var last = -1.0;
            foreach (var t in ts)
            {
                // Two dividers crossed at the same place — a corner of the grid
                // — needs one point, not two.
                if (t - last < 1e-9) continue;
                last = t;
                result.Add(Lerp(a, b, t));
                added++;
            }
        }
        result.Add(points[^1]);

        if (added > 0)
        {
            points.Clear();
            points.AddRange(result);
        }
        return added;
    }

    private static StrokePoint Lerp(StrokePoint a, StrokePoint b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Pressure + (b.Pressure - a.Pressure) * t,
        LerpOptional(a.TiltX, b.TiltX, t),
        LerpOptional(a.TiltY, b.TiltY, t),
        LerpOptional(a.Speed, b.Speed, t));

    /// <summary>
    /// Lerp two optional channels, keeping "not reported" as not reported.
    /// </summary>
    /// <remarks>
    /// Null on either side means the channel was never measured there, and
    /// inventing a zero would be inventing a reading — a tilt of 0 is "upright",
    /// which is a fact, not an absence. One side present and the other not is
    /// treated as the present one holding across the gap, which is what every
    /// other consumer of a partly-reported channel does.
    /// </remarks>
    private static double? LerpOptional(double? a, double? b, double t) =>
        (a, b) switch
        {
            (null, null) => null,
            ({ } av, null) => av,
            (null, { } bv) => bv,
            ({ } av, { } bv) => av + (bv - av) * t,
        };
}
