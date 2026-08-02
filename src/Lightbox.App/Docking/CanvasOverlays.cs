namespace Lightbox.App.Docking;

/// <summary>
/// The floating bars that sit on the canvas rather than in a strip.
/// </summary>
/// <remarks>
/// These are not dockers and must not become them. A docker takes space away
/// from the drawing; an overlay bar sits on top of it, which is the right
/// trade for a handful of controls you reach for constantly and read at a
/// glance. They are listed separately in the View menu for the same reason.
/// </remarks>
public enum OverlayId
{
    /// <summary>Zoom, rotate, mirror, reset — how you are looking at the canvas.</summary>
    View,

    /// <summary>Onion skin, through-camera, play/pause — what you are looking at.</summary>
    Shortcuts,
}

public enum CanvasEdge
{
    Top,
    Right,
    Bottom,
    Left,
}

/// <summary>Where one overlay bar sits, and what state it is in.</summary>
public sealed class OverlayPlacement
{
    public CanvasEdge Edge { get; set; } = CanvasEdge.Top;

    /// <summary>
    /// How far along that edge, 0 to 1 — left to right on a horizontal edge,
    /// top to bottom on a vertical one.
    /// </summary>
    /// <remarks>
    /// A fraction rather than pixels so a bar keeps its place when the window
    /// is resized. Pinned to a corner in pixels, a bar ends up in the middle of
    /// the canvas the first time somebody maximises the window.
    /// </remarks>
    public double Along { get; set; } = 1;

    /// <summary>Rolled up to its grip, so it is out of the way but findable.</summary>
    public bool Collapsed { get; set; }

    public bool Visible { get; set; } = true;

    public OverlayPlacement Clone() => (OverlayPlacement)MemberwiseClone();
}

/// <summary>Where every overlay bar sits. Part of the workspace.</summary>
public sealed class CanvasOverlayLayout
{
    public Dictionary<OverlayId, OverlayPlacement> Placements { get; set; } = [];

    /// <summary>View top-right, shortcuts down the right-hand edge.</summary>
    public static CanvasOverlayLayout Default()
    {
        var layout = new CanvasOverlayLayout();
        layout.Place(OverlayId.View).Edge = CanvasEdge.Top;
        layout.Place(OverlayId.View).Along = 1;
        layout.Place(OverlayId.Shortcuts).Edge = CanvasEdge.Right;
        layout.Place(OverlayId.Shortcuts).Along = 0.5;
        return layout;
    }

    public OverlayPlacement Place(OverlayId id)
    {
        if (!Placements.TryGetValue(id, out var placement))
        {
            Placements[id] = placement = new OverlayPlacement();
        }
        return placement;
    }

    public bool IsVisible(OverlayId id) => Place(id).Visible;

    public CanvasOverlayLayout Clone()
    {
        var copy = new CanvasOverlayLayout();
        foreach (var (id, placement) in Placements) copy.Placements[id] = placement.Clone();
        return copy;
    }

    /// <summary>A bar on a left or right edge runs vertically.</summary>
    public static bool IsVertical(CanvasEdge edge) => edge is CanvasEdge.Left or CanvasEdge.Right;

    /// <summary>
    /// Which edge a point is nearest, for resolving a drop.
    /// </summary>
    /// <remarks>
    /// Straight nearest-distance, which means the canvas divides into four
    /// triangles meeting at the centre. That is what makes dropping predictable:
    /// the answer only depends on where the pointer is, not on which edge the
    /// bar started from.
    /// </remarks>
    public static CanvasEdge NearestEdge(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return CanvasEdge.Top;
        var left = x;
        var right = width - x;
        var top = y;
        var bottom = height - y;

        var best = CanvasEdge.Left;
        var distance = left;
        if (right < distance) (best, distance) = (CanvasEdge.Right, right);
        if (top < distance) (best, distance) = (CanvasEdge.Top, top);
        if (bottom < distance) best = CanvasEdge.Bottom;
        return best;
    }

    /// <summary>How far along <paramref name="edge"/> a point sits, clamped to 0–1.</summary>
    public static double AlongFor(CanvasEdge edge, double x, double y, double width, double height)
    {
        var value = IsVertical(edge)
            ? (height <= 0 ? 0 : y / height)
            : (width <= 0 ? 0 : x / width);
        return Math.Clamp(value, 0, 1);
    }
}
