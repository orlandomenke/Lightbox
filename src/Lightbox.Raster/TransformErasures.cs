using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The erasure-aware commit of a region-limited transform: moves the filtered
/// strokes exactly as <see cref="TransformOps.TransformFrame"/> does, and keeps
/// an untransformed copy of any moved erasure that was still holding staying
/// ink down.
/// </summary>
/// <remarks>
/// <para>
/// <b>Erased ink must never come back — that is the rule this class serves.</b>
/// An eraser stroke is a record entry with geometry, so a marquee whose
/// majority test catches one used to carry it along like a line: the strokes it
/// had rubbed out, sitting outside the selection, reappeared the moment it left
/// — as a ghost in the drag preview and permanently on apply. But the erasure
/// cannot simply be left behind either: an artist who carves a shape with the
/// eraser and then moves that shape means the carving to travel, and pinning
/// the erasure down would un-carve the copy at the destination. So the erasure
/// moves <em>and</em> stays: the moved one keeps carving the strokes it
/// travels with, the copy keeps holding down what it erased in place. This is
/// the transform's face of the doctrine <c>StrokePicker</c> states — an
/// erasure is not an object, its mark is the absence of ink, and no gesture
/// may turn that absence back into paint (B232, Q102).
/// </para>
/// <para>
/// <b>The copy is dropped when it plainly held nothing down</b>, because a
/// stroke that erases nothing is exactly the stray Q102 exists to stop — it
/// cannot be seen, clicked or removed. "Plainly" is a conservative reach-box
/// test rather than a pixel probe: boxes over-approximate, so the failure mode
/// is an inert extra stroke in the record, never resurrected ink. Q102's
/// direction, applied here: when in doubt, the paint is treated as still
/// erased. The same caution keeps every copy on a frame with baseline pixels —
/// the record cannot say where a baseline's ink is, and the one wrong
/// direction is the one that brings rubbed-out paint back.
/// </para>
/// <para>
/// In Raster rather than Core because "did this erasure touch ink" is a
/// question about the <em>mark</em>, and only <see cref="BrushEngine.ReachOf"/>
/// knows how far past its points a brush can throw paint.
/// </para>
/// </remarks>
public static class TransformErasures
{
    /// <summary>
    /// Record positions of the strokes a region-limited transform moves,
    /// ascending: ink judged by where its <em>surviving</em> points sit, and
    /// erasures by where their raw points sit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B297 — rule three, applied to the region filter.</b> The filter used
    /// to be <see cref="TransformOps.MajorityInside"/> over every stroke's raw
    /// points, and a stroke record remembers ink an artist can no longer see.
    /// So a lasso over a redrawn nose caught the <em>previous</em> nose — fully
    /// rubbed out, points still majority-inside — and the move lifted it out
    /// from under its eraser: an invisible line became visible and rode the
    /// drag. The dual failure hid in the same test: a partially-erased line
    /// whose surviving end the artist plainly boxed could still classify as
    /// "stays", because the rubbed-out points pulled its majority outside.
    /// <see cref="StrokePicker"/> already answers this for a click, a marquee
    /// and select-all — erased ink is not there — and this is that answer for
    /// the transform, from the same <see cref="StrokePicker.ErasesPoint"/>
    /// geometry so the two surfaces cannot disagree about one rub.
    /// </para>
    /// <para>
    /// <b>Erasures and gradients keep the raw test on purpose.</b> An erasure
    /// has no surviving ink to judge — it travels by where it sits, so it can
    /// carry the carving of the strokes it moves with, and
    /// <see cref="TransformFrame"/> below decides what it leaves behind. A
    /// gradient covers the layer wherever its two axis points sit, which is
    /// <see cref="TransformOps.MajorityInside"/>'s own special case.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> MovingWithin(
        IReadOnlyList<Stroke> strokes, bool[] mask, int w, int h)
    {
        var moving = new List<int>();
        var erasures = StrokePicker.ErasurePositions(strokes);
        for (var i = 0; i < strokes.Count; i++)
        {
            var stroke = strokes[i];
            if (IsErasure(stroke) || stroke.Tool == ToolKind.Gradient)
            {
                if (TransformOps.MajorityInside(stroke, mask, w, h)) moving.Add(i);
                continue;
            }

            var survivors = 0;
            var inside = 0;
            foreach (var p in stroke.Points)
            {
                if (ErasedAt(strokes, erasures, i, p)) continue;
                survivors++;
                var x = (int)Math.Round(p.X);
                var y = (int)Math.Round(p.Y);
                if (x >= 0 && x < w && y >= 0 && y < h && mask[y * w + x]) inside++;
            }
            // A stroke none of whose ink survives is not on the canvas, so no
            // region can mean it — moving it would turn absence back into paint.
            if (survivors == 0) continue;
            if (inside * 2 >= survivors) moving.Add(i);
        }
        return moving;
    }

    /// <summary>
    /// Has a <em>later</em> erasure taken this point's paint away? Earlier ones
    /// cannot have: the render stamps in record order.
    /// </summary>
    private static bool ErasedAt(
        IReadOnlyList<Stroke> strokes, List<int> erasures, int position, StrokePoint p)
    {
        for (var k = 0; k < erasures.Count; k++)
        {
            if (erasures[k] <= position) continue;
            if (StrokePicker.ErasesPoint(strokes[erasures[k]], p.X, p.Y)) return true;
        }
        return false;
    }

    /// <summary>
    /// Transform every stroke of <paramref name="frame"/> that passes
    /// <paramref name="filter"/>, leaving a stay copy of each moved erasure
    /// that was erasing something that stays. Returns how many strokes moved
    /// (copies not counted).
    /// </summary>
    public static int TransformFrame(
        Frame frame, TransformOps.PointMap map, double sizeScale, Func<Stroke, bool> filter)
    {
        var strokes = TransformOps.StrokesOf(frame);
        // Every filter decision is taken before anything moves: the region
        // filter reads point positions, so a stroke judged after its own
        // transform — or after a copy was inserted beside it — would be judged
        // where it landed rather than where the artist saw it.
        var record = strokes.ToArray();
        var moves = new bool[record.Length];
        for (var i = 0; i < record.Length; i++) moves[i] = filter(record[i]);

        var count = 0;
        for (var i = 0; i < record.Length; i++)
        {
            if (!moves[i]) continue;
            var stroke = record[i];
            if (IsErasure(stroke) && ErasesStayingInk(frame, record, moves, i))
            {
                // Directly beneath the moved original, so the copy erases
                // exactly what the original erased: everything laid before it,
                // and nothing laid after.
                strokes.Insert(strokes.IndexOf(stroke), stroke.Clone());
            }
            TransformOps.TransformStroke(stroke, map, sizeScale);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Whether the moving erasure at <paramref name="index"/> could be holding
    /// down ink that does not move with it. Errs toward yes — see the class
    /// remarks for why that is the only safe direction.
    /// </summary>
    private static bool ErasesStayingInk(Frame frame, Stroke[] record, bool[] moves, int index)
    {
        // Baseline pixels never move under a region-limited transform, and the
        // record cannot say where their ink is.
        if (frame.HasBaseline) return true;

        var erasure = ReachBounds(record[index]);
        // Replay order: an erasure only takes paint from what was laid before it.
        for (var k = 0; k < index; k++)
        {
            if (moves[k] || IsErasure(record[k])) continue;
            // A gradient colours the whole layer wherever its two points sit.
            if (record[k].Tool == ToolKind.Gradient) return true;
            if (erasure.IntersectsWith(ReachBounds(record[k]))) return true;
        }
        return false;
    }

    /// <summary>
    /// The box a stroke's mark can reach: its points, padded by
    /// <see cref="BrushEngine.ReachOf"/> so scatter and bleed count.
    /// </summary>
    private static SKRect ReachBounds(Stroke stroke)
    {
        if (stroke.Points.Count == 0) return SKRect.Empty;
        var reach = BrushEngine.ReachOf(stroke.Brush);
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in stroke.Points)
        {
            if (p.X < minX) minX = (float)p.X;
            if (p.Y < minY) minY = (float)p.Y;
            if (p.X > maxX) maxX = (float)p.X;
            if (p.Y > maxY) maxY = (float)p.Y;
        }
        return new SKRect(minX - reach, minY - reach, maxX + reach, maxY + reach);
    }

    /// <summary>The two ways a drawing loses paint: a rubbed path and an emptied area.</summary>
    private static bool IsErasure(Stroke stroke) =>
        stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion;
}
