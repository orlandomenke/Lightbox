namespace Lightbox.Core.Documents;

/// <summary>What varies the weight of a traced outline along its length.</summary>
/// <remarks>
/// Every source is a 0..1 signal read at a point on the contour, so drivers
/// blend by summation and a negative amount inverts one. Each is chosen to be
/// something a person can <em>see</em> in a drawing — Q118's requirement, since
/// the same vocabulary has to be inferable from a reference image.
/// </remarks>
public enum WeightSource
{
    /// <summary>How sharply the contour bends here. The classic animation line.</summary>
    Curvature,

    /// <summary>How fast the fluid is moving. Needs a velocity field; contributes nothing without one.</summary>
    FlowSpeed,

    /// <summary>How steeply the field falls off — a hard edge against a feathered one.</summary>
    FieldGradient,

    /// <summary>
    /// Heaviest where the contour faces <em>away</em> from
    /// <see cref="LineTreatment.LightAngleDeg"/> — the shadow side, which is
    /// what gives an inked drawing its solidity. A negative amount puts the
    /// weight on the lit side instead.
    /// </summary>
    LightDirection,

    /// <summary>Outermost band heaviest, innermost lightest. Depth without shading.</summary>
    BandDepth,

    /// <summary>Away from the ends of the drawn run — a stroke that starts and finishes light.</summary>
    ArcTaper,
}

/// <summary>One driver's contribution. Amount may be negative to invert it.</summary>
public readonly record struct WeightDriver(WeightSource Source, double Amount);

/// <summary>How much of a band's contour actually gets an outline.</summary>
public enum LineCoverage
{
    /// <summary>The whole way round.</summary>
    Full,

    /// <summary>Only where the contour faces <see cref="LineTreatment.CoverageAngleDeg"/>.</summary>
    Facing,
}

/// <summary>How the band levels are spread through the field's range.</summary>
public enum BandSpacing
{
    Even,

    /// <summary>Bunched toward the top of the range, so the hot core gets the detail.</summary>
    CoreBiased,
}

/// <summary>Which bands get an outline drawn on them.</summary>
public enum OutlinedBands
{
    None,
    Silhouette,
    Every,
}

/// <summary>
/// How a traced contour becomes a drawn stroke: where the line sits, what
/// varies its weight, and how smooth it is.
/// </summary>
/// <remarks>
/// <para>
/// <b>A brush preset says what the mark looks like; this says how a contour
/// becomes a stroke for that brush to draw.</b> Nothing here repeats anything
/// <see cref="BrushSettings"/> already expresses — width, texture, tip shape,
/// grain and jitter all come from the brush, including one imported out of
/// Photoshop or Krita. That division is the reason following an art style is
/// mostly free: Q116 chose to bake strokes rather than pixels, so the outline is
/// a real brush stroke and every brush an artist owns already draws it.
/// </para>
/// <para>
/// <b>Every field is nullable, and that is the cascade</b> (Q118). An element
/// names a shared treatment and may override fields on top of it; because the
/// defaults live in exactly one place — <see cref="Defaults"/> — a shared
/// treatment and an override are the <em>same record</em>, and resolution is
/// <c>override ?? shared ?? default</c>. The obvious alternative, a parallel
/// mirror record of nullable fields, is the one thing that would have to be kept
/// in step forever. It also lands *optional means absent*: an untouched
/// treatment writes no keys at all.
/// </para>
/// <para>
/// <b>Units are ratios, angles and stroke-widths — never pixels, never
/// coefficients.</b> Two arguments land on the same answer. Invariant 7's: a
/// treatment expressed in pixels means something different at another element
/// scale or output scale, while a stroke-width is scale-free. And Q118's: every
/// field has to be observable in a drawing, because the same vocabulary is what
/// a model will infer from a reference image — nobody can see a gamma of 1.4,
/// and anybody can see that a line is three times heavier on the bends.
/// </para>
/// </remarks>
public sealed class LineTreatment
{
    /// <summary>The brush that draws the outline. Null falls back to the tool's current brush.</summary>
    public string? BrushPresetId { get; set; }

    /// <summary>Stroke-widths to push the line off the contour, positive outward.</summary>
    public double? Offset { get; set; }

    public LineCoverage? Covers { get; set; }

    /// <summary>Which way the drawn part of the contour faces, degrees clockwise from up.</summary>
    public double? CoverageAngleDeg { get; set; }

    /// <summary>How wide a fan around that angle still gets drawn, in degrees.</summary>
    public double? CoverageSpreadDeg { get; set; }

    /// <summary>Where the light comes from for <see cref="WeightSource.LightDirection"/>, degrees clockwise from up.</summary>
    public double? LightAngleDeg { get; set; }

    /// <summary>Stroke-widths of drawn line between gaps. Absent or zero means an unbroken line.</summary>
    public double? BreakLength { get; set; }

    /// <summary>Stroke-widths of gap between the drawn runs.</summary>
    public double? BreakGap { get; set; }

    /// <summary>The pressure a point gets before any driver moves it.</summary>
    public double? BaseWeight { get; set; }

    public List<WeightDriver>? Weights { get; set; }

    /// <summary>
    /// Douglas–Peucker tolerance, in stroke-widths. Larger is cleaner and more
    /// angular. It is a chord deviation, so anything under about a fifth of a
    /// line width disappears beneath the line that draws it — which is where the
    /// default sits.
    /// </summary>
    public double? Simplify { get; set; }

    /// <summary>0 leaves the traced polyline as it is; 1 replaces it with fitted cubics.</summary>
    public double? Smoothing { get; set; }

    /// <summary>A turn sharper than this survives smoothing as a corner.</summary>
    public double? CornerAngleDeg { get; set; }

    public int? Bands { get; set; }

    public BandSpacing? Spacing { get; set; }

    public OutlinedBands? Outlined { get; set; }

    /// <summary>
    /// The one place a default is written down. Everything else reads through
    /// <see cref="Resolve"/>, so there is no second copy to drift.
    /// </summary>
    public static ResolvedTreatment Defaults { get; } = new(
        BrushPresetId: null,
        Offset: 0,
        Covers: LineCoverage.Full,
        CoverageAngleDeg: 0,
        CoverageSpreadDeg: 180,
        LightAngleDeg: -45,
        BreakLength: 0,
        BreakGap: 0,
        BaseWeight: 0.55,
        Weights: [],
        Simplify: 0.15,
        Smoothing: 0,
        CornerAngleDeg: 55,
        Bands: 3,
        Spacing: BandSpacing.Even,
        Outlined: OutlinedBands.Silhouette);

    /// <summary>
    /// Flatten the cascade: an element's overrides over its shared treatment
    /// over the defaults. Either may be null.
    /// </summary>
    public static ResolvedTreatment Resolve(LineTreatment? shared, LineTreatment? over = null)
    {
        var d = Defaults;
        return new ResolvedTreatment(
            over?.BrushPresetId ?? shared?.BrushPresetId ?? d.BrushPresetId,
            over?.Offset ?? shared?.Offset ?? d.Offset,
            over?.Covers ?? shared?.Covers ?? d.Covers,
            over?.CoverageAngleDeg ?? shared?.CoverageAngleDeg ?? d.CoverageAngleDeg,
            over?.CoverageSpreadDeg ?? shared?.CoverageSpreadDeg ?? d.CoverageSpreadDeg,
            over?.LightAngleDeg ?? shared?.LightAngleDeg ?? d.LightAngleDeg,
            over?.BreakLength ?? shared?.BreakLength ?? d.BreakLength,
            over?.BreakGap ?? shared?.BreakGap ?? d.BreakGap,
            over?.BaseWeight ?? shared?.BaseWeight ?? d.BaseWeight,
            over?.Weights ?? shared?.Weights ?? d.Weights,
            over?.Simplify ?? shared?.Simplify ?? d.Simplify,
            over?.Smoothing ?? shared?.Smoothing ?? d.Smoothing,
            over?.CornerAngleDeg ?? shared?.CornerAngleDeg ?? d.CornerAngleDeg,
            over?.Bands ?? shared?.Bands ?? d.Bands,
            over?.Spacing ?? shared?.Spacing ?? d.Spacing,
            over?.Outlined ?? shared?.Outlined ?? d.Outlined);
    }

    /// <summary>Which fields this record actually states, for a UI that has to mark them as overridden.</summary>
    /// <remarks>
    /// The cascade's one unavoidable cost (Q118): without a way to see that a
    /// field is overridden and put it back, "two places to look" reads to an
    /// artist as the app being inconsistent rather than as a cascade.
    /// </remarks>
    public IEnumerable<string> StatedFields()
    {
        if (BrushPresetId is not null) yield return nameof(BrushPresetId);
        if (Offset is not null) yield return nameof(Offset);
        if (Covers is not null) yield return nameof(Covers);
        if (CoverageAngleDeg is not null) yield return nameof(CoverageAngleDeg);
        if (CoverageSpreadDeg is not null) yield return nameof(CoverageSpreadDeg);
        if (LightAngleDeg is not null) yield return nameof(LightAngleDeg);
        if (BreakLength is not null) yield return nameof(BreakLength);
        if (BreakGap is not null) yield return nameof(BreakGap);
        if (BaseWeight is not null) yield return nameof(BaseWeight);
        if (Weights is not null) yield return nameof(Weights);
        if (Simplify is not null) yield return nameof(Simplify);
        if (Smoothing is not null) yield return nameof(Smoothing);
        if (CornerAngleDeg is not null) yield return nameof(CornerAngleDeg);
        if (Bands is not null) yield return nameof(Bands);
        if (Spacing is not null) yield return nameof(Spacing);
        if (Outlined is not null) yield return nameof(Outlined);
    }

    public LineTreatment Clone() => new()
    {
        BrushPresetId = BrushPresetId, Offset = Offset, Covers = Covers,
        CoverageAngleDeg = CoverageAngleDeg, CoverageSpreadDeg = CoverageSpreadDeg,
        LightAngleDeg = LightAngleDeg, BreakLength = BreakLength, BreakGap = BreakGap,
        BaseWeight = BaseWeight, Weights = Weights is null ? null : [.. Weights],
        Simplify = Simplify, Smoothing = Smoothing, CornerAngleDeg = CornerAngleDeg,
        Bands = Bands, Spacing = Spacing, Outlined = Outlined,
    };
}

/// <summary>
/// A <see cref="LineTreatment"/> with every field settled. Derived, never
/// authored and never serialized — which is what keeps the cascade at one
/// authored record rather than two (Q118).
/// </summary>
public readonly record struct ResolvedTreatment(
    string? BrushPresetId,
    double Offset,
    LineCoverage Covers,
    double CoverageAngleDeg,
    double CoverageSpreadDeg,
    double LightAngleDeg,
    double BreakLength,
    double BreakGap,
    double BaseWeight,
    IReadOnlyList<WeightDriver> Weights,
    double Simplify,
    double Smoothing,
    double CornerAngleDeg,
    int Bands,
    BandSpacing Spacing,
    OutlinedBands Outlined);
