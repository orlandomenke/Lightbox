namespace Lightbox.Core.Documents;

public enum ToolKind
{
    Brush,
    Eraser,

    /// <summary>
    /// A filled region: <c>Stroke.Points</c> is the traced OUTER contour,
    /// <c>Stroke.Holes</c> the inner contours (even-odd). Rendered as a
    /// filled path through the same pipeline as everything else.
    /// </summary>
    Fill,

    /// <summary>
    /// A gradient ramp. <c>Stroke.Points</c> holds exactly two points — the
    /// axis the artist dragged — and <c>Stroke.GradientId</c> names the ramp
    /// in <c>Doc.Gradients</c>. Everything else about it (the selection it
    /// was drawn under, alpha lock, opacity) works as it does for a fill,
    /// because it goes through the same pipeline.
    /// </summary>
    Gradient,
}
