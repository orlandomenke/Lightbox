using System.Diagnostics;

namespace Lightbox.Bench;

/// <summary>
/// How often an artist pays for an operation, expressed as the time it has to
/// fit in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The budget encodes the frequency, so ranking by pressure is already a
/// cost-times-frequency ranking.</b> A pointer event gets 16 ms because it
/// happens sixty times a second; an export gets two seconds because it happens
/// once. Multiplying a raw cost by a separate frequency weight would say the
/// same thing twice and give two numbers to argue about.
/// </para>
/// <para>
/// This is why the harness ranks by <c>p95 ÷ budget</c> rather than by
/// milliseconds. A 200 ms commit is not the problem a 20 ms pointer event is,
/// however much bigger the number looks.
/// </para>
/// </remarks>
public enum Cadence
{
    /// <summary>Every pointer event while drawing. A frame at 60 Hz.</summary>
    WhileDrawing,

    /// <summary>Every frame of playback. One period at 12 fps.</summary>
    WhilePlaying,

    /// <summary>Once when the pen lifts, or once per edit. Felt, but not as stutter.</summary>
    PerAction,

    /// <summary>Opening, saving, exporting. Allowed to take a moment.</summary>
    PerSession,
}

public static class Budgets
{
    public static double Ms(Cadence cadence) => cadence switch
    {
        Cadence.WhileDrawing => 16,
        Cadence.WhilePlaying => 1000.0 / 12,
        Cadence.PerAction => 100,
        _ => 2000,
    };
}

/// <summary>One measured point: a scenario at one value of its dimension.</summary>
public sealed record Sample(int X, double P50, double P95, double Max, double AllocKb, long Bytes)
{
    public double Pressure(Cadence cadence) => P95 / Budgets.Ms(cadence);
}

/// <summary>
/// One thing worth sweeping: an operation, the dimension that grows under it,
/// and how often an artist pays for it.
/// </summary>
/// <param name="Name">What is being measured, in the artist's terms.</param>
/// <param name="Dimension">What grows. The x axis of the curve.</param>
/// <param name="Values">The points to sweep. Geometric, so a log-log fit has spread.</param>
/// <param name="Cadence">How often it is paid, which sets the budget.</param>
/// <param name="Setup">Build the workload for one value. Not timed.</param>
/// <param name="Work">The operation itself. Timed.</param>
/// <param name="Note">Anything the reader needs to interpret the row.</param>
public sealed record Scenario(
    string Name,
    string Dimension,
    int[] Values,
    Cadence Cadence,
    Func<int, IDisposable?> Setup,
    Action<int> Work,
    string? Note = null)
{
    /// <summary>
    /// Something the scenario measures that is not time — cache bytes, for
    /// instance. Reported beside the timings when present.
    /// </summary>
    public Func<long>? Gauge { get; init; }

    public string? GaugeUnit { get; init; }

    /// <summary>
    /// Timed repeats per value. Per-scenario because the cost per repeat spans
    /// four orders of magnitude here: a warm composite wants enough runs for a
    /// p95 to mean something, and a cold pass over a whole scene wants three.
    /// A single global count would either make the cheap rows noise or the
    /// expensive ones an overnight job.
    /// </summary>
    public int Iterations { get; init; } = 15;

    public int Warmup { get; init; } = 3;
}

/// <summary>A swept scenario: its samples, its fitted exponent, and its cliff.</summary>
public sealed class Curve(Scenario scenario, IReadOnlyList<Sample> samples)
{
    public Scenario Scenario { get; } = scenario;

    public IReadOnlyList<Sample> Samples { get; } = samples;

    /// <summary>
    /// The exponent of <c>cost ∝ n^k</c>, by least squares on the log-log
    /// points.
    /// </summary>
    /// <remarks>
    /// <b>The exponent is the durable result and the milliseconds are not.</b>
    /// A time is a property of the machine that measured it — every budget in
    /// the charter carries a "measured on the slow container" caveat for that
    /// reason. A slope is a property of the code: linear here is linear on a
    /// desktop, and a jump from 1.0 to 2.0 is a real defect on any hardware.
    /// </remarks>
    public double Exponent { get; } = FitExponent(samples);

    /// <summary>
    /// The first swept value whose p95 misses the budget, or null if none did.
    /// </summary>
    /// <remarks>
    /// <b>p95, not the fastest run.</b> The regression budgets take the fastest
    /// of several, and that is right for a ratchet — it is the least
    /// contaminated estimate of what the code costs. It is the wrong statistic
    /// for a cliff, because a path that is usually fast and occasionally
    /// terrible is exactly what an artist feels as a stutter, and the fastest
    /// run is blind to it by construction.
    /// </remarks>
    public int? Cliff { get; } = samples
        .Where(s => s.P95 > Budgets.Ms(scenario.Cadence))
        .Select(s => (int?)s.X)
        .FirstOrDefault();

    /// <summary>How close the largest swept workload came to the budget.</summary>
    public double WorstPressure => Samples.Count == 0
        ? 0
        : Samples.Max(s => s.Pressure(Scenario.Cadence));

    private static double FitExponent(IReadOnlyList<Sample> samples)
    {
        var points = samples.Where(s => s.X > 0 && s.P50 > 0).ToList();
        if (points.Count < 2) return double.NaN;

        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var s in points)
        {
            var x = Math.Log(s.X);
            var y = Math.Log(s.P50);
            sx += x;
            sy += y;
            sxx += x * x;
            sxy += x * y;
        }

        var n = points.Count;
        var denom = n * sxx - sx * sx;
        return Math.Abs(denom) < 1e-12 ? double.NaN : (n * sxy - sx * sy) / denom;
    }
}

/// <summary>
/// Runs scenarios and produces curves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every scenario is fully torn down and the heap collected between
/// values.</b> Two benchmarks in one process are not two measurements: earlier
/// in this codebase an A/B on the fluid lattice read as a 25% regression that
/// was entirely an earlier scenario having grown a shared buffer. That cost an
/// hour and nearly shipped the wrong conclusion, so isolation is a property of
/// the harness rather than a thing each scenario remembers.
/// </para>
/// <para>
/// A calibration figure is recorded alongside every run — a fixed synthetic
/// loop with no I/O and no allocation. It makes two runs on different machines
/// comparable in the only way absolute timings ever can be: as a ratio.
/// </para>
/// </remarks>
public static class Runner
{
    public static Curve Sweep(Scenario scenario)
    {
        var samples = new List<Sample>();
        int iterations = scenario.Iterations, warmup = scenario.Warmup;

        foreach (var x in scenario.Values)
        {
            using var state = scenario.Setup(x);

            for (var i = 0; i < warmup; i++) scenario.Work(x);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var times = new double[iterations];
            var before = GC.GetTotalAllocatedBytes(precise: true);
            for (var i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                scenario.Work(x);
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }
            var allocKb = (GC.GetTotalAllocatedBytes(precise: true) - before) / 1024.0 / iterations;

            Array.Sort(times);
            samples.Add(new Sample(
                x,
                P50: times[times.Length / 2],
                P95: times[Math.Min(times.Length - 1, (int)Math.Ceiling(times.Length * 0.95) - 1)],
                Max: times[^1],
                AllocKb: allocKb,
                Bytes: scenario.Gauge?.Invoke() ?? 0));
        }

        return new Curve(scenario, samples);
    }

    /// <summary>
    /// A fixed unit of arithmetic, in milliseconds. Runs on every machine at
    /// the same instruction count, so two reports can be compared by ratio even
    /// though their absolute timings cannot.
    /// </summary>
    public static double CalibrationMs()
    {
        var best = double.MaxValue;
        for (var r = 0; r < 5; r++)
        {
            var sw = Stopwatch.StartNew();
            double acc = 0;
            for (var i = 1; i < 20_000_000; i++) acc += 1.0 / i;
            sw.Stop();
            GC.KeepAlive(acc);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }
}
