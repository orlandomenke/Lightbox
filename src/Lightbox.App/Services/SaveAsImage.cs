using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <param name="Quality">
/// 1–100, and meaningless for PNG. Ignored where
/// <see cref="ImageSaveFormats.HasQuality"/> is false rather than silently
/// changing the file size of a lossless format.
/// </param>
/// <param name="Scale">
/// Output scale. Applied by rendering onto a bigger surface, never by scaling
/// stroke geometry (invariant 7) — this hands straight to
/// <see cref="SequenceExporter.RenderFrame"/>, which is the same path export
/// takes.
/// </param>
/// <param name="Matte">
/// The colour that shows through where the drawing is transparent, for a format
/// that cannot keep alpha. White by default, because that is what a JPEG of
/// artwork is nearly always wanted on and what every other application assumes;
/// the save dialog exposes it for the times it is not — a sprite matted onto
/// magenta, say.
/// </param>
public sealed record ImageSaveOptions(
    ImageSaveFormat Format = ImageSaveFormat.Png,
    int Quality = 90,
    double Scale = 1.0,
    string Matte = "#ffffff");

/// <param name="LostTransparency">
/// True when the drawing actually had transparent pixels and the chosen format
/// could not keep them. Measured from the rendered image rather than guessed
/// from the scene, so a fully painted canvas saved as JPEG warns about nothing.
/// </param>
public sealed record ImageSaveResult(
    IReadOnlyList<string> Paths,
    ImageSaveFormat Format,
    bool LostTransparency)
{
    /// <summary>The one sentence worth putting in front of the artist, or null.</summary>
    public string? Warning => LostTransparency
        ? $"{ImageSaveFormats.Label(Format)} has no transparency — the see-through "
            + "areas were filled in. Save as PNG or WebP to keep them."
        : null;
}

/// <summary>
/// Saves the drawing as an ordinary picture — the plain "save this as an image"
/// that sat beside a whole export system and did not exist.
/// </summary>
/// <remarks>
/// <para>
/// <b>It renders through <see cref="SequenceExporter.RenderFrame"/>, deliberately.</b>
/// That is the composite export already uses, camera and parallax included, so a
/// PNG saved here and the same frame taken from a PNG sequence are the same
/// pixels by construction. A second compositing path would be free to drift, and
/// the drift would show up as "the export looks different from the save", which
/// is a bug nobody can localise.
/// </para>
/// <para>
/// <b>What this is not.</b> A run of numbered frames is
/// <see cref="ImageSequenceExporter"/>; sheets, trimming, packing and engine
/// metadata live in <see cref="ExportRunner"/> behind a preset. This writes one
/// picture. It used to carry an opt-in "every frame", from when the sequence
/// export was PNG-only and a JPEG or WebP sequence had no other route; Q219
/// moved that to the sequence export, which shares <see cref="Encode"/> so the
/// two cannot disagree about what a format does with transparency.
/// </para>
/// </remarks>
public static class SaveAsImage
{
    /// <summary>
    /// Reconcile the format the dialog chose with the extension actually typed
    /// into the file picker.
    /// </summary>
    /// <remarks>
    /// <b>The extension wins</b>, because it is the more deliberate of the two: a
    /// default was accepted, a name was typed. Extracted from the click handler
    /// so it can be tested — it is the one place the two can disagree, and the
    /// view it lived in is exercised by nothing.
    /// </remarks>
    public static ImageSaveOptions Reconcile(ImageSaveOptions options, string path) =>
        ImageSaveFormats.FromExtension(path) is { } typed && typed != options.Format
            ? options with { Format = typed }
            : options;

    /// <summary>Write the drawing to <paramref name="path"/>.</summary>
    /// <param name="frameIndex">Which timeline frame to write.</param>
    public static ImageSaveResult Write(
        Doc doc, string path, ImageSaveOptions? options = null, int frameIndex = 0)
    {
        options ??= new ImageSaveOptions();
        var scene = doc.Scene;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var cache = new FrameBitmapCache();
        cache.Rig = RigIndex.For(doc);
        cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel, cache.Rig);

        var index = Math.Clamp(frameIndex, 0, Math.Max(0, scene.FrameCount - 1));
        using var image = SequenceExporter.RenderFrame(doc, cache, index, options.Scale);
        var lostTransparency = Encode(image, path, options);

        return new ImageSaveResult([path], options.Format, lostTransparency);
    }

    /// <summary>
    /// Encode one rendered frame to <paramref name="path"/> — the single place a
    /// format's rules about quality and transparency are applied, shared with
    /// <see cref="ImageSequenceExporter"/>.
    /// </summary>
    /// <returns>Whether transparency was present and had to be filled in.</returns>
    internal static bool Encode(SKImage image, string path, ImageSaveOptions options)
    {
        var keepsAlpha = ImageSaveFormats.SupportsAlpha(options.Format);
        var transparent = !keepsAlpha && HasTransparency(image);

        using var flattened = transparent
            ? Flatten(image, Lightbox.Raster.BrushEngine.ParseColor(options.Matte))
            : null;

        var quality = ImageSaveFormats.HasQuality(options.Format)
            ? Math.Clamp(options.Quality, 1, 100)
            : 100;
        using var data = (flattened ?? image).Encode(Encoded(options.Format), quality)
            ?? throw new InvalidOperationException(
                $"{ImageSaveFormats.Label(options.Format)} encode failed.");

        using var file = File.Create(path);
        data.SaveTo(file);
        return transparent;
    }

    private static SKEncodedImageFormat Encoded(ImageSaveFormat format) => format switch
    {
        ImageSaveFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        ImageSaveFormat.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png,
    };

    /// <summary>
    /// Composite onto an opaque matte, for a format with nowhere to put alpha.
    /// </summary>
    /// <remarks>
    /// Done explicitly rather than left to the encoder: handing a premultiplied
    /// image with alpha to the JPEG encoder darkens every soft edge toward black,
    /// which reads as a dirty halo around the drawing rather than as a missing
    /// feature.
    /// </remarks>
    private static SKImage Flatten(SKImage source, SKColor matte)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create matte surface.");
        surface.Canvas.Clear(matte);
        surface.Canvas.DrawImage(source, 0, 0);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// Whether any pixel is less than fully opaque.
    /// </summary>
    /// <remarks>
    /// A full scan, and only ever on a format that cannot keep alpha. This is a
    /// save, not a pointer event, so a pass over the pixels is affordable where
    /// invariant 6 would forbid it in the paint path — and sampling would be
    /// worse than useless here, because the one pixel it misses is the one the
    /// artist cares about.
    /// </remarks>
    private static bool HasTransparency(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0)) return false;

        var pixels = bitmap.GetPixelSpan();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255) return true;
        }
        return false;
    }
}
