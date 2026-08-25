using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Which composite this is, completely enough that two frames sharing a key
/// would blend to the same pixels.
/// </summary>
/// <param name="Frame">
/// Everything about the view and the exposure sheet a composite depends on —
/// see <see cref="FrameFingerprint"/>, which already had to enumerate exactly
/// this for B165 and is reused rather than re-derived.
/// </param>
/// <param name="Epoch">
/// How many times the document's rendered content has been invalidated. The
/// half <see cref="FrameFingerprint"/> does not carry and could not: it answers
/// "would this frame compose to what the *previous* publish composed", which is
/// a question about two adjacent moments, and a cache asks a question about two
/// moments a lap apart. Between them an artist can have drawn.
/// </param>
internal readonly record struct ComposeKey(FrameFingerprint Frame, int Epoch);

/// <summary>
/// Finished composites, kept so that going round a loop a second time is free.
/// </summary>
/// <remarks>
/// <para>
/// <b>B167 phase 7, and it is the phase that pays with the GPU toggle off</b> —
/// which is what nearly every machine runs. B165 already declines to recompose
/// a frame identical to the one on screen, but <c>PublishState.LastPublished</c>
/// is a <em>single slot</em>, so it only catches consecutive repeats: the hold
/// case on 2s. Loop back to frame 0 and every frame composites again although
/// the blend is byte-identical to the one it did a second ago. This makes lap
/// two of every loop essentially free, and it is route-independent — unlike the
/// GPU work, which reaches the culled route while playback takes the tiled one.
/// </para>
/// <para>
/// <b>The lifetime is the hard part, not the LRU</b>, and this repository has
/// already paid for learning that twice. A composed image handed out and then
/// freed underneath its reader is B130: an access violation inside
/// <c>sk_canvas_draw_image_rect</c>, no managed stack, an empty log, and
/// "Lightbox dies as soon as I touch anything". So the protocol here is the one
/// <see cref="FrameBitmapCache"/> already runs for cel bitmaps rather than a
/// second invention: <b>an entry evicted while somebody is reading it leaves the
/// map immediately — the budget has to stay enforceable — and is freed by the
/// last <see cref="Release"/>.</b>
/// </para>
/// <para>
/// <b>Counted, not flagged.</b> One composite can be held by two live snapshots
/// at once — the retirement queue keeps up to <c>RetiredHardCap + 1</c> of them
/// — and a flag would free it on the first release while the second reader was
/// still drawing.
/// </para>
/// <para>
/// <b>Locked, unlike <see cref="FrameBitmapCache"/>, and the difference is which
/// thread owns it.</b> That cache is the view model's and its pin table "is a
/// plain dictionary that assumes exactly this": every pin and unpin happens on
/// the UI thread. This one is filled inside the draw op, on the render thread,
/// and released from <see cref="RenderSnapshot.Dispose"/> on the UI thread — so
/// the two genuinely meet. The lock is uncontended in practice (one lookup a
/// frame, a handful of live holds) and a race here is a native crash, which is
/// not a trade worth making for a dictionary access.
/// </para>
/// <para>
/// <b>A GPU-backed image is never freed here.</b> It goes to
/// <see cref="GpuImageReaper"/>, which frees it inside the draw op with the
/// lease's context current. Releasing a GPU resource off the context's thread
/// parks it on a deferred queue rather than freeing it, which is B179 — one
/// render target leaked per publish, invisible to every counter, until the
/// process reached 12 GB.
/// </para>
/// </remarks>
internal sealed class ComposeCache(long budgetBytes)
{
    private sealed class Entry(SKImage image, long bytes, bool gpuBacked)
    {
        internal readonly SKImage Image = image;
        internal readonly long Bytes = bytes;

        /// <summary>
        /// Where this one has to be freed. Carried by the entry rather than
        /// passed to every release, because the caller that lets go last is not
        /// the caller that composed it and has no business knowing.
        /// </summary>
        internal readonly bool GpuBacked = gpuBacked;

        internal int Holds;

        /// <summary>True once the cache has given this up but a reader still has it.</summary>
        internal bool Orphaned;
    }

    private readonly object _gate = new();

    /// <summary>Most-recently-used last, which is the order <see cref="Trim"/> evicts from.</summary>
    private readonly LinkedList<ComposeKey> _order = new();

    private readonly Dictionary<ComposeKey, LinkedListNode<ComposeKey>> _nodes = [];
    private readonly Dictionary<ComposeKey, Entry> _entries = [];

    /// <summary>Entries the cache has let go of but whose last reader has not.</summary>
    private readonly List<Entry> _orphans = [];

    internal long BudgetBytes { get; } = budgetBytes;

    /// <summary>Bytes of composites the cache is holding.</summary>
    internal long CachedBytes { get; private set; }

    /// <summary>How many composites are resident.</summary>
    internal int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>Lookups that found a composite, and lookups that did not.</summary>
    internal int Hits { get; private set; }

    /// <summary>See <see cref="Hits"/>.</summary>
    internal int Misses { get; private set; }

    /// <summary>Composites dropped to stay inside the budget.</summary>
    internal int Evictions { get; private set; }

    internal void ResetCounters()
    {
        lock (_gate)
        {
            Hits = 0;
            Misses = 0;
            Evictions = 0;
        }
    }

    /// <summary>
    /// The composite for this key, held against disposal until
    /// <see cref="Release"/> — or null when there is not one.
    /// </summary>
    /// <remarks>
    /// The hold is taken here rather than by the caller afterwards, because a
    /// lookup that returned an image and then let the caller take the hold has a
    /// window in it where an eviction can free what was just handed over.
    /// </remarks>
    internal SKImage? Acquire(ComposeKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                Misses++;
                return null;
            }
            Hits++;
            entry.Holds++;
            Touch(key);
            return entry.Image;
        }
    }

    /// <summary>
    /// Take ownership of a freshly composed image and hand back a hold on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The caller stops owning <paramref name="image"/> and must
    /// <see cref="Release"/> it rather than dispose it.</b> Handing ownership in
    /// is what lets the cache outlive the snapshot that produced the pixels,
    /// which is the entire point — a snapshot lives for a few frames and a loop
    /// comes round in seconds.
    /// </para>
    /// <para>
    /// A key already present keeps the existing entry and the new image is
    /// refused — returning null, so the caller disposes it as its own. That
    /// cannot normally happen (one composite per publish per key) and silently
    /// replacing would orphan an entry live readers are holding for no gain.
    /// </para>
    /// </remarks>
    internal bool Store(ComposeKey key, SKImage image, long bytes, bool gpuBacked)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key)) return false;
            var entry = new Entry(image, bytes, gpuBacked) { Holds = 1 };
            _entries[key] = entry;
            _nodes[key] = _order.AddLast(key);
            CachedBytes += bytes;
            Trim();
            return true;
        }
    }

    /// <summary>
    /// Let go of one hold, freeing the image if the cache has already dropped it
    /// and this was the last reader.
    /// </summary>
    /// <param name="image">The image a previous <see cref="Acquire"/> or <see cref="Store"/> returned.</param>
    internal void Release(SKImage image)
    {
        Entry? dying = null;
        lock (_gate)
        {
            var entry = Find(image);
            if (entry is null) return;
            if (--entry.Holds > 0) return;
            if (!entry.Orphaned) return;
            _orphans.Remove(entry);
            dying = entry;
        }
        Free(dying);
    }

    /// <summary>Drop everything the cache holds. Live readers keep theirs.</summary>
    internal void Clear()
    {
        List<Entry> dying = [];
        lock (_gate)
        {
            foreach (var entry in _entries.Values) Retire(entry, dying);
            _entries.Clear();
            _nodes.Clear();
            _order.Clear();
            CachedBytes = 0;
        }
        foreach (var entry in dying) Free(entry);
    }

    private Entry? Find(SKImage image)
    {
        foreach (var entry in _entries.Values)
        {
            if (ReferenceEquals(entry.Image, image)) return entry;
        }
        foreach (var entry in _orphans)
        {
            if (ReferenceEquals(entry.Image, image)) return entry;
        }
        return null;
    }

    private void Touch(ComposeKey key)
    {
        if (!_nodes.TryGetValue(key, out var node)) return;
        _order.Remove(node);
        _nodes[key] = _order.AddLast(key);
    }

    /// <summary>
    /// Evict least-recently-used entries until the budget is met. Never evicts
    /// the entry just stored, which would make a full cache a slow no-op.
    /// </summary>
    private void Trim()
    {
        List<Entry> dying = [];
        while (CachedBytes > BudgetBytes && _order.Count > 1)
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            _nodes.Remove(oldest);
            if (!_entries.Remove(oldest, out var entry)) continue;
            CachedBytes -= entry.Bytes;
            Evictions++;
            Retire(entry, dying);
        }
        // Freed inside the lock here, unlike Release and Clear: Trim only runs
        // from Store, which is already on the render thread with the context
        // current, and an entry reaching here with no holds has no reader by
        // definition.
        foreach (var entry in dying) Free(entry);
    }

    /// <summary>
    /// The cache is done with this entry: free it now, or park it until its last
    /// reader lets go.
    /// </summary>
    private void Retire(Entry entry, List<Entry> dying)
    {
        if (entry.Holds > 0)
        {
            entry.Orphaned = true;
            _orphans.Add(entry);
            return;
        }
        dying.Add(entry);
    }

    private static void Free(Entry? entry)
    {
        if (entry is null) return;
        if (entry.GpuBacked) GpuImageReaper.Enqueue(entry.Image);
        else entry.Image.Dispose();
    }
}
