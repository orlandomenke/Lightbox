namespace Lightbox.Core.Projects;

/// <summary>
/// The picture formats Lightbox can save a drawing as.
/// </summary>
/// <remarks>
/// <para>
/// Exactly the three Skia in this build can encode. That was measured rather
/// than assumed: of the fourteen values in <c>SKEncodedImageFormat</c>, eleven —
/// BMP, GIF, ICO, WBMP, PKM, KTX, ASTC, DNG, HEIF, AVIF and JPEG XL — return
/// null from <c>SKImage.Encode</c>, because the native library is not built with
/// those encoders. Adding one means writing the encoder, not adding an enum
/// member, and an enum member without one would be a menu entry that silently
/// writes nothing.
/// </para>
/// <para>
/// <b>TIFF and PSD are the two absences worth naming</b>, since an artist will
/// look for them. Neither has a Skia encoder; both would be ours to write. PSD
/// write was considered and declined for this pass (2026-08-24), which leaves
/// Lightbox able to read a Photoshop file and not hand one back.
/// </para>
/// </remarks>
public enum ImageSaveFormat
{
    /// <summary>Lossless, alpha, the right default for artwork.</summary>
    Png,

    /// <summary>Lossy and has no alpha at all — see <see cref="ImageSaveFormats.SupportsAlpha"/>.</summary>
    Jpeg,

    /// <summary>Lossy but keeps alpha, which is the reason it is here.</summary>
    Webp,
}

/// <summary>What each format can do, in one place so no caller has to guess.</summary>
public static class ImageSaveFormats
{
    public static readonly ImageSaveFormat[] All = [ImageSaveFormat.Png, ImageSaveFormat.Jpeg, ImageSaveFormat.Webp];

    public static string Extension(ImageSaveFormat format) => format switch
    {
        ImageSaveFormat.Jpeg => ".jpg",
        ImageSaveFormat.Webp => ".webp",
        _ => ".png",
    };

    /// <summary>Every extension that should open as this format in a file dialog.</summary>
    public static string[] Extensions(ImageSaveFormat format) => format switch
    {
        ImageSaveFormat.Jpeg => [".jpg", ".jpeg"],
        ImageSaveFormat.Webp => [".webp"],
        _ => [".png"],
    };

    public static string Label(ImageSaveFormat format) => format switch
    {
        ImageSaveFormat.Jpeg => "JPEG",
        ImageSaveFormat.Webp => "WebP",
        _ => "PNG",
    };

    /// <summary>
    /// Whether transparency survives. False for JPEG, and that is the whole
    /// reason this predicate exists rather than being folded into the writer:
    /// a character saved as JPEG comes back on a solid box, and an artist who
    /// finds that out later has already sent the file somewhere.
    /// </summary>
    public static bool SupportsAlpha(ImageSaveFormat format) => format is not ImageSaveFormat.Jpeg;

    /// <summary>Whether a quality setting means anything. PNG ignores it.</summary>
    public static bool HasQuality(ImageSaveFormat format) => format is not ImageSaveFormat.Png;

    /// <summary>The format for a path's extension, or null when it names none of them.</summary>
    public static ImageSaveFormat? FromExtension(string pathOrExtension)
    {
        var ext = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(ext)) ext = pathOrExtension;
        ext = ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
        foreach (var format in All)
        {
            if (Array.IndexOf(Extensions(format), ext) >= 0) return format;
        }
        return null;
    }
}
