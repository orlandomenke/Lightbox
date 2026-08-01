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
    public static void StampStroke(SKCanvas target, Stroke stroke, SKImageInfo info, SKBitmap? targetPixels = null, bool draft = false)
    {
        if (stroke.Points.Count == 0) return;

        if (stroke.Tool == ToolKind.Fill)
        {
            StampFill(target, stroke, info);
            return;
        }

        switch (stroke.Brush.Kind)
        {
            case BrushKind.Smudge when targetPixels is not null && stroke.Tool != ToolKind.Eraser:
                WithHardClip(target, stroke, () => StampSmudge(target, targetPixels, stroke));
                return;
            case BrushKind.Blur when targetPixels is not null && stroke.Tool != ToolKind.Eraser:
                if (draft) WithHardClip(target, stroke, () => StampBlurDraft(target, targetPixels, stroke, info));
                else WithHardClip(target, stroke, () => StampBlur(target, targetPixels, stroke, info));
                return;
        }

        if (draft) StampPaintDraft(target, stroke, info);
        else StampPaint(target, stroke, info);
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
    private static void StampFill(SKCanvas target, Stroke stroke, SKImageInfo info)
    {
        if (stroke.Points.Count < 3) return;
        using var scratch = SKSurface.Create(info);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        var contours = new List<IReadOnlyList<StrokePoint>> { stroke.Points };
        if (stroke.Holes is not null) contours.AddRange(stroke.Holes);
        using (var path = PathFromContours(contours))
        using (var paint = new SKPaint { IsAntialias = stroke.Brush.AntiAlias, Color = ParseColor(stroke.Color) })
        {
            canvas.DrawPath(path, paint);
        }
        ApplyClip(canvas, stroke, info);

        using var snapshot = scratch.Snapshot();
        using var composite = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(stroke.Brush.Opacity, 0, 1) * 255)),
            BlendMode = SKBlendMode.SrcOver,
        };
        target.DrawImage(snapshot, 0, 0, composite);
    }

    /// <summary>Apply the stroke's recorded selection (if any) to a full-canvas scratch.</summary>
    private static void ApplyClip(SKCanvas scratchCanvas, Stroke stroke, SKImageInfo info) =>
        ApplyClip(scratchCanvas, stroke, info, new SKRectI(0, 0, info.Width, info.Height));

    /// <summary>
    /// Apply the stroke's recorded selection (if any) to a scratch that covers
    /// only <paramref name="rect"/> of the document (the scratch canvas must
    /// be translated to doc coordinates).
    /// </summary>
    private static void ApplyClip(SKCanvas scratchCanvas, Stroke stroke, SKImageInfo local, SKRectI rect)
    {
        if (stroke.ClipId is null || ClipRegionRegistry.Resolve(stroke.ClipId) is not { } region) return;
        using var mask = SKSurface.Create(local);
        if (mask is null) return;
        mask.Canvas.Clear(SKColors.Transparent);
        mask.Canvas.Translate(-rect.Left, -rect.Top);
        using (var path = PathFromContours(region.Contours))
        using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.White })
        {
            if (region.Feather > 0)
            {
                var sigma = (float)(region.Feather / 2);
                paint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);
            }
            mask.Canvas.DrawPath(path, paint);
        }
        using var maskImage = mask.Snapshot();
        using var clipPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        scratchCanvas.DrawImage(maskImage, rect.Left, rect.Top, clipPaint);
    }

    /// <summary>Smudge/blur mutate the target directly — clip them with a hard path clip.</summary>
    private static void WithHardClip(SKCanvas target, Stroke stroke, Action stamp)
    {
        if (stroke.ClipId is null || ClipRegionRegistry.Resolve(stroke.ClipId) is not { } region)
        {
            stamp();
            return;
        }
        target.Save();
        using (var path = PathFromContours(region.Contours))
        {
            target.ClipPath(path, antialias: true);
        }
        stamp();
        target.Restore();
    }

    // ---- paint (the default pipeline) ----------------------------------------

    private static void StampPaint(SKCanvas target, Stroke stroke, SKImageInfo info)
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

        var local = new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scratch = SKSurface.Create(local);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-rect.Left, -rect.Top); // scratch canvas works in DOC coordinates

        StampDabs(canvas, stroke);

        if (brush.WetEdge > 0) ApplyWetEdge(scratch, canvas, brush, local, rect);
        if (brush.Granulation > 0) ApplyGranulation(canvas, brush, rect);
        ApplyClip(canvas, stroke, local, rect);

        using var snapshot = scratch.Snapshot();
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(brush.Opacity, 0, 1) * 255)),
            BlendMode = stroke.Tool == ToolKind.Eraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
        };
        target.DrawImage(snapshot, rect.Left, rect.Top, paint);
    }

    private static void StampDabs(SKCanvas canvas, Stroke stroke)
    {
        var brush = stroke.Brush;
        var color = ParseColor(stroke.Color);
        var tip = brush.TipId is null ? null : BrushTipRegistry.Resolve(brush.TipId);
        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            StampDab(canvas, pos, pressure, brush, color, tip);
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
        return (float)(brush.Size * (radiusFactor + Math.Max(0, brush.Scatter)) + 4);
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
            BlendMode = stroke.Tool == ToolKind.Eraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
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

    private static void StampPaintDraft(SKCanvas target, Stroke stroke, SKImageInfo info)
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

        using var snapshot = scratch.Snapshot();
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(brush.Opacity, 0, 1) * 255)),
            BlendMode = stroke.Tool == ToolKind.Eraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
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

    private static void StampDab(SKCanvas canvas, SKPoint pos, double pressure, BrushSettings brush, SKColor color, SKBitmap? tip)
    {
        var radius = (float)(RadiusAt(brush, pressure));
        if (radius <= 0) return;

        var alpha = DabAlpha(brush, pressure);
        if (alpha <= 0) return;

        // Position-seeded scatter keeps re-renders identical.
        if (brush.Scatter > 0)
        {
            var amount = Hash01(pos.X, pos.Y, 1) * brush.Scatter * brush.Size;
            var angle = Hash01(pos.X, pos.Y, 2) * Math.PI * 2;
            pos = new SKPoint(pos.X + (float)(Math.Cos(angle) * amount), pos.Y + (float)(Math.Sin(angle) * amount));
        }

        var dabColor = color.WithAlpha((byte)Math.Round(alpha * 255));
        if (tip is not null)
        {
            var rotation = brush.TipRotationDeg;
            if (brush.RotationJitter > 0)
            {
                rotation += (Hash01(pos.X, pos.Y, 3) - 0.5) * 360 * brush.RotationJitter;
            }
            canvas.Save();
            canvas.Translate(pos.X, pos.Y);
            canvas.RotateDegrees((float)rotation);
            var scale = radius * 2 / Math.Max(tip.Width, tip.Height);
            canvas.Scale(scale);
            using var paint = new SKPaint
            {
                IsAntialias = brush.AntiAlias,
                ColorFilter = SKColorFilter.CreateBlendMode(dabColor, SKBlendMode.SrcIn),
            };
            canvas.DrawBitmap(tip, -tip.Width / 2f, -tip.Height / 2f, paint);
            canvas.Restore();
            return;
        }

        using var round = new SKPaint { IsAntialias = brush.AntiAlias };
        var hardness = HardnessAt(brush, pressure);
        if (hardness >= 0.999f)
        {
            round.Color = dabColor;
        }
        else
        {
            round.Shader = SKShader.CreateRadialGradient(
                pos,
                radius,
                [dabColor, dabColor.WithAlpha(0)],
                [hardness, 1f],
                SKShaderTileMode.Clamp);
        }
        canvas.DrawCircle(pos, radius, round);
    }

    /// <summary>
    /// Darkened rim where paint pools at the stroke's edge (watercolor look).
    /// Operates on a stroke-bounded scratch: the rim surface matches the
    /// scratch's local size, and the result composites back at the scratch's
    /// document offset.
    /// </summary>
    private static void ApplyWetEdge(SKSurface scratch, SKCanvas canvas, BrushSettings brush, SKImageInfo local, SKRectI rect)
    {
        using var img = scratch.Snapshot();
        // Rim width, in pixels, capped so a huge brush does not imply a huge
        // band: past a few dozen pixels it stops reading as a wet edge anyway.
        var width = (float)Math.Clamp(brush.Size * 0.12, 1.0, 48.0);
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
        canvas.DrawImage(rimImg, rect.Left, rect.Top, darken);
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
    private static void StampSmudge(SKCanvas target, SKBitmap pixels, Stroke stroke)
    {
        var brush = stroke.Brush;
        var strength = Math.Clamp(brush.Flow, 0, 1);
        if (strength <= 0) return;

        SKColor carried = default;
        var hasColor = false;
        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            var radius = (float)RadiusAt(brush, pressure);
            if (radius <= 0) continue;

            var sample = SampleAverage(pixels, pos, Math.Max(1f, radius / 2));
            if (!hasColor)
            {
                carried = sample;
                hasColor = true;
                continue;
            }

            if (carried.Alpha > 0)
            {
                using var paint = new SKPaint { IsAntialias = brush.AntiAlias };
                var dabColor = carried.WithAlpha((byte)Math.Round(carried.Alpha * strength));
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

            carried = Mix(carried, sample, 0.5);
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
    private static void StampBlur(SKCanvas target, SKBitmap pixels, Stroke stroke, SKImageInfo info)
    {
        var brush = stroke.Brush;
        var sigma = (float)(Math.Clamp(brush.Flow, 0, 1) * Math.Max(1, brush.Size) / 4);
        if (sigma <= 0) return;

        using var snapshot = SKImage.FromBitmap(pixels); // immutable copy of pre-stroke pixels
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
            target.DrawImage(snapshot, 0, 0, blurPaint);
            target.Restore();
        }
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

    private static double RadiusAt(BrushSettings brush, double pressure)
    {
        if (!brush.PressureEnabled) pressure = 1;
        var gamma = brush.PressureSizeGamma <= 0 ? 0.0 : brush.PressureSizeGamma;
        var factor = gamma <= 0 ? 1.0 : Math.Pow(pressure, gamma);
        return brush.Size * factor / 2;
    }

    private static double DabAlpha(BrushSettings brush, double pressure)
    {
        if (!brush.PressureEnabled) pressure = 1;
        var flow = Math.Clamp(brush.Flow, 0, 1);
        if (brush.PressureFlowGamma > 0) flow *= Math.Pow(pressure, brush.PressureFlowGamma);
        return Math.Clamp(flow, 0, 1);
    }

    /// <summary>Dab-edge hardness after the pressure response (light pressure = softer edge).</summary>
    private static float HardnessAt(BrushSettings brush, double pressure)
    {
        var hardness = Math.Clamp(brush.Hardness, 0, 1);
        if (brush.PressureEnabled && brush.PressureHardnessGamma > 0)
        {
            hardness *= Math.Pow(pressure, brush.PressureHardnessGamma);
        }
        return (float)Math.Clamp(hardness, 0, 1);
    }

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
    public static IEnumerable<(SKPoint Pos, double Pressure)> DabPositions(Stroke stroke)
    {
        var pts = stroke.Points;
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

    public static SKColor ParseColor(string hex)
    {
        var (r, g, b) = ColorOps.HexToRgb(hex);
        return new SKColor(r, g, b);
    }
}
