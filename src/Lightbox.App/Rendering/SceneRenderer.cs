using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The in-progress stroke drawn on top of its layer. Kept separate from the
/// layer bitmap so a live preview never has to copy the layer (a full-canvas
/// copy costs ~1 s at 4K): the dabs accumulate in their own scratch and are
/// composited over the layer here, with the stroke's opacity applied once.
/// An eraser stroke removes only THIS layer's pixels, so the pair is
/// composited in isolation.
/// </summary>
/// <param name="AlphaLocked">
/// Mask the stroke to pixels the layer already had. The mask is the layer
/// bitmap itself, which does not change during a stroke — the dabs live in
/// the scratch — so applying it here gives exactly what the commit will.
/// </param>
/// <param name="Clip">
/// The selection the stroke was started under, or null. Also constant for the
/// stroke's lifetime.
/// </param>
public sealed record StrokeOverlay(
    SKBitmap Scratch,
    double Opacity,
    bool Erases,
    bool AlphaLocked = false,
    ClipRegion? Clip = null)
{
    /// <summary>Whether this overlay needs an isolated layer to be masked in.</summary>
    public bool NeedsMask => AlphaLocked || Clip is not null;
}

/// <summary>One compositing pass: a layer bitmap with optional tint, opacity and blend mode.</summary>
/// <param name="Matrix">
/// A document-space matrix applied to this pass alone, or null for the
/// ordinary case. The transform tool uses it to show a drag before it is
/// committed: the pixels already exist, and moving them for one composite is
/// far cheaper — and far more responsive — than re-mapping the stroke record
/// on every pointer event. The record is only ever touched on apply, so
/// invariant 1 is untouched; this is a preview, not an edit.
/// </param>
/// <param name="Source">
/// Draw only this rectangle of the bitmap, at the origin, or null to draw all
/// of it. An imported reference sheet holds every frame of a run cycle in one
/// image; cutting the cell out here means one decoded bitmap for the whole
/// sheet rather than one per frame, and no copy on the composite path.
/// </param>
/// <param name="SourceFrame">
/// The frame to composite from tiles instead of from <paramref name="Bitmap"/>,
/// set only by the unbounded-canvas pass builder. When present, Bitmap is null
/// and only <c>ComposeUnboundedSnapshot</c> knows what to do — the bounded
/// compositor never receives such a pass, and <c>DrawPass</c> skips it rather
/// than dereferencing nothing.
/// </param>
public sealed record RenderPass(
    SKBitmap? Bitmap,
    SKColor? Tint,
    double Opacity,
    SKBlendMode Blend = SKBlendMode.SrcOver,
    StrokeOverlay? Overlay = null,
    SKMatrix? Matrix = null,
    SKRectI? Source = null,
    Lightbox.Core.Documents.Frame? SourceFrame = null);

/// <summary>
/// Pure SkiaSharp scene compositing: white paper, then passes in order
/// (onion-skin ghosts first, live layers on top). Tinting replaces the pass's
/// color while keeping its alpha — the classic onion-skin look.
/// Runs entirely on the UI thread; the result is an immutable SKImage.
/// </summary>
public static class SceneRenderer
{
    public static SKImage Compose(
        int width, int height, IReadOnlyList<RenderPass> passes,
        SKColor? background = null, SKMatrix? transform = null, double scale = 1.0)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create compose surface.");
        // A bigger render is a bigger surface and a canvas scale — invariant 7.
        // The transform path has already folded the scale into its matrix.
        ComposeInto(surface, passes, background, clip: null, scale, transform);
        return surface.Snapshot();
    }

    /// <summary>
    /// What to clear the composite to. A document with a Background layer
    /// clears to transparent and lets that layer supply the paper, so erasing
    /// it reveals real transparency. Documents saved before background layers
    /// existed still clear to their scene colour, or they would open blank.
    /// </summary>
    public static SKColor BackgroundOf(Lightbox.Core.Documents.Scene scene) =>
        scene.TransparentBackground || scene.Layers.Exists(l => l.IsBackground)
            ? SKColors.Transparent
            : Lightbox.Raster.BrushEngine.ParseColor(scene.BackgroundColor);

    /// <summary>
    /// Composite into an existing (reusable) surface — the hot path during
    /// painting. <paramref name="clip"/> limits the work to a document region
    /// (null = the whole canvas); everything outside it keeps the surface's
    /// previous contents, so a live stroke only repaints what it touched.
    /// </summary>
    /// <param name="transform">
    /// A camera's document-to-device matrix, or null for the plain
    /// uniform-scale path. Null is not "identity": it takes the branch that
    /// existed before cameras did, so a document without one composites
    /// exactly as it always has rather than paying for a matrix concat.
    /// </param>
    public static void ComposeInto(
        SKSurface surface,
        IReadOnlyList<RenderPass> passes,
        SKColor? background = null,
        SKRectI? clip = null,
        double scale = 1.0,
        SKMatrix? transform = null)
    {
        var canvas = surface.Canvas;
        canvas.Save();
        // The clip arrives in document coordinates; the surface may be smaller
        // than the document when the canvas cannot display full detail, and
        // under a camera it is somewhere else entirely.
        if (clip is { } r)
        {
            canvas.ClipRect(CameraTransform.DeviceBounds(r, scale, transform));
        }
        canvas.Clear(background ?? SKColors.White);
        if (transform is { } m) canvas.Concat(m);
        else if (scale != 1.0) canvas.Scale((float)scale);

        foreach (var pass in passes)
        {
            // A pass may carry a matrix of its own — the transform tool's live
            // preview. It nests inside the scene transform rather than
            // replacing it, so the preview lands in document space, which is
            // where the commit will put the strokes.
            if (pass.Matrix is { } passMatrix)
            {
                canvas.Save();
                canvas.Concat(passMatrix);
                DrawPass(canvas, pass);
                canvas.Restore();
            }
            else
            {
                DrawPass(canvas, pass);
            }
        }
        canvas.Restore();
        canvas.Flush();
    }

    private static void DrawPass(SKCanvas canvas, RenderPass pass)
    {
        // A tile-native pass carries no bitmap; only the unbounded compositor
        // can draw it, and it never sends one here.
        if (pass.Bitmap is null) return;
        var alpha = (byte)Math.Round(Math.Clamp(pass.Opacity, 0, 1) * 255);
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(alpha),
            BlendMode = pass.Blend,
        };
        if (pass.Tint is { } tint)
        {
            paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn);
        }

        // A windowed pass is a reference cell: no overlay, no blend, nothing
        // to isolate — just the part of the sheet this frame is showing.
        if (pass.Source is { } window)
        {
            using var sheet = SKImage.FromBitmap(pass.Bitmap);
            if (sheet is not null)
            {
                canvas.DrawImage(
                    sheet, window, SKRect.Create(window.Width, window.Height), Downscale, paint);
            }
            return;
        }

        if (pass.Overlay is not { } overlay)
        {
            DrawLayer(canvas, pass.Bitmap, paint);
            return;
        }

        using var strokePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(
                (byte)Math.Round(Math.Clamp(overlay.Opacity, 0, 1) * 255)),
            BlendMode = overlay.Erases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
        };

        // Isolation is only needed when the stroke must combine with its
        // own layer before that layer meets the ones below — an eraser
        // (which would otherwise cut through everything) or a layer that
        // is transparent or blended. Skipping the offscreen layer in the
        // ordinary case roughly halves the cost of a live repaint.
        var needsIsolation = overlay.Erases || alpha != 255 || pass.Blend != SKBlendMode.SrcOver;
        if (!needsIsolation)
        {
            DrawLayer(canvas, pass.Bitmap, paint);
            DrawStroke(canvas, pass.Bitmap, overlay, strokePaint);
            return;
        }

        // SaveLayer allocates the current clip only, so a bounded live
        // region stays affordable even on a huge canvas.
        canvas.SaveLayer(paint);
        DrawLayer(canvas, pass.Bitmap, null);
        DrawStroke(canvas, pass.Bitmap, overlay, strokePaint);
        canvas.Restore();
    }

    /// <summary>
    /// Draw the in-progress stroke over its layer, masked the way the commit
    /// will mask it.
    ///
    /// Alpha lock and the selection clip used to be applied only when the
    /// stroke was committed, so a locked layer showed the stroke running
    /// across the whole canvas until the pen lifted and then snapped to the
    /// masked shape. An artist cannot judge a mark they are not being shown —
    /// what you draw over and how it lands has to be visible while drawing,
    /// which is how every other painting tool behaves.
    ///
    /// Both masks are constant for the stroke's lifetime — the layer's own
    /// alpha (the dabs are in the scratch, not the layer) and the selection
    /// recorded at stroke start — so applying them at composite time costs one
    /// bounded offscreen and produces exactly the committed result.
    /// </summary>
    private static void DrawStroke(SKCanvas canvas, SKBitmap layer, StrokeOverlay overlay, SKPaint strokePaint)
    {
        if (!overlay.NeedsMask)
        {
            DrawLayer(canvas, overlay.Scratch, strokePaint);
            return;
        }

        // Isolate the stroke so the masks cut the stroke and not the layer
        // under it. SaveLayer allocates the current clip only, so during a
        // stroke this is a dab-sized offscreen, not a canvas-sized one.
        canvas.SaveLayer(strokePaint);

        SKPath? selection = null;
        if (overlay.Clip is { } region)
        {
            selection = Lightbox.Raster.BrushEngine.PathFromContours(region.Contours);
            // A hard selection is a clip: inside this fresh layer, "not drawn"
            // already means "transparent", so clipping IS erasing the outside,
            // and it is exact and free. A DstIn of the path would not be —
            // DstIn only touches pixels the path covers, leaving everything
            // outside it untouched, which is the opposite of a selection.
            if (region.Feather <= 0)
            {
                canvas.Save();
                canvas.ClipPath(selection, antialias: true);
            }
        }

        DrawLayer(canvas, overlay.Scratch, null);

        if (overlay.AlphaLocked)
        {
            using var keep = new SKPaint { BlendMode = SKBlendMode.DstIn };
            DrawLayer(canvas, layer, keep);
        }

        if (selection is not null)
        {
            if (overlay.Clip!.Feather <= 0)
            {
                canvas.Restore();
            }
            else
            {
                // Feathered: erase OUTSIDE the selection, softly. An inverse
                // fill drawn DstOut removes 1-coverage; a Gaussian blur is
                // normalised and linear, so 1 - blur(1 - c) == blur(c), the
                // same mask the commit builds by blurring the fill directly.
                selection.FillType = SKPathFillType.InverseEvenOdd;
                var sigma = (float)(overlay.Clip.Feather / 2);
                using var blur = SKImageFilter.CreateBlur(sigma, sigma);
                using var carve = new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.White,
                    BlendMode = SKBlendMode.DstOut,
                    ImageFilter = blur,
                };
                canvas.DrawPath(selection, carve);
            }
            selection.Dispose();
        }

        canvas.Restore();
    }

    /// <summary>
    /// Blit a layer bitmap at the origin. Going through a zero-copy image
    /// view rather than <c>DrawBitmap</c> matters enormously under a clip:
    /// drawing a 4K bitmap into a small dirty region costs ~5.5 ms the
    /// direct way and ~0.5 ms this way, because Skia stops re-wrapping the
    /// mutable bitmap on every call. The view is a live window onto the same
    /// pixels and never outlives this call.
    /// </summary>
    private static void DrawLayer(SKCanvas canvas, SKBitmap bitmap, SKPaint? paint)
    {
        using var pixels = bitmap.PeekPixels();
        if (pixels is not null)
        {
            using var view = SKImage.FromPixels(pixels);
            if (view is not null)
            {
                canvas.DrawImage(view, 0, 0, Downscale, paint);
                return;
            }
        }
        canvas.DrawBitmap(bitmap, 0, 0, paint);
    }

    /// <summary>
    /// Sampling for the layer blit. Linear is the honest choice when the
    /// compose surface is smaller than the document: nearest aliases thin
    /// line art badly, and mipmaps cost an order of magnitude more to build
    /// than the blit itself saves.
    /// </summary>
    private static readonly SKSamplingOptions Downscale = new(SKFilterMode.Linear);

    public static readonly SKColor OnionPrevTint = new(0xd0, 0x40, 0x40);

    /// <summary>
    /// A tint from settings, falling back rather than throwing. A colour the
    /// user has half-typed must not stop the canvas from drawing.
    /// </summary>
    public static SKColor ParseTint(string? hex, SKColor fallback) =>
        SKColor.TryParse(hex, out var parsed) ? parsed : fallback;
    public static readonly SKColor OnionNextTint = new(0x30, 0x60, 0xc0);

    /// <summary>
    /// Photoshop-style layer blend modes map 1:1 onto Skia's. Delegated to
    /// <see cref="BlendModes"/>, which the brush engine also uses — a brush's
    /// Multiply and a layer's Multiply have to be the same thing.
    /// </summary>
    public static SKBlendMode ToSkia(LayerBlendMode mode) => BlendModes.ToSkia(mode);

    /// <summary>Linear sampling, as the view model used for these draws.</summary>
    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear);

    /// <summary>
    /// Composite a viewport's worth of passes — the route playback takes
    /// (B167 phase 3b).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Moved here from the view model unchanged.</b> It had already become a
    /// pure function of its arguments: phase 3a took the tile branch out, and
    /// with it the last read of <c>_tileFrames</c>, so nothing was left that
    /// belonged to a view model. <c>scene</c>, <c>seq</c> and <c>cameraView</c>
    /// were parameters it no longer used at all.
    /// </para>
    /// <para>
    /// <b>Being here is what lets the draw op call it</b>, which is the whole of
    /// phase 3b: playback's composite now happens on the render thread, where the
    /// graphics context is, instead of on the UI thread. Phase 1 measured the
    /// blending it does at roughly two thirds of Compose — 42.7 ms/tick at 1080p
    /// and 54.6 ms at 4K — which is why this route rather than the culled one.
    /// </para>
    /// <para>
    /// <b>The GPU surface and resident textures come free with the move (phase 4).</b>
    /// Both are the same machinery the culled route already used, and both are
    /// off unless the artist has switched GPU compositing on.
    /// </para>
    /// </remarks>
    internal static SKImage ComposeUnbounded(
        IReadOnlyList<RenderPass> passes,
        SKColor background,
        double renderScale,
        SKRectI viewport,
        GRContext? gpu,
        LayerTextureCache? textures,
        out bool gpuBacked)
    {
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Ceiling(viewport.Width * renderScale)),
            Math.Max(1, (int)Math.Ceiling(viewport.Height * renderScale)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var surface = GpuComposite.CreateSurface(gpu, info, out gpuBacked);
        // Residency is GPU-only and the condition is gpuBacked rather than "there
        // is a context": a refused allocation falls back to a CPU surface, and
        // drawing textures onto that would read back across the bus every pass.
        var resident = gpuBacked && gpu is not null ? textures : null;
        var canvas = surface.Canvas;
        canvas.Clear(background);

        // Document space under the viewport: everything below draws in
        // document coordinates and lands where the artist is looking.
        canvas.Save();
        canvas.Scale((float)renderScale);
        canvas.Translate(-viewport.Left, -viewport.Top);

        foreach (var pass in passes)
        {
            if (pass.Bitmap is null && pass.SourceFrame is null) continue;

            var alpha = (byte)Math.Round(Math.Clamp(pass.Opacity, 0, 1) * 255);
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(alpha),
                BlendMode = pass.Blend,
            };
            if (pass.Tint is { } tint)
            {
                paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn);
            }

            // Mirrors SceneRenderer.DrawPass: an eraser overlay or a
            // translucent/blended layer combines with its own layer before
            // meeting the stack.
            var needsIsolation = pass.Overlay is { Erases: true }
                || alpha != 255
                || pass.Blend != SKBlendMode.SrcOver;
            if (needsIsolation) canvas.SaveLayer(paint);
            var contentPaint = needsIsolation ? null : paint;

            if (pass.SourceFrame is not null)
            {
                // Pre-resolved before the composite (B167 phase 3): a tile-native
                // pass is flattened into a bitmap with a placement matrix by
                // FlattenTilePasses, so nothing here needs the tile cache. That is
                // what lets this whole body move to the render thread — the cache
                // lives on the view model and the draw op cannot reach it.
                //
                // Reaching here means something published a tile pass without
                // going through that step. Skipping is what the bounded
                // compositor already does with one, so it stays a vanished layer
                // rather than a crash — but it should not happen.
                continue;
            }
            else if (pass.Matrix is { } m)
            {
                // A positioned pass — a reference strip. Its matrix nests
                // inside the viewport transform, exactly as it nests inside
                // the scene transform on the bounded path.
                canvas.Save();
                canvas.Concat(m);
                DrawWhole(canvas, pass, contentPaint);
                canvas.Restore();
            }
            else if (pass.Source is not null)
            {
                DrawWhole(canvas, pass, contentPaint);
            }
            else
            {
                // The ordinary layer: draw only the part the viewport can
                // see. The source rectangle is clamped to the bitmap — the
                // viewport of an unbounded canvas extends past every edge of
                // the nominal document, and a source rect off the bitmap is
                // undefined rather than transparent.
                var src = SKRectI.Intersect(
                    viewport, SKRectI.Create(0, 0, pass.Bitmap!.Width, pass.Bitmap.Height));
                if (src.Width > 0 && src.Height > 0)
                {
                    var dst = SKRect.Create(src.Left, src.Top, src.Width, src.Height);
                    if (resident?.Resident(gpu!, pass.Bitmap) is { } texture)
                    {
                        canvas.DrawImage(texture, src, dst, Linear, contentPaint);
                    }
                    else
                    {
                        using var img = SKImage.FromBitmap(pass.Bitmap);
                        if (img is not null) canvas.DrawImage(img, src, dst, Linear, contentPaint);
                    }
                }
            }

            if (pass.Overlay is { } overlay)
            {
                using var strokePaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(
                        (byte)Math.Round(Math.Clamp(overlay.Opacity, 0, 1) * 255)),
                    BlendMode = overlay.Erases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
                };
                var src = SKRectI.Intersect(
                    viewport,
                    SKRectI.Create(0, 0, overlay.Scratch.Width, overlay.Scratch.Height));
                if (src.Width > 0 && src.Height > 0)
                {
                    using var scratch = SKImage.FromBitmap(overlay.Scratch);
                    if (scratch is not null)
                    {
                        canvas.DrawImage(
                            scratch, src,
                            SKRect.Create(src.Left, src.Top, src.Width, src.Height),
                            Linear, strokePaint);
                    }
                }
            }

            if (needsIsolation) canvas.Restore();
        }

        canvas.Restore();
        canvas.Flush();
        return surface.Snapshot();
    }
    private static void DrawWhole(SKCanvas canvas, RenderPass pass, SKPaint? paint)
    {
        using var img = SKImage.FromBitmap(pass.Bitmap);
        if (img is null) return;
        if (pass.Source is { } window)
        {
            canvas.DrawImage(
                img, window, SKRect.Create(window.Width, window.Height), Linear, paint);
        }
        else
        {
            canvas.DrawImage(img, 0, 0, Linear, paint);
        }
    }
}
