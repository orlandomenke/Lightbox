using Avalonia.Media.Imaging;

namespace Lightbox.App.Rendering;

/// <summary>
/// frameId → the timeline cell's thumbnail. Owned and used by the UI thread only.
/// </summary>
/// <remarks>
/// <para>
/// <b>A thumbnail is a property of a drawing, and it used to be stored on the
/// cell that displayed it (B202).</b> That is the whole bug. <c>SyncLayerRows</c>
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

    /// <summary>What the thumbnails hold, for the memory report: four bytes a pixel.</summary>
    public long Bytes => _thumbs.Values.Sum(b => (long)b.PixelSize.Width * b.PixelSize.Height * 4);

    /// <summary>How many thumbnails have been rendered rather than found (B202's guard).</summary>
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

    /// <summary>Whether a drawing's thumbnail is held.</summary>
    public bool Holds(string frameId) => _thumbs.ContainsKey(frameId);

    /// <summary>Hold a thumbnail made elsewhere — the background worker's. Returns it.</summary>
    public Bitmap Put(string frameId, Bitmap made)
    {
        Renders++;
        if (!_thumbs.ContainsKey(frameId)) _order.Enqueue(frameId);
        _thumbs[frameId] = made;
        while (_order.Count > MaxEntries) _thumbs.Remove(_order.Dequeue());
        return made;
    }

    /// <summary>Drop one drawing's thumbnail — its pixels changed.</summary>
    /// <remarks>
    /// <b>And every picture composed from it (B503).</b> A key such as the
    /// navigator's names several drawings at once, so dropping the key that is
    /// the drawing's id left those standing — and whatever else the key held
    /// to tell a stale one apart, the record can come back to.
    /// </remarks>
    public void Invalidate(string frameId)
    {
        _thumbs.Remove(frameId);
        if (_dependents.Remove(frameId, out var keys))
        {
            foreach (var key in keys) _thumbs.Remove(key);
        }
    }

    /// <summary>
    /// A picture composed from several drawings, rendered only if not already
    /// held, and dropped when any of <paramref name="dependsOn"/> is invalidated.
    /// </summary>
    /// <remarks>
    /// The navigator's picture (B503). Its freshness is then exactly the
    /// timeline thumbnails' — the same <see cref="Invalidate"/> calls serve both —
    /// rather than a proxy in the key that an undo or a clear can repeat.
    /// </remarks>
    public Bitmap Get(string key, Func<Bitmap> render, IEnumerable<string> dependsOn)
    {
        var held = _thumbs.ContainsKey(key);
        var made = Get(key, render);
        if (!held)
        {
            foreach (var frameId in dependsOn)
            {
                if (!_dependents.TryGetValue(frameId, out var keys)) _dependents[frameId] = keys = [];
                keys.Add(key);
            }
        }
        return made;
    }

    /// <summary>
    /// Each drawing's composed pictures, by key. A key evicted by the cap stays
    /// listed until its drawing is next invalidated, which removes nothing.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _dependents = [];

    /// <summary>Drop everything — a document-wide change, or a new document.</summary>
    public void Clear()
    {
        _thumbs.Clear();
        _order.Clear();
        _dependents.Clear();
    }

    internal void ResetTraffic()
    {
        Renders = 0;
        Hits = 0;
    }
}
