using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>Renders a frame bitmap into a small Avalonia bitmap for timeline cells.</summary>
public static class ThumbnailRenderer
{
    public const int Width = 32;
    public const int Height = 18;

    public static Bitmap Render(SKBitmap frame)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create thumbnail surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        var scale = Math.Min((float)Width / frame.Width, (float)Height / frame.Height);
        var w = frame.Width * scale;
        var h = frame.Height * scale;
        var dest = new SKRect(
            (Width - w) / 2, (Height - h) / 2,
            (Width + w) / 2, (Height + h) / 2);
        canvas.DrawBitmap(frame, dest, new SKPaint { IsAntialias = true });

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
