using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// <see cref="GeometryOps.Densify"/> for a stroke that is still being drawn: the settled prefix is
/// reused and only the tail is recomputed.
/// </summary>
/// <remarks>
/// <para>
/// B46. The live preview walks the whole stroke on every pointer event — that is BR1's rule and it
/// is not negotiable, because a dab's position decides every dynamic seeded from it. Densifying the
/// whole path each time is the expensive part of obeying it: <b>0.84 ms of a 1.15 ms walk at 600
/// points</b>, and all but the last fraction of it is recomputation of an answer that cannot have
/// changed.
/// </para>
/// <para>
/// It cannot have changed because <c>Densify</c> is local. Span <em>i</em> is interpolated from
/// <c>points[i-1 … i+2]</c>, so appending a point at index <em>k</em> can only alter the spans that
/// see it: <em>k-2</em>, <em>k-1</em> and <em>k</em>. Everything before that is settled, which is
/// the same observation <see cref="GeometryOps.Densify"/>'s own remarks make about why the newest
/// span is drawn straighter than it will be.
/// </para>
/// <para>
/// <b>Derived from the cached input rather than assumed to be an append.</b> A stabiliser may
/// rewrite the tail, undo may shorten the list, and a second stroke may arrive on the same
/// instance — so each call finds the first index whose value differs and invalidates from two spans
/// before it. That comparison is a scan of value-equal structs and costs nothing next to a
/// Catmull–Rom evaluation; assuming "one longer than last time" would be faster and wrong the first
/// time smoothing moved a point.
/// </para>
/// <para>
/// The output is value-identical to <c>Densify</c>'s, and
/// <c>IncrementalDensifyTests</c> asserts exactly that over every prefix of several strokes.
/// Faithfulness matters more than the saving here: these points decide where dabs land, and a
/// cache that drifted from the reference by one interpolated point would move a dab, re-seed its
/// scatter from a different position, and change the mark.
/// </para>
/// </remarks>
public sealed class IncrementalDensify(double maxChord = 2.0)
{
    /// <summary>The points we last densified, so a change can be located rather than assumed.</summary>
    private readonly List<StrokePoint> _seen = [];

    /// <summary>The densified result for <see cref="_seen"/>.</summary>
    private readonly List<StrokePoint> _output = [];

    /// <summary>How long <see cref="_output"/> was after each span, so a tail can be dropped.</summary>
    private readonly List<int> _spanEnds = [];

    /// <summary>Spans recomputed on the last call, for the tests that measure the saving.</summary>
    public int LastRecomputedSpans { get; private set; }

    /// <summary>
    /// The densified path, reusing whatever of the previous answer still applies.
    /// </summary>
    public IReadOnlyList<StrokePoint> Of(IReadOnlyList<StrokePoint> points)
    {
        // Fewer than three points has no interior span to interpolate, and Densify hands the
        // caller its own list back. Matching that keeps the two substitutable.
        if (points.Count < 3)
        {
            Reset();
            LastRecomputedSpans = 0;
            return points;
        }

        var firstChanged = FirstDifference(points);
        // A span is interpolated from one point behind and two ahead, so a change at index k is
        // first visible in span k-2.
        var firstSpan = Math.Max(0, firstChanged - 2);

        if (firstSpan == 0)
        {
            _output.Clear();
            _spanEnds.Clear();
            _output.Add(points[0]);
        }
        else
        {
            // Everything up to the end of the last untouched span survives verbatim.
            var keep = _spanEnds[firstSpan - 1];
            _output.RemoveRange(keep, _output.Count - keep);
            _spanEnds.RemoveRange(firstSpan, _spanEnds.Count - firstSpan);
        }

        for (var i = firstSpan; i < points.Count - 1; i++)
        {
            GeometryOps.AppendSpan(_output, points, i, maxChord);
            _spanEnds.Add(_output.Count);
        }

        LastRecomputedSpans = points.Count - 1 - firstSpan;
        _seen.Clear();
        _seen.AddRange(points);
        return _output;
    }

    /// <summary>Forget everything — the next call rebuilds from scratch.</summary>
    public void Reset()
    {
        _seen.Clear();
        _output.Clear();
        _spanEnds.Clear();
    }

    /// <summary>
    /// The first index at which the caller's points differ from the ones we cached, or the length
    /// of the shorter list when one is a prefix of the other.
    /// </summary>
    private int FirstDifference(IReadOnlyList<StrokePoint> points)
    {
        // A shortened list invalidates from where it now ends: the last span's neighbours are gone.
        if (points.Count < _seen.Count) return Math.Max(0, points.Count - 1);

        var shared = Math.Min(points.Count, _seen.Count);
        for (var i = 0; i < shared; i++)
        {
            if (points[i] != _seen[i]) return i;
        }
        // A pure append: the previously last point gains a neighbour, so the span behind it moves.
        return Math.Max(0, shared - 1);
    }
}
