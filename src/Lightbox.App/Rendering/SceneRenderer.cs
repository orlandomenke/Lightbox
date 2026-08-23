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
/// set only by the tile-native pass builder. When present, Bitmap is null
/// and only <c>ComposeTiledSnapshot</c> knows what to do — the bounded
/// compositor never receives such a pass, and <c>DrawPass</c> skips it rather
/// than dereferencing nothing.
/// </param>
/// <summary>
/// An alpha shape carving a pass: the pass keeps only what the shape covers
/// (DstIn), or loses it when <paramref name="Inverted"/> (DstOut). A layer
/// mask contributes one; a clipping mask contributes the base layer's own
/// render and, when the base is masked too, the base's mask after it — a
/// chain of intersections, applied inside the pass's isolation so opacity and
/// blend see the carved result.
/// </summary>
/// <param name="Scratch">
/// The mask stroke in flight, while the artist is painting the mask itself:
/// dabs that belong to the shape's coverage but are not committed yet. Drawn
/// into the shape before it carves, so the preview is exactly the commit — an
/// artist cannot judge a mark they are not being shown, and a mask mark's
/// look is what it reveals or hides. <paramref name="ScratchErases"/> is the
/// eraser's half: the dabs remove coverage instead of adding it.
/// </param>
public sealed record PassShape(
    SKBitmap Mask, bool Inverted = false,
    SKBitmap? Scratch = null, bool ScratchErases = false);

/// <param name="Shapes">
/// Alpha shapes carving this pass — a layer mask, a clipping base — or null
/// for every unshaped layer, which must keep taking the path that existed
/// before shapes did. See <see cref="PassShape"/>.
/// </param>
/// <param name="Effect">
/// The layer's own effect stack as one Skia filter (DESIGN-effects.md's
/// first attachment), applied to this pass's content in its isolation —
/// before blend and opacity, which is what "the layer's baked output" means.
/// Built with document-space parameters: the save-layer applies it under the
/// canvas matrix, so a blur's sigma follows the zoom on its own.
/// </param>
/// <param name="AdjustStack">
/// An adjustment pass: this pass carries no content of its own — it filters
/// the composite already beneath it (Q151), carved by <paramref name="Shapes"/>
/// and faded by <paramref name="Opacity"/>. The stack rides the pass rather
/// than a baked filter because the backdrop draw happens in device space,
/// where the compositor must scale kernel parameters itself — see
/// <see cref="SceneRenderer.DrawAdjustment"/>.
/// </param>
/// <param name="EffectFrame">The timeline frame keyed parameters evaluate at.</param>
public sealed record RenderPass(
    SKBitmap? Bitmap,
    SKColor? Tint,
    double Opacity,
    SKBlendMode Blend = SKBlendMode.SrcOver,
    StrokeOverlay? Overlay = null,
    SKMatrix? Matrix = null,
    SKRectI? Source = null,
    Lightbox.Core.Documents.Frame? SourceFrame = null,
    IReadOnlyList<PassShape>? Shapes = null,
    SKImageFilter? Effect = null,
    Lightbox.Core.Effects.EffectStack? AdjustStack = null,
    int EffectFrame = 0);

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
            // An adjustment pass reads the surface it is being drawn onto, so
            // only this loop — which owns the surface — can draw it.
            if (pass.AdjustStack is not null)
            {
                DrawAdjustment(surface, canvas, pass, scale, transform);
                continue;
            }
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

    /// <summary>
    /// An adjustment layer's pass: the composite so far, re-drawn through the
    /// stack's filter, carved by the pass's shapes and faded by its opacity —
    /// where the shapes do not cover (or the opacity lets through), the
    /// original pixels beneath simply remain.
    /// </summary>
    /// <remarks>
    /// The snapshot is copy-on-write on the raster backend, so taking one is
    /// cheap; the filtered redraw is bounded by the canvas clip, which the
    /// publish path has already limited to the dirty region. The filter is
    /// built here rather than at materialize time because the snapshot draw
    /// runs at identity — device space — so kernel parameters declared in
    /// document pixels (invariant 7) must be scaled by the device scale by
    /// hand; a save-layer's CTM cannot do it for a draw that has none.
    /// </remarks>
    private static void DrawAdjustment(
        SKSurface surface, SKCanvas canvas, RenderPass pass, double scale, SKMatrix? transform)
    {
        var device = transform is { } m
            ? (float)Math.Sqrt(Math.Abs(m.ScaleX * m.ScaleY - m.SkewX * m.SkewY))
            : (float)scale;
        if (device <= 0) device = 1f;
        // Cached per stack by the registry — nothing here is disposed by us
        // except the bitmaps we make.
        var program = Lightbox.Raster.Effects.EffectRegistry.ProgramFor(
            pass.AdjustStack, pass.EffectFrame, device);
        if (program is null) return; // a stack that currently does nothing

        canvas.Flush();

        var alpha = (byte)Math.Round(Math.Clamp(pass.Opacity, 0, 1) * 255);
        using var group = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };

        if (program.SoleFilter is { } filter)
        {
            // All-native: one filtered redraw of the surface, bounded by the
            // clip at draw time. The snapshot is copy-on-write — cheap.
            using var snapshot = surface.Snapshot();
            canvas.SaveLayer(group);
            // The backdrop is device-space pixels: draw it back one-to-one,
            // under the clip but outside the document transform.
            canvas.Save();
            canvas.SetMatrix(SKMatrix.CreateIdentity());
            using (var paint = new SKPaint { ImageFilter = filter })
            {
                canvas.DrawImage(snapshot, 0, 0, paint);
            }
            canvas.Restore();
        }
        else
        {
            // A CPU step somewhere (a true-HSL grade): read back only the
            // clip, inflated by the stack's reach so a kernel step still
            // sees the pixels it spills from — invariant 6's bound, kept.
            using var full = surface.Snapshot();
            var reach = (int)Math.Ceiling(
                Lightbox.Raster.Effects.EffectRegistry.ReachOf(
                    pass.AdjustStack, pass.EffectFrame) * device);
            var clip = canvas.DeviceClipBounds;
            var left = Math.Max(0, clip.Left - reach);
            var top = Math.Max(0, clip.Top - reach);
            var right = Math.Min(full.Width, clip.Right + reach);
            var bottom = Math.Min(full.Height, clip.Bottom + reach);
            if (right <= left || bottom <= top) return;
            var rect = new SKRectI(left, top, right, bottom);

            using var subset = full.Subset(rect);
            var worked = SKBitmap.FromImage(subset); // ApplyTo takes ownership
            var processed = Lightbox.Raster.Effects.EffectRegistry.ApplyTo(worked, program);
            canvas.SaveLayer(group);
            canvas.Save();
            canvas.SetMatrix(SKMatrix.CreateIdentity());
            canvas.DrawBitmap(processed, rect.Left, rect.Top);
            canvas.Restore();
            processed.Dispose();
        }

        // Shapes are document-space bitmaps and carve under the transform,
        // exactly as they carve an ordinary pass.
        if (pass.Shapes is { Count: > 0 } shapes) ApplyShapes(canvas, shapes);
        canvas.Restore();
    }

    private static void DrawPass(SKCanvas canvas, RenderPass pass)
    {
        // A tile-native pass carries no bitmap; only the tiled compositor
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

        var shaped = pass.Shapes is { Count: > 0 };
        var fx = pass.Effect;

        if (pass.Overlay is not { } overlay)
        {
            if (!shaped && fx is null)
            {
                DrawLayer(canvas, pass.Bitmap, paint);
                return;
            }
            // The shapes must carve the layer alone, so it is isolated and the
            // paint — opacity, blend, tint — applies to the carved result on
            // restore, exactly as it would have applied to the whole layer.
            // A self effect filters the content *first*, in a group of its
            // own, so a mask still cuts a crisp edge through a blurred layer
            // rather than blurring the cut.
            canvas.SaveLayer(paint);
            DrawFiltered(canvas, pass.Bitmap, fx);
            if (shaped) ApplyShapes(canvas, pass.Shapes!);
            canvas.Restore();
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
        // A shaped or filtered pass always isolates: the shapes and the
        // effect must take the layer and its live stroke together, and
        // nothing else.
        var needsIsolation = shaped || fx is not null
            || overlay.Erases || alpha != 255 || pass.Blend != SKBlendMode.SrcOver;
        if (!needsIsolation)
        {
            DrawLayer(canvas, pass.Bitmap, paint);
            DrawStroke(canvas, pass.Bitmap, overlay, strokePaint);
            return;
        }

        // SaveLayer allocates the current clip only, so a bounded live
        // region stays affordable even on a huge canvas.
        canvas.SaveLayer(paint);
        if (fx is not null)
        {
            // The live stroke sits inside the filter group on purpose: a
            // blurred layer blurs the mark being made on it, which is what
            // the commit will show.
            using var fxPaint = new SKPaint { ImageFilter = fx };
            canvas.SaveLayer(fxPaint);
        }
        DrawLayer(canvas, pass.Bitmap, null);
        DrawStroke(canvas, pass.Bitmap, overlay, strokePaint);
        if (fx is not null) canvas.Restore();
        if (shaped) ApplyShapes(canvas, pass.Shapes!);
        canvas.Restore();
    }

    /// <summary>The pass's content through its own effect group, or plainly without one.</summary>
    private static void DrawFiltered(SKCanvas canvas, SKBitmap bitmap, SKImageFilter? fx)
    {
        if (fx is null)
        {
            DrawLayer(canvas, bitmap, null);
            return;
        }
        using var fxPaint = new SKPaint { ImageFilter = fx };
        canvas.SaveLayer(fxPaint);
        DrawLayer(canvas, bitmap, null);
        canvas.Restore();
    }

    /// <summary>
    /// Carve the isolated pass by each shape in turn. Order does not matter
    /// for the keeps (intersection commutes); an inverted shape subtracts.
    /// </summary>
    private static void ApplyShapes(SKCanvas canvas, IReadOnlyList<PassShape> shapes)
    {
        foreach (var shape in shapes)
        {
            using var carve = new SKPaint
            {
                BlendMode = shape.Inverted ? SKBlendMode.DstOut : SKBlendMode.DstIn,
            };
            if (shape.Scratch is null)
            {
                DrawLayer(canvas, shape.Mask, carve);
                continue;
            }
            // A mask being painted: the committed coverage and the dabs in
            // flight are one shape, so they group before the carve — the
            // same isolate-then-apply the stroke overlay itself uses.
            canvas.SaveLayer(carve);
            DrawLayer(canvas, shape.Mask, null);
            using var dabs = new SKPaint
            {
                BlendMode = shape.ScratchErases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
            };
            DrawLayer(canvas, shape.Scratch, dabs);
            canvas.Restore();
        }
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
    internal static SKImage ComposeTiled(
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
                // A positioned pass — a reference strip, or a flattened tile
                // pass, which is every layer on this route. Its matrix nests
                // inside the viewport transform, exactly as it nests inside
                // the scene transform on the bounded path.
                //
                // Residency is passed through here because this is the arm the
                // tiled route actually takes: without it the flatten cache's
                // stable bitmaps were re-uploaded every frame. See DrawWhole.
                canvas.Save();
                canvas.Concat(m);
                DrawWhole(canvas, pass, contentPaint, resident, gpu);
                canvas.Restore();
            }
            else if (pass.Source is not null)
            {
                DrawWhole(canvas, pass, contentPaint, resident, gpu);
            }
            else
            {
                // The ordinary layer: draw only the part the viewport can
                // see. The source rectangle is clamped to the bitmap — a
                // zoomed-out viewport extends past every edge of the
                // document, and a source rect off the bitmap is undefined
                // rather than transparent.
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
    /// <summary>
    /// Draw a whole pass — a positioned strip, or a flattened tile pass under its
    /// placement matrix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This took a resident texture only after the first GPU-on render report
    /// showed why it had to (2026-08-12).</b> That capture read
    /// <c>310 publishes composited on the card</c> and, three sections later,
    /// <c>no layer textures were asked for</c> — a contradiction that turned out
    /// to be a wiring gap rather than a measurement error.
    /// </para>
    /// <para>
    /// <b>Every flattened tile pass carries a placement matrix</b>
    /// (<c>MainViewModel.FlattenTilePasses</c>), so on the tiled route every pass
    /// took the matrix arm and arrived here — and this method called
    /// <see cref="SKImage.FromBitmap"/> unconditionally, which uploads the pixels
    /// again on every draw. Only the ordinary-layer arm consulted residency, and
    /// on the route playback takes nothing reaches it. So the cache B125 stage 5
    /// built was never asked a single question on the one path it was meant to
    /// serve.
    /// </para>
    /// <para>
    /// <b>What makes these worth keeping resident is B167 phase 2.</b> The flatten
    /// cache made these bitmaps stable across frames — the same capture measured
    /// <b>99% reuse, 867 of 876 passes</b> — and then this method threw that
    /// stability away by re-uploading a 31.6 MB bitmap per layer per frame at 4K.
    /// A stable bitmap re-uploaded every frame is precisely the case a texture
    /// cache exists for.
    /// </para>
    /// <para>
    /// <b>What this is not:</b> it is not phase 5. Phase 5 uploads the <em>tiles</em>
    /// and composites them on the card, skipping the flatten entirely. This keeps
    /// the flatten and stops re-uploading its result — a fraction of the work, and
    /// the honest way to find out whether the upload was ever the cost. Phase 5
    /// stays blocked on the re-measurement this makes possible.
    /// </para>
    /// </remarks>
    /// <param name="resident">
    /// Non-null only when the caller has a GPU-backed surface, which is why
    /// <paramref name="gpu"/> is dereferenced with <c>!</c> below rather than
    /// re-checked — the same idiom the ordinary-layer arm above uses, and the
    /// reason it is an idiom is that two checks of one invariant can disagree.
    /// </param>
    internal static void DrawWhole(
        SKCanvas canvas, RenderPass pass, SKPaint? paint,
        LayerTextureCache? resident = null, GRContext? gpu = null)
    {
        if (pass.Bitmap is not { } bitmap) return;

        // A resident texture is already on the card, so it is drawn directly
        // rather than wrapped — SKImage.FromBitmap is the upload this avoids.
        if (resident?.Resident(gpu!, bitmap) is { } texture)
        {
            DrawImage(canvas, texture, pass, paint);
            return;
        }

        using var img = SKImage.FromBitmap(bitmap);
        if (img is null) return;
        DrawImage(canvas, img, pass, paint);
    }

    private static void DrawImage(SKCanvas canvas, SKImage img, RenderPass pass, SKPaint? paint)
    {
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
