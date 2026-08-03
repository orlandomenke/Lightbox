namespace Lightbox.Core.Documents;

/// <summary>
/// The physical medium a brush imitates. Picking one selects which parts of
/// the simulation run; the numbers in <see cref="MediumSettings"/> then tune
/// it. <see cref="None"/> is the plain dab-stamping path that every brush
/// used before media existed, and it stays the default so nothing that
/// already renders changes.
/// </summary>
public enum MediumKind
{
    /// <summary>No simulation — stamp dabs and stop.</summary>
    None,

    /// <summary>Transparent, mobile, granulating. Pools at the edges as it dries.</summary>
    Watercolour,

    /// <summary>Opaque and matte. Barely flows; sits where it is put and hides what is under it.</summary>
    Gouache,

    /// <summary>Thick and slow. Holds ridges, drags what is already there, runs out.</summary>
    Oil,

    /// <summary>Very fluid, strongly absorbed, minimal granulation. Bleeds along paper fibres.</summary>
    Ink,
}

/// <summary>Paper surface the medium interacts with.</summary>
public enum PaperKind
{
    /// <summary>Hot-pressed: almost no tooth.</summary>
    Smooth,

    /// <summary>Cold-pressed: the usual watercolour paper.</summary>
    ColdPress,

    /// <summary>Rough: pronounced tooth, strong granulation and dry-brush skip.</summary>
    Rough,

    /// <summary>Woven canvas: directional weave, for oil.</summary>
    Canvas,
}

/// <summary>
/// Parameters of the medium simulation, stored per stroke like every other
/// setting that reaches pixels (invariant 4), so changing a preference never
/// alters existing art.
///
/// Everything here is deterministic. The lattice runs a fixed number of
/// iterations in a fixed order, and every value that looks random is derived
/// from document position — there is no RNG and no clock, so a reload and an
/// AI inbetween both re-render the identical image (invariant 2).
/// </summary>
public sealed class MediumSettings
{
    /// <summary>Which medium to simulate. <see cref="MediumKind.None"/> skips the whole pass.</summary>
    public MediumKind Kind { get; set; } = MediumKind.None;

    // ---- fluid ---------------------------------------------------------------

    /// <summary>0..1: water the brush carries. Drives how far pigment can travel.</summary>
    public double Wetness { get; set; } = 0.5;

    /// <summary>0..1: resistance to flow. Watercolour is near 0, oil near 1.</summary>
    public double Viscosity { get; set; } = 0.3;

    /// <summary>0..1: friction against the paper, which kills velocity over time.</summary>
    public double Drag { get; set; } = 0.4;

    /// <summary>
    /// Lattice iterations. The single knob that trades fidelity for time —
    /// cost is linear in this, so it is clamped and the budget tests pin it.
    /// </summary>
    public int FlowSteps { get; set; } = 12;

    /// <summary>0..1: how fast the paper pulls pigment out of suspension (drying).</summary>
    public double Absorbency { get; set; } = 0.5;

    /// <summary>
    /// 0..1: capillary pull toward the wet boundary. This is what makes a
    /// darkened rim appear on its own instead of being drawn on afterwards.
    /// </summary>
    public double EdgePull { get; set; } = 0.4;

    // ---- pigment -------------------------------------------------------------

    /// <summary>0..1: pigment carried per unit of water.</summary>
    public double PigmentDensity { get; set; } = 0.6;

    /// <summary>0..1: tendency to settle into the paper's valleys (granulation).</summary>
    public double Granularity { get; set; } = 0.3;

    /// <summary>
    /// 0..1: how much the pigment hides what is beneath it. Watercolour is
    /// near 0 (transparent glaze), gouache and oil near 1.
    /// </summary>
    public double Hiding { get; set; } = 0.2;

    /// <summary>
    /// Composite through Kubelka–Munk rather than source-over. This is what
    /// makes a glaze read as pigment over pigment instead of as film over
    /// film, and it is the difference most people see first.
    /// </summary>
    public bool PhysicalMixing { get; set; } = true;

    // ---- paper ---------------------------------------------------------------

    public PaperKind Paper { get; set; } = PaperKind.ColdPress;

    /// <summary>Grain size in document pixels — the tooth's characteristic wavelength.</summary>
    public double PaperScale { get; set; } = 12;

    /// <summary>0..1: how strongly the paper modulates deposition and flow.</summary>
    public double PaperInfluence { get; set; } = 0.5;

    // ---- body (opaque media) -------------------------------------------------

    /// <summary>0..1: paint thickness. Above zero the stroke carries a height field.</summary>
    public double Body { get; set; }

    /// <summary>0..1: how strongly that height field is shaded, giving impasto relief.</summary>
    public double Relief { get; set; }

    /// <summary>0..1: streaking along the stroke direction, as bristles comb the paint.</summary>
    public double BristleDrag { get; set; }

    /// <summary>
    /// 0..1: paint on the brush at the start of a stroke. Below 1 the brush
    /// runs out as the stroke lengthens, which is what dry-brush is.
    /// </summary>
    public double PaintLoad { get; set; } = 1;

    /// <summary>0..1: how much of what is already on the canvas the brush picks up and drags.</summary>
    public double Pickup { get; set; }

    // ---- how pressure reaches the medium ---------------------------------------
    //
    // Pressure already scales dab size and dab alpha. These decide what a
    // *light* touch means physically, which differs by medium: a watercolour
    // brush held lightly carries proportionally more water than pigment, while
    // an oil brush held lightly barely engages the paint underneath.

    /// <summary>
    /// 0..1: how much a light touch means water rather than pigment. Raising
    /// it makes light strokes paler and more prone to blooming, because the
    /// extra water carries what pigment there is further before it dries.
    /// </summary>
    public double PressureWater { get; set; }

    /// <summary>
    /// 0..1: how much pressure governs mixing with the paint already there.
    /// At 1 a light touch barely disturbs the canvas and a firm one drags it
    /// fully; at 0 pressure makes no difference to mixing.
    /// </summary>
    public double PressureMix { get; set; }

    /// <summary>
    /// 0..1: how much this stroke re-wets what is under it. Above zero, paint
    /// already on the layer is lifted back into suspension and moves with the
    /// new stroke — wet into wet. Deterministic despite being history
    /// dependent: the rasterizer replays strokes in record order, so what a
    /// stroke re-wets is exactly what the strokes before it left.
    /// </summary>
    public double Rewetting { get; set; }

    public MediumSettings Clone() => new()
    {
        Kind = Kind,
        Wetness = Wetness,
        Viscosity = Viscosity,
        Drag = Drag,
        FlowSteps = FlowSteps,
        Absorbency = Absorbency,
        EdgePull = EdgePull,
        PigmentDensity = PigmentDensity,
        Granularity = Granularity,
        Hiding = Hiding,
        PhysicalMixing = PhysicalMixing,
        Paper = Paper,
        PaperScale = PaperScale,
        PaperInfluence = PaperInfluence,
        Body = Body,
        Relief = Relief,
        BristleDrag = BristleDrag,
        PaintLoad = PaintLoad,
        Pickup = Pickup,
        PressureWater = PressureWater,
        PressureMix = PressureMix,
        Rewetting = Rewetting,
    };

    /// <summary>
    /// Is this the medium block a brush that has never asked for one carries?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used to keep it out of the file. Twenty-one keys on every stroke of
    /// every document is what "the simulation is optional" looked like on disk
    /// before this: behaviourally absent, and a third of the brush record.
    /// </para>
    /// <para>
    /// The comparison serializes rather than listing the fields, for the same
    /// reason <c>BrushComparison</c> does — a hand-written check over twenty-one
    /// properties is a list somebody forgets to extend, and forgetting here
    /// means silently dropping a setting an artist dialled in.
    /// </para>
    /// </remarks>
    public bool IsUntouched() => Canonical.Of(this) == Canonical.Default;

    private static class Canonical
    {
        private static readonly System.Text.Json.JsonSerializerOptions Options = new()
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        internal static readonly string Default = Of(new MediumSettings());

        internal static string Of(MediumSettings settings) =>
            System.Text.Json.JsonSerializer.Serialize(settings, Options);
    }
}
