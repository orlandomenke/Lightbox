using SkiaSharp;
namespace Lightbox.Raster;

/// <summary>
/// Where a mark's pixel finds its ceiling, when the footprint that caps it is
/// not the same size as the mark (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>The footprint is a coverage mask, and nobody looks at it.</b> It is a
/// running maximum of the brush's own single-dab coverage, kept so a soft brush
/// can be held to its own falloff instead of hardening where dabs overlap
/// (Q157, B293, B299) — and its only consumer is a per-pixel comparison against
/// the mark. That makes it the one of the live path's three document-sized
/// buffers with no claim on document resolution: the scratch holds ink the
/// artist sees, the post-scratch holds the processed body, and this holds a
/// number that decides how far another buffer's alpha may go.
/// </para>
/// <para>
/// <b>Why it is worth the arithmetic.</b> Every dab in the live path is stamped
/// twice, once as colour and once as a footprint, and the second walk costs the
/// same as the first: <b>43-45 us</b> a dab against <b>44</b>, or <b>49-51%</b>
/// of an event's dab work, measured in Release by
/// <c>FootprintCostsAsMuchAsTheMarkTests</c>. Accumulated at the owner's compose
/// scale of 0.375 the same walk costs <b>11 us</b> — <b>4.1x</b> cheaper, worth
/// <b>39%</b> of an event's stamping. The cost is an area and nothing else: a
/// footprint-only stamp does strictly less work than a colour stamp and costs
/// the same, so there is no per-dab setup to shave and the resolution is the
/// only lever there is.
/// </para>
/// <para>
/// <b>It changes the preview and not the art.</b> <c>EndStroke</c> commits
/// through <c>AppendToFrameRender</c> into <c>StampStroke</c>, which builds its
/// own footprint at document resolution from the stroke record; the live
/// buffer is never read after the pen lifts. So a mark drawn with a coarse
/// ceiling settles onto the exact one the moment the stroke ends, and every
/// export, reload and re-render is bit-identical to what it was.
/// </para>
/// <para>
/// <b>Sampled bilinearly rather than nearest, on purpose.</b> A ceiling read
/// with nearest neighbour is flat across 2.67 document pixels at 0.375, and the
/// only place a ceiling binds at all is the soft rim — so the artefact would be
/// a stair-stepped edge on exactly the brushes this machinery exists to protect.
/// Bilinear errs the other way, a shade under the true maximum inside the
/// falloff, which reads as the rim being marginally softer for as long as the
/// pen is down.
/// </para>
/// </remarks>
/// <param name="Scale">
/// Footprint pixels per mark pixel. <c>1.0</c> is the two buffers agreeing, and
/// every path that has not asked for anything else gets it.
/// </param>
/// <param name="OffsetX">
/// Where the mark's left edge sits in the footprint buffer, in footprint
/// pixels. Non-zero only when the footprint has been cropped to a region whose
/// left edge did not land on a whole footprint pixel — the sub-pixel remainder
/// a crop cannot carry.
/// </param>
/// <param name="OffsetY">The same, downward.</param>
public readonly record struct FootprintSpace(double Scale, double OffsetX, double OffsetY)
{
    /// <summary>The footprint is the document's size and shares its origin.</summary>
    public static readonly FootprintSpace Document = new(1.0, 0, 0);

    /// <summary>
    /// Whether a mark pixel and a footprint pixel are the same pixel, in which
    /// case the capping loops take their original index-for-index path.
    /// </summary>
    public bool IsDocument => Scale >= 1.0 && OffsetX == 0 && OffsetY == 0;

    /// <summary>
    /// The footprint buffer covering a document of this size at this scale.
    /// </summary>
    /// <remarks>
    /// <b>Rounded outward</b>, for the reason <c>LiveTipScale.BufferSize</c>
    /// records: a dimension times a scale is almost never whole, and rounding
    /// down loses the last row and column — where the newest dab sits whenever
    /// the pen is heading right or down.
    /// </remarks>
    public static (int Width, int Height) BufferSize(int width, int height, double scale) =>
        scale >= 1.0
            ? (width, height)
            : (Math.Max(1, (int)Math.Ceiling(width * scale)),
               Math.Max(1, (int)Math.Ceiling(height * scale)));

    /// <summary>
    /// The footprint's ceiling for the mark pixel at
    /// <paramref name="x"/>, <paramref name="y"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both coordinates are taken at their pixel <em>centres</em> — a mark pixel
    /// covers <c>[x, x+1)</c> and a footprint pixel covers <c>[j, j+1)</c>, so
    /// the sample position is <c>(x + 0.5) * scale - 0.5</c> in the footprint's
    /// indexing. Getting that half-pixel wrong shifts the whole ceiling by more
    /// than a document pixel at 0.375 and would drag the cap off the rim it is
    /// supposed to sit on.
    /// </para>
    /// <para>
    /// The red channel is the running maximum; see <c>BrushEngine.StampFootprint</c>
    /// for why the shape lives in a colour channel rather than in alpha.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The same lookup over a ceiling already computed for one window of the
    /// footprint — one byte per cell, row-major, <paramref name="win"/> in the
    /// footprint's own pixels (B349's swept ceiling).
    /// </summary>
    public byte CeilingAt(ReadOnlySpan<byte> window, SKRectI win, int x, int y)
    {
        var u = ((x + 0.5) * Scale) + OffsetX - 0.5 - win.Left;
        var v = ((y + 0.5) * Scale) + OffsetY - 0.5 - win.Top;

        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var tx = u - x0;
        var ty = v - y0;

        var xa = Math.Clamp(x0, 0, win.Width - 1);
        var xb = Math.Clamp(x0 + 1, 0, win.Width - 1);
        var ya = Math.Clamp(y0, 0, win.Height - 1);
        var yb = Math.Clamp(y0 + 1, 0, win.Height - 1);

        double c00 = window[(ya * win.Width) + xa], c10 = window[(ya * win.Width) + xb];
        double c01 = window[(yb * win.Width) + xa], c11 = window[(yb * win.Width) + xb];

        var top = c00 + ((c10 - c00) * tx);
        var bottom = c01 + ((c11 - c01) * tx);
        var value = top + ((bottom - top) * ty);
        return (byte)Math.Clamp(value + 0.5, 0, 255);
    }

    public byte CeilingAt(
        ReadOnlySpan<byte> cap, int rowBytes, int width, int height, int x, int y)
    {
        var u = (x + 0.5) * Scale + OffsetX - 0.5;
        var v = (y + 0.5) * Scale + OffsetY - 0.5;

        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var tx = u - x0;
        var ty = v - y0;

        var xa = Math.Clamp(x0, 0, width - 1);
        var xb = Math.Clamp(x0 + 1, 0, width - 1);
        var ya = Math.Clamp(y0, 0, height - 1);
        var yb = Math.Clamp(y0 + 1, 0, height - 1);

        var rowA = ya * rowBytes;
        var rowB = yb * rowBytes;
        double c00 = cap[rowA + xa * 4], c10 = cap[rowA + xb * 4];
        double c01 = cap[rowB + xa * 4], c11 = cap[rowB + xb * 4];

        var top = c00 + ((c10 - c00) * tx);
        var bottom = c01 + ((c11 - c01) * tx);
        var value = top + ((bottom - top) * ty);
        return (byte)Math.Clamp(value + 0.5, 0, 255);
    }
}
