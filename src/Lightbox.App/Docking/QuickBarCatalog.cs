namespace Lightbox.App.Docking;

/// <summary>One thing the Quick options bar can carry.</summary>
/// <param name="Id">The stable id a workspace's list stores. Never rename one:
/// it is written into <c>workspaces.json</c>.</param>
/// <param name="Label">What the customize flyout calls it.</param>
/// <param name="Hint">The tooltip that says what taking it buys.</param>
public sealed record QuickBarOption(string Id, string Label, string Hint);

/// <summary>
/// The registry of everything the Quick options bar can offer.
/// </summary>
/// <remarks>
/// <para>
/// The bar is a <b>workspace's</b> smart bar, not the tool's (Q70, sharpened
/// 2026-08-13): each workspace chooses its contents from this catalogue, with
/// size and opacity fixed outside it. An Animation workspace reaches for the
/// transport; an Illustration workspace reaches for the marquee. The catalogue
/// exists so the customize flyout has something to enumerate — the same reason
/// <c>ShortcutMap</c> does one level up.
/// </para>
/// <para>
/// Tool-bound entries stay tool-gated as well: a workspace that carries
/// "Fill options" shows them while the fill is in hand, not as a dead strip
/// all day. The workspace decides what the bar <em>offers</em>; the tool in
/// hand still decides what of that is relevant right now.
/// </para>
/// </remarks>
public static class QuickBarCatalog
{
    /// <summary>
    /// Retired id (2026-08-14, owner's ask): the preset picker is pinned
    /// beside the colour pair and no longer on offer — the same reason size
    /// and opacity were never in the catalogue. The constant stays because
    /// lists saved while it was choosable still carry the string; an id no
    /// gate reads is inert, and <see cref="WorkspaceViewModel.SetQuickOption"/>
    /// drops unknown ids on the next edit.
    /// </summary>
    public const string BrushPreset = "brush-preset";

    public const string BrushOptions = "brush-options";
    public const string EraserOptions = "eraser-options";
    public const string ShapeOptions = "shape-options";
    public const string FillOptions = "fill-options";
    public const string SelectOptions = "select-options";
    public const string GradientOptions = "gradient-options";
    public const string ArrowOptions = "arrow-options";

    /// <summary>
    /// What you can do to the line you are inside — Simplify, and its count.
    /// </summary>
    /// <remarks>
    /// <b>B221.</b> The capability shipped with vector phase 4c and the
    /// registration did not, which is this catalogue's whole reason for
    /// existing: the customize flyout enumerates it, so an entry that is not
    /// here cannot be chosen, and <c>Simplify</c> had no control on any surface
    /// at all — a command in <c>ShortcutMap</c> with a null gesture, documented
    /// in the manual as living somewhere nothing drew.
    /// </remarks>
    public const string LineOptions = "line-options";

    /// <summary>The font, size, tracking and alignment while the text tool is in hand.</summary>
    public const string TextOptions = "text-options";
    public const string GuideOptions = "guide-options";
    public const string Transport = "transport";
    public const string AddFrame = "add-frame";

    public static readonly IReadOnlyList<QuickBarOption> All =
    [
        new(BrushOptions, "Brush options", "Hardness, flow and smoothing while the brush is in hand"),
        new(EraserOptions, "Eraser options", "Hardness and smoothing while the eraser is in hand"),
        new(ShapeOptions, "Shape options", "Which shape, and its settings, while the shape tool is in hand"),
        new(FillOptions, "Fill options", "Tolerance and reach while the fill is in hand"),
        new(SelectOptions, "Selection options", "Marquee shape, feather and the selection actions while selecting"),
        new(GradientOptions, "Gradient options", "The ramp and its type while the gradient tool is in hand"),
        new(ArrowOptions, "Line selection", "What the arrow tool has picked up"),
        new(LineOptions, "Inside a line", "Simplify, and how many points the line you are inside has"),
        new(TextOptions, "Text options", "The font, size, tracking and alignment while the text tool is in hand"),
        new(GuideOptions, "Guide options", "The guide the Move tool has picked up — where it is, and the numbers behind it"),
        new(Transport, "Play / pause", "The transport, without reaching down to the timeline"),
        new(AddFrame, "Add frame", "New and duplicated frames, without reaching down to the timeline"),
    ];

    /// <summary>
    /// What a layout that never chose shows: every tool's own group and no
    /// timeline buttons — the bar exactly as it was before workspaces chose,
    /// so a <c>workspaces.json</c> written before this existed changes nothing.
    /// </summary>
    public static readonly IReadOnlyList<string> ToolDefaults =
    [
        BrushOptions, EraserOptions, ShapeOptions,
        FillOptions, SelectOptions, GradientOptions, ArrowOptions, GuideOptions,
        LineOptions, TextOptions,
    ];
}
