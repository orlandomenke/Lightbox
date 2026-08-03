using Avalonia.Media.Imaging;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// One tile in the brush's tip picker: the tip, and a picture of it.
/// </summary>
/// <remarks>
/// <para>
/// A dropdown of names is unusable for shapes. Nobody knows what "Cut nib"
/// looks like until they have seen it, and once they have they recognise the
/// picture and never read the name again — which is why the picker is a grid of
/// thumbnails and the name is the caption rather than the control.
/// </para>
/// <para>
/// Thumbnails are cached by tip id. The picker is opened and closed constantly
/// while somebody is choosing, and decoding eight PNGs and recolouring them on
/// each open would be a visible stutter on the one interaction whose whole
/// purpose is browsing.
/// </para>
/// </remarks>
public sealed class TipChoice
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    private TipChoice(BrushTip? tip, string name, Bitmap? thumbnail)
    {
        Tip = tip;
        Name = name;
        Thumbnail = thumbnail;
    }

    /// <summary>The tip, or null for the plain round dab the engine draws itself.</summary>
    public BrushTip? Tip { get; }

    public string Name { get; }

    public Bitmap? Thumbnail { get; }

    /// <summary>
    /// No tip at all. First in the list, because it is what most brushes are
    /// and what somebody backing out of a choice is looking for.
    /// </summary>
    public static TipChoice Round() => new(null, "Round", RoundThumbnail.Value);

    public static TipChoice For(BrushTip tip) => new(tip, tip.Name, Render(tip));

    private static Bitmap? Render(BrushTip tip)
    {
        if (Cache.TryGetValue(tip.Id, out var cached)) return cached;

        Bitmap? bitmap;
        try
        {
            using var decoded = PngCodec.Decode(tip.Png);
            bitmap = Ink(decoded);
        }
        catch
        {
            bitmap = null; // a malformed tip shows no picture rather than throwing
        }

        Cache[tip.Id] = bitmap;
        return bitmap;
    }

    private static readonly Lazy<Bitmap?> RoundThumbnail = new(() =>
    {
        // Drawn rather than baked from the catalogue: this tile means "the
        // engine's own dab", which is not one of the library's tips and must
        // not look like it is.
        var info = new SKImageInfo(96, 96, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawCircle(48, 48, 44, paint);
        }
        return Ink(bitmap);
    });

    /// <summary>
    /// The tip as ink on paper. Its shape lives in alpha, so drawing it
    /// straight onto a dark panel would show a bright blob where a mark should
    /// be — the same treatment the tip workshop's previews use.
    /// </summary>
    private static Bitmap Ink(SKBitmap tip)
    {
        var info = new SKImageInfo(tip.Width, tip.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create a tip thumbnail surface.");
        surface.Canvas.Clear(new SKColor(0xf2, 0xf0, 0xec));
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0x1a, 0x1a, 0x1a), SKBlendMode.SrcIn),
        };
        surface.Canvas.DrawBitmap(tip, 0, 0, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
