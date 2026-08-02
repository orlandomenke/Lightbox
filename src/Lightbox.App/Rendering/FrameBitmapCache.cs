using System.Globalization;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

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
    public static long ByteBudget { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// Keep at least this many where the budget allows, so onion skin does not
    /// thrash. It is a preference, not a floor — see the eviction loop.
    /// </summary>
    private const int MinFrames = 6;

    private const int MaxFrames = 96;

    private readonly record struct Entry(string Key, string FrameId, SKBitmap Bmp);

    private readonly Dictionary<string, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = [];

    /// <summary>Bytes of frame bitmaps currently held.</summary>
    public long CachedBytes { get; private set; }

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
    private static string KeyOf(Frame frame, int width, int height, double outputScale, int celIndex)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{frame.Id}|{width}x{height}@{outputScale:0.####}");
        return frame is PaintedFrame { HasPlacements: true }
            ? string.Create(CultureInfo.InvariantCulture, $"{key}#{celIndex}")
            : key;
    }

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
        SKBitmap? backdrop = null)
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
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Bmp;
        }

        var bmp = Render(frame, width, height, outputScale, celIndex, backdrop);
        var newNode = _lru.AddFirst(new Entry(key, frame.Id, bmp));
        _map[key] = newNode;
        CachedBytes += BytesOf(bmp);

        Evict();
        return bmp;
    }

    /// <summary>Whether anything on this frame reads the layers beneath it, live.</summary>
    private static bool SamplesLive(Frame frame) =>
        frame is PaintedFrame p
        && p.Strokes.Any(s => s.Brush.SampleSource == SampleSource.AllLayersLive);

    private static SKBitmap Render(
        Frame frame, int width, int height, double outputScale, int celIndex, SKBitmap? backdrop) =>
        frame switch
        {
            PaintedFrame p => FrameRasterizer.Materialize(p, width, height, outputScale, celIndex, backdrop),
            VectorFrame v => FrameRasterizer.Rasterize(v.Strokes, width, height, outputScale),
            _ => throw new InvalidOperationException($"Unknown frame type {frame.GetType().Name}"),
        };

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
        while (_lru.Count > MaxFrames) RemoveNode(_lru.Last!);
        while (_lru.Count > MinFrames && CachedBytes > ByteBudget) RemoveNode(_lru.Last!);
        // Still over after honouring the preference: the document is big
        // enough that the preference itself is what does not fit.
        while (_lru.Count > 1 && CachedBytes > ByteBudget) RemoveNode(_lru.Last!);
    }

    private static long BytesOf(SKBitmap bmp) => bmp.Width * (long)bmp.Height * 4;

    /// <summary>
    /// Drop every render of a frame. A frame can be cached at more than one
    /// size or scale at once, and a stroke invalidates all of them.
    /// </summary>
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
        foreach (var entry in _lru) entry.Bmp.Dispose();
        _lru.Clear();
        _map.Clear();
        CachedBytes = 0;
    }

    private void RemoveNode(LinkedListNode<Entry> node)
    {
        _map.Remove(node.Value.Key);
        _lru.Remove(node);
        CachedBytes -= BytesOf(node.Value.Bmp);
        node.Value.Bmp.Dispose();
    }

    public void Dispose() => Clear();
}
