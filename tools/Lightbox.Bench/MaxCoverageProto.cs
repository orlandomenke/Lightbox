using System.Diagnostics;
using System.Text;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// A prototype of the other way to stop overlapping dabs saturating their own
/// rim: accumulate coverage as a running <b>maximum</b> instead of unioning one
/// path (B299).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is worth measuring rather than arguing about.</b> The shipped
/// silhouette fixes rim saturation by computing coverage once for a path that
/// unions every dab. That is correct and it is why Ink costs <b>12.46 ms</b> an
/// event at 4K on a 400-event stroke - 4.18 ms rebuilding a 12,050-contour path
/// and 8.28 ms filling it - against 0.20-0.66 ms for every other shipped brush.
/// Neither half is fixable by shrinking the band alone.
/// </para>
/// <para>
/// <b>Max is the property that changes the shape of the problem.</b> It is
/// order-independent and idempotent, so a cached prefix plus a fresh tail is
/// exactly the whole: there is no settled cut to derive and get wrong, which is
/// what defeated three attempts at the clip. Per event only the new dabs are
/// stamped, so there is no path to rebuild and no band to re-fill.
/// </para>
/// <para>
/// <b>The mechanism is already in the engine.</b> <c>StampFootprint</c>
/// accumulates a soft dab's shape this way and its remarks carry the trap:
/// Skia has no blend mode that maxes <em>alpha</em> - every separable mode
/// composites alpha the Porter-Duff way, so <c>Lighten</c> on an alpha gradient
/// gives <c>1-(1-a)(1-b)</c>, the very saturation being undone. On an
/// <b>opaque</b> surface both alphas are 1 and <c>Lighten</c> is exactly
/// <c>max</c> per colour channel.
/// </para>
/// <para>
/// <b>What this prototype has to do differently, and it is the crux.</b>
/// <c>StampFootprint</c>'s gradient runs White at the hardness stop to Black at
/// 1, which for a hard brush (hardness 1) is degenerate - white to the rim and
/// nothing to antialias with. Leaning on the shape's own antialiasing instead
/// would put the coverage back into source alpha and re-saturate. So the ramp
/// here is a fixed <b>one pixel</b> at the rim, expressed in the shader, and
/// the circle is drawn <em>aliased</em> and one pixel oversized so every touched
/// pixel carries source alpha 1 and the blend really is a max. That ramp is a
/// straight line where true disc coverage is an S, which is the fidelity
/// question the numbers below were meant to answer.
/// </para>
/// <para>
/// <b>The cost number is sound and the fidelity number is NOT, and the second
/// is why this is committed unfinished.</b> Per event the prototype costs
/// <b>0.02 ms</b> against the silhouette's 12.46 ms, and that is a timing of
/// one thing rather than a comparison of two, so it stands: max accumulation is
/// incremental exactly as its algebra promises. The pixel comparison, however,
/// puts the two marks about <b>14 px apart</b> down the middle of the same arc,
/// growing from nothing at the start - a progressive skew, on the same dab
/// list, when the two coverage models can differ by at most a fraction of a
/// pixel. That is a fault in this harness, not a finding about the approach,
/// and until it is found the 204/255 mean below means nothing.
/// </para>
/// <para>
/// <b>Ruled out so far</b>, each by changing it and re-running to identical
/// output: the two renders using different dab lists (the reference now draws
/// from the list the prototype stamped); the colourise pass indexing bitmaps by
/// hand (rewritten to <c>SetPixel</c>); and the band bounds. The next places to
/// look are <c>MaxDab</c>'s oversized circle and gradient stops, which are the
/// only geometry this file owns, and whether <c>DrawCircle</c> under a shader
/// centred on the same point lands where <c>BuildSilhouette</c>'s
/// <c>AddCircle</c> does.
/// </para>
/// </remarks>
public static class MaxCoverageProto
{
    private const int Width = 3840;
    private const int Height = 2160;

    /// <summary>Document pixels the owner's hand covers per delivered move.</summary>
    private const double TravelPerEvent = 15.1;

    private static Stroke Ink(int points)
    {
        var preset = BuiltInPresets.Create().First(p => p.Name == "Ink");
        var pts = new List<StrokePoint>(points);
        double x = 200, y = 1080;
        var heading = 0.0;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.004;
            x += TravelPerEvent * Math.Cos(heading);
            y += TravelPerEvent * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Brush = preset.Settings,
            Points = pts,
        };
    }

    /// <summary>
    /// One dab's coverage, maxed into the footprint's colour channels.
    /// </summary>
    /// <remarks>
    /// Aliased and oversized on purpose. An antialiased edge would arrive as
    /// source alpha and blend Porter-Duff; drawing one pixel wide of the rim
    /// with the gradient already at black means the outer ring contributes
    /// <c>max(0, dst) = dst</c> and the jagged geometric edge cannot show.
    /// </remarks>
    private static void MaxDab(SKCanvas footprint, SKPoint centre, float radius)
    {
        var outer = radius + 1f;
        using var paint = new SKPaint
        {
            IsAntialias = false,
            BlendMode = SKBlendMode.Lighten,
            Shader = SKShader.CreateRadialGradient(
                centre,
                outer,
                [SKColors.White, SKColors.White, SKColors.Black, SKColors.Black],
                [0f, Math.Max(0f, (radius - 0.5f) / outer), (radius + 0.5f) / outer, 1f],
                SKShaderTileMode.Clamp),
        };
        footprint.DrawCircle(centre, outer, paint);
    }

    /// <summary>Turn the footprint's red channel into premultiplied ink.</summary>
    /// <remarks>
    /// A managed pass because this is a prototype and the question is whether
    /// the <em>coverage</em> matches; in the engine this is a colour matrix or a
    /// runtime shader and costs one band draw. Bounded to the region asked for,
    /// which is what keeps the per-event cost incremental.
    /// </remarks>
    private static void Colourise(SKBitmap footprint, SKBitmap into, SKColor color, SKRectI band)
    {
        // SetPixel rather than pointer arithmetic. The first draft indexed both
        // bitmaps by hand and skewed the mark progressively across the canvas -
        // 14 px out by mid-stroke, which reads exactly like a fidelity result
        // and is not one. This is a prototype whose job is to answer whether the
        // COVERAGE matches; the per-pixel cost of being obviously right is worth
        // more here than the speed, and the timing above excludes it anyway in
        // the engine, where this is a colour matrix over one band.
        for (var y = Math.Max(0, band.Top); y < Math.Min(into.Height, band.Bottom); y++)
        {
            for (var x = Math.Max(0, band.Left); x < Math.Min(into.Width, band.Right); x++)
            {
                var cover = footprint.GetPixel(x, y).Red;
                into.SetPixel(x, y, new SKColor(color.Red, color.Green, color.Blue, cover));
            }
        }
    }

    private static SKRectI BandOf(IReadOnlyList<BrushEngine.Dab> dabs, int from, float reach)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = Math.Max(0, from); i < dabs.Count; i++)
        {
            var p = dabs[i].Pos;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        if (minX > maxX) return SKRectI.Empty;
        return new SKRectI(
            (int)Math.Floor(minX - reach), (int)Math.Floor(minY - reach),
            (int)Math.Ceiling(maxX + reach), (int)Math.Ceiling(maxY + reach));
    }

    public static string Report(int points = 400, int repeats = 3)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var sb = new StringBuilder();
        sb.AppendLine("-- max-accumulated coverage, as an alternative (B299) ---------");
        sb.AppendLine($"   Ink, {points} events, {Width}x{Height}, {TravelPerEvent} document px an event.");
        sb.AppendLine();

        var stroke = Ink(points);
        var color = SKColor.Parse(stroke.Color);
        var densify = new IncrementalDensify();
        var dabs = BrushEngine.WalkDabs(stroke, densify);
        var radius = (float)BrushEngine.RadiusAt(stroke.Brush, 1);

        using var reference = new SKBitmap(info);

        // ---- the prototype, stamped the way a live stroke would ----------------
        using var mark = new SKBitmap(info);
        using var footprint = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var perEvent = double.MaxValue;
        List<BrushEngine.Dab>? finalDabs = null;

        for (var r = 0; r < repeats; r++)
        {
            using var fpCanvas = new SKCanvas(footprint);
            fpCanvas.Clear(SKColors.Black);
            using (var m = new SKCanvas(mark)) m.Clear(SKColors.Transparent);

            var stamped = 0;
            var last = 0.0;
            // Event by event, exactly as the live path would: only the dabs that
            // are new since the previous event are touched.
            for (var n = 2; n <= points; n++)
            {
                var sofar = new Stroke
                {
                    Tool = stroke.Tool,
                    Color = stroke.Color,
                    Brush = stroke.Brush,
                    Points = stroke.Points.Take(n).ToList(),
                };
                var now = BrushEngine.WalkDabs(sofar, densify);

                var sw = Stopwatch.StartNew();
                for (var i = stamped; i < now.Count; i++)
                {
                    MaxDab(fpCanvas, now[i].Pos, (float)BrushEngine.RadiusAt(stroke.Brush, now[i].Pressure));
                }

                fpCanvas.Flush();
                var band = BandOf(now, stamped, radius + 2);
                if (!band.IsEmpty) Colourise(footprint, mark, color, band);
                last = sw.Elapsed.TotalMilliseconds;
                stamped = now.Count;
            }

            perEvent = Math.Min(perEvent, last);
            finalDabs = BrushEngine.WalkDabs(stroke, densify);
        }

        // The reference is rendered from the SAME dab list the prototype
        // stamped, and that is not fussiness. Walking the full stroke with one
        // IncrementalDensify and then walking prefixes with it gives two
        // different lists here, and comparing them put the two marks 14 px
        // apart down the same arc — a harness fault that reads exactly like a
        // fidelity result (204/255 mean) if it is not ruled out.
        using (var canvas = new SKCanvas(reference))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampDabRange(canvas, stroke, finalDabs!, 0, finalDabs!.Count);
            canvas.Flush();
        }

        // ---- the same model, stamped ONCE from the final list ------------------
        // The discriminator. If this matches the reference and the incremental
        // one does not, the coverage model is sound and what drifts is the
        // bookkeeping: max cannot take back a dab that moved, and Densify moves
        // the newest span until the point after it arrives.
        using var oneShot = new SKBitmap(info);
        using (var fp2 = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque)))
        {
            using (var c2 = new SKCanvas(fp2))
            {
                c2.Clear(SKColors.Black);
                foreach (var dab in finalDabs!)
                {
                    MaxDab(c2, dab.Pos, (float)BrushEngine.RadiusAt(stroke.Brush, dab.Pressure));
                }

                c2.Flush();
            }

            using (var m2 = new SKCanvas(oneShot)) m2.Clear(SKColors.Transparent);
            Colourise(fp2, oneShot, color, BandOf(finalDabs!, 0, radius + 2));
        }

        static (double Mean, int Worst, long Differing, long Total) Compare(SKBitmap a, SKBitmap b)
        {
            double sum = 0;
            long differing = 0, total = 0;
            var worst = 0;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                int pa = a.GetPixel(x, y).Alpha, pb = b.GetPixel(x, y).Alpha;
                if (pa == 0 && pb == 0) continue;
                total++;
                var d = Math.Abs(pa - pb);
                if (d > 0) differing++;
                if (d > worst) worst = d;
                sum += d;
            }

            return (total == 0 ? 0 : sum / total, worst, differing, total);
        }

        var oneShotVsRef = Compare(reference, oneShot);
        // Which mark is where the dabs actually are. Decides whether the
        // reference or the prototype is the one that moved.
        // The same dabs down the PER-DAB path, by nudging hardness under the
        // silhouette predicate. If this lands where the dabs are and the
        // silhouette does not, the displacement is the silhouette's.
        var soft = System.Text.Json.JsonSerializer.Deserialize<BrushSettings>(
            System.Text.Json.JsonSerializer.Serialize(stroke.Brush))!;
        soft.Hardness = 0.99;
        var perDabStroke = new Stroke
        {
            Tool = stroke.Tool, Color = stroke.Color, Brush = soft, Points = stroke.Points,
        };
        using var perDab = new SKBitmap(info);
        using (var c3 = new SKCanvas(perDab))
        {
            c3.Clear(SKColors.Transparent);
            BrushEngine.StampDabRange(c3, perDabStroke, finalDabs!, 0, finalDabs!.Count);
            c3.Flush();
        }

        sb.AppendLine($"   silhouette predicate: ink {BrushEngine.DrawsAsOneSilhouette(stroke.Brush)}, soft {BrushEngine.DrawsAsOneSilhouette(soft)}");
        sb.AppendLine("   at x=1511, first ink row down the column:");
        static int FirstInk(SKBitmap b, int x)
        {
            for (var y = 0; y < Height; y++) if (b.GetPixel(x, y).Alpha > 8) return y;
            return -1;
        }

        sb.AppendLine("      x    dab y   per-dab  silhouette   max     error");
        foreach (var px in new[] { 400, 800, 1200, 1600, 2000, 2400, 2800 })
        {
            var near = finalDabs!.OrderBy(d => Math.Abs(d.Pos.X - px)).First();
            int pd = FirstInk(perDab, px), si = FirstInk(reference, px), mx = FirstInk(mark, px);
            sb.AppendLine(
                $"   {px,5}{near.Pos.Y,9:0.0}{pd,10}{si,12}{mx,7}{(si < 0 || pd < 0 ? 0 : si - pd),9}");
        }

        sb.AppendLine();

        var probe = finalDabs!.OrderBy(d => Math.Abs(d.Pos.X - 1511f)).First();
        sb.AppendLine($"   dab nearest x=1511 sits at  ({probe.Pos.X:0.0}, {probe.Pos.Y:0.0})  r={BrushEngine.RadiusAt(stroke.Brush, probe.Pressure):0.00}");
        sb.AppendLine($"   first dab                   ({finalDabs![0].Pos.X:0.0}, {finalDabs![0].Pos.Y:0.0})");
        sb.AppendLine($"   last dab                    ({finalDabs![^1].Pos.X:0.0}, {finalDabs![^1].Pos.Y:0.0})");
        sb.AppendLine($"   first point                 ({stroke.Points[0].X:0.0}, {stroke.Points[0].Y:0.0})");
        sb.AppendLine($"   last point                  ({stroke.Points[^1].X:0.0}, {stroke.Points[^1].Y:0.0})");
        sb.AppendLine();
        sb.AppendLine("   ONE-SHOT max vs the shipped silhouette (model fidelity)");
        sb.AppendLine($"     mean difference           {oneShotVsRef.Mean,8:0.00} /255");
        sb.AppendLine($"     worst difference          {oneShotVsRef.Worst,8} /255");
        sb.AppendLine($"     pixels differing at all   {oneShotVsRef.Differing,8} of {oneShotVsRef.Total}");
        sb.AppendLine();

        // ---- how far apart are they -------------------------------------------
        long differing = 0, refInk = 0, protoInk = 0, total = 0;
        double sum = 0;
        var worst = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var a = reference.GetPixel(x, y).Alpha;
                var b = mark.GetPixel(x, y).Alpha;
                if (a > 8) refInk++;
                if (b > 8) protoInk++;
                if (a == 0 && b == 0) continue;
                total++;
                var d = Math.Abs(a - b);
                if (d > 0) differing++;
                if (d > worst) worst = d;
                sum += d;
            }
        }

        // Where each mark actually is, because equal ink counts with almost no
        // overlap is a placement fault rather than a coverage one.
        static SKRectI InkBounds(SKBitmap b)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++)
            {
                if (b.GetPixel(x, y).Alpha <= 8) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
            return minX > maxX ? SKRectI.Empty : new SKRectI(minX, minY, maxX, maxY);
        }

        // A slice across the mark, so a disagreement can be read rather than
        // inferred from a mean.
        var rb = InkBounds(reference);
        var cx = rb.Left + rb.Width / 2;
        sb.AppendLine($"   slice down x={cx} (alpha: reference / prototype)");
        var shown = 0;
        for (var y = rb.Top; y < rb.Bottom && shown < 14; y++)
        {
            var a = reference.GetPixel(cx, y).Alpha;
            var b = mark.GetPixel(cx, y).Alpha;
            if (a == 0 && b == 0) continue;
            sb.AppendLine($"     y={y,5}   {a,4} / {b,4}");
            shown++;
        }

        sb.AppendLine($"   reference ink bounds        {InkBounds(reference)}");
        sb.AppendLine($"   prototype ink bounds        {InkBounds(mark)}");
        sb.AppendLine($"   dabs                        {dabs.Count}");
        sb.AppendLine($"   per event, LAST event       {perEvent,8:0.00} ms   (silhouette: 12.46 ms)");
        sb.AppendLine();
        sb.AppendLine("   pixels, against the shipped silhouette");
        sb.AppendLine($"     mean difference           {(total == 0 ? 0 : sum / total),8:0.00} /255");
        sb.AppendLine($"     worst difference          {worst,8} /255");
        sb.AppendLine($"     pixels differing at all   {differing,8} of {total}");
        sb.AppendLine($"     ink pixels                {protoInk,8} vs {refInk} (ratio {(refInk == 0 ? 0 : protoInk / (double)refInk),0:0.000})");
        sb.AppendLine();
        return sb.ToString();
    }
}
