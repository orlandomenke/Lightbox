using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// What Skia is holding on its own account, which is not any cache this
/// application wrote.
/// </summary>
/// <remarks>
/// <para>
/// <b>B179's second narrowing.</b> The first capture with the memory section in
/// reported a 2958 MB working set against 596 MB of application caches, with
/// <c>evicted, still in use</c> at zero and the managed heap at 52 MB — so the
/// missing 2362 MB is native, is not awaiting an unpin, and is not managed
/// objects. That refuted the leading hypothesis and left one large pool nobody
/// had measured.
/// </para>
/// <para>
/// <b>Skia keeps two caches of its own and both are queryable.</b> The GPU
/// resource cache holds textures and render targets against a byte limit, and
/// purges to that limit rather than on dispose — so a texture handed back is
/// *purgeable*, not freed, and the cache sits at its ceiling by design. The CPU
/// resource cache holds decoded images, masks and glyphs on the same principle.
/// Neither appears in any budget this application owns, and either can be
/// gigabytes without a single line of application code being wrong.
/// </para>
/// <para>
/// <b>Sampled from the draw op rather than read on demand</b>, because the
/// <see cref="GRContext"/> exists only inside the render thread's lease. The
/// report runs on the UI thread and cannot reach one, which is exactly why this
/// was invisible: the number was never unavailable, it was unreachable from
/// where anybody thought to ask.
/// </para>
/// </remarks>
internal static class SkiaMemory
{
    /// <summary>GPU resources Skia is holding, and the limit it purges to.</summary>
    /// <remarks>
    /// Zero until a frame has been drawn on a GPU-backed lease. Reported as
    /// null in that case rather than as zero — "not measured" and "measured as
    /// nothing" are different findings, and this file exists because they were
    /// once conflated.
    /// </remarks>
    internal static (long Used, long Limit)? Gpu { get; private set; }

    /// <summary>
    /// Take a reading. Cheap — two native getters — and called once per draw.
    /// </summary>
    internal static void Sample(GRContext? gpu)
    {
        if (gpu is null) return;
        try
        {
            gpu.GetResourceCacheUsage(out _, out var used);
            Gpu = (used, gpu.GetResourceCacheLimit());
        }
        catch
        {
            // A diagnostic must never be the thing that fails — DiagnosticLog's
            // rule, and this runs inside the draw op where throwing would take
            // the frame down.
        }
    }

    /// <summary>
    /// Skia's CPU-side cache of decoded images, masks and glyphs. Static, so
    /// unlike <see cref="Gpu"/> it needs no context and is always available.
    /// </summary>
    internal static (long Used, long Limit) Cpu
    {
        get
        {
            try
            {
                return (SKGraphics.GetResourceCacheTotalBytesUsed(),
                        SKGraphics.GetResourceCacheTotalByteLimit());
            }
            catch
            {
                return (0, 0);
            }
        }
    }

    /// <summary>For tests, and for a report written before anything was drawn.</summary>
    internal static void ResetForTests() => Gpu = null;
}
