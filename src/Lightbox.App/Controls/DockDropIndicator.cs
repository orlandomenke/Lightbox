using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lightbox.App.Docking;

namespace Lightbox.App.Controls;

/// <summary>
/// The highlight shown while a panel is being dragged: where it would land,
/// drawn at the size it would land at.
/// </summary>
/// <remarks>
/// Drawn rather than composed from controls because it sits over everything
/// and must not take a single pointer event — a hit-testable overlay would
/// swallow the very moves it exists to follow.
///
/// It grows into place instead of appearing. Opening an edge that has nothing
/// in it is the case with no landmarks — there is no strip to see the panel
/// join — so the highlight has to be the strip, and a rectangle that expands
/// to that size reads as "this area is about to open" in a way a static box
/// does not.
/// </remarks>
public class DockDropIndicator : Control
{
    private DropTarget? _target;
    private Rect _drawn;

    public DockDropIndicator()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Show a target, or null to clear. Safe to call on every pointer move.</summary>
    public void Show(DropTarget? target)
    {
        var wasOpen = _target.HasValue;
        var same = _target.HasValue && target.HasValue
                   && _target.Value.Side == target.Value.Side
                   && _target.Value.Index == target.Value.Index
                   && _target.Value.Preview.Equals(target.Value.Preview);
        if (same) return;

        _target = target;
        if (target is not { } t)
        {
            _drawn = default;
            InvalidateVisual();
            return;
        }

        var full = new Rect(t.Preview.X, t.Preview.Y, t.Preview.Width, t.Preview.Height);
        // Only an area that is not there yet needs to be introduced. Moving
        // between two positions in a strip that already exists should snap —
        // an animation there reads as lag, not as feedback.
        _drawn = t.OpensArea && !wasOpen ? Collapsed(t) : full;
        InvalidateVisual();
        if (t.OpensArea && !_drawn.Equals(full)) Grow(full);
    }

    /// <summary>A sliver at the edge the area would open from.</summary>
    private static Rect Collapsed(DropTarget t) => t.Side switch
    {
        DockSide.Left => new Rect(t.Preview.X, t.Preview.Y, 0, t.Preview.Height),
        DockSide.Right => new Rect(t.Preview.Right, t.Preview.Y, 0, t.Preview.Height),
        DockSide.Top => new Rect(t.Preview.X, t.Preview.Y, t.Preview.Width, 0),
        _ => new Rect(t.Preview.X, t.Preview.Bottom, t.Preview.Width, 0),
    };

    private async void Grow(Rect full)
    {
        const int steps = 6;
        var from = _drawn;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(12);
            // A later move superseded this one; abandon rather than fight it.
            if (_target is not { } t || !FullOf(t).Equals(full)) return;
            var k = (double)i / steps;
            _drawn = new Rect(
                Lerp(from.X, full.X, k), Lerp(from.Y, full.Y, k),
                Lerp(from.Width, full.Width, k), Lerp(from.Height, full.Height, k));
            InvalidateVisual();
        }
    }

    private static Rect FullOf(DropTarget t) =>
        new(t.Preview.X, t.Preview.Y, t.Preview.Width, t.Preview.Height);

    private static double Lerp(double a, double b, double k) => a + (b - a) * k;

    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x4a, 0x9e, 0xff));

    private static readonly IPen Edge =
        new Pen(new SolidColorBrush(Color.FromArgb(0xd0, 0x6a, 0xb4, 0xff)), 2);

    public override void Render(DrawingContext context)
    {
        if (_target is null || _drawn.Width <= 0 && _drawn.Height <= 0) return;
        context.DrawRectangle(Fill, Edge, _drawn, 3, 3);
    }
}
