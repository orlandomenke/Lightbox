using System.Diagnostics;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The performance budgets run alone.
/// </summary>
/// <remarks>
/// They measure wall-clock time, so anything running beside them is measurement
/// noise — and on a four-core box the rest of this assembly is several threads
/// of rasterisation. A collection that disables parallelisation is not run
/// alongside other collections, which is the cheapest way to get the timings
/// off a contended machine.
///
/// It does not help against the <em>other</em> test assemblies, which run at
/// the same time under <c>dotnet test Lightbox.sln</c>. That is what
/// <see cref="Bench.FastestMs"/> is for.
/// </remarks>
[CollectionDefinition("Performance", DisableParallelization = true)]
public class PerformanceCollection;

/// <summary>
/// Timing a hot path in a way that survives a loaded machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fastest run, not the median.</b> Every one of these budgets used to
/// compare a median, and a median is a measurement of the machine as much as of
/// the code: when the scheduler takes half the cores away, half the runs are
/// slow and the median moves with them. Measured here, six busy threads on four
/// cores moved the flood-fill median from 114 ms to 179 ms against a 250 ms
/// budget — and a CI runner with fewer cores and a noisier neighbour is exactly
/// where that tips over. It did, repeatedly, and each time it was patched by
/// raising one number.
/// </para>
/// <para>
/// The minimum of several runs is the standard answer, and it is the right one
/// here for a reason specific to what these tests are for: they exist to catch
/// order-of-magnitude regressions — a live preview going full-canvas again —
/// and a change like that raises the floor as surely as it raises the middle.
/// Nothing that makes the code ten times slower leaves a fast run behind.
/// </para>
/// <para>
/// What this deliberately gives up is sensitivity to <em>occasional</em>
/// slowness — a path that is usually fast and sometimes terrible reads as fast
/// here. That is a real gap, and it belongs to a latency test with percentiles
/// rather than to a regression budget; pretending a noisy median covered it was
/// the more expensive mistake.
/// </para>
/// </remarks>
internal static class Bench
{
    /// <summary>
    /// Run <paramref name="action"/> <paramref name="runs"/> times and return
    /// the fastest, in milliseconds.
    /// </summary>
    /// <param name="before">
    /// Setup that must happen fresh before each run and must not be timed —
    /// evicting a cache, rebuilding state the action consumes.
    /// </param>
    /// <param name="warm">
    /// Whether to run once untimed first. On by default: the first call pays
    /// for JIT and for whatever tables the path builds once per process, and a
    /// budget that measured that would be measuring the runtime.
    /// </param>
    public static double FastestMs(
        int runs, Action action, Action? before = null, bool warm = true, ITestOutputHelper? log = null)
    {
        if (warm)
        {
            before?.Invoke();
            action();
        }

        var best = double.MaxValue;
        var worst = 0.0;
        var sw = new Stopwatch();
        for (var i = 0; i < runs; i++)
        {
            before?.Invoke();
            sw.Restart();
            action();
            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds;
            best = Math.Min(best, ms);
            worst = Math.Max(worst, ms);
        }
        // Both numbers go to the log. The spread is what tells a later reader
        // whether a budget failure was the code or the machine.
        log?.WriteLine($"fastest {best:0.00} ms of {runs} (slowest {worst:0.00} ms)");
        return best;
    }
}
