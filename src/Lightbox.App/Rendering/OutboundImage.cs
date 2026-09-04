using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Scaling for a picture on its way <i>out</i> of Lightbox — to a model in an AI
/// request, or to an agent over MCP.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the mechanism, never the policy.</b> Two callers cap an outbound
/// image and they do it for reasons that are only superficially the same: an AI
/// request is billed by area, and an MCP reply is spent out of the agent's own
/// context. They therefore keep <i>separate</i> constants — see
/// <c>ReferenceViewImages.LongEdge</c> and
/// <c>IpcDocumentApi.RenderedFrameLongEdge</c> — and share only the arithmetic
/// here. B31 is why: a single cap put somewhere convenient leaked onto a
/// consumer nobody was thinking about, and the fix was to move it to the one
/// call site where its reason was true. A shared constant would rebuild exactly
/// that coupling, and Q27's per-view heuristic would silently drag
/// <c>render_frame</c> along with it when it lands.
/// </para>
/// <para>
/// <b>Mipmapped, and deliberately not what the compositor uses.</b>
/// <c>SceneRenderer.Downscale</c> is linear with no mipmaps, which is the right
/// call for a layer blit — mipmaps cost more to build than that blit saves. Here
/// the trade reverses: a plain bilinear minification drops thin line art
/// entirely, and thin line art is the one thing a reader of this image must not
/// lose. Do not "unify" the two; they disagree on purpose.
/// </para>
/// <para>
/// <b>Invariant 7 holds.</b> Scaling happens on the composed surface and never on
/// geometry, so no stroke coordinate is touched and no <c>Hash01</c> dab dynamic
/// is re-rolled. Nothing here is seeded or sampled, so two calls on an unchanged
/// document return identical bytes — which is what makes caching them sound.
/// </para>
/// </remarks>
static class OutboundImage
{
    /// <summary>
    /// <paramref name="image"/> scaled so neither side exceeds
    /// <paramref name="longEdge"/>, or null when it already fits and the caller
    /// should use the original.
    /// </summary>
    /// <remarks>
    /// A cap is a ceiling and not a target: an image already inside it is
    /// returned as null rather than upscaled, because upscaling would spend
    /// tokens on pixels the artist never drew. <paramref name="longEdge"/> of 0
    /// or less means the authored size.
    /// </remarks>
    internal static SKImage? Downscaled(SKImage image, int longEdge)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longEdge <= 0 || longest <= longEdge) return null;

        var scale = longEdge / (double)longest;
        var w = Math.Max(1, (int)Math.Round(image.Width * scale));
        var h = Math.Max(1, (int)Math.Round(image.Height * scale));

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null) return null; // no surface, no downscale — send the full size
        surface.Canvas.Clear(SKColors.Transparent);
        // Mipmapped linear: a plain bilinear minification of line art drops thin strokes
        // entirely, which is the one thing the reader must not lose.
        surface.Canvas.DrawImage(
            image,
            new SKRect(0, 0, w, h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }
}
