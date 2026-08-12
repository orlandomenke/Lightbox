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
    /// A region taken *away*: same contour convention as <see cref="Fill"/> —
    /// <c>Stroke.Points</c> the outer contour, <c>Stroke.Holes</c> the inner
    /// ones — composited <c>DstOut</c> instead of over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B173.</b> Its own kind rather than an <see cref="Eraser"/> stroke that
    /// happens to be closed, because the two are read completely differently:
    /// an eraser's points are a <em>path</em> the engine walks laying dabs, and
    /// these are a <em>contour</em> it fills. Nothing about the geometry says
    /// which, so a renderer asked to guess would guess wrong on any short
    /// eraser stroke — and the failure would be silent and rare, which is the
    /// worst shape a rendering bug can have.
    /// </para>
    /// <para>
    /// It is also not a blend mode, for the reason <c>BlendModes.ForStroke</c>
    /// already gives about the eraser: taking alpha away is not something a
    /// separable blend does.
    /// </para>
    /// </remarks>
    ClearRegion,

    /// <summary>
    /// A gradient ramp. <c>Stroke.Points</c> holds exactly two points — the
    /// axis the artist dragged — and <c>Stroke.GradientId</c> names the ramp
    /// in <c>Doc.Gradients</c>. Everything else about it (the selection it
    /// was drawn under, alpha lock, opacity) works as it does for a fill,
    /// because it goes through the same pipeline.
    /// </summary>
    Gradient,
}
