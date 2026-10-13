using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>
/// A brush's alternates at the canvas: which one the keys held at a press ask
/// for, and what the view model says that key does with the brush in hand (Q255).
/// </summary>
public sealed partial class CanvasControl
{
    /// <summary>
    /// What a held alternate key does with the brush in hand: whether it starts
    /// a stroke at all, and whether that stroke erases (Q255). Null leaves Alt
    /// as the eraser it always was and Ctrl+Alt to the eyedropper.
    /// </summary>
    private Func<Services.BrushAlternate, (bool Starts, bool Erases)>? _brushAlternate;

    /// <summary>Let the view model say what the brush's alternates are.</summary>
    public void SetBrushAlternateQuery(Func<Services.BrushAlternate, (bool Starts, bool Erases)> query) =>
        _brushAlternate = query;

    /// <summary>The alternate the keys held at a press ask for.</summary>
    private static Services.BrushAlternate AlternateOf(KeyModifiers modifiers) =>
        !modifiers.HasFlag(KeyModifiers.Alt) ? Services.BrushAlternate.None
        : modifiers.HasFlag(KeyModifiers.Control) ? Services.BrushAlternate.Second
        : Services.BrushAlternate.First;

    /// <summary>Ctrl+Alt at this press paints with the brush's second alternate, so it is not the eyedropper's.</summary>
    private bool CtrlAltPaints(KeyModifiers modifiers) =>
        ToolMode == CanvasToolMode.Paint
        && AlternateOf(modifiers) == Services.BrushAlternate.Second
        && _brushAlternate?.Invoke(Services.BrushAlternate.Second).Starts == true;

    /// <summary>
    /// The alternate a paint stroke begins with, noting whether it erases. A
    /// brush that names none keeps what Alt always did: it turns the brush in
    /// your hand into an eraser without swapping tools, so it keeps its size,
    /// shape and dynamics - unlike E, which brings the eraser's own settings.
    /// </summary>
    private Services.BrushAlternate AlternateAtPress(KeyModifiers modifiers)
    {
        var alternate = AlternateOf(modifiers);
        _erasingThisStroke = alternate != Services.BrushAlternate.None
            && (_brushAlternate?.Invoke(alternate).Erases ?? alternate == Services.BrushAlternate.First);
        return alternate;
    }
}
