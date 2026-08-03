using Lightbox.Core.Export;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Writing a normal map for a sheet that has already been rendered.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the finished sheet's alpha rather than re-composed from the document,
/// and that is the whole design. The sheet is the thing the engine will sample; a map
/// generated from a second render could disagree with it about where a sprite's edge
/// is by a pixel, and a normal map that is one pixel out of register with its albedo
/// puts a bright rim on every silhouette.
/// </para>
/// <para>
/// It also means padding, trimming and packing are already applied — the map lines up
/// cell for cell with the sheet by construction, with nothing to keep in step.
/// </para>
/// </remarks>
public static class NormalMapWriter
{
    /// <summary>The path a sheet's normal map goes to.</summary>
    public static string PathFor(string sheetPath) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(sheetPath))!,
            Path.GetFileNameWithoutExtension(sheetPath) + "_normal.png");

    /// <summary>
    /// Read a sheet, generate its normal map, and write it beside it. Returns the path.
    /// </summary>
    public static string Write(string sheetPath, NormalMapOptions options)
    {
        using var sheet = SKBitmap.Decode(sheetPath)
            ?? throw new InvalidOperationException($"Could not read {sheetPath} back to map it.");

        var info = new SKImageInfo(sheet.Width, sheet.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var source = new SKBitmap(info);
        if (!sheet.CopyTo(source, SKColorType.Rgba8888))
            throw new InvalidOperationException("Could not convert the sheet for mapping.");

        var alpha = new byte[sheet.Width * sheet.Height];
        using (var pixels = source.PeekPixels())
        {
            var span = pixels.GetPixelSpan();
            for (var y = 0; y < sheet.Height; y++)
            {
                for (var x = 0; x < sheet.Width; x++)
                {
                    alpha[y * sheet.Width + x] = span[y * pixels.RowBytes + x * 4 + 3];
                }
            }
        }

        var rgba = NormalMapGenerator.Generate(alpha, sheet.Width, sheet.Height, options);

        using var map = new SKBitmap(info);
        // Through the pixel address: SKBitmap.GetPixelSpan is read-only, so a span copy
        // would not compile against it.
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, map.GetPixels(), rgba.Length);

        var path = PathFor(sheetPath);
        using var image = SKImage.FromBitmap(map);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed for the normal map.");
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }
}
