using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// frameId → sparse tile store (with its mip pyramid), for documents whose
/// canvas is unbounded. The tile-native sibling of <see cref="FrameBitmapCache"/>:
/// where that one materialises a document-sized bitmap per frame, this one
/// holds only the tiles a frame's ink reaches, so memory follows what was
/// drawn rather than how big the paper is — the property "unbounded" cannot
/// exist without, since an unbounded canvas has no size to allocate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owned and used by the UI thread only</b>, like the frame cache beside
/// it, and invalidated through the same funnel: the view model routes every
/// frame mutation through one pair of helpers that clears both caches, so the
/// two can never disagree about which frames changed.
/// </para>
/// <para>
/// <b>Commits stay proportional to the stroke.</b> <see cref="Append"/> stamps
/// the new stroke into the tiles it reaches (invariant 6, tiled); the pyramid
/// is re-wrapped rather than patched, because its levels are lazy — dropping
/// them costs nothing until the next zoomed-out publish asks, and a mip level
/// patched incrementally is a mip level that can drift.
/// </para>
/// <para>
/// <b>What refuses to tile, and what happens then.</b> A frame with baseline
/// pixels or symbol placements renders through <see cref="FrameRasterizer.Materialize"/>
/// (they are document-anchored, not stroke-shaped), and an effect brush reads
/// pixels a single tile does not hold (B59) — <see cref="CanTileFrame"/> says
/// no to all of those, and the caller falls back to the bitmap path. That is
/// a fallback, not a failure: correctness first, sparsity where it is sound.
/// </para>
/// </remarks>
public sealed class TileFrameCache : IDisposable
{
    /// <summary>
    /// Tile bytes held before the least recently used frame is evicted. The
    /// pyramid's lazy levels add at most a third on top (¼ + ¹⁄₁₆ + …), which
    /// the budget deliberately rounds into rather than tracks.
    /// </summary>
    public static long ByteBudget { get; set; } = 256L * 1024 * 1024;

    private sealed record Entry(TileStore Store, TilePyramid Pyramid);

    private readonly Dictionary<string, LinkedListNode<(string Id, Entry Entry)>> _map = [];
    private readonly LinkedList<(string Id, Entry Entry)> _lru = [];

    public int CachedFrames => _lru.Count;

    /// <summary>Tile bytes currently held, across every cached frame.</summary>
    public long AllocatedBytes { get; private set; }

    /// <summary>Whether this frame can be held as tiles at all.</summary>
    /// <remarks>
    /// Was a two-arm switch over <c>VectorFrame</c> and <c>PaintedFrame</c>, and
    /// the arms disagreed only because one class could not hold the things the
    /// other was checked for: the vector arm skipped the baseline and placement
    /// tests since a <c>VectorFrame</c> had nowhere to put either. One frame class
    /// means one condition, and it is the painted arm's — a tile is rebuilt from
    /// strokes, so stored pixels and placed symbols are exactly what it cannot
    /// reproduce. Reading <see cref="Frame.HasBaseline"/> rather than
    /// <c>PngBase64.Length == 0</c> also matters now the field is nullable.
    /// </remarks>
    public static bool CanTileFrame(Frame frame) =>
        !frame.HasBaseline
        && !frame.HasPlacements
        && TiledRasterizer.CanTile(frame.Strokes);

    /// <summary>
    /// The frame's tiles and pyramid, built from its stroke record on a miss.
    /// </summary>
    public (TileStore Store, TilePyramid Pyramid) Get(Frame frame, int width, int height)
    {
        if (_map.TryGetValue(frame.Id, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return (node.Value.Entry.Store, node.Value.Entry.Pyramid);
        }

        var store = new TileStore();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        TiledRasterizer.Rasterize(store, StrokesOf(frame), info);
        var entry = new Entry(store, new TilePyramid(store));

        var fresh = _lru.AddFirst((frame.Id, entry));
        _map[frame.Id] = fresh;
        AllocatedBytes += store.AllocatedBytes;
        Evict();
        return (entry.Store, entry.Pyramid);
    }

    /// <summary>
    /// A stroke was committed to this frame: stamp it into the cached tiles.
    /// A frame that is not cached stays uncached — the next
    /// <see cref="Get"/> rebuilds from the record, stroke included.
    /// </summary>
    public void Append(Frame frame, Stroke stroke, int width, int height)
    {
        if (!_map.TryGetValue(frame.Id, out var node)) return;

        var entry = node.Value.Entry;
        AllocatedBytes -= entry.Store.AllocatedBytes;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (!TiledRasterizer.AppendStroke(entry.Store, stroke, info))
        {
            // An effect brush: the tiles cannot say what it did, so the whole
            // frame leaves the cache and the next publish rebuilds it via the
            // whole-frame fallback.
            Remove(node);
            return;
        }
        AllocatedBytes += entry.Store.AllocatedBytes;

        // The mip levels describe the tiles as they stood; re-wrap rather than
        // patch. Levels are lazy, so this costs nothing until a zoomed-out
        // publish next asks for one.
        entry.Pyramid.Dispose();
        var replaced = new Entry(entry.Store, new TilePyramid(entry.Store));
        node.Value = (frame.Id, replaced);
        Evict();
    }

    public void Invalidate(string frameId)
    {
        if (_map.TryGetValue(frameId, out var node)) Remove(node);
    }

    public void Clear()
    {
        foreach (var (_, entry) in _lru)
        {
            entry.Pyramid.Dispose();
            entry.Store.Dispose();
        }
        _lru.Clear();
        _map.Clear();
        AllocatedBytes = 0;
    }

    public void Dispose() => Clear();

    private static IReadOnlyList<Stroke> StrokesOf(Frame frame) => frame.Strokes;

    private void Evict()
    {
        while (_lru.Count > 1 && AllocatedBytes > ByteBudget && _lru.Last is { } last)
        {
            Remove(last);
        }
    }

    private void Remove(LinkedListNode<(string Id, Entry Entry)> node)
    {
        AllocatedBytes -= node.Value.Entry.Store.AllocatedBytes;
        node.Value.Entry.Pyramid.Dispose();
        node.Value.Entry.Store.Dispose();
        _map.Remove(node.Value.Id);
        _lru.Remove(node);
    }
}
