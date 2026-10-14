using System.Globalization;
using Lightbox.Ai;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The keys as pictures for an inbetween request (2026-10-14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Sent only as stroke coordinates, a model pairs stroke 5 with
/// stroke 5 — a different line whenever the keys were drawn with different
/// strokes, which is how an artist draws them and how a trace comes back. Shown
/// the keys, it can see which line is which part of the subject. The owner chose
/// the whole scene as well (the overview picture, 2026-10-13): one layer's lines
/// alone do not say "this is the far leg of a runner".
/// </para>
/// <para>
/// <b>Four pictures</b>: the layer's own drawing at each key, cropped to where
/// either key has ink so the lines are as large as the budget allows, and the
/// whole visible scene at each key for context. Each is at most
/// <see cref="PictureLongEdge"/> on its long edge — about 450 tokens apiece,
/// against tens of thousands for the strokes (<c>docs/DESIGN-ai-payload.md</c>).
/// </para>
/// <para>
/// <b>What leaves the machine grows</b>: the scene pictures show every visible
/// layer, not only the one being inbetweened. The manual says so.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The long edge of every key picture: the size the payload note measured at ~450 tokens.</summary>
    internal const int PictureLongEdge = 768;

    /// <summary>Paper round the drawing in a cropped picture, in scene pixels.</summary>
    private const int PictureMargin = 24;

    /// <summary>
    /// The keys of <paramref name="layer"/> at <paramref name="aIndex"/> and
    /// <paramref name="bIndex"/> as captioned pictures, or null when neither
    /// key has any ink to show.
    /// </summary>
    internal IReadOnlyList<KeyPicture>? KeyPicturesFor(Layer layer, int aIndex, int bIndex)
    {
        if (layer.Cels[aIndex].Frame is not { } a || layer.Cels[bIndex].Frame is not { } b) return null;
        var (w, h) = (Scene.Width, Scene.Height);
        using var hold = _cache.HoldFetches();
        // The cache's renders, not copies: they are shared and must not be disposed here.
        var drawnA = _cache.Get(a, w, h, celIndex: aIndex);
        var drawnB = _cache.Get(b, w, h, celIndex: bIndex);
        if (InkBox(drawnA, drawnB) is not { } box) return null;

        var scale = Math.Min(1.0, (double)PictureLongEdge / Math.Max(box.Width, box.Height));
        var whole = Math.Min(1.0, (double)PictureLongEdge / Math.Max(w, h));
        return
        [
            new KeyPicture(Cropped("A", aIndex, InScene(box), scale), OnPaper(drawnA, box, scale)),
            new KeyPicture(Cropped("B", bIndex, InScene(box), scale), OnPaper(drawnB, box, scale)),
            new KeyPicture(Whole("A", aIndex, whole), RenderFramePng(aIndex, PictureLongEdge)),
            new KeyPicture(Whole("B", bIndex, whole), RenderFramePng(bIndex, PictureLongEdge)),
        ];
    }

    /// <summary>
    /// A box of render pixels in stroke coordinates: the render's top-left is
    /// the paper's corner, which is not stroke (0, 0) once the canvas has grown
    /// or been cropped on that side.
    /// </summary>
    private SKRectI InScene(SKRectI box)
    {
        box.Offset((int)Scene.Left, (int)Scene.Top);
        return box;
    }

    internal static string Cropped(string key, int index, SKRectI box, double scale) => string.Create(
        CultureInfo.InvariantCulture,
        $"Key {key} (frame {index + 1}), the layer being inbetweened, alone: scene x {box.Left}–{box.Right}, y {box.Top}–{box.Bottom}, at {scale:0.###} scale. Picture pixel (px, py) is scene pixel ({box.Left} + px / {scale:0.###}, {box.Top} + py / {scale:0.###}).");

    internal static string Whole(string key, int index, double scale) => string.Create(
        CultureInfo.InvariantCulture,
        $"Key {key} (frame {index + 1}), the whole scene with every visible layer, at {scale:0.###} scale — context for what the drawing is; draw only the layer being inbetweened.");

    /// <summary>Where either picture has ink, padded by the margin and kept on the page; null for none.</summary>
    private static SKRectI? InkBox(SKBitmap a, SKBitmap b)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var bitmap in new[] { a, b })
        {
            var pixels = bitmap.GetPixelSpan();
            var row = bitmap.RowBytes;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (pixels[y * row + x * 4 + 3] < 16) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < left) return null;
        return new SKRectI(
            Math.Max(0, left - PictureMargin), Math.Max(0, top - PictureMargin),
            Math.Min(a.Width, right + 1 + PictureMargin), Math.Min(a.Height, bottom + 1 + PictureMargin));
    }

    /// <summary>The box of a render, on white, scaled, as base64 PNG: black on white is what a model reads best.</summary>
    private static string OnPaper(SKBitmap drawn, SKRectI box, double scale)
    {
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(box.Width * scale)), Math.Max(1, (int)Math.Round(box.Height * scale)));
        using var picture = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(picture))
        using (var image = SKImage.FromBitmap(drawn))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, SKRect.Create(box.Left, box.Top, box.Width, box.Height),
                SKRect.Create(0, 0, size.Width, size.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        using var data = picture.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }
}
