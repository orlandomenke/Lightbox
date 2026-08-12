using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The everyday brushes stay affordable, measured through the real pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>B177's guard.</b> The report was "ink is really responsive; flat medium
/// and pencil lag", and nothing in the suite measured the one axis an artist
/// actually changes — which brush they picked. The sweep that diagnosed it
/// lives in <c>tools/Lightbox.Bench</c> (<c>BrushPresetSweeps</c>); this is the
/// cheap always-on version, deliberately loose the way every budget here is —
/// it catches an order-of-magnitude regression, not drift.
/// </para>
/// <para>
/// Commit time, not live time, on purpose: the live path defers wet edge and
/// granulation to pen-lift, so the texture cost B177 measured lands entirely
/// in <c>EndStroke</c> — a per-event budget would never see it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushPresetBudgetTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// Generous on purpose: the measured commits are 10–75 ms on this
    /// hardware, and this suite runs beside two thousand other tests on
    /// loaded machines. What this catches is the 10× accident — an effect
    /// pass gone quadratic, a full-canvas surface where a stroke-bounded one
    /// was meant.
    /// </summary>
    private const double CommitBudgetMs = 500;

    [AvaloniaTheory]
    [Trait("Category", "Performance")]
    [InlineData("builtin-ink")]
    [InlineData("builtin-pencil")]
    [InlineData("builtin-soft-round")]
    [InlineData("builtin-airbrush")]
    public void TheEverydayPresetsAreAllInsideTheStrokeBudget(string presetId)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, ColorHex = "#000000" };
        vm.NewDocument(new NewDocumentSettings("budget", 1920, 1080, 12, 72, "#ffffff", false));

        var preset = vm.BrushPresetChoices.FirstOrDefault(p => p.Id == presetId);
        Assert.NotNull(preset);
        vm.ApplyPreset(preset!);

        // Warm once: shader caches, the grain tile, JIT — first-stroke costs
        // that are paid per process, not per stroke.
        Stroke(vm);

        // Median of five, so one GC pause cannot fail the run.
        var commits = new List<double>();
        for (var run = 0; run < 5; run++)
        {
            commits.Add(Stroke(vm));
        }
        commits.Sort();
        var median = commits[commits.Count / 2];

        output.WriteLine($"{preset!.Name}: commit median {median:F1} ms "
            + $"(runs: {string.Join(", ", commits.Select(c => c.ToString("F1")))})");
        Assert.True(median < CommitBudgetMs,
            $"{preset.Name} commits in {median:F1} ms — over the {CommitBudgetMs} ms budget, "
            + "which is an order-of-magnitude regression, not drift");
    }

    /// <summary>One 40-event stroke; returns how long the pen-lift took.</summary>
    private static double Stroke(MainViewModel vm)
    {
        vm.BeginStroke(60, 300, 1);
        for (var i = 1; i <= 40; i++)
        {
            vm.MoveStroke(60 + i * 40, 300 + Math.Sin(i / 4.0) * 150, 0.8);
        }
        Dispatcher.UIThread.RunJobs();

        var clock = Stopwatch.StartNew();
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        return clock.Elapsed.TotalMilliseconds;
    }
}
