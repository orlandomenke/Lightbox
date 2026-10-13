using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Raster;

/// <summary>How a merge treats a drawing whose erasers would reach the layer below.</summary>
public enum EraserMerge
{
    /// <summary>Flatten that drawing to pixels: exactly what the two layers looked like.</summary>
    Flatten,

    /// <summary>
    /// Cut the drawing's own strokes where its erasers crossed them and drop the
    /// erasers: still strokes, and nearly what the two layers looked like.
    /// </summary>
    KeepStrokes,
}

/// <summary>
/// Applies a drawing's erasures to its own strokes as cuts — the way a vector
/// eraser works — and returns the strokes that are left, with no eraser among
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> An eraser is recorded as a stroke that removes whatever
/// is under it. On its own layer that can only be that layer's marks; merged
/// into the layer below, the same eraser would start removing the lower layer's
/// marks too, so a merge has had to flatten any such drawing to pixels — which
/// an artist can no longer edit and the inbetweener cannot read. A line-art
/// drawing nearly always has erasing in it, so in practice merging line art
/// meant losing it as line art. Resolving the erasers first removes the reason.
/// </para>
/// <para>
/// <b>It is nearly exact, not exact, and each way it differs was accepted by
/// name</b> (Q264): a soft or half-strength eraser is all-or-nothing here; a cut
/// end is the brush's own round end rather than the eraser's bite, so the
/// corners of a cut come out slightly short; an eraser that shaved a line's
/// edge without reaching its centre leaves the line whole; and a textured brush
/// restarts at the cut, so its grain shifts there.
/// </para>
/// <para>
/// <b>Where a cut ends.</b> A piece stops where its own dab would first touch
/// what the eraser took — not a fixed distance back along the line. The first
/// version pulled back by the line's half-width along its path, which is right
/// only for a square crossing: at 45° the round end still reached 94 pixels
/// into the erased area, at a shallow slant 339 (found by the adversary pass).
/// The retreat is bounded at four half-widths, so an eraser that runs
/// <em>along</em> a line after crossing it does not swallow the stretch it only
/// shaved; past that the shaved edge returns, like any other shave.
/// </para>
/// <para>
/// <b>What it refuses, and it refuses rather than approximates.</b> Null means
/// "flatten this drawing as you always did", and it is the answer whenever the
/// cut would not be the picture: an erasure used inside a selection (the
/// renderer clips it there; cutting by its path would cut lines never erased),
/// a mirrored or tile-wrapped erasure (its copies erase too), a mirrored or
/// wrapped stroke with any erasure after it (the erasure may have crossed a
/// copy that is nowhere near the stroke's own points), and an erasure that
/// reaches anything that is not a plain path of paint dabs — a fill, gradient
/// or text, a blur, smudge, mixing, alpha-locked or simulated-medium stroke
/// (all read what is under them, or keep state along the stroke), or one bound
/// to a rig.
/// </para>
/// <para>
/// Order is meaning: an erasure cuts only what was drawn before it.
/// </para>
/// </remarks>
public static class EraserCut
{
    /// <summary>Longest step between samples along a stroke, in pixels.</summary>
    private const double Step = 1.0;

    /// <summary>Most samples taken along one stroke; the step widens past this.</summary>
    private const int MaxSamples = 20000;

    /// <summary>How far a cut may retreat from the erased centre, in half-widths of the line.</summary>
    private const double MaxRetreat = 4;

    /// <summary>
    /// How clear of the erased area a piece's last dab has to be, in pixels. A
    /// dab whose edge sits exactly on the eraser's still rasterises onto it.
    /// </summary>
    private const double Clearance = 1;

    public static bool IsErasure(Stroke stroke) => stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion;

    /// <summary>
    /// Would <see cref="Resolve"/> give an answer for this record? The same
    /// refusals, decided from what the strokes are and where their boxes lie —
    /// no cutting — so a caller that only needs yes or no does not pay for the
    /// cut it is about to ask for again.
    /// </summary>
    public static bool CanCut(IReadOnlyList<Stroke> record) => Plan(record) is not null;

    /// <summary>
    /// The record with every erasure applied as a cut and removed, or null when
    /// the cut would not be the picture (see the type's remarks).
    /// </summary>
    public static List<Stroke>? Resolve(IReadOnlyList<Stroke> record)
    {
        if (Plan(record) is not { } plan) return null;

        var result = new List<Stroke>(record.Count);
        var prepared = new Dictionary<Stroke, Erasure>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < record.Count; i++)
        {
            if (IsErasure(record[i])) continue;
            if (plan[i] is not { Count: > 0 } reaching)
            {
                result.Add(record[i]);
                continue;
            }
            var erasures = new List<Erasure>(reaching.Count);
            foreach (var stroke in reaching)
            {
                if (!prepared.TryGetValue(stroke, out var erasure)) prepared[stroke] = erasure = new Erasure(stroke);
                erasures.Add(erasure);
            }
            result.AddRange(Cut(record[i], erasures));
        }
        return result;
    }

    /// <summary>
    /// For each stroke, the later erasures that could touch it — or null for
    /// the whole record when any of them makes the cut something to refuse.
    /// </summary>
    private static List<Stroke>?[]? Plan(IReadOnlyList<Stroke> record)
    {
        var plan = new List<Stroke>?[record.Count];
        var boxes = new Dictionary<Stroke, Box>(ReferenceEqualityComparer.Instance);
        Box BoxOf(Stroke s)
        {
            if (!boxes.TryGetValue(s, out var box))
                boxes[s] = box = Box.Around(s, s.Tool == ToolKind.ClearRegion ? 0 : BrushEngine.RadiusAt(s.Brush, 1));
            return box;
        }

        for (var i = 0; i < record.Count; i++)
        {
            var stroke = record[i];
            if (IsErasure(stroke) || stroke.Points.Count == 0) continue;

            List<Stroke>? reaching = null;
            var anyLater = false;
            for (var k = i + 1; k < record.Count; k++)
            {
                var erasure = record[k];
                if (!IsErasure(erasure) || erasure.Points.Count == 0) continue;
                anyLater = true;
                // Its real footprint is not its path: the renderer clips it to
                // its selection, and repeats it for every mirror or tile.
                if (erasure.ClipId is not null || erasure.Symmetry is not null || erasure.Wrap is not null) return null;
                if (BoxOf(erasure).Touches(BoxOf(stroke))) (reaching ??= []).Add(erasure);
            }
            if (!anyLater) continue;
            // Its copies lie elsewhere; an erasure anywhere may have crossed one.
            if (stroke.Symmetry is not null || stroke.Wrap is not null) return null;
            if (reaching is null) continue;
            if (!IsPaintPath(stroke)) return null;
            plan[i] = reaching;
        }
        return plan;
    }

    /// <summary>A walked path of paint dabs and nothing more — the one kind that can be shortened.</summary>
    private static bool IsPaintPath(Stroke s) =>
        s.Tool == ToolKind.Brush
        && s.Brush.Kind == BrushKind.Paint && s.Brush.Mixing is null && s.Brush.Medium.Kind == MediumKind.None
        && !s.AlphaLocked
        && s.Path is null && s.Holes is null && s.GradientId is null
        && s.SimId is null && s.TextId is null && s.Baked is null
        && s.Weights is null && s.RestPoints is null;

    private readonly record struct Box(double Left, double Top, double Right, double Bottom)
    {
        public static Box Around(Stroke s, double grow)
        {
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var p in s.Points)
            {
                left = Math.Min(left, p.X);
                top = Math.Min(top, p.Y);
                right = Math.Max(right, p.X);
                bottom = Math.Max(bottom, p.Y);
            }
            return new Box(left - grow, top - grow, right + grow, bottom + grow);
        }

        public bool Touches(Box o) => Left <= o.Right && o.Left <= Right && Top <= o.Bottom && o.Top <= Bottom;

        /// <summary>Is the point within <paramref name="reach"/> of the box?</summary>
        public bool Near(double x, double y, double reach) =>
            x >= Left - reach && x <= Right + reach && y >= Top - reach && y <= Bottom + reach;
    }

    /// <summary>
    /// An erasure made ready to be asked about many points: its segments with
    /// the radius and box each one sweeps, worked out once rather than per
    /// point — the pressure curve behind a radius is not free, and a long
    /// eraser asked about every pixel of every line it might touch is where a
    /// merge's time went.
    /// </summary>
    private sealed class Erasure
    {
        private readonly record struct Segment(StrokePoint A, StrokePoint B, double Radius, Box Box);

        private readonly Segment[] _segments;
        private readonly List<IReadOnlyList<StrokePoint>>? _contours;
        private readonly Box _box;

        public Erasure(Stroke stroke)
        {
            var pts = stroke.Points;
            if (stroke.Tool == ToolKind.ClearRegion)
            {
                _contours = [pts];
                if (stroke.Holes is not null) _contours.AddRange(stroke.Holes);
                // The edges, for how far outside a point is; radius 0.
                var edges = new List<Segment>();
                foreach (var contour in _contours)
                {
                    for (var i = 0; i < contour.Count; i++)
                    {
                        var a = contour[i];
                        var b = contour[(i + 1) % contour.Count];
                        edges.Add(new Segment(a, b, 0, BoxOf(a, b, 0)));
                    }
                }
                _segments = [.. edges];
            }
            else if (pts.Count == 1)
            {
                var r = BrushEngine.RadiusAt(stroke.Brush, pts[0].Pressure);
                _segments = [new Segment(pts[0], pts[0], r, BoxOf(pts[0], pts[0], r))];
            }
            else
            {
                _segments = new Segment[pts.Count - 1];
                for (var i = 1; i < pts.Count; i++)
                {
                    var r = BrushEngine.RadiusAt(stroke.Brush, Math.Max(pts[i - 1].Pressure, pts[i].Pressure));
                    _segments[i - 1] = new Segment(pts[i - 1], pts[i], r, BoxOf(pts[i - 1], pts[i], r));
                }
            }
            _box = Box.Around(stroke, 0);
            foreach (var s in _segments)
            {
                _box = new Box(Math.Min(_box.Left, s.Box.Left), Math.Min(_box.Top, s.Box.Top),
                    Math.Max(_box.Right, s.Box.Right), Math.Max(_box.Bottom, s.Box.Bottom));
            }
        }

        private static Box BoxOf(StrokePoint a, StrokePoint b, double r) =>
            new(Math.Min(a.X, b.X) - r, Math.Min(a.Y, b.Y) - r, Math.Max(a.X, b.X) + r, Math.Max(a.Y, b.Y) + r);

        /// <summary>
        /// How far outside what this erasure removed the point is: zero or less
        /// inside it, and otherwise the gap to its edge — or
        /// <paramref name="limit"/> when the gap is at least that, which is all
        /// a caller asking "does a dab of this radius touch it" needs.
        /// </summary>
        public double Gap(double x, double y, double limit)
        {
            if (!_box.Near(x, y, limit)) return limit;
            if (_contours is not null && GeometryOps.ContainsEvenOdd(_contours, x, y)) return -1;

            var p = new StrokePoint(x, y, 0);
            var best = limit;
            foreach (var s in _segments)
            {
                if (!s.Box.Near(x, y, best)) continue;
                var gap = GeometryOps.DistToSegment(p, s.A, s.B) - s.Radius;
                if (gap < best) best = gap;
                if (best <= 0 && _contours is null) return best;
            }
            return best;
        }
    }

    private readonly record struct Sample(StrokePoint Point, double At, bool IsVertex);

    private static IEnumerable<Stroke> Cut(Stroke stroke, List<Erasure> erasures)
    {
        var pts = stroke.Points;
        if (pts.Count == 1)
        {
            if (erasures.All(e => e.Gap(pts[0].X, pts[0].Y, 1) > 0)) yield return stroke;
            yield break;
        }

        var samples = Samples(pts);
        var radius = new double[samples.Count];
        var covered = new bool[samples.Count];   // the eraser took this point of the line
        var touched = new bool[samples.Count];   // a dab here would reach into what it took
        var any = false;
        for (var i = 0; i < samples.Count; i++)
        {
            var p = samples[i].Point;
            radius[i] = BrushEngine.RadiusAt(stroke.Brush, p.Pressure);
            var reach = radius[i] + Clearance;
            var gap = reach;
            foreach (var erasure in erasures)
            {
                gap = Math.Min(gap, erasure.Gap(p.X, p.Y, gap));
                if (gap <= 0) break;
            }
            covered[i] = gap <= 0;
            touched[i] = gap < reach;
            any |= covered[i];
        }
        if (!any)
        {
            // Shaved at most, never crossed: one line still.
            yield return stroke;
            yield break;
        }

        // Each cut retreats from the erased stretch while the line's own dab
        // would still touch what was erased, and no further than MaxRetreat.
        var removed = (bool[])covered.Clone();
        Retreat(samples, radius, covered, touched, removed, forward: true);
        Retreat(samples, radius, covered, touched, removed, forward: false);

        var pieces = new List<List<StrokePoint>>();
        var start = -1;
        for (var i = 0; i <= samples.Count; i++)
        {
            var kept = i < samples.Count && !removed[i];
            if (kept && start < 0) start = i;
            if (!kept && start >= 0)
            {
                // One sample is a position, not a mark: a sliver that short is
                // the leftover between two cuts, not a line the artist drew.
                if (i - start >= 2) pieces.Add(Piece(samples, start, i - 1));
                start = -1;
            }
        }
        if (pieces.Count == 0) yield break;

        // One label per part: two strokes sharing it would both claim the
        // inbetweener's match, so the longest piece keeps the name.
        var longest = pieces.MaxBy(Length)!;
        foreach (var points in pieces)
        {
            var piece = stroke.Clone();
            piece.Points = points;
            if (!ReferenceEquals(points, longest)) piece.Label = null;
            yield return piece;
        }
    }

    /// <summary>One sweep along the line, extending every erased stretch on its far side.</summary>
    private static void Retreat(
        List<Sample> samples, double[] radius, bool[] covered, bool[] touched, bool[] removed, bool forward)
    {
        var from = double.NaN;   // where the erased stretch being retreated from ended
        var n = samples.Count;
        for (var step = 0; step < n; step++)
        {
            var i = forward ? step : n - 1 - step;
            if (covered[i])
            {
                from = samples[i].At;
                continue;
            }
            if (double.IsNaN(from)) continue;
            if (touched[i] && Math.Abs(samples[i].At - from) <= MaxRetreat * (radius[i] + Clearance))
            {
                removed[i] = true;
            }
            else
            {
                from = double.NaN;   // clear of it: the next sample starts a piece
            }
        }
    }

    /// <summary>A piece's points: where it starts, the stroke's own vertices on the way, where it ends.</summary>
    private static List<StrokePoint> Piece(List<Sample> samples, int first, int last)
    {
        var points = new List<StrokePoint> { samples[first].Point };
        for (var i = first + 1; i < last; i++)
        {
            if (samples[i].IsVertex) points.Add(samples[i].Point);
        }
        points.Add(samples[last].Point);
        return points;
    }

    private static double Length(List<StrokePoint> points)
    {
        var length = 0.0;
        for (var i = 1; i < points.Count; i++) length += GeometryOps.Dist(points[i - 1], points[i]);
        return length;
    }

    /// <summary>
    /// Points along the stroke about a pixel apart, every vertex among them as
    /// the very point the pen recorded — tilt and speed included.
    /// </summary>
    private static List<Sample> Samples(List<StrokePoint> pts)
    {
        var total = 0.0;
        for (var i = 1; i < pts.Count; i++) total += GeometryOps.Dist(pts[i - 1], pts[i]);
        var step = Math.Max(Step, total / MaxSamples);

        var samples = new List<Sample> { new(pts[0], 0, true) };
        var at = 0.0;
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var length = GeometryOps.Dist(a, b);
            var n = Math.Max(1, (int)Math.Ceiling(length / step));
            for (var k = 1; k < n; k++)
            {
                var u = (double)k / n;
                samples.Add(new Sample(Between(a, b, u), at + length * u, false));
            }
            at += length;
            samples.Add(new Sample(b, at, true));
        }
        return samples;
    }

    /// <summary>A point partway along a span: everything the pen recorded, where it had got to.</summary>
    private static StrokePoint Between(StrokePoint a, StrokePoint b, double u) => new(
        a.X + (b.X - a.X) * u,
        a.Y + (b.Y - a.Y) * u,
        a.Pressure + (b.Pressure - a.Pressure) * u,
        Mix(a.TiltX, b.TiltX, u),
        Mix(a.TiltY, b.TiltY, u),
        Mix(a.Speed, b.Speed, u));

    private static double? Mix(double? a, double? b, double u) =>
        a is { } x && b is { } y ? x + (y - x) * u : a ?? b;
}
