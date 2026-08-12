namespace Lightbox.App.Services;

/// <summary>
/// How much memory a cache may take, derived from the machine it is running on
/// rather than from the machine it was written on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every budget in this application was a constant, and every constant was
/// chosen while looking at one laptop.</b> `FrameBitmapCache.ByteBudget` was
/// 512 MB and `LayerTextureCache.BudgetBytes` was 192 MB — numbers that leave a
/// 64 GB workstation idling and that a minimum-spec machine cannot afford. That
/// is specificity dressed as a default, and it is the wrong shape for something
/// meant to run in production on hardware nobody here owns.
/// </para>
/// <para>
/// <b>So the rule is: derive, clamp, allow an override.</b> A fraction of what
/// the machine actually has, floored so the minimum spec still works and
/// ceilinged so a large machine does not hand a cache more than it can usefully
/// spend. The artist's setting stays the final word — this decides the
/// <em>default</em>, which is the thing that was wrong.
/// </para>
/// <para>
/// <b>The minimum spec this floors against is a production decision, not a guess
/// (Q64).</b> Lightbox targets indie 2D game work at the low end: sprite and
/// character-cycle documents, which are typically well under 4K even when the
/// game's output is 4K, on an ordinary laptop with integrated graphics and
/// <b>8 GB of RAM</b>. Every floor here is chosen so that machine can open and
/// play a sprite document comfortably, and the ceilings exist so the same code
/// scales up without anybody editing a constant.
/// </para>
/// <para>
/// <b>What this cannot see, said rather than implied: VRAM.</b> There is no
/// portable way to ask how much a graphics card has, so the GPU budget is
/// derived from system memory as a proxy. On integrated graphics that is
/// literally the same memory, so the proxy is good; on a discrete card it
/// underestimates, which errs toward not exhausting VRAM — the safe direction,
/// since an allocation refused there falls back to the processor rather than
/// failing.
/// </para>
/// </remarks>
internal static class MemoryBudget
{
    /// <summary>The minimum spec's memory, in bytes — 8 GB (Q64).</summary>
    internal const long MinimumSpecBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// Memory this process may use, as the runtime understands it.
    /// </summary>
    /// <remarks>
    /// <see cref="GCMemoryInfo.TotalAvailableMemoryBytes"/> rather than the
    /// physical total: it respects a container limit or a cgroup, which is the
    /// number that actually matters when the answer is wrong.
    /// </remarks>
    internal static long Available =>
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes is > 0 and var b
            ? b
            : MinimumSpecBytes;

    /// <summary>
    /// A share of available memory, clamped.
    /// </summary>
    /// <param name="fraction">How much of the machine this cache may claim.</param>
    /// <param name="floorBytes">
    /// What it gets on the minimum spec — the amount below which the feature
    /// stops being worth having rather than merely being smaller.
    /// </param>
    /// <param name="ceilingBytes">
    /// The most it can usefully spend. A cache larger than the working set is
    /// memory held for nothing.
    /// </param>
    internal static long Share(double fraction, long floorBytes, long ceilingBytes)
    {
        if (ceilingBytes < floorBytes) (floorBytes, ceilingBytes) = (ceilingBytes, floorBytes);
        var share = (long)(Available * Math.Clamp(fraction, 0, 1));
        return Math.Clamp(share, floorBytes, ceilingBytes);
    }

    /// <summary>
    /// The range the frame cache lives in, as bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named rather than inlined because the artist's setting clamps against
    /// the ceiling too</b> (<c>MainViewModel.FrameCacheBudgetMb</c>). Two ceilings
    /// that disagree is how a preference ends up offering a value the cache will
    /// not honour: the setting reads back something other than what was typed,
    /// with nothing to explain why.
    /// </para>
    /// <para>
    /// <b>The floor is deliberately not shared</b>, and that asymmetry is the
    /// design. This one is what a minimum-spec machine needs for the cache to be
    /// worth having; the setting's floor is lower, because somebody who has
    /// decided they would rather have the memory back is allowed to say so.
    /// </para>
    /// </remarks>
    internal const long FrameCacheFloorBytes = 256L * 1024 * 1024;

    /// <inheritdoc cref="FrameCacheFloorBytes"/>
    internal const long FrameCacheCeilingBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Rendered frames kept in memory, so scrubbing and onion skin stay instant.
    /// </summary>
    /// <remarks>
    /// An eighth of the machine: 1 GB on the 8 GB minimum spec, 4 GB on a 32 GB
    /// one. The floor is 256 MB, which holds a handful of 4K frames — below that
    /// a large document thrashes and the cache is doing harm rather than nothing.
    /// </remarks>
    internal static long FrameCache() =>
        Share(1.0 / 8, FrameCacheFloorBytes, FrameCacheCeilingBytes);

    /// <summary>
    /// Tiles kept for the unbounded canvas — the route a playing document takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A thirty-second — a quarter of the frame cache's share, and the meanest of
    /// the three. Two reasons, and the second is the one that decided it:
    /// </para>
    /// <para>
    /// A tiled frame is <em>sparse</em>: it holds the tiles a stroke actually
    /// touched rather than a whole canvas, so the same number of bytes buys
    /// considerably more frames here than in the bitmap cache. And these three
    /// budgets are additive in the worst case, so their sum is what a
    /// minimum-spec machine has to survive — at an eighth, a sixteenth and a
    /// sixteenth they would claim a full quarter of an 8 GB laptop between them,
    /// which is a machine that swaps rather than a machine with a fast cache.
    /// </para>
    /// <para>
    /// The floor is 128 MB, which is a few seconds of a sprite document at the
    /// minimum spec — below that playback evicts the frame it is about to need
    /// again. It lands at 256 MB on the minimum spec, which is exactly the
    /// constant this replaced; what changes is that it now grows.
    /// </para>
    /// </remarks>
    internal static long TileCache() =>
        Share(1.0 / 32, 128L * 1024 * 1024, 2L * 1024 * 1024 * 1024);

    /// <summary>
    /// Flattened viewport bitmaps for the tiled route (B167 phase 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A sixty-fourth — the meanest share, and on the merits rather than to
    /// make the sum fit.</b> What a miss costs is what sizes a cache, and this is
    /// the cheapest miss of the four: one composite of tiles that are already in
    /// memory. A frame-cache miss, at the other end, replays a whole frame from
    /// its stroke record, which is the most expensive thing that happens per
    /// frame — so that one gets an eighth and this one gets an eighth of that.
    /// </para>
    /// <para>
    /// The floor is 64 MB, which holds a handful of FullHD viewports — enough for
    /// the held layers of one playback frame, which is the working set that makes
    /// the cache worth having at all.
    /// </para>
    /// </remarks>
    internal static long FlattenCache() =>
        Share(1.0 / 64, 64L * 1024 * 1024, 512L * 1024 * 1024);

    /// <summary>
    /// Layer rasters kept resident on the graphics card (B125 stage 5).
    /// </summary>
    /// <remarks>
    /// A sixteenth, and deliberately meaner than the frame cache: on integrated
    /// graphics this is the same memory the compositor is already competing for,
    /// and overshooting is not a slow frame but a driver refusing the next
    /// allocation. The floor is 64 MB — two 4K layers — because below that the
    /// residency never survives a frame and the feature is pure overhead.
    /// </remarks>
    internal static long LayerTextures() =>
        Share(1.0 / 16, 64L * 1024 * 1024, 1L * 1024 * 1024 * 1024);
}
