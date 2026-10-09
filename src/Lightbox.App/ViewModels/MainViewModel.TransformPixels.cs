using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// B433: a transform under a selection moves the pixels the selection holds,
/// not only the strokes.
/// </summary>
/// <remarks>
/// <para>
/// A drawing can be pixels as well as strokes — <see cref="Frame.PngBase64"/>,
/// the baseline an imported image, a PSD layer or a baked merge leaves. A
/// transform with no selection always resampled it. Under a selection it was
/// left where it was, and the box round what moves counted it only when there
/// was no selection — so a lasso over a layer that is all pixels found nothing
/// and Ctrl+T said <i>"Nothing to transform in this scope"</i>. That is the
/// owner's report (Q233) on any layer that did not begin as strokes.
/// </para>
/// <para>
/// <b>The rule is the strokes' rule, applied to pixels</b>: what lies inside the
/// selection moves, what lies outside stays, and the cut is the selection's
/// edge. The baseline is split in two at the selection's outline — the part
/// outside stays, the part inside goes through exactly the resample the whole
/// baseline would have — and the two are put back together, the moved part
/// over the staying one, as a floating selection lands.
/// </para>
/// <para>
/// <b>Surface space throughout.</b> The selection's contours and the baseline
/// are both laid out on the paper's pixels, so the split needs no origin; only
/// the box handed to the gizmo, which is in stroke coordinates, adds the
/// paper's corner.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>
    /// The selection this transform session cuts pixels by, copied when the
    /// session opened; null when the session moves no pixels apart from the
    /// whole drawing — no selection, or a filter that picked lines.
    /// </summary>
    private List<List<StrokePoint>>? _pixelRegion;

    /// <summary>Say, as a session opens, whether it cuts pixels by the selection.</summary>
    private void TakePixelRegion(bool selectionIsTheSubject) =>
        _pixelRegion = selectionIsTheSubject && HasSelection
            ? [.. _selectionContours.Select(c => c.ToList())]
            : null;

    /// <summary>
    /// The box round this drawing's baseline pixels inside the session's
    /// selection, in stroke coordinates; null when there are none.
    /// </summary>
    private SKRect? BaselineInsideRegion(Frame frame)
    {
        if (_pixelRegion is null || frame.PngBase64 is not { Length: > 0 }) return null;
        using var inside = BaselineHalf(frame, inside: true);
        if (inside is null) return null;
        var bytes = inside.GetPixelSpan();
        var rowBytes = inside.RowBytes;
        int minX = inside.Width, minY = inside.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < inside.Height; y++)
        {
            var row = y * rowBytes;
            for (var x = 0; x < inside.Width; x++)
            {
                if (bytes[row + (x * 4) + 3] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        if (maxX < 0) return null;
        return new SKRect(
            minX + Scene.Left, minY + Scene.Top, maxX + 1 + Scene.Left, maxY + 1 + Scene.Top);
    }

    /// <summary>
    /// One side of the baseline, cut at the session's selection: the pixels
    /// inside it, or the pixels outside. Paper-sized; null when the frame has
    /// no baseline or it cannot be decoded.
    /// </summary>
    /// <remarks>
    /// Cut with an antialiased clip on both sides, so a pixel the outline
    /// crosses is shared between the halves in proportion and the two add back
    /// up to the original — no seam where the selection's edge was.
    /// </remarks>
    private SKBitmap? BaselineHalf(Frame frame, bool inside)
    {
        if (_pixelRegion is null || frame.PngBase64 is not { Length: > 0 } encoded) return null;
        SKBitmap? baseline;
        try
        {
            baseline = PngCodec.Decode(encoded);
        }
        catch (FormatException)
        {
            return null;
        }
        if (baseline is null) return null;
        using (baseline)
        {
            var half = new SKBitmap(new SKImageInfo(
                Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(half);
            canvas.Clear(SKColors.Transparent);
            using var path = BrushEngine.PathFromContours(_pixelRegion);
            canvas.ClipPath(path, inside ? SKClipOperation.Intersect : SKClipOperation.Difference, antialias: true);
            canvas.DrawBitmap(baseline, 0, 0);
            canvas.Flush();
            return half;
        }
    }

    /// <summary>
    /// Move the part of a drawing's baseline inside the session's selection
    /// through <paramref name="resample"/> — the same resample a transform with
    /// no selection gives the whole baseline — and leave the rest where it is.
    /// </summary>
    private void ResampleBaselineInsideRegion(Frame frame, Action<Frame> resample)
    {
        using var stay = BaselineHalf(frame, inside: false);
        using var moving = BaselineHalf(frame, inside: true);
        if (stay is null || moving is null) return;

        // The moving half stands in for the whole baseline while it is
        // resampled, so every way of transforming — a matrix, a perspective,
        // the bands, the cage — moves exactly what it would have moved, and
        // no second copy of any of them exists.
        frame.PngBase64 = PngCodec.Encode(moving);
        resample(frame);

        using var moved = PngCodec.Decode(frame.PngBase64!);
        using var result = stay.Copy();
        using (var canvas = new SKCanvas(result))
        {
            canvas.DrawBitmap(moved, 0, 0);
            canvas.Flush();
        }
        frame.PngBase64 = PngCodec.Encode(result);
    }
}
