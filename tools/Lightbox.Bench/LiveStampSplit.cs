using System.Diagnostics;
using System.Text;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// What one pointer event costs the UI thread mid-stroke, split by phase and
/// swept against how long the stroke already is (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a table rather than a swept curve.</b> The sweep harness times one
/// operation and fits one exponent; the question here is which of four phases
/// inside one operation grows, and that needs four numbers per row. Same shape
/// as <see cref="BrushPresetSweeps.Isolate"/>, and printed rather than written
/// into <c>PERFORMANCE.md</c> for the same reason: it is a diagnosis, not a
/// ratchet.
/// </para>
/// <para>
/// <b>The measurement that prompted it.</b> Three render reports from the
/// owner's machine, 2026-08-25, on a 3840x2160 document: <c>stamping the dabs</c>
/// mean <b>7.36 ms</b> over short strokes and <b>13.42 ms</b> over strokes of
/// two to ten seconds, against pen events arriving every <b>8.9 ms</b>
/// (1698 moves in 15.2 s). An event that costs more than the gap before the
/// next one cannot be caught up on, which is the artist's "the stroke only
/// appears when I lift the pen". What no report could say is <em>which part</em>
/// of the stamp grows.
/// </para>
/// <para>
/// <b>This mirrors <c>MainViewModel.StampLiveDabs</c> rather than calling it</b>,
/// because the live session state it works on is internal to the app and
/// visible only to its own test assembly. The four phases below are that
/// method's four steps in its order, on the same engine entry points, holding
/// the same state across events. <b>It has to be kept in step by hand</b> - a
/// divergence here would measure something the artist does not pay for.
/// </para>
/// <para>
/// <b>And it drifted within a day of that sentence being written, which is the
/// argument for the split below.</b> When B311 replaced the silhouette's path
/// union with max accumulation, this file went on mirroring the old flow and
/// reported Ink at 65.07 ms an event - a cost the code had stopped paying. A
/// hand-copy of a hot path is a second implementation, and a second
/// implementation is a thing that disagrees.
/// </para>
/// <para>
/// <b>So the silhouette route is no longer mirrored at all.</b> It now has real
/// entry points - <c>BrushEngine.AccumulateCoverage</c> and
/// <c>CoverageToInk</c> - so this calls them, and the only thing left in the
/// harness is the bookkeeping being measured: which dabs are new, what the tail
/// borrowed, which band is read back. The per-dab route keeps its four-phase
/// mirror because that route did not change, and its phases are exactly what
/// the question is about.
/// </para>
/// <para>
/// <b>Silhouette brushes are a separate row on purpose, and that row is the
/// answer.</b> <see cref="BrushEngine.DrawsAsOneSilhouette"/> pins the settled
/// cut at zero, so for those the "settled" phase never advances and the tail is
/// the whole mark every event. That is a different growth curve out of the same
/// code, and averaging the two would hide both.
/// </para>
/// <para>
/// <b>What it found, and what it refuted.</b> The hypothesis this was written to
/// test was that the whole-stroke dab walk dominates - BR1's rule, the one the
/// code calls non-negotiable. <b>It does not.</b> The walk is linear in stroke
/// length as predicted (exponent <b>0.94</b> for a silhouette brush) and costs
/// <b>0.49-1.16 ms</b> at 400 events; every non-silhouette brush finishes a whole
/// event in <b>0.20-0.66 ms</b>. Caching the walk would have bought almost
/// nothing.
/// </para>
/// <para>
/// <b>The cost is the silhouette redraw, and only one shipped brush pays it.</b>
/// At 4K, 400 events in, <c>Ink</c> costs <b>18.64 ms</b> an event against
/// 0.20-0.66 ms for everything else - <b>17.48 ms of it in the draw</b>, not the
/// walk - and 5.84 ms at 100 events, so it grows with the mark. Ink is
/// <c>Hardness 1, Flow 1, no tip</c>, which is exactly the silhouette predicate.
/// The owner's machine reported a mean of 13.42 ms an event over strokes of two
/// to ten seconds; a session of mixed lengths on Ink lands there.
/// </para>
/// <para>
/// <b>Why it is pinned, so the fix is not mistaken for a one-line change.</b>
/// <c>StampLiveDabs</c> explains it: a silhouette's coverage is computed in a
/// single pass, so drawing a settled prefix would bake the still-provisional
/// tail's shape into the pixels that exist to be taken back - measured there as
/// the live mark 2.8% fatter than the commit. The pin buys live-equals-commit.
/// Making a silhouette's prefix separable without baking the tail is the real
/// problem, and it is a harder one than a cache.
/// </para>
/// </remarks>
public static class LiveStampSplit
{
    /// <summary>How long the stroke already is when the event arrives.</summary>
    private static readonly int[] Lengths = [50, 100, 200, 400, 800, 1600];

    /// <summary>The owner's document, because the costs below are area-sensitive.</summary>
    private const int Width = 3840;
    private const int Height = 2160;

    /// <summary>
    /// The live session's state, held across pointer events exactly as
    /// <c>LivePaintSession</c> holds it.
    /// </summary>
    private sealed class Live : IDisposable
    {
        public readonly SKBitmap Scratch =
            new(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));

        public readonly SKCanvas Canvas;

        /// <summary>The silhouette route's persistent coverage, as LivePaintSession holds it.</summary>
        public readonly SKBitmap Coverage =
            new(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));

        public readonly SKCanvas CoverageCanvas;
        public SKBitmap? TailBackup;
        public SKRectI? TailRegion;
        public List<BrushEngine.Dab>? Dabs;
        public int StableDabs;
        public readonly IncrementalDensify Densify = new();

        public Live()
        {
            Canvas = new SKCanvas(Scratch);
            CoverageCanvas = new SKCanvas(Coverage);
            CoverageCanvas.Clear(SKColors.Black);
        }

        public void Dispose()
        {
            Canvas.Dispose();
            Scratch.Dispose();
            CoverageCanvas.Dispose();
            Coverage.Dispose();
            TailBackup?.Dispose();
        }
    }

    /// <summary>
    /// A stroke whose consecutive points are <paramref name="travel"/> document
    /// pixels apart, using a preset's own settings.
    /// </summary>
    /// <remarks>
    /// <b>The spacing is the owner's hand, not a guess.</b> Their capture of
    /// 2026-08-25 puts the pen 5.66 view pixels further along per delivered
    /// move (228 in-contact moves over 1.41 s). On a 3840x2160 document viewed
    /// fit-to-window the compose scale is 0.375, so one view pixel is 2.67
    /// document pixels and the hand covers about 15 document pixels an event.
    /// Dab pitch is size x spacing in document pixels, so that figure decides
    /// how many dabs a preset places per event - which is the whole question.
    /// </remarks>
    private static Stroke MarkWith(BrushSettings brush, int points, double travel)
    {
        // A shallow arc, so Densify's arc walk runs and the tail region is not
        // the degenerate one a straight line gives.
        var pts = new List<StrokePoint>(points);
        double x = 200, y = 1080;
        var heading = 0.0;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 0.4 + i % 3 * 0.3));
            heading += 0.004;
            x += travel * Math.Cos(heading);
            y += travel * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Brush = brush,
            Points = pts,
        };
    }

    private static Stroke Mark(int points, bool silhouette)
    {
        // An arc, not a line: a straight stroke understates the tail region and
        // Densify's arc walk never runs (charter O8).
        var brush = new BrushSettings
        {
            Size = 40,
            Hardness = silhouette ? 1.0 : 0.6,
            Opacity = 1,
            Flow = silhouette ? 1.0 : 0.9,
            Spacing = 0.08,
            PressureFlowGamma = 1,
            AntiAlias = true,
        };
        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Brush = brush,
            Points = Enumerable.Range(0, points)
                .Select(i => new StrokePoint(
                    200 + i * 1.6,
                    800 + Math.Sin(i * 0.02) * 400,
                    0.4 + i % 3 * 0.3))
                .ToList(),
        };
    }

    /// <summary>
    /// <c>StampLiveDabs</c>'s four steps, each timed, for one pointer event
    /// arriving on a stroke that is already this long.
    /// </summary>
    private static (double Walk, double Restore, double Settled, double Tail) OneEvent(
        Live live, Stroke sofar, SKImageInfo info)
    {
        var sw = Stopwatch.StartNew();

        // 0. The whole-stroke walk. BR1: it cannot start from the middle,
        //    because a dab's position decides every dynamic seeded from it.
        var dabs = BrushEngine.WalkDabs(sofar, live.Densify);
        var walk = sw.Elapsed.TotalMilliseconds;

        var wholeMark = BrushEngine.DrawsAsOneSilhouette(sofar.Brush);
        var stable = BrushEngine.StableCount(dabs, live.Dabs);
        live.Dabs = dabs;

        if (wholeMark)
        {
            // The engine's own entry points, not a copy of them. Everything
            // below this branch is the per-dab route, which is unchanged.
            sw.Restart();
            if (live.TailRegion is { } handedBack && live.TailBackup is not null)
            {
                using var restore = SKImage.FromBitmap(live.TailBackup);
                using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                live.CoverageCanvas.DrawImage(
                    restore,
                    new SKRect(0, 0, handedBack.Width, handedBack.Height),
                    new SKRect(handedBack.Left, handedBack.Top, handedBack.Right, handedBack.Bottom),
                    src);
                live.CoverageCanvas.Flush();
            }

            var restoredMs = sw.Elapsed.TotalMilliseconds;

            var cut = Math.Max(live.StableDabs, Math.Min(stable, dabs.Count));
            sw.Restart();
            BrushEngine.AccumulateCoverage(live.CoverageCanvas, sofar.Brush, dabs, live.StableDabs, cut);
            live.CoverageCanvas.Flush();
            var settledMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var lend = BrushEngine.RangeBounds(dabs, cut, sofar.Brush, info);
            if (lend is { } lending)
            {
                if (live.TailBackup is null
                    || live.TailBackup.Width < lending.Width
                    || live.TailBackup.Height < lending.Height)
                {
                    live.TailBackup?.Dispose();
                    live.TailBackup = new SKBitmap(new SKImageInfo(
                        Math.Max(lending.Width, 64), Math.Max(lending.Height, 64),
                        SKColorType.Rgba8888, SKAlphaType.Premul));
                }

                using (var region = new SKBitmap())
                {
                    if (live.Coverage.ExtractSubset(region, lending))
                    {
                        using var px = region.PeekPixels();
                        using var view = px is null ? null : SKImage.FromPixels(px);
                        if (view is not null)
                        {
                            using var into = new SKCanvas(live.TailBackup);
                            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                            into.DrawImage(view, 0, 0, src);
                            into.Flush();
                        }
                    }
                }

                BrushEngine.AccumulateCoverage(live.CoverageCanvas, sofar.Brush, dabs, cut, dabs.Count);
                live.CoverageCanvas.Flush();
            }

            var changed = BrushEngine.RangeBounds(dabs, live.StableDabs, sofar.Brush, info);
            if (live.TailRegion is { } before)
            {
                changed = changed is { } g ? SKRectI.Union(g, before) : before;
            }

            if (lend is { } now)
            {
                changed = changed is { } g ? SKRectI.Union(g, now) : now;
            }

            if (changed is { } band) BrushEngine.CoverageToInk(live.Canvas, live.Coverage, sofar, band);
            live.Canvas.Flush();
            live.StableDabs = cut;
            live.TailRegion = lend;
            return (walk, restoredMs, settledMs, sw.Elapsed.TotalMilliseconds);
        }

        // 1. Take back the tail lent out last time.
        sw.Restart();
        if (live.TailRegion is { } lent && live.TailBackup is not null)
        {
            using var restore = SKImage.FromBitmap(live.TailBackup);
            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
            live.Canvas.DrawImage(
                restore,
                new SKRect(0, 0, lent.Width, lent.Height),
                new SKRect(lent.Left, lent.Top, lent.Right, lent.Bottom),
                src);
            live.Canvas.Flush();
            live.TailRegion = null;
        }
        var restoreMs = sw.Elapsed.TotalMilliseconds;

        // 2. Everything whose position has stopped moving, permanently.
        sw.Restart();
        BrushEngine.StampDabRange(live.Canvas, sofar, dabs, live.StableDabs, stable);
        live.StableDabs = Math.Max(live.StableDabs, Math.Min(stable, dabs.Count));
        live.Canvas.Flush();
        var settled = sw.Elapsed.TotalMilliseconds;

        // 3. The rest on loan, so the mark reaches the pen tip.
        sw.Restart();
        if (BrushEngine.RangeBounds(dabs, live.StableDabs, sofar.Brush, info) is { } tail)
        {
            live.Canvas.Flush();
            if (live.TailBackup is null
                || live.TailBackup.Width < tail.Width || live.TailBackup.Height < tail.Height)
            {
                live.TailBackup?.Dispose();
                live.TailBackup = new SKBitmap(new SKImageInfo(
                    Math.Max(tail.Width, 64), Math.Max(tail.Height, 64),
                    SKColorType.Rgba8888, SKAlphaType.Premul));
            }
            using (var region = new SKBitmap())
            {
                if (live.Scratch.ExtractSubset(region, tail))
                {
                    using var pixels = region.PeekPixels();
                    using var view = pixels is null ? null : SKImage.FromPixels(pixels);
                    if (view is not null)
                    {
                        using var into = new SKCanvas(live.TailBackup);
                        using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                        into.DrawImage(view, 0, 0, src);
                        into.Flush();
                        live.TailRegion = tail;
                    }
                }
            }
            BrushEngine.StampDabRange(live.Canvas, sofar, dabs, live.StableDabs, dabs.Count);
            live.Canvas.Flush();
        }
        var tailMs = sw.Elapsed.TotalMilliseconds;

        return (walk, restoreMs, settled, tailMs);
    }

    /// <summary>
    /// Walk a stroke from two points up to this many, one pointer event at a
    /// time, and report what the LAST event cost.
    /// </summary>
    /// <remarks>
    /// The last one, not the mean: the complaint is that a long stroke stops
    /// keeping up, so the number that matters is what an event costs once the
    /// stroke is already that long. Growing it event by event is also the only
    /// way the incremental state - the densify prefix, the settled cut, the
    /// tail backup - reaches the shape it would really be in.
    /// </remarks>
    private static (double Walk, double Restore, double Settled, double Tail) AtLength(
        int points, bool silhouette, int repeats)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var best = (Walk: 0.0, Restore: 0.0, Settled: 0.0, Tail: 0.0);
        var bestTotal = double.MaxValue;

        for (var r = 0; r < repeats; r++)
        {
            using var live = new Live();
            var full = Mark(points, silhouette);
            (double Walk, double Restore, double Settled, double Tail) last = default;

            for (var n = 2; n <= points; n++)
            {
                var sofar = new Stroke
                {
                    Tool = full.Tool,
                    Color = full.Color,
                    Brush = full.Brush,
                    Points = full.Points.Take(n).ToList(),
                };
                last = OneEvent(live, sofar, info);
            }

            // The cheapest of the repeats, the standard defence against another
            // process stealing the machine mid-measurement.
            var total = last.Walk + last.Restore + last.Settled + last.Tail;
            if (total < bestTotal)
            {
                bestTotal = total;
                best = last;
            }
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

    /// <summary>
    /// Every shipped brush, in the cell the owner actually draws in: a
    /// 3840x2160 document, fit to the window, at their measured hand speed.
    /// </summary>
    /// <remarks>
    /// <b>The cell neither existing measurement covered.</b> B177's per-preset
    /// table is a 40-event stroke on 1920x1080; the split above is 4K but one
    /// synthetic brush. The reported 13.42 ms an event is at 4K, with a real
    /// preset, hundreds of events into a stroke - which is this table.
    /// </remarks>
    public static string Presets(int shortRun = 100, int longRun = 400)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var sb = new StringBuilder();
        sb.AppendLine("-- one pointer event, by shipped brush, at 4K (B189) ----------");
        sb.AppendLine($"   {Width}x{Height}, fit to window, 15.1 document px of travel per");
        sb.AppendLine($"   event (the owner's measured hand). Budget: {Budgets.Ms(Cadence.WhileDrawing):0} ms.");
        sb.AppendLine();
        sb.AppendLine($"   brush                  dabs   walk@{longRun}  draw@{longRun}   TOTAL@{shortRun}   TOTAL@{longRun}");

        foreach (var preset in BuiltInPresets.Create())
        {
            var results = new (double Walk, double Restore, double Settled, double Tail)[2];
            var dabs = 0;

            for (var k = 0; k < 2; k++)
            {
                var points = k == 0 ? shortRun : longRun;
                using var live = new Live();
                var full = MarkWith(preset.Settings, points, TravelPerEvent);
                (double Walk, double Restore, double Settled, double Tail) last = default;
                for (var n = 2; n <= points; n++)
                {
                    var sofar = new Stroke
                    {
                        Tool = full.Tool,
                        Color = full.Color,
                        Brush = full.Brush,
                        Points = full.Points.Take(n).ToList(),
                    };
                    last = OneEvent(live, sofar, info);
                }

                results[k] = last;
                if (k == 1) dabs = live.Dabs?.Count ?? 0;
            }

            var shortTotal = results[0].Walk + results[0].Restore + results[0].Settled + results[0].Tail;
            var longTotal = results[1].Walk + results[1].Restore + results[1].Settled + results[1].Tail;
            var draw = results[1].Restore + results[1].Settled + results[1].Tail;
            sb.AppendLine(
                $"   {preset.Name,-20}{dabs,7}{results[1].Walk,10:0.00}{draw,9:0.00}"
                + $"{shortTotal,11:0.00}{longTotal,11:0.00}");
        }

        sb.AppendLine();
        sb.AppendLine("   dabs is the whole stroke's dab count at the long length. TOTAL is");
        sb.AppendLine("   what ONE event costs once the stroke is that long - compare it with");
        sb.AppendLine("   the 8.9 ms gap between the owner's pen events, not with the budget:");
        sb.AppendLine("   an event costing more than that gap can never be caught up on.");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Document pixels the owner's hand covers per delivered move. See MarkWith.</summary>
    private const double TravelPerEvent = 15.1;

    /// <summary>
    /// Is a silhouette event's cost in BUILDING the path or in FILLING it?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fork that decides B299's fix, and it needs no engine change to
    /// answer.</b> `StampSilhouette` does two things per event: rebuild an
    /// SKPath holding a contour per kept dab, and fill it clipped to a band.
    /// Everything proposed so far shrinks the BAND. If the cost is the build,
    /// none of it helps and the answer is to cache the path; if the cost is the
    /// fill, the band is the whole game.
    /// </para>
    /// <para>
    /// Measured by clipping the canvas to a pinhole before the call. Skia
    /// intersects clips, so the engine's own band becomes the pinhole and the
    /// fill collapses to nothing while the build is untouched. Whatever time
    /// survives the pinhole is the build.
    /// </para>
    /// </remarks>
    public static string BuildOrFill(int points = 400, int repeats = 5)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var sb = new StringBuilder();
        sb.AppendLine("-- is it the build or the fill? (B299) ------------------------");
        sb.AppendLine($"   Ink at {points} events, {Width}x{Height}, one StampDabRange call.");
        sb.AppendLine();

        var ink = BuiltInPresets.Create().First(p => p.Name == "Ink");
        var full = MarkWith(ink.Settings, points, TravelPerEvent);

        using var live = new Live();
        var dabs = BrushEngine.WalkDabs(full, live.Densify);

        double whole = double.MaxValue, pinhole = double.MaxValue;
        for (var r = 0; r < repeats; r++)
        {
            var sw = Stopwatch.StartNew();
            BrushEngine.StampDabRange(live.Canvas, full, dabs, 0, dabs.Count);
            live.Canvas.Flush();
            whole = Math.Min(whole, sw.Elapsed.TotalMilliseconds);

            // The same call, with nowhere to put pixels.
            sw.Restart();
            live.Canvas.Save();
            live.Canvas.ClipRect(SKRect.Create(0, 0, 1, 1), SKClipOperation.Intersect, antialias: false);
            BrushEngine.StampDabRange(live.Canvas, full, dabs, 0, dabs.Count);
            live.Canvas.Flush();
            live.Canvas.Restore();
            pinhole = Math.Min(pinhole, sw.Elapsed.TotalMilliseconds);
        }

        sb.AppendLine($"   dabs in the mark            {dabs.Count}");
        sb.AppendLine($"   whole band (what ships)     {whole,8:0.00} ms");
        sb.AppendLine($"   clipped to one pixel        {pinhole,8:0.00} ms   <- the BUILD");
        sb.AppendLine($"   difference                  {whole - pinhole,8:0.00} ms   <- the FILL");
        sb.AppendLine();
        sb.AppendLine("   If the build is most of it, shrinking the band cannot help and the");
        sb.AppendLine("   path wants caching. If the fill is most of it, the band is the game.");
        sb.AppendLine();
        return sb.ToString();
    }

    public static string Report(int repeats = 3)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- what one pointer event costs mid-stroke (B189) -------------");
        sb.AppendLine($"   {Width}x{Height} document, size-40 brush. The cost of the LAST");
        sb.AppendLine($"   event at each length. Budget: {Budgets.Ms(Cadence.WhileDrawing):0} ms an event.");
        sb.AppendLine();

        foreach (var silhouette in new[] { false, true })
        {
            var kind = silhouette
                ? "silhouette (hard, round, anti-aliased)"
                : "ordinary (soft edge)";
            sb.AppendLine($"  {kind}");
            sb.AppendLine("    points     walk   restore   settled      tail     TOTAL");

            var walk = new double[Lengths.Length];
            var restore = new double[Lengths.Length];
            var settled = new double[Lengths.Length];
            var tail = new double[Lengths.Length];
            var total = new double[Lengths.Length];

            for (var i = 0; i < Lengths.Length; i++)
            {
                var r = AtLength(Lengths[i], silhouette, repeats);
                walk[i] = r.Walk;
                restore[i] = r.Restore;
                settled[i] = r.Settled;
                tail[i] = r.Tail;
                total[i] = r.Walk + r.Restore + r.Settled + r.Tail;
                sb.AppendLine(
                    $"    {Lengths[i],6}{walk[i],9:0.00}{restore[i],10:0.00}"
                    + $"{settled[i],10:0.00}{tail[i],10:0.00}{total[i],10:0.00}");
            }

            sb.AppendLine(
                $"    n^     {Exponent(Lengths, walk),9:0.00}{Exponent(Lengths, restore),10:0.00}"
                + $"{Exponent(Lengths, settled),10:0.00}{Exponent(Lengths, tail),10:0.00}"
                + $"{Exponent(Lengths, total),10:0.00}");
            sb.AppendLine();
        }

        sb.AppendLine("  An exponent near 0 is a phase that does not care how long the stroke");
        sb.AppendLine("  is; near 1 is one that reads the whole stroke every event, which makes");
        sb.AppendLine("  the stroke as a whole quadratic. That is the number to fix.");
        return sb.ToString();
    }
}
