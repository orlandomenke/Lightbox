using System.Diagnostics;
using System.Text;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// What the live post-process costs per pointer event for the brushes that
/// still take it, whole-mark against band-local (B313).
/// </summary>
/// <remarks>
/// <para>
/// <b>B293 fixed Soft round and Airbrush by noticing their pass had nothing in
/// it but the ceiling, and left everything with an effect in it alone.</b> That
/// is Pencil, the three flats and the media. Its entry recorded the reason as a
/// pixel decision — granulation was believed to seed its field from the rect
/// corner — and <c>LivePassIsBandInvariantTests</c> has since measured that to
/// be false. So the question this asks is the one that was skipped: what does
/// the whole-mark rect actually cost these brushes, and what is left once the
/// pass only redoes the band that moved.
/// </para>
/// <para>
/// <b>The copies are inside the timer and that is deliberate.</b> Half of this
/// pass's price is not the effects at all: the dabs are copied out on the UI
/// thread before the worker starts, and that copy is the size of the rect. A
/// figure that timed only <c>PostProcessRegion</c> would report the cheaper
/// half of the change and understate it.
/// </para>
/// <para>
/// Measured in the cell the owner draws in — 3840x2160, and 15.1 document
/// pixels of travel an event, taken from their own capture rather than chosen.
/// </para>
/// </remarks>
public static class LivePassBandCost
{
    private const int Width = 3840;
    private const int Height = 2160;

    /// <summary>Document pixels the owner's hand covers per delivered move.</summary>
    private const double TravelPerEvent = 15.1;

    private static readonly int[] Lengths = [50, 100, 200, 400, 800];

    /// <summary>
    /// A stroke that stays on the canvas however long it gets.
    /// </summary>
    /// <remarks>
    /// <b>An arc will not do, and the first version of this harness proved
    /// it.</b> At 15.1 document pixels an event, 800 events is 12,000 pixels of
    /// path — three canvases wide. The arc walked straight off the edge, the
    /// newest dabs' band clipped to nothing, and the band column printed a
    /// perfect 0.00 ms at every length past 100. A measurement that reads
    /// <em>zero</em> is not a fast one; it is a broken one, and this repository
    /// has published that mistake before.
    /// </remarks>
    private static Stroke Arc(BrushSettings brush, int points)
    {
        var pts = new List<StrokePoint>(points);
        const double left = 200, right = 3640;
        double x = left, y = 200, dir = 1;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 0.6 + i % 3 * 0.2));
            x += TravelPerEvent * dir;
            if (x > right || x < left)
            {
                x = Math.Clamp(x, left, right);
                dir = -dir;
                // Down a row, wrapping so a very long stroke folds back over
                // itself rather than leaving the canvas.
                y += 60;
                if (y > 1960) y = 200;
            }
        }

        return new Stroke { Tool = ToolKind.Brush, Color = "#204060", Brush = brush, Points = pts };
    }

    private static BrushSettings Copy(BrushSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<BrushSettings>(json)!;
    }

    /// <summary>A real copy of one rect, as the view model hands the worker.</summary>
    private static SKBitmap? CopyRegion(SKBitmap src, SKRectI r, SKAlphaType alpha)
    {
        if (r.Width <= 0 || r.Height <= 0) return null;
        var bmp = new SKBitmap(new SKImageInfo(r.Width, r.Height, SKColorType.Rgba8888, alpha));
        using var sub = new SKBitmap();
        if (!src.ExtractSubset(sub, r)) { bmp.Dispose(); return null; }
        using var px = sub.PeekPixels();
        using var view = px is null ? null : SKImage.FromPixels(px);
        if (view is null) { bmp.Dispose(); return null; }
        using (var canvas = new SKCanvas(bmp))
        using (var replace = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            canvas.DrawImage(view, 0, 0, replace);
            canvas.Flush();
        }

        return bmp;
    }

    /// <summary>
    /// One pointer event's post-process at the end of a stroke this long, in ms.
    /// </summary>
    /// <param name="bandLocal">
    /// Redo only the region the last event moved, plus the skirt the wet edge
    /// needs — against redoing the whole mark, which is what ships today.
    /// </param>
    private static double Event(BrushSettings brush, int points, bool bandLocal, int repeats)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = Arc(brush, points);
        var dabs = BrushEngine.WalkDabs(stroke, new IncrementalDensify());
        var caps = BrushEngine.NeedsFootprintCap(brush);

        // The buffers the live path already holds, filled outside the timer:
        // the dab scratch and the carried footprint are maintained by the
        // stamping, not by this pass.
        using var scratch = new SKBitmap(info);
        using (var canvas = new SKCanvas(scratch))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
            canvas.Flush();
        }

        SKBitmap? footprint = null;
        if (caps)
        {
            footprint = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(footprint);
            canvas.Clear(SKColors.Black);
            BrushEngine.AccumulateFootprint(canvas, stroke, dabs, 0, dabs.Count);
            canvas.Flush();
        }

        if (BrushEngine.PostProcessBounds(stroke, info) is not { } mark)
        {
            footprint?.Dispose();
            return 0;
        }

        var rect = mark;
        if (bandLocal)
        {
            // What the last event moved: the dabs it added, and the tail it took
            // back. One event's worth, which is what the live path accumulates.
            var perEvent = Math.Max(1, dabs.Count / Math.Max(1, points));
            var from = Math.Max(0, dabs.Count - perEvent * 2);
            if (BrushEngine.RangeBounds(dabs, from, brush, info) is not { } moved)
            {
                footprint?.Dispose();
                return 0;
            }

            var halo = BrushEngine.LivePassHalo(brush);
            rect = SKRectI.Intersect(
                new SKRectI(moved.Left - halo, moved.Top - halo, moved.Right + halo, moved.Bottom + halo),
                mark);
        }

        var best = double.MaxValue;
        for (var r = 0; r < repeats; r++)
        {
            var sw = Stopwatch.StartNew();
            using var dabsCrop = CopyRegion(scratch, rect, SKAlphaType.Premul);
            using var printCrop = footprint is null ? null : CopyRegion(footprint, rect, SKAlphaType.Opaque);
            if (dabsCrop is null) break;
            using var result = BrushEngine.PostProcessRegion(
                dabsCrop, stroke, rect, null, rect.Location, default, printCrop);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        footprint?.Dispose();
        return best == double.MaxValue ? 0 : best;
    }

    /// <summary>Least-squares exponent of a log-log fit, as the sweep harness does.</summary>
    private static double Exponent(IReadOnlyList<int> xs, IReadOnlyList<double> ys)
    {
        var n = 0;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            if (ys[i] <= 0) continue;
            var lx = Math.Log(xs[i]);
            var ly = Math.Log(ys[i]);
            sx += lx; sy += ly; sxx += lx * lx; sxy += lx * ly;
            n++;
        }

        if (n < 2) return 0;
        var denom = n * sxx - sx * sx;
        return Math.Abs(denom) < 1e-9 ? 0 : (n * sxy - sx * sy) / denom;
    }

    public static string Report(int repeats = 3)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- the live post-process, whole mark against band (B313) --");
        sb.AppendLine($"   {Width}x{Height}, {TravelPerEvent} document px an event, copies included.");
        sb.AppendLine("   ONE pass, which the live path runs per pointer event.");
        sb.AppendLine();

        var presets = BuiltInPresets.Create().ToList();
        foreach (var name in new[] { "Pencil", "Watercolor (flat)", "Gouache (flat)", "Oil (flat)" })
        {
            if (presets.FirstOrDefault(p => p.Name == name) is not { } preset) continue;
            var brush = Copy(preset.Settings);

            sb.AppendLine(
                $"   {name}  size {brush.Size}, granulation {brush.Granulation:0.00}, "
                + $"wet edge {brush.WetEdge:0.00}, skirt {BrushEngine.LivePassHalo(brush)} px, "
                + $"caps {BrushEngine.NeedsFootprintCap(brush)}");
            sb.AppendLine("    points     whole      band   events/s whole    band");

            var whole = new double[Lengths.Length];
            var band = new double[Lengths.Length];
            for (var i = 0; i < Lengths.Length; i++)
            {
                whole[i] = Event(brush, Lengths[i], bandLocal: false, repeats);
                band[i] = Event(brush, Lengths[i], bandLocal: true, repeats);
                var a = whole[i] > 0.0001 ? 1000.0 / whole[i] : 0;
                var b = band[i] > 0.0001 ? 1000.0 / band[i] : 0;
                // A zero is a band that clipped to nothing, not a free pass —
                // said out loud because the first run of this harness printed a
                // column of them and they read as a result.
                var note = band[i] <= 0.0001 ? "   <- EMPTY BAND, not a measurement" : "";
                sb.AppendLine(
                    $"    {Lengths[i],6}{whole[i],10:0.00}{band[i],10:0.00}{a,14:0}{b,9:0}{note}");
            }

            sb.AppendLine(
                $"    n^   {Exponent(Lengths, whole),10:0.00}{Exponent(Lengths, band),10:0.00}");
            sb.AppendLine();
        }

        sb.AppendLine("   An exponent near 1 is a pass that reads the whole mark every event,");
        sb.AppendLine("   which is what makes the mark converge further behind the pen the");
        sb.AppendLine("   longer the stroke gets. Near 0 is a pass that does not care.");
        sb.AppendLine();
        sb.AppendLine("   events/s is the ceiling this pass alone puts on the preview. The");
        sb.AppendLine("   owner's pen delivers a move every 8.9 ms, so anything under about");
        sb.AppendLine("   112 a second cannot keep up while the hand is moving.");
        return sb.ToString();
    }
}
