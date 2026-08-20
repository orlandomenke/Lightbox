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
    /// The pass matrix for a layer at <paramref name="depth"/> under
    /// <paramref name="framing"/>, or null when the layer sits on the picture
    /// plane, the camera is at home, or the matrix cannot be formed.
    /// <paramref name="outputWidth"/>/<paramref name="outputHeight"/> must be
    /// the same output size the enclosing camera matrix was built with, or the
    /// two do not cancel.
    /// </summary>
    public static SKMatrix? PassMatrix(
        double? depth, CameraFraming framing, CameraFraming home,
        int outputWidth, int outputHeight)
    {
        if (depth is not { } d || d == 0) return null;
        var attenuated = LayerDepth.Attenuate(framing, home, d);
        if (attenuated == framing) return null;

        // Scale 1 on both: any shared outer factor (render scale, the view
        // transform) wraps both matrices and cancels in the product.
        var full = CameraTransform.Matrix(framing, outputWidth, outputHeight, 1.0);
        if (!full.TryInvert(out var inverse)) return null;
        var target = CameraTransform.Matrix(attenuated, outputWidth, outputHeight, 1.0);
        // P(p) = M⁻¹(M_att(p)): the attenuated framing first, then back out of
        // the full one the canvas already carries. Concat(a, b) maps a(b(p)).
        return SKMatrix.Concat(inverse, target);
    }
}
