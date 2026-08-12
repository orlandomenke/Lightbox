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
    /// <remarks>
    /// Derived from the machine rather than fixed at 256 MB — see
    /// <see cref="Services.MemoryBudget"/> for why a constant chosen on one
    /// laptop is the wrong default for a tool meant to run in production.
    /// </remarks>
    public static long ByteBudget { get; set; } = Services.MemoryBudget.TileCache();

    private sealed record Entry(TileStore Store, TilePyramid Pyramid, long Stamp);

    /// <summary>
    /// Ever-increasing, and <b>static on purpose</b>: a stamp must be unique
    /// across every cache instance in the process, because a downstream cache
    /// keyed on one outlives any single <see cref="TileFrameCache"/>.
    /// </summary>
    private static long _stamps;

    /// <summary>
    /// A number identifying this frame's tiles <em>as they currently stand</em>,
    /// or 0 when they are not held. Anything derived from the tiles keys on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B167 phase 2 needs a version, and neither obvious candidate works.</b>
    /// Frame id alone is wrong because <see cref="Append"/> stamps a committed
    /// stroke into the tiles <em>in place</em> — the same trap
    /// <see cref="LayerStackBake"/> documents one level up, where an instance
    /// survives its pixels changing. And the <see cref="TilePyramid"/> instance,
    /// which <see cref="Append"/> does replace, is disposed when it is replaced:
    /// keying on it would mean holding a reference to a disposed object to
    /// compare against.
    /// </para>
    /// <para>
    /// <b>A per-frame counter restarting at zero is the version of this that is
    /// subtly wrong</b>, and it is worth writing down because it looks right.
    /// Invalidate a changed frame, let <see cref="Get"/> rebuild it, and a
    /// per-entry counter is back at zero — matching a derived entry cached
    /// before the change, whose pixels are now wrong. A single counter that only
    /// ever goes up cannot collide with anything it has already issued, so a
    /// stale derived entry simply never matches and ages out of its own LRU.
    /// </para>
    /// <para>
    /// <b>An eviction and a rebuild from the same record return a new stamp for
    /// pixels that are identical</b>, which costs one re-flatten and is the safe
    /// direction. Making it cheaper would mean hashing the strokes, which costs
    /// more than the flatten it saves.
    /// </para>
    /// </remarks>
    public long StampOf(string frameId) =>
        _map.TryGetValue(frameId, out var node) ? node.Value.Entry.Stamp : 0;

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

        var (store, pyramid) = RenderDetached(frame, width, height);
        var entry = new Entry(store, pyramid, ++_stamps);

        var fresh = _lru.AddFirst((frame.Id, entry));
        _map[frame.Id] = fresh;
        AllocatedBytes += store.AllocatedBytes;
        Evict();
        return (entry.Store, entry.Pyramid);
    }

    /// <summary>Whether this frame's tiles are already held.</summary>
    public bool Holds(string frameId) => _map.ContainsKey(frameId);

    /// <summary>
    /// Build a frame's tiles without touching the cache, so it can be done on a
    /// background thread and handed back through <see cref="InsertWarm"/>.
    /// </summary>
    /// <param name="level">
    /// A mip level to build while we are off the UI thread. The pyramid is lazy
    /// by design, which means the first zoomed-out publish of a frame pays for
    /// the downscale — on the tick, in front of the artist. Warming the level
    /// the compositor is going to ask for moves that cost to the worker too;
    /// passing 0 leaves the pyramid untouched.
    /// </param>
    /// <remarks>
    /// Safe off the UI thread for the same reason as
    /// <see cref="FrameBitmapCache.RenderDetached"/>: a tile is rebuilt from
    /// the stroke record, and invariant 2 makes that a pure function. The
    /// returned pair is owned by the caller until a cache takes it.
    /// </remarks>
    public static (TileStore Store, TilePyramid Pyramid) RenderDetached(
        Frame frame, int width, int height, int level = 0)
    {
        var store = new TileStore();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        TiledRasterizer.Rasterize(store, StrokesOf(frame), info);
        var pyramid = new TilePyramid(store);
        if (level > 0) pyramid.Level(level);
        return (store, pyramid);
    }

    /// <summary>
    /// Take ownership of tiles built elsewhere, or refuse them. False means the
    /// caller still owns the pair and must dispose it.
    /// </summary>
    /// <remarks>
    /// Never evicts, and enters at the tail — see
    /// <see cref="FrameBitmapCache.InsertWarm"/> for the argument. It is the
    /// same one: a prewarm that displaced a real entry would be spending the
    /// artist's frame to save its own.
    /// </remarks>
    public bool InsertWarm(Frame frame, TileStore store, TilePyramid pyramid)
    {
        if (_map.ContainsKey(frame.Id)) return false;
        if (AllocatedBytes + store.AllocatedBytes > ByteBudget) return false;

        var node = _lru.AddLast((frame.Id, new Entry(store, pyramid, ++_stamps)));
        _map[frame.Id] = node;
        AllocatedBytes += store.AllocatedBytes;
        return true;
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
        // A new stamp, because the tiles under it just changed. Anything derived
        // from them (B167 phase 2's flattened bitmaps) stops matching here.
        var replaced = new Entry(entry.Store, new TilePyramid(entry.Store), ++_stamps);
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
