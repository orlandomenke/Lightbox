using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Drawing budgets on a 4K document, measured through the real view-model
/// path — pointer events, the composite the canvas displays, the commit on
/// pen lift. These are the numbers the artist feels.
///
/// Budgets are generous because shared CI runners are slow and noisy; they
/// exist to catch order-of-magnitude regressions (per-event full-canvas
/// compositing creeping back, a layer copy per stroke) rather than drift.
/// </summary>
/// <summary>
/// These tests allocate 4K buffers and measure wall-clock time, so they must
/// not share the machine with the rest of the suite.
/// </summary>
[CollectionDefinition("LargeCanvasPerformance", DisableParallelization = true)]
public class LargeCanvasPerformanceCollection;

[Trait("Category", "Performance")]
[Collection("LargeCanvasPerformance")]
public class LargeCanvasPerformanceTests(ITestOutputHelper output)
{
    private const int W = 3840;
    private const int H = 2160;

    private static MainViewModel Vm4K(double brushSize = 180)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("4K", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#204080";
        vm.BrushSize = brushSize;
        vm.BrushHardness = 0.6;
        vm.BrushOpacity = 0.9;
        vm.BrushFlow = 0.8;
        vm.BrushGranulation = 0;
        vm.BrushWetEdge = 0;
        vm.BrushScatter = 0;
        return vm;
    }

    /// <summary>Runs the queued repaint, exactly as the dispatcher would.</summary>
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private double MedianMs(int runs, Action action)
    {
        action();
        var times = new List<double>(runs);
        var sw = new Stopwatch();
        for (var i = 0; i < runs; i++)
        {
            sw.Restart();
            action();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var median = times[times.Count / 2];
        output.WriteLine($"median {median:0.00} ms over {runs} runs (min {times[0]:0.00}, max {times[^1]:0.00})");
        return median;
    }

    [AvaloniaFact]
    public void FourK_PointerEventDuringAStroke_RepaintsOnlyWhatChanged()
    {
        var vm = Vm4K();
        vm.BeginStroke(400, 400, 1);
        Pump();
        var x = 400.0;
        var median = MedianMs(20, () =>
        {
            x += 14;
            vm.MoveStroke(x, 420 + Math.Sin(x / 30) * 50, 0.85);
            Pump(); // include the repaint the canvas actually shows
        });
        vm.EndStroke();

        // A pointer event must fit inside a 60 Hz frame with room to spare.
        // Full-canvas compositing here would cost ~70 ms on this hardware.
        Assert.True(median < 20, $"4K pointer event took {median:0.00} ms (budget 20)");
    }

    [AvaloniaFact]
    public void FourK_WholeStrokeIncludingCommit_HasNoPenLiftStall()
    {
        var vm = Vm4K();
        var y = 300.0;
        var median = MedianMs(6, () =>
        {
            y += 40;
            vm.BeginStroke(300, y, 1);
            for (var i = 1; i <= 12; i++)
            {
                vm.MoveStroke(300 + i * 60, y + i * 4, 0.9);
                Pump();
            }
            vm.EndStroke();
            Pump();
        });
        Assert.True(median < 400, $"4K stroke + commit took {median:0.00} ms (budget 400)");
    }

    [AvaloniaFact]
    public void FourK_UndoAfterAStroke_StaysResponsive()
    {
        var vm = Vm4K();
        var median = MedianMs(4, () =>
        {
            vm.BeginStroke(500, 500, 1);
            for (var i = 1; i <= 8; i++)
            {
                vm.MoveStroke(500 + i * 50, 520, 0.9);
                Pump();
            }
            vm.EndStroke();
            Pump();
            vm.UndoCommand.Execute(null);
            Pump();
        });
        Assert.True(median < 1500, $"4K stroke + undo took {median:0.00} ms (budget 1500)");
    }

    /// <summary>
    /// The start of a stroke must not repaint more than the middle of one.
    ///
    /// It used to: the compose ring's idle buffers each accumulated the union
    /// of everything that changed while they sat out, so after a stroke
    /// crossing the canvas the next stroke's first three events each
    /// recomposited that whole bounding box — measured at 73, 80, 80 ms
    /// against a 7–12 ms steady state. Three slow events in a row is exactly
    /// long enough to feel like the pen skipped.
    ///
    /// Asserted on the repainted area rather than the clock: the area is what
    /// the cost is proportional to, and it does not move under a noisy runner.
    /// </summary>
    [AvaloniaFact]
    public void FourK_TheFirstEventsOfAStroke_RepaintNoMoreThanTheMiddleOfOne()
    {
        var vm = Vm4K();
        var sw = new Stopwatch();
        var times = new List<double>();

        long Event(double x, double y)
        {
            sw.Restart();
            vm.MoveStroke(x, y, 0.9);
            Pump();
            times.Add(sw.Elapsed.TotalMilliseconds);
            var clip = vm.LastPublishClip;
            // A whole-canvas publish reports no clip at all.
            return clip is { } r ? (long)r.Width * r.Height : (long)W * H;
        }

        // A stroke right across the canvas, so every idle buffer falls behind
        // by the full width of the document.
        vm.BeginStroke(200, 1000, 1);
        Pump();
        for (var i = 1; i <= 24; i++) Event(200 + i * 150, 1000 + i * 30);
        vm.EndStroke();
        Pump();

        // Now a second stroke elsewhere. Its first events are the ones that
        // used to pay off the first stroke's debt.
        vm.BeginStroke(300, 300, 1);
        Pump();
        times.Clear();
        var areas = new List<long>();
        for (var i = 1; i <= 12; i++) areas.Add(Event(300 + i * 25, 300 + i * 6));
        vm.EndStroke();
        Pump();

        var steady = areas.Skip(4).OrderBy(a => a).ToList();
        var median = steady[steady.Count / 2];
        var worst = areas.Take(3).Max();
        output.WriteLine(
            $"first three areas {string.Join(", ", areas.Take(3))} px² | steady median {median} px² | " +
            $"first three {string.Join(", ", times.Take(3).Select(t => $"{t:0.0}"))} ms");

        // A dab-sized repaint, not a stroke-sized one. Doubling is slack for
        // the coalescing of a couple of events; the defect was ~600x.
        Assert.True(worst <= median * 2,
            $"stroke start repainted {worst} px² against a {median} px² steady state");
    }

    /// <summary>
    /// Alpha lock and the selection clip are applied live, which means an
    /// isolating SaveLayer on every publish. That is only affordable because
    /// the layer is bounded to the dirty region — a refactor that lost the
    /// clip would allocate a 4K offscreen per pointer event and this is what
    /// would notice.
    /// </summary>
    [AvaloniaFact]
    public void FourK_MaskedStroke_CostsNoMoreThanAnUnmaskedOne()
    {
        double Median(bool locked)
        {
            var vm = Vm4K();
            vm.Doc.Scene.Layers.First(l => !l.IsBackground).AlphaLocked = locked;
            vm.BeginStroke(400, 400, 1);
            Pump();
            var x = 400.0;
            return MedianMs(15, () =>
            {
                x += 14;
                vm.MoveStroke(x, 420 + Math.Sin(x / 30) * 50, 0.85);
                Pump();
            });
        }

        var plain = Median(locked: false);
        var masked = Median(locked: true);
        output.WriteLine($"unmasked {plain:0.00} ms | alpha-locked {masked:0.00} ms");

        // The absolute budget matters more than the ratio: both must fit in a
        // frame. A canvas-sized SaveLayer here would be tens of milliseconds.
        Assert.True(masked < 20, $"a masked 4K pointer event took {masked:0.00} ms (budget 20)");
    }

    /// <summary>
    /// A wet-media stroke re-renders the whole stroke so far on a background
    /// pass, so its cost grows with stroke length. Bounded here so the growth
    /// stays a slope rather than a wall.
    /// </summary>
    [AvaloniaFact]
    public void FourK_WetMediaStroke_StaysWithinItsBudget()
    {
        var store = MainViewModel.BrushStorePath;
        MainViewModel.BrushStorePath = Path.Combine(
            Path.GetTempPath(), $"lightbox-perf-brushes-{Guid.NewGuid():N}.json");
        try
        {
            var vm = Vm4K(brushSize: 90);
            vm.BrushMedium = Lightbox.Core.Documents.MediumKind.Watercolour;

            var sw = Stopwatch.StartNew();
            vm.BeginStroke(400, 600, 0.9);
            for (var i = 1; i <= 20; i++)
            {
                vm.MoveStroke(400 + i * 70, 600 + Math.Sin(i * 0.4) * 120, 0.9);
                Pump();
            }
            vm.EndStroke();
            Pump();
            sw.Stop();

            var total = sw.Elapsed.TotalMilliseconds;
            output.WriteLine($"4K watercolour stroke, 20 events + commit: {total:0} ms " +
                             $"({vm.LivePostPasses} live passes)");

            // Generous, and deliberately so: this is the whole stroke plus its
            // commit plus every settle pass. It is here to catch the shape
            // going wrong — a pass per event with no coalescing, or a pass
            // that stops being bounded — not to police drift.
            Assert.True(total < 12000, $"4K watercolour stroke took {total:0} ms (budget 12000)");
            Assert.True(vm.LivePostPasses > 0, "the live medium never rendered at all");
        }
        finally
        {
            MainViewModel.BrushStorePath = store;
        }
    }

    [AvaloniaFact]
    public void FourK_FrameCache_StaysWithinItsMemoryBudget()
    {
        var vm = Vm4K();
        // Fill enough frames that a count-based cache would balloon: 24 4K
        // frames would be 800 MB.
        for (var i = 0; i < 24; i++)
        {
            vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(200 + i * 8, 200, 1);
            vm.MoveStroke(260 + i * 8, 260, 1);
            vm.EndStroke();
        }
        Pump();
        Assert.Contains("MB images", vm.MemoryLabel);
        var mb = double.Parse(vm.MemoryLabel.Split(' ')[0]);
        output.WriteLine($"{vm.DocumentSizeLabel} | {vm.DocumentContentLabel} | {vm.MemoryLabel}");
        Assert.True(mb < 900, $"4K frame cache grew to {mb} MB (budget 900)");
    }

    [AvaloniaFact]
    public void HeadroomReportsSmooth_WhilePaintingOnFourK()
    {
        var vm = Vm4K();
        vm.BeginStroke(400, 400, 1);
        Pump();
        for (var i = 1; i <= 15; i++)
        {
            vm.MoveStroke(400 + i * 20, 400 + i * 3, 0.9);
            Pump();
        }
        vm.EndStroke();
        Pump();

        output.WriteLine($"headroom {vm.Performance.HeadroomPercent}% ({vm.Performance.HealthLabel}), " +
                         $"publish median {vm.Performance.PublishMs:0.0} ms");
        Assert.True(vm.Performance.HeadroomPercent >= 50,
            $"painting on 4K reported only {vm.Performance.HeadroomPercent}% headroom");
        Assert.False(vm.Performance.NeedsAttention);
    }
}
