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
/// selection moves, what lies outside stays. The baseline is split in two at
/// the selection's outline — the part outside stays, the part inside goes
/// through exactly the resample the whole baseline would have — and the two
/// are put back together, the moved part over the staying one, as a floating
/// selection lands.
/// </para>
/// <para>
/// <b>The cut is in the baseline's space.</b> A baseline is laid out in stroke
/// coordinates with its corner at zero (B409); a selection is held on the
/// paper's pixels. The outline is moved by the paper's corner onto the
/// baseline, rather than the baseline onto the paper, so the halves keep the
/// baseline's own size and nothing outside the paper is cropped by a cut.
/// </para>
/// <para>
/// <b>The cut is pixel-exact, not antialiased.</b> Antialiased halves add back
/// up to the original only by addition; laid one over the other they lose a
/// quarter of the alpha where the outline crosses a pixel half-way, which is a
/// translucent line along every lasso edge through solid paint (measured: 192
/// where 255 was). A hard cut has no such line, and its stair-step is the one
/// the selection's own mask already has.
/// </para>
/// <para>
/// <b>The selection is read live</b>, as the strokes' split reads it: a
/// selection nudged while the transform is open cuts the strokes and the
/// pixels alike, at apply.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>
    /// Whether this transform session cuts pixels by the selection — its
    /// subject is the selection rather than the whole drawing or picked lines.
    /// </summary>
    private bool _pixelsFollowSelection;

    /// <summary>
    /// The box round each drawing's pixels under the selection, worked out
    /// once per session: the gizmo's box and the preview's both want it.
    /// </summary>
    private readonly Dictionary<string, SKRect?> _pixelBoxes = [];

    /// <summary>Say, as a session opens, whether it cuts pixels by the selection.</summary>
    private void TakePixelRegion(bool selectionIsTheSubject)
    {
        _pixelsFollowSelection = selectionIsTheSubject && HasSelection;
        _pixelBoxes.Clear();
    }

    /// <summary>The live selection, moved from the paper's pixels onto the baseline's.</summary>
    private SKPath? RegionOnBaseline()
    {
        if (!_pixelsFollowSelection || !HasSelection) return null;
        var path = BrushEngine.PathFromContours(_selectionContours);
        path.Transform(SKMatrix.CreateTranslation(Scene.Left, Scene.Top));
        return path;
    }

    /// <summary>
    /// The box round this drawing's baseline pixels inside the selection, in
    /// stroke coordinates; null when there are none.
    /// </summary>
    private SKRect? BaselineInsideRegion(Frame frame)
    {
        if (!_pixelsFollowSelection || frame.PngBase64 is not { Length: > 0 }) return null;
        if (_pixelBoxes.TryGetValue(frame.Id, out var known)) return known;
        var box = MeasureBaselineInsideRegion(frame);
        _pixelBoxes[frame.Id] = box;
        return box;
    }

    private SKRect? MeasureBaselineInsideRegion(Frame frame)
    {
        using var region = RegionOnBaseline();
        if (region is null || DecodeBaseline(frame) is not { } baseline) return null;
        using (baseline)
        {
            // Only the rows and columns the outline can reach are read.
            var reach = SKRectI.Intersect(
                SKRectI.Ceiling(region.TightBounds), new SKRectI(0, 0, baseline.Width, baseline.Height));
            if (reach.Width <= 0 || reach.Height <= 0) return null;
            using var inside = Half(baseline, region, inside: true);
            var bytes = inside.GetPixelSpan();
            var rowBytes = inside.RowBytes;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (var y = reach.Top; y < reach.Bottom; y++)
            {
                var row = y * rowBytes;
                for (var x = reach.Left; x < reach.Right; x++)
                {
                    if (bytes[row + (x * 4) + 3] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
            }
            // The baseline's pixels are stroke coordinates already.
            return maxX < 0 ? null : new SKRect(minX, minY, maxX + 1, maxY + 1);
        }
    }

    /// <summary>
    /// The baseline cut at the selection — the pixels outside it and the
    /// pixels inside it — from one decode; null when the frame has no baseline,
    /// it cannot be decoded, or this session cuts nothing.
    /// </summary>
    private (SKBitmap Stay, SKBitmap Moving)? BaselineHalves(Frame frame)
    {
        using var region = RegionOnBaseline();
        if (region is null || DecodeBaseline(frame) is not { } baseline) return null;
        using (baseline)
        {
            return (Half(baseline, region, inside: false), Half(baseline, region, inside: true));
        }
    }

    /// <summary>
    /// A half of the baseline drawn onto a paper-sized surface through the
    /// paper's corner, as the canvas shows it; takes ownership of
    /// <paramref name="half"/>.
    /// </summary>
    private SKBitmap OnThePaper(SKBitmap half)
    {
        using (half)
        {
            var paper = new SKBitmap(new SKImageInfo(
                Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(paper);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(half, -Scene.Left, -Scene.Top);
            canvas.Flush();
            return paper;
        }
    }

    private static SKBitmap Half(SKBitmap baseline, SKPath region, bool inside)
    {
        var half = new SKBitmap(new SKImageInfo(
            baseline.Width, baseline.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(half);
        canvas.Clear(SKColors.Transparent);
        canvas.ClipPath(region, inside ? SKClipOperation.Intersect : SKClipOperation.Difference, antialias: false);
        canvas.DrawBitmap(baseline, 0, 0);
        canvas.Flush();
        return half;
    }

    /// <summary>
    /// The baseline as pixels, or null — never a throw: a baseline that cannot
    /// be read moves nothing, as a corrupt one never has.
    /// </summary>
    private static SKBitmap? DecodeBaseline(Frame frame)
    {
        if (frame.PngBase64 is not { Length: > 0 } encoded) return null;
        try
        {
            return PngCodec.Decode(encoded);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Move the part of a drawing's baseline inside the selection through
    /// <paramref name="resample"/> — the same resample a transform with no
    /// selection gives the whole baseline — and leave the rest where it is.
    /// </summary>
    /// <remarks>
    /// The moving half stands in for the whole baseline while it is resampled,
    /// so every way of transforming — a matrix, a perspective, the cage — moves
    /// exactly what it would have moved, and no second copy of any of them
    /// exists. If the resample fails the baseline is put back as it was rather
    /// than left holding only the moving half.
    /// </remarks>
    private void ResampleBaselineInsideRegion(Frame frame, Action<Frame> resample)
    {
        if (BaselineHalves(frame) is not var (stay, moving)) return;
        var original = frame.PngBase64;
        using (stay)
        using (moving)
        {
            try
            {
                frame.PngBase64 = PngCodec.Encode(moving);
                resample(frame);
                using var moved = PngCodec.Decode(frame.PngBase64!);
                using var result = new SKBitmap(new SKImageInfo(
                    Math.Max(stay.Width, moved.Width), Math.Max(stay.Height, moved.Height),
                    SKColorType.Rgba8888, SKAlphaType.Premul));
                using (var canvas = new SKCanvas(result))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(stay, 0, 0);
                    canvas.DrawBitmap(moved, 0, 0);
                    canvas.Flush();
                }
                frame.PngBase64 = PngCodec.Encode(result);
            }
            catch
            {
                frame.PngBase64 = original;
                throw;
            }
        }
    }
}
