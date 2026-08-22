using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Character-sheet views flattened to base64 PNG, capped on the way out, and cached
/// until the document changes.
/// </summary>
/// <remarks>
/// <para>
/// Tier 1 of <c>docs/DESIGN-mainviewmodel-decomposition.md</c>, extracted under Q78
/// from inside the <c>character sheets</c> section — which owned almost nothing else,
/// being 590 lines of public API over document state. This is the part of it with a
/// job.
/// </para>
/// <para>
/// <b>B31 is the whole reason the cache exists.</b> Compose + PNG + base64 cost
/// <b>52 ms for one 960×540 view</b>, on the UI thread, before every AI call — about
/// 100 ms of stall for the default two views, producing byte-identical output each
/// time because the sheet had not changed. The per-layer render was already memoised
/// by the frame cache; the expensive half was not.
/// </para>
/// <para>
/// <b>Invalidated from the edit funnel, not from the document-changed handler.</b> B31
/// proposed the latter and it would have been wrong in the dangerous direction:
/// <c>OnDocumentChanged</c> takes an early return for scoped edits, and a stroke commit
/// is exactly that — so a cache hung off it would survive the edit and hand the model a
/// picture of art the artist had already changed. Wrong quietly, which is worse than
/// slow. Cleared wholesale rather than per view, because over-invalidating costs one
/// re-encode and under-invalidating costs correctness.
/// </para>
/// <para>
/// <b>Two sizes, and the difference is a contract rather than a default.</b>
/// <see cref="Render"/> with <c>longEdge</c> of 0 or less gives the view at the size it
/// was drawn, which is what the MCP surface answers <c>render_reference_view</c> with —
/// an agent asking for a picture of a view should get the view. The
/// <see cref="LongEdge"/> cap belongs to an AI <i>request</i>, where providers bill by
/// area, and it is applied at exactly one call site (<see cref="Encoded"/>). Capping
/// the other overload shrank the MCP reply as a side effect of B31 and was caught by
/// <c>RenderReferenceView_ProducesDecodablePng</c>.
/// </para>
/// <para>
/// <b>Determinism.</b> Scaling happens on the composed surface, never on geometry — the
/// same rule as invariant 7, and no stroke coordinate is touched. Nothing here is
/// seeded or sampled, so two calls on an unchanged document return identical bytes,
/// which is what makes caching them sound in the first place. Note that Q27's per-view
/// heuristic must preserve that when it lands: a cap derived from the view's own content
/// is still a pure function of the view, but a cap derived from anything else — a clock,
/// a request counter, a remembered previous choice — would make two requests on an
/// unchanged document send different pictures.
/// </para>
/// </remarks>
sealed class ReferenceViewImages(FrameBitmapCache frames)
{
    /// <summary>
    /// The longest edge of a reference image sent to a model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Capped on the way out, never on the view.</b> An artist's sheet stays whatever
    /// size they drew it; this is only what leaves the machine. Providers bill by area
    /// regardless of file size, so per <c>docs/DESIGN-ai-payload.md</c> a 768 px long edge
    /// is <b>442 image tokens against 691, and 244 KB against 333 KB</b> for a 960×540
    /// view. A cap is a ceiling and not a target: a 400×300 sheet is sent at 400×300,
    /// because upscaling would spend tokens on pixels the artist never drew.
    /// </para>
    /// <para>
    /// <b>A flat 768 is not the settled answer, and it has a measured failure mode.</b>
    /// Do not read this constant as "the right size" — a body silhouette survives even
    /// 512, but art-director rendered face close-ups through this exact path and found
    /// <b>eyebrows vanish and eyes turn to grey smudges</b>: mipmapped minification greys
    /// a thin dark line toward the ground rather than keeping it crisp-and-small, and
    /// those lines were drawn at pressure 0.25–0.5 where pressure drives flow as well as
    /// size, so they were hairline already. The cap made a marginal line invisible rather
    /// than a solid line marginal.
    /// </para>
    /// <para>
    /// <b>Q27 is answered (d), 2026-08-07: choose the cap per view from what is in it</b> —
    /// one number never fitted both a face turnaround and a walk-cycle sheet. That is not
    /// built yet, and this constant is what stands in for it. When it is built, the answer
    /// carries three guards Q27 recorded as conditions rather than extras: the request
    /// shows what cap each view got, a view can be pinned to a size, and the heuristic is
    /// a pure function of the view tested on the fixtures <see cref="Render"/> already
    /// has — because a number nobody can predict changing what the model sees is the one
    /// failure mode the objection to (d) named.
    /// </para>
    /// </remarks>
    internal const int LongEdge = 768;

    /// <summary>Encoded reference views, keyed by view id. See the class remarks (B31).</summary>
    private readonly Dictionary<string, string> _encoded = new(StringComparer.Ordinal);

    /// <summary>Throw away encoded views — something in the document moved.</summary>
    internal void Invalidate() => _encoded.Clear();

    /// <summary>
    /// One view as base64 PNG, no wider or taller than <paramref name="longEdge"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="longEdge"/> of 0 or less means the authored size. Explicit rather
    /// than defaulted, so a new caller has to say which of the two it wants.
    /// </remarks>
    internal string Render(ReferenceView view, int longEdge)
    {
        var passes = new List<RenderPass>();
        for (var layerIndex = 0; layerIndex < view.Layers.Count; layerIndex++)
        {
            var layer = view.Layers[layerIndex];
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, 0);
            if (frame is null) continue;
            // The view shows the referenced document as its own canvas would,
            // masks and clipping included.
            var shapes = LayerShapes.For(view.Layers, l => l.Visible, layerIndex, 0);
            if (shapes is { Count: 0 }) continue;
            passes.Add(new RenderPass(
                frames.Get(frame, view.Width, view.Height), null,
                layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode),
                Shapes: LayerShapes.Resolve(shapes, frames, view.Width, view.Height)));
        }

        // Composed at the authored size so the warm per-layer cache entries are the ones
        // every other consumer already made, then scaled once.
        using var image = SceneRenderer.Compose(view.Width, view.Height, passes);
        using var sized = Downscaled(image, longEdge);
        using var data = (sized ?? image).Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>
    /// One view as base64 PNG at the request cap, cached until the document changes.
    /// </summary>
    internal string Encoded(ReferenceView view)
    {
        if (_encoded.TryGetValue(view.Id, out var cached)) return cached;
        // The one place the cap applies: this is a request, and a request is billed by area.
        var encoded = Render(view, LongEdge);
        _encoded[view.Id] = encoded;
        return encoded;
    }

    /// <summary>The image no larger than <paramref name="longEdge"/>, or null if it already is.</summary>
    private static SKImage? Downscaled(SKImage image, int longEdge)
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
        // entirely, which is the one thing the model must not lose.
        surface.Canvas.DrawImage(
            image,
            new SKRect(0, 0, w, h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }
}
