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
    /// Stamps dabs and finishes them as it goes: a wet edge, a grain or a
    /// paper laid on the band each pointer event touched. More per event than
    /// a plain brush, and nothing at pen-lift.
    /// </summary>
    /// <remarks>
    /// <b>The finish moved under the pen on 2026-10-10</b>
    /// (docs/DESIGN-paint-path.md, phase 2). What follows is why the tier was
    /// made, and it describes the passes as they were then; the tier stays
    /// because the finish is still paid for, per event instead of per stroke.
    /// <para/>
    /// <b>B177 is why this tier exists.</b> The original argument kept texture
    /// off the badge — "per-dab arithmetic on values the engine already has" —
    /// and it was measured wrong: these passes are not per-dab at all, they are
    /// full-stroke surfaces at pen-lift, and they were 57–74% of the commit
    /// cost of the presets an artist reported as laggy while the badge called
    /// them Fast. A badge that can be checked is a badge that gets corrected.
    /// </remarks>
    Textured,

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
/// What is <em>not</em> on it matters as much. Scatter, jitter, a bitmap tip
/// and pressure curves are all per-dab arithmetic on values the engine already
/// has — they change how a mark looks without changing what the engine has to
/// go and fetch, and calling them expensive would make the badge meaningless
/// by putting it on everything.
/// </para>
/// <para>
/// <b>Wet edge and granulation sat in that list and did not belong there
/// (B177).</b> They read like per-dab effects and are not: both run as
/// full-stroke surface passes at pen-lift, and measurement put them at 57–74%
/// of the commit cost of the presets an artist reported as laggy. They are
/// <see cref="BrushCost.Textured"/> — cheaper than a medium, not free, and the
/// tier exists so the badge tells the truth an artist already noticed.
/// </para>
/// </remarks>
public static class BrushCostOf
{
    /// <summary>What this configuration costs to draw with.</summary>
    public static BrushCost Settings(BrushSettings brush) =>
        brush.Medium.Kind != MediumKind.None
        || brush.Kind is BrushKind.Smudge or BrushKind.Blur
        || brush.SampleSource != SampleSource.ThisLayer
        || brush.Mixing is not null
            ? BrushCost.Expressive
        : brush.WetEdge > 0 || brush.Granulation > 0 || HasPaper(brush)
            ? BrushCost.Textured
            : BrushCost.Fast;

    /// <summary>
    /// A paper texture that bites. The same full-stroke pass at pen-lift as
    /// granulation, and it was missing from the tier until the pencil became
    /// one (2026-10-09): a brush with a paper and a depth wore no badge.
    /// </summary>
    private static bool HasPaper(BrushSettings brush) =>
        (brush.TextureDepth > 0 || (brush.TextureSettle ?? 0) > 0)
        && (brush.TextureSurface is not null || brush.TextureId is not null);

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
        if (brush.Mixing is not null) reasons.Add("picks up the colour it is laid on");
        if (brush.SampleSource != SampleSource.ThisLayer) reasons.Add("blends the layers underneath");
        if (brush.WetEdge > 0) reasons.Add("darkens its own edge as you draw (wet edge)");
        if (brush.Granulation > 0) reasons.Add("settles pigment into the grain as you draw");
        if (HasPaper(brush))
        {
            // What the surface does to this brush, in the brush's own terms: a
            // pencil's tooth, paint that pressure pushes into a weave, a plain
            // paper pressed in - and, beside any of them, thin paint settling.
            if (brush.TextureDepth > 0)
            {
                reasons.Add(
                    brush.TexturePressure > 0 && brush.TextureFill is not null ? "catches on the surface until pressure fills it, as you draw"
                    : brush.TexturePressure > 0 ? "catches graphite on the paper's tooth as you draw"
                    : "presses the paper's texture in as you draw");
            }
            if ((brush.TextureSettle ?? 0) > 0) reasons.Add("lets thin paint settle into the surface as you draw");
        }
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
