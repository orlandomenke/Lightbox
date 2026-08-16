using Avalonia.Threading;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The bucket's and the wand's hover preview: the region a click would
/// flood or select, traced before the click by the same functions the click
/// runs (<c>FillRegion</c>, <c>WandRegion</c>) — the pick ring's principle,
/// because a preview computed separately from the thing it previews breaks
/// quietly in the case nobody tested.
/// </summary>
/// <remarks>
/// A flood per pointer event would be the regression invariant 6 names, so
/// three things bound the work. The trace runs as one posted job at
/// Background priority with at most one in flight (the
/// <c>RequestLivePostProcess</c> pattern), the pointer path only records the
/// position; a seed inside the last traced region floods the same region, so
/// the hover recomputes only when it crosses the boundary (even-odd
/// containment, holes included); and the canvas draws a cached path, not the
/// contours, per frame.
/// </remarks>
public partial class MainViewModel
{
    private List<List<StrokePoint>>? _fillPreviewContours;
    private bool _fillPreviewIsWand;
    private string? _fillPreviewColor;

    /// <summary>Doc-space region for the containment shortcut; disposed on replace.</summary>
    private SKPath? _fillPreviewRegion;

    private bool _fillPreviewComputing;

    /// <summary>The preview to draw: contours (null clears), whether it is the wand's, and the fill colour.</summary>
    public event Action<IReadOnlyList<List<StrokePoint>>?, bool, string>? FillPreviewChanged;

    private bool FillPreviewEligible =>
        !IsPlaying
        && _hoverPoint is not null
        && (ActiveTool == ToolId.Fill
            || (ActiveTool == ToolId.Select && ActiveSelectVariant == SelectVariant.Wand));

    /// <summary>
    /// Called from the pointer paths and the document-change funnel, like
    /// <c>RefreshPickPreview</c>. Cheap unless the pointer crossed the traced
    /// boundary — then it schedules one background trace.
    /// </summary>
    private void RefreshFillPreview()
    {
        if (!FillPreviewEligible)
        {
            ClearFillPreview();
            return;
        }
        var (x, y) = _hoverPoint!.Value;
        var wand = ActiveTool != ToolId.Fill;
        if (_fillPreviewContours is not null
            && _fillPreviewIsWand == wand
            && _fillPreviewRegion?.Contains((float)x, (float)y) == true)
        {
            // Same connected region, same answer — but the tint must follow
            // the colour in hand even when the region does not move.
            if (!wand && _fillPreviewColor != ColorHex)
            {
                _fillPreviewColor = ColorHex;
                FillPreviewChanged?.Invoke(_fillPreviewContours, wand, ColorHex);
            }
            return;
        }
        if (_fillPreviewComputing) return; // the running job re-checks on finish
        _fillPreviewComputing = true;
        Dispatcher.UIThread.Post(ComputeFillPreview, DispatcherPriority.Background);
    }

    /// <summary>The one background trace. Re-reads the live hover point, so a fast hand coalesces.</summary>
    private void ComputeFillPreview()
    {
        var settled = false;
        try
        {
            if (!FillPreviewEligible)
            {
                ClearFillPreview();
                settled = true;
                return;
            }
            var (x, y) = _hoverPoint!.Value;
            var wand = ActiveTool != ToolId.Fill;
            var result = wand
                ? WandRegion(x, y)
                : FillRegion(x, y, invertSmart: false,
                    ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex));
            if (result is null)
            {
                ClearFillPreview();
                settled = true;
                return;
            }
            var contours = new List<List<StrokePoint>> { result.Outer };
            contours.AddRange(result.Holes);
            contours = ToDocument(contours);

            _fillPreviewContours = contours;
            _fillPreviewIsWand = wand;
            _fillPreviewColor = ColorHex;
            _fillPreviewRegion?.Dispose();
            _fillPreviewRegion = BrushEngine.PathFromContours(contours);
            FillPreviewChanged?.Invoke(contours, wand, ColorHex);
            settled = true;
        }
        finally
        {
            _fillPreviewComputing = false;
            // The pointer may have crossed into another region while this
            // trace ran; ask again and let the containment shortcut decide.
            if (settled) RefreshFillPreview();
        }
    }

    private void ClearFillPreview()
    {
        if (_fillPreviewContours is null && _fillPreviewRegion is null) return;
        _fillPreviewContours = null;
        _fillPreviewColor = null;
        _fillPreviewRegion?.Dispose();
        _fillPreviewRegion = null;
        FillPreviewChanged?.Invoke(null, false, ColorHex);
    }

    /// <summary>
    /// An edit may have changed what a click would flood — a fill just
    /// committed under the pointer is the common case — so forget the traced
    /// region and let the next ask recompute. No clear event: flashing the
    /// preview off and on for an unrelated edit would read as flicker.
    /// </summary>
    private void ForgetFillPreviewRegion()
    {
        _fillPreviewRegion?.Dispose();
        _fillPreviewRegion = null;
        _fillPreviewContours = null;
        RefreshFillPreview();
    }
}
