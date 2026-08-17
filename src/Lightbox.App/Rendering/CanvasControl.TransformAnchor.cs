namespace Lightbox.App.Rendering;

/// <summary>
/// What a scale drag holds still (B244) — the anchor decision, split out the
/// way <c>CanvasControl.Cursors.cs</c> split the cursor decision.
/// </summary>
/// <remarks>
/// These two read only press-time state (<c>_txStart</c>, the source bounds,
/// the handle), so they can live away from the pointer handler that uses them.
/// The scale case inside <c>TxDragTo</c> cannot follow: it is the gesture
/// itself, inside the one switch that receives the drag — the same boundary
/// B217, B223 and B241 record in the ratchet.
/// </remarks>
public partial class CanvasControl
{
    /// <summary>
    /// The source-space point a scale drag holds still: the opposite corner
    /// for a corner handle, the opposite edge's midpoint for an edge handle.
    /// </summary>
    /// <remarks>
    /// B244. Scaling used to happen around the pivot whatever handle was
    /// dragged, so pulling one side grew the box on all sides — Photoshop's
    /// <em>Alt</em> behaviour as the only behaviour. The default everywhere
    /// else, and the one a hand expects, is that the side you did not touch
    /// stays where it is; Alt now opts into scaling from the pivot instead,
    /// read per event so it can be pressed and released mid-drag.
    /// </remarks>
    private (double X, double Y) TxAnchorSource()
    {
        (double X, double Y)[] c =
        [
            (_txMinX, _txMinY), (_txMaxX, _txMinY), (_txMaxX, _txMaxY), (_txMinX, _txMaxY),
        ];
        if (_txDrag == TxDrag.ScaleCorner) return c[(_txHandle + 2) % 4];
        var a = c[(_txHandle + 2) % 4];
        var b = c[(_txHandle + 3) % 4];
        return ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    /// <summary>
    /// Cursor position in the press-time local frame — the start pivot and the
    /// start angle, not the live ones.
    /// </summary>
    /// <remarks>
    /// <see cref="TxLocal"/> measures from <see cref="TxPivotNow"/>, which
    /// rides the offset — fine while a scale drag never moved the offset, and
    /// wrong the moment anchored scaling started adjusting it per event: the
    /// cursor would be measured from a pivot the previous event just shifted,
    /// and the box would chase its own correction.
    /// </remarks>
    private (double X, double Y) TxLocalAtStartFrame(double x, double y)
    {
        var dx = x - (_txPivotX + _txStart.Dx);
        var dy = y - (_txPivotY + _txStart.Dy);
        var cos = Math.Cos(-_txStart.Angle);
        var sin = Math.Sin(-_txStart.Angle);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }}
