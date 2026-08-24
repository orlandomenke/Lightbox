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

    /// <summary>
    /// One glyph of set type: <c>Stroke.Points</c> is the first contour of the
    /// glyph outline, <c>Stroke.Holes</c> every other one — counters, and the
    /// separate pieces of a glyph like the dot on an <c>i</c> — filled even-odd
    /// exactly as a <see cref="Fill"/> is, and through the same code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Its own kind although it renders identically to <see cref="Fill"/>,</b>
    /// which is the opposite call to the one <see cref="ClearRegion"/> needed and
    /// rests on the same question: does anything <em>read</em> these differently?
    /// The rasterizer does not. The tools do — a flood fill is a region an artist
    /// poured into and a glyph is one of forty marks that together are a
    /// sentence, so picking one, recolouring one or asking "what is under the
    /// cursor" has a different right answer for each. Folding them together
    /// would make the fill tool's own machinery walk over type.
    /// </para>
    /// <para>
    /// The handle back to what was typed is <see cref="Stroke.TextId"/>. It is
    /// provenance, not behaviour: a text stroke whose element has been deleted is
    /// an ordinary contour fill that renders exactly the same and can no longer
    /// be retyped.
    /// </para>
    /// </remarks>
    Text,
}

/// <summary>
/// The questions worth asking about a <see cref="ToolKind"/> in more than one
/// place.
/// </summary>
/// <remarks>
/// <b>A registry rather than a repeated pattern match.</b> Adding
/// <see cref="ToolKind.Text"/> meant finding six <c>is Fill or ClearRegion</c>
/// tests scattered across the rasterizer, the picker, the hover preview and the
/// node editor, each of which had to learn about it independently and any one of
/// which could have been missed silently — a text stroke previewing as an open
/// horseshoe, or refusing to be clicked. The next contour kind asks here.
/// </remarks>
public static class ToolKinds
{
    /// <summary>
    /// Whether this kind's points are a closed contour to be filled, rather than
    /// a path to be walked laying dabs.
    /// </summary>
    /// <remarks>
    /// The convention every one of them shares: <c>Stroke.Points</c> is the first
    /// contour and <c>Stroke.Holes</c> the rest, read even-odd. What they do
    /// <em>with</em> the filled region still differs —
    /// <see cref="ToolKind.ClearRegion"/> takes it away — so this answers the
    /// shape question only.
    /// </remarks>
    public static bool FillsAContour(this ToolKind tool) =>
        tool is ToolKind.Fill or ToolKind.ClearRegion or ToolKind.Text;
}
