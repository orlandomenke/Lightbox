namespace Lightbox.Core.Documents;

/// <summary>
/// What a brush costs to draw with, as a badge the artist can see before they
/// pick it.
/// </summary>
public enum BrushCost
{
    /// <summary>
    /// Stamps dabs and stops. Predictable, bounded, and what almost every
    /// brush is.
    /// </summary>
    Fast,

    /// <summary>
    /// Reads the canvas back, simulates a medium, or composites the layer
    /// stack — the things that make a mark behave like a material.
    /// </summary>
    /// <remarks>
    /// Not a warning. These brushes exist because the coupling is what makes
    /// the mark expressive, and an artist reaching for one has decided the
    /// trade is worth it. The badge is so that decision is made knowingly
    /// rather than discovered on a 4K canvas at frame 180.
    /// </remarks>
    Expressive,
}

/// <summary>
/// Which brushes are the expensive expressive ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never asserted.</b> The same rule the roadmap checkboxes
/// follow, and for the same reason: a preset carrying a hand-set "this one is
/// fast" flag would be wrong the first time somebody edited it, and a badge
/// that lies is worse than no badge. Turn the medium on and the brush becomes
/// Expressive in the picker without anybody remembering to say so; turn it off
/// and it goes back.
/// </para>
/// <para>
/// The list below is deliberately short, and everything on it earns its place
/// by reading something other than the dab it is drawing:
/// </para>
/// <list type="bullet">
/// <item>a simulated medium runs a lattice over the stroke's region;</item>
/// <item>smudge and blur sample the canvas back per dab;</item>
/// <item>sampling other layers composites the stack behind the stroke.</item>
/// </list>
/// <para>
/// What is <em>not</em> on it matters as much. Scatter, jitter, a bitmap tip,
/// paper texture and pressure curves are all per-dab arithmetic on values the
/// engine already has — they change how a mark looks without changing what the
/// engine has to go and fetch, and calling them expensive would make the badge
/// meaningless by putting it on everything.
/// </para>
/// </remarks>
public static class BrushCostOf
{
    /// <summary>What this configuration costs to draw with.</summary>
    public static BrushCost Settings(BrushSettings brush) =>
        brush.Medium.Kind != MediumKind.None
        || brush.Kind is BrushKind.Smudge or BrushKind.Blur
        || brush.SampleSource != SampleSource.ThisLayer
            ? BrushCost.Expressive
            : BrushCost.Fast;

    /// <summary>
    /// The reason, for a tooltip — or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Named rather than counted: "three expensive options" tells an artist
    /// nothing they can act on, and "it simulates watercolour" tells them
    /// exactly what to turn off if they want the speed back.
    /// </remarks>
    public static string? Why(BrushSettings brush)
    {
        var reasons = new List<string>(3);
        if (brush.Medium.Kind != MediumKind.None) reasons.Add($"simulates {Medium(brush.Medium.Kind)}");
        if (brush.Kind is BrushKind.Smudge or BrushKind.Blur) reasons.Add("reads the canvas back as it goes");
        if (brush.SampleSource != SampleSource.ThisLayer) reasons.Add("blends the layers underneath");
        return reasons.Count == 0 ? null : string.Join(", ", reasons);
    }

    private static string Medium(MediumKind kind) => kind switch
    {
        MediumKind.Watercolour => "watercolour",
        MediumKind.Gouache => "gouache",
        MediumKind.Oil => "oil",
        MediumKind.Ink => "ink",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
