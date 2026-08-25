using System.Text;
using Lightbox.Bench;

// A sweep takes minutes, so it is not in `dotnet test`: the budget tests are
// the per-commit ratchet and this is the periodic map. Two artefacts, two jobs.
//
//   dotnet run --project tools/Lightbox.Bench -c Release
//   dotnet run --project tools/Lightbox.Bench -c Release -- --filter onion
//
// Writes .claude/quality/PERFORMANCE.md.

var filter = Args("--filter");
var outPath = Args("--out") ?? FindReportPath();

Console.WriteLine("Calibrating…");
var calibration = Runner.CalibrationMs();
Console.WriteLine($"  {calibration:F0} ms for the reference loop\n");

// B177: the per-preset table is not a swept curve — the axis is categorical, and
// it reports two numbers per row rather than one. Run before the curves so a
// filtered investigation gets it first, and printed rather than written into
// PERFORMANCE.md, because it is a diagnosis rather than a ratchet.
if (filter is null || "brushes".Contains(filter, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Measuring each shipped brush…\n");
    Console.WriteLine(BrushPresetSweeps.Report(BrushPresetSweeps.Measure()));
    Console.WriteLine(BrushPresetSweeps.Isolate());
}

// B189: which phase of one pointer event grows with the stroke. A table
// rather than a curve, and printed rather than written into PERFORMANCE.md,
// for the reason the brush table above gives — a diagnosis, not a ratchet.
if (filter is not null && "livestamp".Contains(filter, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Splitting one pointer event by phase...");
    Console.WriteLine(LiveStampSplit.Report());
    Console.WriteLine(LiveStampSplit.Presets());
    Console.WriteLine(LiveStampSplit.BuildOrFill());
    Console.WriteLine(MaxCoverageProto.Report());
}

var scenarios = AnimationSweeps.All()
    .Concat(DrawingSweeps.All())
    .Where(s => filter is null || s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                              || s.Dimension.Contains(filter, StringComparison.OrdinalIgnoreCase))
    .ToList();

var curves = new List<Curve>();
foreach (var scenario in scenarios)
{
    Console.Write($"{scenario.Name} by {scenario.Dimension} ");
    var curve = Runner.Sweep(scenario);
    curves.Add(curve);
    Console.WriteLine(
        $"— n^{curve.Exponent:F2}, worst {curve.WorstPressure * 100:F0}% of budget"
        + (curve.Cliff is { } c ? $", cliff at {c}" : ""));
}

// A filtered run holds only the scenarios asked for. Diffing it against a
// full baseline would report every other scenario as GONE, which is how
// somebody investigating one curve gets told nine things broke.
var Partial = filter is not null;
var report = Report(curves, calibration);
// A filter that matches no scenario — a diagnostic run like --filter
// livestamp — must not overwrite the map with an empty one. The tables
// those runs print are their whole output; the map is not theirs to touch.
if (outPath is not null && curves.Count > 0)
{
    File.WriteAllText(outPath, report);

    // The markdown is for a person; this is what scripts/bench.py diffs. Only
    // the machine-independent parts are worth comparing across runs — the
    // exponent, the cliff (a dimension value, not a time), and times divided
    // by the calibration figure. Raw milliseconds from two different machines
    // compare to nothing.
    var json = Path.ChangeExtension(outPath, ".json");
    File.WriteAllText(json, Json(curves, calibration, Partial));
    Console.WriteLine($"\nWrote {outPath}\n      {json}");
}
else
{
    Console.WriteLine(report);
}

return curves.Any(c => c.Cliff is not null) ? 1 : 0;

string? Args(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string Json(List<Curve> curves, double calibration, bool partial)
{
    var sb = new StringBuilder();
    sb.AppendLine("{");
    sb.AppendLine($"  \"calibrationMs\": {calibration:F1},");
    sb.AppendLine($"  \"partial\": {(partial ? "true" : "false")},");
    sb.AppendLine("  \"scenarios\": [");
    for (var i = 0; i < curves.Count; i++)
    {
        var c = curves[i];
        var worst = c.Samples.Count == 0 ? 0 : c.Samples.Max(s => s.P95);
        sb.AppendLine("    {");
        sb.AppendLine($"      \"name\": \"{c.Scenario.Name}\",");
        sb.AppendLine($"      \"dimension\": \"{c.Scenario.Dimension}\",");
        sb.AppendLine($"      \"cadence\": \"{c.Scenario.Cadence}\",");
        sb.AppendLine($"      \"budgetMs\": {Budgets.Ms(c.Scenario.Cadence):F1},");
        sb.AppendLine($"      \"exponent\": {(double.IsNaN(c.Exponent) ? "null" : c.Exponent.ToString("F2"))},");
        sb.AppendLine($"      \"cliff\": {(c.Cliff is { } cl ? cl.ToString() : "null")},");
        sb.AppendLine($"      \"worstP95Calibrated\": {worst / calibration:F3},");
        sb.AppendLine($"      \"maxSwept\": {(c.Scenario.Values.Length == 0 ? 0 : c.Scenario.Values[^1])}");
        sb.Append("    }");
        sb.AppendLine(i == curves.Count - 1 ? "" : ",");
    }
    sb.AppendLine("  ]");
    sb.AppendLine("}");
    return sb.ToString();
}

static string? FindReportPath()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var quality = Path.Combine(dir.FullName, ".claude", "quality");
        if (Directory.Exists(quality)) return Path.Combine(quality, "PERFORMANCE.md");
        dir = dir.Parent;
    }
    return null;
}

static string Report(List<Curve> curves, double calibration)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Performance map");
    sb.AppendLine();
    sb.AppendLine("**Generated — do not edit.** `dotnet run --project tools/Lightbox.Bench -c Release`");
    sb.AppendLine();
    sb.AppendLine("This is the map, not the ratchet. The `Category=Performance` tests are the");
    sb.AppendLine("per-commit guard on paths we already know about; this answers where the");
    sb.AppendLine("cliffs are on paths nobody has walked.");
    sb.AppendLine();
    sb.AppendLine("Three things are reported and each answers a different question:");
    sb.AppendLine();
    sb.AppendLine("- **Exponent** — *how* the cost grows. This is the durable result: a");
    sb.AppendLine("  millisecond is a property of the machine that measured it, a slope is a");
    sb.AppendLine("  property of the code. Linear here is linear on a desktop.");
    sb.AppendLine("- **Cliff** — *when* it stops being usable: the first swept value whose p95");
    sb.AppendLine("  misses the budget. p95 rather than the fastest run, because a path that is");
    sb.AppendLine("  usually fast and occasionally terrible is what an artist feels as a stutter.");
    sb.AppendLine("- **Pressure** — *what* to fix first: p95 over the budget for that cadence.");
    sb.AppendLine("  The budget already encodes how often the operation is paid, so ranking by");
    sb.AppendLine("  pressure is a cost-times-frequency ranking without a second number to argue");
    sb.AppendLine("  about. A 200 ms commit is not the problem a 20 ms pointer event is.");
    sb.AppendLine();
    sb.AppendLine($"Reference loop: **{calibration:F0} ms** on the machine that produced this.");
    sb.AppendLine("Compare two reports by the ratio of that figure, never by raw milliseconds.");
    sb.AppendLine();

    sb.AppendLine("## Ranked by pressure");
    sb.AppendLine();
    sb.AppendLine("| | Operation | Grows with | Budget | Worst p95 | Pressure | Exponent | Cliff |");
    sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |");
    var rank = 1;
    foreach (var c in curves.OrderByDescending(c => c.WorstPressure))
    {
        var worst = c.Samples.MaxBy(s => s.Pressure(c.Scenario.Cadence));
        var flag = c.WorstPressure >= 1 ? "🔴" : c.WorstPressure >= 0.5 ? "🟠" : "🟢";
        sb.AppendLine(
            $"| {flag} {rank++} | {c.Scenario.Name} | {c.Scenario.Dimension} "
            + $"| {Budgets.Ms(c.Scenario.Cadence):F0} ms | {worst?.P95:F1} ms "
            + $"| {c.WorstPressure * 100:F0}% | n^{c.Exponent:F2} "
            + $"| {(c.Cliff is { } cliff ? $"**{cliff}**" : "—")} |");
    }
    sb.AppendLine();
    sb.AppendLine("🔴 over budget · 🟠 over half · 🟢 clear. A cliff is the value it broke at.");
    sb.AppendLine();

    foreach (var c in curves)
    {
        sb.AppendLine($"## {c.Scenario.Name}");
        sb.AppendLine();
        sb.AppendLine(
            $"Grows with **{c.Scenario.Dimension}** · paid **{Cadence(c.Scenario.Cadence)}** "
            + $"· budget **{Budgets.Ms(c.Scenario.Cadence):F0} ms** · **n^{c.Exponent:F2}**");
        if (c.Scenario.Note is { } note)
        {
            sb.AppendLine();
            sb.AppendLine(note);
        }
        sb.AppendLine();

        var gauge = c.Scenario.GaugeUnit;
        sb.AppendLine($"| {c.Scenario.Dimension} | p50 | p95 | max | alloc | {gauge ?? "—"} |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var s in c.Samples)
        {
            var over = s.P95 > Budgets.Ms(c.Scenario.Cadence) ? " ⚠" : "";
            sb.AppendLine(
                $"| {s.X}{over} | {s.P50:F1} | {s.P95:F1} | {s.Max:F1} | {s.AllocKb:F0} KB "
                + $"| {(gauge is null ? "—" : $"{s.Bytes / 1024.0 / 1024:F0}")} |");
        }
        sb.AppendLine();
    }

    sb.AppendLine("## What to do with a cliff that moved");
    sb.AppendLine();
    sb.AppendLine("Open it in `BUGS.md` like any other defect, with the two measurements as the");
    sb.AppendLine("evidence. A performance report with its own private backlog gets read once;");
    sb.AppendLine("one that files into the same ledger as everything else gets prioritised");
    sb.AppendLine("against real work, which is the only way it changes what gets built.");
    return sb.ToString();
}

static string Cadence(Cadence c) => c switch
{
    Lightbox.Bench.Cadence.WhileDrawing => "every pointer event",
    Lightbox.Bench.Cadence.WhilePlaying => "every frame of playback",
    Lightbox.Bench.Cadence.PerAction => "once per stroke or edit",
    _ => "once per session",
};
