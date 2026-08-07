namespace Lightbox.App.Docking;

/// <summary>A rectangle in window coordinates. Plain on purpose — see <see cref="DockZones"/>.</summary>
public readonly record struct DockRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    public DockRect Deflate(double by) =>
        new(X + by, Y + by, Math.Max(0, Width - 2 * by), Math.Max(0, Height - 2 * by));
}

/// <summary>Where one docked panel currently is on screen.</summary>
/// <param name="HeaderHeight">
/// How deep the panel's header strip is. Carried because dropping onto a
/// header means something different from dropping onto the panel — the header
/// tabs, the body inserts — and the arithmetic cannot know a header exists
/// otherwise.
/// </param>
public readonly record struct PanelSlot(
    DockPanelId Id, DockSide Side, int Order, DockRect Bounds, double HeaderHeight = 0)
{
    /// <summary>The header band, which is the tab target.</summary>
    public DockRect Header => new(Bounds.X, Bounds.Y, Bounds.Width, Math.Min(HeaderHeight, Bounds.Height));
}

/// <summary>
/// The answer to "if I let go here, what happens" — the strip, the position
/// in it, and the rectangle to light up while the pointer is still down.
/// </summary>
public readonly record struct DropTarget(DockSide Side, int Index, DockRect Preview)
{
    /// <summary>
    /// True when the drop opens an area that currently has nothing in it. The
    /// view animates those wider, because the highlight has to stand in for a
    /// strip that is not there to see.
    /// </summary>
    public bool OpensArea { get; init; }

    /// <summary>
    /// Set when the drop tabs the panel into an existing slot rather than
    /// making a new one. Names the panel whose slot to join.
    /// </summary>
    /// <remarks>
    /// A separate field rather than a magic <see cref="Index"/> value, because
    /// the two are genuinely different operations — one inserts a slot and
    /// shifts everything after it, the other changes nobody's position. A
    /// sentinel index would have made every caller remember which.
    /// </remarks>
    public DockPanelId? IntoGroupOf { get; init; }
}

/// <summary>
/// Turns a pointer position into a drop target.
/// </summary>
/// <remarks>
/// Deliberately free of Avalonia. Drop resolution is fiddly arithmetic with a
/// lot of cases — four edges, empty versus occupied, before versus after each
/// neighbour, the panel being dragged out of the very strip it is over — and
/// every one of those is a question about numbers, not about windows. Keeping
/// it here means the cases are covered by tests that run in milliseconds
/// rather than by dragging things around by hand.
/// </remarks>
public static class DockZones
{
    /// <summary>
    /// How close to an edge counts as aiming at it. Wide enough to hit without
    /// care, narrow enough that crossing the canvas does not keep lighting
    /// areas up.
    /// </summary>
    public const double EdgeBand = 56;

    public static DropTarget? Resolve(
        double x, double y,
        DockRect content,
        IReadOnlyList<PanelSlot> slots,
        DockPanelId dragged,
        DockLayout layout)
    {
        if (!DockPanels.Of(dragged).Movable) return null;
        if (!content.Contains(x, y)) return null;

        // Over an existing panel: the question is which of its neighbours the
        // dragged panel lands between, so the strip's own axis decides.
        foreach (var slot in slots)
        {
            if (!slot.Bounds.Contains(x, y)) continue;
            if (slot.Id == dragged && CountIn(slots, slot.Side) == 1) return null; // nowhere to go
            return Beside(slot, x, y, dragged, slots);
        }

        // Not over a panel: the edges of the free space are the offer.
        var side = NearestEdge(x, y, content);
        if (side is null) return null;

        var existing = CountIn(slots, side.Value);
        if (existing > 0)
        {
            // The strip is there but the pointer missed its panels (the gap
            // under a short stack). Append.
            return new DropTarget(side.Value, existing, StripPreview(side.Value, content, dragged))
            {
                OpensArea = false,
            };
        }
        return new DropTarget(side.Value, 0, StripPreview(side.Value, content, dragged))
        {
            OpensArea = true,
        };
    }

    private static int CountIn(IReadOnlyList<PanelSlot> slots, DockSide side)
    {
        var n = 0;
        foreach (var s in slots)
        {
            if (s.Side == side) n++;
        }
        return n;
    }

    /// <summary>Insert before or after the panel under the pointer.</summary>
    private static DropTarget Beside(
        PanelSlot slot, double x, double y, DockPanelId dragged, IReadOnlyList<PanelSlot> slots)
    {
        // The header first, because it is inside the upper half and would
        // otherwise never be reachable: aiming at a header would insert the
        // panel above the one whose tabs you were aiming for.
        if (slot.HeaderHeight > 0 && slot.Header.Contains(x, y) && slot.Id != dragged)
        {
            return new DropTarget(slot.Side, slot.Order, slot.Header) { IntoGroupOf = slot.Id };
        }

        var vertical = slot.Side is DockSide.Left or DockSide.Right;
        var along = vertical ? y - slot.Bounds.Y : x - slot.Bounds.X;
        var span = vertical ? slot.Bounds.Height : slot.Bounds.Width;
        var after = along > span / 2;

        var index = slot.Order + (after ? 1 : 0);
        // Dropping a panel back where it already is: taking it out shifts
        // everything after it up by one, so the index has to come with it or
        // the panel walks one place forward on every no-op drag.
        var draggedSlot = slots.FirstOrDefault(s => s.Id == dragged);
        if (draggedSlot.Side == slot.Side && draggedSlot.Order < index) index--;

        // The preview is a band at the boundary, as deep as the dragged panel
        // wants, so the neighbour visibly makes room rather than the highlight
        // hovering over it.
        var want = Math.Min(DockPanels.Of(dragged).DefaultExtent, span / 2);
        var preview = vertical
            ? new DockRect(slot.Bounds.X, after ? slot.Bounds.Bottom - want : slot.Bounds.Y, slot.Bounds.Width, want)
            : new DockRect(after ? slot.Bounds.Right - want : slot.Bounds.X, slot.Bounds.Y, want, slot.Bounds.Height);
        return new DropTarget(slot.Side, index, preview);
    }

    /// <summary>The whole strip, at the width or height the panel would open at.</summary>
    private static DockRect StripPreview(DockSide side, DockRect content, DockPanelId dragged)
    {
        var info = DockPanels.Of(dragged);
        return side switch
        {
            DockSide.Left => new DockRect(content.X, content.Y, Across(info), content.Height),
            DockSide.Right => new DockRect(content.Right - Across(info), content.Y, Across(info), content.Height),
            DockSide.Top => new DockRect(content.X, content.Y, content.Width, info.DefaultExtent),
            _ => new DockRect(content.X, content.Bottom - info.DefaultExtent, content.Width, info.DefaultExtent),
        };

        // Across a side strip a panel is as wide as its cap, or a sensible
        // sidebar width when it has none — an uncapped panel wants the space
        // but not the whole window.
        static double Across(DockPanelInfo info) => info.MaxExtent ?? 300;
    }

    private static DockSide? NearestEdge(double x, double y, DockRect content)
    {
        var left = x - content.X;
        var right = content.Right - x;
        var top = y - content.Y;
        var bottom = content.Bottom - y;
        var best = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (best > EdgeBand) return null;
        if (best == left) return DockSide.Left;
        if (best == right) return DockSide.Right;
        if (best == top) return DockSide.Top;
        return DockSide.Bottom;
    }
}
