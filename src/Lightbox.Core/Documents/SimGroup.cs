namespace Lightbox.Core.Documents;

/// <summary>
/// Several effects elements that are one effect: a fireball, its smoke and its
/// sparks, named and handled together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layering already worked; this is what makes it feel like one thing.</b>
/// Elements have always baked to a layer each, so they composite, z-order and
/// blend like any drawing. What did not exist was any way to say that three of
/// them belong together — so retiming an explosion meant editing three
/// <see cref="SimElement.FirstFrame"/>s, moving it meant editing three origins,
/// and duplicating it meant copying three records and remembering which.
/// </para>
/// <para>
/// <b>A named set and batch operations, not a transform to apply at bake
/// time.</b> The obvious design is a group origin and frame offset added to
/// each member when it renders, the way a scene graph works. It was rejected
/// after looking at the call sites: placement is read in the solve, in the
/// trace, in the mask rasteriser, in the bake and in the preview's frame range,
/// and an offset missed at any one of them is a bake that lands somewhere other
/// than where the preview showed it. A silent wrong-position bug is exactly
/// what <c>InvalidateFrameRender</c>'s funnel exists to avoid elsewhere.
/// </para>
/// <para>
/// <b>And the additive form is already provided, one level up.</b>
/// <see cref="SymbolPlacement"/> has <c>X</c>, <c>Y</c> and
/// <c>FrameOffset</c> — so once a group bakes to a symbol, placing it twice at
/// different times and positions is placement work. Putting an offset on the
/// group as well would be two mechanisms for one job, and the one on the
/// placement is the one that can be used many times over.
/// </para>
/// <para>
/// So a group carries no geometry at all. <see cref="SimGroupOps"/> moves and
/// retimes by writing the members' own records, which keeps every element's
/// origin honestly its origin, needs no change to the bake path, and undoes
/// through the ordinary editor.
/// </para>
/// </remarks>
public sealed class SimGroup
{
    public string Id { get; set; } = Ids.NewId("simgrp");

    /// <summary>What the artist calls it — "explosion", "chimney", "her cloak".</summary>
    public string Name { get; set; } = "Effect";

    /// <summary>
    /// Its members, by <see cref="SimElement.Id"/>, back to front.
    /// </summary>
    /// <remarks>
    /// Ids rather than elements, for the reason every other cross-reference in
    /// this document is an id: undo swaps a whole <see cref="Doc"/>, and a list
    /// holding the objects would go on describing the one that was replaced.
    /// The order is the drawing order, so it is the order the baked layers are
    /// stacked in.
    /// </remarks>
    public List<string> ElementIds { get; set; } = [];

    /// <summary>
    /// The layer folder this group's baked layers live in, or null before it has
    /// been baked.
    /// </summary>
    /// <remarks>
    /// Recorded rather than found by name, on <c>SimBakeOps.LayerFor</c>'s
    /// precedent: renaming a folder is an ordinary thing to do and must not
    /// orphan the group and make the next bake build a second one.
    /// </remarks>
    public string? LayerGroupId { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public SimGroup Clone()
    {
        var copy = (SimGroup)MemberwiseClone();
        copy.ElementIds = [.. ElementIds];
        return copy;
    }
}
