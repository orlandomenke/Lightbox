using Avalonia;

namespace Lightbox.App.Input;

/// <summary>
/// The arithmetic behind dragging a folder's mark along its X-sheet row
/// (Q227): which frame the pointer is over, and whether a press has travelled
/// far enough to be a drag.
/// </summary>
/// <remarks>
/// Pulled out of the row's code-behind for <see cref="CelDragGesture"/>'s
/// reason: the handlers around it are only reachable through synthetic input,
/// which is unreliable here, so what can be asserted is the rule they apply.
/// </remarks>
public static class FolderSummaryDrag
{
    /// <summary>
    /// The frame a pointer is over, <paramref name="x"/> pixels along a strip
    /// of <paramref name="count"/> cells each <paramref name="cellWidth"/> wide
    /// with <paramref name="gap"/> between them. The gap after a cell belongs
    /// to that cell, and a pointer past either end names the end cell — a drag
    /// that overshoots the sheet should land on its edge, not be thrown away.
    /// </summary>
    public static int FrameAt(double x, double cellWidth, double gap, int count)
    {
        if (count <= 0) return 0;
        var pitch = cellWidth + gap;
        if (pitch <= 0) return 0;
        return Math.Clamp((int)Math.Floor(x / pitch), 0, count - 1);
    }

    /// <summary>Whether a press has moved far enough to be a drag — the cel drag's threshold.</summary>
    public static bool IsDrag(Point from, Point to)
    {
        var delta = to - from;
        return Math.Abs(delta.X) >= CelDragGesture.ThresholdPx || Math.Abs(delta.Y) >= CelDragGesture.ThresholdPx;
    }
}
