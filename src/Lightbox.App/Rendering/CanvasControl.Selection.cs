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

    /// <summary>How wide the ring on a polygon's first vertex is, on screen.</summary>
    private const float PolygonVertexRingPixels = 4f;

    /// <summary>The overlay paths one frame draws; the op owns and disposes them.</summary>
    /// <param name="scale">
    /// View scale, so the first vertex's ring stays one size on screen instead
    /// of growing with the zoom — the rule every other piece of chrome in
    /// <c>DrawAnts</c> follows.
    /// </param>
    /// <remarks>
    /// Both halves work in <b>surface</b> pixels, which is what the renderer
    /// draws in: the polygon is already there (<c>AddPolygonVertex</c> converts
    /// per vertex), and the drag shape and the pointer are converted here.
    /// </remarks>
    private (SKPath? Ants, SKPath? Open) AntsPathsForFrame(float scale)
    {
        var origin = (X: (double)DocumentOrigin.X, Y: (double)DocumentOrigin.Y);
        return (
            SelectionAnts.FramePath(
                _antsBase, _selectionPreview, _dragShape, _txActive, origin),
            SelectionAnts.OpenPath(
                _polygonInProgress,
                PolygonRubberBandTarget(),
                PolygonVertexRingPixels / Math.Max(0.01f, scale)));
    }

    /// <summary>
    /// Where the polygon's rubber band reaches: the pointer, in surface
    /// pixels — null when no polygon is being drawn or the pointer has left.
    /// </summary>
    /// <remarks>
    /// <b>B315.</b> The band is the half of the preview that says the tool is
    /// live between clicks. Without it the trail only ever showed what had
    /// already been decided, so the gap between placing a vertex and placing
    /// the next one looked exactly like a tool that had stopped responding.
    /// Read from <see cref="_hoverPoint"/> rather than from a pointer event so
    /// it survives the frames where the pointer has not moved.
    /// </remarks>
    private (double X, double Y)? PolygonRubberBandTarget()
    {
        if (_polygonInProgress.Count == 0) return null;
        if (ToolMode != CanvasToolMode.SelectPolygon) return null;
        if (_hoverPoint is not { } p) return null;
        var (dx, dy) = ViewToDoc(p);
        return DocToSurface(dx, dy);
    }

    // ---- fill / wand hover preview -----------------------------------------

    private SKPath? _fillPreviewBase;
    private bool _fillPreviewWand;
    private SKColor _fillPreviewColor = SKColors.Black;

    /// <summary>
    /// The region the bucket or the wand would take at the pointer, pushed
    /// by the window from the view model's trace — null contours clear it.
    /// Cached as a path here for the ants' reason: the overlay repaints at
    /// pointer rate and the region only changes when the trace does.
    /// </summary>
    public void SetFillPreview(
        IReadOnlyList<List<Core.Documents.StrokePoint>>? contours, bool wand, string colorHex)
    {
        _fillPreviewBase?.Dispose();
        _fillPreviewBase = contours is { Count: > 0 }
            ? Raster.BrushEngine.PathFromContours(contours)
            : null;
        _fillPreviewWand = wand;
        _fillPreviewColor = SKColor.TryParse(colorHex, out var c) ? c : SKColors.Black;
        InvalidateVisual();
    }

    /// <summary>A per-frame copy for the op to own, like the ants base.</summary>
    private SKPath? FillPreviewForFrame() =>
        _fillPreviewBase is { } path ? new SKPath(path) : null;

    private void StartAntsIfNeeded()
    {
        if (_antsAnimating || (_selectionContours.Count == 0 && _polygonInProgress.Count == 0)) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _antsAnimating = true;
        top.RequestAnimationFrame(OnAntsFrame);
    }

    /// <summary>
    /// One tick of the ants. Advances the phase and repaints, then asks for the
    /// next frame.
    /// </summary>
    /// <remarks>
    /// <b>Under a gizmo the loop keeps running and stops repainting</b>, which
    /// is deliberately not the same as stopping the loop. The outline is hidden
    /// there (<c>SelectionAnts.FramePath</c>), so advancing a phase nobody can
    /// see and invalidating the whole canvas for it is work for nothing — but
    /// bailing out entirely would end the animation, and then the ants would
    /// come back <em>drawn and frozen</em> when the session ended, which reads
    /// as a dead selection. A cancel never touches the selection, so nothing
    /// else would restart them.
    /// <para>
    /// Keeping the loop alive costs one empty callback per frame and resumes
    /// marching by itself the moment the gizmo goes — no restart to wire, and
    /// nothing in <c>EndTransformGizmo</c> that has to remember this exists.
    /// </para>
    /// </remarks>
    private void OnAntsFrame(TimeSpan _)
    {
        _antsAnimating = false;
        if (_selectionContours.Count == 0 && _polygonInProgress.Count == 0) return;
        if (!_txActive)
        {
            _antsPhase = (_antsPhase + 0.35f) % 8f;
            InvalidateVisual();
        }
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
    // ---- Ctrl takes hold of what the marquee holds (Q104) ---------------------

    /// <summary>
    /// Ctrl was held on the artwork: take hold of the marquee's contents if
    /// there are any. Returns whether a session opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A delegate that answers rather than an event that does not, for the
    /// reason the line picker is one: the canvas has to know whether the press
    /// became a move or is still on its way to the tool underneath it. Both
    /// refusals — no selection, or the press outside it — are the view model's,
    /// so they are reachable by a test with no window attached.
    /// </para>
    /// <para>
    /// Here rather than beside the press that calls it, because this is the
    /// file that owns what the selection <i>is</i> on this control — the
    /// contours, the ants and the preview matrix that makes them follow a live
    /// move are all above.
    /// </para>
    /// </remarks>
    private Func<double, double, bool>? _beginSelectionMove;

    public void SetSelectionMoveEntry(Func<double, double, bool>? begin) =>
        _beginSelectionMove = begin;

}
