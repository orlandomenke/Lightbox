using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// A pointer event must stay inside the frame budget however long the stroke gets.
/// </summary>
/// <remarks>
/// <para>
/// <b>The existing 4K budget could not have caught what this measures.</b> It runs twenty
/// events, and twenty events cannot distinguish per-event work from work proportional to
/// the stroke so far.
/// </para>
/// <para>
/// <b>Fixing B45 traded scaling for correctness, deliberately and with the numbers
/// known.</b> The live preview used to stamp the newest two points, which was O(1) an
/// event and rendered a mark that did not match its own commit. It now walks the whole
/// stroke, which is O(n) an event: measured at 0.41 ms over the first fifty events of a
/// six-hundred-event stroke and 1.70 ms over the last fifty. Attributed rather than
/// guessed — <c>GeometryOps.Densify</c> is 0.84 ms of the 1.15 ms walk at 600 points, and
/// both it and the walk are re-run from the start each event.
/// </para>
/// <para>
/// <b>So this guards the budget rather than the slope</b>, because the slope is known and
/// accepted for now: even at six hundred events a pointer event costs under a tenth of a
/// 60 Hz frame. B46 has the fix — densify incrementally so only the newest spans are
/// recomputed — and the growth figure is printed here so that work can be measured
/// against something.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LongStrokeCostTests(ITestOutputHelper output) : BrushStateIsolated
{
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void APointerEventStaysInBudgetDeepIntoALongStroke()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false, BrushSize = 24, ColorHex = "#000000",
        };
        vm.NewDocument(new NewDocumentSettings("long", 1920, 1080, 12, 72, "#ffffff", false));

        vm.BeginStroke(50, 500, 1);
        Dispatcher.UIThread.RunJobs();

        var times = new List<double>();
        var x = 50.0;
        for (var i = 0; i < 600; i++)
        {
            x += 2.5;
            if (x > 1850) x = 50;
            var clock = Stopwatch.StartNew();
            vm.MoveStroke(x, 500 + Math.Sin(x / 40) * 200, 0.9);
            Dispatcher.UIThread.RunJobs();
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        vm.EndStroke();

        // Medians, not means: one GC pause in a bucket moves a mean by more than the
        // effect being measured, and this runs beside two thousand other tests.
        double Median(int from, int count)
        {
            var slice = times.Skip(from).Take(count).OrderBy(t => t).ToList();
            return slice[slice.Count / 2];
        }

        var first = Median(0, 50);
        var middle = Median(275, 50);
        var last = Median(550, 50);

        output.WriteLine($"events   0- 50: {first,6:F3} ms");
        output.WriteLine($"events 275-325: {middle,6:F3} ms");
        output.WriteLine($"events 550-600: {last,6:F3} ms");
        output.WriteLine($"growth last/first: {last / Math.Max(0.001, first):F2}x  (known O(n), see B46)");

        // The budget, not the slope. A 60 Hz frame is 16.7 ms and a pointer event has to
        // share it with the repaint, so a fifth of a frame at the far end of a very long
        // stroke is the line worth holding.
        Assert.True(
            last < 6.0,
            $"a pointer event cost {last:F2} ms after 550 events of one stroke (budget 6)");
    }
}
