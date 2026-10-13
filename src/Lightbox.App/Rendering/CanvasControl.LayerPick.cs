using Avalonia;
using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>Part of CanvasControl: the two things a held Ctrl asks of a press.</summary>
public sealed partial class CanvasControl
{
    /// <summary>
    /// Ctrl+Shift+click on the canvas: <c>(x, y, tolerance)</c> in document units,
    /// asking for the layer that shows something there to be made active.
    /// </summary>
    /// <remarks>
    /// The tolerance travels with it for the reason the black arrow's does: only the
    /// canvas knows the zoom.
    /// </remarks>
    public event Action<double, double, double>? LayerPickClicked;

    /// <summary>
    /// A Ctrl+Shift press that started a move instead: where it was, in document and
    /// view coordinates, in case it turns out to have been a click.
    /// </summary>
    private (double X, double Y, Point View)? _layerPickIfNoDrag;

    /// <summary>Whether the keys held are the layer pick's: Ctrl and Shift, and not Alt.</summary>
    internal static bool IsLayerPick(KeyModifiers held) =>
        (held & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt))
        == (KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>
    /// Ctrl+Shift+press finds the layer under the pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked before the held eyedropper, which reads a bare Ctrl with
    /// <c>HasFlag</c> and so used to take this press as well: with a brush in hand
    /// Ctrl+Shift+click fetched a colour, by accident rather than by design.
    /// </para>
    /// <para>
    /// <b>Two presses are left alone here, because they already mean a drag</b>
    /// (Q256: click picks, drag moves). Inside a marquee Ctrl moves what the
    /// selection holds, and that arm is asked before this one. With the Move tool
    /// Ctrl drags the whole layer, so this declines and the tool takes the press.
    /// Both are caught again on release by <see cref="LayerPickIfItWasAClick"/>:
    /// with Shift held as well, a press that went nowhere was a click.
    /// </para>
    /// </remarks>
    private bool LayerPickPressed(PointerPressedEventArgs e, double x, double y)
    {
        if (!IsLayerPick(e.KeyModifiers) || ToolMode == CanvasToolMode.Move) return false;
        LayerPickClicked?.Invoke(x, y, DocTolerance(GrabPixels));
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// After a press: remember it if it was Ctrl+Shift and began a move, so the
    /// release can tell a click from a drag.
    /// </summary>
    private void RememberLayerPickPress(PointerPressedEventArgs e)
    {
        _layerPickIfNoDrag = null;
        if (!IsLayerPick(e.KeyModifiers) || !(_movingContent || _lineMoveLive)) return;
        var view = e.GetPosition(this);
        var (x, y) = ViewToDoc(view);
        _layerPickIfNoDrag = (x, y, view);
    }

    /// <summary>
    /// Before a release is handled: if a remembered Ctrl+Shift press went nowhere,
    /// it was a click. Returns the pick to make once the release has been handled.
    /// </summary>
    /// <remarks>
    /// The same three screen pixels that tell a line's click from its drag. A move
    /// that small is taken back rather than committed — a click that also nudged the
    /// drawing by a pixel would be the worst of both. The marquee's move already
    /// discards a press that went nowhere; the Move tool's is cancelled here.
    /// </remarks>
    private (double X, double Y)? LayerPickIfItWasAClick(PointerReleasedEventArgs e)
    {
        if (_layerPickIfNoDrag is not { } press) return null;
        _layerPickIfNoDrag = null;
        var at = e.GetPosition(this);
        if (Math.Max(Math.Abs(at.X - press.View.X), Math.Abs(at.Y - press.View.Y)) >= LineDragMinimumPixels)
        {
            return null;
        }
        if (_movingContent)
        {
            _movingContent = false;
            e.Pointer.Capture(null);
            ContentMoveCancelled?.Invoke();
        }
        return (press.X, press.Y);
    }

    /// <summary>
    /// Ctrl is a held eyedropper while painting or filling: the colour you want is
    /// almost always already on the canvas, and reaching for a tool to fetch it
    /// breaks the stroke you were about to make. Except Ctrl+Alt on a brush with a
    /// second alternate, which paints (Q255).
    /// </summary>
    private bool HeldEyedropperPressed(PointerPressedEventArgs e, double x, double y)
    {
        if (ToolMode is not (CanvasToolMode.Paint or CanvasToolMode.Fill)
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || CtrlAltPaints(e.KeyModifiers))
        {
            return false;
        }
        PickClicked?.Invoke(x, y);
        e.Handled = true;
        return true;
    }
}
