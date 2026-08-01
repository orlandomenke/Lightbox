namespace Lightbox.Core.Documents;

/// <summary>
/// How a smudge brush carries colour. Krita's Color Smudge engine ships both
/// and they behave quite differently; we only had the first, with its two
/// governing numbers hardcoded.
/// </summary>
public enum SmudgeMode
{
    /// <summary>
    /// Drag a sample along the stroke, refreshing it as it goes. Reads like
    /// pulling a loaded brush through wet paint — detail smears into streaks.
    /// </summary>
    Smearing,

    /// <summary>
    /// Take one colour from under the dab, mix, and lay that down flat. Reads
    /// like a finger or a blender stump: detail dissolves rather than smearing.
    /// This is what a dedicated blender brush wants.
    /// </summary>
    Dulling,
}

/// <summary>How a brush applies paint.</summary>
public enum BrushKind
{
    /// <summary>Deposit color (the default painting brush).</summary>
    Paint,

    /// <summary>Drag existing canvas color along the stroke.</summary>
    Smudge,

    /// <summary>Soften existing canvas content along the stroke.</summary>
    Blur,
}

/// <summary>
/// Parameters of the brush that painted a stroke. Stored per stroke so the
/// raster pipeline can re-render any stroke (inbetweens, undo) exactly as it
/// was originally painted. Every property is deterministic — effects that
/// need randomness (scatter, rotation jitter, granulation) are seeded from
/// dab positions, never from a clock or RNG state.
/// </summary>
public sealed class BrushSettings
{
    /// <summary>Dab diameter in document pixels at pressure 1.</summary>
    public double Size { get; set; } = 6;

    /// <summary>
    /// Anti-alias the stroke's edges (the app's global AA toggle is stamped
    /// into each stroke at paint time, so re-renders stay bit-identical no
    /// matter how the toggle changes later).
    /// </summary>
    public bool AntiAlias { get; set; } = true;

    /// <summary>0 = fully soft (gaussian-ish falloff), 1 = hard round.</summary>
    public double Hardness { get; set; } = 0.8;

    /// <summary>Stroke-level opacity cap, 0..1 (overlapping dabs never exceed it).</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Per-dab paint amount, 0..1 — dabs build up within a stroke (Krita's flow).</summary>
    public double Flow { get; set; } = 1;

    /// <summary>Dab spacing as a fraction of dab size (typical 0.15).</summary>
    public double Spacing { get; set; } = 0.15;

    /// <summary>What the brush does to the canvas.</summary>
    public BrushKind Kind { get; set; } = BrushKind.Paint;

    /// <summary>Which smudge algorithm to use (ignored unless Kind is Smudge).</summary>
    public SmudgeMode SmudgeMode { get; set; } = SmudgeMode.Smearing;

    /// <summary>
    /// 0..1: how much of the carried colour survives each dab. Low values
    /// refresh the sample constantly so the brush barely transports colour;
    /// high values drag it a long way. Was hardcoded at 0.5.
    /// </summary>
    public double SmudgeLength { get; set; } = 0.5;

    /// <summary>
    /// 0..1: how far around the dab the brush samples, as a fraction of its
    /// radius. Wider sampling blends more and preserves less detail. Was
    /// hardcoded at 0.5.
    /// </summary>
    public double SmudgeRadius { get; set; } = 0.5;

    /// <summary>
    /// 0..1: how much of the brush's own colour is added as it smudges. Zero
    /// is a pure blender that only moves what is already there; raising it
    /// turns the same brush into one that paints and blends at once.
    /// </summary>
    public double ColorRate { get; set; }

    /// <summary>0..1: darkened rim where paint pools at the stroke edge (watercolor).</summary>
    public double WetEdge { get; set; }

    /// <summary>0..1: paper-grain noise multiplied into the stroke (watercolor/gouache).</summary>
    public double Granulation { get; set; }

    /// <summary>Key into <see cref="Doc.BrushTips"/> for a custom tip shape; null = round.</summary>
    public string? TipId { get; set; }

    /// <summary>Base rotation of the tip in degrees.</summary>
    public double TipRotationDeg { get; set; }

    /// <summary>0..1: random-looking (position-seeded) rotation per dab, as a fraction of 360°.</summary>
    public double RotationJitter { get; set; }

    /// <summary>0..1: position-seeded dab offset, as a fraction of dab size.</summary>
    public double Scatter { get; set; }

    /// <summary>
    /// Master pen-pressure switch for this brush. Off = the tablet's pressure
    /// is ignored entirely (every dab acts as full pressure), regardless of
    /// the per-setting response curves below.
    /// </summary>
    public bool PressureEnabled { get; set; } = true;

    /// <summary>Pressure→size response curve exponent (1 = linear, 0 = size ignores pressure).</summary>
    public double PressureSizeGamma { get; set; } = 1;

    /// <summary>Pressure→flow (transparency) response exponent; 0 = pressure does not affect flow (the default).</summary>
    public double PressureFlowGamma { get; set; }

    /// <summary>Pressure→hardness response exponent; 0 = off. Light pressure = softer dab edge.</summary>
    public double PressureHardnessGamma { get; set; }

    /// <summary>
    /// Physical medium to simulate after the dabs are laid down. Defaults to
    /// <see cref="MediumKind.None"/>, so a brush that never sets it renders
    /// exactly as it did before media existed.
    /// </summary>
    public MediumSettings Medium { get; set; } = new();

    public BrushSettings Clone() => new()
    {
        Size = Size,
        AntiAlias = AntiAlias,
        Hardness = Hardness,
        Opacity = Opacity,
        Flow = Flow,
        Spacing = Spacing,
        Kind = Kind,
        SmudgeMode = SmudgeMode,
        SmudgeLength = SmudgeLength,
        SmudgeRadius = SmudgeRadius,
        ColorRate = ColorRate,
        WetEdge = WetEdge,
        Granulation = Granulation,
        TipId = TipId,
        TipRotationDeg = TipRotationDeg,
        RotationJitter = RotationJitter,
        Scatter = Scatter,
        PressureEnabled = PressureEnabled,
        PressureSizeGamma = PressureSizeGamma,
        PressureFlowGamma = PressureFlowGamma,
        PressureHardnessGamma = PressureHardnessGamma,
        Medium = Medium.Clone(),
    };
}
