using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// A render ↔ base64, losing nothing. The only codec that may touch
/// <c>StrokeCheckpoint.PixelsBase64</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The container is a PNG and what it holds is not a picture.</b> PNG is
/// defined as straight alpha and a render is premultiplied, so this writes the
/// premultiplied bytes into a surface that <em>declares</em> them straight and
/// reads them back the same way. Encoder and decoder are therefore both handed
/// matching alpha types and have nothing to convert — exact by the format's own
/// semantics rather than by Skia happening to round-trip. The file is
/// consequently wrong for any other program that opens it, which is correct: it
/// is a cache field, not an export, and the class it lives on says so.
/// </para>
/// <para>
/// <b>The trap this exists to avoid is <c>SKBitmap.Decode</c>, and it is not the
/// obvious one.</b> Premultiplication survives the round trip intact — measured
/// exhaustively over every legal (alpha, channel) pair, both this route and a
/// plain premultiplied encode come back byte for byte. What does not survive is
/// the <em>channel order</em>: <c>SKBitmap.Decode</c> picks its own layout and
/// returns <c>Bgra8888</c> here, so anything reading those bytes as RGBA gets red
/// and blue swapped — <b>230 902 of 614 400 bytes differ, worst 164</b> on a
/// 250-stroke painting, which reads exactly like precision loss and is nothing of
/// the kind. Every decode below therefore names the <see cref="SKImageInfo"/> it
/// wants instead of accepting what it is given.
/// </para>
/// <para>
/// Size is close to a wash and moves with the art — this route was 11% smaller
/// than a plain premultiplied encode on a 1080p painting and 4% larger on a
/// smaller one — so it is not what the choice rests on. Raw deflate, the other
/// exact option, was more than twice the size of either.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A checkpoint is derived state and one that will
/// not decode is a slow reopen, never a broken one — B137's rule, which was filed
/// for exactly this shape of unguarded decode in the render path. Every failure
/// returns null and the caller replays the record.
/// </para>
/// </remarks>
public static class CheckpointCodec
{
    /// <summary>The layout this codec can carry, both ways.</summary>
    private const SKColorType Format = SKColorType.Rgba8888;

    /// <summary>
    /// Encode a premultiplied render, or null if it is not one this can carry
    /// exactly.
    /// </summary>
    public static string? Encode(SKBitmap render)
    {
        if (render.Info.ColorType != Format || render.Info.AlphaType != SKAlphaType.Premul)
            return null;
        if (render.RowBytes != (long)render.Width * 4) return null;
        if (render.GetPixels() == IntPtr.Zero) return null;

        try
        {
            // Not a copy: the same buffer, described differently, so the
            // encoder is handed straight-alpha data and converts nothing.
            // `render` owns the pixels and outlives this block, which is the
            // whole contract `InstallPixels` asks for.
            var carrier = new SKImageInfo(render.Width, render.Height, Format, SKAlphaType.Unpremul);
            using var reinterpreted = new SKBitmap();
            if (!reinterpreted.InstallPixels(carrier, render.GetPixels(), (int)render.RowBytes))
                return null;

            using var image = SKImage.FromBitmap(reinterpreted);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data is null ? null : Convert.ToBase64String(data.AsSpan());
        }
        catch (Exception e) when (e is OutOfMemoryException or OverflowException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decode to a premultiplied render of exactly this size, or null.
    /// </summary>
    /// <remarks>
    /// The size is demanded rather than discovered. A checkpoint whose pixels
    /// are a different shape from the document that holds it is a checkpoint
    /// from before a canvas resize, and the honest answer to one is to replay
    /// the record — never to stretch pixels into a rectangle they were not
    /// drawn for.
    /// </remarks>
    public static SKBitmap? Decode(string base64, int width, int height)
    {
        if (base64.Length == 0 || width <= 0 || height <= 0) return null;

        SKBitmap? decoded = null;
        try
        {
            var bytes = Convert.FromBase64String(base64);

            using var codec = SKCodec.Create(new MemoryStream(bytes));
            if (codec is null) return null;
            if (codec.Info.Width != width || codec.Info.Height != height) return null;

            decoded = new SKBitmap(new SKImageInfo(width, height, Format, SKAlphaType.Premul));
            if (decoded.GetPixels() == IntPtr.Zero || decoded.RowBytes != (long)width * 4)
                return null;

            // Decoded straight into the premultiplied surface's own buffer, with
            // the codec told the bytes are straight — the same declaration
            // `Encode` made, so again there is nothing to convert. Naming the
            // info is also what keeps the channel order ours rather than
            // whatever `SKBitmap.Decode` would have chosen.
            var carrier = new SKImageInfo(width, height, Format, SKAlphaType.Unpremul);
            if (codec.GetPixels(carrier, decoded.GetPixels()) != SKCodecResult.Success)
                return null;

            var owned = decoded;
            decoded = null;
            return owned;
        }
        catch (Exception e) when (
            e is FormatException or OutOfMemoryException or OverflowException
                or ArgumentException or IOException)
        {
            return null;
        }
        finally
        {
            decoded?.Dispose();
        }
    }
}
