using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.Raster;

/// <summary>
/// Which consecutive strokes were still wet together, and therefore have to be
/// simulated as one wash rather than as a stack of separate ones.
/// </summary>
/// <remarks>
/// <para>
/// A simulated medium gives every stroke its own wet region, its own boundary
/// and its own drying. Fill an area with several strokes and the result is
/// ribbons with a pale seam along each join — measured as mottle over a flat
/// swatch, <b>11.1%</b> for six overlapping strokes against <b>2.2%</b> for the
/// same dab coverage sharing one region. Filling an area is the commonest thing
/// a watercolourist does, so that is the wrong way round.
/// </para>
/// <para>
/// <b>The window is the paint's, and it is bounded.</b>
/// <see cref="BrushSettings.WetStrokes"/> says how many following strokes this
/// paint stays wet for. Bounded is what keeps invariant 1 whole: the wet state
/// at stroke <c>k</c> is a pure function of strokes <c>k−N…k−1</c>, which are
/// already in the record, so nothing is saved and a reload reproduces the
/// painting by replaying strokes in order.
/// </para>
/// <para>
/// <b>Grouping is deliberately narrow.</b> Two strokes fuse only when
/// everything the shared composite can carry once is identical — brush record,
/// colour, blend mode, clip, alpha lock, tool — and when their reachable
/// regions actually touch. Anything looser would either silently change what a
/// stroke's own opacity or blend mode means, or pay for a lattice spanning two
/// marks that never met.
/// </para>
/// <para>
/// A run is also bounded in space by its members: a chain fuses through
/// neighbours, so six strokes sweeping across an area share one region, and
/// six strokes scattered over the paper do not share any.
/// </para>
/// <para>
/// Strokes drawn with <em>different</em> brushes therefore do not fuse yet,
/// even inside a window. That is wet-into-wet between two different paints, and
/// it needs the lattice seeded per stroke rather than once for the run; the
/// wash case is the one an artist hits first and it is the one this covers.
/// </para>
/// </remarks>
public static class WetRun
{
    private static readonly JsonSerializerOptions Canonical = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Does this stroke's paint stay wet for anything that follows?</summary>
    /// <remarks>
    /// <para>
    /// The last two clauses are about which pipeline the stroke goes down, not
    /// about wetness. A fill, a gradient, a smudge and a blur are dispatched
    /// before the paint path is reached and have no dab scratch to share, so a
    /// medium on one of those is inert today and fusing it would route it
    /// somewhere it has never been.
    /// </para>
    /// <para>
    /// <b>An eraser does not stay wet either</b>, even though it does go down
    /// the paint path. It takes paint away rather than laying a pass another
    /// stroke could join, and letting it fuse reaches a commit path that can
    /// discard the stroke it just committed: an erasure that changed nothing
    /// inside its own bounds is thrown back out of the record
    /// (<c>DiscardErasureThatDidNothing</c>), and a fused re-simulation can
    /// move pixels outside those bounds. Narrow, and cheaper to make
    /// impossible than to reason about.
    /// </para>
    /// </remarks>
    public static bool StaysWet(Stroke stroke) =>
        stroke.Brush.Medium.Kind != MediumKind.None
        && stroke.Brush.WetStrokeWindow > 0
        && stroke.Tool == ToolKind.Brush
        && stroke.Brush.Kind is not (BrushKind.Smudge or BrushKind.Blur);

    /// <summary>
    /// Split a stroke list into the runs that must be rendered together. Most
    /// runs are one stroke long; a wet run is longer.
    /// </summary>
    public static List<List<Stroke>> Split(IReadOnlyList<Stroke> strokes)
    {
        var runs = new List<List<Stroke>>();
        foreach (var stroke in strokes)
        {
            if (runs.Count > 0
                && runs[^1] is { } open
                && open.Count <= open[0].Brush.WetStrokeWindow
                && CanFuse(open[^1], stroke))
            {
                open.Add(stroke);
                continue;
            }
            runs.Add([stroke]);
        }
        return runs;
    }

    /// <summary>
    /// The run <paramref name="next"/> would land in, given the strokes already
    /// committed to this frame — or null when it starts a run of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the interactive commit, which sees one stroke at a time and has to
    /// group it exactly as <see cref="Split"/> would over the finished record.
    /// Anything else and what an artist painted stops matching what reloading
    /// it renders.
    /// </para>
    /// <para>
    /// It walks the whole record rather than the tail because a run's members
    /// are counted from where the run <em>started</em>: seven fusible strokes
    /// with a window of six are one run of six and one of one, so whether the
    /// eighth joins anything cannot be answered from the end backwards.
    /// </para>
    /// </remarks>
    public static List<Stroke>? OpenRunFor(IReadOnlyList<Stroke> committed, Stroke next)
    {
        if (!StaysWet(next) || committed.Count == 0) return null;
        var runs = Split(committed);
        var open = runs[^1];
        if (open.Count > open[0].Brush.WetStrokeWindow) return null;
        if (!CanFuse(open[^1], next)) return null;
        return [.. open, next];
    }

    /// <summary>
    /// May <paramref name="next"/> join the run <paramref name="previous"/> is
    /// in? Both must stay wet, agree on everything the one shared composite
    /// carries, and reach the same paper.
    /// </summary>
    public static bool CanFuse(Stroke previous, Stroke next)
    {
        if (!StaysWet(previous) || !StaysWet(next)) return false;
        if (previous.Tool != next.Tool) return false;
        if (previous.Color != next.Color) return false;
        if (previous.ClipId != next.ClipId) return false;
        if (previous.AlphaLocked != next.AlphaLocked) return false;

        // Touching, or there is no wash to share. Two marks at opposite corners
        // of the paper are wet at the same time and still have nothing to do
        // with each other; fusing them would put one lattice across everything
        // between, which is invariant 6 broken for no visual gain. Unclamped
        // bounds on purpose — `ReachBounds` answers "where is the stroke",
        // which is the question here, rather than "what needs repainting".
        if (BrushEngine.ReachBounds(previous) is not { } here) return false;
        if (BrushEngine.ReachBounds(next) is not { } there) return false;
        if (!here.IntersectsWith(there)) return false;

        // The brush record whole, serialized for the same reason
        // `BrushComparison.SameMark` does it that way — the same technique
        // rather than the same code, since that one lives in the app and this
        // has to run in the rasteriser. A hand-written check over forty
        // properties is a list somebody forgets to extend, and forgetting here
        // fuses two strokes that paint differently. It also covers the blend
        // mode and the opacity, which live on the brush rather than the stroke
        // and are exactly what the one shared composite can only carry once.
        return Fingerprint(previous.Brush) == Fingerprint(next.Brush);
    }

    private static string Fingerprint(BrushSettings brush) =>
        JsonSerializer.Serialize(brush, Canonical);
}
