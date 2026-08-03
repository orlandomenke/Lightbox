using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The stamp-based brush. Walks a stroke's path by arc length and stamps
/// dabs — round soft/hard dabs or custom tip bitmaps — whose radius, alpha,
/// rotation and position follow pressure and the brush's parameters.
///
/// Paint strokes are stamped at dab (flow) alpha onto a scratch surface,
/// then composited once at the stroke's opacity — so overlapping dabs inside
/// one stroke build up by flow but never exceed the opacity cap, matching how
/// painting apps behave. Smudge and blur brushes operate on the target
/// pixels directly (they need to read the canvas).
///
/// Everything here is DETERMINISTIC: scatter, rotation jitter and paper
/// granulation are seeded from dab positions, never from an RNG — a stroke
/// record always re-renders to the same pixels. This is deliberately the
/// ONLY place pixels are produced from strokes: live painting, inbetween
/// re-render, AI strokes, and undo re-render all call into it, which is what
/// makes generated frames indistinguishable from hand-painted ones.
/// </summary>
public static class BrushEngine
{
    private const double MinPressure = 0.05;
    private const double MinStepPx = 0.5;

    /// <summary>
    /// Stamp a stroke onto <paramref name="target"/>. Brush strokes composite
    /// SrcOver; eraser strokes composite DstOut (they remove layer content).
    /// <paramref name="targetPixels"/> gives effect brushes (smudge/blur)
    /// read access to the canvas; without it they degrade to paint.
    ///
    /// <paramref name="draft"/> is the LIVE-PREVIEW path: it stamps into a
    /// scratch surface bounded to the new dabs (not the whole canvas) and
    /// skips the stroke-global effects (wet edge, granulation, feathered
    /// clips), so per-pointer-event cost is proportional to the segment, not
    /// the document. The committed stroke is always re-rendered exactly
    /// through the non-draft path — draft never touches the record.
    /// </summary>
    /// <param name="info">
    /// The DOCUMENT size, always — stroke geometry and the bounds derived from
    /// it stay in document coordinates whatever <paramref name="outputScale"/>
    /// says.
    /// </param>
    /// <param name="outputScale">
    /// Pixels per document unit in the target. A render at 2 produces a
    /// sharper image of the same stroke, not a different one: the geometry is
    /// never multiplied, only the surfaces it is rasterised into. See
    /// <see cref="Hash01"/> — every dynamic is seeded from a dab's document
    /// position, and scaling coordinates would re-roll all of them.
    /// Ignored on the draft path, which is display-only and always 1.
    /// </param>
    /// <param name="backdrop">
    /// The composite of everything beneath this layer, for a stroke that asked
    /// to sample all of them. Null when there is nothing underneath or the
    /// caller cannot supply it, in which case an all-layers stroke falls back
    /// to its own layer — the same mark it would have made before the option
    /// existed, rather than nothing.
    /// </param>
    public static void StampStroke(
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels = null,
        bool draft = false, double outputScale = 1.0, SKBitmap? backdrop = null)
    {
        if (stroke.Points.Count == 0) return;

        if (stroke.Tool == ToolKind.Fill)
        {
            StampFill(target, stroke, info, outputScale);
            return;
        }

        if (stroke.Tool == ToolKind.Gradient)
        {
            StampGradient(target, stroke, info, targetPixels, outputScale);
            return;
        }

        switch (stroke.Brush.Kind)
        {
            case BrushKind.Smudge when targetPixels is not null && stroke.Tool != ToolKind.Eraser:
            {
                using var owned = SampledSource(stroke, targetPixels, backdrop, info);
                var read = owned ?? targetPixels;
                WithHardClip(target, stroke, outputScale, () => StampSmudge(target, read, stroke, outputScale));
                return;
            }

            case BrushKind.Blur when targetPixels is not null && stroke.Tool != ToolKind.Eraser:
            {
                using var owned = SampledSource(stroke, targetPixels, backdrop, info);
                var read = owned ?? targetPixels;
                if (draft) WithHardClip(target, stroke, 1.0, () => StampBlurDraft(target, read, stroke, info));
                else WithHardClip(target, stroke, outputScale, () => StampBlur(target, read, stroke, info, outputScale));
                return;
            }
        }

        if (draft) StampPaintDraft(target, stroke, info, targetPixels);
        else StampPaint(target, stroke, info, targetPixels, outputScale);
    }

    /// <summary>
    /// What a smudge or blur actually reads, or null to read its own layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layer is composited <em>over</em> whatever is beneath rather than
    /// replacing it, because "sample all layers" means what you can see: the
    /// artist is smudging the picture, and the picture at that point is this
    /// layer's paint on top of everything below it. Reading the backdrop alone
    /// would smudge the background through the very strokes being blended into
    /// it.
    /// </para>
    /// <para>
    /// A baked stroke reads what it froze, which is why it needs neither the
    /// caller's backdrop nor an invalidation rule: it carries its own.
    /// </para>
    /// </remarks>
    private static SKBitmap? SampledSource(
        Stroke stroke, SKBitmap layer, SKBitmap? backdrop, SKImageInfo info)
    {
        // No ThisLayer fast path: neither branch below matches it, so an early
        // return would be a line no test could distinguish from its absence.
        var source = stroke.Brush.SampleSource;
        if (source == SampleSource.ThisLayer) return null;

        SKBitmap? beneath = null;
        var owned = false;
        if (source == SampleSource.AllLayersLive && backdrop is not null)
        {
            // A caller that can hand over the stack at render time wins.
            beneath = backdrop;
        }
        else if (stroke.Baked is { PngBase64.Length: > 0 } baked)
        {
            // Otherwise the stroke's own frozen sample, and that is the normal
            // path for BOTH modes. Live is kept current by re-freezing it
            // whenever a layer beneath changes rather than by re-reading the
            // stack on every render — one place that can be wrong instead of
            // one per render path. The difference between the two modes is
            // therefore when the sample is refreshed, not how it is read.
            beneath = BakedBackdrop(baked, layer.Width, layer.Height);
            owned = true;
        }
        if (beneath is null) return null;

        var merged = new SKBitmap(
            new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(merged))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(beneath, 0, 0);
            canvas.DrawBitmap(layer, 0, 0);
            canvas.Flush();
        }
        if (owned) beneath.Dispose();
        return merged;
    }

    /// <summary>A baked sample expanded back to the layer's own frame.</summary>
    private static SKBitmap? BakedBackdrop(BakedSample baked, int width, int height)
    {
        SKBitmap? patch = null;
        try
        {
            patch = PngCodec.Decode(baked.PngBase64);
        }
        catch
        {
            // A corrupt sample falls back to reading the layer, which is the
            // mark this stroke would have made without the option. Losing the
            // blend is a smaller failure than refusing to open the document.
            return null;
        }
        var full = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(full))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(patch, baked.X, baked.Y);
            canvas.Flush();
        }
        patch.Dispose();
        return full;
    }

    /// <summary>
    /// Freeze what a stroke sampled, cropped to the pixels it can reach.
    /// </summary>
    /// <remarks>
    /// Called once, when the stroke is committed. The crop is what keeps this
    /// affordable: a smudge reaches a few hundred pixels, and storing the whole
    /// canvas per stroke would put a megabyte in the record for a mark the size
    /// of a thumbnail.
    /// </remarks>
    public static BakedSample? BakeSample(Stroke stroke, SKBitmap backdrop, SKImageInfo info)
    {
        if (CommitBounds(stroke, info) is not { } rect) return null;
        rect.Intersect(new SKRectI(0, 0, backdrop.Width, backdrop.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return null;

        using var patch = new SKBitmap(
            new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(patch))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(backdrop, -rect.Left, -rect.Top);
            canvas.Flush();
        }
        return new BakedSample { PngBase64 = PngCodec.Encode(patch), X = rect.Left, Y = rect.Top };
    }

    /// <summary>
    /// Restrict a stroke to pixels the layer already had. The mask is the
    /// target's own alpha as it stands right now, which is exactly right:
    /// strokes are stamped in record order, so "now" is the content before
    /// this stroke. That means nothing has to be stored with the stroke
    /// beyond the flag, and a reload reconstructs the same mask for free.
    /// </summary>
    private static void ApplyAlphaLock(SKCanvas scratchCanvas, Stroke stroke, SKBitmap? targetPixels, SKRectI dev)
    {
        if (!stroke.AlphaLocked || targetPixels is null) return;
        using var pixels = targetPixels.PeekPixels();
        if (pixels is null) return;
        using var existing = SKImage.FromPixels(pixels);
        if (existing is null) return;

        // Keep only where the layer already has paint. The layer bitmap is at
        // the same resolution as the scratch (both come from the render's
        // output scale), so this is a straight offset by the scratch's origin.
        using var keep = new SKPaint { BlendMode = SKBlendMode.DstIn };
        scratchCanvas.DrawImage(existing, -dev.Left, -dev.Top, keep);
    }

    /// <summary>An even-odd path from closed contours (fill regions, selections).</summary>
    public static SKPath PathFromContours(IEnumerable<IReadOnlyList<StrokePoint>> contours)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            path.MoveTo((float)contour[0].X, (float)contour[0].Y);
            for (var i = 1; i < contour.Count; i++)
            {
                path.LineTo((float)contour[i].X, (float)contour[i].Y);
            }
            path.Close();
        }
        return path;
    }

    /// <summary>A filled region stroke: outer contour + holes, even-odd, at stroke opacity.</summary>
    private static void StampFill(SKCanvas target, Stroke stroke, SKImageInfo info, double outputScale)
    {
        if (stroke.Points.Count < 3) return;
        // A fill is contours, so it rasterises sharp at any output scale.
        var dev = DeviceRect(new SKRectI(0, 0, info.Width, info.Height), outputScale);
        var local = new SKImageInfo(dev.Width, dev.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        InDocumentSpace(canvas, dev, outputScale, () =>
        {
            var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
            if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
            using var path = PathFromContours(contours);
            using var paint = new SKPaint { IsAntialias = stroke.Brush.AntiAlias, Color = StrokeColor(stroke) };
            canvas.DrawPath(path, paint);
        });
        ApplyClip(canvas, stroke, local, dev, outputScale);

        using var snapshot = scratch.Snapshot();
        using var composite = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(stroke.Brush.Opacity, 0, 1) * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };
        target.DrawImage(snapshot, 0, 0, composite);
    }

    /// <summary>
    /// A gradient ramp across the whole layer, bounded by whatever the stroke
    /// was drawn under — its selection, its alpha lock, its opacity.
    ///
    /// The axis is the two points the artist dragged, in document
    /// coordinates, and the ramp is a pure function of that axis and the
    /// stops. No dabs, no dynamics, nothing seeded from position: a gradient
    /// is the one mark in the app with no stochastic element at all, so
    /// invariant 2 costs nothing here.
    ///
    /// A gradient with no selection covers the layer, which is what every
    /// other tool does with a gradient too — the selection IS the shape.
    /// </summary>
    private static void StampGradient(
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels, double outputScale)
    {
        if (stroke.Points.Count < 2) return;
        if (stroke.GradientId is null) return;
        if (PaletteRegistry.ResolveGradient(stroke.GradientId) is not { } gradient) return;

        var dev = DeviceRect(new SKRectI(0, 0, info.Width, info.Height), outputScale);
        var local = new SKImageInfo(dev.Width, dev.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) return;
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Both tracks. Building the shader from the colour stops alone drops
        // every opacity stop that sits between two of them — which is most of
        // them, since the whole reason opacity has its own track is that it
        // changes in different places.
        var marks = GradientOps.Ordered(gradient).Select(s => s.Position);
        if (gradient.AlphaStops is { } alphas) marks = marks.Concat(alphas.Select(s => s.Position));
        var stops = marks
            .Select(p => Math.Clamp(p, 0, 1))
            .Distinct()
            .OrderBy(p => p)
            .ToList();
        if (stops.Count == 0) return;

        var colors = new SKColor[stops.Count];
        var positions = new float[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            var (r, g, b, a) = GradientOps.Sample(gradient, stops[i]);
            colors[i] = new SKColor(r, g, b, a);
            positions[i] = (float)stops[i];
        }

        var tile = gradient.Spread switch
        {
            GradientSpread.Repeat => SKShaderTileMode.Repeat,
            GradientSpread.Mirror => SKShaderTileMode.Mirror,
            _ => SKShaderTileMode.Clamp,
        };

        var from = new SKPoint((float)stroke.Points[0].X, (float)stroke.Points[0].Y);
        var to = new SKPoint((float)stroke.Points[^1].X, (float)stroke.Points[^1].Y);

        InDocumentSpace(canvas, dev, outputScale, () =>
        {
            using var shader = gradient.Kind == GradientKind.Radial
                // Radial: the drag is centre-to-edge, so its length is the
                // radius. A zero-length drag would make a degenerate shader.
                ? SKShader.CreateRadialGradient(
                    from, Math.Max(0.01f, Distance(from, to)), colors, positions, tile)
                : SKShader.CreateLinearGradient(from, to, colors, positions, tile);
            using var paint = new SKPaint { Shader = shader, IsAntialias = stroke.Brush.AntiAlias };
            canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
        });

        ApplyClip(canvas, stroke, local, dev, outputScale);
        ApplyAlphaLock(canvas, stroke, targetPixels, dev);

        using var snapshot = scratch.Snapshot();
        using var composite = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(stroke.Brush.Opacity, 0, 1) * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };
        target.DrawImage(snapshot, dev.Left, dev.Top, composite);
    }

    /// <summary>
    /// The colour a stroke actually paints: its palette swatch if it has one
    /// and the swatch still exists, otherwise the literal it recorded.
    ///
    /// The fallback is not a nicety. A document can arrive with its palette
    /// removed, or referencing a swatch someone deleted, and the honest
    /// answer then is the colour the artist last saw — not black, and not a
    /// refusal to render.
    /// </summary>
    public static SKColor StrokeColor(Stroke stroke) =>
        stroke.SwatchId is { Length: > 0 } id && PaletteRegistry.ResolveSwatch(id) is { } swatch
            ? ParseColor(swatch.Color)
            : ParseColor(stroke.Color);

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Apply the stroke's recorded selection (if any) to a scratch whose
    /// device origin is <paramref name="dev"/>. The selection is a path, so it
    /// rasterises sharp at whatever <paramref name="outputScale"/> asks for;
    /// its feather is a physical width and scales with it.
    /// </summary>
    private static void ApplyClip(
        SKCanvas scratchCanvas, Stroke stroke, SKImageInfo local, SKRectI dev, double outputScale)
    {
        if (stroke.ClipId is null || ClipRegionRegistry.Resolve(stroke.ClipId) is not { } region) return;
        using var mask = SKSurface.Create(local);
        if (mask is null) return;
        mask.Canvas.Clear(SKColors.Transparent);
        InDocumentSpace(mask.Canvas, dev, outputScale, () =>
        {
            using var path = PathFromContours(region.Contours);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            if (region.Feather > 0)
            {
                var sigma = (float)(region.Feather / 2);
                paint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);
            }
            mask.Canvas.DrawPath(path, paint);
        });
        using var maskImage = mask.Snapshot();
        using var clipPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        scratchCanvas.DrawImage(maskImage, 0, 0, clipPaint);
    }

    /// <summary>
    /// Smudge/blur mutate the target directly — clip them with a hard path
    /// clip. The contours are document coordinates and the target is at
    /// <paramref name="outputScale"/>, so the clip path is scaled while the
    /// stamps inside it position themselves.
    /// </summary>
    private static void WithHardClip(SKCanvas target, Stroke stroke, double outputScale, Action stamp)
    {
        if (stroke.ClipId is null || ClipRegionRegistry.Resolve(stroke.ClipId) is not { } region)
        {
            stamp();
            return;
        }
        target.Save();
        using (var path = PathFromContours(region.Contours))
        {
            if (outputScale != 1.0) path.Transform(SKMatrix.CreateScale((float)outputScale, (float)outputScale));
            target.ClipPath(path, antialias: true);
        }
        stamp();
        target.Restore();
    }

    // ---- paint (the default pipeline) ----------------------------------------

    private static void StampPaint(
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels, double outputScale)
    {
        // The scratch covers only what the stroke can reach — dabs, effects
        // and feathered clips all happen inside it. This is what keeps a
        // stroke commit independent of the canvas size (a full-canvas
        // granulation pass alone used to cost most of a second).
        var brush = stroke.Brush;
        var margin = DabReach(brush);
        var region = stroke.ClipId is null ? null : ClipRegionRegistry.Resolve(stroke.ClipId);
        if (region is { Feather: > 0 }) margin += (float)(region.Feather * 2);
        if (SegmentBounds(stroke, info, margin) is not { } rect) return;

        // Bigger surface, same geometry. The scratch is allocated at output
        // resolution but the dabs are stamped in unchanged document
        // coordinates under a canvas transform, so every Hash01 seeded from a
        // dab position receives the identical input at every scale. Scaling
        // the coordinates instead would re-roll all ten dynamics.
        var dev = DeviceRect(rect, outputScale);
        var local = new SKImageInfo(dev.Width, dev.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Everything below works in DEVICE pixels on a scratch whose origin is
        // dev.Left/dev.Top; the passes that need document coordinates say so.
        InDocumentSpace(canvas, dev, outputScale, () =>
        {
            StampDabs(canvas, stroke);

            // The medium replaces the flat texture effects: wet edge and
            // granulation are the cheap stand-ins for what the simulation
            // actually computes, so running both would double the rim and the
            // grain.
            if (brush.Medium.Kind == MediumKind.None && brush.TextureSurface is null && brush.Granulation > 0)
            {
                ApplyGranulation(canvas, brush, rect);
            }
        });

        if (brush.Medium.Kind != MediumKind.None)
        {
            Media.MediumSimulator.Apply(scratch, targetPixels, StrokeColor(stroke), brush.Medium, dev);
        }
        else
        {
            if (brush.WetEdge > 0) ApplyWetEdge(scratch, canvas, brush, local, outputScale);
            if (brush.TextureSurface is not null) ApplyTexture(canvas, brush, rect, local);
        }
        ApplyClip(canvas, stroke, local, dev, outputScale);
        ApplyAlphaLock(canvas, stroke, targetPixels, dev);

        using var snapshot = scratch.Snapshot();
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(brush.Opacity, 0, 1) * 255)),
            BlendMode = BlendModes.ForStroke(stroke),
        };
        target.DrawImage(snapshot, dev.Left, dev.Top, paint);
    }

    /// <summary>
    /// Run the post-dab pipeline over dabs that have <em>already been
    /// stamped</em>, and write the result into <paramref name="destination"/>.
    ///
    /// This exists for the live preview. Medium, wet edge, texture and
    /// granulation are stroke-global — the rim comes from the whole
    /// silhouette, the fluid flows across the whole wet area — so the preview
    /// has to recompute them over the entire stroke each time, not per
    /// segment. Doing that through <see cref="StampStroke"/> re-stamped every
    /// dab on every pass, which is O(stroke length) work to reproduce pixels
    /// the preview already had sitting in its scratch. Here the dabs are
    /// copied in and only the effects run, so a pass costs what the effects
    /// cost and stops growing with the number of dabs.
    ///
    /// Deliberately does NOT apply the clip or the alpha lock: the compositor
    /// masks the live overlay itself, and applying a feathered selection twice
    /// would double its softness.
    /// </summary>
    /// <param name="dabs">
    /// Document-resolution bitmap holding the stroke's dabs without stroke
    /// opacity — exactly what the live scratch accumulates.
    /// </param>
    /// <param name="destination">
    /// Document-resolution bitmap the processed stroke is written into,
    /// replacing whatever was in the affected region.
    /// </param>
    /// <returns>The region written, or null when the stroke reaches nothing.</returns>
    public static SKRectI? PostProcessDabs(
        SKBitmap dabs, SKBitmap destination, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels)
    {
        var brush = stroke.Brush;
        if (SegmentBounds(stroke, info, DabReach(brush)) is not { } rect) return null;

        var local = new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) return null;
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        // The dabs, moved rather than re-made. Only the stroke's own region is
        // handed over: wrapping the whole canvas-sized bitmap made Skia set up
        // a 33 MB image on every pass at 4K, which cost more than the dabs it
        // was saving on a short stroke.
        using (var subset = new SKBitmap())
        {
            if (!dabs.ExtractSubset(subset, rect)) return null;
            using var pixels = subset.PeekPixels();
            using var view = pixels is null ? null : SKImage.FromPixels(pixels);
            if (view is null) return null;
            canvas.DrawImage(view, 0, 0);
        }

        // Identical to StampPaint's post-dab half, at output scale 1 — the
        // live preview is display-only and never renders bigger.
        if (brush.Medium.Kind == MediumKind.None && brush.TextureSurface is null && brush.Granulation > 0)
        {
            InDocumentSpace(canvas, rect, 1.0, () => ApplyGranulation(canvas, brush, rect));
        }

        if (brush.Medium.Kind != MediumKind.None)
        {
            Media.MediumSimulator.Apply(scratch, targetPixels, StrokeColor(stroke), brush.Medium, rect);
        }
        else
        {
            if (brush.WetEdge > 0) ApplyWetEdge(scratch, canvas, brush, local, 1.0);
            if (brush.TextureSurface is not null) ApplyTexture(canvas, brush, rect, local);
        }

        using var snapshot = scratch.Snapshot();
        using var target = new SKCanvas(destination);
        using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
        target.DrawImage(snapshot, rect.Left, rect.Top, replace);
        target.Flush();
        return rect;
    }

    /// <summary>
    /// A document rect in the pixels a render at <paramref name="scale"/>
    /// actually has. Outward-rounded so the scratch never clips ink the
    /// document rect included, and integral so the scratch composites back
    /// one-for-one instead of being resampled.
    /// </summary>
    private static SKRectI DeviceRect(SKRectI rect, double scale)
    {
        if (scale == 1.0) return rect;
        return new SKRectI(
            (int)Math.Floor(rect.Left * scale),
            (int)Math.Floor(rect.Top * scale),
            (int)Math.Ceiling(rect.Right * scale),
            (int)Math.Ceiling(rect.Bottom * scale));
    }

    /// <summary>
    /// Run <paramref name="draw"/> with the scratch canvas in document
    /// coordinates: a document point lands at (doc × scale − device origin).
    /// </summary>
    private static void InDocumentSpace(SKCanvas canvas, SKRectI dev, double scale, Action draw)
    {
        canvas.Save();
        canvas.Translate(-dev.Left, -dev.Top);
        if (scale != 1.0) canvas.Scale((float)scale);
        draw();
        canvas.Restore();
    }

    private static void StampDabs(SKCanvas canvas, Stroke stroke)
    {
        var brush = stroke.Brush;
        var color = StrokeColor(stroke);
        var tip = brush.TipId is null ? null : BrushTipRegistry.Resolve(brush.TipId);
        var tipImage = brush.TipId is null ? null : BrushTipRegistry.ResolveImage(brush.TipId);

        // Direction comes from consecutive dab centres rather than from the
        // source points, so it follows the path the dabs actually took —
        // spacing and smoothing have already had their say by then.
        SKPoint? previous = null;
        var heading = double.NaN;
        // Distance along the stroke, for PaintLoad. Measured between dab
        // centres rather than between recorded points, so it is the path the
        // paint actually took after spacing and smoothing have had their say.
        double travelled = 0;
        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            if (previous is { } from)
            {
                float dx = pos.X - from.X, dy = pos.Y - from.Y;
                var step = Math.Sqrt(dx * dx + dy * dy);
                travelled += step;
                // A stationary dab keeps the last heading; recomputing from a
                // zero-length step would snap the tip to zero degrees.
                if (brush.AngleFollowsDirection && step > 0.01) heading = Math.Atan2(dy, dx) * 180 / Math.PI;
            }
            StampDab(canvas, pos, pressure, brush, color, tip, heading, tipImage, LoadAt(travelled, brush));
            previous = pos;
        }
    }

    /// <summary>Everything a dab can reach beyond its center: radius, scatter offset, soft edge.</summary>
    /// <summary>
    /// How far from the stroke's centreline a dab can put ink. This bounds
    /// every scratch surface and every repaint region, so it must never be
    /// short (clipped ink would change the render) and should not be
    /// generous either — the region's area drives the cost of painting on a
    /// large canvas.
    ///
    /// Worst case: the dab radius (half the size at full pressure), the
    /// scatter offset (up to Scatter × size), and — when a bitmap tip is
    /// rotated — its half-diagonal, which for a tip scaled to fit the dab is
    /// at most √2 × radius. A couple of pixels cover antialiasing.
    /// </summary>
    private static float DabReach(BrushSettings brush)
    {
        var radiusFactor = brush.TipId is null ? 0.5 : 0.75; // 0.5 × √2, rounded up
        return (float)(brush.Size * (radiusFactor + Math.Max(0, brush.Scatter)) + 4) + BleedReach(brush);
    }

    /// <summary>
    /// How far past the dabs a simulated medium can carry paint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This was B27, and the cause was not the fluid model.</b> The wash
    /// bled 20% however wet it was and however long it ran, and the suspicion
    /// was the lattice's capillary entry pressure pinning the front. Measured
    /// directly, the lattice spreads fine — a disc of radius 12 reaches 19 at
    /// water depth 1 and 26 at depth 4. What it could not do was spread past
    /// the region it was given, and that region came from
    /// <see cref="DabReach"/>: the geometry of the <em>stamps</em>, which knows
    /// nothing about fluid. The lattice edge was a wall two pixels outside the
    /// mark, and everything downstream faithfully simulated a wash in a box.
    /// </para>
    /// <para>
    /// So the region has to be told. Scaled by wetness and by how long the
    /// solver runs, because those are exactly what decide how far paint
    /// travels, and capped — a margin is area, area is the cost of painting,
    /// and invariant 6 says painting is bounded work. A generous wash on a big
    /// brush is allowed to be expensive; it is not allowed to be unbounded.
    /// </para>
    /// <para>
    /// Zero without a medium, so a plain brush's region is exactly what it
    /// always was and nothing about ordinary drawing pays for this.
    /// </para>
    /// </remarks>
    private static float BleedReach(BrushSettings brush)
    {
        var medium = brush.Medium;
        if (medium.Kind == MediumKind.None) return 0;

        var wetness = Math.Clamp(medium.Wetness, 0, 1);
        var steps = Math.Clamp(medium.FlowSteps, 0, 32);
        if (wetness <= 0 || steps <= 0) return 0;

        // Roughly a lattice cell a step at full wetness — the CFL bound the
        // transport step already obeys — and never more than the brush is wide.
        var reach = wetness * steps * 0.75;
        return (float)Math.Min(reach, brush.Size);
    }

    /// <summary>The stroke's points inflated by the dab reach, clamped to the canvas; null when off-canvas.</summary>
    private static SKRectI? SegmentBounds(Stroke stroke, SKImageInfo info, float margin)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in stroke.Points)
        {
            minX = Math.Min(minX, (float)p.X);
            maxX = Math.Max(maxX, (float)p.X);
            minY = Math.Min(minY, (float)p.Y);
            maxY = Math.Max(maxY, (float)p.Y);
        }
        var left = (int)Math.Floor(Math.Clamp(minX - margin, 0, info.Width));
        var top = (int)Math.Floor(Math.Clamp(minY - margin, 0, info.Height));
        var right = (int)Math.Ceiling(Math.Clamp(maxX + margin, 0, info.Width));
        var bottom = (int)Math.Ceiling(Math.Clamp(maxY + margin, 0, info.Height));
        if (right <= left || bottom <= top) return null;
        return new SKRectI(left, top, right, bottom);
    }

    /// <summary>
    /// Live-preview paint: the scratch surface covers only the new segment,
    /// wet edge and granulation wait for the commit, and a feathered
    /// selection clips hard. Cost tracks the segment, not the canvas.
    /// </summary>
    /// <summary>
    /// Live-preview building blocks for the whole-stroke scratch model: dabs
    /// accumulate in a stroke-local scratch WITHOUT stroke opacity, and the
    /// scratch is composed over the pristine layer once per event, bounded to
    /// the new segment — identical semantics to the exact render, so what the
    /// artist sees while drawing is what commits.
    /// </summary>
    public static void StampDraftDabs(SKCanvas scratchCanvas, Stroke tail) => StampDabs(scratchCanvas, tail);

    /// <summary>Pixels a live segment can reach (dab size + scatter margin); null when off-canvas.</summary>
    public static SKRectI? DraftSegmentBounds(Stroke tail, SKImageInfo info) =>
        SegmentBounds(tail, info, DabReach(tail.Brush));

    /// <summary>
    /// Every pixel the FINAL render of a stroke can touch — the same margin
    /// the exact stamping path uses, widened for blur and for the feather of
    /// its clip region. Callers use it to repaint only what changed.
    /// </summary>
    public static SKRectI? CommitBounds(Stroke stroke, SKImageInfo info)
    {
        var margin = DabReach(stroke.Brush);
        if (stroke.Brush.Kind == BrushKind.Blur)
        {
            margin += (float)(Math.Max(0.5, stroke.Brush.Size * 0.25) * 4);
        }
        if (stroke.ClipId is not null
            && ClipRegionRegistry.Resolve(stroke.ClipId) is { Feather: > 0 } region)
        {
            margin += (float)(region.Feather * 2);
        }
        return SegmentBounds(stroke, info, margin);
    }

    /// <summary>
    /// Rebuild one region of the live composite: reset it to the committed
    /// layer, then lay the whole-stroke scratch over it with the stroke's
    /// opacity (eraser = DstOut) and clip — opacity applied once, like the
    /// exact render, so self-crossings don't darken.
    /// </summary>
    public static void ComposeDraftRegion(SKCanvas composite, SKBitmap layerBase, SKBitmap scratch, SKRectI rect, Stroke stroke)
    {
        var region = SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height);
        composite.Save();
        composite.ClipRect(region);
        using (var reset = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            composite.DrawBitmap(layerBase, region, region, reset);
        }
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(stroke.Brush.Opacity, 0, 1) * 255)),
            BlendMode = BlendModes.ForStroke(stroke),
        };
        var clip = stroke.ClipId is null ? null : ClipRegionRegistry.Resolve(stroke.ClipId);
        if (clip is not null)
        {
            using var path = PathFromContours(clip.Contours);
            composite.ClipPath(path, antialias: true);
        }
        composite.DrawBitmap(scratch, region, region, paint);
        composite.Restore();
    }

    private static void StampPaintDraft(SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels = null)
    {
        var brush = stroke.Brush;
        if (SegmentBounds(stroke, info, DabReach(brush)) is not { } rect) return;

        var boundsInfo = new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(boundsInfo);
        if (scratch is null) return;
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-rect.Left, -rect.Top);
        StampDabs(canvas, stroke);
        ApplyAlphaLock(canvas, stroke, targetPixels, rect);

        using var snapshot = scratch.Snapshot();
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(brush.Opacity, 0, 1) * 255)),
            BlendMode = BlendModes.ForStroke(stroke),
        };
        var region = stroke.ClipId is null ? null : ClipRegionRegistry.Resolve(stroke.ClipId);
        if (region is not null)
        {
            target.Save();
            using var path = PathFromContours(region.Contours);
            target.ClipPath(path, antialias: true);
        }
        target.DrawImage(snapshot, rect.Left, rect.Top, paint);
        if (region is not null) target.Restore();
    }

    /// <summary>How a bitmap tip is resampled. See the note in <see cref="StampDab"/>.</summary>
    private static readonly SKSamplingOptions TipSampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static void StampDab(
        SKCanvas canvas, SKPoint pos, double pressure, BrushSettings brush, SKColor color,
        SKBitmap? tip, double directionDeg = double.NaN, SKImage? tipImage = null,
        double load = 1)
    {
        var radius = (float)(RadiusAt(brush, pressure));
        if (radius <= 0) return;

        var alpha = DabAlpha(brush, pressure) * load;
        if (alpha <= 0) return;

        // Every dynamic below is seeded from the dab's own position, never
        // from an index or a clock. Two consequences that matter here: a
        // re-render is identical, and — because neighbouring frames put dabs
        // in nearly the same places — a sequence does not boil at 12 fps.
        if (brush.SizeJitter > 0)
        {
            var floor = Math.Clamp(brush.MinimumDiameter, 0, 1);
            var scale = floor + (1 - floor) * (1 - Hash01(pos.X, pos.Y, 11) * brush.SizeJitter);
            radius = (float)(radius * scale);
            if (radius <= 0) return;
        }

        if (brush.FlowJitter > 0)
        {
            alpha *= 1 - Hash01(pos.X, pos.Y, 12) * Math.Clamp(brush.FlowJitter, 0, 1);
            if (alpha <= 0) return;
        }

        color = JitterColor(color, pos, brush);

        // Position-seeded scatter keeps re-renders identical. Pressure scales
        // how far it throws, not whether it throws — the direction stays the
        // hash's, so a harder press spreads the same pattern rather than
        // reshuffling it, and the mark does not boil between frames.
        var scatter = brush.Scatter * PressureResponse.Factor(brush, BrushDynamic.Scatter, pressure);
        if (scatter > 0)
        {
            var amount = Hash01(pos.X, pos.Y, 1) * scatter * brush.Size;
            var angle = Hash01(pos.X, pos.Y, 2) * Math.PI * 2;
            pos = new SKPoint(pos.X + (float)(Math.Cos(angle) * amount), pos.Y + (float)(Math.Sin(angle) * amount));
        }

        var dabColor = color.WithAlpha((byte)Math.Round(alpha * 255));

        // Roundness squashes the dab across its own axis. Combined with a
        // direction-following angle, that is what turns a round tip into a
        // chisel and a custom tip into bristles.
        // Pressure fattens the dab toward circular rather than thinning it:
        // a flat brush pressed down spreads, and a curve that squashed instead
        // would make a hard press narrower than a light one.
        var roundness = Math.Clamp(brush.Roundness, 0.05, 1);
        if (PressureResponse.IsDriven(brush, BrushDynamic.Roundness))
        {
            var open = PressureResponse.Factor(brush, BrushDynamic.Roundness, pressure);
            roundness = Math.Clamp(roundness + (1 - roundness) * open, 0.05, 1);
        }
        if (brush.RoundnessJitter > 0)
        {
            roundness *= 1 - Hash01(pos.X, pos.Y, 13) * Math.Clamp(brush.RoundnessJitter, 0, 1);
            roundness = Math.Clamp(roundness, 0.05, 1);
        }

        if (tip is not null)
        {
            var rotation = brush.TipRotationDeg;
            if (brush.AngleFollowsDirection && !double.IsNaN(directionDeg)) rotation += directionDeg;
            if (brush.RotationJitter > 0)
            {
                rotation += (Hash01(pos.X, pos.Y, 3) - 0.5) * 360 * brush.RotationJitter;
            }
            canvas.Save();
            canvas.Translate(pos.X, pos.Y);
            canvas.RotateDegrees((float)rotation);
            var scale = radius * 2 / Math.Max(tip.Width, tip.Height);
            canvas.Scale(scale, (float)(scale * roundness));
            using var paint = new SKPaint
            {
                IsAntialias = brush.AntiAlias,
                ColorFilter = SKColorFilter.CreateBlendMode(dabColor, SKBlendMode.SrcIn),
            };
            // DrawBitmap with no sampling options is nearest-neighbour, and
            // IsAntialias does not change that — it smooths the edge of the
            // drawn shape, not the resampling of what is inside it. So an
            // imported tip came out blocky enlarged and gritty reduced, which
            // is most of what a 512px .abr tip stamped at 20px ever is.
            //
            // Mipmaps are the half that matters at small brush sizes, and what
            // they fix is ink density rather than aliasing. Linear samples four
            // texels; on a sparse tip — spatter, stipple, a dry-brush scan —
            // minifying 256 to 12 means those four texels are nowhere near a
            // fair sample of the 21x21 region they stand for, and a window that
            // clips one dot reports a quarter of full alpha for it. Measured on
            // a tip with 1.6% coverage, that came out 2.3x too dark: the brush
            // was heavier at small sizes than at large ones, which is the
            // opposite of what the artist asked for. The chain is built once
            // because the image is held by the registry rather than wrapped per
            // dab.
            if (tipImage is not null)
            {
                canvas.DrawImage(tipImage, -tip.Width / 2f, -tip.Height / 2f, TipSampling, paint);
            }
            else
            {
                canvas.DrawBitmap(tip, -tip.Width / 2f, -tip.Height / 2f, paint);
            }
            canvas.Restore();
            return;
        }

        using var round = new SKPaint { IsAntialias = brush.AntiAlias };
        var hardness = HardnessAt(brush, pressure);

        // Only a squashed dab needs the transform. Rotating a circle is a
        // no-op mathematically but not in the rasteriser — pixels shift under
        // the matrix — so taking that path for a round dab would make
        // direction-following visibly change a brush it cannot affect.
        //
        // The transform is required rather than convenient: the gradient
        // shader is built in the space it is painted in, so squashing the
        // circle afterwards would squash its falloff too and the dab would
        // stop matching its own hardness.
        var oriented = roundness < 0.999;

        if (oriented)
        {
            var rotation = brush.TipRotationDeg;
            if (brush.AngleFollowsDirection && !double.IsNaN(directionDeg)) rotation += directionDeg;
            canvas.Save();
            canvas.Translate(pos.X, pos.Y);
            canvas.RotateDegrees((float)rotation);
            canvas.Scale(1f, (float)roundness);
            SetRoundPaint(round, SKPoint.Empty, radius, hardness, dabColor);
            canvas.DrawCircle(SKPoint.Empty, radius, round);
            canvas.Restore();
            return;
        }

        SetRoundPaint(round, pos, radius, hardness, dabColor);
        canvas.DrawCircle(pos, radius, round);
    }

    private static void SetRoundPaint(SKPaint paint, SKPoint centre, float radius, float hardness, SKColor color)
    {
        if (hardness >= 0.999f)
        {
            paint.Color = color;
            return;
        }
        paint.Shader = SKShader.CreateRadialGradient(
            centre, radius, [color, color.WithAlpha(0)], [hardness, 1f], SKShaderTileMode.Clamp);
    }

    /// <summary>
    /// Per-dab colour variation. Natural media drifts between two colours the
    /// artist chose rather than randomly around one, which is why the drift
    /// toward a secondary colour is separate from the hue/saturation/value
    /// wobble — the first reads as paint, the second alone reads as noise.
    /// </summary>
    private static SKColor JitterColor(SKColor color, SKPoint pos, BrushSettings brush)
    {
        if (brush.ColorJitter > 0 && brush.SecondaryColor is { } secondary)
        {
            var t = Hash01(pos.X, pos.Y, 21) * Math.Clamp(brush.ColorJitter, 0, 1);
            color = Mix(color, ParseColor(secondary), t);
        }

        var hueJitter = Math.Clamp(brush.HueJitter, 0, 1);
        var satJitter = Math.Clamp(brush.SaturationJitter, 0, 1);
        var valJitter = Math.Clamp(brush.BrightnessJitter, 0, 1);
        if (hueJitter <= 0 && satJitter <= 0 && valJitter <= 0) return color;

        color.ToHsv(out var h, out var sat, out var val);
        if (hueJitter > 0) h = (float)((h + (Hash01(pos.X, pos.Y, 22) - 0.5) * 60 * hueJitter + 360) % 360);
        if (satJitter > 0) sat = (float)Math.Clamp(sat * (1 + (Hash01(pos.X, pos.Y, 23) - 0.5) * 2 * satJitter), 0, 100);
        if (valJitter > 0) val = (float)Math.Clamp(val * (1 + (Hash01(pos.X, pos.Y, 24) - 0.5) * 2 * valJitter), 0, 100);
        return SKColor.FromHsv(h, sat, val, color.Alpha);
    }

    /// <summary>
    /// Darkened rim where paint pools at the stroke's edge (watercolor look).
    /// Operates on a stroke-bounded scratch: the rim surface matches the
    /// scratch's local size, and the result composites back at the scratch's
    /// document offset.
    /// </summary>
    private static void ApplyWetEdge(
        SKSurface scratch, SKCanvas canvas, BrushSettings brush, SKImageInfo local, double outputScale)
    {
        using var img = scratch.Snapshot();
        // Rim width, in pixels, capped so a huge brush does not imply a huge
        // band: past a few dozen pixels it stops reading as a wet edge anyway.
        // The cap is a document width, so the rim keeps the same physical size
        // when the render scale goes up.
        var width = (float)(Math.Clamp(brush.Size * 0.12, 1.0, 48.0) * outputScale);
        using var rim = SKSurface.Create(local);
        if (rim is null) return;
        rim.Canvas.DrawImage(img, 0, 0);

        // Shrink the stroke and subtract it, leaving the outline. Skia's erode
        // does this exactly but costs O(area x radius) — 10.6 s for a 500 px
        // brush on a 4K stroke, which is the whole pen-lift stall. Its blur is
        // a three-pass box filter whose cost barely moves with radius, so blur
        // and then push the alpha back toward solid: the soft edge contracts
        // instead of merely fading, which is the same rim ~50x cheaper.
        // Work from a saturated silhouette rather than the stroke's own alpha.
        // A stroke laid down at flow 0.7 is translucent everywhere, and any
        // test that treats "alpha below 1" as "near the edge" then darkens the
        // whole mark instead of its outline.
        using var mask = SKSurface.Create(local);
        if (mask is null) return;
        for (var i = 0; i < 4; i++) mask.Canvas.DrawImage(img, 0, 0);
        using var maskImg = mask.Snapshot();

        // Contract the silhouette: blur it, then raise it to the fourth power
        // so the solid core survives and the feathered skirt collapses. That
        // is an erode in all but name, and unlike Skia's — which is
        // O(area x radius) and cost 10.6 s on a 500 px brush at 4K — a blur is
        // a three-pass box filter whose cost barely moves with radius.
        using var shrunk = SKSurface.Create(local);
        if (shrunk is null) return;
        using (var blur = SKImageFilter.CreateBlur(width * 0.7f, width * 0.7f))
        using (var soften = new SKPaint { ImageFilter = blur })
        {
            shrunk.Canvas.DrawImage(maskImg, 0, 0, soften);
        }
        using (var softImg = shrunk.Snapshot())
        using (var multiply = new SKPaint { BlendMode = SKBlendMode.DstIn })
        {
            for (var i = 0; i < 3; i++) shrunk.Canvas.DrawImage(softImg, 0, 0, multiply);
        }

        // rim = silhouette minus its contracted self, kept inside the stroke.
        rim.Canvas.Clear(SKColors.Transparent);
        rim.Canvas.DrawImage(maskImg, 0, 0);
        using (var contracted = shrunk.Snapshot())
        using (var carve = new SKPaint { BlendMode = SKBlendMode.DstOut })
        {
            rim.Canvas.DrawImage(contracted, 0, 0, carve);
        }
        using var rimImg = rim.Snapshot();
        using var darken = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(
                SKColors.Black.WithAlpha((byte)Math.Round(Math.Clamp(brush.WetEdge, 0, 1) * 150)),
                SKBlendMode.SrcIn),
            BlendMode = SKBlendMode.SrcATop, // only darken where the stroke has paint
        };
        canvas.DrawImage(rimImg, 0, 0, darken);
    }

    /// <summary>
    /// Paper-grain noise multiplied into the stroke's alpha (fixed seed →
    /// deterministic). Drawn in document coordinates, so the grain field is
    /// anchored to the canvas regardless of the scratch's bounds.
    /// </summary>
    private const int GrainTileSize = 1024;

    private static SKBitmap? _grainTile;

    /// <summary>
    /// One deterministic tile of paper grain, generated once and repeated.
    /// Evaluating three octaves of Perlin per pixel over a whole stroke costs
    /// ~0.9 s at 4K; the tile costs that once per process and then nothing.
    /// Repetition is anchored to document coordinates, so a re-render lands
    /// the grain in exactly the same place.
    /// </summary>
    private static SKBitmap GrainTile()
    {
        if (_grainTile is not null) return _grainTile;
        var tile = new SKBitmap(new SKImageInfo(
            GrainTileSize, GrainTileSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var surface = SKSurface.Create(tile.Info))
        {
            if (surface is null) return tile;
            using var noise = SKShader.CreatePerlinNoiseFractalNoise(0.09f, 0.09f, 3, 7f);
            using var paint = new SKPaint { Shader = noise };
            surface.Canvas.DrawRect(new SKRect(0, 0, GrainTileSize, GrainTileSize), paint);
            surface.Canvas.Flush();
            using var image = surface.Snapshot();
            image.ReadPixels(tile.Info, tile.GetPixels(), tile.RowBytes, 0, 0);
        }
        return _grainTile = tile;
    }

    /// <summary>
    /// Photoshop's Texture, which is granulation with the surface, its scale
    /// and its depth made settable. Applied to the stroke rather than per dab:
    /// the grain belongs to the paper, so it must not move when a dab lands on
    /// it, and doing it once is far cheaper than doing it hundreds of times.
    /// </summary>
    /// <summary>
    /// Photoshop's Texture. The grain is generated at document resolution and
    /// magnified when the render scale goes up, rather than resampled finer:
    /// <see cref="Media.PaperField"/> hashes lattice coordinates, so asking it
    /// for more samples would produce a different pattern, not a sharper one —
    /// the same trap that makes the dabs stay in document coordinates. A
    /// bigger render therefore gets the same paper, softer. That is honest for
    /// a texture with a fixed physical scale, and it keeps a 1x and a 2x
    /// render of the same stroke the same image.
    /// </summary>
    private static void ApplyTexture(SKCanvas canvas, BrushSettings brush, SKRectI rect, SKImageInfo local)
    {
        if (brush.TextureSurface is not { } surface) return;
        var depth = (float)Math.Clamp(brush.TextureDepth, 0, 1);
        if (depth <= 0) return;

        var w = Math.Max(1, rect.Width);
        var h = Math.Max(1, rect.Height);
        var height = new float[w * h];
        Media.PaperField.Fill(height, w, h, rect.Left, rect.Top, surface, brush.TextureScale);

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var mask = new SKBitmap(info);

        // Filled as bytes and installed once. SetPixel per pixel bounds-checks
        // and repacks an SKColor each time, which over a stroke-sized region
        // is millions of calls for what is a two-instruction write.
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < height.Length; i++)
        {
            // A' = 1 - depth * (1 - height): the tooth keeps paint, the
            // valleys let it through, and depth decides how deep the bite.
            var keep = 1f - depth * (1f - height[i]);
            var o = i * 4;
            rgba[o] = 255;
            rgba[o + 1] = 255;
            rgba[o + 2] = 255;
            rgba[o + 3] = (byte)Math.Round(Math.Clamp(keep, 0, 1) * 255);
        }
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, mask.GetPixels(), rgba.Length);

        using var image = SKImage.FromBitmap(mask);
        using var paint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        canvas.DrawImage(
            image,
            new SKRect(0, 0, local.Width, local.Height),
            new SKSamplingOptions(SKFilterMode.Linear),
            paint);
    }

    private static void ApplyGranulation(SKCanvas canvas, BrushSettings brush, SKRectI rect)
    {
        var g = (float)Math.Clamp(brush.Granulation, 0, 1);
        // Aligned to the document origin, not the scratch, so the grain field
        // stays anchored to the canvas wherever the stroke happens to be.
        using var noise = SKShader.CreateBitmap(
            GrainTile(), SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        // A' = g·R + (1−g): noise carves alpha away by up to its full depth at
        // g=1. (SkiaSharp color-matrix offsets are in 0..1 scale.)
        var matrix = new float[]
        {
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            g, 0, 0, 0, 1 - g,
        };
        using var paint = new SKPaint
        {
            Shader = noise,
            ColorFilter = SKColorFilter.CreateColorMatrix(matrix),
            BlendMode = SKBlendMode.DstIn,
        };
        canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), paint);
    }

    // ---- smudge ---------------------------------------------------------------

    /// <summary>
    /// Drag canvas color along the stroke: each dab samples under the previous
    /// position and deposits the carried color ahead. Flow is the smudge
    /// strength. Replayed in stroke order this is fully deterministic.
    /// </summary>
    /// <param name="outputScale">
    /// Dab geometry stays in document coordinates under a canvas transform, as
    /// everywhere else; only the reads from <paramref name="pixels"/> — which
    /// is at output resolution — are mapped into it.
    /// </param>
    private static void StampSmudge(SKCanvas target, SKBitmap pixels, Stroke stroke, double outputScale)
    {
        var brush = stroke.Brush;
        var strength = Math.Clamp(brush.Flow, 0, 1);
        if (strength <= 0) return;

        target.Save();
        if (outputScale != 1.0) target.Scale((float)outputScale);
        try { StampSmudgeDabs(target, pixels, stroke, outputScale, strength); }
        finally { target.Restore(); }
    }

    private static void StampSmudgeDabs(
        SKCanvas target, SKBitmap pixels, Stroke stroke, double outputScale, double strength)
    {
        var brush = stroke.Brush;

        // Krita's two Color Smudge algorithms. Smearing drags a sample along
        // the stroke and refreshes it as it goes, so detail smears into
        // streaks. Dulling takes one colour from under the dab and lays it
        // down flat, so detail dissolves instead — that is what a blender
        // brush wants, and it is why a blender built out of Smearing never
        // quite behaves.
        var dulling = brush.SmudgeMode == SmudgeMode.Dulling;
        var baseCarry = Math.Clamp(brush.SmudgeLength, 0, 1);
        var spread = (float)Math.Clamp(brush.SmudgeRadius, 0.05, 1);
        var baseRate = Math.Clamp(brush.ColorRate, 0, 1);
        var ownColor = StrokeColor(stroke);

        SKColor carried = default;
        var hasColor = false;
        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            var radius = (float)RadiusAt(brush, pressure);
            if (radius <= 0) continue;

            var s = (float)outputScale;
            var sample = SampleAverage(
                pixels, new SKPoint(pos.X * s, pos.Y * s), Math.Max(1f, radius * spread * s));
            if (!hasColor)
            {
                // The first dab picks up what is under it and lays it straight
                // back down, so a tap softens a boundary and does nothing at
                // all on flat colour. Returning early here instead would make
                // the brush appear dead until the pointer moved.
                carried = sample;
                hasColor = true;
            }

            // Both of these are per dab, because pressure is: pressing harder
            // mid-stroke has to drag further and add more of the brush's own
            // colour from that point on, not from the beginning.
            var carryOver =
                (float)(baseCarry * PressureResponse.Factor(brush, BrushDynamic.SmudgeLength, pressure));
            var colorRate = baseRate * PressureResponse.Factor(brush, BrushDynamic.ColorRate, pressure);

            // Dulling deposits the colour under the dab rather than the
            // colour it has been carrying, which is the whole difference.
            var deposit = dulling ? Mix(sample, carried, carryOver * 0.5) : carried;
            if (colorRate > 0) deposit = Mix(deposit, ownColor, colorRate);

            if (deposit.Alpha > 0)
            {
                using var paint = new SKPaint { IsAntialias = brush.AntiAlias };
                var dabColor = deposit.WithAlpha((byte)Math.Round(deposit.Alpha * strength));
                var hardness = (float)Math.Clamp(brush.Hardness, 0, 1);
                if (hardness >= 0.999f)
                {
                    paint.Color = dabColor;
                }
                else
                {
                    paint.Shader = SKShader.CreateRadialGradient(
                        pos, radius, [dabColor, dabColor.WithAlpha(0)], [hardness, 1f], SKShaderTileMode.Clamp);
                }
                target.DrawCircle(pos, radius, paint);
                target.Flush(); // the next sample must see this deposit
            }

            // How much of the carried colour survives into the next dab. At 0
            // the sample is replaced every dab and colour barely travels; at 1
            // it is dragged the length of the stroke.
            carried = Mix(sample, carried, carryOver);
        }
    }

    private static SKColor SampleAverage(SKBitmap pixels, SKPoint pos, float spread)
    {
        Span<(int dx, int dy)> offsets = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)];
        int a = 0, r = 0, g = 0, b = 0, n = 0;
        foreach (var (dx, dy) in offsets)
        {
            var x = Math.Clamp((int)(pos.X + dx * spread), 0, pixels.Width - 1);
            var y = Math.Clamp((int)(pos.Y + dy * spread), 0, pixels.Height - 1);
            var c = pixels.GetPixel(x, y);
            a += c.Alpha;
            r += c.Red;
            g += c.Green;
            b += c.Blue;
            n++;
        }
        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
    }

    private static SKColor Mix(SKColor x, SKColor y, double t) => new(
        (byte)(x.Red + (y.Red - x.Red) * t),
        (byte)(x.Green + (y.Green - x.Green) * t),
        (byte)(x.Blue + (y.Blue - x.Blue) * t),
        (byte)(x.Alpha + (y.Alpha - x.Alpha) * t));

    // ---- blur -----------------------------------------------------------------

    /// <summary>
    /// Soften the canvas along the stroke: dabs re-draw the PRE-STROKE content
    /// through a gaussian blur, clipped to the dab. Flow is the blur strength.
    /// </summary>
    private static void StampBlur(
        SKCanvas target, SKBitmap pixels, Stroke stroke, SKImageInfo info, double outputScale)
    {
        var brush = stroke.Brush;
        var sigma = (float)(Math.Clamp(brush.Flow, 0, 1) * Math.Max(1, brush.Size) / 4);
        if (sigma <= 0) return;

        using var snapshot = SKImage.FromBitmap(pixels); // immutable copy of pre-stroke pixels
        if (snapshot is null) return;
        // Sigma is a document width; the canvas transform carries it into
        // device pixels, so the blur stays the same physical softness.
        using var blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };

        // The snapshot is already at output resolution, so its destination is
        // the whole document — one for one in device pixels, not a resample.
        var whole = new SKRect(0, 0, info.Width, info.Height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);

        target.Save();
        if (outputScale != 1.0) target.Scale((float)outputScale);
        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            var radius = (float)RadiusAt(brush, pressure);
            if (radius <= 0) continue;
            target.Save();
            using (var clip = new SKPath())
            {
                clip.AddCircle(pos.X, pos.Y, radius);
                target.ClipPath(clip, antialias: true);
            }
            target.DrawImage(snapshot, whole, sampling, blurPaint);
            target.Restore();
        }
        target.Restore();
    }

    /// <summary>
    /// Live-preview blur: identical per-dab work, but the pre-stroke snapshot
    /// copies only the segment's reachable region instead of the full canvas.
    /// </summary>
    private static void StampBlurDraft(SKCanvas target, SKBitmap pixels, Stroke stroke, SKImageInfo info)
    {
        var brush = stroke.Brush;
        var sigma = (float)(Math.Clamp(brush.Flow, 0, 1) * Math.Max(1, brush.Size) / 4);
        if (sigma <= 0) return;
        if (SegmentBounds(stroke, info, DabReach(brush) + sigma * 4) is not { } rect) return;

        using var subset = new SKBitmap();
        if (!pixels.ExtractSubset(subset, rect)) return;
        using var snapshot = SKImage.FromBitmap(subset); // copies only the subset
        if (snapshot is null) return;
        using var blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };

        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            var radius = (float)RadiusAt(brush, pressure);
            if (radius <= 0) continue;
            target.Save();
            using (var clip = new SKPath())
            {
                clip.AddCircle(pos.X, pos.Y, radius);
                target.ClipPath(clip, antialias: true);
            }
            target.DrawImage(snapshot, rect.Left, rect.Top, blurPaint);
            target.Restore();
        }
    }

    // ---- shared ---------------------------------------------------------------

    /// <summary>
    /// Dab radius the engine will actually use. Public because the brush
    /// cursor has to draw the same number — a ring that disagrees with the
    /// stroke is worse than no ring, and duplicating the curve here is how
    /// that disagreement starts.
    /// </summary>
    public static double RadiusAt(BrushSettings brush, double pressure) =>
        brush.Size * PressureResponse.Factor(brush, BrushDynamic.Size, pressure) / 2;

    // The base is clamped before the response, not after. Clamping the product
    // instead would let an out-of-range Flow of 2 come back as 1 at half
    // pressure, which reads as pressure doing nothing on exactly the brushes
    // where somebody has already got a value wrong.
    private static double DabAlpha(BrushSettings brush, double pressure) =>
        Math.Clamp(
            Math.Clamp(brush.Flow, 0, 1) * PressureResponse.Factor(brush, BrushDynamic.Flow, pressure), 0, 1);

    /// <summary>Dab-edge hardness after the pressure response (light pressure = softer edge).</summary>
    private static float HardnessAt(BrushSettings brush, double pressure) =>
        (float)Math.Clamp(
            Math.Clamp(brush.Hardness, 0, 1) * PressureResponse.Factor(brush, BrushDynamic.Hardness, pressure), 0, 1);

    /// <summary>Deterministic 0..1 hash of a dab position (never an RNG).</summary>
    private static double Hash01(float x, float y, uint salt)
    {
        var h = 2166136261u ^ (salt * 0x9E3779B9u);
        h = (h ^ (uint)BitConverter.SingleToInt32Bits(x)) * 16777619u;
        h = (h ^ (uint)BitConverter.SingleToInt32Bits(y)) * 16777619u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }

    /// <summary>
    /// Dab centers and pressures along the stroke, spaced by
    /// <c>Brush.Spacing × the CURRENT dab diameter</c> of arc length
    /// (floor 0.5 px). Spacing must follow the pressure-scaled size:
    /// light pen pressure shrinks dabs, so the step shrinks with them —
    /// otherwise a light stroke degenerates into a dotted trail of stamps.
    /// Positions and pressures are interpolated between input points, so
    /// fast strokes with sparse events still produce continuous lines.
    /// </summary>
    /// <remarks>
    /// The walk runs over <see cref="GeometryOps.Densify"/>d points rather than
    /// the raw record. A pen samples at a fixed rate, so a fast stroke arrives
    /// as widely spaced points, and walking the chords between them puts the
    /// dabs inside the curve the artist drew — the flat facet that results is
    /// wider than a thick brush and reads as the tops of individual stamps
    /// showing through the outside of a bend. The curve passes through every
    /// recorded point and breaks at deliberate corners, so a drawn rectangle
    /// keeps its corners and the record still says where the pen was.
    /// </remarks>
    public static IEnumerable<(SKPoint Pos, double Pressure)> DabPositions(Stroke stroke)
    {
        var pts = GeometryOps.Densify(stroke.Points);
        var brush = stroke.Brush;
        var first = pts[0];
        var firstPressure = Math.Max(first.Pressure, MinPressure);
        yield return (new SKPoint((float)first.X, (float)first.Y), firstPressure);
        if (pts.Count == 1) yield break;

        double StepAt(double pressure) =>
            Math.Max(RadiusAt(brush, pressure) * 2 * brush.Spacing, MinStepPx);

        var step = StepAt(firstPressure);
        double acc = 0;
        var prev = first;
        for (var i = 1; i < pts.Count; i++)
        {
            var cur = pts[i];
            var d = GeometryOps.Dist(prev, cur);
            while (d > 0 && acc + d >= step)
            {
                var t = (step - acc) / d;
                var np = GeometryOps.LerpPoint(prev, cur, t);
                var pressure = Math.Max(np.Pressure, MinPressure);
                yield return (new SKPoint((float)np.X, (float)np.Y), pressure);
                d -= step - acc;
                acc = 0;
                prev = np;
                step = StepAt(pressure);
            }
            acc += d;
            prev = cur;
        }
    }

    /// <summary>
    /// How much paint the brush still has, this far along the stroke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PaintLoad said one thing and did another.</b> It is documented as
    /// "paint on the brush at the start of a stroke — below 1 the brush runs
    /// out as the stroke lengthens, which is what dry-brush is", and it was
    /// implemented as a flat multiplier on pigment, which made an oil stroke
    /// evenly forty per cent transparent from the first dab to the last. That
    /// is a transparency slider wearing a dry-brush label (B26).
    /// </para>
    /// <para>
    /// Depletion needs distance, and distance lives here rather than in
    /// <c>MediumSimulator</c> — which works off the scratch surface's coverage
    /// on purpose and never learns how the stroke was drawn. Putting it in the
    /// dab walk also means it works for a brush with no medium at all, which
    /// is what a real dry-brush is.
    /// </para>
    /// <para>
    /// Exponential, not linear: a brush does not run out at a steady rate and
    /// then stop, it gives up most of its paint early and trails off. The
    /// length scale is in brush diameters, so a big brush carries further —
    /// which is also true, and it means an artist who resizes a brush does not
    /// have to re-tune the load.
    /// </para>
    /// <para>
    /// A load of 1 returns exactly 1 at every distance, so the default is a
    /// byte-for-byte no-op and no existing drawing moves.
    /// </para>
    /// </remarks>
    internal static double LoadAt(double travelled, BrushSettings brush)
    {
        var load = Math.Clamp(brush.Medium.PaintLoad, 0, 1);
        if (load >= 1) return 1;

        // Diameters of stroke the paint lasts, per unit of load.
        //
        // Small, and the reason is dab overlap. At the usual spacing a dozen
        // dabs land on any given pixel, so the mark's alpha is
        // 1-(1-a)^12 and it saturates: cutting per-dab alpha by 15% moves the
        // visible mark by under 1%. Measured — at Reach 50 a load of 0.35
        // faded from 1.000 to 0.858 over seven hundred pixels, which is not a
        // brush running out, it is a brush thinking about it. The per-dab
        // curve has to fall much further than the mark does.
        const double Reach = 12;
        var scale = Math.Max(brush.Size, 1) * Reach * load;
        return Math.Exp(-travelled / scale);
    }

    public static SKColor ParseColor(string hex)
    {
        var (r, g, b) = ColorOps.HexToRgb(hex);
        return new SKColor(r, g, b);
    }
}
