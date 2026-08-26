using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// How much of a stroke is still moving, and whether that depends on pen speed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The owner reports fast strokes trailing and slow ones not, and every fix
/// aimed at the consequences has missed.</b> The counter that keeps appearing is
/// the provisional tail: <b>221</b> dabs re-stamped per pointer event in one
/// capture, <b>427</b> in the next. That is the work every event repeats, it
/// sets the region the post-process must redo, and it is the only measured
/// quantity that plausibly scales with how fast the hand is moving.
/// </para>
/// <para>
/// <b>But dabs are the wrong unit for the question.</b> The walk emits a dab
/// every <c>spacing × diameter</c> of arc length, so a faster hand produces more
/// dabs per event whatever else is true — 427 dabs might be fourteen events of
/// lag or one. Those want completely different fixes, and the difference is
/// invisible in the report's units.
/// </para>
/// <para>
/// So this reports the tail in <b>events</b> as well: dabs of tail divided by
/// dabs per event. <c>StableCount</c>'s own documentation says densify looks one
/// point ahead, so about one is the number that would mean nothing is wrong.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class ProvisionalTailTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int Events = 120;

    /// <summary>Gouache (flat): the brush the owner was drawing with.</summary>
    private static MainViewModel Ready(bool smooth)
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = smooth,
            ColorHex = "#203040",
            BrushSize = 18,
            BrushHardness = 0.75,
            BrushOpacity = 0.95,
            BrushFlow = 0.9,
            BrushSpacing = 0.12,
            BrushWetEdge = 0.15,
            BrushGranulation = 0.25,
            LivePostRunner = work => { work(); return Task.CompletedTask; },
        };
        vm.NewDocument(new NewDocumentSettings("tail", 3840, 2160, 12, 72, "#ffffff", false));
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>
    /// Drive a stroke at a fixed travel per event and watch the settled cut.
    /// </summary>
    /// <remarks>
    /// A gentle arc rather than a straight line: a perfectly straight stroke is
    /// the case where densify has nothing to revise, so it would report a tail
    /// of nothing and prove only that the harness was too kind.
    /// </remarks>
    private (double DabsPerEvent, int WorstTail, double WorstTailEvents) Measure(
        bool smooth, double travel)
    {
        var vm = Ready(smooth);
        double x = 300, y = 1080, heading = 0;

        vm.BeginStroke(x, y, 1);
        Dispatcher.UIThread.RunJobs();

        var worstTail = 0;
        var total = 0;
        for (var i = 0; i < Events; i++)
        {
            heading += 0.01;
            x += travel * Math.Cos(heading);
            y += travel * Math.Sin(heading);
            if (x > 3600 || x < 200) { heading = Math.PI - heading; x = Math.Clamp(x, 200, 3600); }
            if (y > 2000 || y < 160) { heading = -heading; y = Math.Clamp(y, 160, 2000); }

            vm.MoveStroke(x, y, 0.9);
            Dispatcher.UIThread.RunJobs();

            var (settled, dabs) = vm.LiveDabCutForTests;
            total = dabs;
            // The opening events have no settled prefix to speak of — the tail
            // IS the mark — so they say nothing about the steady state.
            if (i >= 20 && dabs - settled > worstTail) worstTail = dabs - settled;
        }

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        var perEvent = total / (double)Events;
        var tailEvents = perEvent > 0 ? worstTail / perEvent : 0;
        output.WriteLine(
            $"  smooth {(smooth ? "on " : "off")}  travel {travel,5:0.#} px"
            + $"  dabs/event {perEvent,6:0.0}  worst tail {worstTail,5} dabs"
            + $"  = {tailEvents,5:0.0} events");
        return (perEvent, worstTail, tailEvents);
    }

    [AvaloniaFact]
    public void HowFarBehindTheSettledCutFalls()
    {
        output.WriteLine("provisional tail against pen speed (B313 follow-up)");
        output.WriteLine("  the owner's capture: 15.1 px an event is an ordinary hand,");
        output.WriteLine("  and a fast flick is several times that.");

        foreach (var smooth in new[] { false, true })
        {
            foreach (var travel in new[] { 5.0, 15.1, 30.0, 60.0, 120.0 })
            {
                Measure(smooth, travel);
            }
        }
    }
}
