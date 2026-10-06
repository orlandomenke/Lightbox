using Avalonia;
using Avalonia.Input;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The symmetry gizmo's side of the canvas: the axis it shows, the two handles
/// a press can take, and the drag that follows. Split out for the monolith
/// ratchet's reason — <c>CanvasControl.cs</c> may not grow — and because this
/// is one gesture with one state, which reads better in one place.
/// </summary>
/// <remarks>
/// The geometry is all <see cref="SymmetryAxisGizmo"/>'s; this file only
/// routes pointer events to it and draws what it returns.
/// </remarks>
public partial class CanvasControl
{
    /// <summary>
    /// The symmetry axis the next stroke will be painted under, or null for
    /// none — in which case no symmetry chrome is drawn at all. Set from the
    /// view model whenever the axis appears, moves or turns.
    /// </summary>
    /// <remarks>
    /// A snapshot, not the view model's object: the gizmo is view-only chrome
    /// (invariant 5) and draws from what it was told, exactly like
    /// <see cref="CameraFrame"/>. Its geometry comes from
    /// <see cref="SymmetryAxisGizmo"/>, so what is drawn and what a press hits
    /// are decided in one place that has no canvas.
    /// </remarks>
    public Core.Documents.SymmetryAxis? Symmetry
    {
        get => _symmetry;
        set
        {
            _symmetry = value;
            InvalidateVisual();
        }
    }

    private Core.Documents.SymmetryAxis? _symmetry;

    /// <summary>A symmetry handle was taken; the drag that follows is one undo step.</summary>
    public event Action? SymmetryDragStarted;

    /// <summary>The centre handle moved, as a document-space delta per move.</summary>
    public event Action<double, double>? SymmetryCentreDragged;

    /// <summary>The rotate handle is at this document point; true when Shift asks for 15° snapping.</summary>
    public event Action<double, double, bool>? SymmetryAngleDragged;

    /// <summary>The handle was released.</summary>
    public event Action? SymmetryDragEnded;

    private SymmetryHandle _symmetryDrag;
    private Point _symmetryLast;

    /// <summary>
    /// A press on one of the gizmo's two handles, tested after the camera's
    /// for the same reason: small targets, and a press on one is a decision.
    /// Anywhere else on the canvas still paints — under the axis, if it is on.
    /// </summary>
    private bool TrySymmetryPress(double x, double y, PointerPressedEventArgs e)
    {
        if (_symmetry is not { } axis) return false;
        var handle = SymmetryAxisGizmo.HandleAt(axis, x, y, (float)(FitScale() * _zoom));
        if (handle == SymmetryHandle.None) return false;
        _symmetryDrag = handle;
        _symmetryLast = new Point(x, y);
        SymmetryDragStarted?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private bool TrySymmetryMove(PointerEventArgs e)
    {
        if (_symmetryDrag == SymmetryHandle.None) return false;
        var (sx, sy) = ViewToDoc(e.GetPosition(this));
        switch (_symmetryDrag)
        {
            case SymmetryHandle.Centre:
                // Incremental doc-space deltas, like the camera's pan — the
                // window moves the axis per event and keys one undo step when
                // the pointer lifts.
                SymmetryCentreDragged?.Invoke(sx - _symmetryLast.X, sy - _symmetryLast.Y);
                break;
            case SymmetryHandle.Rotate:
                // Absolute: the handle follows the pointer, so the angle cannot
                // drift from the hand over a long drag.
                SymmetryAngleDragged?.Invoke(sx, sy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
        }
        _symmetryLast = new Point(sx, sy);
        e.Handled = true;
        return true;
    }

    private bool TrySymmetryRelease(PointerReleasedEventArgs e)
    {
        if (!EndSymmetryDragIfAny()) return false;
        e.Pointer.Capture(null);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// End a drag in flight, if there is one — on release, and when capture is
    /// lost mid-drag (alt-tab, a tab switch).
    /// </summary>
    /// <remarks>
    /// <b>End rather than abandon</b>, the weight brush's choice and for its
    /// reason: the moves already made are on the record, so ending gives them
    /// their one undo step, where abandoning would leave an edit nothing can
    /// take back. Without this, a drag that lost capture kept dragging on
    /// plain hover, and the next stroke's events went to the axis instead of
    /// the paper — the adversary's sequence, written down so it stays fixed.
    /// </remarks>
    private bool EndSymmetryDragIfAny()
    {
        if (_symmetryDrag == SymmetryHandle.None) return false;
        _symmetryDrag = SymmetryHandle.None;
        SymmetryDragEnded?.Invoke();
        return true;
    }

    private sealed partial class DrawOp
    {
        /// <summary>
        /// The symmetry gizmo: a line per reflection, a dashed spoke per
        /// rotation, a square at the centre to move it and a circle along the
        /// axis to turn it. View-only chrome like the camera frame — it shows
        /// where the copies of the next mark will land and never reaches a
        /// pixel.
        ///
        /// Absent unless an axis is on. A document painted without symmetry
        /// shows none of this, which is what "optional" has to mean.
        /// </summary>
        /// <remarks>
        /// <c>AccentCyan</c> from the palette, by value: the camera is orange
        /// everywhere it appears, and the axis needs to read as "not the
        /// camera" at a glance. Cyan is the palette's chosen-state accent and
        /// an axis in use is exactly that. Drawn twice, dark casing then light
        /// line, so it reads on paper and on ink alike.
        /// </remarks>
        private void DrawSymmetryAxis(SKCanvas canvas)
        {
            if (symmetry is not { } axis) return;
            var scale = Math.Max(0.01f, view.Scale);

            using var casing = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3.0f / scale,
                Color = new SKColor(0x26, 0x29, 0x2f, 160),
            };
            using var line = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0x00, 0xd1, 0xbc, 230),
            };
            using var spoke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0x00, 0xd1, 0xbc, 200),
                PathEffect = SKPathEffect.CreateDash([6f / scale, 5f / scale], 0),
            };

            foreach (var l in SymmetryAxisGizmo.Lines(axis, new SKRect(0, 0, view.DocW, view.DocH)))
            {
                canvas.DrawLine(l.From, l.To, casing);
                canvas.DrawLine(l.From, l.To, l.IsMirror ? line : spoke);
            }

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(0x00, 0xd1, 0xbc, 255),
            };
            using var rim = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0x26, 0x29, 0x2f, 220),
            };

            var centre = SymmetryAxisGizmo.Centre(axis);
            var half = SymmetryAxisGizmo.CentreHalfPx / scale;
            var box = new SKRect(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
            canvas.DrawRect(box, fill);
            canvas.DrawRect(box, rim);

            var rotate = SymmetryAxisGizmo.RotateHandle(axis, SymmetryAxisGizmo.RotateOffsetPx / scale);
            var r = SymmetryAxisGizmo.RotateRadiusPx / scale;
            canvas.DrawCircle(rotate, r, fill);
            canvas.DrawCircle(rotate, r, rim);
        }

    }
}
