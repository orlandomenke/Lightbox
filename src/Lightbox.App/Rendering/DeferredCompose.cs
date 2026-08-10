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
/// <item><b>Unbounded</b> reads the tile caches, which the view model owns. It
/// stays until those move.</item>
/// </list>
/// <para>
/// The culled route is also the one that matters most for the GPU: it is what
/// runs on a whole-canvas publish while zoomed in, which is a frame change at
/// 4K — exactly the case B125 exists for.
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
    SKRectI Viewport)
{
    /// <summary>
    /// Perform it. On the render thread, with the context from the lease when
    /// there is one.
    /// </summary>
    public SKImage Compose(GRContext? gpu, out bool gpuBacked)
    {
        using var surface = GpuComposite.CreateSurface(gpu, Info, out gpuBacked);
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

            // Only the visible sub-rectangle is read, which is where the saving is:
            // src and dst are the same rectangle in document space, so no scaling
            // beyond RenderScale and no resampling of the parts nobody can see.
            canvas.DrawBitmap(pass.Bitmap, visible, visible, paint);

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
        }

        canvas.Flush();
        return surface.Snapshot();
    }
}
