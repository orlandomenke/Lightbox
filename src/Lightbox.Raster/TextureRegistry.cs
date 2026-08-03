using System.Collections.Concurrent;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Imported paper textures, keyed by their document id. Documents register
/// their <c>Textures</c> on load; the brush engine resolves by id.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as <see cref="BrushTipRegistry"/>, and for the same
/// reason: a texture is an asset the document carries, so a file renders with
/// nothing else installed — invariant 1. It is decoded once rather than per
/// stroke, because a stroke needs it as a height field and decoding a PNG per
/// mark is a stutter nobody can attribute.
/// </para>
/// <para>
/// <b>Stored as a height field, not as pixels.</b> What the engine wants is one
/// float per texel, and converting on every stroke would be the same waste as
/// re-decoding. Luminance rather than any single channel, so a texture scanned
/// slightly warm or cool reads as the same tooth it looks like.
/// </para>
/// </remarks>
public static class TextureRegistry
{
    /// <summary>One texture, already reduced to the height field the engine samples.</summary>
    public sealed record Field(int Width, int Height, float[] Heights)
    {
        /// <summary>
        /// The texel at a document position, tiled. Nearest rather than
        /// bilinear: a paper tooth is high-frequency by definition, and
        /// smoothing it is throwing away the thing that was imported.
        /// </summary>
        public float At(int x, int y)
        {
            // Positive modulo — a negative document coordinate is ordinary
            // (the region can start left of the origin) and `%` alone would
            // index backwards off the array.
            var u = ((x % Width) + Width) % Width;
            var v = ((y % Height) + Height) % Height;
            return Heights[v * Width + u];
        }
    }

    private static readonly ConcurrentDictionary<string, Field> Fields = new();

    /// <summary>The largest texture that will be kept, per side.</summary>
    /// <remarks>
    /// A 6000px scan of watercolour paper is 36 million floats — 144 MB for a
    /// tooth whose detail repeats every few hundred pixels. Downscaled on
    /// import instead, which also makes the tiling period something an artist
    /// can actually see rather than a pattern that never repeats on screen.
    /// </remarks>
    public const int MaxSide = 1024;

    public static void Register(IReadOnlyDictionary<string, string> textures)
    {
        foreach (var (id, png) in textures)
        {
            if (Fields.ContainsKey(id)) continue;
            try
            {
                using var bitmap = PngCodec.Decode(png);
                Fields[id] = ToField(bitmap);
            }
            catch
            {
                // A malformed texture must never break rendering — the stroke
                // falls back to no texture at all.
            }
        }
    }

    public static Field? Resolve(string? id) =>
        id is null ? null : Fields.GetValueOrDefault(id);

    /// <summary>Reduce an image to a tiling height field, downscaling if it is huge.</summary>
    public static Field ToField(SKBitmap source)
    {
        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(source.Width, source.Height));
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var scaled = scale < 1
            ? source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        var image = scaled ?? source;

        var heights = new float[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = image.GetPixel(x, y);
                // Rec. 601 luminance. A texture is a tooth, not a colour.
                heights[y * w + x] = (0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue) / 255f;
            }
        }

        return new Field(w, h, heights);
    }

    /// <summary>Forget everything. Tests only — the app registers and never unregisters.</summary>
    internal static void ClearForTest() => Fields.Clear();
}
