namespace Lightbox.Core.Documents;

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

    /// <summary>Pressure→size response curve exponent (1 = linear).</summary>
    public double PressureSizeGamma { get; set; } = 1;

    /// <summary>Pressure→flow response exponent; 0 = pressure does not affect flow (the default).</summary>
    public double PressureFlowGamma { get; set; }

    public BrushSettings Clone() => new()
    {
        Size = Size,
        Hardness = Hardness,
        Opacity = Opacity,
        Flow = Flow,
        Spacing = Spacing,
        Kind = Kind,
        WetEdge = WetEdge,
        Granulation = Granulation,
        TipId = TipId,
        TipRotationDeg = TipRotationDeg,
        RotationJitter = RotationJitter,
        Scatter = Scatter,
        PressureSizeGamma = PressureSizeGamma,
        PressureFlowGamma = PressureFlowGamma,
    };
}
