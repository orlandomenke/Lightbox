using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Which strokes reach a region, without walking the ones that do not.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what stops tiling being slower than not tiling.</b> Rasterising a
/// tile means replaying the strokes that land in it. Without an index there is
/// no way to know which those are, so every tile replays every stroke and a
/// frame split into <c>n</c> tiles costs <c>n</c> times what it costs today —
/// tiles would turn a linear replay into a quadratic one. The index is
/// therefore a precondition of the tiled render path rather than an
/// optimisation of it.
/// </para>
/// <para>
/// <b>Order is the invariant, not speed.</b> Strokes composite in the order they
/// were painted; invariant 1 says the pixels are derived from that record. A
/// query that returned the right strokes in the wrong order would render a
/// different image — a later stroke landing under an earlier one — and would do
/// it only where two strokes happen to overlap, which is the kind of defect that
/// survives a whole test suite. Every query here yields ascending record
/// positions, and <c>StrokeIndexTests</c> pins it.
/// </para>
/// <para>
/// A uniform grid rather than an R-tree, and deliberately: it reuses
/// <see cref="TileGrid"/>, so the index cells and the render tiles are the same
/// squares. An R-tree adapts better to wildly uneven distributions and costs a
/// rebalance on insert — and strokes arrive one at a time while an artist draws,
/// which is the case a grid is best at and a tree is worst at.
/// </para>
/// </remarks>
public sealed class StrokeIndex
{
    private readonly Dictionary<TileCoord, List<int>> _cells = [];

    /// <summary>Bounds per stroke position, so a query can confirm a real overlap.</summary>
    private readonly Dictionary<int, SKRectI> _bounds = [];

    public StrokeIndex(TileGrid? grid = null)
    {
        Grid = grid ?? new TileGrid();
    }

    public TileGrid Grid { get; }

    /// <summary>How many strokes are indexed.</summary>
    public int Count => _bounds.Count;

    /// <summary>How many cells hold at least one stroke. For tests and diagnostics.</summary>
    public int CellCount => _cells.Count;

    /// <summary>
    /// Index a stroke at its position in the record.
    /// </summary>
    /// <param name="position">
    /// Where the stroke sits in the frame's stroke list. This is what a query
    /// returns, and what the caller replays in ascending order — so it has to be
    /// the record position and not an arbitrary id, or the render order is lost.
    /// </param>
    /// <param name="bounds">
    /// What the stroke can reach, from <see cref="BrushEngine.ReachBounds"/> —
    /// unclamped, because the index answers "where is this stroke" rather than
    /// "what do I repaint", and a stroke lying entirely outside the document
    /// still has to be findable (B134). Null means the stroke reaches nothing —
    /// an empty stroke — and it is recorded as reaching nothing rather than
    /// skipped, so <see cref="Count"/> still matches the record and a caller
    /// cannot silently lose a stroke.
    /// </param>
    public void Add(int position, SKRectI? bounds)
    {
        if (bounds is not { } rect || rect.Width <= 0 || rect.Height <= 0)
        {
            _bounds[position] = SKRectI.Empty;
            return;
        }

        _bounds[position] = rect;
        foreach (var cell in Grid.Covering(rect.Left, rect.Top, rect.Width, rect.Height))
        {
            if (!_cells.TryGetValue(cell, out var list))
            {
                list = [];
                _cells[cell] = list;
            }
            // Ascending by construction: strokes are added in record order, so
            // appending keeps each cell's list sorted without a comparison.
            list.Add(position);
        }
    }

    /// <summary>
    /// Build an index over a whole stroke list, in record order.
    /// </summary>
    /// <remarks>
    /// No <c>SKImageInfo</c>, deliberately: the index used to be built from the
    /// surface-clamped <see cref="BrushEngine.CommitBounds"/>, which recorded a
    /// stroke wholly outside the document as reaching nothing — unpickable
    /// however close the click (B134). Reach does not depend on the paper.
    /// </remarks>
    public static StrokeIndex Of(IReadOnlyList<Stroke> strokes, TileGrid? grid = null)
    {
        var index = new StrokeIndex(grid);
        for (var i = 0; i < strokes.Count; i++)
        {
            index.Add(i, BrushEngine.ReachBounds(strokes[i]));
        }
        return index;
    }

    /// <summary>
    /// The record positions of every stroke that can reach this region, ascending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ascending is the contract, not a convenience — see the class remarks. The
    /// caller replays these against the record and gets the same pixels the
    /// unindexed path would produce for that region.
    /// </para>
    /// <para>
    /// A stroke spanning several cells appears in each, so the walk deduplicates.
    /// The bounds re-check is not redundant with the cell walk either: a cell is
    /// a square that a stroke's *bounding box* touched, so a stroke can be listed
    /// in a cell whose visible region its box does not actually intersect once
    /// the query rectangle is narrower than the cell.
    /// </para>
    /// </remarks>
    public IEnumerable<int> Intersecting(SKRectI region)
    {
        if (region.Width <= 0 || region.Height <= 0) yield break;

        var seen = new HashSet<int>();
        var hits = new List<int>();
        foreach (var cell in Grid.Covering(region.Left, region.Top, region.Width, region.Height))
        {
            if (!_cells.TryGetValue(cell, out var list)) continue;
            foreach (var position in list)
            {
                if (!seen.Add(position)) continue;
                if (_bounds.TryGetValue(position, out var b) && b.IntersectsWith(region))
                {
                    hits.Add(position);
                }
            }
        }

        hits.Sort();
        foreach (var position in hits) yield return position;
    }

    /// <summary>What a stroke can reach, or empty when it reaches nothing.</summary>
    public SKRectI BoundsOf(int position) =>
        _bounds.TryGetValue(position, out var b) ? b : SKRectI.Empty;
}
