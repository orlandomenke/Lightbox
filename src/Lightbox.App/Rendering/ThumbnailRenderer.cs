using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>Renders a frame bitmap into a small Avalonia bitmap for timeline cells and layer rows.</summary>
public static class ThumbnailRenderer
{
    public const int Width = 32;
    public const int Height = 18;

    /// <summary>Timeline-cell thumbnail: paper-white background.</summary>
    public static Bitmap Render(SKBitmap frame) => Render(frame, Width, Height, checker: false);

    /// <summary>
    /// Layer-docker thumbnail: checkerboard background so transparent areas
    /// read as transparent rather than painted white.
    /// </summary>
    public static Bitmap RenderChecker(SKBitmap frame, int width, int height) =>
        Render(frame, width, height, checker: true);

    private static Bitmap Render(SKBitmap frame, int width, int height, bool checker)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create thumbnail surface.");
        var canvas = surface.Canvas;
        if (checker) DrawChecker(canvas, width, height);
        else canvas.Clear(SKColors.White);
        var scale = Math.Min((float)width / frame.Width, (float)height / frame.Height);
        var w = frame.Width * scale;
        var h = frame.Height * scale;
        var dest = new SKRect(
            (width - w) / 2, (height - h) / 2,
            (width + w) / 2, (height + h) / 2);
        canvas.DrawBitmap(frame, dest, new SKPaint { IsAntialias = true });

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    private static void DrawChecker(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(new SKColor(0xc8, 0xc8, 0xc8));
        using var dark = new SKPaint { Color = new SKColor(0x9a, 0x9a, 0x9a) };
        const int cell = 4;
        for (var y = 0; y * cell < height; y++)
        {
            for (var x = 0; x * cell < width; x++)
            {
                if ((x + y) % 2 == 0) continue;
                canvas.DrawRect(x * cell, y * cell, cell, cell, dark);
            }
        }
    }
}
