using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The alpha-weighted area and centroid of a rendered frame — the two moments
/// the volume and balance checker reads.
/// </summary>
/// <remarks>
/// <para>
/// A <b>checker</b>, not a guide: it reads the drawing and reports, touches no
/// input and reaches no pixel. Volume is the 0th moment of the ink mass —
/// squash and stretch preserves it, so a ball that squashes must widen, and
/// drift between frames that no single drawing can show becomes a number.
/// The centroid is the 1st moment of the same mass, three more additions in
/// the same loop, which is why the two are one measurement rather than two
/// features.
/// </para>
/// <para>
/// The centroid equals the physical centre of mass only under uniform
/// density. That is the right lie for a drawing aid — the alternative is a
/// density model nobody would tune — and it belongs in the surface's tooltip,
/// not here.
/// </para>
/// <para>
/// Deterministic and pure: a sum over premultiplied alpha, no sampling, no
/// state. Invariant 2 holds trivially, and the same frame always measures the
/// same. "The character" versus "the ink" — effects layers, props, a second
/// character in frame — is deliberately not solved here: the caller chooses
/// which layers to rasterize, so attribution is the artist's statement rather
/// than an inference problem.
/// </para>
/// </remarks>
public readonly record struct InkMoments(double Area, double CentroidX, double CentroidY)
{
    /// <summary>Whether there was any ink to measure at all.</summary>
    /// <remarks>
    /// An empty frame has no centroid — reporting (0, 0) as a place would put
    /// a balance dot in the corner of every blank frame — so callers branch on
    /// this rather than on a magic coordinate.
    /// </remarks>
    public bool HasInk => Area > 0;

    /// <summary>
    /// Measure a rendered frame. Area is in alpha-weighted pixels — one fully
    /// opaque pixel counts 1, a half-covered edge pixel counts ½ — so
    /// anti-aliasing does not inflate a mark's mass.
    /// </summary>
    /// <remarks>
    /// Reads the alpha channel only, so it is blind to colour and works the
    /// same on any ink. Premultiplied or straight makes no difference to
    /// alpha, which is the other reason the loop is this simple.
    /// </remarks>
    public static InkMoments Measure(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        return pixmap is null ? default : Measure(pixmap);
    }

    public static InkMoments Measure(SKPixmap pixmap)
    {
        var info = pixmap.Info;
        if (info.Width <= 0 || info.Height <= 0) return default;

        double area = 0, mx = 0, my = 0;
        var bytesPerPixel = info.BytesPerPixel;
        var alphaOffset = AlphaOffset(info.ColorType);
        if (alphaOffset < 0 || bytesPerPixel <= 0) return default;

        var pixels = pixmap.GetPixelSpan();
        for (var y = 0; y < info.Height; y++)
        {
            var row = pixels.Slice(y * pixmap.RowBytes, info.Width * bytesPerPixel);
            // Row sums accumulate x locally so the outer accumulators see
            // one addition per row — cheaper and, more importantly for a
            // checker, less floating-point drift on large canvases.
            double rowArea = 0, rowX = 0;
            for (var x = 0; x < info.Width; x++)
            {
                var a = row[x * bytesPerPixel + alphaOffset] * (1.0 / 255.0);
                if (a == 0) continue;
                rowArea += a;
                rowX += a * x;
            }
            area += rowArea;
            mx += rowX;
            my += rowArea * y;
        }

        return area > 0
            // Pixel centres, so a mark one pixel wide centres on that pixel
            // rather than on its left edge.
            ? new InkMoments(area, mx / area + 0.5, my / area + 0.5)
            : default;
    }

    /// <summary>Where the alpha byte lives, or -1 for a format with none.</summary>
    private static int AlphaOffset(SKColorType type) => type switch
    {
        SKColorType.Rgba8888 or SKColorType.Bgra8888 => 3,
        SKColorType.Alpha8 => 0,
        _ => -1,
    };
}
