using Avalonia.Media.Imaging;

namespace Lightbox.App.Rendering;

/// <summary>
/// frameId → the timeline cell's thumbnail. Owned and used by the UI thread only.
/// </summary>
/// <remarks>
/// <para>
/// <b>A thumbnail is a property of a drawing, and it used to be stored on the
/// cell that displayed it (B201).</b> That is the whole bug. <c>SyncLayerRows</c>
/// maps row <i>i</i> to <c>layers.Count - 1 - i</c>, so adding or removing a
/// layer slides every row onto a <em>different</em> layer — and since staleness
/// was decided by comparing the cell's remembered frame id against the frame now
/// under it, every cell in the timeline mismatched and re-rendered. Measured on a
/// 60-frame scene: undoing <i>add layer</i> cost 61 full-resolution frame
/// rasterizations and 1,312 ms, for a document in which not one drawing had
/// changed.
/// </para>
/// <para>
/// Keyed by frame id, none of that arises: rows may be re-pointed at will and the
/// thumbnails follow their drawings. Invalidation stays where it always was — the
/// frame-render funnel — so a thumbnail is dropped exactly when the pixels behind
/// it are, and never because the timeline was rearranged.
/// </para>
/// <para>
/// <b>Eviction does not dispose.</b> A cell may still be bound to a bitmap this
/// cache has forgotten, and disposing one out from under a live binding paints a
/// hole. Abandoning them to the GC is what the per-cell code did on every single
/// refresh, so this is strictly less garbage rather than a new leak — and at
/// 32×18 the cap below is about 4.7 MB, small enough that the cheap answer is the
/// right one.
/// </para>
/// </remarks>
public sealed class ThumbnailCache
{
    /// <summary>
    /// Enough for a long scene several layers deep, because the cost of a miss
    /// is a full-resolution frame render. 2 048 × 32 × 18 × 4 B ≈ 4.7 MB.
    /// </summary>
    private const int MaxEntries = 2048;

    private readonly Dictionary<string, Bitmap> _thumbs = [];

    /// <summary>
    /// Insertion order, so eviction can drop the oldest without a sort. An id
    /// invalidated and re-rendered appears twice, so eviction can retire a live
    /// entry early — which costs one miss and never a wrong picture, and at this
    /// cap it effectively does not happen.
    /// </summary>
    private readonly Queue<string> _order = new();

    public int Count => _thumbs.Count;

    /// <summary>How many thumbnails have been rendered rather than found (B201's guard).</summary>
    public int Renders { get; private set; }

    public int Hits { get; private set; }

    /// <summary>
    /// The thumbnail for a drawing, rendering it only if it is not already held.
    /// </summary>
    public Bitmap Get(string frameId, Func<Bitmap> render)
    {
        if (_thumbs.TryGetValue(frameId, out var found))
        {
            Hits++;
            return found;
        }

        var made = render();
        Renders++;
        _thumbs[frameId] = made;
        _order.Enqueue(frameId);
        while (_order.Count > MaxEntries) _thumbs.Remove(_order.Dequeue());
        return made;
    }

    /// <summary>Drop one drawing's thumbnail — its pixels changed.</summary>
    public void Invalidate(string frameId) => _thumbs.Remove(frameId);

    /// <summary>Drop everything — a document-wide change, or a new document.</summary>
    public void Clear()
    {
        _thumbs.Clear();
        _order.Clear();
    }

    internal void ResetTraffic()
    {
        Renders = 0;
        Hits = 0;
    }
}
