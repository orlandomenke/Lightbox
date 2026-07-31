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
}
