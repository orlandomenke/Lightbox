using System.Collections.Concurrent;
using Lightbox.Core.Documents;

namespace Lightbox.Raster;

/// <summary>
/// The silhouette of a brush tip, as contours in unit space, for drawing the
/// on-canvas cursor.
/// </summary>
/// <remarks>
/// <para>
/// <b>B74.</b> The cursor was two concentric circles whatever the brush was, so a
/// chisel, a bristle or any imported tip was previewed as something it is not —
/// and the one question the gizmo exists to answer is *what mark will this
/// leave*. A circle is a confident wrong answer.
/// </para>
/// <para>
/// <b>Traced from the same bitmap the engine stamps, which is the point.</b> The
/// alternative was a table of outlines per <see cref="TipShape"/>, and a table is
/// a second description of the shape that drifts from the first — a generator
/// tweak would move the mark and leave the preview behind, silently, which is
/// exactly the class of defect B70 was. Deriving it from
/// <see cref="BrushTipRegistry"/> means the preview cannot disagree with the dab,
/// including for imported <c>.abr</c> and <c>.gbr</c> tips nobody wrote a table
/// entry for.
/// </para>
/// <para>
/// <b>Cached per tip, because tracing is not per-frame work.</b> Moore-neighbour
/// tracing plus Douglas–Peucker over a few hundred thousand pixels is a
/// millisecond or two — nothing once, and a stall at 60 Hz while the pointer
/// moves. Invariant 6 is about painting, and the same reasoning applies to any
/// per-render path: the trace happens once per tip and every frame after that is
/// a transform of the result.
/// </para>
/// <para>
/// Unit space — x and y in [-0.5, 0.5] about the tip's centre — so the caller
/// scales by whatever radius it is drawing at without this needing to know about
/// zoom, pressure or the view transform. That also makes the cache independent of
/// brush size, which is what lets one trace serve every size of the same tip.
/// </para>
/// </remarks>
public static class BrushTipOutline
{
    /// <summary>
    /// Alpha at or above this counts as inside the tip.
    /// </summary>
    /// <remarks>
    /// A silhouette needs a threshold and any threshold is a judgement, so this
    /// is the one worth stating: <b>low, on purpose.</b> A soft round tip fades
    /// over most of its radius, and outlining it at 50% would draw a ring at half
    /// the size the artist set and read as the brush being smaller than it is.
    /// Low enough to catch the faint edge of a feathered tip, high enough to
    /// ignore the compression noise an imported PNG carries in its transparent
    /// corners.
    /// </remarks>
    private const byte Inside = 8;

    private static readonly ConcurrentDictionary<string, List<List<StrokePoint>>> Cache = new();

    /// <summary>
    /// The tip's outline in unit space, or null when there is no such tip or it
    /// has no visible pixels.
    /// </summary>
    /// <remarks>
    /// Null rather than a circle, so the caller decides what "no tip" looks like.
    /// The cursor draws a plain ellipse there, which is the true shape of the
    /// round dab the engine falls back to.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<StrokePoint>>? Of(string? tipId)
    {
        if (string.IsNullOrEmpty(tipId)) return null;
        if (Cache.TryGetValue(tipId, out var cached)) return cached.Count == 0 ? null : cached;

        var traced = Trace(tipId);
        // An empty list is cached too: a tip with nothing in it must not be
        // re-traced on every frame just because the answer was "nothing".
        Cache[tipId] = traced;
        return traced.Count == 0 ? null : traced;
    }

    /// <summary>Forget every traced outline. For tests that re-register tips under reused ids.</summary>
    public static void Clear() => Cache.Clear();

    private static List<List<StrokePoint>> Trace(string tipId)
    {
        if (BrushTipRegistry.Resolve(tipId) is not { } tip) return [];
        var w = tip.Width;
        var h = tip.Height;
        if (w <= 2 || h <= 2) return [];

        var mask = new bool[w * h];
        var any = false;
        // Alpha is where a tip carries its shape — the importers bake grayscale
        // into it, and BrushEngine stamps through it. Reading anything else here
        // would outline a different tip from the one that gets drawn.
        using (var pixmap = tip.PeekPixels())
        {
            if (pixmap is null) return [];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (pixmap.GetPixelColor(x, y).Alpha < Inside) continue;
                    mask[y * w + x] = true;
                    any = true;
                }
            }
        }
        if (!any) return [];

        var (outer, holes) = ContourTracer.Trace(mask, w, h);
        var contours = new List<List<StrokePoint>>();
        // Epsilon in tip pixels rather than screen pixels: a 512-pixel tip
        // simplified at a quarter of a pixel is still smooth at any size the
        // cursor is drawn, and the point of simplifying is that the ring is a
        // few hundred segments rather than a few thousand.
        if (outer.Count >= 3) contours.Add(ToUnit(FloodFill.Simplify(outer, 0.25), w, h));
        foreach (var hole in holes)
        {
            // Holes are kept: a ring tip or a bristle comb is mostly holes, and a
            // silhouette without them is a blob claiming to be a brush.
            if (hole.Count >= 3) contours.Add(ToUnit(FloodFill.Simplify(hole, 0.25), w, h));
        }
        return contours;
    }

    /// <summary>
    /// Tip pixels to unit space about the tip's centre, divided by the tip's
    /// <b>larger</b> dimension.
    /// </summary>
    /// <remarks>
    /// <b>Not width by width and height by height, which is the obvious thing and
    /// is wrong.</b> <c>BrushEngine.StampDab</c> scales a tip by
    /// <c>radius * 2 / Math.Max(tip.Width, tip.Height)</c> — one factor, so a 64×32
    /// tip stamps at 2:1 and its long axis is what the brush size means.
    /// Normalising the axes independently would have squared every non-square tip
    /// and drawn an outline the right size around a mark of the wrong shape, which
    /// is precisely the disagreement between preview and dab that B74 is.
    /// </remarks>
    private static List<StrokePoint> ToUnit(List<StrokePoint> points, int w, int h)
    {
        var span = (double)Math.Max(w, h);
        var unit = new List<StrokePoint>(points.Count);
        foreach (var p in points)
        {
            unit.Add(new StrokePoint((p.X - w / 2.0) / span, (p.Y - h / 2.0) / span, 1));
        }
        return unit;
    }
}
