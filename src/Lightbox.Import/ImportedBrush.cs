using SkiaSharp;

namespace Lightbox.Import;

/// <summary>
/// A brush read from an external file, reduced to what Lightbox's engine
/// shares with the source format: a tip shape (alpha-mask PNG) and basic
/// parameters. Engine-specific features of the source (Krita paintop
/// options, Photoshop dynamics) intentionally do not map 1:1.
/// </summary>
public sealed record ImportedBrush(
    string Name,
    string? TipPngBase64,
    double SizePx,
    double Spacing,
    double Opacity = 1,
    double Flow = 1);

/// <summary>Entry point: dispatch on file extension.</summary>
public static class BrushImport
{
    public static List<ImportedBrush> Read(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".gbr" => GbrReader.Read(bytes),
            ".gih" => GihReader.Read(bytes),
            ".abr" => AbrReader.Read(bytes),
            ".kpp" => [KppReader.Read(Path.GetFileNameWithoutExtension(fileName), bytes)],
            _ => throw new FormatException($"Unsupported brush file type \"{ext}\"."),
        };
    }

    /// <summary>Grayscale/alpha tip pixels → white RGBA PNG whose ALPHA carries the shape.</summary>
    internal static string EncodeTip(int width, int height, Func<int, int, byte> alphaAt)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(255, 255, 255, alphaAt(x, y)));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new FormatException("Could not encode brush tip.");
        return Convert.ToBase64String(data.AsSpan());
    }

    internal static double NormalizeSpacing(double percent) =>
        percent <= 0 ? 0.15 : Math.Clamp(percent / 100.0, 0.02, 2);
}
