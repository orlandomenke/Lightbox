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

    public static void Register(IReadOnlyDictionary<string, string> tips)
    {
        foreach (var (id, png) in tips)
        {
            if (Tips.ContainsKey(id)) continue;
            try
            {
                Tips[id] = PngCodec.Decode(png);
            }
            catch
            {
                // A malformed tip must never break rendering — the stroke
                // falls back to the round dab.
            }
        }
    }

    public static SKBitmap? Resolve(string id) => Tips.GetValueOrDefault(id);
}
