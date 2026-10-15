namespace Lightbox.App.Docking;

/// <summary>Where a panel listed in a header menu stands relative to that header.</summary>
public enum HeaderMenuPanelState
{
    /// <summary>Already a tab in this header. Picking it brings it forward.</summary>
    InThisGroup,

    /// <summary>Open in another group or a window of its own. Picking it moves it here.</summary>
    Elsewhere,

    /// <summary>Closed. Picking it opens it here.</summary>
    Closed,
}

/// <summary>One panel in a header menu.</summary>
/// <param name="Where">
/// For a panel open elsewhere, where that is, in the words the menu prints.
/// Carried because picking it <em>moves</em> it (Q276), and a move the menu
/// did not warn about reads as the panel vanishing from where it was.
/// </param>
public sealed record HeaderMenuPanel(DockPanelId Id, string Title, HeaderMenuPanelState State, string? Where);

/// <summary>
/// What a docker header's right-click menu offers: every panel, and what
/// picking each one would do to this header (Q276).
/// </summary>
/// <remarks>
/// Free of Avalonia for the reason <see cref="DockZones"/> is: which panels are
/// offered, and how each is described, is a question about the layout, and it is
/// tested as one. The window turns this into menu items.
/// </remarks>
public static class DockHeaderMenu
{
    /// <summary>
    /// The panels to list on <paramref name="target"/>'s header: this group's,
    /// then the ones open elsewhere, then the closed ones.
    /// </summary>
    /// <param name="usable">
    /// Whether the open document can show a panel at all — the project tree
    /// without a project cannot. The window knows; the layout does not.
    /// </param>
    /// <returns>
    /// Empty for a floating header: a float holds one panel (Q278 left floating
    /// groups unbuilt), so a tab has nowhere to go.
    /// </returns>
    public static IReadOnlyList<HeaderMenuPanel> Panels(
        DockLayout layout, DockPanelId target, Func<DockPanelId, bool> usable)
    {
        if (layout.SideOf(target) is DockSide.Floating or DockSide.Hidden) return [];

        var group = layout.SlotOf(target);
        var items = new List<HeaderMenuPanel>();
        foreach (var info in DockPanels.All)
        {
            if (!usable(info.Id)) continue;
            if (group.Contains(info.Id))
            {
                items.Add(new(info.Id, info.Title, HeaderMenuPanelState.InThisGroup, null));
                continue;
            }
            // The timeline cannot leave its edge, so offering to move it here
            // would be an item that does nothing.
            if (!info.Movable) continue;
            var side = layout.SideOf(info.Id);
            items.Add(side == DockSide.Hidden
                ? new(info.Id, info.Title, HeaderMenuPanelState.Closed, null)
                : new(info.Id, info.Title, HeaderMenuPanelState.Elsewhere, Describe(side)));
        }
        // Here, then elsewhere, then closed, registry order inside each. At
        // seventeen panels this is the longest menu in the app, and grouping
        // by what picking an item would do is what makes it scannable. Not by
        // family: most panels have none, and a submenu would bury the one
        // thing the menu is for. OrderBy is stable, so the registry order
        // inside each group survives.
        return [.. items.OrderBy(i => i.State)];
    }

    private static string Describe(DockSide side) => side switch
    {
        DockSide.Left => "left",
        DockSide.Right => "right",
        DockSide.Top => "top",
        DockSide.Bottom => "bottom",
        _ => "floating window",
    };
}
