using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// What each shipped brush costs, per pointer event and at commit, separately.
/// </summary>
/// <remarks>
/// <para>
/// <b>B177: an artist reported that Ink is responsive while Pencil and the flat
/// brushes lag, and nothing in this repository could say whether that was
/// true.</b> Every existing sweep varies geometry — canvas area, brush diameter,
/// stroke length — with one brush held fixed. So the axis an artist actually
/// changes, <em>which brush they picked</em>, was the one axis never measured.
/// </para>
/// <para>
/// <b>Two numbers per preset rather than one, because the two lag in different
/// ways and the fix is different for each.</b> Drag while drawing is per-event
/// cost; a pause when the pen lifts is commit cost. A single whole-stroke figure
/// adds them together and hides which one an artist is feeling — and the live
/// path defers wet edge and granulation to the commit, so the two genuinely do
/// not contain the same work.
/// </para>
/// <para>
/// <b>Cost per dab is reported beside cost per stroke, and that is the column
/// that settles B177's confound.</b> Pencil is a 3 px brush at 0.12 spacing, so
/// it places a dab every 0.36 px; Ink is 5 px at 0.10, a dab every 0.50 px. That
/// is <b>39% more dabs per unit of stroke</b> before any brush setting is
/// considered. Attributing that to granulation would be the same mistake this
/// project has made before — the number real, the attribution wrong — so the
/// division is done here rather than in an argument.
/// </para>
/// <para>
/// <b>Presets are read from <see cref="BuiltInPresets"/> rather than rebuilt.</b>
/// A copy of the settings here would drift from the ones an artist actually gets
/// on the first edit, and then this would measure a brush nobody owns.
/// </para>
/// </remarks>
public static class BrushPresetSweeps
{
    /// <summary>Where the strokes are drawn. One canvas per preset, as the app holds one.</summary>
    private const int CanvasW = 1920;
    private const int CanvasH = 1080;

    /// <summary>
    /// How many pointer events a measured stroke is made of, and how far apart.
    /// </summary>
    /// <remarks>
    /// Chosen to be an ordinary mark rather than a stress test: this is asking
    /// "does the brush an artist reaches for feel slow", so an unrealistic stroke
    /// would answer a question nobody asked.
    /// </remarks>
    private const int Events = 40;
    private const int Span = 18;

    public sealed record PresetCost(
        string Name,
        int Dabs,
        double LiveMsPerEvent,
        double CommitMs,
        double UsPerDab,
        bool Simulated);

    /// <summary>Measure every shipped preset. Release build only — a Debug number is noise.</summary>
    public static List<PresetCost> Measure(int iterations = 7, int warmup = 2)
    {
        var results = new List<PresetCost>();
        foreach (var preset in BuiltInPresets.Create())
        {
            results.Add(MeasureOne(preset.Name, preset.Settings, iterations, warmup));
        }
        return results;
    }

    private static PresetCost MeasureOne(
        string name, BrushSettings settings, int iterations, int warmup)
    {
        var stroke = Mark(settings);
        var dabs = BrushEngine.WalkDabs(stroke).Count;
        var info = new SKImageInfo(CanvasW, CanvasH, SKColorType.Rgba8888, SKAlphaType.Premul);

        var live = Median(iterations, warmup, () => LiveStroke(stroke, info)) / Events;
        var commit = Median(iterations, warmup, () => CommitStroke(stroke, info));

        return new PresetCost(
            name,
            dabs,
            live,
            commit,
            // Microseconds: a dab is tens of nanoseconds to a few microseconds,
            // and milliseconds-per-dab would print as a column of zeroes.
            dabs == 0 ? 0 : (live * Events + commit) * 1000.0 / dabs,
            settings.Medium.Kind != MediumKind.None);
    }

    /// <summary>
    /// One stroke drawn the way the app draws it: dabs into a stroke-local
    /// scratch, composed over the layer once per event, bounded to the new
    /// segment.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>StampStroke</c> in a loop.</b> That would commit forty times and
    /// measure something no artist ever waits for. The live path is the one that
    /// runs per pointer event, and it is the one that decides whether a line
    /// trails behind the pen.
    /// </remarks>
    private static double LiveStroke(Stroke stroke, SKImageInfo info)
    {
        using var layer = new SKBitmap(info);
        using var scratch = new SKBitmap(info);
        using var scratchCanvas = new SKCanvas(scratch);
        using var composite = new SKBitmap(info);
        using var compositeCanvas = new SKCanvas(composite);

        var all = BrushEngine.WalkDabs(stroke);
        var perEvent = Math.Max(1, all.Count / Events);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var e = 0; e < Events; e++)
        {
            var from = e * perEvent;
            var to = Math.Min(all.Count, from + perEvent);
            if (from >= all.Count) break;

            BrushEngine.StampDabRange(scratchCanvas, stroke, all, from, to);
            scratchCanvas.Flush();

            // **The tail, not the whole stroke.** The app builds a stroke of the
            // last few points and bounds the compose to that
            // (`MainViewModel:6487`); passing the whole stroke here made the
            // compose region grow with every event, and the first run of this
            // sweep read ~5 ms/event for every brush including Ink — a flat
            // floor that was the harness rather than the engine. Measuring the
            // measurement before believing it, which is the rule this project
            // keeps having to relearn.
            if (Tail(stroke, e) is { } tail
                && BrushEngine.DraftSegmentBounds(tail, info) is { } rect)
            {
                BrushEngine.ComposeDraftRegion(compositeCanvas, layer, scratch, rect, stroke);
                compositeCanvas.Flush();
            }
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The last two points of the stroke as it stood at event <paramref name="e"/> —
    /// the shape the app composes, so the region is a segment rather than a
    /// growing stroke.
    /// </summary>
    private static Stroke? Tail(Stroke stroke, int e)
    {
        var upto = Math.Min(stroke.Points.Count, e + 1);
        if (upto < 2) return null;
        return new Stroke
        {
            Tool = stroke.Tool,
            Color = stroke.Color,
            Brush = stroke.Brush,
            Points = stroke.Points.Skip(upto - 2).Take(2).ToList(),
        };
    }

    private static double CommitStroke(Stroke stroke, SKImageInfo info)
    {
        using var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The same mark for every preset, so the only thing that varies is the
    /// brush. A bend rather than a straight line — charter O8, and a curve is
    /// what exercises the arc walk.
    /// </summary>
    private static Stroke Mark(BrushSettings settings) =>
        new()
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Points = Enumerable.Range(0, Events)
                .Select(i => new StrokePoint(
                    120 + i * Span,
                    400 + Math.Sin(i * 0.4) * 90,
                    0.4 + i % 3 * 0.3))
                .ToList(),
            Brush = settings,
        };

    /// <summary>
    /// Median rather than mean, and warmed first: a first run pays JIT and the
    /// first native allocation, and a mean lets one outlier write the answer.
    /// </summary>
    private static double Median(int iterations, int warmup, Func<double> run)
    {
        for (var i = 0; i < warmup; i++) run();

        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++) samples[i] = run();
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    /// <summary>
    /// What one setting costs, by turning it off and measuring again.
    /// </summary>
    /// <remarks>
    /// <b>The table above says Pencil costs more than Ink at an identical dab
    /// count; it cannot say which setting did it.</b> Naming granulation from the
    /// settings list alone would be attribution by elimination, which is how a
    /// real number gets pinned on the wrong cause — twice already in this
    /// project. So each suspect is switched off and the stroke measured again.
    /// </remarks>
    public static string Isolate(int iterations = 7, int warmup = 2)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- what one setting costs, measured by removing it ------------");
        sb.AppendLine("preset / change            commit ms    delta");

        foreach (var preset in BuiltInPresets.Create())
        {
            var s = preset.Settings;
            var baseline = MeasureOne(preset.Name, s, iterations, warmup).CommitMs;
            var variants = new List<(string What, BrushSettings Settings)>();

            if (s.Granulation > 0)
                variants.Add(("no granulation", With(s, granulation: 0)));
            if (s.WetEdge > 0)
                variants.Add(("no wet edge", With(s, wetEdge: 0)));
            if (s.Granulation > 0 && s.WetEdge > 0)
                variants.Add(("neither", With(s, granulation: 0, wetEdge: 0)));

            if (variants.Count == 0) continue;

            sb.AppendLine($"{preset.Name,-26}{baseline,9:0.0}        —");
            foreach (var (what, settings) in variants)
            {
                var ms = MeasureOne(preset.Name, settings, iterations, warmup).CommitMs;
                sb.AppendLine($"  {what,-24}{ms,9:0.0}{baseline - ms,9:+0.0;-0.0}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// A copy with one or two settings changed. Serialize-and-edit rather than a
    /// hand-written clone: a copy constructor listing forty fields is a list
    /// somebody forgets to extend, and the failure is silent.
    /// </summary>
    private static BrushSettings With(
        BrushSettings source, double? granulation = null, double? wetEdge = null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var copy = System.Text.Json.JsonSerializer.Deserialize<BrushSettings>(json)!;
        if (granulation is { } g) copy.Granulation = g;
        if (wetEdge is { } w) copy.WetEdge = w;
        return copy;
    }

    /// <summary>
    /// The table, with the comparison spelled out rather than left as arithmetic.
    /// </summary>
    public static string Report(IReadOnlyList<PresetCost> costs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- what each shipped brush costs (B177) ----------------------");
        sb.AppendLine($"a {Events}-event stroke on {CanvasW}x{CanvasH}, median of the runs");
        sb.AppendLine();
        sb.AppendLine("brush                 dabs    live ms/event   commit ms    us/dab");

        foreach (var c in costs)
        {
            var mark = c.Simulated ? " *" : "  ";
            sb.AppendLine(
                $"{c.Name,-20}{mark}{c.Dabs,6}{c.LiveMsPerEvent,16:0.000}{c.CommitMs,12:0.0}{c.UsPerDab,10:0.00}");
        }

        sb.AppendLine();
        sb.AppendLine("  * simulates a medium — badged Expressive, and priced as such.");

        // The comparison B177 is actually about, printed rather than left to the
        // reader: an absent conclusion reads as "nothing was found".
        var ink = costs.FirstOrDefault(c => c.Name == "Ink");
        var pencil = costs.FirstOrDefault(c => c.Name == "Pencil");
        if (ink is not null && pencil is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"  >> Pencil places {pencil.Dabs} dabs to Ink's {ink.Dabs} "
                          + $"({(double)pencil.Dabs / Math.Max(1, ink.Dabs):0.00}x).");
            sb.AppendLine($"     Live: {pencil.LiveMsPerEvent:0.000} vs {ink.LiveMsPerEvent:0.000} ms/event.");
            sb.AppendLine($"     Commit: {pencil.CommitMs:0.0} vs {ink.CommitMs:0.0} ms.");
            sb.AppendLine($"     Per dab: {pencil.UsPerDab:0.00} vs {ink.UsPerDab:0.00} us.");
            sb.AppendLine("     If per-dab is close and the totals are not, the cost is dab COUNT");
            sb.AppendLine("     and the brush settings are innocent. If per-dab differs, they are not.");
        }

        return sb.ToString();
    }
}
