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
    private const long ByteBudget = 512L * 1024 * 1024;

    /// <summary>Always keep at least this many, so onion skin never thrashes.</summary>
    private const int MinFrames = 6;

    private const int MaxFrames = 96;

    private readonly Dictionary<string, LinkedListNode<(string Id, SKBitmap Bmp)>> _map = [];
    private readonly LinkedList<(string Id, SKBitmap Bmp)> _lru = [];

    /// <summary>Bytes of frame bitmaps currently held.</summary>
    public long CachedBytes { get; private set; }

    public int CachedFrames => _lru.Count;

    public SKBitmap Get(Frame frame, int width, int height)
    {
        if (_map.TryGetValue(frame.Id, out var node))
        {
            if (node.Value.Bmp.Width == width && node.Value.Bmp.Height == height)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Bmp;
            }
            RemoveNode(node);
        }

        var bmp = frame switch
        {
            PaintedFrame p => FrameRasterizer.Materialize(p, width, height),
            VectorFrame v => FrameRasterizer.Rasterize(v.Strokes, width, height),
            _ => throw new InvalidOperationException($"Unknown frame type {frame.GetType().Name}"),
        };
        var newNode = _lru.AddFirst((frame.Id, bmp));
        _map[frame.Id] = newNode;
        CachedBytes += BytesOf(bmp);

        while (_lru.Count > MaxFrames
               || (_lru.Count > MinFrames && CachedBytes > ByteBudget))
        {
            RemoveNode(_lru.Last!);
        }
        return bmp;
    }

    private static long BytesOf(SKBitmap bmp) => bmp.Width * (long)bmp.Height * 4;

    public void Invalidate(string frameId)
    {
        if (_map.TryGetValue(frameId, out var node)) RemoveNode(node);
    }

    public void Clear()
    {
        foreach (var (_, bmp) in _lru) bmp.Dispose();
        _lru.Clear();
        _map.Clear();
        CachedBytes = 0;
    }

    private void RemoveNode(LinkedListNode<(string Id, SKBitmap Bmp)> node)
    {
        _map.Remove(node.Value.Id);
        _lru.Remove(node);
        CachedBytes -= BytesOf(node.Value.Bmp);
        node.Value.Bmp.Dispose();
    }

    public void Dispose() => Clear();
}
