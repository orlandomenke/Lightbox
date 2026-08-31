using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of <see cref="CanvasControl"/> — the gestures that belong to one tool
/// and the view-only chrome that goes with them.
/// </summary>
/// <remarks>
/// Here rather than in <c>CanvasControl.cs</c>, which is on the monolith
/// ratchet. What lives here is the narrow surface between a pointer and a tool:
/// the canvas knows where the press landed, the view model knows what is under
/// it, and nothing in between needs the compositor.
/// </remarks>
public sealed partial class CanvasControl
{
    // ---- the text tool ---------------------------------------------------------

    /// <summary>Where the artist wants a caret. The keyboard does the rest.</summary>
    public event Action<double, double>? TextPlaced;

    /// <summary>The far end of a selection being dragged out.</summary>
    public event Action<double, double>? TextDragged;

    /// <summary>A double-click: take the word under it.</summary>
    public event Action<double, double>? TextWordPicked;

    /// <summary>
    /// Whether a selection is being dragged with the text tool.
    /// </summary>
    /// <remarks>
    /// <b>There is a drag now, which there was not before.</b> A press used to
    /// be the whole gesture — place a caret and let go — so the tool took no
    /// capture at all. Selecting by dragging needs both: the capture, or the
    /// pointer leaving the canvas mid-sweep abandons the selection halfway; and
    /// the flag, or a move with the brush in hand would be read as one.
    /// </remarks>
    private bool _textDragging;

    /// <summary>
    /// A press with the text tool in hand: place a caret, pick up the type
    /// under it, or take a word.
    /// </summary>
    /// <remarks>
    /// Focus comes here so the keystrokes that follow arrive at the window
    /// rather than at whatever was last clicked. What the press <em>means</em>
    /// is still the view model's to decide — it is the side that knows what is
    /// on the frame — and this only says where and how many clicks.
    /// </remarks>
    private void PressText(Avalonia.Input.PointerPressedEventArgs e, double x, double y)
    {
        Focus();
        if (e.ClickCount >= 2)
        {
            // The word, and no drag: a double-click that then swept would be
            // Word's select-by-word gesture, which nothing here needs.
            TextWordPicked?.Invoke(x, y);
            e.Handled = true;
            return;
        }
        e.Pointer.Capture(this);
        _textDragging = true;
        TextPlaced?.Invoke(x, y);
        e.Handled = true;
    }

    /// <summary>Carry the selection's far end with the pointer.</summary>
    /// <returns>Whether this was a text drag, and so already handled.</returns>
    private bool TextDragMoved(Avalonia.Input.PointerEventArgs e)
    {
        if (!_textDragging) return false;
        var (x, y) = ViewToDoc(e.GetPosition(this));
        TextDragged?.Invoke(x, y);
        e.Handled = true;
        return true;
    }

    /// <inheritdoc cref="TextDragMoved"/>
    private bool TextDragReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!_textDragging) return false;
        _textDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Ask the view model to open the type under a point for editing.
    /// </summary>
    /// <remarks>
    /// The Arrow's double-click, and a sibling of <c>_enterPathEdit</c> in every
    /// respect — same gesture, same reasoning (Q53: reaching into something is
    /// never what a single click does by accident), same shape of hook. The
    /// canvas does not know what type is; it knows a double-click landed here
    /// and asks.
    /// </remarks>
    private Func<double, double, bool>? _enterTextEdit;

    public void SetEnterTextEdit(Func<double, double, bool>? enter) => _enterTextEdit = enter;

    // ---- the gradient tool's axis ----------------------------------------------

    /// <summary>The axis being dragged, in document coordinates, or null when idle.</summary>
    private (double X, double Y)? _gradientFrom, _gradientTo;

    /// <summary>
    /// Show the axis while the VM renders the ramp. View-only chrome, like the
    /// transform gizmo: it is drawn over the composite and never reaches the
    /// document.
    /// </summary>
    public void SetGradientAxis((double X, double Y)? from, (double X, double Y)? to)
    {
        _gradientFrom = from;
        _gradientTo = to;
        InvalidateVisual();
    }

    private (SKPoint From, SKPoint To)? GradientAxisPoints() =>
        _gradientFrom is { } a && _gradientTo is { } b
            ? (new SKPoint((float)a.X, (float)a.Y), new SKPoint((float)b.X, (float)b.Y))
            : null;
}
