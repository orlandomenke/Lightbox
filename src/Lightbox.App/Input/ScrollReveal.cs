namespace Lightbox.App.Input;

/// <summary>
/// How far a list has to scroll to show one of its rows: as little as
/// possible, and not at all when the row is already showing.
/// </summary>
/// <remarks>
/// On its own, without a control in sight, because the cases worth getting
/// right are arithmetic — a row half off one edge, a row taller than the view,
/// a view that has not been laid out yet — and a window is a slow and
/// unreliable way to ask about arithmetic.
/// </remarks>
public static class ScrollReveal
{
    /// <param name="offset">Where the list is scrolled to now.</param>
    /// <param name="viewport">How much of the list shows.</param>
    /// <param name="top">The row's top, measured in the list's content — the same space as <paramref name="offset"/>.</param>
    /// <param name="bottom">The row's bottom, in that space.</param>
    /// <returns>The offset to scroll to. Not clamped to the list's extent; the caller knows it.</returns>
    public static double Offset(double offset, double viewport, double top, double bottom)
    {
        // Not laid out yet: there is no view to bring anything into.
        if (!(viewport > 0)) return offset;
        // Taller than the view, so it cannot all show: its start is the part to see.
        if (bottom - top >= viewport) return top;
        if (top < offset) return top;
        if (bottom > offset + viewport) return bottom - viewport;
        return offset;
    }
}
