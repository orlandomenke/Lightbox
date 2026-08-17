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

    /// <summary>
    /// Where the panel was docked before it floated, for the redock button.
    /// Distinct from <see cref="HomeSide"/>, which floating overwrites —
    /// this one only ever holds a real strip. Hidden means "never docked".
    /// </summary>
    public DockSide LastDockedSide { get; set; } = DockSide.Hidden;

    public int LastDockedOrder { get; set; }

    /// <summary>Only meaningful while floating.</summary>
    public double FloatX { get; set; }

    public double FloatY { get; set; }

    public double FloatWidth { get; set; } = 320;

    public double FloatHeight { get; set; } = 400;

    /// <summary>
    /// Whether this is the panel showing in its slot.
    /// </summary>
    /// <remarks>
    /// Only means anything when a slot holds more than one panel. Defaults to
    /// true so a layout written before tabs existed — where every panel had a
    /// slot of its own — loads with every panel showing, which is what it meant.
    /// </remarks>
    public bool TabActive { get; set; } = true;

    /// <summary>
    /// Who this panel was tabbed with when it was last hidden. Reopening joins
    /// the first of these still on screen, so the group the artist made
    /// survives a close — the family default only applies when this is empty.
    /// </summary>
    public List<DockPanelId> LastGroupedWith { get; set; } = [];

    public DockPlacement Clone()
    {
        var copy = (DockPlacement)MemberwiseClone();
        // MemberwiseClone shares the list; a shared list means hiding a panel
        // in one workspace edits the saved copy of another.
        copy.LastGroupedWith = [.. LastGroupedWith];
        return copy;
    }
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

    /// <summary>
    /// Rulers along the top and left of the canvas.
    /// </summary>
    /// <remarks>
    /// Off by default. They cost a strip of screen on every side of the canvas
    /// and most drawing never wants them — but they are how a guide is made,
    /// so turning them on is also how the whole feature arrives.
    /// </remarks>
    public bool Rulers { get; set; }

    /// <summary>Whether guides are drawn. Separate from whether they snap.</summary>
    public bool GuidesVisible { get; set; } = true;

    /// <summary>
    /// Whether guides can be dragged on the canvas.
    /// </summary>
    /// <remarks>
    /// Exists because grabbing a guide and drawing along one are the same
    /// gesture in the same place. Locking is how you say which you meant, and
    /// it is why the toggle is one keystroke away rather than buried.
    /// </remarks>
    public bool GuidesLocked { get; set; }

    /// <summary>
    /// Whether references can be picked up and moved on the canvas.
    /// </summary>
    /// <remarks>
    /// The guide lock's argument applied to reference (Q108): grabbing a plate
    /// and drawing over one are the same gesture in the same place, and this is
    /// how an artist says which they meant. Per-reference locking is on the
    /// reference itself and travels with the document; this is the sweep, and it
    /// belongs to the workspace for the reason the guide lock does — it is how
    /// your screen is set up, not a property of the art.
    /// </remarks>
    public bool ReferencesLocked { get; set; }

    /// <summary>
    /// What the Quick options bar carries in this arrangement, as ids from
    /// <see cref="QuickBarCatalog"/>. Null means the layout never chose, and
    /// the bar shows <see cref="QuickBarCatalog.ToolDefaults"/> — nullable so
    /// an untouched workspace writes no key, per the optional-means-absent
    /// rule. Size and opacity are not in here and cannot be: they are pinned
    /// in the bar's fixed section, outside anything a workspace chooses.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? QuickBar { get; set; }

    /// <summary>The bar's contents with the default resolved in.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> QuickBarContents => QuickBar ?? QuickBarCatalog.ToolDefaults;

    /// <summary>The layout the app opens with the first time.</summary>
    public static DockLayout Default()
    {
        var layout = new DockLayout();
        layout.Dock(DockPanelId.Project, DockSide.Right, 0);
        layout.Dock(DockPanelId.Layers, DockSide.Right, 1);
        layout.Dock(DockPanelId.Color, DockSide.Right, 2);
        layout.Dock(DockPanelId.Sheets, DockSide.Right, 3);
        layout.Dock(DockPanelId.Timeline, DockSide.Bottom, 0);
        // The timeline family shares the bottom slot as tabs, track view in
        // front — the reference's Timeline | Xsheet | Graph Editor strip.
        layout.JoinGroup(DockPanelId.Xsheet, DockPanelId.Timeline);
        layout.JoinGroup(DockPanelId.GraphEditor, DockPanelId.Timeline);
        layout.Activate(DockPanelId.Timeline);
        // The colour family shares one slot as tabs, the same shape the
        // built-in workspaces use. These two used to be hidden outright —
        // "empty, they are sidebar height the layers could use" — but a tab
        // costs a word in a header rather than a strip of sidebar, and the
        // owner asked for the tabbed dockers to be the default view. Absent
        // until asked for still governs what a tab SHOWS: an empty palette
        // stays empty until one is made.
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);
        layout.JoinGroup(DockPanelId.Gradient, DockPanelId.Color);
        layout.JoinGroup(DockPanelId.Channels, DockPanelId.Color);
        layout.Activate(DockPanelId.Color);
        layout.Place(DockPanelId.Reference).Side = DockSide.Hidden;
        // Same rule: the gear opens it the first time it is wanted.
        layout.Place(DockPanelId.ToolOptions).Side = DockSide.Hidden;
        // And again: the Window menu opens it the first time it is wanted.
        layout.Place(DockPanelId.History).Side = DockSide.Hidden;
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
    /// The strip's slots, each holding the panels tabbed together in it.
    /// </summary>
    /// <remarks>
    /// A slot is not an object and a group is not declared anywhere: panels
    /// that happen to share an <see cref="DockPlacement.Order"/> are tabbed
    /// together, and a slot of one is an ordinary docker. Drag the last member
    /// out and the group stops existing, with nothing to clean up.
    /// </remarks>
    public List<List<DockPanelId>> SlotsIn(DockSide side) =>
        Placements
            .Where(p => p.Value.Side == side)
            .GroupBy(p => p.Value.Order)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(p => (int)p.Key).Select(p => p.Key).ToList())
            .ToList();

    /// <summary>The panel showing in each slot — one per slot, in strip order.</summary>
    public List<DockPanelId> ActiveIn(DockSide side) =>
        SlotsIn(side).Select(ActiveOf).ToList();

    /// <summary>Which member of a slot is showing.</summary>
    /// <remarks>
    /// Falls back to the first rather than trusting the flag. A layout can
    /// arrive from disk with none of a slot's members marked active, or with
    /// two, and a panel that renders nothing is a worse answer than a panel
    /// that renders the wrong tab.
    /// </remarks>
    public DockPanelId ActiveOf(IReadOnlyList<DockPanelId> slot) =>
        slot.FirstOrDefault(id => Place(id).TabActive, slot[0]);

    /// <summary>The slot a panel shares, itself included.</summary>
    public List<DockPanelId> SlotOf(DockPanelId id)
    {
        var placement = Place(id);
        if (placement.Side is DockSide.Hidden or DockSide.Floating) return [id];
        return SlotsIn(placement.Side).FirstOrDefault(s => s.Contains(id)) ?? [id];
    }

    /// <summary>
    /// Set a slot's height, on every panel in it.
    /// </summary>
    /// <remarks>
    /// Extent is stored per panel, which was right when a panel was a slot. With
    /// tabs the members have to agree, or switching tab resizes the slot to
    /// whatever height that panel was last given on its own — the layout
    /// twitching every time you look at another tab.
    /// </remarks>
    public void SetExtent(DockPanelId id, double extent)
    {
        foreach (var member in SlotOf(id)) Place(member).Extent = extent;
    }

    /// <summary>Show this panel in its slot, and stop showing its siblings.</summary>
    public void Activate(DockPanelId id)
    {
        foreach (var sibling in SlotOf(id)) Place(sibling).TabActive = sibling == id;
    }

    /// <summary>
    /// Tab a panel together with another, taking the target's slot.
    /// </summary>
    /// <remarks>
    /// The joiner becomes the one showing, because the artist just dragged it
    /// there — landing it behind the panel it was dropped on would look like
    /// the drop had failed. It also takes the slot's extent, so the group does
    /// not resize when the tab changes.
    /// </remarks>
    public void JoinGroup(DockPanelId id, DockPanelId target)
    {
        if (id == target) return;
        var into = Place(target);
        if (into.Side is DockSide.Hidden or DockSide.Floating) return;
        if (!DockPanels.Of(id).Movable) return;

        var placement = Place(id);
        var from = placement.Side;
        placement.Side = into.Side;
        placement.HomeSide = into.Side;
        placement.Order = into.Order;
        placement.Extent = into.Extent;
        Activate(id);
        if (from != into.Side) Renumber(SlotsIn(from));
    }

    /// <summary>
    /// True when a strip has nothing in it. The view collapses those rather
    /// than leaving an empty gutter — dragging the last panel out of an area
    /// takes the area with it.
    /// </summary>
    public bool IsEmpty(DockSide side) => !Placements.Any(p => p.Value.Side == side);

    /// <summary>The width a side strip should be, from the panels showing in it.</summary>
    /// <remarks>
    /// The panels <em>showing</em>, not every panel the strip holds. An
    /// uncapped panel removes the ceiling for the whole strip, so counting
    /// hidden tabs would let a tucked-away Layers tab widen the sidebar for a
    /// Colour panel that wants to stay narrow — a strip sized by something
    /// nobody can see.
    /// </remarks>
    public double? CapFor(DockSide side) => DockPanels.CapOf(ActiveIn(side));

    /// <summary>
    /// Put a panel in a strip at <paramref name="index"/>, pulling it out of
    /// wherever it was. Indices are renumbered so a strip is always 0..n-1
    /// with no gaps — the one bookkeeping rule everything else relies on.
    /// </summary>
    /// <summary>The most slots a side may hold. A drop that would open a fifth
    /// strip tabs into the nearest slot instead — the cap holds and nothing is
    /// refused, so the panel always lands where the artist can see it.</summary>
    public const int MaxSlotsPerSide = 4;

    public void Dock(DockPanelId id, DockSide side, int index)
    {
        if (side is DockSide.Floating or DockSide.Hidden)
        {
            throw new ArgumentException($"{side} is not a strip; use Float or Hide.", nameof(side));
        }
        var placement = Place(id);
        var from = placement.Side;
        // Out of whatever slot it was in first, so a group it is leaving closes
        // up behind it and cannot be counted twice by the insert below.
        placement.Side = DockSide.Hidden;
        var slots = SlotsIn(side);

        if (slots.Count >= MaxSlotsPerSide)
        {
            // The side is full: join the nearest slot as a tab rather than
            // opening a fifth strip. Inlined rather than JoinGroup, which
            // refuses unmovable panels — the timeline still has to be able to
            // land on a full bottom edge.
            var target = Place(slots[Math.Clamp(index, 0, slots.Count - 1)][0]);
            placement.Side = side;
            placement.HomeSide = side;
            placement.Order = target.Order;
            placement.Extent = target.Extent;
            Activate(id);
            Renumber(SlotsIn(side));
            if (from != side && from is not (DockSide.Floating or DockSide.Hidden)) Renumber(SlotsIn(from));
            return;
        }

        placement.Side = side;
        placement.HomeSide = side;
        placement.TabActive = true;       // a slot of its own, so it is showing
        index = Math.Clamp(index, 0, slots.Count);
        slots.Insert(index, [id]);
        Renumber(slots);
        if (from != side) Renumber(SlotsIn(from));
    }

    /// <summary>Detach into its own window at the given screen rectangle.</summary>
    public void Float(DockPanelId id, double x, double y, double width, double height)
    {
        var placement = Place(id);
        var from = placement.Side;
        // Remember where it came from, so the redock button has an answer.
        if (from is not (DockSide.Floating or DockSide.Hidden))
        {
            placement.LastDockedSide = from;
            placement.LastDockedOrder = placement.Order;
        }
        placement.Side = DockSide.Floating;
        placement.HomeSide = DockSide.Floating;
        placement.FloatX = x;
        placement.FloatY = y;
        placement.FloatWidth = width;
        placement.FloatHeight = height;
        if (from is not (DockSide.Floating or DockSide.Hidden)) Renumber(SlotsIn(from));
    }

    /// <summary>
    /// Put a floating panel back where it was docked before it floated, or on
    /// the right if it has only ever floated. The inverse of the float button.
    /// </summary>
    public void Redock(DockPanelId id)
    {
        var placement = Place(id);
        if (placement.Side != DockSide.Floating) return;
        var side = placement.LastDockedSide is DockSide.Left or DockSide.Right or DockSide.Top or DockSide.Bottom
            ? placement.LastDockedSide
            : DockSide.Right;
        Dock(id, side, placement.LastDockedOrder);
    }

    /// <summary>Take a panel off screen. Its size and order are kept for reopening.</summary>
    public void Hide(DockPanelId id)
    {
        var placement = Place(id);
        var from = placement.Side;
        // Remember who it was tabbed with, so reopening can put it back in
        // that group — the group the artist made outranks the family default.
        if (from is not (DockSide.Floating or DockSide.Hidden))
        {
            placement.LastGroupedWith = SlotOf(id).Where(m => m != id).ToList();
        }
        placement.Side = DockSide.Hidden;
        if (from is not (DockSide.Floating or DockSide.Hidden)) Renumber(SlotsIn(from));
    }

    /// <summary>
    /// Put a hidden panel back on screen. In order of who has the strongest
    /// claim: the group it was closed out of, its family's slot, the place it
    /// last was, the fallback side.
    /// </summary>
    /// <remarks>
    /// The family rule is the owner's: "unopened or closed tabs always open in
    /// [their] tab group unless in the current session grouped with other
    /// dockers, or the workspace is saved like that." A panel whose whole
    /// family is closed opens alone — the family finds it when a member opens
    /// later, rather than three panels appearing that nobody asked for.
    /// </remarks>
    public void Show(DockPanelId id, DockSide fallback = DockSide.Right)
    {
        if (IsVisible(id)) return;
        var placement = Place(id);
        if (placement.HomeSide == DockSide.Floating)
        {
            Float(id, placement.FloatX, placement.FloatY, placement.FloatWidth, placement.FloatHeight);
            return;
        }
        // The join branches are for movable panels only. JoinGroup refuses an
        // unmovable joiner and says nothing — so routing the timeline through
        // it left the panel hidden with its toggle ticked.
        if (DockPanels.Of(id).Movable)
        {
            if (FirstDocked(placement.LastGroupedWith) is { } mate)
            {
                JoinGroup(id, mate);
                return;
            }
            // Family only for a panel that has never been placed: one the
            // artist parked somewhere on purpose — solo included — goes back
            // there.
            if (placement.HomeSide == DockSide.Hidden &&
                FirstDocked(DockPanels.FamilyOf(id)) is { } kin)
            {
                JoinGroup(id, kin);
                return;
            }
        }
        var side = placement.HomeSide == DockSide.Hidden ? fallback : placement.HomeSide;
        Dock(id, side, placement.Order);
    }

    /// <summary>The first of these panels that is docked in a strip, if any.</summary>
    private DockPanelId? FirstDocked(IEnumerable<DockPanelId>? ids)
    {
        foreach (var id in ids ?? [])
        {
            if (Place(id).Side is not (DockSide.Hidden or DockSide.Floating)) return id;
        }
        return null;
    }

    // Swap is gone. It was the switcher's mechanism — "this slot shows that
    // panel instead, and the displaced one goes where it came from" — and with
    // tabs a slot shows several panels, so trading places is no longer what
    // choosing another panel means. JoinGroup and Activate replace it.

    /// <summary>
    /// Make a strip's slots 0..n-1 with no gaps — the one bookkeeping rule
    /// everything else relies on. Every member of a slot gets that slot's
    /// number, which is what makes them a group.
    /// </summary>
    private void Renumber(IReadOnlyList<IReadOnlyList<DockPanelId>> slots)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            foreach (var id in slots[i]) Place(id).Order = i;
        }
        foreach (var slot in slots)
        {
            // Exactly one showing per slot, always. Two actives renders the
            // wrong one; none renders nothing at all.
            var active = ActiveOf(slot);
            foreach (var id in slot) Place(id).TabActive = id == active;
        }
    }

    /// <summary>A copy that shares nothing with the original.</summary>
    /// <remarks>
    /// <b>Every field, and the three at the bottom are why this has a comment.</b>
    /// <c>Rulers</c>, <c>GuidesVisible</c> and <c>GuidesLocked</c> were missing,
    /// and both <c>WorkspaceViewModel.Apply</c> and <c>WorkspaceStore.Save</c>
    /// go through here — so switching workspace or saving one silently reset
    /// all three to their defaults. Nothing failed; the settings just went back
    /// to how they started, which reads as the application forgetting rather
    /// than as a bug with a place to look.
    ///
    /// A field added to this type and not added here has that same failure
    /// waiting for it, and the compiler will not say a word.
    /// </remarks>
    public DockLayout Clone() => new()
    {
        Placements = Placements.ToDictionary(p => p.Key, p => p.Value.Clone()),
        AreaExtents = new Dictionary<DockSide, double>(AreaExtents),
        Overlays = Overlays.Clone(),
        Rulers = Rulers,
        GuidesVisible = GuidesVisible,
        GuidesLocked = GuidesLocked,
        ReferencesLocked = ReferencesLocked,
        QuickBar = QuickBar?.ToList(),
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
