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
