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
    /// <param name="draftDabs">
    /// The whole stroke already walked, for a live-preview effect pass. Only the effect brushes read
    /// it, and only when <paramref name="draft"/> is set: they need the <em>commit's</em> dab
    /// positions, which a per-segment walk cannot produce (B54). Null walks the stroke here.
    /// </param>
    /// <param name="draftFromDab">
    /// The first dab in <paramref name="draftDabs"/> that is not settled yet. Dabs before it were
    /// stamped by an earlier pointer event and must not be drawn again — an antialiased rim redrawn
    /// with <c>Src</c> does not land where it did the first time.
    /// </param>
    /// <param name="origin">
    /// Where the paper's top-left corner sits in stroke coordinates —
    /// <see cref="Scene.Left"/> and <see cref="Scene.Top"/>. Zero for a document
    /// that has never been resized, which is why every existing caller can leave
    /// it alone. See the coordinate-space note on <see cref="ToSurface"/>: this
    /// is the only quantity that separates the two spaces, and getting it wrong
    /// moves the paper under the ink rather than failing loudly.
    /// </param>
    public static void StampStroke(
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels = null,
        bool draft = false, double outputScale = 1.0, SKBitmap? backdrop = null,
        IReadOnlyList<Dab>? draftDabs = null, int draftFromDab = 0, SKPointI origin = default)
    {
        if (stroke.Points.Count == 0) return;

        // Both are contours rather than paths, and differ only in whether the
        // region lands over what is there or takes it away (B173).
        if (stroke.Tool is ToolKind.Fill or ToolKind.ClearRegion)
        {
            StampFill(target, stroke, info, outputScale, origin);
            return;
        }

        if (stroke.Tool == ToolKind.Gradient)
        {
            StampGradient(target, stroke, info, targetPixels, outputScale, origin);
            return;
        }

        switch (stroke.Brush.Kind)
        {
            case BrushKind.Smudge when targetPixels is not null && stroke.Tool != ToolKind.Eraser:
            {
                using var owned = SampledSource(stroke, targetPixels, backdrop, info);
                var read = owned ?? targetPixels;
                WithHardClip(
                    target, stroke, outputScale, origin,
                    () => StampSmudge(target, read, stroke, outputScale, origin));
                return;
            }

            case BrushKind.Blur when targetPixels is not null && stroke.Tool != ToolKind.Eraser:
            {
                using var owned = SampledSource(stroke, targetPixels, backdrop, info);
                var read = owned ?? targetPixels;
                if (draft)
                {
                    // Whole-stroke dabs, so densification and spacing are the commit's (B54). A
                    // caller with no range of its own stamps all of them, which is what a
                    // single-shot draft render means.
                    var walk = draftDabs ?? WalkDabs(stroke);
                    WithHardClip(
                        target, stroke, 1.0, origin,
                        () => StampBlurDraft(target, read, stroke, info, walk, draftFromDab, origin));
                }
                else
                {
                    WithHardClip(
                        target, stroke, outputScale, origin,
                        () => StampBlur(target, read, stroke, info, outputScale, origin));
                }
                return;
            }
        }

        if (draft) StampPaintDraft(target, stroke, info, targetPixels, origin);
        else StampPaint(target, stroke, info, targetPixels, outputScale, origin);
    }

    /// <summary>
    /// A document point in the pixels of a surface whose top-left is
    /// <paramref name="origin"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two coordinate spaces run through this file, and the whole of the
    /// origin's cost is keeping them apart.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b>Document</b> — what a <c>Stroke</c> records and what every
    /// <c>Hash01</c> seed must receive. It does not change when the paper is
    /// resized, which is the entire point of <see cref="Scene.OriginX"/>: a dab
    /// keeps its scatter, size, flow, roundness, rotation and colour jitters
    /// because it keeps its coordinate.</item>
    /// <item><b>Surface</b> — a pixel in a layer bitmap or a scratch, where
    /// (0,0) is the document's top-left corner. Anything indexing pixels
    /// directly, extracting a subset, or reporting a region to repaint speaks
    /// this.</item>
    /// </list>
    /// <para>
    /// The canvas transform converts between them for everything that draws
    /// (<see cref="InDocumentSpace"/>). Raw pixel access has no transform to
    /// carry it, so it converts here — and the failure when it does not is
    /// silent: the mark keeps its shape and lands in the wrong place, or keeps
    /// its place and re-rolls its grain. <c>DocumentOriginTests</c> is the guard,
    /// and it is a byte comparison for exactly that reason.
    /// </para>
    /// </remarks>
    private static SKPoint ToSurface(SKPoint document, SKPointI origin) =>
        origin is { X: 0, Y: 0 }
            ? document
            : new SKPoint(document.X - origin.X, document.Y - origin.Y);

    /// <inheritdoc cref="ToSurface(SKPoint, SKPointI)"/>
    private static SKRectI ToSurface(SKRectI document, SKPointI origin) =>
        origin is { X: 0, Y: 0 }
            ? document
            : new SKRectI(
                document.Left - origin.X, document.Top - origin.Y,
                document.Right - origin.X, document.Bottom - origin.Y);

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
    private static void StampFill(
        SKCanvas target, Stroke stroke, SKImageInfo info, double outputScale, SKPointI origin)
    {
        if (stroke.Points.Count < 3) return;
        // A fill is contours, so it rasterises sharp at any output scale.
        var dev = DeviceRect(new SKRectI(0, 0, info.Width, info.Height), outputScale);
        var local = new SKImageInfo(dev.Width, dev.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        InDocumentSpace(canvas, dev, outputScale, origin, () =>
        {
            var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
            if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
            using var path = PathFromContours(contours);
            using var paint = new SKPaint { IsAntialias = stroke.Brush.AntiAlias, Color = StrokeColor(stroke) };
            canvas.DrawPath(path, paint);
        });
        ApplyClip(canvas, stroke, local, dev, outputScale, origin);

        using var snapshot = scratch.Snapshot();
        using var composite = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(stroke.Brush.Opacity, 0, 1) * 255)),
            // B173. The one line that separates a region laid down from a
            // region taken away — everything above it, contour to clip to
            // anti-aliasing, is identical for both.
            BlendMode = stroke.Tool == ToolKind.ClearRegion
                ? SKBlendMode.DstOut
                : SKBlendMode.SrcOver,
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
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels, double outputScale,
        SKPointI origin)
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

        InDocumentSpace(canvas, dev, outputScale, origin, () =>
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

        ApplyClip(canvas, stroke, local, dev, outputScale, origin);
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
        stroke.SwatchId is { Length: > 0 } id
        && PaletteRegistry.ResolveSwatch(stroke.PaletteId, id) is { } swatch
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
        SKCanvas scratchCanvas, Stroke stroke, SKImageInfo local, SKRectI dev, double outputScale,
        SKPointI origin)
    {
        if (stroke.ClipId is null || ClipRegionRegistry.Resolve(stroke.ClipId) is not { } region) return;
        using var mask = SKSurface.Create(local);
        if (mask is null) return;
        mask.Canvas.Clear(SKColors.Transparent);
        InDocumentSpace(mask.Canvas, dev, outputScale, origin, () =>
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
    private static void WithHardClip(
        SKCanvas target, Stroke stroke, double outputScale, SKPointI origin, Action stamp)
    {
        if (stroke.ClipId is null || ClipRegionRegistry.Resolve(stroke.ClipId) is not { } region)
        {
            stamp();
            return;
        }
        target.Save();
        using (var path = PathFromContours(region.Contours))
        {
            // A clip region's contours are document coordinates and the canvas
            // being clipped is a surface, so the origin comes off before the
            // scale — the same document-then-surface order every conversion in
            // this file uses.
            if (origin.X != 0 || origin.Y != 0)
            {
                path.Transform(SKMatrix.CreateTranslation(-origin.X, -origin.Y));
            }
            if (outputScale != 1.0) path.Transform(SKMatrix.CreateScale((float)outputScale, (float)outputScale));
            target.ClipPath(path, antialias: true);
        }
        stamp();
        target.Restore();
    }

    // ---- paint (the default pipeline) ----------------------------------------

    private static void StampPaint(
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels, double outputScale,
        SKPointI origin)
    {
        // The scratch covers only what the stroke can reach — dabs, effects
        // and feathered clips all happen inside it. This is what keeps a
        // stroke commit independent of the canvas size (a full-canvas
        // granulation pass alone used to cost most of a second).
        var brush = stroke.Brush;
        var margin = DabReach(brush);
        var region = stroke.ClipId is null ? null : ClipRegionRegistry.Resolve(stroke.ClipId);
        if (region is { Feather: > 0 }) margin += (float)(region.Feather * 2);
        if (SegmentBounds(stroke, info, margin, origin) is not { } rect) return;

        // Bigger surface, same geometry. The scratch is allocated at output
        // resolution but the dabs are stamped in unchanged document
        // coordinates under a canvas transform, so every Hash01 seeded from a
        // dab position receives the identical input at every scale. Scaling
        // the coordinates instead would re-roll all ten dynamics.
        //
        // `rect` is a DOCUMENT rect, because the granulation and texture passes
        // below seed a repeating field from its corner. The scratch it sizes is
        // a surface, so the origin comes off here and nowhere else — which is
        // what keeps the composite at the bottom unchanged.
        var dev = DeviceRect(ToSurface(rect, origin), outputScale);
        var local = new SKImageInfo(dev.Width, dev.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Everything below works in DEVICE pixels on a scratch whose origin is
        // dev.Left/dev.Top; the passes that need document coordinates say so.
        InDocumentSpace(canvas, dev, outputScale, origin, () =>
        {
            StampDabs(canvas, stroke);

            // The medium replaces the flat texture effects: wet edge and
            // granulation are the cheap stand-ins for what the simulation
            // actually computes, so running both would double the rim and the
            // grain.
            if (brush.Medium.Kind == MediumKind.None && !HasTexture(brush) && brush.Granulation > 0)
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
            if (HasTexture(brush)) ApplyTexture(canvas, brush, rect, local);
        }
        ApplyClip(canvas, stroke, local, dev, outputScale, origin);
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
        SKBitmap dabs, SKBitmap destination, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels,
        SKPointI origin = default)
    {
        if (PostProcessBounds(stroke, info, origin) is not { } rect) return null;

        using var snapshot = PostProcessRegion(dabs, stroke, rect, targetPixels, origin);
        if (snapshot is null) return null;

        using var target = new SKCanvas(destination);
        using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
        target.DrawImage(snapshot, rect.Left, rect.Top, replace);
        target.Flush();
        return rect;
    }

    /// <summary>
    /// The document region <see cref="PostProcessRegion"/> will write for this
    /// stroke — computed separately so a caller running the pass on another
    /// thread can crop its inputs on the thread that owns them first.
    /// </summary>
    public static SKRectI? PostProcessBounds(Stroke stroke, SKImageInfo info, SKPointI origin = default) =>
        SegmentBounds(stroke, info, DabReach(stroke.Brush), origin);

    /// <summary>
    /// The effects half of <see cref="PostProcessDabs"/>, returning the
    /// processed <paramref name="rect"/> as its own image instead of pasting
    /// it — so the expensive part can run on a worker over copies while the
    /// UI thread keeps the paste, which is the cheap part (B189).
    /// </summary>
    /// <param name="dabs">
    /// The stamped dabs. Indexed via <paramref name="origin"/>: where this
    /// bitmap's (0,0) sits in document space — the full live scratch at
    /// <c>default</c>, or a crop of exactly <paramref name="rect"/> with
    /// <c>origin = rect.Location</c>.
    /// </param>
    /// <param name="targetPixels">
    /// The layer beneath, for re-wetting and mixing. Indexed via
    /// <paramref name="beneathOrigin"/> the same way; a crop must cover
    /// <see cref="Media.MediumSimulator.ExistingRegionNeeded"/> or the rim
    /// samples stop matching the commit's.
    /// </param>
    public static SKImage? PostProcessRegion(
        SKBitmap dabs, Stroke stroke, SKRectI rect, SKBitmap? targetPixels,
        SKPointI origin = default, SKPointI beneathOrigin = default)
    {
        var brush = stroke.Brush;

        // `rect` is a document rect, because the granulation pass below seeds
        // its field from the corner; `surface` is the same region in the dab
        // bitmap's own indexing.
        var surface = ToSurface(rect, origin);
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
            if (!dabs.ExtractSubset(subset, surface)) return null;
            using var pixels = subset.PeekPixels();
            using var view = pixels is null ? null : SKImage.FromPixels(pixels);
            if (view is null) return null;
            canvas.DrawImage(view, 0, 0);
        }

        // Identical to StampPaint's post-dab half, at output scale 1 — the
        // live preview is display-only and never renders bigger.
        if (brush.Medium.Kind == MediumKind.None && !HasTexture(brush) && brush.Granulation > 0)
        {
            InDocumentSpace(canvas, surface, 1.0, origin, () => ApplyGranulation(canvas, brush, rect));
        }

        if (brush.Medium.Kind != MediumKind.None)
        {
            Media.MediumSimulator.Apply(
                scratch, targetPixels, StrokeColor(stroke), brush.Medium, rect, beneathOrigin);
        }
        else
        {
            if (brush.WetEdge > 0) ApplyWetEdge(scratch, canvas, brush, local, 1.0);
            if (HasTexture(brush)) ApplyTexture(canvas, brush, rect, local);
        }

        return scratch.Snapshot();
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
    /// <param name="dev">
    /// The scratch's rectangle in <b>surface</b> device pixels — already shifted
    /// by the origin, which is why the translate below does not mention it.
    /// </param>
    /// <param name="origin">
    /// The document's top-left in stroke coordinates. The extra translate is
    /// what lets everything inside <paramref name="draw"/> keep speaking
    /// document coordinates, which is what every <c>Hash01</c> seed needs.
    /// </param>
    private static void InDocumentSpace(
        SKCanvas canvas, SKRectI dev, double scale, SKPointI origin, Action draw)
    {
        canvas.Save();
        canvas.Translate(-dev.Left, -dev.Top);
        if (scale != 1.0) canvas.Scale((float)scale);
        if (origin.X != 0 || origin.Y != 0) canvas.Translate(-origin.X, -origin.Y);
        draw();
        canvas.Restore();
    }

    /// <summary>
    /// One dab, with every stroke-global quantity already resolved.
    /// </summary>
    /// <remarks>
    /// The walk carries a spacing phase, a travelled distance and a heading, so working
    /// out where dab <em>n</em> goes means walking dabs 0..n. Recording the result lets the
    /// live preview walk once per pointer event instead of once per question it wants to
    /// ask — which was measured: four walks an event made a 600-event stroke cost 3.2×
    /// more per event at the end than at the start, and invariant 6 does not allow that.
    /// </remarks>
    public readonly record struct Dab(SKPoint Pos, double Pressure, double Heading, double Load)
    {
        /// <summary>
        /// The bind-pose position every dynamic hashes from, when the stroke
        /// has been posed — null on the ordinary dab, whose <see cref="Pos"/>
        /// is its own seed. A property rather than a fifth positional field
        /// so the four-element deconstructions all over this file keep
        /// meaning what they say.
        /// </summary>
        public SKPoint? Seed { get; init; }
    }

    /// <summary>Every dab of a stroke, in order.</summary>
    /// <param name="densify">
    /// A per-stroke densify cache, for the live preview. Null re-densifies from scratch, which is
    /// what every committed render wants — it walks a stroke once. Passing one is B46: the walk has
    /// to cover the whole stroke on every pointer event (BR1), and densifying it again each time
    /// was 0.84 ms of a 1.15 ms walk at 600 points, all but a fraction of it recomputation. The
    /// output is value-identical either way, which is what lets the live path use the cache and the
    /// commit not.
    /// </param>
    public static List<Dab> WalkDabs(Stroke stroke, IncrementalDensify? densify = null)
    {
        // A posed stroke walks its bind-pose path and stamps on its posed one
        // (docs/DESIGN-bones.md): the walk below this branch never sees it.
        // Note for the live rigged preview when it lands: this branch ignores
        // `densify` — the posed walk needs none, PoseStroke pre-densified —
        // but that also means the incremental cache does nothing for a posed
        // stroke, so a per-pointer-event pose preview must budget a full walk
        // per event or grow a posed equivalent of the cache (B46's cost).
        if (stroke.RestPoints is { Count: > 1 } rest && rest.Count == stroke.Points.Count)
            return WalkPosedDabs(stroke, rest);

        var brush = stroke.Brush;
        var dabs = new List<Dab>();

        // Direction comes from consecutive dab centres rather than from the
        // source points, so it follows the path the dabs actually took —
        // spacing and smoothing have already had their say by then.
        SKPoint? previous = null;
        var heading = double.NaN;
        // Distance along the stroke, for PaintLoad. Measured between dab
        // centres rather than between recorded points, so it is the path the
        // paint actually took after spacing and smoothing have had their say.
        double travelled = 0;

        foreach (var (pos, pressure) in DabPositions(stroke, densify))
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
            dabs.Add(new Dab(pos, pressure, heading, LoadAt(travelled, brush)));
            previous = pos;
        }
        return dabs;
    }

    /// <summary>
    /// The dab walk for a posed stroke: spacing, pressure and seeds come from
    /// the <b>rest</b> path, and each dab is placed at the corresponding spot
    /// on the posed one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the trap rule made mechanism — <em>dynamics seed from the
    /// bind-pose coordinates; the transform moves only placement</em>. The
    /// dab set is generated in bind space, so two poses of the same stroke
    /// lay down the same dabs with the same scatter, jitter and grain, moved.
    /// A walk over the posed path instead would re-roll everything wherever a
    /// joint stretched the path, and the character would boil exactly there.
    /// </para>
    /// <para>
    /// No densify on either list: <c>Skinning.PoseStroke</c> densified the
    /// rest path before posing, precisely so the 1:1 correspondence walked
    /// here exists. Heading and travelled distance follow the <b>posed</b>
    /// chain — direction-following tips and paint depletion belong to the
    /// mark the artist sees, and both vary smoothly with the pose rather
    /// than hashing from it.
    /// </para>
    /// </remarks>
    private static List<Dab> WalkPosedDabs(Stroke stroke, List<StrokePoint> rest)
    {
        var brush = stroke.Brush;
        var dabs = new List<Dab>();
        SKPoint? previous = null;
        var heading = double.NaN;
        double travelled = 0;

        foreach (var (seed, pos, pressure) in PosedDabPositions(rest, stroke.Points, brush))
        {
            if (previous is { } from)
            {
                float dx = pos.X - from.X, dy = pos.Y - from.Y;
                var step = Math.Sqrt(dx * dx + dy * dy);
                travelled += step;
                if (brush.AngleFollowsDirection && step > 0.01) heading = Math.Atan2(dy, dx) * 180 / Math.PI;
            }
            dabs.Add(new Dab(pos, pressure, heading, LoadAt(travelled, brush)) { Seed = seed });
            previous = pos;
        }
        return dabs;
    }

    /// <summary>
    /// <see cref="DabPositions"/>' walk run over the rest path, yielding each
    /// dab's seed (on the rest path) and its stamped position (the same
    /// parametric spot on the posed path). The two lists correspond 1:1.
    /// </summary>
    private static IEnumerable<(SKPoint Seed, SKPoint Pos, double Pressure)> PosedDabPositions(
        IReadOnlyList<StrokePoint> rest, IReadOnlyList<StrokePoint> posed, BrushSettings brush)
    {
        static SKPoint At(StrokePoint p) => new((float)p.X, (float)p.Y);

        var first = rest[0];
        var firstPressure = Math.Max(first.Pressure, MinPressure);
        yield return (At(first), At(posed[0]), firstPressure);

        double StepAt(double pressure) =>
            Math.Max(RadiusAt(brush, pressure) * 2 * brush.Spacing, MinStepPx);

        var step = StepAt(firstPressure);
        double acc = 0;
        var prev = first;
        var prevPosed = posed[0];
        for (var i = 1; i < rest.Count; i++)
        {
            var cur = rest[i];
            var curPosed = posed[i];
            var d = GeometryOps.Dist(prev, cur);
            while (d > 0 && acc + d >= step)
            {
                var t = (step - acc) / d;
                var np = GeometryOps.LerpPoint(prev, cur, t);
                // The posed position is the same chained lerp, with the same
                // t, on the posed segment — not a fresh parameter against the
                // segment's ends. Chaining keeps the arithmetic path
                // identical to the rest walk's, so an identity pose lands on
                // the very same doubles and renders the very same pixels.
                var npPosed = GeometryOps.LerpPoint(prevPosed, curPosed, t);
                var pressure = Math.Max(np.Pressure, MinPressure);
                yield return (At(np), At(npPosed), pressure);
                d -= step - acc;
                acc = 0;
                prev = np;
                prevPosed = npPosed;
                step = StepAt(pressure);
            }
            acc += d;
            prev = cur;
            prevPosed = curPosed;
        }
    }

    /// <summary>
    /// How many leading dabs two walks of the same growing stroke agree on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A dab's position is not settled the moment it is generated, and that was the
    /// second half of B45.</b> <see cref="GeometryOps.Densify"/> interpolates a span using
    /// its neighbours' control points, so the newest span is drawn straighter than it will
    /// be once the next point arrives. Dabs in it move — usually by a fraction of a pixel,
    /// but every dab dynamic is seeded from the dab's position, so with scatter at 0.35 on
    /// a 30 px brush a sub-pixel move throws the dab up to ten pixels somewhere else.
    /// Measured: identical renders at 0.85 px spans, a worst-case pixel 206/255 out at
    /// 8.7 px spans.
    /// </para>
    /// <para>
    /// <b>Determined by observation rather than by encoding Densify's rule.</b> Densify
    /// looks one point ahead, so a dab that survived the last point arriving will survive
    /// the next one too. Deriving it this way means a future change to smoothing or
    /// densification cannot silently invalidate the assumption — the comparison simply
    /// reports a shorter stable prefix.
    /// </para>
    /// <para>
    /// At least one whenever there is a dab at all: the first sits on the first recorded
    /// point, which nothing later can move, so a tap inks immediately.
    /// </para>
    /// </remarks>
    public static int StableCount(IReadOnlyList<Dab> now, IReadOnlyList<Dab>? before)
    {
        if (now.Count == 0) return 0;
        if (before is null) return 1;

        var agreed = 0;
        var shared = Math.Min(now.Count, before.Count);
        while (agreed < shared && now[agreed].Pos == before[agreed].Pos) agreed++;
        return Math.Max(1, agreed);
    }

    /// <summary>Every pixel the dabs from <paramref name="from"/> onward can touch.</summary>
    /// <remarks>
    /// Computed from the dabs rather than from the source points, because that is what the
    /// live preview has to be able to take back — and scatter can throw a dab well off the
    /// polyline the points describe. Null when the range is empty or off-canvas.
    /// </remarks>
    /// <param name="origin">
    /// The document's top-left in stroke coordinates. The returned rect is in
    /// <b>surface</b> coordinates — its consumers extract bitmap subsets with
    /// it — while the dabs it measures are in document ones, so the clamp is to
    /// the document rectangle and the result is shifted back by the origin.
    /// </param>
    public static SKRectI? RangeBounds(
        IReadOnlyList<Dab> dabs, int from, BrushSettings brush, SKImageInfo info,
        SKPointI origin = default)
    {
        if (from >= dabs.Count) return null;
        var reach = DabReach(brush);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = Math.Max(0, from); i < dabs.Count; i++)
        {
            var pos = dabs[i].Pos;
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
        }

        var rect = new SKRectI(
            (int)Math.Floor(minX - reach), (int)Math.Floor(minY - reach),
            (int)Math.Ceiling(maxX + reach), (int)Math.Ceiling(maxY + reach));
        var document = new SKRectI(
            origin.X, origin.Y, origin.X + info.Width, origin.Y + info.Height);
        var clipped = SKRectI.Intersect(rect, document);
        return clipped.Width <= 0 || clipped.Height <= 0 ? null : ToSurface(clipped, origin);
    }

    /// <summary>Stamp dabs <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive).</summary>
    public static void StampDabRange(
        SKCanvas canvas, Stroke stroke, IReadOnlyList<Dab> dabs, int from, int to)
    {
        var brush = stroke.Brush;
        var color = StrokeColor(stroke);
        var tip = brush.TipId is null ? null : BrushTipRegistry.Resolve(brush.TipId);
        var tipImage = brush.TipId is null ? null : BrushTipRegistry.ResolveImage(brush.TipId);

        for (var i = Math.Max(0, from); i < Math.Min(to, dabs.Count); i++)
        {
            var dab = dabs[i];
            StampDab(canvas, dab.Pos, dab.Pressure, brush, color, tip, dab.Heading, tipImage, dab.Load, dab.Seed);
        }
    }

    private static void StampDabs(SKCanvas canvas, Stroke stroke) =>
        StampDabRange(canvas, stroke, WalkDabs(stroke), 0, int.MaxValue);


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
    /// How far from a stroke point this brush can put paint — scatter, tip
    /// rotation and medium bleed included. The bound every region-limited
    /// repaint of a finished stroke must respect; half the brush size is the
    /// geometry of the points, not of the mark.
    /// </summary>
    public static float ReachOf(BrushSettings brush) => DabReach(brush);

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

    /// <summary>The stroke's points inflated by the dab reach, unclamped; null for an empty stroke.</summary>
    private static SKRectI? RawBounds(Stroke stroke, float margin)
    {
        if (stroke.Points.Count == 0) return null;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in stroke.Points)
        {
            minX = Math.Min(minX, (float)p.X);
            maxX = Math.Max(maxX, (float)p.X);
            minY = Math.Min(minY, (float)p.Y);
            maxY = Math.Max(maxY, (float)p.Y);
        }
        return new SKRectI(
            (int)Math.Floor(minX - margin),
            (int)Math.Floor(minY - margin),
            (int)Math.Ceiling(maxX + margin),
            (int)Math.Ceiling(maxY + margin));
    }

    /// <summary>
    /// The stroke's points inflated by the dab reach, clamped to the document
    /// rectangle; null when the stroke lies entirely off the paper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Returns DOCUMENT coordinates, and the clamp is to
    /// <c>[Left, Right) × [Top, Bottom)</c> rather than to
    /// <c>(0, 0, W, H)</c>.</b> Clamping to the surface instead is the bug that
    /// would ship green: it discards precisely the half of a stroke sitting in
    /// paper the canvas has just gained, which no comparison of the region the
    /// two documents share can see.
    /// </para>
    /// <para>
    /// Document rather than surface because the callers that consume this rect
    /// seed a repeating field from it — <see cref="ApplyGranulation"/> anchors a
    /// tiled noise shader to it and <see cref="ApplyTexture"/> passes its corner
    /// straight into <c>PaperField.Fill</c>. Handing those a surface rect leaves
    /// the mark where it was and slides the paper underneath it, which is
    /// invariant 2 broken in the one way that looks like nothing.
    /// </para>
    /// </remarks>
    private static SKRectI? SegmentBounds(Stroke stroke, SKImageInfo info, float margin, SKPointI origin)
    {
        if (RawBounds(stroke, margin) is not { } raw) return null;
        var left = Math.Clamp(raw.Left, origin.X, origin.X + info.Width);
        var top = Math.Clamp(raw.Top, origin.Y, origin.Y + info.Height);
        var right = Math.Clamp(raw.Right, origin.X, origin.X + info.Width);
        var bottom = Math.Clamp(raw.Bottom, origin.Y, origin.Y + info.Height);
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
    /// <param name="stroke">
    /// The <b>whole</b> stroke so far, never a tail of it.
    /// </param>
    /// <param name="alreadyStamped">
    /// How many of this stroke's dabs are already in the scratch — the return value of
    /// the previous call.
    /// </param>
    /// <returns>The new total, to hand back next time.</returns>
    /// <remarks>
    /// <b>Taking the whole stroke and a count is the point, not a convenience.</b>
    /// Passing a tail here is what made a live mark differ from its own commit (B45):
    /// the dab walk is a fold with four pieces of carried state, and a tail restarts all
    /// four. The signature no longer lets a caller do that.
    /// </remarks>

    /// <summary>Pixels a live segment can reach (dab size + scatter margin); null when off-canvas.</summary>
    /// <remarks>
    /// <b>Surface coordinates</b>, unlike the <see cref="SegmentBounds"/> it
    /// wraps: every caller uses this to repaint a region of a layer bitmap, and
    /// a bitmap's (0,0) is the document's top-left corner whatever that corner
    /// is called in stroke coordinates.
    /// </remarks>
    public static SKRectI? DraftSegmentBounds(Stroke tail, SKImageInfo info, SKPointI origin = default) =>
        SegmentBounds(tail, info, DabReach(tail.Brush), origin) is { } rect
            ? ToSurface(rect, origin)
            : null;


    /// <summary>
    /// Every pixel the FINAL render of a stroke can touch — the same margin
    /// the exact stamping path uses, widened for blur and for the feather of
    /// its clip region. Callers use it to repaint only what changed.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="DraftSegmentBounds" path="/remarks"/>
    /// </remarks>
    public static SKRectI? CommitBounds(Stroke stroke, SKImageInfo info, SKPointI origin = default) =>
        SegmentBounds(stroke, info, CommitMargin(stroke), origin) is { } rect
            ? ToSurface(rect, origin)
            : null;

    /// <summary>
    /// The same reach, unclamped — bounds as <em>where the stroke is</em> rather
    /// than <em>what to repaint</em>. <see cref="CommitBounds"/> clamps to the
    /// surface because nothing off the surface needs repainting; the stroke
    /// index and the picker ask a different question, and a stroke lying
    /// entirely outside the document still has to answer it (B134). Null only
    /// for a stroke with no points.
    /// </summary>
    public static SKRectI? ReachBounds(Stroke stroke) =>
        RawBounds(stroke, CommitMargin(stroke));

    /// <summary>The margin around a stroke's points that its final render can reach.</summary>
    private static float CommitMargin(Stroke stroke)
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
        return margin;
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

    private static void StampPaintDraft(
        SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels = null,
        SKPointI origin = default)
    {
        var brush = stroke.Brush;
        if (SegmentBounds(stroke, info, DabReach(brush), origin) is not { } rect) return;
        // Document rect for the dabs, surface rect for the two bitmaps and the
        // composite — the scratch translate below stays document because that
        // is the space StampDabs draws in.
        var surface = ToSurface(rect, origin);

        var boundsInfo = new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(boundsInfo);
        if (scratch is null) return;
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-rect.Left, -rect.Top);
        StampDabs(canvas, stroke);
        ApplyAlphaLock(canvas, stroke, targetPixels, surface);

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
            if (origin.X != 0 || origin.Y != 0)
            {
                path.Transform(SKMatrix.CreateTranslation(-origin.X, -origin.Y));
            }
            target.ClipPath(path, antialias: true);
        }
        target.DrawImage(snapshot, surface.Left, surface.Top, paint);
        if (region is not null) target.Restore();
    }

    /// <summary>How a bitmap tip is resampled. See the note in <see cref="StampDab"/>.</summary>
    private static readonly SKSamplingOptions TipSampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static void StampDab(
        SKCanvas canvas, SKPoint pos, double pressure, BrushSettings brush, SKColor color,
        SKBitmap? tip, double directionDeg = double.NaN, SKImage? tipImage = null,
        double load = 1, SKPoint? seed = null)
    {
        var radius = (float)(RadiusAt(brush, pressure));
        if (radius <= 0) return;

        var alpha = DabAlpha(brush, pressure) * load;
        if (alpha <= 0) return;

        // Every dynamic below is seeded from the dab's own position, never
        // from an index or a clock. Two consequences that matter here: a
        // re-render is identical, and — because neighbouring frames put dabs
        // in nearly the same places — a sequence does not boil at 12 fps.
        //
        // A POSED stroke's dab hashes from `s` — its bind-pose position —
        // instead, so a pose moves the mark without re-rolling it
        // (docs/DESIGN-bones.md). The scatter block shifts `s` by the same
        // offset it shifts `pos`, because the sites after it have always
        // hashed the SCATTERED position: mirroring the shift is what keeps an
        // unposed dab byte-identical to what it always was, and a posed dab's
        // seed chain anchored entirely in bind space.
        var s = seed ?? pos;
        if (brush.SizeJitter > 0)
        {
            var floor = Math.Clamp(brush.MinimumDiameter, 0, 1);
            var scale = floor + (1 - floor) * (1 - Hash01(s.X, s.Y, 11) * brush.SizeJitter);
            radius = (float)(radius * scale);
            if (radius <= 0) return;
        }

        if (brush.FlowJitter > 0)
        {
            alpha *= 1 - Hash01(s.X, s.Y, 12) * Math.Clamp(brush.FlowJitter, 0, 1);
            if (alpha <= 0) return;
        }

        color = JitterColor(color, s, brush);

        // Position-seeded scatter keeps re-renders identical. Pressure scales
        // how far it throws, not whether it throws — the direction stays the
        // hash's, so a harder press spreads the same pattern rather than
        // reshuffling it, and the mark does not boil between frames.
        var scatter = brush.Scatter * PressureResponse.Factor(brush, BrushDynamic.Scatter, pressure);
        if (scatter > 0)
        {
            var amount = Hash01(s.X, s.Y, 1) * scatter * brush.Size;
            var angle = Hash01(s.X, s.Y, 2) * Math.PI * 2;
            var (ox, oy) = ((float)(Math.Cos(angle) * amount), (float)(Math.Sin(angle) * amount));
            pos = new SKPoint(pos.X + ox, pos.Y + oy);
            s = new SKPoint(s.X + ox, s.Y + oy);
        }

        var dabColor = color.WithAlpha((byte)Math.Round(alpha * 255));

        // Roundness squashes the dab across its own axis. Combined with a
        // direction-following angle, that is what turns a round tip into a
        // chisel and a custom tip into bristles.
        // Pressure fattens the dab toward circular rather than thinning it:
        // a flat brush pressed down spreads, and a curve that squashed instead
        // would make a hard press narrower than a light one.
        var roundness = RoundnessAt(brush, s, pressure);

        if (tip is not null)
        {
            var rotation = brush.TipRotationDeg;
            if (brush.AngleFollowsDirection && !double.IsNaN(directionDeg)) rotation += directionDeg;
            if (brush.RotationJitter > 0)
            {
                rotation += (Hash01(s.X, s.Y, 3) - 0.5) * 360 * brush.RotationJitter;
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
        // Written into below, from the mask — nothing is drawn here first. It
        // used to receive the raw stroke image on creation, a full-region draw
        // the Clear before the carve then threw away entirely (B177).
        using var rim = SKSurface.Create(local);
        if (rim is null) return;

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
    /// <summary>Does this brush have a paper to bite into — imported or built in?</summary>
    private static bool HasTexture(BrushSettings brush) =>
        brush.TextureSurface is not null || TextureRegistry.Resolve(brush.TextureId) is not null;

    /// <summary>
    /// An imported texture, tiled in <em>document</em> coordinates.
    /// </summary>
    /// <remarks>
    /// Document coordinates, not region coordinates, and that is the whole of
    /// what makes it usable: the grain stays put as the artist draws over it,
    /// so two strokes crossing the same patch of paper sit on the same tooth.
    /// Sampling from the region's own origin instead would give every stroke
    /// its own paper, aligned to wherever the stroke happened to start — which
    /// looks correct in a single mark and obviously wrong the moment there are
    /// two.
    ///
    /// <c>TextureScale</c> is the size of one texel in document pixels, so it
    /// means the same thing it does for the procedural papers.
    /// </remarks>
    private static void FillFromImage(
        Span<float> dest, int width, int height, int originX, int originY,
        TextureRegistry.Field field, double scale)
    {
        var step = 1.0 / Math.Max(0.25, scale / 8.0);
        for (var y = 0; y < height; y++)
        {
            var v = (int)Math.Floor((originY + y) * step);
            for (var x = 0; x < width; x++)
            {
                var u = (int)Math.Floor((originX + x) * step);
                dest[y * width + x] = field.At(u, v);
            }
        }
    }

    private static void ApplyTexture(SKCanvas canvas, BrushSettings brush, SKRectI rect, SKImageInfo local)
    {
        var imported = TextureRegistry.Resolve(brush.TextureId);
        if (imported is null && brush.TextureSurface is null) return;

        var depth = (float)Math.Clamp(brush.TextureDepth, 0, 1);
        if (depth <= 0) return;

        var w = Math.Max(1, rect.Width);
        var h = Math.Max(1, rect.Height);
        var height = new float[w * h];

        if (imported is not null) FillFromImage(height, w, h, rect.Left, rect.Top, imported, brush.TextureScale);
        else Media.PaperField.Fill(height, w, h, rect.Left, rect.Top, brush.TextureSurface!.Value, brush.TextureScale);

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
    /// Unlike the other stamp paths, this one never puts the factor on the
    /// canvas transform. <see cref="LerpDab"/> writes through raw pixel spans
    /// on both <paramref name="target"/> and <paramref name="pixels"/> — a
    /// device-pixel patch, indexed by device-pixel bounds from
    /// <c>GetDeviceClipBounds</c> — so <em>every</em> coordinate, read or
    /// write, is converted to device pixels by hand before it touches memory.
    /// Scaling the canvas as well (B56) made a dab land at outputScale²
    /// instead of outputScale — invisible at 1x, where the two coincide, and
    /// invisible today because nothing in the app renders an effect brush at
    /// any other scale. Found while eliminating causes for B39, which it is
    /// not: B39 reproduces at outputScale 1.
    /// </param>
    private static void StampSmudge(
        SKCanvas target, SKBitmap pixels, Stroke stroke, double outputScale, SKPointI origin)
    {
        var brush = stroke.Brush;
        var strength = Math.Clamp(brush.Flow, 0, 1);
        if (strength <= 0) return;

        StampSmudgeDabs(
            target, pixels, stroke, outputScale, strength, WalkDabs(stroke), 0, int.MaxValue, default,
            origin);
    }

    /// <summary>
    /// What a smudge is carrying at a point in the stroke — the whole of its sequential state.
    /// </summary>
    /// <remarks>
    /// A smudge is not replayable from an index the way a blur is, and this is why. A blur dab reads
    /// the pre-stroke pixels, so dab <i>n</i> depends on nothing but its own position; a smudge dab
    /// depends on the colour the previous dabs handed it <em>and</em> on the pixels they wrote. The
    /// second half is the caller's problem (restore what the unsettled tail wrote, then re-stamp);
    /// this is the first half, small enough to checkpoint every pointer event.
    /// </remarks>
    public readonly record struct SmudgeCarry(SKColor Colour, bool Started);

    /// <summary>
    /// Stamp dabs <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive) of a
    /// smudge, resuming from <paramref name="carry"/> and reporting where it ends up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B69/B89.</b> The live preview used to hand the engine the newest <em>segment</em>, which
    /// restarted four things every pointer event: the dab walk's spacing phase, the carried colour,
    /// the heading, and — because the segment overlaps one point and a smudge is not idempotent —
    /// it re-smeared the join. All four changed the moment the pen lifted and the whole stroke was
    /// re-rendered from the record in one pass.
    /// </para>
    /// <para>
    /// This is the same treatment blur got in B54, plus the piece blur does not need. The caller
    /// walks the <b>whole</b> stroke so densification and spacing are the commit's, stamps only the
    /// range that is not settled, and carries <see cref="SmudgeCarry"/> across events so the
    /// resumed range sees exactly what a single pass would have handed it.
    /// </para>
    /// </remarks>
    /// <param name="origin">
    /// The document's top-left in stroke coordinates; zero for a document that has
    /// never been resized, which is every caller until one says otherwise.
    /// </param>
    public static SmudgeCarry StampSmudgeRange(
        SKCanvas target, SKBitmap read, Stroke stroke, IReadOnlyList<Dab> dabs, int from, int to,
        SmudgeCarry carry, SKPointI origin = default)
    {
        var strength = Math.Clamp(stroke.Brush.Flow, 0, 1);
        if (strength <= 0) return carry;
        return StampSmudgeDabs(target, read, stroke, 1.0, strength, dabs, from, to, carry, origin);
    }

    /// <summary>
    /// The shape of one dab, sampled per pixel: a tip's own alpha when the brush has one, and the
    /// radial hardness falloff when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B70.</b> A tip changed a paint dab and was silently ignored by smudge, blur and the
    /// blender — <c>StampSmudge</c> built its dab from <c>RadiusAt</c> and a falloff, and neither
    /// consulted <c>BrushTipRegistry</c>. So a chisel or a bristle did nothing on exactly the
    /// brushes whose whole job is to push existing paint around, and said nothing about it.
    /// </para>
    /// <para>
    /// Sampled rather than drawn, because these paths composite per pixel: the smudge deposit is a
    /// weighted move toward a colour, so what a tip contributes is a <em>weight</em>, not a shape to
    /// blit. Nearest-neighbour on purpose — the weight is multiplied into a lerp that is already
    /// smooth, and a bilinear fetch here would cost four reads per pixel to soften an edge the
    /// falloff has already softened.
    /// </para>
    /// <para>
    /// Rotation follows the same three rules a paint dab uses, in the same order, so a tip behaves
    /// the same whichever brush picks it up: the brush's own <c>TipRotationDeg</c>, the stroke's
    /// heading when <c>AngleFollowsDirection</c> is set, and jitter seeded from the dab's position
    /// through <c>Hash01</c> — never from an index or a clock, so invariant 2 holds and a re-render
    /// puts every bristle back where it was.
    /// </para>
    /// </remarks>
    private readonly struct DabShape
    {
        private readonly SKBitmap? _tip;
        private readonly float _cx, _cy, _cos, _sin, _invX, _invY, _hardness;

        /// <param name="seed">
        /// The dab's <b>document</b> position, and the only thing rotation jitter is
        /// allowed to hash. <paramref name="deviceCentre"/> is a surface coordinate:
        /// it moves when the paper is resized and it moves again with the output
        /// scale, so seeding from it would re-roll the bristles of every effect dab
        /// on a canvas resize and on a 2x render alike.
        /// </param>
        internal DabShape(
            BrushSettings brush, SKPoint deviceCentre, float deviceRadius, float roundness,
            double headingDeg, SKPoint seed)
        {
            _tip = brush.TipId is null ? null : BrushTipRegistry.Resolve(brush.TipId);
            _cx = deviceCentre.X;
            _cy = deviceCentre.Y;
            _hardness = (float)Math.Clamp(brush.Hardness, 0, 1);

            var rotation = brush.TipRotationDeg;
            if (brush.AngleFollowsDirection && !double.IsNaN(headingDeg)) rotation += headingDeg;
            if (brush.RotationJitter > 0)
            {
                rotation += (Hash01(seed.X, seed.Y, 3) - 0.5) * 360 * brush.RotationJitter;
            }
            var radians = rotation * Math.PI / 180;
            _cos = (float)Math.Cos(radians);
            _sin = (float)Math.Sin(radians);

            // The same scale StampDab applies: the tip is fitted to the dab's diameter on its longer
            // side, then squashed by roundness. Inverted here, since this maps pixels back to texels.
            var fitted = _tip is null ? 1f : deviceRadius * 2 / Math.Max(_tip.Width, _tip.Height);
            _invX = fitted <= 0 ? 0 : 1 / fitted;
            _invY = fitted <= 0 || roundness <= 0 ? 0 : 1 / (fitted * roundness);
        }

        /// <summary>Whether this dab is masked by a tip rather than by a falloff.</summary>
        internal bool HasTip => _tip is not null;

        /// <summary>
        /// Coverage at a device pixel, 0..1. <paramref name="falloff"/> is the radial weight the
        /// caller already computed, returned unchanged when there is no tip.
        /// </summary>
        internal float At(int x, int y, float falloff)
        {
            if (_tip is null) return falloff;

            // Into the dab's own frame, then out of its rotation and scale.
            var dx = x + 0.5f - _cx;
            var dy = y + 0.5f - _cy;
            var lx = (dx * _cos + dy * _sin) * _invX;
            var ly = (-dx * _sin + dy * _cos) * _invY;

            var tx = (int)MathF.Round(lx + _tip.Width / 2f);
            var ty = (int)MathF.Round(ly + _tip.Height / 2f);
            if (tx < 0 || ty < 0 || tx >= _tip.Width || ty >= _tip.Height) return 0;

            // A tip is a grayscale coverage map: its alpha is the shape. Multiplied by the falloff
            // rather than replacing it, so Hardness still softens a tipped effect dab the way it
            // softens a tipped paint dab.
            return _tip.GetPixel(tx, ty).Alpha / 255f * falloff;
        }
    }

    private static SmudgeCarry StampSmudgeDabs(
        SKCanvas target, SKBitmap pixels, Stroke stroke, double outputScale, double strength,
        IReadOnlyList<Dab> dabs, int from, int to, SmudgeCarry carry, SKPointI origin)
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

        var carried = carry.Colour;
        var hasColor = carry.Started;

        // Heading comes from the walk now rather than being tracked here. It has to: a resumed
        // range has no previous dab of its own, so recomputing locally would snap a direction-
        // following tip to zero degrees at every pointer-event boundary — visible on a chisel or a
        // bristle smudge (B70) and invisible on a round one, which is the worst kind of bug.
        for (var i = Math.Max(0, from); i < Math.Min(to, dabs.Count); i++)
        {
            var (pos, pressure, heading, _) = dabs[i];
            var radius = (float)RadiusAt(brush, pressure);
            if (radius <= 0) continue;

            var s = (float)outputScale;
            // The sample indexes the layer bitmap directly, so it is the one
            // place in this loop that speaks surface pixels; `pos` stays a
            // document coordinate for everything seeded from it.
            var surface = ToSurface(pos, origin);
            var sample = SampleAverage(
                pixels, new SKPoint(surface.X * s, surface.Y * s), Math.Max(1f, radius * spread * s));
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
            var deposit = dulling ? MixPigment(sample, carried, carryOver * 0.5) : carried;
            if (colorRate > 0) deposit = MixPigment(deposit, ownColor, colorRate);

            if (deposit.Alpha > 0)
            {
                LerpDab(
                    target, pixels, pos, radius, brush, deposit, strength, outputScale, heading, pressure,
                    origin, dabs[i].Seed);
            }

            // How much of the carried colour survives into the next dab. At 0
            // the sample is replaced every dab and colour barely travels; at 1
            // it is dragged the length of the stroke.
            carried = MixPigment(sample, carried, carryOver);
        }

        return new SmudgeCarry(carried, hasColor);
    }

    /// <summary>
    /// The colour under a dab: a coverage-weighted mean of five taps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B91, and it is the whole of why a smudge used to darken what it touched.</b>
    /// This averaged <em>unpremultiplied</em> RGB and alpha as four independent
    /// channels. A transparent pixel has no colour — Skia hands back
    /// <c>rgb(0,0,0)</c> for one — so every tap that landed off the edge of a mark
    /// dragged the sampled colour toward <b>black</b> in proportion to how much bare
    /// canvas the dab overlapped. Measured on a solid block with a dab centred on its
    /// edge, four taps of five off the paint: a <b>white</b> block sampled
    /// <c>rgb(50,50,50)</c> and a red one <c>rgb(40,5,5)</c>. Smudging outward did not
    /// carry the colour out, it carried a darkened ghost of it.
    /// </para>
    /// <para>
    /// Weighting each tap by its own coverage is the fix, and it is the same
    /// arithmetic <see cref="LerpDab"/> is careful about two functions down — "lerping
    /// premultiplied colour is the right operation as well as the fast one". The
    /// sampler simply never got the same treatment.
    /// </para>
    /// <para>
    /// Alpha is still the plain mean, and has to be: it is <em>how much</em> is under
    /// the dab, which is exactly the unweighted average. Only the colour is a question
    /// about the pixels that actually have one.
    /// </para>
    /// </remarks>
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
            r += c.Red * c.Alpha;
            g += c.Green * c.Alpha;
            b += c.Blue * c.Alpha;
            n++;
        }
        // Nothing with any coverage under the dab: no colour to report, and
        // dividing by the summed alpha would be a divide by zero.
        if (a == 0) return default;
        return new SKColor((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / n));
    }

    /// <summary>
    /// Blend two colours by coverage, rather than as four independent channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B91's second half.</b> <see cref="Mix"/> lerps unpremultiplied RGB and alpha
    /// separately, which is right for a gradient — its one other caller, where both
    /// stops are colours an artist chose — and wrong for pigment, where a nearly
    /// transparent entry should contribute nearly nothing to the hue. Carried through a
    /// smudge dab over dab it crushed the channels: a red mark
    /// <c>rgb(220,40,40)</c> arrived down the stroke as <c>rgb(205,6,6)</c>.
    /// </para>
    /// <para>
    /// Kept separate from <see cref="Mix"/> rather than replacing it, so the gradient
    /// path cannot be collateral damage from a smudge fix.
    /// </para>
    /// </remarks>
    private static SKColor MixPigment(SKColor x, SKColor y, double t)
    {
        double xa = x.Alpha / 255.0, ya = y.Alpha / 255.0;
        var a = xa + (ya - xa) * t;
        if (a <= 0) return default;

        byte Channel(byte xc, byte yc)
        {
            var premul = xc * xa + (yc * ya - xc * xa) * t;
            return (byte)Math.Clamp(Math.Round(premul / a), 0, 255);
        }

        return new SKColor(
            Channel(x.Red, y.Red),
            Channel(x.Green, y.Green),
            Channel(x.Blue, y.Blue),
            (byte)Math.Clamp(Math.Round(a * 255), 0, 255));
    }

    /// <summary>
    /// Lay one smudge dab down as a weighted move toward the sampled colour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is B38, and it is a pixel loop because Skia has no blend mode for
    /// what a smudge deposit means. What is wanted is
    /// <c>dst = lerp(dst, deposit, w)</c> with <c>w</c> the dab's falloff.
    /// <c>SrcOver</c> gets the <em>colour</em> right — it is already a lerp
    /// toward the source — and gets the <em>alpha</em> wrong, because it adds
    /// instead of interpolating. That is the whole bug: the deposit's alpha came
    /// from the sample, raising the canvas alpha, which raised the next sample,
    /// which raised the deposit. On a wash of alpha 60 ten overlapping dabs
    /// reached opaque black.
    /// </para>
    /// <para>
    /// <c>SrcATop</c> was tried and is worse: it holds the destination alpha,
    /// which is right, but paints nothing where the destination is transparent —
    /// so it breaks smudging onto bare canvas and breaks all-layers sampling
    /// outright, where the colour comes from the backdrop and the destination
    /// layer is empty by definition. Nine tests named that.
    /// </para>
    /// <para>
    /// Interpolating alpha as an ordinary channel is what makes both work: a
    /// dab over a wash converges on the wash's own opacity rather than climbing
    /// past it, and a dab reaching off the edge of a mark can still carry
    /// pigment out into bare canvas, because zero is a value to move away from
    /// rather than a place that cannot be painted.
    /// </para>
    /// <para>
    /// Bounded work, as invariant 6 requires: the loop covers one dab's
    /// bounding box, not the canvas. That is the same order as the draw it
    /// replaces.
    /// </para>
    /// </remarks>
    /// <param name="pos">
    /// The dab's <b>document</b> position. It is both the <c>Hash01</c> seed for
    /// roundness and rotation and — once the origin comes off — the centre of the
    /// pixel span written below. Those two uses are why this method is the awkward
    /// one: conflating them re-grains every smudge on a resized canvas while
    /// leaving the mark in the right place, which looks like nothing.
    /// </param>
    private static void LerpDab(
        SKCanvas target, SKBitmap read, SKPoint pos, float radius, BrushSettings brush,
        SKColor deposit, double strength, double outputScale, double headingDeg = double.NaN,
        double pressure = 1, SKPointI origin = default, SKPoint? seed = null)
    {
        // The target is written through its own bitmap rather than the canvas,
        // so flush anything the canvas still holds first.
        target.Flush();
        if (!target.GetDeviceClipBounds(out var clip) || clip.IsEmpty) return;

        var s = (float)outputScale;
        var surface = ToSurface(pos, origin);
        var cx = surface.X * s;
        var cy = surface.Y * s;
        var r = radius * s;
        if (r <= 0) return;

        var hardness = (float)Math.Clamp(brush.Hardness, 0, 1);
        var w0 = (float)Math.Clamp(strength, 0, 1);
        if (w0 <= 0) return;

        // B70: the dab's shape, which is the tip's alpha when the brush has one. Built once per dab
        // — the transform is per dab, only the sampling is per pixel.
        var shape = new DabShape(
            brush, new SKPoint(cx, cy), r, (float)RoundnessAt(brush, seed ?? pos, pressure), headingDeg,
            seed ?? pos);

        var left = Math.Max(clip.Left, (int)MathF.Floor(cx - r));
        var top = Math.Max(clip.Top, (int)MathF.Floor(cy - r));
        var right = Math.Min(clip.Right, (int)MathF.Ceiling(cx + r) + 1);
        var bottom = Math.Min(clip.Bottom, (int)MathF.Ceiling(cy + r) + 1);
        if (right <= left || bottom <= top) return;

        // Spans over PeekPixels, not GetPixel/SetPixel. Those are a P/Invoke
        // each, and the first cut of this cost 10.8 s for one 4K smudge stroke
        // against a 400 ms budget — the same lesson DESIGN-performance.md
        // already records from the first time this engine did it.
        using var readPix = read.PeekPixels();
        if (readPix is null) return;
        var srcRow = readPix.RowBytes;
        var src = readPix.GetPixelSpan();

        var info = new SKImageInfo(right - left, bottom - top, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var patch = new SKBitmap(info);
        using var patchPix = patch.PeekPixels();
        if (patchPix is null) return;
        var dstRow = patchPix.RowBytes;
        var dst = patchPix.GetPixelSpan<byte>();

        // The lattice is premultiplied, and lerping premultiplied colour is the
        // right operation as well as the fast one: no unpremultiply/repremultiply
        // per pixel, and no divide-by-zero where alpha is nil.
        var da = deposit.Alpha / 255f;
        var dr = deposit.Red * da;
        var dg = deposit.Green * da;
        var db = deposit.Blue * da;
        var dAl = deposit.Alpha;

        for (var y = top; y < bottom; y++)
        {
            var sRow = y * srcRow;
            var pRow = (y - top) * dstRow;
            for (var x = left; x < right; x++)
            {
                var si = sRow + x * 4;
                var pi = pRow + (x - left) * 4;

                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - cy;
                var d = MathF.Sqrt(dx * dx + dy * dy) / r;

                float w;
                if (d > 1f)
                {
                    w = 0f;
                }
                else
                {
                    // Flat to the hardness radius, then a linear ramp to
                    // nothing — the profile the radial-gradient paint drew.
                    var falloff = hardness >= 0.999f || d <= hardness
                        ? 1f
                        : 1f - (d - hardness) / (1f - hardness);
                    w = w0 * Math.Clamp(falloff, 0f, 1f);
                }

                w = shape.At(x, y, w);

                if (w <= 0f)
                {
                    dst[pi] = src[si];
                    dst[pi + 1] = src[si + 1];
                    dst[pi + 2] = src[si + 2];
                    dst[pi + 3] = src[si + 3];
                    continue;
                }

                // Rounded, not truncated, and that is B91's third finding. A cast to
                // byte truncates, so every dab lost up to a whole level on every
                // channel — a bias that only ever points one way and compounds across
                // the ~40 dabs that overlap a pixel at ordinary spacing. It hurt small
                // channel values hardest, because losing 1 from a premultiplied green
                // of 6 is 17% and losing it from a red of 35 is 3%: a red mark
                // rgb(220,40,40) desaturated to rgb(211,12,12) down the stroke while
                // the red channel barely moved. Alpha truncates the same way, which is
                // erosion the brush was never asked for.
                dst[pi] = (byte)(src[si] + (dr - src[si]) * w + 0.5f);
                dst[pi + 1] = (byte)(src[si + 1] + (dg - src[si + 1]) * w + 0.5f);
                dst[pi + 2] = (byte)(src[si + 2] + (db - src[si + 2]) * w + 0.5f);
                dst[pi + 3] = (byte)(src[si + 3] + (dAl - src[si + 3]) * w + 0.5f);
            }
        }

        using var image = SKImage.FromBitmap(patch);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        target.DrawImage(image, left, top, paint);
        target.Flush(); // the next sample must see this deposit
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
        SKCanvas target, SKBitmap pixels, Stroke stroke, SKImageInfo info, double outputScale,
        SKPointI origin)
    {
        var brush = stroke.Brush;
        var sigma = (float)(Math.Clamp(brush.Flow, 0, 1) * Math.Max(1, brush.Size) / 4);
        if (sigma <= 0) return;

        using var snapshot = SKImage.FromBitmap(pixels); // immutable copy of pre-stroke pixels
        if (snapshot is null) return;
        // Sigma is a document width; the canvas transform carries it into
        // device pixels, so the blur stays the same physical softness.
        // Src, not the default SrcOver: a blur REPLACES the dab's pixels with
        // the softened version. Drawing a semi-transparent blurred snapshot
        // over itself once per dab stacked its alpha, which is why a pale
        // stroke went black and why bare canvas beside it picked up a solid
        // surround from the blur's own spread (B37).
        using var blurPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
            BlendMode = SKBlendMode.Src,
        };

        // The snapshot is already at output resolution, so its destination is
        // the whole document — one for one in device pixels, not a resample.
        // Expressed in DOCUMENT coordinates, because the canvas below is
        // translated into that space so every dab position stays the one the
        // stroke recorded.
        var whole = new SKRect(
            origin.X, origin.Y, origin.X + info.Width, origin.Y + info.Height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);

        var tip = brush.TipId is null ? null : BrushTipRegistry.ResolveImage(brush.TipId);
        var tipBitmap = brush.TipId is null ? null : BrushTipRegistry.Resolve(brush.TipId);

        target.Save();
        if (outputScale != 1.0) target.Scale((float)outputScale);
        if (origin.X != 0 || origin.Y != 0) target.Translate(-origin.X, -origin.Y);
        var previous = (SKPoint?)null;
        var heading = double.NaN;
        // WalkDabs rather than DabPositions so a posed stroke's dabs arrive
        // with their bind-pose seeds; heading is still recomputed here, from
        // every step, as this path always has.
        foreach (var walked in WalkDabs(stroke))
        {
            var (pos, pressure) = (walked.Pos, walked.Pressure);
            var radius = (float)RadiusAt(brush, pressure);
            if (radius <= 0) continue;
            if (previous is { } from)
            {
                float hx = pos.X - from.X, hy = pos.Y - from.Y;
                if (hx * hx + hy * hy > 0.0001f) heading = Math.Atan2(hy, hx) * 180 / Math.PI;
            }
            previous = pos;

            if (tip is not null && tipBitmap is not null)
            {
                // B70: through the tip's own shape rather than a circle. A layer is needed because
                // the mask has to *subtract* — DstIn against the target itself would erase whatever
                // the tip does not cover, which is the layer the artist is blurring. The layer is
                // the dab's own bounds, so the cost tracks the dab and not the canvas (invariant 6).
                DrawBlurredThroughTip(
                    target, snapshot, whole, sampling, blurPaint, tip, tipBitmap, brush,
                    pos, radius, pressure, heading, walked.Seed);
                continue;
            }

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
    /// <remarks>
    /// <para>
    /// <b>B54, and its filed cause was wrong.</b> The entry blamed the cropped snapshot — a dab near
    /// the crop's edge sampling the boundary rather than the real neighbourhood — and quoted 88 px
    /// of over-coverage. Measured, the crop is innocent: hand this method a whole stroke in one call
    /// and it reproduces the committed render <em>to the pixel</em>.
    /// </para>
    /// <para>
    /// What was wrong is that it used to be handed <b>the newest segment</b> and walk it from
    /// scratch. Two consequences, both measured on a checkered bar at a 147 px brush against the
    /// commit: <c>GeometryOps.Densify</c> saw a two-point list and returned it unchanged, and the
    /// spacing phase restarted at that segment's first point — so the dabs landed elsewhere, giving
    /// <b>1148 px the preview blurred that the commit did not</b> and 610 the other way. And every
    /// dab was re-drawn on every event through an <em>antialiased</em> clip with
    /// <c>SKBlendMode.Src</c>, which is not idempotent at partial coverage α
    /// (<c>α·blurred + (1-α)·what is already there</c>), so rims crept to 223 of 255.
    /// </para>
    /// <para>
    /// This is BR1 — "the dab walk restarts every pointer event" — which was fixed for paint and
    /// left true for effects. The fix is the same shape: the caller walks the <b>whole</b> stroke,
    /// so densification and spacing are the commit's, and passes the range that is not settled yet.
    /// Backward context was tried instead and does not work: 2, 3, 4 and 5 points of overlap give
    /// 1579 / 1105 / 2518 / 1728 differing pixels — it wobbles rather than converging, because a
    /// walk's <em>phase</em> is what is wrong and no amount of lookbehind repairs a phase.
    /// </para>
    /// </remarks>
    private static void StampBlurDraft(
        SKCanvas target, SKBitmap pixels, Stroke stroke, SKImageInfo info,
        IReadOnlyList<Dab> dabs, int from, SKPointI origin)
    {
        var brush = stroke.Brush;
        var sigma = (float)(Math.Clamp(brush.Flow, 0, 1) * Math.Max(1, brush.Size) / 4);
        if (sigma <= 0) return;
        from = Math.Max(0, from);
        if (from >= dabs.Count) return;

        // **Only the unsettled dabs, and that is a measured trade rather than a preference.**
        // Replaying every dab of the stroke each event makes the preview *pixel-identical* to the
        // commit — 0 px, all four cases — and costs 194 ms an event by the 300th point of a 147 px
        // blur, against 20.8 ms for this range. Twelve frames of a 60 Hz budget is not a preview, so
        // exactness loses to invariant 6 here and the residue is written down instead: coverage is
        // exact, and the rim of the mark can sit up to 127 of 255 from the commit over a few hundred
        // pixels, because a pixel the commit reached with three overlapping dabs is reached here by
        // one and reads `α·blurred + (1-α)·pre-stroke`.
        //
        // The region those dabs can reach, plus the blur's own spread so a dab at its edge still
        // sees real neighbours rather than the crop.
        if (RangeBounds(dabs, from, brush, info, origin) is not { } dabReach) return;
        var rect = SKRectI.Intersect(
            SKRectI.Inflate(dabReach, (int)Math.Ceiling(sigma * 4), (int)Math.Ceiling(sigma * 4)),
            new SKRectI(0, 0, info.Width, info.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // `rect` is a surface rect, because it crops the bitmap. Every draw
        // below happens with the canvas translated into document space, so the
        // snapshot's destination is the same region named the other way.
        var docRect = new SKRectI(
            rect.Left + origin.X, rect.Top + origin.Y, rect.Right + origin.X, rect.Bottom + origin.Y);

        using var subset = new SKBitmap();
        if (!pixels.ExtractSubset(subset, rect)) return;
        using var snapshot = SKImage.FromBitmap(subset); // copies only the subset
        if (snapshot is null) return;
        // Src for the same reason as the committed path — see StampBlur.
        using var blurPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
            BlendMode = SKBlendMode.Src,
        };

        target.Save();
        if (origin.X != 0 || origin.Y != 0) target.Translate(-origin.X, -origin.Y);

        // Under the dabs about to be drawn, back to the pre-stroke pixels: they are the only ones
        // ever drawn twice, since the settled ones behind them are never revisited, so this is the
        // whole of what stops a rim creeping across a drag.
        //
        // **Clipped to the dabs and not to `rect`.** Restoring the padded rectangle reverts ground
        // earlier dabs blurred correctly and this call does not re-stamp it, which is a worse bug
        // than the one being fixed — 20,710 px of gap trailing the brush at a 147 px size. Widening
        // the range back to every dab overlapping the rectangle instead reaches 10,738 px out by
        // 255. Aliased on purpose: a partially restored rim would then be blended a second time by
        // the dab drawn over it.
        using (var covered = new SKPath())
        {
            for (var i = from; i < dabs.Count; i++)
            {
                var r = (float)RadiusAt(brush, dabs[i].Pressure);
                if (r > 0) covered.AddCircle(dabs[i].Pos.X, dabs[i].Pos.Y, r);
            }
            if (!covered.IsEmpty)
            {
                target.Save();
                target.ClipPath(covered, antialias: false);
                using var restore = new SKPaint { BlendMode = SKBlendMode.Src };
                target.DrawImage(snapshot, docRect.Left, docRect.Top, restore);
                target.Restore();
            }
        }

        var tip = brush.TipId is null ? null : BrushTipRegistry.ResolveImage(brush.TipId);
        var tipBitmap = brush.TipId is null ? null : BrushTipRegistry.Resolve(brush.TipId);
        var whole = new SKRect(
            docRect.Left, docRect.Top, docRect.Left + rect.Width, docRect.Top + rect.Height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);

        for (var i = from; i < dabs.Count; i++)
        {
            var radius = (float)RadiusAt(brush, dabs[i].Pressure);
            if (radius <= 0) continue;

            if (tip is not null && tipBitmap is not null)
            {
                // B70, and through the same helper as the commit — a preview that masked a dab
                // differently from the render would be B54 again in a new place.
                DrawBlurredThroughTip(
                    target, snapshot, whole, sampling, blurPaint, tip, tipBitmap, brush,
                    dabs[i].Pos, radius, dabs[i].Pressure, dabs[i].Heading, dabs[i].Seed);
                continue;
            }

            target.Save();
            using (var clip = new SKPath())
            {
                clip.AddCircle(dabs[i].Pos.X, dabs[i].Pos.Y, radius);
                target.ClipPath(clip, antialias: true);
            }
            target.DrawImage(snapshot, docRect.Left, docRect.Top, blurPaint);
            target.Restore();
        }

        target.Restore();
    }

    /// <summary>
    /// One blur dab shaped by the brush's tip: the blurred snapshot, kept only where the tip covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A layer, and it cannot be avoided.</b> What is wanted is
    /// <c>dst = lerp(dst, blurred, tipAlpha)</c>, and Skia has no blend mode for that against an
    /// existing surface. Drawing the tip with <c>DstIn</c> straight onto the target would erase
    /// everything the tip does not cover — which is the artist's painting. So the blurred content is
    /// built in a layer the size of the dab, masked there, and composited over.
    /// </para>
    /// <para>
    /// The layer is the dab's own bounds rather than the canvas or the stroke, so the cost tracks
    /// the mark and invariant 6 holds. It is also only taken when the brush <em>has</em> a tip: a
    /// round blur still goes through the cheap circle clip, so nothing that works today pays for
    /// this.
    /// </para>
    /// </remarks>
    private static void DrawBlurredThroughTip(
        SKCanvas target, SKImage snapshot, SKRect whole, SKSamplingOptions sampling, SKPaint blurPaint,
        SKImage tip, SKBitmap tipBitmap, BrushSettings brush,
        SKPoint pos, float radius, double pressure, double heading, SKPoint? seed = null)
    {
        var s = seed ?? pos;
        var roundness = (float)RoundnessAt(brush, s, pressure);
        var rotation = brush.TipRotationDeg;
        if (brush.AngleFollowsDirection && !double.IsNaN(heading)) rotation += heading;
        if (brush.RotationJitter > 0)
        {
            rotation += (Hash01(s.X, s.Y, 3) - 0.5) * 360 * brush.RotationJitter;
        }

        // Generous enough for a rotated tip's corner: the half-diagonal, not the radius.
        var reach = radius * 1.5f + 2;
        var bounds = new SKRect(pos.X - reach, pos.Y - reach, pos.X + reach, pos.Y + reach);

        target.Save();
        target.ClipRect(bounds);
        target.SaveLayer();

        // The blurred pixels, then the tip taking away everything it does not cover.
        target.DrawImage(snapshot, whole, sampling, blurPaint);

        // **A shader over the whole bounds, not the tip drawn at its own size.** Drawing the tip
        // image with DstIn only masks the pixels that image covers, so the ring between the tip's
        // rect and the dab's bounds kept its blur — measured at 19,646 px touched against the round
        // dab's 9,823, which is the opposite of the fix. As a shader with Decal tiling the tip reads
        // as transparent outside itself, so one DrawRect masks the whole layer.
        var scale = radius * 2 / Math.Max(tipBitmap.Width, tipBitmap.Height);
        var local = SKMatrix.CreateTranslation(-tipBitmap.Width / 2f, -tipBitmap.Height / 2f)
            .PostConcat(SKMatrix.CreateScale(scale, scale * roundness))
            .PostConcat(SKMatrix.CreateRotationDegrees((float)rotation))
            .PostConcat(SKMatrix.CreateTranslation(pos.X, pos.Y));
        using (var shader = SKShader.CreateImage(
            tip, SKShaderTileMode.Decal, SKShaderTileMode.Decal, TipSampling, local))
        using (var mask = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = shader,
            IsAntialias = brush.AntiAlias,
        })
        {
            target.DrawRect(bounds, mask);
        }

        target.Restore();   // composites the masked layer over the target
        target.Restore();
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

    /// <summary>
    /// How squashed this dab is, 0.05..1 — the other half of the dab's shape.
    /// </summary>
    /// <remarks>
    /// One implementation, because two paths need it and they must agree: a paint dab draws it as a
    /// canvas scale and an effect dab samples it per pixel (B70). Pressure fattens the dab toward
    /// circular rather than thinning it — a flat brush pressed down spreads, and a curve that
    /// squashed instead would make a hard press narrower than a light one. Jitter is seeded from the
    /// dab's position through <c>Hash01</c>, so invariant 2 holds.
    /// </remarks>
    private static double RoundnessAt(BrushSettings brush, SKPoint pos, double pressure)
    {
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
        return roundness;
    }

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
    /// <param name="densify">
    /// A per-stroke cache for the live preview; null densifies from scratch. See
    /// <see cref="WalkDabs(Stroke, IncrementalDensify?)"/>.
    /// </param>
    public static IEnumerable<(SKPoint Pos, double Pressure)> DabPositions(
        Stroke stroke, IncrementalDensify? densify = null)
    {
        var pts = densify is null ? GeometryOps.Densify(stroke.Points) : densify.Of(stroke.Points);
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
