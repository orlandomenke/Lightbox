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
/// <see cref="Bench.FastestMs"/> and <see cref="Bench.MachineIsQuiet"/> are for.
///
/// <b>Every class carrying a performance-tagged test belongs in a collection
/// that disables parallelisation — but not necessarily this one.</b> On
/// 2026-08-25 four such classes were found outside it and three were moved in;
/// the fourth, <c>BrushTipOutlineTests</c>, is <c>[Collection("Registries")]</c>,
/// which is *also* <c>DisableParallelization = true</c> and therefore already
/// gives it what this collection would. A class can only be in one collection,
/// so the check to make is "is it in a serial collection", not "is it in this
/// one" — and moving it here would have cost it the registry isolation it is in
/// that collection for.
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
    /// <summary>
    /// Whether this machine looks quiet enough for a <em>tight</em> budget to
    /// mean anything — measured now, on the box the test is running on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> The budgets here are loose because a contended
    /// machine inflates them, and loose budgets give up the thing they were for:
    /// a 1200 ms ceiling around a 200 ms cost cannot see a change that makes the
    /// path twice as slow. That is a real gap and raising or lowering the one
    /// number cannot close it — the number has to answer two questions at once.
    /// So it does not: the loose ceiling stays as the backstop, and the tight
    /// one is applied only when the measurement can carry it.
    /// </para>
    /// <para>
    /// <b>What is measured, and why it is the spread rather than the time.</b>
    /// A fixed arithmetic loop is run several times and the fastest and slowest
    /// are compared. An absolute calibration time cannot tell a busy fast
    /// machine from an idle slow one — but contention is *intermittent* by
    /// nature, so it shows up as variance between identical runs. Measured on
    /// the container this was written in: <b>spread 1.09 idle, 1.59 under eight
    /// busy threads on four cores.</b>
    /// </para>
    /// <para>
    /// <b>What was tried first and does not work</b>, recorded so it is not
    /// retried: normalising the measurement by the calibration time — the
    /// obvious move, and what <c>tools/Lightbox.Bench</c> does for its curves.
    /// The ratio was **10.13 idle, then 16.12 and 8.63 in two loaded runs** —
    /// it drifts in *both* directions, because a tight float loop and a mixed
    /// rasterisation workload do not lose the same share of a contended core.
    /// A signal that moves 1.9x on its own cannot detect a 2x regression.
    /// </para>
    /// <para>
    /// <b>The residual gap, stated rather than hidden.</b> A machine under
    /// *steady* load has low variance and looks quiet here, so the tight budget
    /// would be applied to an inflated number and could fail honestly-slow.
    /// The structural answer to all of this is to stop running the
    /// performance-tagged tests beside three other assemblies at all — this is
    /// what can be done from inside the test assembly.
    /// </para>
    /// </remarks>
    public static bool MachineIsQuiet(ITestOutputHelper? log = null, double maxSpread = 1.25)
    {
        const int runs = 6;
        var best = double.MaxValue;
        var worst = 0.0;
        for (var r = 0; r < runs; r++)
        {
            var sw = Stopwatch.StartNew();
            double acc = 0;
            for (var i = 1; i < 3_000_000; i++) acc += 1.0 / i;
            sw.Stop();
            GC.KeepAlive(acc);
            var ms = sw.Elapsed.TotalMilliseconds;
            best = Math.Min(best, ms);
            worst = Math.Max(worst, ms);
        }
        var spread = best > 0 ? worst / best : double.MaxValue;
        var quiet = spread <= maxSpread;
        // Always logged, both numbers: a later reader needs to know whether the
        // tight budget ran at all before reading anything into it passing.
        log?.WriteLine(
            $"calibration {best:0.00}-{worst:0.00} ms, spread {spread:0.00} — "
            + (quiet ? "quiet, the tight budget applies" : "contended, only the backstop applies"));
        return quiet;
    }

    /// <summary>
    /// The fastest of <paramref name="runs"/> for each of two actions, measured
    /// alternately so that both see the same machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ratio needs its two sides sampled together, and the minimum alone
    /// does not give it that.</b> Taking the fastest of several runs is what
    /// makes a single measurement survive a loaded machine — but two minima
    /// taken minutes apart are two measurements of two different machines, and
    /// whatever contention landed on one and not the other goes straight into
    /// the quotient. Alternating the two puts any load on both sides, where it
    /// divides out instead of accumulating.
    /// </para>
    /// <para>
    /// <b>Measured, on B339's checkpointed-open ratio.</b> Taken apart, three
    /// consecutive trials on an idle box read 1.45, 1.97 and 2.40 — against a
    /// ceiling of 2.5, with nothing else running. Alternated, the same quantity
    /// read 1.47, 1.55, 1.63. The excursions are what pairing removes.
    /// </para>
    /// <para>
    /// <b>What it does not remove is a floor</b>, and that is worth knowing
    /// before reaching for this: the ratio above settled at ~1.55 because half
    /// of what it timed genuinely grows with the input. Pairing makes a ratio
    /// *repeatable*; it cannot make it *mean* something it does not. If the
    /// paired number is stable and not where the claim says it should be, the
    /// claim and the measurement disagree about what is being timed.
    /// </para>
    /// </remarks>
    public static (double A, double B) PairedFastestMs(
        int runs, Action a, Action b, bool warm = true, ITestOutputHelper? log = null)
    {
        if (warm)
        {
            a();
            b();
        }

        var bestA = double.MaxValue;
        var bestB = double.MaxValue;
        var sw = new Stopwatch();
        for (var i = 0; i < runs; i++)
        {
            sw.Restart();
            a();
            sw.Stop();
            bestA = Math.Min(bestA, sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            b();
            sw.Stop();
            bestB = Math.Min(bestB, sw.Elapsed.TotalMilliseconds);
        }
        log?.WriteLine($"paired fastest of {runs}: {bestA:0.00} ms / {bestB:0.00} ms");
        return (bestA, bestB);
    }

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
