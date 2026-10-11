using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Everything a paused whole-page composite reads: the passes in order, the
/// paper, the scale, the window's corner, the surface, and how many times the
/// document's drawings have been edited.
/// </summary>
/// <remarks>
/// <para>
/// <b>The passes are compared as the compositor will read them.</b>
/// <see cref="RenderPass"/> is a record, so two passes are equal when their
/// bitmap is the same object and their tint, opacity, blend, matrix and source
/// are the same values — a hidden layer is a missing pass, a moved one is a
/// reordered list, an opacity is a number. Nothing has to be remembered to add
/// here when compositing gains an input, which is the failure B165's
/// fingerprint warns about in its own words.
/// </para>
/// <para>
/// <b>A bitmap is compared by reference, and the edit history is why that is
/// safe.</b> A committed stroke is appended into its drawing's cached bitmap in
/// place, and an undo can patch one (B327), so the same object can hold new
/// pixels — and the append does not pass through <c>InvalidateFrameRender</c>,
/// so the render epoch alone missed it: the first version of this cache showed
/// a frame without the stroke just drawn on it. Every committed change to a
/// drawing is a step in the document's history, and the history's revision
/// names the document's state exactly — undoing back to a revision is the state
/// that revision showed — so it is in the key, beside the epoch. A bitmap the
/// still cache evicts is disposed, never pooled, and a stack bake allocates a
/// new one each time, so a reference cannot come back holding another drawing.
/// </para>
/// </remarks>
internal sealed class RevisitKey(
    RenderPass[] passes, SKColor background, double scale, SKPointI origin, SKSizeI surface, int epoch,
    long history)
    : IEquatable<RevisitKey>
{
    private readonly RenderPass[] _passes = passes;

    internal int Epoch { get; } = epoch;

    public bool Equals(RevisitKey? other) =>
        other is not null
        && Epoch == other.Epoch
        && history == other.History
        && background == other.Background
        && scale == other.Scale
        && origin == other.Origin
        && surface == other.Surface
        && _passes.AsSpan().SequenceEqual(other._passes);

    private long History => history;
    private SKColor Background => background;
    private double Scale => scale;
    private SKPointI Origin => origin;
    private SKSizeI Surface => surface;

    public override bool Equals(object? obj) => Equals(obj as RevisitKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Epoch);
        hash.Add(history);
        hash.Add(background);
        hash.Add(scale);
        hash.Add(origin);
        hash.Add(surface);
        hash.Add(_passes.Length);
        foreach (var pass in _passes) hash.Add(pass.Bitmap);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Whether these passes can be keyed at all: plain layers only. A pass with
    /// a mask, an effect, a style, a live overlay or a warp preview carries an
    /// object that can change behind its reference — an adjustment's
    /// <c>EffectStack</c> is edited in place — so it is composed every time,
    /// as before. Conservative on purpose: a wrong miss costs one composite, a
    /// wrong hit shows stale art.
    /// </summary>
    internal static bool CanKey(IReadOnlyList<RenderPass> passes)
    {
        foreach (var p in passes)
        {
            if (p.Bitmap is null || p.Overlay is not null || p.Shapes is not null || p.Effect is not null
                || p.AdjustStack is not null || p.Style is not null || p.Mesh is not null)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Finished whole-page composites of paused frames, so flipping back to one
/// already seen draws one image instead of blending every layer again.
/// </summary>
/// <remarks>
/// <para>
/// UI-thread only, unlike <see cref="ComposeCache"/>: it is filled and read
/// inside the publish, and what it hands out is drawn into the ring's own
/// surface there and then — never given to the render thread — so nothing
/// outlives a lookup and nothing needs counting.
/// </para>
/// <para>
/// Bounded by bytes: half of a render-thread slice of the picture limit (Q221),
/// read live, and never fewer than two pictures, which is what flipping between
/// two drawings needs. A 1080p page is 8 MB.
/// </para>
/// </remarks>
internal sealed class RevisitCache : IDisposable
{
    private readonly LinkedList<(RevisitKey Key, SKImage Image)> _order = new();
    private readonly Dictionary<RevisitKey, LinkedListNode<(RevisitKey Key, SKImage Image)>> _map = [];

    internal long CachedBytes { get; private set; }

    internal int Count => _map.Count;

    internal static long BudgetBytes => PictureMemory.RenderSlice / 2;

    /// <summary>The composite for <paramref name="key"/>, most recent first; null when there is none.</summary>
    internal SKImage? Find(RevisitKey key)
    {
        if (!_map.TryGetValue(key, out var node)) return null;
        _order.Remove(node);
        _order.AddFirst(node);
        return node.Value.Image;
    }

    /// <summary>Keep a copy of <paramref name="composed"/>, which belongs to the ring and is not kept.</summary>
    internal void Store(RevisitKey key, SKImage composed)
    {
        if (_map.ContainsKey(key)) return;
        // Entries from before the last edit can never be found again.
        DropOlderThan(key.Epoch);
        using var bitmap = new SKBitmap(composed.Info);
        if (!composed.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) return;
        var image = SKImage.FromBitmap(bitmap);
        _map[key] = _order.AddFirst((key, image));
        CachedBytes += BytesOf(image);
        while (_map.Count > 2 && CachedBytes > BudgetBytes && _order.Last is { } last) Remove(last);
    }

    private void DropOlderThan(int epoch)
    {
        for (var node = _order.First; node is not null;)
        {
            var next = node.Next;
            if (node.Value.Key.Epoch != epoch) Remove(node);
            node = next;
        }
    }

    private void Remove(LinkedListNode<(RevisitKey Key, SKImage Image)> node)
    {
        _order.Remove(node);
        _map.Remove(node.Value.Key);
        CachedBytes -= BytesOf(node.Value.Image);
        node.Value.Image.Dispose();
    }

    private static long BytesOf(SKImage image) => (long)image.Width * image.Height * 4;

    public void Clear()
    {
        foreach (var (_, image) in _order) image.Dispose();
        _order.Clear();
        _map.Clear();
        CachedBytes = 0;
    }

    public void Dispose() => Clear();
}
