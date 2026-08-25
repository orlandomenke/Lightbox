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

    /// <summary>
    /// A press with the text tool in hand.
    /// </summary>
    /// <remarks>
    /// <b>No capture, because there is no drag.</b> A press either starts a
    /// caret or, landing on type already set, picks that up — both of which the
    /// view model decides, because it is the side that knows what is on the
    /// frame. Focus comes here so the keystrokes that follow arrive at the
    /// window rather than at whatever was last clicked.
    /// </remarks>
    private void PlaceText(double x, double y)
    {
        TextPlaced?.Invoke(x, y);
        Focus();
    }

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
