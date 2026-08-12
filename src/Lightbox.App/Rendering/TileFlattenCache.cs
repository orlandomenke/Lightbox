using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Flattened viewport bitmaps, so a layer that holds is composited from its
/// tiles once rather than once per playback frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>B167 phase 2.</b> <c>TileCompositor.CompositeToBitmap</c> runs on every
/// publish for every tile-native pass, and phase 1 measured it at <b>30–35% of
/// Compose</b> on the route playback actually takes. Its inputs are the frame's
/// tiles, the pyramid level and the level-space rectangle — all three identical
/// from one playback frame to the next for any layer that holds. So the work is
/// not merely repeated, it is repeated <em>on exactly the same arguments</em>,
/// which is the definition of something to cache.
/// </para>
/// <para>
/// <b>The expected hit rate is a number already measured rather than a hope.</b>
/// B165 counted layer draws that repeat the previous frame's drawing over a
/// 24-frame range: <b>26% at two layers, 51% at six, 59% at ten</b>. This
/// exploits the same property of animation — holds, and layers that do not move
/// — one level down, so that is the share of flattens that should disappear.
/// </para>
/// <para>
/// <b>Staleness is the whole risk, and the key is what answers it.</b> A frame's
/// tiles are mutated in place by <c>TileFrameCache.Append</c>, so a frame id is
/// not a version — see <see cref="TileFrameCache.StampOf"/>, which exists for
/// this and explains why a per-frame counter restarting at zero is the version
/// of the idea that is subtly wrong. A stamp only ever goes up, so a stale entry
/// cannot match one and ages out of the LRU on its own.
/// </para>
/// <para>
/// <b>Pinning is not optional here, and skipping it would be B130 again.</b>
/// Before this cache existed a flattened bitmap was allocated per publish and
/// disposed with the snapshot — owned by nobody, safe by construction. A cached
/// one is borrowed by every snapshot that got it, and eviction while the render
/// thread is mid-draw frees pixels Skia is about to read: an access violation
/// inside native code, no managed stack, an empty crash log. So this runs the
/// same counted pin protocol as <see cref="FrameBitmapCache"/> — eviction takes
/// the entry out immediately so the budget stays enforceable, and the disposal
/// waits for the last <see cref="Unpin"/>.
/// </para>
/// <para>
/// <b>Owned by the UI thread only</b>, like the two caches beside it. The render
/// thread only ever reads bitmaps it was handed and that are pinned for it.
/// </para>
/// </remarks>
public sealed class TileFlattenCache : IDisposable
{
    /// <summary>
    /// Flattened bytes held before the least recently used is evicted.
    /// </summary>
    /// <remarks>
    /// <b>These are viewport-sized bitmaps, which is exactly the memory problem
    /// B144 built tiles to avoid</b> — an unbounded cache here would reintroduce
    /// it one level up. Derived from the machine
    /// (<see cref="Services.MemoryBudget"/>) and the meanest share of the four,
    /// because a flatten is a convenience: missing costs one composite of tiles
    /// that are already in memory, where a frame-cache miss costs a whole frame
    /// replayed from its stroke record.
    /// </remarks>
    public static long ByteBudget { get; set; } = Services.MemoryBudget.FlattenCache();

    private readonly record struct Key(string FrameId, long Stamp, int Level, SKRectI Viewport);

    private readonly Dictionary<Key, LinkedListNode<(Key Key, SKBitmap Bitmap)>> _map = [];
    private readonly LinkedList<(Key Key, SKBitmap Bitmap)> _lru = [];
    private readonly Dictionary<SKBitmap, int> _pins = [];
    private readonly HashSet<SKBitmap> _awaitingUnpin = [];

    /// <summary>Bytes held by cached flattens.</summary>
    public long CachedBytes { get; private set; }

    /// <summary>
    /// Bytes held by flattens that have left the cache and cannot be freed yet.
    /// </summary>
    /// <remarks>
    /// Reported separately from <see cref="CachedBytes"/> for the reason
    /// <see cref="FrameBitmapCache.AwaitingUnpinBytes"/> gives: they are not
    /// cache contents, so counting them there would make the budget look
    /// breached — but they are real memory and omitting them would understate
    /// what the application holds.
    /// </remarks>
    public long AwaitingUnpinBytes { get; private set; }

    /// <summary>Flattens served from memory, and flattens that had to be composited.</summary>
    public int Hits { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public int Misses { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public int Evictions { get; private set; }

    /// <summary>How many flattens are held. For tests and the report.</summary>
    public int CachedCount => _lru.Count;

    /// <summary>
    /// How many distinct bitmaps are pinned, and how many of those the cache has
    /// already given up on and is waiting to free (B179).
    /// </summary>
    /// <remarks>
    /// A pin count that climbs without bound means published snapshots are not
    /// being released — which is the leak shape this cache can produce, because
    /// a pinned bitmap is one eviction cannot free.
    /// </remarks>
    public int PinnedCount => _pins.Count;

    /// <inheritdoc cref="PinnedCount"/>
    public int AwaitingUnpinCount => _awaitingUnpin.Count;

    /// <summary>
    /// A cached flatten for exactly these tiles, level and rectangle, or null.
    /// </summary>
    /// <remarks>
    /// <paramref name="stamp"/> of 0 means the tiles are not held at all, which
    /// cannot have produced a cached flatten and is answered without a lookup.
    /// </remarks>
    public SKBitmap? Get(string frameId, long stamp, int level, SKRectI viewport)
    {
        if (stamp == 0)
        {
            Misses++;
            return null;
        }

        var key = new Key(frameId, stamp, level, viewport);
        if (!_map.TryGetValue(key, out var node))
        {
            Misses++;
            return null;
        }

        _lru.Remove(node);
        _lru.AddFirst(node);
        Hits++;
        return node.Value.Bitmap;
    }

    /// <summary>
    /// Take ownership of a flatten. False means the cache refused it and the
    /// caller still owns the bitmap.
    /// </summary>
    /// <remarks>
    /// <b>Refusing is a real outcome rather than a failure</b>, and the caller
    /// has to handle it: a viewport larger than the whole budget cannot be
    /// cached at any size of cache, and pretending otherwise would either evict
    /// everything on every publish or blow the budget. A refused flatten is
    /// disposed with the snapshot exactly as every flatten was before this class
    /// existed.
    /// </remarks>
    public bool Insert(string frameId, long stamp, int level, SKRectI viewport, SKBitmap flat)
    {
        ArgumentNullException.ThrowIfNull(flat);
        if (stamp == 0) return false;

        var bytes = BytesOf(flat);
        if (bytes > ByteBudget) return false;

        var key = new Key(frameId, stamp, level, viewport);
        if (_map.ContainsKey(key)) return false;

        var node = _lru.AddFirst((key, flat));
        _map[key] = node;
        CachedBytes += bytes;
        Evict();
        return true;
    }

    /// <inheritdoc cref="FrameBitmapCache.Pin"/>
    public void Pin(SKBitmap bmp)
    {
        _pins[bmp] = _pins.TryGetValue(bmp, out var n) ? n + 1 : 1;
    }

    /// <inheritdoc cref="FrameBitmapCache.Unpin"/>
    public void Unpin(SKBitmap bmp)
    {
        if (!_pins.TryGetValue(bmp, out var n)) return;
        if (n > 1)
        {
            _pins[bmp] = n - 1;
            return;
        }

        _pins.Remove(bmp);
        if (!_awaitingUnpin.Remove(bmp)) return;

        AwaitingUnpinBytes -= BytesOf(bmp);
        bmp.Dispose();
    }

    /// <summary>Drop everything. The tiles under it all changed, or the document did.</summary>
    public void Clear()
    {
        foreach (var (_, bitmap) in _lru) DisposeOrDefer(bitmap);
        _lru.Clear();
        _map.Clear();
        CachedBytes = 0;
    }

    public void Dispose() => Clear();

    private void Evict()
    {
        // Down to one rather than to zero: the entry just inserted is the one
        // about to be drawn, and evicting it would make the cache a pure cost on
        // a viewport big enough that two do not fit.
        while (_lru.Count > 1 && CachedBytes > ByteBudget && _lru.Last is { } last)
        {
            Remove(last);
            Evictions++;
        }
    }

    private void Remove(LinkedListNode<(Key Key, SKBitmap Bitmap)> node)
    {
        CachedBytes -= BytesOf(node.Value.Bitmap);
        _map.Remove(node.Value.Key);
        _lru.Remove(node);
        DisposeOrDefer(node.Value.Bitmap);
    }

    /// <inheritdoc cref="FrameBitmapCache.Pin"/>
    private void DisposeOrDefer(SKBitmap bmp)
    {
        if (_pins.ContainsKey(bmp))
        {
            if (_awaitingUnpin.Add(bmp)) AwaitingUnpinBytes += BytesOf(bmp);
            return;
        }
        bmp.Dispose();
    }

    private static long BytesOf(SKBitmap bmp) => (long)bmp.Width * bmp.Height * 4;
}
