using System.Diagnostics;
using System.Text;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// What a soft brush's live post-process costs, and whether it grows with the
/// mark (B293).
/// </summary>
/// <remarks>
/// <para>
/// <b>The question comes from the artist, after B299 landed.</b> With Ink's
/// live stamp made incremental it now sits under the pen, and the brushes that
/// did not change - Pencil, Soft round, Airbrush, the flats - became visibly the
/// ones that lag. That is exactly the split
/// <see cref="BrushEngine.NeedsFootprintCap"/> draws: it is the near-complement
/// of <see cref="BrushEngine.DrawsAsOneSilhouette"/>, true for an antialiased
/// tipless brush with hardness below 1, so every soft brush runs a live
/// post-process pass and Ink no longer does.
/// </para>
/// <para>
/// <b>What is suspected, and why it is measured rather than assumed.</b>
/// `PostProcessRegion` rebuilds the footprint from the whole stroke on every
/// pass - it walks all the dabs and stamps each one's gradient - and
/// `PostProcessBounds` is the whole stroke's bounds, which B293 already records
/// as the reason bounding the pass spatially culled nothing. If that is the lag
/// then the cost grows with the mark; if it does not grow, the diagnosis is
/// wrong before a line of the fix is written. Ink's own bottleneck was guessed
/// wrongly twice before it was split, which is the argument for doing this
/// first.
/// </para>
/// <para>
/// <b>The footprint half is isolated by removing it, not by instrumenting it.</b>
/// `StampFootprintPass` is private, so the same stroke is run twice: once as it
/// ships, and once with hardness at 1, which makes `NeedsFootprintCap` false and
/// skips the block entirely. Soft round carries no granulation, wet edge,
/// texture or medium, so nothing else in the pass changes with it - the
/// difference is the footprint block and nothing but.
/// </para>
/// </remarks>
public static class FootprintPassCost
{
    private const int Width = 3840;
    private const int Height = 2160;

    /// <summary>Document pixels the owner's hand covers per delivered move.</summary>
    private const double TravelPerEvent = 15.1;

    private static readonly int[] Lengths = [50, 100, 200, 400, 800];

    private static Stroke Arc(BrushSettings brush, int points)
    {
        var pts = new List<StrokePoint>(points);
        double x = 200, y = 1080, heading = 0;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 0.6 + i % 3 * 0.2));
            heading += 0.004;
            x += TravelPerEvent * Math.Cos(heading);
            y += TravelPerEvent * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Brush = brush,
            Points = pts,
        };
    }

    private static BrushSettings Copy(BrushSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<BrushSettings>(json)!;
    }

    /// <summary>One post-process pass over a stroke of this length, in ms.</summary>
    private static double Pass(BrushSettings brush, int points, int repeats)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = Arc(brush, points);
        var dabs = BrushEngine.WalkDabs(stroke, new IncrementalDensify());

        // The dab scratch the pass reads, stamped once outside the timer: the
        // live path already holds this, and it is not what is being measured.
        using var scratch = new SKBitmap(info);
        using (var canvas = new SKCanvas(scratch))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
            canvas.Flush();
        }

        if (BrushEngine.PostProcessBounds(stroke, info) is not { } rect) return 0;

        var best = double.MaxValue;
        for (var r = 0; r < repeats; r++)
        {
            var sw = Stopwatch.StartNew();
            using var result = BrushEngine.PostProcessRegion(scratch, stroke, rect, null);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    /// <summary>Least-squares exponent of a log-log fit, as the sweep harness does.</summary>
    private static double Exponent(IReadOnlyList<int> xs, IReadOnlyList<double> ys)
    {
        var pts = xs.Zip(ys).Where(p => p.First > 0 && p.Second > 1e-6).ToList();
        if (pts.Count < 2) return 0;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var (x, y) in pts)
        {
            double lx = Math.Log(x), ly = Math.Log(y);
            sx += lx;
            sy += ly;
            sxx += lx * lx;
            sxy += lx * ly;
        }

        var n = pts.Count;
        var denom = n * sxx - sx * sx;
        return Math.Abs(denom) < 1e-9 ? 0 : (n * sxy - sx * sy) / denom;
    }

    public static string Report(int repeats = 3)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- a soft brush's live post-process, by stroke length (B293) --");
        sb.AppendLine($"   Soft round at {Width}x{Height}, {TravelPerEvent} document px an event.");
        sb.AppendLine("   ONE pass, which the live path runs per pointer event.");
        sb.AppendLine();

        var soft = Copy(BuiltInPresets.Create().First(p => p.Name == "Soft round").Settings);
        var noCap = Copy(soft);
        noCap.Hardness = 1;

        sb.AppendLine($"   footprint cap: soft {BrushEngine.NeedsFootprintCap(soft)}, control {BrushEngine.NeedsFootprintCap(noCap)}");
        sb.AppendLine();
        sb.AppendLine("    points   whole pass   without cap   the CAP    events/s");

        var whole = new double[Lengths.Length];
        var without = new double[Lengths.Length];
        var cap = new double[Lengths.Length];

        for (var i = 0; i < Lengths.Length; i++)
        {
            whole[i] = Pass(soft, Lengths[i], repeats);
            without[i] = Pass(noCap, Lengths[i], repeats);
            cap[i] = Math.Max(0, whole[i] - without[i]);
            var rate = whole[i] > 0.0001 ? 1000.0 / whole[i] : 0;
            sb.AppendLine($"    {Lengths[i],6}{whole[i],13:0.00}{without[i],14:0.00}{cap[i],11:0.00}{rate,12:0}");
        }

        sb.AppendLine(
            $"    n^   {Exponent(Lengths, whole),13:0.00}{Exponent(Lengths, without),14:0.00}{Exponent(Lengths, cap),11:0.00}");
        sb.AppendLine();
        sb.AppendLine("   An exponent near 0 is a pass that does not care how long the stroke");
        sb.AppendLine("   is, and the lag is somewhere else. Near 1 is a pass that reads the");
        sb.AppendLine("   whole mark every event, which is what B293 suspects and what makes");
        sb.AppendLine("   the mark converge behind the pen as the stroke grows.");
        sb.AppendLine();
        sb.AppendLine("   events/s is the ceiling this pass alone puts on the preview. The");
        sb.AppendLine("   owner's pen delivers a move every 8.9 ms, so anything under about");
        sb.AppendLine("   112 a second cannot keep up while the hand is moving.");
        return sb.ToString();
    }
}
