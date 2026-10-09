namespace Lightbox.Core.Documents;

/// <summary>
/// Paint that picks up what it is laid on (Q232, <c>docs/DESIGN-colour-mixing.md</c>).
/// Each dab samples the ground under it — the layer beneath and the stroke's own
/// earlier dabs — carries that colour along the stroke, and lays its own paint
/// mixed toward it in pigment, so yellow into blue goes green.
/// </summary>
/// <remarks>
/// <para>
/// Optional the way the camera is: <see cref="BrushSettings.Mixing"/> is null for
/// a brush that never mixes, and null writes nothing to the file. Stored per
/// stroke like every setting that reaches pixels (invariant 4).
/// </para>
/// <para>
/// Clip Studio Paint's names for the same three ideas are <i>amount of paint</i>,
/// <i>color stretch</i> (here <i>length</i>, the smudge option's word for the same idea) and the sampling radius it does not expose; the manual
/// uses the words an artist would.
/// </para>
/// </remarks>
public sealed class ColourMixing
{
    /// <summary>
    /// 0..1: how much of a dab is the brush's own paint. The rest is what the
    /// brush picked up. At 1 nothing mixes; at 0 the brush only moves what is
    /// there.
    /// </summary>
    /// <remarks>
    /// Not pressure-driven yet, on purpose. A pressure curve is stored under a
    /// <see cref="BrushDynamic"/> key, and a key an older build does not know
    /// makes its brush store unreadable — which that build then saves over,
    /// empty (sensitivity-guardian, on landing). It waits for Q193's version
    /// field, like every other addition to that enum.
    /// </remarks>
    public double Amount { get; set; } = 0.5;

    /// <summary>
    /// 0..1: how much of the carried colour survives each dab. At 0 the pickup
    /// is replaced every dab and colour barely travels; at 1 it is dragged the
    /// length of the stroke.
    /// </summary>
    public double Length { get; set; } = 0.5;

    /// <summary>
    /// 0..1: how far out from the dab's centre the ground is sampled, as a
    /// fraction of its radius. Near 1 the brush reads its own leading edge,
    /// where the ground is still fresh; near 0 it reads only what it just laid.
    /// </summary>
    public double Reach { get; set; } = 0.9;

    public ColourMixing Clone() => new() { Amount = Amount, Length = Length, Reach = Reach };
}
