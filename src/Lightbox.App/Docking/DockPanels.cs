namespace Lightbox.App.Docking;

/// <summary>
/// Every panel the workspace can hold, named once so a layout can refer to
/// one without holding the control.
/// </summary>
/// <remarks>
/// These names are written into saved workspaces, so they are API: renaming a
/// member silently drops that panel out of every layout a user has saved.
/// </remarks>
public enum DockPanelId
{
    Project,
    Layers,
    Color,
    Sheets,
    Palette,
    Gradient,
    Timeline,
}

/// <summary>Where a panel lives.</summary>
public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
    Floating,
    /// <summary>Not on screen at all. Closing a panel parks it here.</summary>
    Hidden,
}

/// <summary>
/// What a panel needs from the space it is given, independent of where it
/// currently is.
/// </summary>
/// <param name="MaxExtent">
/// The widest a panel is ever worth making — across the strip, so width in a
/// side strip and height in a top or bottom one. Null means it grows with the
/// area, which is right for the three panels that hold a list as long as the
/// work: the timeline, the project tree and the layer stack. Everything else
/// is a fixed-size control with whitespace after it, and stretching those is
/// how a sidebar ends up half empty.
/// </param>
/// <param name="Movable">
/// False for the timeline. Its row is the length of the animation, so it is a
/// bottom-edge panel by construction; letting it be dragged into a 300px
/// sidebar offers a placement that cannot work.
/// </param>
public sealed record DockPanelInfo(
    DockPanelId Id,
    string Title,
    double? MaxExtent,
    double DefaultExtent,
    double MinExtent,
    bool Movable = true);

/// <summary>The catalogue. One entry per panel, and the only place sizes live.</summary>
public static class DockPanels
{
    public static readonly IReadOnlyList<DockPanelInfo> All =
    [
        new(DockPanelId.Project, "Project", MaxExtent: null, DefaultExtent: 200, MinExtent: 120),
        new(DockPanelId.Layers, "Layers", MaxExtent: null, DefaultExtent: 260, MinExtent: 120),
        new(DockPanelId.Color, "Color", MaxExtent: 320, DefaultExtent: 300, MinExtent: 140),
        new(DockPanelId.Sheets, "Character sheets", MaxExtent: 320, DefaultExtent: 150, MinExtent: 80),
        new(DockPanelId.Palette, "Palette", MaxExtent: 320, DefaultExtent: 220, MinExtent: 110),
        new(DockPanelId.Gradient, "Gradient", MaxExtent: 320, DefaultExtent: 240, MinExtent: 120),
        new(DockPanelId.Timeline, "Timeline", MaxExtent: null, DefaultExtent: 280, MinExtent: 140, Movable: false),
    ];

    public static DockPanelInfo Of(DockPanelId id) => All.First(p => p.Id == id);

    public static string TitleOf(DockPanelId id) => Of(id).Title;

    /// <summary>
    /// The widest a strip holding these panels is worth making, or null for no
    /// ceiling.
    /// </summary>
    /// <remarks>
    /// A strip is as wide as its widest panel wants, but no wider — except that
    /// a panel with no cap at all removes the ceiling, because it has genuine
    /// use for the space. This is what makes a sidebar a sidebar: sized for the
    /// controls in it, not for the largest thing that ever landed there.
    /// </remarks>
    public static double? CapOf(IEnumerable<DockPanelId> panels)
    {
        double cap = 0;
        var any = false;
        foreach (var id in panels)
        {
            any = true;
            if (Of(id).MaxExtent is not { } max) return null;
            cap = Math.Max(cap, max);
        }
        return any ? cap : null;
    }
}
