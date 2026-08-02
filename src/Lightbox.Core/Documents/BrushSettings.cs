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

/// <summary>
/// What a smudge or blur brush reads: its own layer, or everything under it.
/// </summary>
/// <remarks>
/// <para>
/// The answer to <c>QUESTIONS.md</c> Q6, and it is three values rather than two
/// because neither of the two was right for every mark. A smudge blending a
/// character into a background wants to follow that background when it is
/// repainted; a smudge nudged until it looked right wants to stay exactly as it
/// was. Both are reasonable intentions about the same gesture, so the stroke
/// records which one it was — invariant 4, the same reason anti-aliasing is
/// stored per stroke rather than read from the tool bar at render time.
/// </para>
/// <para>
/// <see cref="ThisLayer"/> is the default and what every stroke drawn before
/// this existed is, so a document that never asks for the others renders and
/// serializes exactly as it did.
/// </para>
/// </remarks>
public enum SampleSource
{
    /// <summary>
    /// Read only the layer being painted on. Keeps a layer's pixels a function
    /// of that layer alone, which is what makes the frame cache per-layer.
    /// </summary>
    ThisLayer,

    /// <summary>
    /// Read the composite of everything beneath, at render time.
    /// </summary>
    /// <remarks>
    /// What "sample all layers" means everywhere else, and it follows edits:
    /// repaint the background and the smudge above it changes with it. The
    /// price is the cache — a frame holding one of these cannot be reused,
    /// because what is underneath may have moved since.
    /// </remarks>
    AllLayersLive,

    /// <summary>
    /// Read the composite of everything beneath <em>as it was when the stroke
    /// was made</em>, captured into the stroke.
    /// </summary>
    /// <remarks>
    /// The mark never changes again, and it costs no cache work at all because
    /// the stroke carries what it needs. It costs file size, and it is the one
    /// place pixels enter the record without being derived from strokes — note
    /// that the document still re-renders identically from itself, so this
    /// bends invariant 1's spirit rather than breaking its letter.
    /// </remarks>
    AllLayersBaked,
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
    /// What a smudge or blur reads (ignored by every other kind).
    /// </summary>
    public SampleSource SampleSource { get; set; } = SampleSource.ThisLayer;

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

    // ---- shape dynamics --------------------------------------------------------
    //
    // Photoshop's grouping, because an imported preset should be recognisable
    // to whoever made it. Each of these varies a value we already store, and
    // each is seeded from dab position through Hash01 — so they cost nothing
    // in determinism and nothing in re-render divergence, which is what makes
    // them safe to replay across a whole sequence.

    /// <summary>0..1: how much dab diameter varies per dab.</summary>
    public double SizeJitter { get; set; }

    /// <summary>
    /// 0..1: floor for jittered size, as a fraction of the brush size.
    /// Photoshop's Minimum Diameter — without it, jitter produces dabs that
    /// vanish entirely and the stroke reads as broken rather than varied.
    /// </summary>
    public double MinimumDiameter { get; set; } = 0.25;

    /// <summary>0..1: how much dab roundness varies per dab (1 = fully squashed).</summary>
    public double RoundnessJitter { get; set; }

    /// <summary>Base roundness, 1 = circular, lower squashes the dab.</summary>
    public double Roundness { get; set; } = 1;

    /// <summary>
    /// Rotate each dab to follow the stroke. Required for any flat or chisel
    /// tip to behave like one rather than a repeated stamp.
    /// </summary>
    public bool AngleFollowsDirection { get; set; }

    // ---- transfer --------------------------------------------------------------

    /// <summary>0..1: how much per-dab paint amount varies, giving wash variation.</summary>
    public double FlowJitter { get; set; }

    // ---- texture ---------------------------------------------------------------

    /// <summary>Paper surface multiplied into the dab; null keeps the flat granulation.</summary>
    public PaperKind? TextureSurface { get; set; }

    /// <summary>Grain size in document pixels for <see cref="TextureSurface"/>.</summary>
    public double TextureScale { get; set; } = 12;

    /// <summary>0..1: how strongly the texture bites into the dab.</summary>
    public double TextureDepth { get; set; }

    // ---- colour dynamics -------------------------------------------------------

    /// <summary>
    /// The other colour to jitter toward. Natural media varies between two
    /// picked colours rather than around one — that is what separates colour
    /// dynamics from noise.
    /// </summary>
    public string? SecondaryColor { get; set; }

    /// <summary>0..1: how far each dab drifts toward <see cref="SecondaryColor"/>.</summary>
    public double ColorJitter { get; set; }

    /// <summary>0..1: per-dab hue drift, as a fraction of a sixth of the wheel.</summary>
    public double HueJitter { get; set; }

    /// <summary>0..1: per-dab saturation drift.</summary>
    public double SaturationJitter { get; set; }

    /// <summary>0..1: per-dab brightness drift.</summary>
    public double BrightnessJitter { get; set; }

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
        SampleSource = SampleSource,
        SmudgeLength = SmudgeLength,
        SmudgeRadius = SmudgeRadius,
        ColorRate = ColorRate,
        WetEdge = WetEdge,
        Granulation = Granulation,
        TipId = TipId,
        TipRotationDeg = TipRotationDeg,
        RotationJitter = RotationJitter,
        Scatter = Scatter,
        SizeJitter = SizeJitter,
        MinimumDiameter = MinimumDiameter,
        RoundnessJitter = RoundnessJitter,
        Roundness = Roundness,
        AngleFollowsDirection = AngleFollowsDirection,
        FlowJitter = FlowJitter,
        TextureSurface = TextureSurface,
        TextureScale = TextureScale,
        TextureDepth = TextureDepth,
        SecondaryColor = SecondaryColor,
        ColorJitter = ColorJitter,
        HueJitter = HueJitter,
        SaturationJitter = SaturationJitter,
        BrightnessJitter = BrightnessJitter,
        PressureEnabled = PressureEnabled,
        PressureSizeGamma = PressureSizeGamma,
        PressureFlowGamma = PressureFlowGamma,
        PressureHardnessGamma = PressureHardnessGamma,
        Medium = Medium.Clone(),
    };
}
