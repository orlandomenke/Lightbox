namespace Lightbox.Core.Documents;

/// <summary>
/// Parameters of the brush that painted a stroke. Stored per stroke so the
/// raster pipeline can re-render any stroke (inbetweens, undo) exactly as it
/// was originally painted.
/// </summary>
public sealed class BrushSettings
{
    /// <summary>Dab diameter in document pixels at pressure 1.</summary>
    public double Size { get; set; } = 6;

    /// <summary>0 = fully soft (gaussian-ish falloff), 1 = hard round.</summary>
    public double Hardness { get; set; } = 0.8;

    /// <summary>Base opacity of the stroke, 0..1.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Dab spacing as a fraction of dab size (typical 0.15).</summary>
    public double Spacing { get; set; } = 0.15;

    public BrushSettings Clone() => new()
    {
        Size = Size,
        Hardness = Hardness,
        Opacity = Opacity,
        Spacing = Spacing,
    };
}
