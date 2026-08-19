namespace Lightbox.App.Docking;

/// <summary>A rectangle in window coordinates. Plain on purpose — see <see cref="DockZones"/>.</summary>
public readonly record struct DockRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    public DockRect Deflate(double by) =>
        new(X + by, Y + by, Math.Max(0, Width - 2 * by), Math.Max(0, Height - 2 * by));

    /// <summary>The same rectangle, moved. For lifting a control's own
    /// coordinates into the window's.</summary>
    public DockRect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);
}

/// <summary>One tab in a slot's header, and where it is drawn.</summary>
public readonly record struct PanelTab(DockPanelId Id, DockRect Bounds);

/// <summary>Where one docked panel currently is on screen.</summary>
/// <param name="HeaderHeight">
/// How deep the panel's header strip is. Carried because dropping onto a
/// header means something different from dropping onto the panel — the header
/// tabs, the body inserts — and the arithmetic cannot know a header exists
/// otherwise.
/// </param>
/// <param name="Tabs">
/// The slot's tabs, left to right, measured from the realised header. Needed
/// because "which position in this strip" is a question about where the tabs
/// actually are: their widths are their titles' widths, so nothing can be
/// derived from the count. Null or empty means the caller could not measure
/// them, and a drop then means what it always did — join the group, last.
/// </param>
public readonly record struct PanelSlot(
    DockPanelId Id, DockSide Side, int Order, DockRect Bounds, double HeaderHeight = 0,
    IReadOnlyList<PanelTab>? Tabs = null)
{
    /// <summary>The header band, which is the tab target.</summary>
    public DockRect Header => new(Bounds.X, Bounds.Y, Bounds.Width, Math.Min(HeaderHeight, Bounds.Height));

    /// <summary>The tabs, never null.</summary>
    public IReadOnlyList<PanelTab> Strip => Tabs ?? [];
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
    /// <para>
    /// It can name the dragged panel itself, which is a tab being put back in
    /// the header it came from at another position — the same group, rearranged.
    /// </para>
    /// </remarks>
    public DockPanelId? IntoGroupOf { get; init; }

    /// <summary>
    /// Where in <see cref="IntoGroupOf"/>'s tab strip the panel lands. Null
    /// means last, which is what a drop anywhere but the header means.
    /// </summary>
    public int? TabIndex { get; init; }

    /// <summary>
    /// The insertion mark to draw between two tabs, when the drop is aimed at
    /// a position in a strip rather than only at a panel.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Preview"/> rather than instead of it: the wash says
    /// which panel the tab is joining and the caret says where in its header it
    /// lands, and a rearrangement inside one header needs the second without
    /// the first meaning anything.
    /// </remarks>
    public DockRect? Caret { get; init; }
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
            // Nowhere to go: the panel is alone on its side, so no strip
            // position is different from the one it is in — unless it shares
            // its slot with other tabs, in which case its own header is
            // somewhere to go and the whole of what rearranging tabs means.
            if (slot.Id == dragged && CountIn(slots, slot.Side) == 1 && slot.Strip.Count < 2) return null;
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

    /// <summary>
    /// How deep the insert bands at a panel's first and last edge run. Slim on
    /// purpose: the owner's call is that a panel's body is ONE target — join
    /// its tabs — with the half-panel insert targets gone. The bands remain
    /// only so a stack can still be reordered and a tab group still split.
    /// </summary>
    public const double InsertBand = 18;

    /// <summary>
    /// Join the panel under the pointer, or slip in at one of its edges.
    /// </summary>
    /// <remarks>
    /// The body — full width AND full height, per the owner — means "tab into
    /// this panel", previewed over the whole panel so the offer reads as
    /// becoming part of it. The old layout offered the header for tabbing and
    /// the panel's halves for inserting, and the report was exact about what
    /// that felt like: a dead lower half and a top target that should have
    /// been the entire docker.
    /// </remarks>
    private static DropTarget? Beside(
        PanelSlot slot, double x, double y, DockPanelId dragged, IReadOnlyList<PanelSlot> slots)
    {
        var vertical = slot.Side is DockSide.Left or DockSide.Right;
        var along = vertical ? y - slot.Bounds.Y : x - slot.Bounds.X;
        var span = vertical ? slot.Bounds.Height : slot.Bounds.Width;
        var band = Math.Min(InsertBand, span / 4);

        // The tab strip is the loudest "join" affordance there is, so it wins
        // outright — even over the insert sliver it sits inside. It is also the
        // only place a position in the strip can be aimed at, which is why it
        // resolves further than the body does.
        if (slot.HeaderHeight > 0 && (slot.Header.Contains(x, y) || OverStrip(slot, x, y)))
        {
            return JoinAtTab(slot, x, dragged);
        }
        if (along >= band && along <= span - band)
        {
            return Join(slot, dragged);
        }

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

    /// <summary>
    /// Tab into this panel's slot, previewed over the whole panel so the
    /// offer reads as becoming part of it. Null when the panel is the one
    /// being dragged — nothing can join its own group.
    /// </summary>
    /// <remarks>
    /// No tab position: this is the answer for a drop on the <em>body</em>,
    /// which says which group and nothing about where in it. Landing last is
    /// the only honest reading of a gesture that never went near the strip.
    /// </remarks>
    private static DropTarget? Join(PanelSlot slot, DockPanelId dragged) =>
        slot.Id == dragged
            ? null
            : new DropTarget(slot.Side, slot.Order, slot.Bounds) { IntoGroupOf = slot.Id };

    /// <summary>
    /// Tab into this slot <em>at the position the pointer is between</em> —
    /// which is what makes a header drop able to rearrange a strip rather than
    /// only append to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Its own header is a target here, and everywhere else it is not.</b>
    /// A panel cannot join the group it is already in, so every other route
    /// returns null for it and the release floats it. Over its own tabs the
    /// gesture means something different and ordinary: put this tab down
    /// somewhere else in this strip.
    /// </para>
    /// <para>
    /// <b>Two indices, and they are not the same number.</b> The caret is drawn
    /// where the pointer is aiming — the gap between two tabs as they are drawn
    /// now — while the index the layout is given describes the strip with the
    /// dragged tab already lifted out of it. Drawing the corrected one puts the
    /// mark a whole tab away from the pointer; applying the visual one walks
    /// the tab forward a place on every drag that lets go where it started.
    /// </para>
    /// </remarks>
    private static DropTarget? JoinAtTab(PanelSlot slot, double x, DockPanelId dragged)
    {
        var tabs = slot.Strip;
        // Unmeasured: a header with no tab rectangles in it cannot say where
        // in the strip anything goes, so the drop means what it always did.
        if (tabs.Count == 0) return Join(slot, dragged);

        var caret = InsertionAt(tabs, x);
        var index = caret;
        var lifted = IndexOf(tabs, dragged);
        if (lifted >= 0 && index > lifted) index--;

        // Already a member — not necessarily the member in front. A press
        // selects the tab it lands on, but the layout learns that a dispatcher
        // turn later (see Docker.OnTabPicked), so a quick pull can resolve
        // against a slot whose active panel is still the previous one. Asking
        // whether the dragged panel is IN this group answers that correctly;
        // asking whether it is the group's face reads a rearrangement as a
        // panel joining the group it has never left.
        if (lifted >= 0)
        {
            // A strip of one has no other position to offer, so this stays the
            // no-target case and the release floats the panel, as it always has.
            if (tabs.Count < 2) return null;
            // The header rather than the whole panel: nothing about where the
            // panel sits is changing, and washing the body would say it was.
            return new DropTarget(slot.Side, slot.Order, slot.Header)
            {
                IntoGroupOf = slot.Id,
                TabIndex = index,
                Caret = CaretAt(tabs, caret),
            };
        }

        return new DropTarget(slot.Side, slot.Order, slot.Bounds)
        {
            IntoGroupOf = slot.Id,
            TabIndex = index,
            Caret = CaretAt(tabs, caret),
        };
    }

    /// <summary>Whether the pointer is on the tabs themselves.</summary>
    /// <remarks>
    /// Beside the header band rather than instead of it, because the two
    /// disagree and the measurement says by how much: a tab is laid out to sit
    /// <em>on</em> the header's bottom rule, and its last row of pixels lands
    /// just outside the band the header measured. That row is the bottom edge
    /// of the word an artist is aiming at, and it belongs to the strip — without
    /// this, letting go one pixel low silently means "join, last" instead of the
    /// position under the pointer.
    /// </remarks>
    private static bool OverStrip(PanelSlot slot, double x, double y)
    {
        foreach (var tab in slot.Strip)
        {
            if (tab.Bounds.Contains(x, y)) return true;
        }
        return false;
    }

    /// <summary>Which gap in the strip the pointer is nearest, 0..count.</summary>
    /// <remarks>
    /// Midpoints rather than edges, so the tab under the pointer gives way as
    /// soon as the pointer is past halfway through it. Tab widths are their
    /// titles' widths, which is why this needs the rectangles and cannot be
    /// arithmetic on a count.
    /// </remarks>
    private static int InsertionAt(IReadOnlyList<PanelTab> tabs, double x)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (x < tabs[i].Bounds.X + tabs[i].Bounds.Width / 2) return i;
        }
        return tabs.Count;
    }

    private static int IndexOf(IReadOnlyList<PanelTab> tabs, DockPanelId id)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].Id == id) return i;
        }
        return -1;
    }

    /// <summary>How wide the insertion mark between two tabs is drawn.</summary>
    public const double CaretWidth = 3;

    /// <summary>The mark drawn in the gap the tab would land in.</summary>
    /// <remarks>
    /// Straddling the boundary rather than sitting to one side of it, and as
    /// tall as the tabs themselves rather than as the header: it is a caret
    /// between two words, and it has to read as being between them.
    /// </remarks>
    private static DockRect CaretAt(IReadOnlyList<PanelTab> tabs, int index)
    {
        var anchor = tabs[Math.Min(index, tabs.Count - 1)].Bounds;
        var x = index < tabs.Count ? anchor.X : anchor.Right;
        return new DockRect(x - CaretWidth / 2, anchor.Y, CaretWidth, anchor.Height);
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
