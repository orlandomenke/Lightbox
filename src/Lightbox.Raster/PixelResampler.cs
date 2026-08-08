using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The Skia half of <see cref="IPixelResampler"/>: resizing the two payloads
/// in a document that are pixels rather than instructions, when the artwork
/// around them is rescaled.
/// </summary>
/// <remarks>
/// <para>
/// Resampling is the honest thing to do here and it is a loss, unlike the rest
/// of an image resize. A stroke is re-stamped at the new size from its own
/// record, so it comes back as sharp as it ever was; an imported baseline
/// scan and a baked smudge sample have no record behind them and can only be
/// interpolated. That asymmetry is worth knowing rather than hiding: it is the
/// argument for the stroke record in one sentence.
/// </para>
/// <para>
/// <see cref="SKSamplingOptions"/> with mipmaps rather than plain bilinear,
/// because scaling <em>down</em> with bilinear point-samples one texel in four
/// and aliases a scan into noise — which on a baked smudge sample shows up as
/// grain that was never in the paint.
/// </para>
/// </remarks>
public sealed class PixelResampler : IPixelResampler
{
    /// <summary>The one instance anything needs; it holds no state.</summary>
    public static readonly PixelResampler Instance = new();

    public string? Resample(string pngBase64, double scaleX, double scaleY)
    {
        if (string.IsNullOrEmpty(pngBase64)) return null;

        SKBitmap source;
        try
        {
            source = PngCodec.Decode(pngBase64);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            // A payload that will not decode cannot be scaled, and keeping it
            // at the old size would render the document wrong in a way nobody
            // would trace back to a resize. The caller drops it.
            return null;
        }

        using (source)
        {
            var width = Math.Max(1, (int)Math.Round(source.Width * scaleX));
            var height = Math.Max(1, (int)Math.Round(source.Height * scaleY));
            if (width == source.Width && height == source.Height) return pngBase64;

            using var scaled = new SKBitmap(new SKImageInfo(
                width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(scaled))
            {
                canvas.Clear(SKColors.Transparent);
                using var image = SKImage.FromBitmap(source);
                canvas.DrawImage(
                    image,
                    SKRect.Create(width, height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }

            return PngCodec.Encode(scaled);
        }
    }
}
