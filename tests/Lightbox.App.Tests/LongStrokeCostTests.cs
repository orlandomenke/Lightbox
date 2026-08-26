using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.Raster;

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
    /// <summary>
    /// A hard round stroke re-stamps only the dabs that are still moving,
    /// however long it gets (B299).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Structural rather than timed, and that is the point.</b> The budget
    /// test above measures milliseconds and is therefore at the mercy of the
    /// machine; this measures the thing the milliseconds come from. If the
    /// settled cut advances, a pointer event repeats only the tail, and the cost
    /// cannot grow with the mark no matter what the clock says on the day.
    /// </para>
    /// <para>
    /// <b>Why it can be asked at all now.</b> Coverage accumulated as a maximum
    /// is a per-dab property, so a dab whose position has settled contributes a
    /// fixed amount for ever and <c>StableCount</c> is a sound cut. The path
    /// union it replaced had no such prefix - its outline is not a prefix of
    /// itself - which is why the same cut was tried three times against it and
    /// failed three times. This test would have been unwritable a day ago.
    /// </para>
    /// <para>
    /// <b>The bound is generous on purpose.</b> What is being refused is growth
    /// proportional to the stroke, not a particular tail length: at 600 events
    /// this mark carries thousands of dabs, so a tail that stays in the tens is
    /// the assertion however the densifier is tuned later.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A brush with an effect in it re-processes only the band that moved
    /// (B313).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The twin of the test below, for the other route.</b> That one is
    /// about re-stamping dabs; this is about the post-process, which is where
    /// Pencil and the flats spend their time — one pass per pointer event over
    /// the whole mark, copies included, growing with every event.
    /// </para>
    /// <para>
    /// <b>An area, not a duration.</b> A millisecond figure on a loaded machine
    /// cannot separate "reads less of the mark" from "happened to run while
    /// nothing else did", and this repository has been fooled by exactly that
    /// before. The pass's own rect against the mark's rect is the structural
    /// question and the clock is not involved.
    /// </para>
    /// <para>
    /// The runner is made synchronous so a pass lands inside <c>RunJobs</c>;
    /// asynchronously the stroke would finish before any pass ran and the test
    /// would measure nothing at all.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AGranulatedStrokeReprocessesOnlyTheBandThatMoved()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            BrushSize = 3,
            BrushHardness = 0.9,
            BrushOpacity = 1,
            BrushFlow = 0.85,
            BrushGranulation = 0.15,
            ColorHex = "#000000",
            LivePostRunner = work => { work(); return Task.CompletedTask; },
        };
        vm.NewDocument(new NewDocumentSettings("pencil", 1920, 1080, 12, 72, "#ffffff", false));

        vm.BeginStroke(50, 500, 1);
        Dispatcher.UIThread.RunJobs();

        var x = 50.0;
        for (var i = 0; i < 400; i++)
        {
            x += 4.0;
            if (x > 1850) x = 50;
            vm.MoveStroke(x, 500 + Math.Sin(x / 40) * 200, 0.9);
            Dispatcher.UIThread.RunJobs();
        }

        var worst = vm.LivePostWorstPixels;
        var mark = vm.LivePostWorstMarkPixels;
        vm.EndStroke();
        output.WriteLine(
            $"worst pass {worst} px of a {mark} px mark ({(mark == 0 ? 0 : 100.0 * worst / mark):0.0}%)");

        // The mark has to be big enough for the ratio to mean anything: a pass
        // over a mark barely larger than one event's reach would pass this on a
        // build that read the whole thing every time.
        Assert.True(mark > 400_000, $"the mark only reached {mark} px");
        Assert.True(
            worst < mark / 4,
            $"a pass read {worst} px of a {mark} px mark");
    }

    [AvaloniaFact]
    public void ASilhouetteStrokeRestampsOnlyItsMovingTail()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            BrushSize = 6,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            ColorHex = "#000000",
        };
        vm.NewDocument(new NewDocumentSettings("ink", 1920, 1080, 12, 72, "#ffffff", false));
        Assert.True(
            BrushEngine.DrawsAsOneSilhouette(vm.CurrentToolSettingsForTest),
            "this test is about the silhouette route and the brush must take it");

        vm.BeginStroke(50, 500, 1);
        Dispatcher.UIThread.RunJobs();

        var worstTail = 0;
        var total = 0;
        var x = 50.0;
        for (var i = 0; i < 600; i++)
        {
            x += 2.5;
            if (x > 1850) x = 50;
            vm.MoveStroke(x, 500 + Math.Sin(x / 40) * 200, 0.9);
            Dispatcher.UIThread.RunJobs();

            var (settled, dabs) = vm.LiveDabCutForTests;
            total = dabs;
            // Ignore the opening events, where there is no settled prefix to
            // speak of and the tail IS the mark.
            if (i >= 20 && dabs - settled > worstTail) worstTail = dabs - settled;
        }

        vm.EndStroke();
        output.WriteLine($"{total} dabs, worst provisional tail {worstTail}");

        // The mark has to be long enough for the question to mean anything: a
        // stroke of forty dabs would pass this on a build that re-stamped
        // everything, which is the shape of mistake brush-measurement warns of.
        Assert.True(total > 1000, $"the stroke only reached {total} dabs");
        Assert.True(worstTail < 80, $"a pointer event re-stamped {worstTail} dabs");
    }

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
