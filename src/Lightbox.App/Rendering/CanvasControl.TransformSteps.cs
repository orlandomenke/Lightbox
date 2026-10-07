using Lightbox.Core.Geometry;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of the CanvasControl — the transform session's own undo history, and
/// the two discrete gizmo edits (mirror, reset) that make steps in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>While a transform is open, Ctrl+Z steps back through the session, not
/// through the document.</b> The owner's call: a tweak to the box is the thing
/// in hand, and an undo that reached past it into the record would change the
/// drawing underneath a preview that is still composited over it. Apply ends
/// the session and its history together, so the commit is then one document
/// step — the state before the transform began is one Ctrl+Z away, exactly as
/// it was before this history existed.
/// </para>
/// <para>
/// <b>A step is a finished gesture, not a pointer event.</b> The state is
/// compared at the moments a gesture can end — a release, a mirror, a reset, a
/// mode switch — against the state the last step left, and only a difference
/// records. A drag of forty moves is one step, and a press that moved nothing
/// is none.
/// </para>
/// <para>
/// <b>The history lives in the gizmo because the state does.</b> The view model
/// owns the frames and never sees the box until the commit; snapshotting the
/// gizmo's fields here keeps it that way rather than teaching the view model a
/// shape it has no other reason to know.
/// </para>
/// </remarks>
public partial class CanvasControl
{
    /// <summary>Everything a session step can change — the gizmo's whole shape.</summary>
    /// <remarks>
    /// Bands and the lattice are immutable values (every edit makes a new one),
    /// so holding them is holding a copy. The quad is an array edited in place
    /// by a corner drag, so it is copied in and out.
    /// </remarks>
    private readonly record struct TxStep(
        double PivotX, double PivotY,
        double ScaleX, double ScaleY, double Angle, double Dx, double Dy,
        bool Perspective, double[] Quad,
        bool Bands, BandScale.Axis BandsX, BandScale.Axis BandsY,
        bool Cage, CageWarp.Lattice Lattice, int CageGrid)
    {
        public bool Equals(TxStep other) =>
            PivotX == other.PivotX && PivotY == other.PivotY
            && ScaleX == other.ScaleX && ScaleY == other.ScaleY && Angle == other.Angle
            && Dx == other.Dx && Dy == other.Dy
            && Perspective == other.Perspective && Quad.AsSpan().SequenceEqual(other.Quad)
            && Bands == other.Bands && BandsX == other.BandsX && BandsY == other.BandsY
            && Cage == other.Cage && Lattice == other.Lattice && CageGrid == other.CageGrid;

        public override int GetHashCode() => HashCode.Combine(ScaleX, ScaleY, Angle, Dx, Dy, Bands, Cage);
    }

    private readonly List<TxStep> _txUndo = [];
    private readonly List<TxStep> _txRedo = [];
    private TxStep _txSettled;

    /// <summary>How many session steps Ctrl+Z can take back.</summary>
    public int TransformUndoDepth => _txUndo.Count;

    /// <summary>How many session steps Ctrl+Y can put back.</summary>
    public int TransformRedoDepth => _txRedo.Count;

    private TxStep TxCapture() => new(
        _txPivotX, _txPivotY, _txScaleX, _txScaleY, _txAngle, _txDx, _txDy,
        _txPerspective, (double[])_txQuad.Clone(),
        _txBands, _txBandsX, _txBandsY,
        _txCage, _txLattice, _txCageGrid);

    /// <summary>A fresh session: nothing behind it, nothing ahead.</summary>
    private void TxStartHistory()
    {
        _txUndo.Clear();
        _txRedo.Clear();
        _txSettled = TxCapture();
    }

    /// <summary>Whether a pointer is mid-gesture on the gizmo.</summary>
    private bool TxGestureHeld => _txDrag != TxDrag.None || _txBandDrag is not null || _txCageDrag is not null;

    /// <summary>
    /// A gesture may have ended: record a step if the gizmo differs from the
    /// last one. Cheap to call when nothing changed, so every ending calls it.
    /// </summary>
    private void TxSettle()
    {
        if (!_txActive || TxGestureHeld) return;
        var now = TxCapture();
        if (now.Equals(_txSettled)) return;
        _txUndo.Add(_txSettled);
        _txRedo.Clear();
        _txSettled = now;
    }

    /// <summary>The gizmo changed outside a held drag — tell the preview, then settle.</summary>
    private void TxChanged()
    {
        TransformGizmoChanged?.Invoke();
        TxSettle();
    }

    /// <summary>
    /// A gizmo drag the pointer can no longer finish — capture lost, window
    /// deactivated — ends where it is and becomes a step. Left held, the next
    /// mirror or reset could not settle and would merge into it.
    /// </summary>
    private void TxEndHeldGesture()
    {
        if (!TxGestureHeld) return;
        _txDrag = TxDrag.None;
        _txBandDrag = null;
        _txCageDrag = null;
        TxSettle();
    }

    /// <summary>Step the session back one gesture.</summary>
    /// <returns>False when there was nothing to take back — the box is as it opened.</returns>
    /// <remarks>
    /// A drag still held when the key arrives is finished first and then taken
    /// back, so Ctrl+Z mid-drag means "not that" rather than being ignored. That
    /// also covers a capture lost without a release, which leaves the drag field
    /// set with no pointer behind it.
    /// </remarks>
    public bool UndoTransformStep() => TxStepAcross(_txUndo, _txRedo);

    /// <summary>Put back the step Ctrl+Z last took.</summary>
    public bool RedoTransformStep() => TxStepAcross(_txRedo, _txUndo);

    private bool TxStepAcross(List<TxStep> from, List<TxStep> to)
    {
        if (!_txActive) return false;
        TxEndHeldGesture();
        if (from.Count == 0) return false;
        to.Add(_txSettled);
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        TxRestore(step);
        TransformGizmoChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    private void TxRestore(TxStep step)
    {
        _txPivotX = step.PivotX; _txPivotY = step.PivotY;
        _txScaleX = step.ScaleX; _txScaleY = step.ScaleY; _txAngle = step.Angle;
        _txDx = step.Dx; _txDy = step.Dy;
        _txPerspective = step.Perspective;
        Array.Copy(step.Quad, _txQuad, _txQuad.Length);
        _txBands = step.Bands; _txBandsX = step.BandsX; _txBandsY = step.BandsY;
        _txCage = step.Cage; _txLattice = step.Lattice; _txCageGrid = step.CageGrid;
        // Derived from the lattice and rebuilt on demand.
        _txCageMesh = null;
        _txCageLines = null;
        _txSettled = step;
    }

    /// <summary>Flip in place around the (draggable) pivot — no translation.</summary>
    public void MirrorTransformGizmo(bool horizontal)
    {
        if (!_txActive) return;
        if (_txPerspective)
        {
            for (var i = 0; i < 4; i++)
            {
                if (horizontal) _txQuad[i * 2] = 2 * _txPivotX - _txQuad[i * 2];
                else _txQuad[i * 2 + 1] = 2 * _txPivotY - _txQuad[i * 2 + 1];
            }
        }
        else if (horizontal)
        {
            _txScaleX = -_txScaleX;
        }
        else
        {
            _txScaleY = -_txScaleY;
        }
        TxChanged();
        InvalidateVisual();
    }

    /// <summary>Back to identity (bounds, pivot and mode stay).</summary>
    /// <remarks>A step like any other, so a reset pressed by mistake is one Ctrl+Z.</remarks>
    public void ResetTransformGizmo()
    {
        _txScaleX = 1; _txScaleY = 1; _txAngle = 0; _txDx = 0; _txDy = 0;
        TxReseedShapes();
        TxChanged();
        InvalidateVisual();
    }
}
