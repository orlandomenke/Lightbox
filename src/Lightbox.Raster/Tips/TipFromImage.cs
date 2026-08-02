using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Tips;

/// <summary>How a scan should be turned into a tip. Every field has a sane default.</summary>
/// <remarks>
/// <b>Applied to a whole batch at once, on purpose.</b> The single most
/// important rule for a set of captures that will be blended is that every one
/// of them gets <em>identical</em> levels — a series where one capture sits a
/// shade darker makes the brush pulse in brightness as the artist tilts the
/// pen. Making the batch the unit these settings apply to is how that is
/// enforced rather than hoped for.
/// </remarks>
public sealed class TipImageSettings
{
    /// <summary>Luminance mapped to nothing. Everything at or below is paper.</summary>
    public double BlackPoint { get; set; }

    /// <summary>Luminance mapped to full. Everything at or above is solid ink.</summary>
    public double WhitePoint { get; set; } = 1;

    /// <summary>
    /// The scan is dark ink on white paper, so the shape is where it is
    /// <em>dark</em>. Off for a capture that is already a white-on-black mask.
    /// </summary>
    public bool Invert { get; set; } = true;

    /// <summary>Baked size, square. Powers of two keep the mipmap chain clean.</summary>
    public int Size { get; set; } = 512;

    /// <summary>
    /// Recentre on the mark's own mass before cropping, rather than trusting
    /// where it happened to sit on the platen.
    /// </summary>
    public bool CentreOnMass { get; set; } = true;

    /// <summary>Width in output pixels of the border ramp that guarantees a clean edge.</summary>
    public int EdgeFeather { get; set; } = 2;
}

/// <summary>What the pipeline made, and what it wants to warn about.</summary>
public sealed record TipImageResult(BrushTip? Tip, string? Rejection)
{
    public bool Ok => Tip is not null;
}

/// <summary>
/// Turns a scan of a physical stamp into a tip: luminance, levels, invert,
/// square, centre, and a guaranteed clean edge.
/// </summary>
/// <remarks>
/// <para>
/// The half of the isolation workflow worth owning. Dust removal and
/// despeckling are general photo retouching and belong in a photo editor;
/// these five steps are mechanical, checkable, and the place they go wrong is
/// silent.
/// </para>
/// <para>
/// <b>The edge check is a refusal, not a fade.</b> If the crop cut through the
/// mark, feathering the border hides the evidence and ships a tip that stamps
/// a soft box down the whole stroke — the commonest way a hand-made brush is
/// ruined, and trivially detectable. So the pipeline measures how much ink sits
/// on the boundary and says no, because the fix is to re-crop the scan, not to
/// blur what is wrong with it.
/// </para>
/// </remarks>
public static class TipFromImage
{
    /// <summary>
    /// Mean coverage on the outermost ring above which the crop is judged to
    /// have cut the mark. Deliberately not zero: a scan has grain, and one
    /// stray speck is not a box edge.
    /// </summary>
    private const float EdgeInkLimit = 0.06f;

    public static TipImageResult Create(SKBitmap source, TipImageSettings settings, string name)
    {
        var size = Math.Clamp(settings.Size, TipGenerator.MinSize, TipGenerator.MaxSize);
        var alpha = Isolate(source, settings);
        if (alpha is null) return new TipImageResult(null, "That image could not be read.");

        var (w, h) = (source.Width, source.Height);
        var box = settings.CentreOnMass ? MassCentredSquare(alpha, w, h) : CentreSquare(w, h);
        var square = Resample(alpha, w, h, box, size);

        if (EdgeInk(square, size) is var ink && ink > EdgeInkLimit)
        {
            return new TipImageResult(null,
                $"The mark runs off the edge of the crop ({ink:P0} ink on the border). " +
                "A tip like this stamps a visible box down every stroke — re-crop the scan " +
                "with clear paper all the way round.");
        }

        Feather(square, size, settings.EdgeFeather);

        using var bitmap = ToBitmap(square, size);
        var (px, py) = Centroid(square, size);
        return new TipImageResult(
            new BrushTip { Name = name, Png = PngCodec.Encode(bitmap), PivotX = px, PivotY = py },
            null);
    }

    /// <summary>Luminance, levels, and the inversion, at source resolution.</summary>
    private static float[]? Isolate(SKBitmap source, TipImageSettings settings)
    {
        using var pixmap = source.PeekPixels();
        if (pixmap is null) return null;

        var lo = (float)Math.Clamp(settings.BlackPoint, 0, 1);
        var hi = (float)Math.Clamp(settings.WhitePoint, 0, 1);
        // A collapsed or reversed range would divide by zero or invert the
        // levels behind the artist's back; clamping to a hair's width keeps it
        // a very hard contrast rather than a surprise.
        if (hi - lo < 1e-3f) hi = lo + 1e-3f;

        int w = source.Width, h = source.Height;
        var a = new float[w * h];
        var span = pixmap.GetPixelSpan();
        var rowBytes = pixmap.RowBytes;
        var bgr = pixmap.ColorType == SKColorType.Bgra8888;

        for (var y = 0; y < h; y++)
        {
            var row = y * rowBytes;
            for (var x = 0; x < w; x++)
            {
                var i = row + x * 4;
                float r = span[i], g = span[i + 1], b = span[i + 2];
                if (bgr) (r, b) = (b, r);
                var luma = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255f;
                var v = Math.Clamp((luma - lo) / (hi - lo), 0f, 1f);
                a[y * w + x] = settings.Invert ? 1f - v : v;
            }
        }

        return a;
    }

    /// <summary>The largest centred square the image can give.</summary>
    private static SKRectI CentreSquare(int w, int h)
    {
        var side = Math.Min(w, h);
        return SKRectI.Create((w - side) / 2, (h - side) / 2, side, side);
    }

    /// <summary>
    /// A square centred on the ink rather than on the paper, so the stamp
    /// rotates about itself instead of wobbling.
    /// </summary>
    private static SKRectI MassCentredSquare(float[] a, int w, int h)
    {
        double sum = 0, sx = 0, sy = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var v = a[y * w + x];
            if (v <= 0.01f) continue;
            sum += v;
            sx += v * x;
            sy += v * y;
        }

        if (sum <= 0) return CentreSquare(w, h);

        var cx = sx / sum;
        var cy = sy / sum;

        // The biggest square centred there that still fits — shrinking beats
        // sliding, because sliding would put the centroid off centre again and
        // that is the one thing this step exists to prevent.
        var side = (int)(2 * Math.Min(Math.Min(cx, w - cx), Math.Min(cy, h - cy)));
        side = Math.Max(TipGenerator.MinSize, Math.Min(side, Math.Min(w, h)));
        return SKRectI.Create((int)(cx - side / 2.0), (int)(cy - side / 2.0), side, side);
    }

    /// <summary>
    /// Box-average the crop down to the tip size. Averaging rather than point
    /// sampling for the same reason the engine does when it minifies a tip:
    /// a 600 DPI scan reduced by point sampling throws away most of the ink and
    /// the mark comes out thin and speckled.
    /// </summary>
    private static float[] Resample(float[] a, int w, int h, SKRectI box, int size)
    {
        var outp = new float[size * size];
        var sx = box.Width / (double)size;
        var sy = box.Height / (double)size;

        for (var oy = 0; oy < size; oy++)
        {
            var y0 = (int)(box.Top + oy * sy);
            var y1 = Math.Max(y0 + 1, (int)(box.Top + (oy + 1) * sy));
            for (var ox = 0; ox < size; ox++)
            {
                var x0 = (int)(box.Left + ox * sx);
                var x1 = Math.Max(x0 + 1, (int)(box.Left + (ox + 1) * sx));

                double sum = 0;
                var n = 0;
                for (var y = y0; y < y1; y++)
                {
                    if ((uint)y >= (uint)h) continue;
                    for (var x = x0; x < x1; x++)
                    {
                        if ((uint)x >= (uint)w) continue;
                        sum += a[y * w + x];
                        n++;
                    }
                }
                outp[oy * size + ox] = n == 0 ? 0f : (float)(sum / n);
            }
        }

        return outp;
    }

    /// <summary>Mean coverage on the outermost ring of the square.</summary>
    private static float EdgeInk(float[] a, int size)
    {
        double sum = 0;
        var n = 0;
        for (var x = 0; x < size; x++)
        {
            sum += a[x] + a[(size - 1) * size + x];
            n += 2;
        }
        for (var y = 1; y < size - 1; y++)
        {
            sum += a[y * size] + a[y * size + size - 1];
            n += 2;
        }
        return (float)(sum / n);
    }

    /// <summary>A short ramp to nothing at the border, so the tip cannot stamp a box.</summary>
    private static void Feather(float[] a, int size, int width)
    {
        var band = Math.Clamp(width, 0, size / 4);
        if (band <= 0) return;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = Math.Min(Math.Min(x, size - 1 - x), Math.Min(y, size - 1 - y));
                if (edge >= band) continue;
                // Reaches exactly zero on the outermost ring rather than
                // merely getting small. "Almost transparent" all the way round
                // a tip is still a box, just a faint one, and a faint box
                // repeated down a stroke accumulates into a visible one.
                a[y * size + x] *= edge / (float)band;
            }
        }
    }

    /// <summary>
    /// Centre of the ink, normalised — the default pivot. Right for a
    /// symmetric stamp, and a starting point the artist can drag for an angled
    /// one, where the pen touched is not where the paint ended up.
    /// </summary>
    private static (double X, double Y) Centroid(float[] a, int size)
    {
        double sum = 0, sx = 0, sy = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var v = a[y * size + x];
            if (v <= 0.01f) continue;
            sum += v;
            sx += v * (x + 0.5);
            sy += v * (y + 0.5);
        }
        return sum <= 0 ? (0.5, 0.5) : (sx / sum / size, sy / sum / size);
    }

    private static SKBitmap ToBitmap(float[] alpha, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var bytes = new byte[size * size * 4];
        for (int i = 0, o = 0; i < alpha.Length; i++, o += 4)
        {
            bytes[o] = 255;
            bytes[o + 1] = 255;
            bytes[o + 2] = 255;
            bytes[o + 3] = (byte)(Math.Clamp(alpha[i], 0f, 1f) * 255f + 0.5f);
        }
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }
}
