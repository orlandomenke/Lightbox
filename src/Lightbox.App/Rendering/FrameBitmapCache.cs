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
    private const int Capacity = 96;

    private readonly Dictionary<string, LinkedListNode<(string Id, SKBitmap Bmp)>> _map = [];
    private readonly LinkedList<(string Id, SKBitmap Bmp)> _lru = [];

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

        while (_lru.Count > Capacity)
        {
            RemoveNode(_lru.Last!);
        }
        return bmp;
    }

    public void Invalidate(string frameId)
    {
        if (_map.TryGetValue(frameId, out var node)) RemoveNode(node);
    }

    public void Clear()
    {
        foreach (var (_, bmp) in _lru) bmp.Dispose();
        _lru.Clear();
        _map.Clear();
    }

    private void RemoveNode(LinkedListNode<(string Id, SKBitmap Bmp)> node)
    {
        _map.Remove(node.Value.Id);
        _lru.Remove(node);
        node.Value.Bmp.Dispose();
    }

    public void Dispose() => Clear();
}
