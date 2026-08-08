using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// A content version for bitmaps that are mutated in place, so caches keyed on
/// bitmap identity can tell "same pixels" from "same object".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why identity is not enough here.</b> Committing a stroke does not
/// re-render the layer — <see cref="FrameRasterizer.Append"/> stamps the new
/// stroke straight into the bitmap the frame cache already holds, which is
/// what keeps a commit proportional to the stroke instead of the canvas
/// (invariant 6). The consequence is that a bitmap's identity survives its
/// content changing, and any cache that concluded "same instance, nothing to
/// do" serves the world as it stood before the stroke. That is not a
/// hypothetical: the unbounded-canvas tile cache lost every committed stroke
/// this way, and the layer-stack bake could serve a stale background after an
/// edit-on-another-layer round trip.
/// </para>
/// <para>
/// Skia tracks exactly this internally (<c>getGenerationID</c>), but
/// SkiaSharp does not expose it on <see cref="SKBitmap"/> — so this is the
/// same idea maintained at the two places this codebase mutates a shared
/// bitmap. <b>The discipline point:</b> whatever mutates a bitmap that a
/// cache may hold calls <see cref="Bump"/>; today that is
/// <see cref="FrameRasterizer.Append"/>, which bumps itself so every caller
/// is covered. A weak table, so versions die with their bitmaps and nothing
/// here extends a bitmap's lifetime.
/// </para>
/// </remarks>
public static class BitmapVersion
{
    private sealed class Counter
    {
        public long Value;
    }

    private static readonly ConditionalWeakTable<SKBitmap, Counter> Versions = [];

    /// <summary>The bitmap's current content version. Never-mutated bitmaps are 0.</summary>
    public static long Of(SKBitmap bitmap) =>
        Versions.TryGetValue(bitmap, out var counter) ? counter.Value : 0;

    /// <summary>Record that the bitmap's pixels changed in place.</summary>
    public static void Bump(SKBitmap bitmap) =>
        Versions.GetOrCreateValue(bitmap).Value++;
}
