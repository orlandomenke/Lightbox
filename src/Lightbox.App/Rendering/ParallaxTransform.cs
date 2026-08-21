using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The multiplane matrix: what a layer with a depth is drawn through when the
/// composite is rendered under a camera. Stage 1 of
/// <c>docs/DESIGN-3d-space.md</c> — the arithmetic is <see cref="LayerDepth"/>,
/// this is only its expression as the per-pass matrix
/// <see cref="RenderPass.Matrix"/> already carries for the transform tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an inverse-times-target rather than a matrix built from scratch:</b>
/// the compositor concatenates the camera's document-to-device matrix once for
/// the whole stack, and a pass matrix nests inside it, in document space. The
/// matrix that lands this layer where the <em>attenuated</em> framing would is
/// therefore <c>M(framing)⁻¹ × M(attenuated)</c> — whatever render scale or
/// view transform is stacked outside cancels, because it wraps both.
/// </para>
/// <para>
/// Null for a layer on the picture plane, not identity: the compositor's
/// no-matrix arm is the path that existed before any of this, and a layer
/// without depth must take it (the design's rule 1, the camera's own
/// precedent).
/// </para>
/// </remarks>
public static class ParallaxTransform
{
    /// <summary>
    /// One publish's worth of the shared arithmetic: the framing, the home it
    /// is measured from, and the framing's inverted matrix. Built once per
    /// publish (or per exported frame) so the per-layer cost is one
    /// attenuation and one concat — pass building runs per pointer event
    /// while drawing, and inverting the same matrix per layer there is
    /// exactly the per-event fat the leak review flagged.
    /// </summary>
    public readonly record struct Frame(
        CameraFraming Framing, CameraFraming Home, SKMatrix Inverse,
        int OutputWidth, int OutputHeight)
    {
        /// <summary>
        /// The pass matrix for a layer at <paramref name="depth"/>, or null
        /// when it sits on the picture plane or the camera is at home — the
        /// null arm is the path that existed before depth did.
        /// </summary>
        public SKMatrix? MatrixFor(double? depth)
        {
            if (depth is not { } d || d == 0) return null;
            var attenuated = LayerDepth.Attenuate(Framing, Home, d);
            if (attenuated == Framing) return null;
            // P(p) = M⁻¹(M_att(p)): the attenuated framing first, then back
            // out of the full one the canvas already carries. Concat(a, b)
            // maps a(b(p)). Scale 1 on both matrices: any shared outer factor
            // (render scale, the view transform) wraps both and cancels.
            return SKMatrix.Concat(
                Inverse, CameraTransform.Matrix(attenuated, OutputWidth, OutputHeight, 1.0));
        }
    }

    /// <summary>
    /// A document-space dirty rect widened to also cover its image on a
    /// plane — the union, rounded outward.
    /// </summary>
    /// <remarks>
    /// The dirty-region machinery maps rects through the camera matrix alone,
    /// and a stroke on a layer with depth lands its pixels somewhere else on
    /// screen (this matrix, applied inside the camera's). Because the parallax
    /// matrix is document-space, the correction is document-space too: widen
    /// the rect before anything maps it, and every downstream clip — the ring,
    /// the culled route, the compose — is right without learning about planes.
    /// Union rather than replace, because the un-parallaxed rect still names
    /// real repaint work: the other layers' pixels under the old clip.
    /// Found by adversarial review; <c>ParallaxDirtyRegionTests</c> holds it.
    /// </remarks>
    public static SKRectI CoverPlane(SKRectI rect, SKMatrix parallax)
    {
        var doc = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var union = SKRect.Union(doc, parallax.MapRect(doc));
        // Outward to whole pixels — an inward-rounded clip is the two-stale-
        // pixels lesson DeviceBounds already records.
        return new SKRectI(
            (int)Math.Floor(union.Left), (int)Math.Floor(union.Top),
            (int)Math.Ceiling(union.Right), (int)Math.Ceiling(union.Bottom));
    }

    /// <summary>
    /// The shared half of the arithmetic, or null when the framing's matrix
    /// is degenerate. <paramref name="outputWidth"/>/<paramref name="outputHeight"/>
    /// must be the output size the enclosing camera matrix is built with, or
    /// the inverse does not cancel it.
    /// </summary>
    public static Frame? Prepare(
        CameraFraming framing, CameraFraming home, int outputWidth, int outputHeight)
    {
        var full = CameraTransform.Matrix(framing, outputWidth, outputHeight, 1.0);
        return full.TryInvert(out var inverse)
            ? new Frame(framing, home, inverse, outputWidth, outputHeight)
            : null;
    }
}
