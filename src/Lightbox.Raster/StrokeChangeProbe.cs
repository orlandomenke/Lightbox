using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Did stamping this stroke change any pixels at all?
/// </summary>
/// <remarks>
/// <para>
/// <b>B236, and only erasures ask.</b> An eraser swept over blank canvas, or a
/// selection cleared while it held nothing, is a gesture that did nothing to
/// nothing — and recording it leaves a stroke on the drawing and a step in the
/// history for an act with no result. Every other tool <em>adds</em> something,
/// so "did it change anything" is a question with a known answer and nobody
/// needs to pay for asking it.
/// </para>
/// <para>
/// <b>Pixels rather than geometry, and that was the decision (Q102).</b> Asking
/// whether the eraser's path crosses an earlier stroke would be free and is
/// blind to a frame's baseline: an imported drawing carries pixels no stroke
/// accounts for, so a record-only test would throw away an eraser that really
/// did rub something out. Wrong in the direction that loses work, which is the
/// one direction this must never be wrong in.
/// </para>
/// <para>
/// <b>Exact rather than conservative</b>, for the same reason. The cheap version
/// — "was the whole region already transparent" — needs no copy and exits on the
/// first ink pixel, but it keeps a useless stroke whenever the bounding box
/// merely clips a line the eraser never touched, which is most cleanup erasing
/// between two lines. Comparing before against after answers the question that
/// was actually asked.
/// </para>
/// <para>
/// <b>The cost is one copy of the region the stroke can reach, per erasing pen
/// lift.</b> That is the same region <c>CommitBounds</c> already repaints and
/// invariant 6 already sanctions, it is paid once at the end of a gesture rather
/// than per pointer event, and no other tool pays it at all.
/// </para>
/// <para>
/// <b>Cannot-tell is not the same as changed-nothing.</b> <see cref="Open"/>
/// returns null when there is no surface to read, and a null probe means the
/// caller keeps the stroke — the safe answer, since the alternative is deleting
/// an artist's edit on the strength of a measurement that did not happen.
/// </para>
/// </remarks>
public sealed class StrokeChangeProbe
{
    private readonly SKBitmap _surface;
    private readonly SKRectI _region;
    private readonly byte[]? _before;

    private StrokeChangeProbe(SKBitmap surface, SKRectI region, byte[]? before)
    {
        _surface = surface;
        _region = region;
        _before = before;
    }

    /// <summary>
    /// Record what the stroke's reach looks like now, before it is stamped.
    /// Null when the surface cannot be read — see the remarks.
    /// </summary>
    public static StrokeChangeProbe? Open(Stroke stroke, SKBitmap? surface)
    {
        if (surface is null || surface.GetPixels() == IntPtr.Zero) return null;

        // Null bounds means the stroke reaches nothing on this surface — it is
        // empty, or lies entirely off the paper. Either way it can have changed
        // nothing, and there is no region worth copying to prove it.
        if (BrushEngine.CommitBounds(stroke, surface.Info) is not { } region
            || region.Width <= 0 || region.Height <= 0)
        {
            return new StrokeChangeProbe(surface, SKRectI.Empty, null);
        }

        var rowLength = region.Width * surface.Info.BytesPerPixel;
        var size = (long)rowLength * region.Height;
        // A region too large to hold is a measurement we cannot make, so say so
        // rather than answering out of a buffer we failed to fill.
        if (size <= 0 || size > int.MaxValue) return null;

        var before = new byte[(int)size];
        Read(surface, region, before);
        return new StrokeChangeProbe(surface, region, before);
    }

    /// <summary>Are the pixels the stroke could reach exactly as they were?</summary>
    public bool ChangedNothing()
    {
        if (_before is null) return true;                       // reached nothing at all
        if (_surface.GetPixels() == IntPtr.Zero) return false;  // cannot tell: assume it counted

        var bytesPerPixel = _surface.Info.BytesPerPixel;
        var rowLength = _region.Width * bytesPerPixel;
        var pixels = _surface.GetPixelSpan();
        var rowBytes = _surface.RowBytes;

        for (var y = 0; y < _region.Height; y++)
        {
            var source = (_region.Top + y) * rowBytes + _region.Left * bytesPerPixel;
            if (source < 0 || source + rowLength > pixels.Length) return false;
            var now = pixels.Slice(source, rowLength);
            var was = _before.AsSpan(y * rowLength, rowLength);
            if (!now.SequenceEqual(was)) return false;
        }
        return true;
    }

    private static void Read(SKBitmap surface, SKRectI region, byte[] into)
    {
        var bytesPerPixel = surface.Info.BytesPerPixel;
        var rowLength = region.Width * bytesPerPixel;
        var pixels = surface.GetPixelSpan();
        var rowBytes = surface.RowBytes;

        for (var y = 0; y < region.Height; y++)
        {
            var source = (region.Top + y) * rowBytes + region.Left * bytesPerPixel;
            if (source < 0 || source + rowLength > pixels.Length) continue;
            pixels.Slice(source, rowLength).CopyTo(into.AsSpan(y * rowLength, rowLength));
        }
    }
}
