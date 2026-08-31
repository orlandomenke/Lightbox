using System.Globalization;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// frameId → materialized SKBitmap, LRU-evicted. Owned and used by the UI
/// thread only. Invalidate a frame after mutating it (e.g. committing a
/// stroke); invalidate everything after undo/redo or document load.
/// </summary>
public sealed class FrameBitmapCache : IDisposable
{
    /// <summary>
    /// Frames are held by total bytes, not by count: 96 cached frames is
    /// nothing at 960×540 (100 MB) and 3 GB at 4K. Small documents therefore
    /// keep a deep cache while large ones stay within a sane footprint.
    /// </summary>
    public static long ByteBudget { get; set; } = MemoryBudget.FrameCache();

    /// <summary>
    /// Keep at least this many where the budget allows, so onion skin does not
    /// thrash. It is a preference, not a floor — see the eviction loop.
    /// </summary>
    private const int MinFrames = 6;

    private const int MaxFrames = 96;

    /// <summary>
    /// Which end of the queue eviction takes from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An LRU against a sequential scan has a zero hit rate</b>, and playing,
    /// scrubbing and exporting are all sequential scans. Walking a sheet longer
    /// than the cache evicts the frames at the start to make room for the ones
    /// at the end, so when the playhead comes round again everything it is
    /// about to ask for has just been thrown away. Not degraded — zero. The
    /// cache stops being a cache and every frame is re-rasterised from strokes
    /// (B28).
    /// </para>
    /// <para>
    /// Evicting the <em>most</em> recent instead is the textbook answer, and it
    /// works because it stops the scan destroying itself: the frames that
    /// arrived first stay put and the tail of the sheet fights over what is
    /// left. A 96-frame scene against a 48-frame budget goes from every frame
    /// missing to about half of them hitting. Half a cache beats none.
    /// </para>
    /// <para>
    /// It is the wrong policy for drawing, where the frames an artist keeps
    /// returning to are the ones they touched last — so it is a mode the
    /// caller turns on for the duration of a scan, not a replacement.
    /// </para>
    /// </remarks>
    public enum EvictionOrder
    {
        /// <summary>Least recently used. Right for drawing, where recency predicts reuse.</summary>
        LeastRecent,

        /// <summary>Most recently used. Right for a scan, where it predicts the opposite.</summary>
        MostRecent,
    }

    /// <summary>
    /// How to evict. Set <see cref="EvictionOrder.MostRecent"/> while playing,
    /// scrubbing or exporting, and back afterwards.
    /// </summary>
    public EvictionOrder Eviction { get; set; } = EvictionOrder.LeastRecent;

    /// <param name="Width">Document width this render was made at — B327 needs it
    /// to replay a stroke into the same coordinate space the entry was built in.</param>
    /// <param name="Height">Document height, likewise.</param>
    /// <param name="OutputScale">Surface pixels per document unit, likewise.</param>
    private readonly record struct Entry(
        string Key, string FrameId, SKBitmap Bmp, int Width, int Height, double OutputScale);

    private readonly Dictionary<string, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = [];

    /// <summary>Bytes of frame bitmaps currently held.</summary>
    public long CachedBytes { get; private set; }

    // ---- pinning: deferred disposal for bitmaps that have left the cache ----

    /// <summary>
    /// Bitmaps somebody is still reading, and the ones already evicted whose
    /// disposal is waiting on that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate on B125 stage 2.</b> Compositing is about to stop happening
    /// on the UI thread: the view model will publish a <em>pass list</em> and the
    /// canvas will composite from it on the render thread. Every pass bitmap
    /// comes out of this cache, and <see cref="RemoveNode"/> disposes on
    /// eviction — so a cache eviction between publish and render would free
    /// pixels Skia is about to read.
    /// </para>
    /// <para>
    /// <b>That failure has already happened once here and it is worth naming.</b>
    /// B130 was exactly this shape one level up: a snapshot disposed while the
    /// compositor still held it, an access violation inside
    /// <c>sk_canvas_draw_image_rect</c>, and because it is a <em>native</em>
    /// crash the reporter's three managed channels never saw it — the log was
    /// empty and it presented as "Lightbox dies as soon as I touch anything".
    /// </para>
    /// <para>
    /// So a pinned bitmap is never disposed while it is pinned. Eviction still
    /// removes it from the cache immediately — the budget must still be
    /// enforceable — and the disposal is deferred to the last
    /// <see cref="Unpin"/>. This is the same protocol
    /// <c>CanvasControl._retired</c> already runs for the composed image, which
    /// is why it is the one to reuse rather than inventing a second.
    /// </para>
    /// <para>
    /// Counted rather than a flag: one bitmap can be in two published pass lists
    /// at once — the same cel exposed on two layers, or a publish overlapping
    /// the one before it — and a flag would free it on the first release while
    /// the second reader was still going.
    /// </para>
    /// </remarks>
    private readonly Dictionary<SKBitmap, int> _pins = [];

    private readonly HashSet<SKBitmap> _awaitingUnpin = [];

    /// <summary>
    /// Bytes held by bitmaps that have left the cache and cannot be freed yet.
    /// </summary>
    /// <remarks>
    /// Reported rather than hidden inside <see cref="CachedBytes"/>: they are
    /// not cache contents and counting them there would make the budget look
    /// breached, but they are real memory and a report that omitted them would
    /// understate what the application is holding.
    /// </remarks>
    public long AwaitingUnpinBytes { get; private set; }

    /// <summary>How many distinct bitmaps are pinned. For tests and the report.</summary>
    public int PinnedCount => _pins.Count;

    /// <summary>
    /// Hold a bitmap against disposal. Every call needs a matching
    /// <see cref="Unpin"/>; see the remarks on <see cref="_pins"/>.
    /// </summary>
    public void Pin(SKBitmap bmp)
    {
        _pins[bmp] = _pins.TryGetValue(bmp, out var n) ? n + 1 : 1;
    }

    /// <summary>
    /// Release one hold, disposing the bitmap if the cache has already let go of
    /// it and this was the last reader.
    /// </summary>
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

        // The cache gave this up while it was pinned; nobody is reading it now.
        AwaitingUnpinBytes -= BytesOf(bmp);
        bmp.Dispose();
    }

    /// <summary>
    /// Dispose now, or hand to <see cref="_awaitingUnpin"/> if somebody is
    /// reading it. The single place a cached bitmap is freed.
    /// </summary>
    private void DisposeOrDefer(SKBitmap bmp)
    {
        if (_pins.ContainsKey(bmp))
        {
            if (_awaitingUnpin.Add(bmp)) AwaitingUnpinBytes += BytesOf(bmp);
            return;
        }
        bmp.Dispose();
    }

    /// <summary>
    /// Free a bitmap the cache never owned through the same pin-aware
    /// deferral its own contents get.
    /// </summary>
    /// <remarks>
    /// For owners whose bitmaps ride in published pass lists —
    /// <see cref="LayerStackBake"/>'s baked segments. Every pass bitmap is
    /// pinned here at publish (owned or not; an unowned pin is just a
    /// dictionary entry the matching unpin removes), so this is the one place
    /// that already knows whether the render thread might still read it.
    /// Disposing directly instead is B130's crash with a different owner: the
    /// UI thread freeing pixels a deferred compose holds.
    /// </remarks>
    public void DisposeExternal(SKBitmap bmp) => DisposeOrDefer(bmp);

    /// <summary>
    /// Lookups served from memory, lookups that had to render, and entries
    /// thrown out — for the render report.
    /// </summary>
    /// <remarks>
    /// <b>A miss here is a full frame replayed from its stroke record</b>, which
    /// is the most expensive thing that can happen on a path that runs per
    /// frame. B152 turned on a claim about exactly this ratio and could not
    /// prove it, because the machine that showed the symptom is not the machine
    /// the suite runs on. These three numbers are how the next report answers it
    /// instead of narrowing it.
    /// </remarks>
    public long Hits { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public long Misses { get; private set; }

    private readonly List<(string FrameId, int Width, int Height, double Scale, int Cel, string Why)>
        _recentMisses = [];

    /// <summary>
    /// The last few lookups that had to render, and why (B332).
    /// </summary>
    /// <remarks>
    /// A miss costs a full frame rasterization on the calling thread — 797 ms
    /// measured, and 100% of a 4.9 second stall in the capture that confirmed
    /// it. The count alone cannot say whether the cause is an edit dropping the
    /// entry, a size or scale nobody asked for before, or a brush that samples
    /// the layers beneath and is deliberately never cached. Those are three
    /// different fixes.
    /// </remarks>
    public IReadOnlyList<(string FrameId, int Width, int Height, double Scale, int Cel, string Why)>
        RecentMisses => _recentMisses;

    /// <inheritdoc cref="Hits"/>
    public long Evictions { get; private set; }

    /// <summary>Forget the counters without touching the pixels.</summary>
    public void ResetCounters()
    {
        Hits = 0;
        Misses = 0;
        Evictions = 0;
    }

    public int CachedFrames => _lru.Count;

    /// <summary>
    /// A cached frame is identified by what was rendered AND how, not by the
    /// frame alone. Keying on the id by itself meant an editing render at 1x
    /// and an export render at 2x evicted each other on every access — each
    /// one re-materializing the frame the other had just built.
    /// </summary>
    /// <remarks>
    /// The cel index joins the key only when the frame places a symbol. A
    /// placement is the one thing that renders differently depending on where
    /// the cel sits — a placed cycle advances with the sequence — and every
    /// other frame in the application is the same picture at every index.
    /// Keying on it unconditionally would give a held drawing one cached
    /// bitmap per exposure, which is the cache doing the opposite of its job.
    /// </remarks>
    private string KeyOf(Frame frame, int width, int height, double outputScale, int celIndex)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{frame.Id}|{width}x{height}@{outputScale:0.####}");
        // A placed symbol and a rig-moved drawing share the same property: the
        // frame's pixels depend on where the playhead is, so the timeline
        // position joins the key. Everything else keys by id alone.
        //
        // Asked of the RIG INDEX rather than of the frame, because a drawing
        // on a rigged LAYER carries no weights of its own (Q90) — it would key
        // by id alone and every frame of the walk cycle would come back as
        // whichever pose rendered first.
        return frame.HasPlacements || Rig.IsPosed(frame)
            ? string.Create(CultureInfo.InvariantCulture, $"{key}#{celIndex}")
            : key;
    }

    /// <summary>
    /// Which drawings the rig moves. Replaced by the owner when the document
    /// changes; <see cref="RigIndex.Empty"/> means "ask the frame", which is
    /// what every document that never rigged gets.
    /// </summary>
    /// <remarks>
    /// <b>Set this before the resolver, and replace both together.</b> They
    /// answer the same question — the index decides whether a frame's key
    /// carries the playhead, the resolver decides what it renders as — and a
    /// pair that disagree gives one pose a key that says "any position".
    /// </remarks>
    public RigIndex Rig { get; set; } = RigIndex.Empty;

    /// <summary>
    /// Turns a frame into what a render at a timeline position should see —
    /// the live rig's one hook. Set by the view model to
    /// <c>Skinning.PoseFrameForRender</c>; null renders every frame as
    /// recorded, which is also what any frame without bound strokes gets.
    /// </summary>
    /// <remarks>
    /// Consulted only on a miss, so a cache hit costs a rigged document
    /// nothing extra — and the resolver's output is rendered, never stored or
    /// keyed, so the transient posed frame can never leak into the record.
    /// Pose and weight edits must invalidate the affected frames; the key
    /// cannot see them.
    /// </remarks>
    public Func<Frame, int, Frame>? PoseResolver { get; set; }

    /// <summary>
    /// This frame's raster checkpoint, if it still describes the drawing — the
    /// one hook that lets a cache miss start from stored pixels instead of
    /// replaying ten thousand strokes (B30). Set by the view model to
    /// <c>FrameCheckpoints.Usable</c> against the open document; null replays
    /// the record, which is what every other caller of the rasterizer does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A delegate rather than a document reference</b>, for the reason
    /// <see cref="PoseResolver"/> is one: this cache holds pixels and knows
    /// nothing about which document they came from, and giving it one would make
    /// every test that renders a frame construct a document first. The view model
    /// owns the document; the cache asks a question.
    /// </para>
    /// <para>
    /// <b>Consulted only on a miss</b>, so a hit costs nothing, and the answer is
    /// rendered rather than stored — the entry that comes back is an ordinary
    /// cached bitmap that no longer refers to the checkpoint at all.
    /// </para>
    /// </remarks>
    public Func<Frame, StrokeCheckpoint?>? CheckpointResolver { get; set; }

    /// <param name="celIndex">
    /// Where on the timeline this cel is being shown. Only matters to a frame
    /// that places a symbol; see <see cref="KeyOf"/>.
    /// </param>
    /// <param name="backdrop">
    /// What is beneath this layer, for strokes that sample all of them. A frame
    /// holding a <see cref="SampleSource.AllLayersLive"/> stroke is rendered
    /// and <b>not stored</b>: what is underneath may have changed since, and
    /// there is no key that can say so without the cache learning the whole
    /// layer stack. That re-render is the price Q6 named for choosing Live, and
    /// it is the reason Baked exists beside it.
    /// </param>
    public SKBitmap Get(
        Frame frame, int width, int height, double outputScale = 1.0, int celIndex = 0,
        SKBitmap? backdrop = null,
        // **B332: who asked.** A miss is a whole-frame render on the calling
        // thread — 797 ms measured — and the capture that proved it could say
        // the size and the frame but not the caller. Two rounds of grepping for
        // "who asks at 960x540" is what this one defaulted parameter replaces,
        // and the compiler fills it in at every call site for free.
        [System.Runtime.CompilerServices.CallerMemberName] string asker = "")
    {
        // Dropped rather than returned uncached: every caller treats what comes
        // back as pixels the cache owns and none of them dispose it, so handing
        // out an unowned bitmap here would leak one per repaint. Invalidating
        // first guarantees the miss, and the entry is then owned and evicted
        // like any other.
        if (SamplesLive(frame)) Invalidate(frame.Id);
        var key = KeyOf(frame, width, height, outputScale, celIndex);
        if (_map.TryGetValue(key, out var node))
        {
            Hits++;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Bmp;
        }

        Misses++;
        // **B332: a miss is 797 ms of whole-frame render on this thread, so the
        // report has to be able to say WHICH lookup paid it.** The counters said
        // five misses in a session and nothing about what they were for; the
        // first guess (a committed stroke dropping the frame) turned out to be
        // wrong, and there was no way to tell from the file. Kept as a short
        // ring rather than a log: the last few are what a capture needs and an
        // unbounded list during a stroke is the shape of cost this whole entry
        // is about.
        if (_recentMisses.Count >= 8) _recentMisses.RemoveAt(0);
        _recentMisses.Add((
            frame.Id,
            width,
            height,
            outputScale,
            celIndex,
            SamplesLive(frame)
                ? $"asked by {asker} — samples all layers live, never cached by design"
                : $"asked by {asker}"));
        var source = PoseResolver?.Invoke(frame, celIndex) ?? frame;
        // Asked of the frame as recorded, never of a posed copy: a pose is a
        // transient render of the drawing and the checkpoint answers to what is
        // in the document. `FrameCheckpoints.CanCheckpoint` refuses bound frames
        // outright, so in practice a posed frame has no checkpoint to find — this
        // keeps that true by construction rather than by coincidence.
        var checkpoint = ReferenceEquals(source, frame) ? CheckpointResolver?.Invoke(frame) : null;
        var bmp = Render(source, width, height, outputScale, celIndex, backdrop, checkpoint);
        var newNode = _lru.AddFirst(new Entry(key, frame.Id, bmp, width, height, outputScale));
        _map[key] = newNode;
        CachedBytes += BytesOf(bmp);

        Evict();
        return bmp;
    }

    /// <summary>Whether anything on this frame reads the layers beneath it, live.</summary>
    private static bool SamplesLive(Frame frame) =>
        frame is Frame p
        && p.Strokes.Any(s => s.Brush.SampleSource == SampleSource.AllLayersLive);

    /// <summary>
    /// Whether a render of this frame at this exact size and scale is already
    /// held — the question a prewarmer asks before spending a thread on it.
    /// </summary>
    public bool Holds(Frame frame, int width, int height, double outputScale, int celIndex) =>
        _map.ContainsKey(KeyOf(frame, width, height, outputScale, celIndex));

    /// <summary>
    /// Whether this frame can be stored at all. False for a frame that samples
    /// the layers beneath it live: <see cref="Get"/> deliberately drops those,
    /// so rendering one ahead of time would be work thrown away twice.
    /// </summary>
    public static bool CanCache(Frame frame) => !SamplesLive(frame);

    /// <summary>
    /// Rasterize a frame without touching the cache, so it can be done on a
    /// background thread and handed back through <see cref="InsertWarm"/>.
    /// </summary>
    /// <remarks>
    /// <b>Safe off the UI thread because rendering is a pure function of the
    /// stroke record</b> — invariant 2 forbids an RNG anywhere in it, so the
    /// same strokes produce the same pixels on any thread, in any order, at any
    /// time. That is not a happy accident of this cache; it is the property the
    /// whole document model is built on, and it is what makes prewarming a
    /// scheduling change rather than a rendering one. The registries a stroke
    /// resolves against (tips, textures, clip regions) are
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>,
    /// so reads race with nothing.
    /// </remarks>
    public static SKBitmap RenderDetached(
        Frame frame, int width, int height, double outputScale = 1.0, int celIndex = 0) =>
        Render(frame, width, height, outputScale, celIndex, backdrop: null);

    /// <summary>
    /// Take ownership of a bitmap rendered elsewhere, or refuse it. False means
    /// the caller still owns it and must dispose it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two rules, and both exist because speculative work must never cost
    /// real work.</b>
    /// </para>
    /// <para>
    /// <b>It never evicts.</b> An ordinary <see cref="Get"/> miss may throw
    /// something out to make room, because the caller needs the frame it asked
    /// for. A prewarm needs nothing — so if it does not fit, it is dropped. The
    /// alternative is worse than useless during playback: eviction is
    /// <see cref="EvictionOrder.MostRecent"/> there, whose victim is the
    /// neighbour of the newest entry, so a warm that evicted would throw out the
    /// frame currently on screen to make room for one that is not.
    /// </para>
    /// <para>
    /// <b>It enters at the tail, not the head.</b> The head is where
    /// <see cref="Get"/> puts what was just used, and under
    /// <see cref="EvictionOrder.MostRecent"/> the victim is chosen from there.
    /// A warm at the head would displace the playhead's own frame into the line
    /// of fire on the very next miss.
    /// </para>
    /// </remarks>
    public bool InsertWarm(
        Frame frame, int width, int height, double outputScale, int celIndex, SKBitmap bmp)
    {
        if (!CanCache(frame)) return false;
        var key = KeyOf(frame, width, height, outputScale, celIndex);
        if (_map.ContainsKey(key)) return false;

        var bytes = BytesOf(bmp);
        if (_lru.Count >= MaxFrames || CachedBytes + bytes > ByteBudget) return false;

        var node = _lru.AddLast(new Entry(key, frame.Id, bmp, width, height, outputScale));
        _map[key] = node;
        CachedBytes += bytes;
        return true;
    }

    private static SKBitmap Render(
        Frame frame, int width, int height, double outputScale, int celIndex, SKBitmap? backdrop,
        StrokeCheckpoint? checkpoint = null) =>
        // Baseline-then-strokes, in one call. The vector arm used to skip
        // straight to `Rasterize` because a `VectorFrame` had no baseline to
        // consider; `Materialize` treats an absent one as nothing to draw.
        FrameRasterizer.Materialize(
            frame, width, height, outputScale, celIndex, backdrop, checkpoint);

    /// <summary>
    /// The byte budget wins. It used to be gated behind the frame floor, so at
    /// a scene wide enough for a camera pan — 12000x2160 is ~104 MB a frame —
    /// six frames came to 622 MB against a 512 MB budget with no way down, and
    /// the budget silently meant nothing. Holding fewer frames than onion skin
    /// would like is a slower redraw; ignoring the budget is an out-of-memory.
    /// The preference still applies wherever the budget can afford it.
    /// </summary>
    private void Evict()
    {
        // Under MostRecent this is the node just inserted's neighbour rather
        // than the node itself: evicting what was only this moment put in
        // would make the cache a no-op on the very frame being shown.
        LinkedListNode<Entry>? Victim() =>
            Eviction == EvictionOrder.LeastRecent ? _lru.Last : _lru.First?.Next ?? _lru.First;

        while (_lru.Count > MaxFrames && Victim() is { } a) RemoveNode(a);
        while (_lru.Count > MinFrames && CachedBytes > ByteBudget && Victim() is { } b) RemoveNode(b);
        // Still over after honouring the preference: the document is big
        // enough that the preference itself is what does not fit.
        while (_lru.Count > 1 && CachedBytes > ByteBudget && Victim() is { } c) RemoveNode(c);
    }

    private static long BytesOf(SKBitmap bmp) => bmp.Width * (long)bmp.Height * 4;

    /// <summary>
    /// Drop every render of a frame. A frame can be cached at more than one
    /// size or scale at once, and a stroke invalidates all of them.
    /// </summary>
    /// <summary>Whether any render of this frame is currently held.</summary>
    /// <remarks>
    /// Exists for B102's regression test, which has to assert that a document
    /// the artist is *not* looking at was invalidated. Reading pixels instead
    /// would also pass on a build that repainted everything unconditionally,
    /// which is the cost the targeted invalidation exists to avoid.
    /// </remarks>
    public bool Holds(string frameId)
    {
        for (var node = _lru.First; node is not null; node = node.Next)
        {
            if (node.Value.FrameId == frameId) return true;
        }
        return false;
    }

    /// <summary>
    /// Rebuild one rectangle of every held render of <paramref name="frame"/>,
    /// instead of throwing them away. Returns whether that was possible; false
    /// means the caller should <see cref="Invalidate(string)"/> as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B327.</b> Undo dropped the frame's bitmap, so the next publish stamped
    /// every stroke on it again — 3 092 ms per Ctrl+Z at 1 600 strokes. This
    /// patches the reverted mark's footprint instead, which is what committing
    /// the mark cost in the first place.
    /// </para>
    /// <para>
    /// <b>A posed frame is refused here rather than inside the rasterizer,</b>
    /// because the reason is the cache's own: <see cref="PoseResolver"/> means
    /// the pixels held under this id were rendered from a <em>substitute</em>
    /// frame, so replaying this one's strokes into them would mix two drawings.
    /// </para>
    /// <para>
    /// <b>An entry that cannot be patched is dropped, never left half-done.</b>
    /// <c>FrameRasterizer.RepaintRegion</c> makes every refusal before it paints
    /// anything, so a false there means that bitmap is untouched and removing it
    /// is exactly today's behaviour for it.
    /// </para>
    /// </remarks>
    /// <param name="region">The rectangle to rebuild, in document coordinates.</param>
    public bool RepaintRegion(Frame frame, SKRectI region)
    {
        if (Rig.IsPosed(frame)) return false;
        if (!FrameRasterizer.CanRepaintRegion(frame)) return false;

        var patched = false;
        var node = _lru.First;
        while (node is not null)
        {
            var next = node.Next;
            var entry = node.Value;
            if (entry.FrameId == frame.Id)
            {
                var target = Clamped(region, entry.OutputScale, entry.Bmp.Width, entry.Bmp.Height);
                if (FrameRasterizer.RepaintRegion(
                        entry.Bmp, frame, target, entry.Width, entry.Height, entry.OutputScale))
                {
                    patched = true;
                }
                else
                {
                    RemoveNode(node);
                }
            }
            node = next;
        }
        return patched;
    }

    /// <summary>
    /// Every cached rendering of one drawing, in the order the LRU holds them.
    /// </summary>
    /// <remarks>
    /// <b>The same set <see cref="RepaintRegion"/> loops, exposed so that
    /// <see cref="MarkSnapshot"/> saves and restores exactly what the replay
    /// would have rebuilt.</b> There can be more than one — an editing render at
    /// 1x and an export at 2x are separate entries under the same frame id — and
    /// a snapshot that covered only the first would leave the two disagreeing
    /// about a drawing the record describes once.
    /// </remarks>
    internal List<(string Key, SKBitmap Bmp, double OutputScale)> EntriesFor(string frameId)
    {
        var found = new List<(string, SKBitmap, double)>();
        for (var node = _lru.First; node is not null; node = node.Next)
        {
            var entry = node.Value;
            if (entry.FrameId == frameId) found.Add((entry.Key, entry.Bmp, entry.OutputScale));
        }
        return found;
    }

    /// <summary>
    /// <inheritdoc cref="Clamped" path="/summary"/>
    /// </summary>
    /// <remarks>
    /// <b>Shared with <see cref="MarkSnapshot"/> on purpose.</b> The rectangle a
    /// snapshot saves and the rectangle an undo writes back have to be the same
    /// one, and the cheapest way to guarantee that is for both to be this
    /// function rather than two readings of the same rule.
    /// </remarks>
    internal static SKRectI ClampedRegion(SKRectI region, double outputScale, int width, int height)
        => Clamped(region, outputScale, width, height);

    /// <summary>A document-space rectangle as surface pixels inside the bitmap.</summary>
    /// <remarks>
    /// Outward to whole pixels before clamping — a mark that covers part of a
    /// pixel dirties all of it, and rounding inward would leave a hairline of the
    /// old drawing along the edge of the repaint.
    /// </remarks>
    private static SKRectI Clamped(SKRectI region, double outputScale, int width, int height)
    {
        var scaled = outputScale == 1.0
            ? region
            : new SKRectI(
                (int)Math.Floor(region.Left * outputScale),
                (int)Math.Floor(region.Top * outputScale),
                (int)Math.Ceiling(region.Right * outputScale),
                (int)Math.Ceiling(region.Bottom * outputScale));

        var left = Math.Clamp(scaled.Left, 0, width);
        var top = Math.Clamp(scaled.Top, 0, height);
        var right = Math.Clamp(scaled.Right, 0, width);
        var bottom = Math.Clamp(scaled.Bottom, 0, height);
        return new SKRectI(left, top, right, bottom);
    }

    public void Invalidate(string frameId)
    {
        var node = _lru.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.FrameId == frameId) RemoveNode(node);
            node = next;
        }
    }

    public void Clear()
    {
        // Deferring here too, not just on eviction: closing a document while a
        // published pass list is still in flight is the same use-after-free with
        // a different trigger, and the more likely one — a document close tears
        // the cache down in one go.
        foreach (var entry in _lru) DisposeOrDefer(entry.Bmp);
        _lru.Clear();
        _map.Clear();
        CachedBytes = 0;
    }

    private void RemoveNode(LinkedListNode<Entry> node)
    {
        Evictions++;
        _map.Remove(node.Value.Key);
        _lru.Remove(node);
        CachedBytes -= BytesOf(node.Value.Bmp);
        DisposeOrDefer(node.Value.Bmp);
    }

    public void Dispose() => Clear();
}
