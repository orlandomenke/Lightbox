using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>SKBitmap ↔ bare PNG base64 (no data-URL prefix).</summary>
public static class PngCodec
{
    public static string Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    public static SKBitmap Decode(string pngBase64)
    {
        var bytes = Convert.FromBase64String(pngBase64);
        return SKBitmap.Decode(bytes)
            ?? throw new InvalidOperationException("PNG decode failed.");
    }
}
