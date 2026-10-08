using System.Diagnostics;

namespace Lightbox.Raster;

/// <summary>
/// A cache of pictures that gives memory back on request: the four UI-thread
/// stores the overall limit brokers (docs/DESIGN-memory-for-pictures.md, Q221).
/// </summary>
public interface IPictureStore
{
    /// <summary>What the store holds, in bytes.</summary>
    long Bytes { get; }

    /// <summary>
    /// When the least recently used entry that may go was last used, on
    /// <see cref="PictureMemory.Clock"/> — or null when nothing may: every entry
    /// pinned, in use, or the store at its own minimum.
    /// </summary>
    long? OldestEvictable { get; }

    /// <summary>Drop that entry; the bytes freed, or 0 if nothing could go.</summary>
    long EvictOldest();
}

/// <summary>
/// One memory limit for every picture the application holds (Q221).
/// </summary>
/// <remarks>
/// Each cache was capped on its own and nothing summed the caps — a third of the
/// machine between them. This holds the brokered stores, and the render-thread
/// caches' slices, to one figure, evicting the least recently used picture
/// across all of them first.
/// </remarks>
public static class PictureMemory
{
    /// <summary>The shared clock every store stamps its entries with.</summary>
    public static long Clock() => Stopwatch.GetTimestamp();

    /// <summary>The limit, in bytes. Defaults to an eighth of the machine.</summary>
    public static long Limit { get; set; } = MemoryBudget.Pictures();

    /// <summary>
    /// The render-thread caches' slice each: layer textures and finished frames
    /// cannot be evicted from here, so each keeps to this much instead.
    /// </summary>
    public static long RenderSlice => Limit / 8;

    /// <summary>
    /// What the brokered stores share: the limit less the two render-thread
    /// slices, so that all six together stay within <see cref="Limit"/>.
    /// </summary>
    public static long Brokered => Limit - 2 * RenderSlice;

    private static readonly List<WeakReference<IPictureStore>> Stores = [];

    public static void Register(IPictureStore store) => Stores.Add(new WeakReference<IPictureStore>(store));

    public static void Unregister(IPictureStore store) =>
        Stores.RemoveAll(w => !w.TryGetTarget(out var s) || ReferenceEquals(s, store));

    /// <summary>What the brokered stores hold together.</summary>
    public static long Total => Live().Sum(s => s.Bytes);

    /// <summary>
    /// Evict the least recently used picture across every store until the total
    /// is under <see cref="Brokered"/> (or <paramref name="limit"/>) or nothing
    /// more may go. UI thread, between publishes.
    /// </summary>
    /// <returns>Bytes freed.</returns>
    public static long Enforce(long? limit = null)
    {
        var cap = limit ?? Brokered;
        var stores = Live();
        var total = stores.Sum(s => s.Bytes);
        long freed = 0;
        while (total > cap)
        {
            // The least recently used picture anywhere is some store's oldest,
            // because each store is already least-recently-used within itself.
            IPictureStore? victim = null;
            long oldest = long.MaxValue;
            foreach (var store in stores)
            {
                if (store.OldestEvictable is { } used && used < oldest)
                {
                    oldest = used;
                    victim = store;
                }
            }
            if (victim is null) break; // everything left is pinned, in use, or a floor
            var got = victim.EvictOldest();
            if (got <= 0) break;       // a store that said it could and did not: stop, never spin
            freed += got;
            total -= got;
        }
        return freed;
    }

    private static List<IPictureStore> Live()
    {
        var live = new List<IPictureStore>(Stores.Count);
        Stores.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in Stores)
        {
            if (w.TryGetTarget(out var s)) live.Add(s);
        }
        return live;
    }
}
