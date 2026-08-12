using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// A composite the publisher described and did not perform, for the canvas to
/// perform inside the draw op — where the graphics context is.
/// </summary>
/// <remarks>
/// <para>
/// <b>B125 stage 3b: the inversion, on the one route where it costs nothing.</b>
/// The design note's first crux is that the <c>GRContext</c> is not where the
/// composite is — <c>PublishSnapshot</c> composites on the UI thread and the
/// context lives only inside the draw op on the render thread. Moving the
/// composite is therefore an inversion rather than a substitution.
/// </para>
/// <para>
/// <b>Only the culled route moves, and the reason is a real constraint rather
/// than caution.</b> Of the three compositors:
/// </para>
/// <list type="bullet">
/// <item><b>Culled</b> already builds a fresh surface every publish and fills all
/// of it, so nothing whatever is lost by building it somewhere else. It moves.</item>
/// <item><b>Ring</b> exists to reuse three buffers and repaint only a dirty
/// region — B121 measured what losing that costs: a dab-sized repaint becoming a
/// viewport-sized one, 1 232 px against 134 400 px. Moving it without first
/// moving the buffers would be that regression. It stays until stage 6 retires
/// it properly.</item>
/// <item><b>Tiled</b> reads the tile caches, which the view model owns. It
/// stays until those move.</item>
/// </list>
/// <para>
/// <b>The culled route was described here as "the one that matters most", and
/// the first real render reports refuted that (2026-08-10).</b> A playing
/// document takes the <em>tiled</em> compositor, because
/// <c>tileModeOn = IsPlaying</c> — so this route, and
/// everything B125 built on it, applies to a path playback never takes. The
/// reports showed 1756 tiled layer passes against 68 layer draws through the
/// texture cache. It is still the right route to have moved first, because it
/// was the one that could move without relocating the tile caches; it is simply
/// not the one that reaches playback.
/// </para>
/// <para>
/// <b>The pixels must not change.</b> This composes exactly what
/// <c>ComposeViewportCulled</c> composed, in the same order, with the same
/// transform; <c>ComposeIdentityTests</c> is the harness that says so, and
/// <c>DeferredComposeTests</c> is where it is said.
/// </para>
/// </remarks>
/// <param name="Passes">
/// The layers to blend. Borrowed from the frame cache and pinned for the life of
/// the snapshot carrying this — see <see cref="RenderSnapshot.Passes"/>.
/// </param>
/// <param name="Background">The paper colour under everything.</param>
/// <param name="RenderScale">The compose scale, applied as a canvas transform.</param>
/// <param name="Info">The surface to compose into.</param>
/// <param name="Viewport">
/// The document rectangle this surface covers. Every pass draws at its own
/// document coordinates and the origin is shifted here, which is what keeps a
/// culled compose identical to an unculled one inside the clip.
/// </param>
public readonly record struct DeferredCompose(
    IReadOnlyList<RenderPass> Passes,
    SKColor Background,
    double RenderScale,
    SKImageInfo Info,
    SKRectI Viewport,
    bool Tiled = false)
{
    /// <summary>
    /// Perform it. On the render thread, with the context from the lease when
    /// there is one.
    /// </summary>
    public SKImage Compose(GRContext? gpu, out bool gpuBacked) => Compose(gpu, null, out gpuBacked);

    /// <inheritdoc cref="Compose(GRContext?, out bool)"/>
    /// <param name="textures">
    /// Resident layer textures (B125 stage 5), or null to upload nothing and
    /// draw the CPU bitmaps directly. **Consulted only when the surface is
    /// actually GPU-backed**: on the CPU path a texture would be a pointless
    /// round trip, and — more importantly — stage 3b promised these pixels are
    /// byte-identical to what the publisher produced, so the CPU path must not
    /// acquire a second implementation.
    /// </param>
    public SKImage Compose(GRContext? gpu, LayerTextureCache? textures, out bool gpuBacked)
    {
        if (Tiled)
        {
            // The route playback takes (B167 phase 3b). Its geometry is not this
            // one's — passes draw clamped to the viewport, references carry their
            // own matrix, and tile passes have already been flattened into
            // matrix'd bitmaps by the publisher. Kept as a separate body rather
            // than merged into the one below, because merging two compose bodies
            // is where pixels drift and nothing would say so.
            return SceneRenderer.ComposeTiled(
                Passes, Background, RenderScale, Viewport, gpu, textures, out gpuBacked);
        }

        using var surface = GpuComposite.CreateSurface(gpu, Info, out gpuBacked);
        // Residency is a GPU-only optimisation, and the condition is deliberately
        // `gpuBacked` rather than `gpu is not null`: an allocation the driver
        // refused falls back to a CPU surface, and drawing textures onto that
        // would read back across the bus every pass.
        var resident = gpuBacked && gpu is not null ? textures : null;
        var canvas = surface.Canvas;
        canvas.Clear(Background);

        // Document space, offset so the viewport's top-left is the surface origin.
        // Every pass then draws at its own document coordinates, exactly as it
        // would into a full-document surface — which is the point: the passes do
        // not learn about culling, so a culled and an unculled compose agree.
        canvas.Scale((float)RenderScale, (float)RenderScale);
        canvas.Translate(-Viewport.Left, -Viewport.Top);

        var visible = new SKRect(Viewport.Left, Viewport.Top, Viewport.Right, Viewport.Bottom);

        for (var i = 0; i < Passes.Count; i++)
        {
            var pass = Passes[i];
            if (pass.Bitmap is null) continue;

            using var paint = new SKPaint { BlendMode = pass.Blend };
            if (pass.Opacity < 1.0)
                paint.Color = paint.Color.WithAlpha((byte)(pass.Opacity * 255));
            if (pass.Tint.HasValue)
                paint.ColorFilter = SKColorFilter.CreateBlendMode(pass.Tint.Value, SKBlendMode.Multiply);

            // B169: an eraser overlay must combine with its own layer before the
            // layer meets the stack — DstOut straight onto the shared surface
            // removes the paper beneath, which is exactly what the artist saw
            // (the checkerboard while erasing). Same rule as SceneRenderer's
            // DrawPass and ComposeTiled, restored here because this body was
            // written after the isolation and left it behind. Only for passes
            // that need it: the SaveLayer is a viewport-sized offscreen, and
            // needsIsolation exists to avoid paying that in the ordinary case.
            var needsIsolation = pass.Overlay is not null
                && (pass.Overlay.Erases || pass.Opacity < 1.0 || pass.Blend != SKBlendMode.SrcOver);
            if (needsIsolation) canvas.SaveLayer(paint);
            var contentPaint = needsIsolation ? null : paint;

            // Only the visible sub-rectangle is read, which is where the saving is:
            // src and dst are the same rectangle in document space, so no scaling
            // beyond RenderScale and no resampling of the parts nobody can see.
            if (resident?.Resident(gpu!, pass.Bitmap) is { } texture)
            {
                canvas.DrawImage(texture, visible, visible, Sampling, contentPaint);
            }
            else
            {
                canvas.DrawBitmap(pass.Bitmap, visible, visible, contentPaint);
            }

            if (pass.Overlay is { } overlay)
            {
                using var overlayPaint = new SKPaint
                {
                    BlendMode = overlay.Erases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
                };
                if (overlay.Opacity < 1.0)
                    overlayPaint.Color = overlayPaint.Color.WithAlpha((byte)(overlay.Opacity * 255));
                canvas.DrawBitmap(overlay.Scratch, visible, visible, overlayPaint);
            }

            if (needsIsolation) canvas.Restore();
        }

        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// Linear, matching what the canvas already uses for the finished frame. The
    /// bitmap path's default is the same, so a resident texture and a fresh
    /// upload resample identically.
    /// </summary>
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);
}
