using Avalonia.Input;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of the CanvasControl — the transform gizmo's band mode (Q184).
/// </summary>
/// <remarks>
/// <para>
/// <b>A third gizmo mode, alongside the affine box and the free quad.</b> In
/// band mode the box itself never changes: the corner and edge handles, the
/// pivot and the rotate ring are all inert, because the entire point is that the
/// drawing keeps the extent it has. What moves is the dividers inside it, and a
/// divider takes space from one neighbour and gives it to the other.
/// </para>
/// <para>
/// <b>Why it is a mode rather than something available in the ordinary box.</b>
/// Bands are axis-aligned in document space, so a rotated box has no meaningful
/// horizontal. Composing a band map with a rotation is possible and would give
/// the artist two gestures whose interaction nobody can predict; keeping them
/// separate means each mode has exactly one thing it does. It is the same
/// reasoning that already makes perspective a mode.
/// </para>
/// <para>
/// The gestures are chosen to match what the rest of the application already
/// means by its modifiers — the selection tools use Shift to add and Alt to
/// subtract, so here Shift picks the other axis and Alt takes a line away.
/// </para>
/// </remarks>
public partial class CanvasControl
{
    private bool _txBands;

    /// <summary>Vertical dividers — lines at an X, rescaling X.</summary>
    private BandScale.Axis _txBandsX;

    /// <summary>Horizontal dividers — lines at a Y, rescaling Y.</summary>
    private BandScale.Axis _txBandsY;

    /// <summary>Which divider is being dragged, if any.</summary>
    private (bool Vertical, int Index)? _txBandDrag;

    /// <summary>
    /// Band mode: divider lines inside the box instead of a box that resizes.
    /// </summary>
    /// <remarks>
    /// Switching in reseeds both axes from the current bounds, so the dividers
    /// belong to the box as it is now rather than to whatever it was when the
    /// session began. Switching out throws them away — they are a gesture, not
    /// a document, and Q184 answered that they do not persist.
    /// </remarks>
    public bool TransformBands
    {
        get => _txBands;
        set
        {
            if (_txBands == value) return;
            _txBands = value;
            if (value)
            {
                _txPerspective = false;
                SeedBandsFromBounds();
            }
            _txBandDrag = null;
            TransformGizmoChanged?.Invoke();
            InvalidateVisual();
        }
    }

    private void SeedBandsFromBounds()
    {
        _txBandsX = BandScale.Axis.None(_txMinX, _txMaxX);
        _txBandsY = BandScale.Axis.None(_txMinY, _txMaxY);
    }

    /// <summary>The two axes, for the commit.</summary>
    public (BandScale.Axis X, BandScale.Axis Y) TransformBandsResult => (_txBandsX, _txBandsY);

    /// <summary>Whether band mode would move anything.</summary>
    public bool TransformBandsAreIdentity => !_txBandsX.Moves && !_txBandsY.Moves;

    /// <summary>How many dividers are placed, for the status line and the tests.</summary>
    public int TransformDividerCount => _txBandsX.Source.Count + _txBandsY.Source.Count;

    /// <summary>
    /// The band grid as passes for the live preview: a source crop and the
    /// matrix that puts it where it belongs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The source rectangle is integer and the destination is not, and that
    /// asymmetry is deliberate.</b> <c>RenderPass.Source</c> is an
    /// <c>SKRectI</c>, so the crop has to land on whole pixels — and each band
    /// edge is rounded <em>once</em> and shared by the cells on both sides of
    /// it, so adjacent crops abut exactly rather than leaving a row of pixels
    /// out or reading one twice.
    /// </para>
    /// <para>
    /// The destination stays fractional, which can leave a hairline where two
    /// cells meet during a drag. That is a preview artefact and it is the right
    /// trade: the commit maps the stroke record through
    /// <see cref="BandScale.Map"/> and is exact, so nothing an artist keeps
    /// depends on this rounding.
    /// </para>
    /// <para>
    /// <b>A crop drawn through <c>Source</c> lands at the origin</b>
    /// (<c>SceneRenderer.DrawImage</c>), not where it was cut from — so the
    /// matrix here maps an origin-anchored crop to the destination rectangle,
    /// which is a scale and a translate and nothing else.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(SKRectI Source, SKMatrix Matrix)> TransformBandPasses
    {
        get
        {
            if (!_txActive || !_txBands) return [];
            var cells = BandScale.Cells(_txBandsX, _txBandsY);
            var passes = new List<(SKRectI, SKMatrix)>(cells.Count);
            foreach (var c in cells)
            {
                var sx0 = (int)Math.Round(c.SrcLow);
                var sx1 = (int)Math.Round(c.SrcHigh);
                var sy0 = (int)Math.Round(c.SrcTop);
                var sy1 = (int)Math.Round(c.SrcBottom);
                // A band rounded away to nothing has no pixels to draw, and a
                // zero-width source rect is a division by zero below.
                if (sx1 <= sx0 || sy1 <= sy0) continue;
                var source = new SKRectI(sx0, sy0, sx1, sy1);

                var scaleX = (float)((c.DstHigh - c.DstLow) / (sx1 - sx0));
                var scaleY = (float)((c.DstBottom - c.DstTop) / (sy1 - sy0));
                passes.Add((source, SKMatrix.CreateScaleTranslation(
                    scaleX, scaleY, (float)c.DstLow, (float)c.DstTop)));
            }
            return passes;
        }
    }

    /// <summary>How close the pointer must come to grab a divider, in document px.</summary>
    private double TxBandTolerance() => 6.0 / Math.Max(0.01, FitScale() * _zoom);

    /// <summary>The divider under a document point, or null.</summary>
    /// <remarks>
    /// Horizontal lines are tested first. They are the ones this feature was
    /// asked for, so where two cross they are the one that comes up — a
    /// coin-toss at an intersection is worse than a stated preference.
    /// </remarks>
    internal (bool Vertical, int Index)? TxBandHitTest(double x, double y)
    {
        if (!_txBands) return null;
        var tol = TxBandTolerance();
        // Only within the box: a line's grab area does not extend past the ends
        // of the line the artist can see.
        if (x < _txMinX - tol || x > _txMaxX + tol || y < _txMinY - tol || y > _txMaxY + tol)
        {
            return null;
        }
        for (var i = 0; i < _txBandsY.Moved.Count; i++)
        {
            if (Math.Abs(y - _txBandsY.Moved[i]) <= tol) return (false, i);
        }
        for (var i = 0; i < _txBandsX.Moved.Count; i++)
        {
            if (Math.Abs(x - _txBandsX.Moved[i]) <= tol) return (true, i);
        }
        return null;
    }

    /// <summary>Whether a document point is inside the band box at all.</summary>
    private bool TxInsideBandBox(double x, double y) =>
        x >= _txMinX && x <= _txMaxX && y >= _txMinY && y <= _txMaxY;

    /// <summary>
    /// A press in band mode: grab a divider, take one away, or place one.
    /// </summary>
    /// <returns>True when the press was band business and nothing else should see it.</returns>
    /// <remarks>
    /// <b>Alt removes and Shift chooses the axis</b>, which is the vocabulary
    /// the selection tools already use — Shift adds, Alt subtracts. A press on
    /// empty space inside the box places a line, so getting started needs no
    /// separate button: switch to band mode, click where the hip is, drag.
    /// </remarks>
    internal bool TxBandPress(double x, double y, bool shift, bool alt)
    {
        if (!_txBands) return false;

        if (TxBandHitTest(x, y) is { } hit)
        {
            if (alt)
            {
                if (hit.Vertical) _txBandsX = BandScale.Remove(_txBandsX, hit.Index);
                else _txBandsY = BandScale.Remove(_txBandsY, hit.Index);
                _txBandDrag = null;
                TransformGizmoChanged?.Invoke();
                InvalidateVisual();
                return true;
            }
            _txBandDrag = hit;
            InvalidateVisual();
            return true;
        }

        if (!TxInsideBandBox(x, y)) return false;
        // Nothing to remove here, so Alt on empty space means nothing rather
        // than meaning "place one" — an accidental Alt-click should not add.
        if (alt) return true;

        if (shift)
        {
            _txBandsX = BandScale.Add(_txBandsX, x);
            var placed = IndexOfNearest(_txBandsX.Moved, x);
            _txBandDrag = placed >= 0 ? (true, placed) : null;
        }
        else
        {
            _txBandsY = BandScale.Add(_txBandsY, y);
            var placed = IndexOfNearest(_txBandsY.Moved, y);
            _txBandDrag = placed >= 0 ? (false, placed) : null;
        }
        // Placing a line moves nothing (Q184), so the gizmo has changed shape
        // without the drawing changing — the preview still has to be told, or
        // the new line would not appear until something else invalidated.
        TransformGizmoChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Which divider ended up nearest a coordinate — the one just placed.
    /// </summary>
    /// <remarks>
    /// <see cref="BandScale.Add"/> inserts in order and does not report where,
    /// because the ordering is its business rather than the caller's. Asking
    /// afterwards keeps that true and costs a walk of two or three entries.
    /// Returns -1 when the add was refused for being too near an edge, which is
    /// what leaves the press without a drag rather than starting one on the
    /// wrong line.
    /// </remarks>
    private static int IndexOfNearest(IReadOnlyList<double> values, double at)
    {
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < values.Count; i++)
        {
            var d = Math.Abs(values[i] - at);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        // Refused adds leave the list as it was, so the nearest line can be one
        // that was already there and far away.
        return bestDist <= 1e-6 ? best : -1;
    }

    /// <summary>Drag the held divider to a document point.</summary>
    internal void TxBandDragTo(double x, double y)
    {
        if (_txBandDrag is not { } held) return;
        if (held.Vertical) _txBandsX = BandScale.Drag(_txBandsX, held.Index, x);
        else _txBandsY = BandScale.Drag(_txBandsY, held.Index, y);
        TransformGizmoChanged?.Invoke();
        InvalidateVisual();
    }

    private void TxBandRelease() => _txBandDrag = null;

    /// <summary>The divider lines and the box they live in, for the overlay.</summary>
    /// <remarks>
    /// A record of its own rather than six more fields on <c>TxGizmoData</c>:
    /// <c>CanvasControl.cs</c> is at its size ratchet, so a mode's worth of
    /// overlay data belongs beside the mode rather than on the end of the file
    /// every mode shares.
    /// </remarks>
    internal readonly record struct TxBandOverlay(
        double[] Vertical, double[] Horizontal,
        float Left, float Top, float Right, float Bottom);

    /// <summary>The overlay for the current band state, or null when not in band mode.</summary>
    private TxBandOverlay? TxBandOverlayNow() =>
        _txBands
            ? new TxBandOverlay(
                [.. _txBandsX.Moved], [.. _txBandsY.Moved],
                (float)_txMinX, (float)_txMinY, (float)_txMaxX, (float)_txMaxY)
            : null;

    /// <summary>The affine arm of <c>TransformIsIdentity</c>.</summary>
    private bool TxAffineIsIdentity =>
        !_txPerspective
        && Math.Abs(_txScaleX - 1) < 1e-9 && Math.Abs(_txScaleY - 1) < 1e-9
        && Math.Abs(_txAngle) < 1e-9 && Math.Abs(_txDx) < 1e-9 && Math.Abs(_txDy) < 1e-9;

    /// <summary>Leave band mode, keeping nothing.</summary>
    private void TxDropBands()
    {
        _txBands = false;
        _txBandDrag = null;
    }

    /// <summary>A fresh session: box mode, dividers seeded from the new bounds.</summary>
    private void TxResetBands()
    {
        TxDropBands();
        SeedBandsFromBounds();
    }

    /// <summary>
    /// A press during a transform, when band mode wants it.
    /// </summary>
    /// <remarks>
    /// <b>Band mode owns the press outright.</b> The box does not resize in this
    /// mode, so the corner and edge handles have nothing to do — handing them
    /// the press would move a box that is meant to be pinned.
    /// </remarks>
    private bool TxBandPressHandled(double x, double y, PointerEventArgs e)
    {
        if (!TxBandPress(
                x, y,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            return false;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    /// <summary>A move while a divider is held.</summary>
    private bool TxBandMoveHandled(PointerEventArgs e)
    {
        if (!_txActive || _txBandDrag is null) return false;
        var (x, y) = ViewToDoc(e.GetPosition(this));
        TxBandDragTo(x, y);
        e.Handled = true;
        return true;
    }

    /// <summary>Letting go of a divider.</summary>
    private bool TxBandReleaseHandled(PointerEventArgs e)
    {
        if (!_txActive || _txBandDrag is null) return false;
        TxBandRelease();
        e.Pointer.Capture(null);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Draw the dividers instead of the pivot, in band mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns true when it has drawn, so the shared painter is one line. It
    /// lives here rather than in the painter for the ratchet's reason and for a
    /// better one: this is the only drawing that belongs to this mode.
    /// </para>
    /// <para>
    /// <b>No pivot is drawn.</b> The box does not move in band mode, so a pivot
    /// handle would be a control that does nothing — and a handle that does
    /// nothing is worse than no handle, because it invites the drag it will
    /// ignore.
    /// </para>
    /// </remarks>
    private static bool DrawBandsInstead(SKCanvas canvas, TxGizmoData g, float scale)
    {
        if (g.Bands is not { } b) return false;
        // Amber — the colour the pivot and the lazy-brush gizmo use for "the
        // thing you grab", against the box's blue.
        var colour = new SKColor(0xe0, 0xa0, 0x30, 240);
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f / scale,
            Color = colour,
        };
        using var grip = new SKPaint { IsAntialias = true, Color = colour };
        var r = 3.5f / scale;
        foreach (var y in b.Horizontal)
        {
            var fy = (float)y;
            canvas.DrawLine(b.Left, fy, b.Right, fy, line);
            // A grip at each end, so a divider lying over dark ink still reads
            // as something you can take hold of.
            canvas.DrawCircle(b.Left, fy, r, grip);
            canvas.DrawCircle(b.Right, fy, r, grip);
        }
        foreach (var x in b.Vertical)
        {
            var fx = (float)x;
            canvas.DrawLine(fx, b.Top, fx, b.Bottom, line);
            canvas.DrawCircle(fx, b.Top, r, grip);
            canvas.DrawCircle(fx, b.Bottom, r, grip);
        }
        return true;
    }

    /// <summary>Where the divider lines are now, in document space, for the overlay.</summary>
    internal (double[] Vertical, double[] Horizontal) TxBandLines() =>
        ([.. _txBandsX.Moved], [.. _txBandsY.Moved]);
}
