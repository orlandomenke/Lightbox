namespace Lightbox.App.Services;

/// <summary>
/// Which of a brush's alternates a stroke was begun with: none, the one on
/// Alt, or the one on Ctrl+Alt (Q255, <see cref="BrushPreset.Alternate"/>).
/// </summary>
public enum BrushAlternate
{
    /// <summary>The brush in hand.</summary>
    None,

    /// <summary>Alt at pen-down: the brush's alternate, or erasing with the brush itself when it names none.</summary>
    First,

    /// <summary>Ctrl+Alt at pen-down: the brush's second alternate. A brush with none leaves the keys to the eyedropper.</summary>
    Second,
}
