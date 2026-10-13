using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

/// <summary>
/// Computes the *effective* stroke record of a frame — what is actually on the
/// drawing, as opposed to what the record had to keep to be able to rebuild it.
///
/// A painted frame's record keeps every stroke, including brush strokes the
/// artist later erased — that's what makes re-rendering deterministic. But
/// interpolating the raw record resurrects erased artwork: erased strokes get
/// matched and drawn in the inbetweens. So before matching, we drop brush
/// strokes that later erasures substantially covered, and drop the erasures
/// themselves (their effect is baked in by the dropping).
///
/// Partially-erased strokes are kept whole — a visible-but-trimmed stroke
/// still carries the motion, and a faded "half eraser" in a tween looks far
/// worse than a briefly-untrimmed line.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is also what any AI sees, and that is the point rather than a side
/// effect.</b> Both surfaces that hand strokes to a model go through here —
/// the inbetween payload and <c>get_frame_strokes</c> — because a request
/// should describe the drawing, not the drawing's history. Erased artwork sent
/// to a model is paid for twice: once in tokens for something the artist
/// deliberately removed, and again in the model reasoning about a line that is
/// not on the page.
/// </para>
/// <para>
/// <b>B233: <see cref="ToolKind.ClearRegion"/> counts as an erasure and did
/// not.</b> Only <see cref="ToolKind.Eraser"/> was dropped, so clearing a
/// selection left the cleared strokes in the effective record *and* added the
/// clear itself — and <c>StrokePayload.ToWire</c> maps everything that is not
/// an eraser to <c>"brush"</c>, so the model was told there was a brush stroke
/// tracing the outline of a region the artist had emptied. The two kinds are
/// separate because they <em>render</em> differently — a path walked laying
/// dabs versus a filled contour — and they are one thing to anybody asking
/// what is on the drawing, which is the same grouping
/// <c>LayerMerge</c>, <c>MotionTrail</c> and <c>StrokePicker</c> already make.
/// </para>
/// </remarks>
public static class StrokeRecordCleaner
{
    /// <summary>Fraction of a stroke's samples an eraser must cover to kill it.</summary>
    public const double CoverageThreshold = 0.85;

    private const int Samples = 32;
    private const double RadiusSlack = 1.05;

    /// <remarks>
    /// <b>A group is read as the strokes inside it (Q265).</b> A merged layer's
    /// drawing sits in one entry of the record with no points of its own; sent
    /// as it stands it would reach a model as an empty brush stroke and the
    /// line art it holds as nothing. So each group is replaced, in place, by
    /// its own effective strokes — its erasers applied to its strokes and to
    /// nothing else, which is all they ever reached — and the erasures that
    /// come after the group in this record then apply to those like any others.
    /// </remarks>
    public static List<Stroke> EffectiveStrokes(IReadOnlyList<Stroke> record)
    {
        record = ThroughGroups(record);
        var result = new List<Stroke>();
        for (var i = 0; i < record.Count; i++)
        {
            var stroke = record[i];
            if (IsErasure(stroke)) continue;

            var erasersAfter = new List<Stroke>();
            for (var k = i + 1; k < record.Count; k++)
            {
                if (IsErasure(record[k])) erasersAfter.Add(record[k]);
            }

            if (erasersAfter.Count == 0 || Coverage(stroke, erasersAfter) < CoverageThreshold)
            {
                result.Add(stroke);
            }
        }
        return result;
    }

    /// <summary>The record with each group replaced by what is effectively inside it.</summary>
    private static IReadOnlyList<Stroke> ThroughGroups(IReadOnlyList<Stroke> record)
    {
        var grouped = false;
        foreach (var stroke in record)
        {
            if (stroke.Group is null) continue;
            grouped = true;
            break;
        }
        // Nearly every drawing: the record itself, and no copy of it.
        if (!grouped) return record;

        var flat = new List<Stroke>(record.Count);
        foreach (var stroke in record)
        {
            if (stroke.Group is { } group) flat.AddRange(EffectiveStrokes(group.Strokes));
            else flat.Add(stroke);
        }
        return flat;
    }

    /// <summary>Fraction of the stroke's resampled points lying inside any eraser's swept area.</summary>
    private static double Coverage(Stroke stroke, IReadOnlyList<Stroke> erasers)
    {
        var points = stroke.Points.Count > Samples
            ? GeometryOps.Resample(stroke.Points, Samples)
            : (IReadOnlyList<StrokePoint>)stroke.Points;
        if (points.Count == 0) return 0;

        var covered = 0;
        foreach (var p in points)
        {
            if (erasers.Any(e => Covers(e, p))) covered++;
        }
        return (double)covered / points.Count;
    }

    /// <summary>
    /// The two ways a drawing loses paint: a rubbed path and an emptied area.
    /// </summary>
    private static bool IsErasure(Stroke stroke) =>
        stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion;

    private static bool Covers(Stroke eraser, StrokePoint p)
    {
        var pts = eraser.Points;
        if (pts.Count == 0) return false;

        // A cleared region is a contour it emptied, not a path it walked, so
        // "covered" means inside it — under the same even-odd rule with the
        // same holes the render composited it with. Reading its outline as a
        // stroke path instead would count only the points near the edge, and a
        // shape cleared out of the middle of a drawing would come back.
        if (eraser.Tool == ToolKind.ClearRegion)
        {
            var contours = new List<IReadOnlyList<StrokePoint>> { eraser.Points };
            if (eraser.Holes is not null) contours.AddRange(eraser.Holes);
            return GeometryOps.ContainsEvenOdd(contours, p.X, p.Y);
        }

        if (pts.Count == 1)
        {
            var r = Radius(eraser, pts[0].Pressure);
            return GeometryOps.Dist(p, pts[0]) <= r;
        }
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var r = Radius(eraser, Math.Max(a.Pressure, b.Pressure));
            if (GeometryOps.DistToSegment(p, a, b) <= r) return true;
        }
        return false;
    }

    private static double Radius(Stroke eraser, double pressure) =>
        eraser.Brush.Size * Math.Max(pressure, 0.05) / 2 * RadiusSlack;
}
