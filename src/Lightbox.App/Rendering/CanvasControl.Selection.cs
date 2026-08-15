using Avalonia;
using Avalonia.Controls;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of <see cref="CanvasControl"/>: the selection overlay — marching
/// ants, the polygon-in-progress trail, and the live transform preview the
/// ants ride so the outline follows the pixels during a drag.
/// </summary>
/// <remarks>
/// Split out under the monolith ratchet, the same move
/// <c>CanvasControl.Pointer.cs</c> made: new work belongs in a partial
/// rather than on the end of the third-riskiest file in the repository.
/// The geometry itself lives in <see cref="SelectionAnts"/>, which is pure
/// and tested; what stays here is the state the window pushes and the
/// caching that keeps the ants animation from re-walking contours at
/// display rate.
/// </remarks>
public partial class CanvasControl
{
    // Selection overlay state (marching ants) — pushed by the window.
    private IReadOnlyList<List<Core.Documents.StrokePoint>> _selectionContours = [];
    private IReadOnlyList<Core.Documents.StrokePoint> _polygonInProgress = [];
    private float _antsPhase;
    private bool _antsAnimating;

    /// <summary>
    /// The committed contours as a path, rebuilt only when the selection
    /// changes. The ants animation invalidates every frame, so building this
    /// in <c>Render</c> walked every contour point per tick for chrome whose
    /// shape had not moved.
    /// </summary>
    private SKPath? _antsBase;

    /// <summary>
    /// The live transform preview. While a session drags, the moving pixels
    /// are composited through this matrix (<c>TransformSession.Preview</c>);
    /// the ants apply the same one so the outline and the pixels agree. Null
    /// outside a drag — and cleared before the commit moves the contours, so
    /// the transform is never applied twice.
    /// </summary>
    private SKMatrix? _selectionPreview;

    public void SetSelectionOverlay(
        IReadOnlyList<List<Core.Documents.StrokePoint>> contours,
        IReadOnlyList<Core.Documents.StrokePoint> polygonInProgress)
    {
        _selectionContours = contours;
        _polygonInProgress = polygonInProgress;
        // The base path never crosses to the render thread — DrawOp gets a
        // per-frame copy — so disposing the old one here cannot race a draw.
        _antsBase?.Dispose();
        _antsBase = SelectionAnts.BasePath(contours);
        InvalidateVisual();
        StartAntsIfNeeded();
    }

    /// <summary>
    /// The matrix a live transform session is previewing through, null to
    /// clear. Pushed by the window from the view model's preview event, the
    /// same value the compositor draws the moving pixels with.
    /// </summary>
    public void SetSelectionPreviewTransform(SKMatrix? preview)
    {
        if (Nullable.Equals(_selectionPreview, preview)) return;
        _selectionPreview = preview;
        InvalidateVisual();
    }

    /// <summary>The overlay paths one frame draws; the op owns and disposes them.</summary>
    private (SKPath? Ants, SKPath? Open) AntsPathsForFrame()
        => (SelectionAnts.FramePath(_antsBase, _selectionPreview, _dragShape),
            SelectionAnts.OpenPath(_polygonInProgress));

    private void StartAntsIfNeeded()
    {
        if (_antsAnimating || (_selectionContours.Count == 0 && _polygonInProgress.Count == 0)) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _antsAnimating = true;
        top.RequestAnimationFrame(OnAntsFrame);
    }

    private void OnAntsFrame(TimeSpan _)
    {
        _antsAnimating = false;
        if (_selectionContours.Count == 0 && _polygonInProgress.Count == 0) return;
        _antsPhase = (_antsPhase + 0.35f) % 8f;
        InvalidateVisual();
        StartAntsIfNeeded();
    }

    /// <summary>
    /// A selection pushed before the control had a <see cref="TopLevel"/>
    /// never animated: <see cref="StartAntsIfNeeded"/> bailed and nothing
    /// retried. Docking a workspace re-attaches the control, so this is a
    /// path an artist actually takes, not just startup order.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartAntsIfNeeded();
    }
}
