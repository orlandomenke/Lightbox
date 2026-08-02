using System.Collections.Concurrent;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Decoded custom brush tips, keyed by their document id (`tip_…`, globally
/// unique). Documents register their <c>BrushTips</c> on load/change; the
/// brush engine resolves by id. Tip PNGs are expected to carry their shape in
/// the ALPHA channel (importers bake grayscale into alpha).
/// </summary>
public static class BrushTipRegistry
{
    private static readonly ConcurrentDictionary<string, SKBitmap> Tips = new();

    /// <summary>
    /// The same tips as images, which is what stamping actually draws.
    /// </summary>
    /// <remarks>
    /// Held rather than made per dab, and that is the whole reason this exists.
    /// A stroke stamps hundreds of dabs; wrapping the bitmap each time would
    /// allocate an <see cref="SKImage"/> per dab in the hot path, and it would
    /// also throw away the mipmap chain Skia builds for a downscale — so every
    /// dab would pay to build it again, or go without.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, SKImage> Images = new();

    public static void Register(IReadOnlyDictionary<string, string> tips)
    {
        foreach (var (id, png) in tips)
        {
            if (Tips.ContainsKey(id)) continue;
            try
            {
                var bitmap = PngCodec.Decode(png);
                Tips[id] = bitmap;
                if (SKImage.FromBitmap(bitmap) is { } image) Images[id] = image;
            }
            catch
            {
                // A malformed tip must never break rendering — the stroke
                // falls back to the round dab.
            }
        }
    }

    public static SKBitmap? Resolve(string id) => Tips.GetValueOrDefault(id);

    /// <summary>The tip as a drawable image, or null.</summary>
    public static SKImage? ResolveImage(string id) => Images.GetValueOrDefault(id);
}
