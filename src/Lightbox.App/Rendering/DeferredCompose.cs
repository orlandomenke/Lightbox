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
/// <b>The pixels must not change, and the first way that was checked was not
/// enough.</b> This was written to compose exactly what the view model's
/// <c>ComposeViewportCulled</c> composed, and it did — both of them read five
/// fields of a <see cref="RenderPass"/> and ignored the rest, so they agreed
/// with each other perfectly while both disagreed with
/// <see cref="SceneRenderer"/>, which is the path that decides what a mask,
/// an effect, a style and an adjustment layer look like. That is B201's shape
/// exactly, one level up, and it is B309. The reference the tests measure
/// against is now <see cref="SceneRenderer"/> itself, and the copy this was
/// taken from is deleted rather than left to be copied again.
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

            // B309: the fast body below knows about bitmaps, tint, opacity,
            // blend and a live overlay, and nothing else. Every other field a
            // pass can carry — a mask's shapes, the layer's own effect, its
            // styles, an adjustment stack, a per-pass matrix, a reference
            // cell's source window — is drawn by the one implementation that
            // understands it. Skipping them, which is what this loop used to
            // do, is a layer that quietly stops being carved and a grade that
            // quietly stops applying, on the route a zoomed-in artist takes.
            if (NeedsFullFidelity(pass))
            {
                SceneRenderer.DrawOne(surface, canvas, pass, RenderScale, transform: null);
                continue;
            }
            if (pass.Bitmap is null) continue;

            using var paint = new SKPaint { BlendMode = pass.Blend };
            if (pass.Opacity < 1.0)
                paint.Color = paint.Color.WithAlpha((byte)(pass.Opacity * 255));
            // SrcIn, matching SceneRenderer.DrawPass: the tint replaces the
            // pass's colour and keeps its alpha, so a transparent pixel stays
            // transparent. Multiply does the opposite — Skia's blend-mode
            // colour filter takes the tint as source, and Multiply against a
            // transparent destination returns the tint at full alpha, so every
            // empty pixel of a ghost came out solid #d04040 and the canvas
            // flooded red. B201.
            if (pass.Tint.HasValue)
                paint.ColorFilter = SKColorFilter.CreateBlendMode(pass.Tint.Value, SKBlendMode.SrcIn);

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
    /// Whether this pass carries anything the fast body below cannot express,
    /// and must therefore go through <see cref="SceneRenderer.DrawOne"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stated as what the fast path CAN do, not as a list of what is new
    /// (B309).</b> The bug this replaces was a loop that read five fields of a
    /// record with thirteen and ignored the rest in silence; enumerating the
    /// exotic ones here would reintroduce it the next time a field is added.
    /// A pass reaches the fast route only by being plainly ordinary, so a
    /// field added to <see cref="RenderPass"/> tomorrow is drawn correctly
    /// (and more slowly) rather than dropped.
    /// </para>
    /// <para>
    /// The overlay's own mask is in the list for the same reason: an
    /// alpha-locked or clipped live stroke needs the isolation
    /// <see cref="StrokeOverlay.NeedsMask"/> describes, and the fast body
    /// draws the scratch flat. <c>MainViewModel</c> asserts no overlay reaches
    /// this route at all, but that is a <c>Debug.Assert</c> — compiled out of
    /// the build an artist runs, which is the build that must not be wrong.
    /// </para>
    /// </remarks>
    internal static bool NeedsFullFidelity(RenderPass pass) =>
        pass.Shapes is { Count: > 0 }
        || pass.Effect is not null
        || pass.Style is not null
        || pass.AdjustStack is not null
        || pass.Matrix is not null
        || pass.Source is not null
        || pass.Overlay is { NeedsMask: true };

    /// <summary>
    /// Linear, matching what the canvas already uses for the finished frame. The
    /// bitmap path's default is the same, so a resident texture and a fresh
    /// upload resample identically.
    /// </summary>
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);
}
