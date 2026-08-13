using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

[Collection("BrushState")]
public class LivePostWedgeReproTests(ITestOutputHelper output) : BrushStateIsolated
{
    [AvaloniaFact]
    public void RunnerThrowingSynchronouslyWedgesTheFlagForever()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#3060c0",
            BrushSize = 24,
            BrushHardness = 0.7,
            BrushOpacity = 1,
            BrushFlow = 0.9,
            BrushMedium = MediumKind.Watercolour,
        };
        var work = new Queue<Action>();
        var throwOnce = true;
        vm.LivePostRunner = w =>
        {
            if (throwOnce) { throwOnce = false; throw new InvalidOperationException("simulated Task.Run scheduling failure"); }
            work.Enqueue(w);
            return Task.CompletedTask;
        };
        Dispatcher.UIThread.RunJobs();

        vm.BeginStroke(100, 100, 0.9);
        for (var i = 1; i <= 4; i++) vm.MoveStroke(100 + i * 40, 100, 0.9);

        bool caught = false;
        try { Dispatcher.UIThread.RunJobs(); } catch (InvalidOperationException) { caught = true; }
        output.WriteLine($"caught on first RunJobs: {caught}, work so far: {work.Count}");

        for (var i = 0; i < 40; i++)
        {
            vm.MoveStroke(2000 + i * 5, 100, 0.9);
            bool c2 = false;
            try { Dispatcher.UIThread.RunJobs(); } catch (InvalidOperationException) { c2 = true; }
            if (work.Count > 0) { output.WriteLine($"work appeared at iteration {i}, caught this round: {c2}"); break; }
        }

        output.WriteLine($"final work items queued: {work.Count}");

        vm.EndStroke();
        try { Dispatcher.UIThread.RunJobs(); } catch { }
    }
}
