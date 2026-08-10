using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Keeps layer rasters resident on the GPU, so a frame that shows the same
/// drawing as the last one does not upload it again.
/// </summary>
/// <remarks>
/// <para>
/// <b>B125 stage 5, which is where the win actually arrives.</b> Stage 4 uploads
/// every layer every frame — the worst case, and the design note is explicit that
/// it exists to be measured rather than kept. Blending is cheap once pixels are
/// resident; uploading two megapixels a layer a frame just moves the cost from
/// blend to bus.
/// </para>
/// <para>
/// <b>The hit rate is a number we already have.</b> A texture is reused exactly
/// when a layer shows the same drawing as the previous frame — which is what
/// <see cref="UnchangedLayerRun"/> measured for B165: <b>26% of layer draws at
/// two layers, 51% at six, 59% at ten</b>, over a 24-frame range. The two changes
/// exploit the same property of animation (holds, and layers that do not move)
/// from opposite ends, which is why B165's measurement sizes this as well.
/// </para>
/// <para>
/// <b>The key is instance plus content version, and identity alone is a trap
/// here</b> — the same trap <see cref="LayerStackBake"/> documents. Committing a
/// stroke does not replace a cached layer bitmap; <c>FrameRasterizer.Append</c>
/// stamps into it in place (bounded work, invariant 6), so an instance survives
/// its pixels changing. <see cref="BitmapVersion"/> carries the version the
/// mutator bumps, and it joins the key: a re-render is a new instance, an
/// in-place edit is a new version, and either way the entry misses and the
/// texture is re-uploaded.
/// </para>
/// <para>
/// <b>What is testable here and what is not, stated rather than implied.</b>
/// There is no graphics context in this repository, so the upload itself cannot
/// run. It is therefore behind <see cref="Uploader"/>: every part of this class
/// that can be wrong in an interesting way — the key, the invalidation, the
/// budget, the eviction, the disposal — is exercised with a stub, and the one
/// line that genuinely needs hardware is the default uploader. That is the same
/// division <see cref="GpuComposite"/> makes, and for the same reason.
/// </para>
/// <para>
/// <b>Owned by the render thread.</b> It is read and filled inside the draw op,
/// where the lease's context lives. Nothing else may touch it — a GPU resource
/// freed from the UI thread is B130 wearing a different hat.
/// </para>
/// </remarks>
public sealed class LayerTextureCache : IDisposable
{
    /// <summary>
    /// How a CPU bitmap becomes a GPU-resident image. Injectable so the cache's
    /// behaviour can be tested where no context exists.
    /// </summary>
    public delegate SKImage? Uploader(GRContext gpu, SKBitmap bitmap);

    private readonly record struct Key(SKBitmap Bitmap, long Version);

    private sealed class Entry(SKImage image, long bytes)
    {
        public readonly SKImage Image = image;
        public readonly long Bytes = bytes;
        public long LastUsed;
    }

    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly Uploader _upload;
    private long _clock;

    /// <summary>
    /// How much GPU memory the resident textures may take.
    /// </summary>
    /// <remarks>
    /// Deliberately smaller than the frame cache's RAM budget: VRAM is scarcer,
    /// and on integrated graphics it is the same memory the CPU is already
    /// competing for. Overshooting it is not a slow frame, it is a driver
    /// refusing an allocation — which <see cref="GpuComposite"/> handles by
    /// falling back to CPU, so the failure mode is "no faster" rather than
    /// "broken".
    /// </remarks>
    internal long BudgetBytes { get; set; } = 192L * 1024 * 1024;

    internal long ResidentBytes { get; private set; }

    internal int Count => _entries.Count;

    /// <summary>Textures served without an upload.</summary>
    internal int Hits { get; private set; }

    /// <summary>Textures uploaded because nothing resident matched.</summary>
    internal int Misses { get; private set; }

    /// <summary>Textures dropped to stay inside the budget.</summary>
    internal int Evictions { get; private set; }

    /// <summary>Uploads the context refused. A miss that could not be filled.</summary>
    internal int FailedUploads { get; private set; }

    public LayerTextureCache(Uploader? upload = null) => _upload = upload ?? DefaultUpload;

    /// <summary>
    /// The real thing: hand the pixels to the driver and keep what comes back.
    /// </summary>
    /// <remarks>
    /// The one part of this file no test in this repository can run. A null
    /// return is a refusal, not an exception — the caller draws the CPU bitmap
    /// instead, which is exactly today's behaviour.
    /// </remarks>
    private static SKImage? DefaultUpload(GRContext gpu, SKBitmap bitmap)
    {
        using var cpu = SKImage.FromBitmap(bitmap);
        return cpu?.ToTextureImage(gpu);
    }

    /// <summary>
    /// The GPU-resident form of this bitmap, uploading it if it is not already
    /// there. Null when the context refused, in which case the caller draws the
    /// bitmap the old way.
    /// </summary>
    public SKImage? Resident(GRContext gpu, SKBitmap bitmap)
    {
        var key = new Key(bitmap, BitmapVersion.Of(bitmap));
        if (_entries.TryGetValue(key, out var found))
        {
            found.LastUsed = ++_clock;
            Hits++;
            return found.Image;
        }

        Misses++;
        // A stale version of the same bitmap is dead weight the moment a newer
        // one is asked for — drop it here rather than waiting for the budget,
        // since an in-place edit would otherwise keep every version it ever had.
        DropOtherVersionsOf(bitmap);

        var uploaded = _upload(gpu, bitmap);
        if (uploaded is null)
        {
            FailedUploads++;
            return null;
        }

        var bytes = (long)bitmap.Info.BytesSize;
        _entries[key] = new Entry(uploaded, bytes) { LastUsed = ++_clock };
        ResidentBytes += bytes;
        TrimToBudget();
        return uploaded;
    }

    private void DropOtherVersionsOf(SKBitmap bitmap)
    {
        List<Key>? stale = null;
        foreach (var (key, _) in _entries)
        {
            if (ReferenceEquals(key.Bitmap, bitmap)) (stale ??= []).Add(key);
        }
        if (stale is null) return;
        foreach (var key in stale) Remove(key);
    }

    private void TrimToBudget()
    {
        while (ResidentBytes > BudgetBytes && _entries.Count > 1)
        {
            var oldest = default(Key);
            var oldestUse = long.MaxValue;
            foreach (var (key, entry) in _entries)
            {
                if (entry.LastUsed >= oldestUse) continue;
                oldest = key;
                oldestUse = entry.LastUsed;
            }
            if (oldestUse == long.MaxValue) return;
            Remove(oldest);
            Evictions++;
        }
    }

    private void Remove(Key key)
    {
        if (!_entries.Remove(key, out var entry)) return;
        ResidentBytes -= entry.Bytes;
        entry.Image.Dispose();
    }

    /// <summary>
    /// Drop everything. For a lost or replaced graphics context, where every
    /// texture handle is already invalid.
    /// </summary>
    internal void Clear()
    {
        foreach (var (_, entry) in _entries) entry.Image.Dispose();
        _entries.Clear();
        ResidentBytes = 0;
    }

    internal void ResetCounters()
    {
        Hits = 0;
        Misses = 0;
        Evictions = 0;
        FailedUploads = 0;
    }

    public void Dispose() => Clear();
}
