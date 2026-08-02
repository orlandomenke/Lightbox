using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.App.Docking;

/// <summary>Where one panel sits, and how big it is there.</summary>
public sealed class DockPlacement
{
    public DockSide Side { get; set; } = DockSide.Hidden;

    /// <summary>
    /// The last strip this panel was actually in. Closing a panel is meant to
    /// be undoable by reopening it, which needs somewhere to put it back —
    /// and <see cref="Side"/> has been overwritten with Hidden by then.
    /// </summary>
    public DockSide HomeSide { get; set; } = DockSide.Hidden;

    /// <summary>Position within its strip, top to bottom or left to right.</summary>
    public int Order { get; set; }

    /// <summary>Along the strip: height in a side strip, width in a top or bottom one.</summary>
    public double Extent { get; set; }

    /// <summary>Only meaningful while floating.</summary>
    public double FloatX { get; set; }

    public double FloatY { get; set; }

    public double FloatWidth { get; set; } = 320;

    public double FloatHeight { get; set; } = 400;

    public DockPlacement Clone() => (DockPlacement)MemberwiseClone();
}

/// <summary>
/// Which panels are on screen and where — the whole of a workspace.
/// </summary>
/// <remarks>
/// This is deliberately a plain model with no Avalonia in it. Docking is the
/// kind of feature where the interesting mistakes are in the bookkeeping —
/// an order that leaves a gap, a side that keeps a strip open with nothing in
/// it, a swap that loses a panel — and none of those need a window to catch.
/// The view reads this and reparents controls to match; it never decides
/// anything.
///
/// One panel is in exactly one place. There is no tabbing and no stacking:
/// the panel switcher in each header swaps two panels rather than piling them
/// up, so "where is the palette" always has one answer.
/// </remarks>
public sealed class DockLayout
{
    public Dictionary<DockPanelId, DockPlacement> Placements { get; set; } = [];

    /// <summary>Across the strip: the sidebar's width, the bottom area's height.</summary>
    public Dictionary<DockSide, double> AreaExtents { get; set; } = [];

    /// <summary>
    /// The bars that sit on the canvas rather than in a strip.
    /// </summary>
    /// <remarks>
    /// Here because they are the same kind of thing a dock layout already is —
    /// an arrangement of the screen, belonging to the person rather than the
    /// artwork — so they save, reset and switch with the workspace exactly as
    /// the panels do.
    /// </remarks>
    public CanvasOverlayLayout Overlays { get; set; } = CanvasOverlayLayout.Default();

    /// <summary>The layout the app opens with the first time.</summary>
    public static DockLayout Default()
    {
        var layout = new DockLayout();
        layout.Dock(DockPanelId.Project, DockSide.Right, 0);
        layout.Dock(DockPanelId.Layers, DockSide.Right, 1);
        layout.Dock(DockPanelId.Color, DockSide.Right, 2);
        layout.Dock(DockPanelId.Sheets, DockSide.Right, 3);
        layout.Dock(DockPanelId.Timeline, DockSide.Bottom, 0);
        // Absent until asked for, the same rule the camera and the project
        // follow. A palette and a gradient are things an artist sets up
        // deliberately; empty, they are sidebar height the layers could use.
        layout.Place(DockPanelId.Palette).Side = DockSide.Hidden;
        layout.Place(DockPanelId.Gradient).Side = DockSide.Hidden;
        layout.Place(DockPanelId.Reference).Side = DockSide.Hidden;
        layout.AreaExtents[DockSide.Right] = 300;
        layout.AreaExtents[DockSide.Bottom] = 280;
        return layout;
    }

    /// <summary>The placement record for a panel, created on first mention.</summary>
    public DockPlacement Place(DockPanelId id)
    {
        if (!Placements.TryGetValue(id, out var placement))
        {
            Placements[id] = placement = new DockPlacement
            {
                Extent = DockPanels.Of(id).DefaultExtent,
            };
        }
        return placement;
    }

    public DockSide SideOf(DockPanelId id) => Place(id).Side;

    public bool IsVisible(DockPanelId id) => Place(id).Side != DockSide.Hidden;

    /// <summary>The panels in one strip, in the order they are drawn.</summary>
    public List<DockPanelId> PanelsIn(DockSide side) =>
        Placements
            .Where(p => p.Value.Side == side)
            .OrderBy(p => p.Value.Order)
            .ThenBy(p => (int)p.Key)
            .Select(p => p.Key)
            .ToList();

    /// <summary>
    /// True when a strip has nothing in it. The view collapses those rather
    /// than leaving an empty gutter — dragging the last panel out of an area
    /// takes the area with it.
    /// </summary>
    public bool IsEmpty(DockSide side) => !Placements.Any(p => p.Value.Side == side);

    /// <summary>The width a side strip should be, from the panels the layout puts in it.</summary>
    public double? CapFor(DockSide side) => DockPanels.CapOf(PanelsIn(side));

    /// <summary>
    /// Put a panel in a strip at <paramref name="index"/>, pulling it out of
    /// wherever it was. Indices are renumbered so a strip is always 0..n-1
    /// with no gaps — the one bookkeeping rule everything else relies on.
    /// </summary>
    public void Dock(DockPanelId id, DockSide side, int index)
    {
        if (side is DockSide.Floating or DockSide.Hidden)
        {
            throw new ArgumentException($"{side} is not a strip; use Float or Hide.", nameof(side));
        }
        var placement = Place(id);
        var from = placement.Side;
        placement.Side = side;
        placement.HomeSide = side;
        placement.Order = int.MinValue;   // sorts first, then the insert puts it right
        var order = PanelsIn(side).Where(p => p != id).ToList();
        index = Math.Clamp(index, 0, order.Count);
        order.Insert(index, id);
        Renumber(order);
        if (from != side) Renumber(PanelsIn(from));
    }

    /// <summary>Detach into its own window at the given screen rectangle.</summary>
    public void Float(DockPanelId id, double x, double y, double width, double height)
    {
        var placement = Place(id);
        var from = placement.Side;
        placement.Side = DockSide.Floating;
        placement.HomeSide = DockSide.Floating;
        placement.FloatX = x;
        placement.FloatY = y;
        placement.FloatWidth = width;
        placement.FloatHeight = height;
        if (from is not (DockSide.Floating or DockSide.Hidden)) Renumber(PanelsIn(from));
    }

    /// <summary>Take a panel off screen. Its size and order are kept for reopening.</summary>
    public void Hide(DockPanelId id)
    {
        var placement = Place(id);
        var from = placement.Side;
        placement.Side = DockSide.Hidden;
        if (from is not (DockSide.Floating or DockSide.Hidden)) Renumber(PanelsIn(from));
    }

    /// <summary>
    /// Put a hidden panel back where it last was, or on the right if it has
    /// never been anywhere.
    /// </summary>
    public void Show(DockPanelId id, DockSide fallback = DockSide.Right)
    {
        if (IsVisible(id)) return;
        var placement = Place(id);
        if (placement.HomeSide == DockSide.Floating)
        {
            Float(id, placement.FloatX, placement.FloatY, placement.FloatWidth, placement.FloatHeight);
            return;
        }
        var side = placement.HomeSide == DockSide.Hidden ? fallback : placement.HomeSide;
        Dock(id, side, placement.Order);
    }

    /// <summary>
    /// Exchange two panels' positions — the header's panel switcher.
    /// </summary>
    /// <remarks>
    /// Blender's rule: a header picks what that slot shows, and no panel is
    /// ever open twice. Choosing "Palette" from the colour docker's switcher
    /// therefore has to send the colour docker where the palette was, not
    /// merely open a second palette. A swap with a hidden panel is the same
    /// operation with one side off screen: the chosen panel takes the slot and
    /// the displaced one goes where the chosen one came from, which is nowhere.
    /// </remarks>
    public void Swap(DockPanelId a, DockPanelId b)
    {
        if (a == b) return;
        var pa = Place(a);
        var pb = Place(b);
        (pa.Side, pb.Side) = (pb.Side, pa.Side);
        (pa.HomeSide, pb.HomeSide) = (pb.HomeSide, pa.HomeSide);
        (pa.Order, pb.Order) = (pb.Order, pa.Order);
        (pa.FloatX, pb.FloatX) = (pb.FloatX, pa.FloatX);
        (pa.FloatY, pb.FloatY) = (pb.FloatY, pa.FloatY);
        (pa.FloatWidth, pb.FloatWidth) = (pb.FloatWidth, pa.FloatWidth);
        (pa.FloatHeight, pb.FloatHeight) = (pb.FloatHeight, pa.FloatHeight);
    }

    private void Renumber(IReadOnlyList<DockPanelId> order)
    {
        for (var i = 0; i < order.Count; i++) Place(order[i]).Order = i;
    }

    public DockLayout Clone() => new()
    {
        Placements = Placements.ToDictionary(p => p.Key, p => p.Value.Clone()),
        AreaExtents = new Dictionary<DockSide, double>(AreaExtents),
        Overlays = Overlays.Clone(),
    };

    // ---- persistence ---------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // Enum-keyed dictionaries need the same converter on the key side.
        DictionaryKeyPolicy = null,
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Read a saved layout, falling back to the default rather than throwing.
    /// A workspace file is a convenience; a corrupt one must not stop the app
    /// from opening.
    /// </summary>
    public static DockLayout Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DockLayout>(json, Json) ?? Default();
        }
        catch (JsonException)
        {
            return Default();
        }
    }
}
