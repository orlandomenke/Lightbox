using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of <see cref="CanvasControl"/>: which device owns the gesture in
/// flight, when two of them are describing the same hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows Ink posts a phantom mouse beside the pen, and either one can
/// arrive first.</b> The pen-first order was handled from the beginning — a
/// mouse press landing mid-stroke is the synthesized click a window activation
/// delivers, and starting the mark again from wherever the mouse was left would
/// be wrong. The mouse-first order was not, and it is the one that costs the
/// artist the drawing.
/// </para>
/// <para>
/// <b>Measured, not reasoned</b> — the reporter's capture of 2026-08-25,
/// <c>huion-pen-echo-press-steals-the-stroke.txt</c>, replayed through these
/// very handlers. The pen came back into proximity, the promoted mouse pressed
/// at (921, 193), and the pen pressed at (921.6, 193.0) <b>63 ms later</b>.
/// The mouse owned the stroke, so every one of the pen's 238 in-contact
/// samples — 210 delivered moves and the coalesced points riding with them —
/// was dropped by the ownership guard in the move handler, and the mark the
/// artist drew for 1.1 seconds reached the record as <b>one dab</b>. The next
/// stroke in the same capture — pen already in proximity, no promoted press —
/// got its 281 samples and drew correctly.
/// </para>
/// <para>
/// <b>That is B256's mechanism, and it retires the one the entry carried.</b>
/// The reporter's words were <i>"if I draw with the pen after a moment of not
/// near the screen, it only draws straight horizontal lines … on release and
/// drawing again solves it"</i>, and both halves fall straight out of the
/// above: the first press after proximity returns is the one the echo wins, and
/// a stroke built from the phantom mouse's handful of whole-pixel samples is a
/// straight segment between them rather than the line the hand made. The
/// axis-lock hypothesis the entry called certain is dead — the trace was taken
/// to decide it and counts <b>0 events claiming Shift</b>.
/// </para>
/// </remarks>
public sealed partial class CanvasControl
{
    /// <summary>
    /// Whether this press is the pen arriving behind its own echo, and should
    /// therefore take the stroke the echo started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only ever pen-over-not-pen.</b> A mouse never takes a stroke from a
    /// pen, and a pen never takes one from another pen — the first is the
    /// original bug in its original direction, and the second would let a
    /// second stylus cut a mark in half.
    /// </para>
    /// <para>
    /// <b>No time window, deliberately.</b> The obvious guard is "within 200 ms
    /// of the echo's press", and it would be a number chosen rather than
    /// measured — the gap here is 63 ms and nothing says what it is on other
    /// hardware. It is also unnecessary: a mouse stroke that a pen interrupts
    /// seconds later is a pen stroke the artist has plainly started, and
    /// handing it over is what they asked for either way.
    /// </para>
    /// <para>
    /// <b>Nothing has to be undone.</b> The takeover falls through into the
    /// ordinary press path, which calls <c>PaintStarted</c> — and the view
    /// model's <c>BeginStroke</c> replaces the builder's stroke outright and
    /// clears the live scratch. A stroke is only ever written to the document
    /// by <c>EndStroke</c>, which the echo's stroke never reaches, so the dab
    /// it stamped is dropped rather than committed. That is why this is a
    /// condition and not a cancellation path.
    /// </para>
    /// </remarks>
    private bool PenTakesOverFrom(PointerEventArgs e) =>
        e.Pointer.Type == PointerType.Pen && !_strokeWasPen;

    /// <summary>
    /// Losing capture ends the gesture — unless the capture that went away
    /// belonged to a device that was not making it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of the takeover, and a hazard on its own.</b> The
    /// phantom mouse holds a capture of its own from the press it won, and
    /// drops it at a moment nothing here controls. Without this guard that drop
    /// runs <see cref="CancelPointerGestures"/> and ends the pen's mark
    /// mid-stroke — the same lost drawing arriving by a different route.
    /// </para>
    /// <para>
    /// <b>Safe to gate on <c>_painting</c> alone.</b> The press handler reaches
    /// the paint branch only after every other gesture has declined the press,
    /// so while a stroke is in flight none of the flags
    /// <see cref="CancelPointerGestures"/> tests is set. A capture loss from a
    /// foreign pointer therefore has nothing else to cancel either.
    /// </para>
    /// <para>
    /// The window's own <c>Deactivated</c> handler still calls
    /// <see cref="CancelPointerGestures"/> directly and is untouched by this: a
    /// release delivered to another application is one this control will never
    /// see, whichever device was holding it (B185).
    /// </para>
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Services.InputTrace.CaptureLost(e.Pointer);
        if (_painting && e.Pointer.Id != _paintPointerId) return;
        CancelPointerGestures();
    }
}
