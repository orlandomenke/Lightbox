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
        var reading = new RegionReading(strokes, mask, w, h);
        var moving = new List<int>();
        for (var i = 0; i < strokes.Count; i++)
        {
            if (reading.Reaches(i)) moving.Add(i);
        }
        return moving;
    }

    /// <summary>
    /// One region asked of one drawing, prepared once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Prepared rather than asked per stroke</b>, because both questions it
    /// answers need the same two things: where every erasure sits, and where a
    /// given stroke sits in the record. Computing either inside a per-stroke
    /// call makes the walk quadratic in the stroke count — the shape of
    /// performance fault invariant 6 is about, and easy to introduce here
    /// because each individual call still looks cheap.
    /// </para>
    /// <para>
    /// <b>The mark is sampled, not the vertices.</b> A stroke records the
    /// points the pen reported, and the ink between two of them is just as
    /// much on the canvas as the ink at them. Asking only about vertices meant
    /// a marquee dropped between two of them found nothing at all — the
    /// reported "the transform does not trigger" in its purest form, on a
    /// drawing where the line is plainly under the box. Sampling along each
    /// segment costs one pass proportional to ink length, which is what
    /// drawing that ink already costs.
    /// </para>
    /// </remarks>
    public sealed class RegionReading
    {
        private readonly IReadOnlyList<Stroke> _strokes;
        private readonly List<int> _erasures;
        private readonly Dictionary<Stroke, int> _positions = [];
        private readonly bool[] _mask;
        private readonly int _w;
        private readonly int _h;

        public RegionReading(IReadOnlyList<Stroke> strokes, bool[] mask, int w, int h)
        {
            _strokes = strokes;
            _mask = mask;
            _w = w;
            _h = h;
            _erasures = StrokePicker.ErasurePositions(strokes);
            for (var i = 0; i < strokes.Count; i++) _positions[strokes[i]] = i;
        }

        /// <summary>Does the region reach any of this stroke's visible mark?</summary>
        /// <remarks>
        /// <b>A gradient is judged by what it covers, not by where its points
        /// are.</b> Its two points are the ends of the axis the ramp runs
        /// along, and the ramp colours the whole layer wherever they sit —
        /// counting them is how a selection drawn straight over a visible
        /// gradient came back <em>"nothing to transform in this scope"</em>
        /// with the colour plainly inside the marquee. Restated here rather
        /// than left to <see cref="Across"/> because a walk along the axis is
        /// exactly the wrong question, and it looks like the right one.
        /// </remarks>
        public bool Reaches(int position)
        {
            if (_strokes[position].Tool == ToolKind.Gradient) return Array.IndexOf(_mask, true) >= 0;
            var (inside, _, any) = Across(position);
            return any && inside;
        }

        /// <summary>
        /// Does the region cut through this stroke — some visible mark inside
        /// and some outside? Those are the ones a clipping transform splits;
        /// everything else moves whole.
        /// </summary>
        public bool Crosses(Stroke stroke)
        {
            if (!_positions.TryGetValue(stroke, out var position)) return false;
            // A gradient crosses unless the region is the whole page.
            //
            // B323, and the owner's call against the recommendation — recorded
            // in Q166 with what it costs, which is this: a region move on a
            // layer carrying a background gradient now leaves a visible
            // rectangle of shifted background, and that is a surprise if you
            // were only moving a character. What buys it is that a marquee
            // means one thing everywhere. Photoshop moves the pixels you
            // selected whatever laid them down, and a gradient that alone
            // ignored the selection was the odd one out once strokes stopped
            // doing so.
            //
            // Judged by what it covers rather than by where its points are —
            // the same reading B7 gave it, pointed the other way. Its two
            // points are the ends of the axis the ramp runs along, so walking
            // between them says nothing about which part of the layer the
            // marquee took; what matters is whether any of the page is left
            // out of the region, because that is the part that stays.
            if (stroke.Tool == ToolKind.Gradient) return Array.IndexOf(_mask, false) >= 0;
            var (inside, outside, _) = Across(position);
            return inside && outside;
        }

        /// <summary>
        /// Whether the stroke's visible mark falls inside the region, outside
        /// it, or both — and whether it has any visible mark at all.
        /// </summary>
        /// <remarks>
        /// <b>Erasures are read on their raw points</b>, as everywhere else in
        /// this file: they have no ink of their own to survive. Ink is read on
        /// the segments between points a later erasure has not taken away
        /// (B297's rule), and a segment with an erased end is skipped rather
        /// than half-walked — conservative in the one direction that matters,
        /// since the failure mode of guessing the other way is rubbed-out
        /// paint coming back.
        /// </remarks>
        private (bool Inside, bool Outside, bool Any) Across(int position)
        {
            var stroke = _strokes[position];
            var raw = IsErasure(stroke) || stroke.Tool == ToolKind.Gradient;
            bool inside = false, outside = false, any = false;
            StrokePoint? previous = null;
            foreach (var point in stroke.Points)
            {
                if (!raw && ErasedAt(_strokes, _erasures, position, point))
                {
                    previous = null;
                    continue;
                }
                any = true;
                Note(point.X, point.Y, ref inside, ref outside);
                if (previous is { } from) Walk(from, point, ref inside, ref outside);
                previous = point;
                if (inside && outside) break;   // nothing further can change the answer
            }
            return (inside, outside, any);
        }

        /// <summary>The mark between two recorded points, at about a pixel a step.</summary>
        private void Walk(StrokePoint from, StrokePoint to, ref bool inside, ref bool outside)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            if (steps <= 1) return;
            for (var i = 1; i < steps; i++)
            {
                var t = (double)i / steps;
                Note(from.X + (dx * t), from.Y + (dy * t), ref inside, ref outside);
                if (inside && outside) return;
            }
        }

        private void Note(double x, double y, ref bool inside, ref bool outside)
        {
            if (InsideMask(x, y)) inside = true;
            else outside = true;
        }

        private bool InsideMask(double x, double y)
        {
            var px = (int)Math.Round(x);
            var py = (int)Math.Round(y);
            return px >= 0 && px < _w && py >= 0 && py < _h && _mask[(py * _w) + px];
        }
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
    /// The two clips a stroke the region cuts through is split into: the part
    /// that travels and the part left where it was.
    /// </summary>
    /// <param name="Moved">
    /// Clip for the copy that moves — the selection, carried through the same
    /// transform as the ink so it keeps covering the same paint.
    /// </param>
    /// <param name="Stayed">
    /// Clip for the original, which does not move: everything the selection
    /// does <em>not</em> cover. Null would mean "no clip", which here would
    /// mean the whole stroke stays and the whole stroke moves — the ink
    /// duplicated rather than divided.
    /// </param>
    public readonly record struct RegionClips(string? Moved, string? Stayed);

    /// <summary>
    /// Transform every stroke of <paramref name="frame"/> that passes
    /// <paramref name="filter"/>, leaving a stay copy of each moved erasure
    /// that was erasing something that stays. Returns how many strokes moved
    /// (copies not counted).
    /// </summary>
    /// <param name="split">
    /// Asked, per moving stroke, whether the region cuts through it and with
    /// which two clips — null for a stroke that moves whole. Absent entirely
    /// (the default) is the old whole-stroke behaviour, which is what a caller
    /// with no region to clip to wants.
    /// </param>
    /// <remarks>
    /// <b>B319. Why the clip rather than a cut.</b> A stroke the region cuts
    /// through becomes two entries carrying complementary clips, not two
    /// shorter strokes. Cutting the polyline would change the mark itself:
    /// <c>Hash01</c> seeds every dab dynamic from position and a cut restarts
    /// the dab walk, so scatter, size, flow, roundness, rotation and all three
    /// colour jitters re-roll along both halves. Two clipped copies of one
    /// stroke re-render dab for dab as the original did, which is what
    /// invariant 2 is protecting, and it is the same answer the line clipboard
    /// already gives for a partial copy.
    /// </remarks>
    /// <param name="mapClip">
    /// A stroke's own clip, carried through the same map — B340. Not applied
    /// to a stroke the region cuts through: <paramref name="split"/> has
    /// already decided both halves' clips, and mapping again would move the
    /// travelling half's stencil twice.
    /// </param>
    public static int TransformFrame(
        Frame frame, TransformOps.PointMap map, double sizeScale, Func<Stroke, bool> filter,
        Func<Stroke, RegionClips?>? split = null, Func<string, string>? mapClip = null)
    {
        var strokes = TransformOps.StrokesOf(frame);
        // Every filter decision is taken before anything moves: the region
        // filter reads point positions, so a stroke judged after its own
        // transform — or after a copy was inserted beside it — would be judged
        // where it landed rather than where the artist saw it.
        var record = strokes.ToArray();
        var moves = new bool[record.Length];
        for (var i = 0; i < record.Length; i++) moves[i] = filter(record[i]);

        // Same rule, same reason: which strokes the region cuts through is
        // asked of the drawing the artist selected, not of one being rewritten
        // under the loop below.
        var splits = new RegionClips?[record.Length];
        if (split is not null)
        {
            for (var i = 0; i < record.Length; i++)
            {
                if (moves[i]) splits[i] = split(record[i]);
            }
        }

        var result = new List<Stroke>(strokes.Count);
        var count = 0;
        for (var i = 0; i < record.Length; i++)
        {
            var stroke = record[i];
            if (!moves[i])
            {
                result.Add(stroke);
                continue;
            }

            if (splits[i] is { } clips)
            {
                // The part left behind keeps the stroke's identity and its
                // place in the record; the part that travels is the copy. That
                // way round because the record's order is what the artist sees
                // as depth — the copy slots in directly after its original, so
                // moving a shape cannot bring it in front of art it was behind.
                stroke.ClipId = clips.Stayed;
                result.Add(stroke);

                var moved = stroke.Clone();
                moved.ClipId = clips.Moved;
                TransformOps.TransformStroke(moved, map, sizeScale);
                result.Add(moved);
                count++;
                continue;
            }

            if (IsErasure(stroke) && ErasesStayingInk(frame, record, moves, i))
            {
                // Directly beneath the moved original, so the copy erases
                // exactly what the original erased: everything laid before it,
                // and nothing laid after.
                result.Add(stroke.Clone());
            }
            TransformOps.TransformStroke(stroke, map, sizeScale, mapClip);
            result.Add(stroke);
            count++;
        }

        strokes.Clear();
        strokes.AddRange(result);
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
