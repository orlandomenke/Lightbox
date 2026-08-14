using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Q83: the deterministic inbetween command fills the <b>run</b> — extreme to
/// extreme, through any breakdown — rather than stopping at the next drawing.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1's second half in <c>docs/DESIGN-ai-correctness.md</c> asked for
/// "<c>FrameRole.Breakdown</c> as a hard constraint — the arc must pass through
/// it", and measuring it first found the premise was already met and for the
/// wrong reason: <c>NextKeyIndex</c> finds the next <em>drawing</em> whatever
/// its role, so a breakdown was an endpoint and the arc passed through it
/// trivially.
/// </para>
/// <para>
/// What was actually broken was the curve. The easing restarted at the
/// breakdown, so one slow-out/slow-in drawn across a run came out as two with a
/// stutter in the middle — and separately, <see cref="SpacingChart"/> had been
/// closing runs at extremes all along, so the timeline held two notions of a
/// span that disagreed exactly when a breakdown existed.
/// </para>
/// </remarks>
public class BreakdownRunTests(Xunit.ITestOutputHelper output)
{
    /// <summary>A horizontal line at a given height — y is the whole state of the pose.</summary>
    private static void DrawAt(MainViewModel vm, double y)
    {
        vm.BeginStroke(10, y, 0.6);
        vm.MoveStroke(40, y, 0.6);
        vm.EndStroke();
    }

    private static List<(FrameRole? Role, double Y)> Sheet(MainViewModel vm)
    {
        var layer = vm.PaintLayer();
        var rows = new List<(FrameRole?, double)>();
        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is not Frame f) { rows.Add((null, double.NaN)); continue; }
            rows.Add((f.Role, f.Strokes.Count == 0 ? double.NaN : f.Strokes[0].Points.Average(p => p.Y)));
        }
        return rows;
    }

    private void Report(string what, MainViewModel vm)
    {
        output.WriteLine(what);
        var rows = Sheet(vm);
        for (var i = 0; i < rows.Count; i++)
        {
            output.WriteLine(rows[i].Role is null
                ? $"  cel {i}: hold"
                : $"  cel {i}: {rows[i].Role,-10} y={rows[i].Y:0.##}");
        }
    }

    /// <summary>Key at 0, breakdown at 1, key at 2 — a run with one waypoint.</summary>
    private static MainViewModel RunWithBreakdown()
    {
        var vm = new MainViewModel(null);
        vm.SmoothStrokes = false;
        DrawAt(vm, 0);
        vm.AddFrameCommand.Execute(null);
        DrawAt(vm, 50);
        vm.AddFrameCommand.Execute(null);
        DrawAt(vm, 100);
        ((Frame)vm.PaintLayer().Cels[1].Frame!).Role = FrameRole.Breakdown;
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    /// <summary>
    /// <b>The property, and the one worth keeping if the rest are ever
    /// rewritten: a breakdown sitting on the arc changes nothing about the
    /// motion.</b>
    /// </summary>
    /// <remarks>
    /// Two documents describing the same movement — one straight from y=0 to
    /// y=100, one with a breakdown drawn at y=50 halfway along, which is
    /// exactly where the ease would have put it. Filled, the inbetweens must
    /// land on the same numbers. That is what "the breakdown is on the arc"
    /// means, stated so it can be measured.
    /// <para>
    /// On the pre-fix build the second document gives 25 and 75 instead of 12.5
    /// and 87.5: the ease ran twice, once per gap, and the drawings bunch
    /// towards the breakdown from both sides.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ABreakdownOnTheArcDoesNotChangeTheMotion()
    {
        var plain = new MainViewModel(null) { SmoothStrokes = false };
        DrawAt(plain, 0);
        plain.AddFrameCommand.Execute(null);
        DrawAt(plain, 100);
        plain.CurrentFrameIndex = 0;
        plain.TweenCount = 3;
        plain.InsertInbetweensCommand.Execute(null);
        Report("no breakdown, three inbetweens:", plain);

        var withBreakdown = RunWithBreakdown();
        withBreakdown.TweenCount = 1;   // one per gap, so both runs hold five drawings
        withBreakdown.InsertInbetweensCommand.Execute(null);
        Report("breakdown at the midpoint, one inbetween per gap:", withBreakdown);

        var a = Sheet(plain).Where(r => r.Role is not null).Select(r => r.Y).ToList();
        var b = Sheet(withBreakdown).Where(r => r.Role is not null).Select(r => r.Y).ToList();
        Assert.Equal(5, a.Count);
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i], precision: 6);

        // Stated absolutely as well as relatively, so the test still says
        // something if both paths regress together.
        Assert.Equal([0, 12.5, 50, 87.5, 100], b.Select(y => Math.Round(y, 4)));
    }

    /// <summary>
    /// One action fills the whole run, and the breakdown survives it untouched
    /// — same frame relative to the run, same pose, still a breakdown.
    /// </summary>
    [AvaloniaFact]
    public void FillingARunFillsEveryGapAndLeavesTheBreakdownAlone()
    {
        var vm = RunWithBreakdown();
        vm.TweenCount = 2;
        vm.InsertInbetweensCommand.Execute(null);
        Report("two inbetweens per gap:", vm);

        var rows = Sheet(vm).Where(r => r.Role is not null).ToList();
        Assert.Equal(7, rows.Count);                                  // 3 drawn + 2 + 2
        Assert.Equal(FrameRole.Breakdown, rows[3].Role);              // dead centre
        Assert.Equal(50, rows[3].Y, precision: 6);                    // and unmoved
        Assert.All(rows.Where((_, i) => i is not (0 or 3 or 6)),
            r => Assert.Equal(FrameRole.Inbetween, r.Role));

        // Monotone: nothing doubles back, which is what a crossed pairing or a
        // pose escaping its own gap would look like here.
        var ys = rows.Select(r => r.Y).ToList();
        Assert.Equal(ys.Order().ToList(), ys);
    }

    /// <summary>
    /// The whole run is one undo step. It was one step per interval before only
    /// because the artist ran the command once per interval; now that a single
    /// action fills several gaps, one Ctrl+Z has to take all of them back.
    /// </summary>
    [AvaloniaFact]
    public void TheWholeRunIsASingleUndoStep()
    {
        var vm = RunWithBreakdown();
        vm.TweenCount = 2;
        var before = vm.Doc.Scene.FrameCount;

        vm.InsertInbetweensCommand.Execute(null);
        Assert.Equal(before + 4, vm.Doc.Scene.FrameCount);

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, vm.Doc.Scene.FrameCount);
        var rows = Sheet(vm).Where(r => r.Role is not null).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(FrameRole.Breakdown, rows[1].Role);
    }

    /// <summary>
    /// A document that has never marked a breakdown must behave exactly as it
    /// did — <see cref="FrameRole.Key"/> is the default role, so every drawing
    /// is an extreme and a run is an interval. This is the guard on the
    /// reduction, and it is the one that would catch the run machinery leaking
    /// into ordinary use.
    /// </summary>
    [AvaloniaFact]
    public void WithNoBreakdownsARunIsJustTheInterval()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        DrawAt(vm, 0);
        vm.AddFrameCommand.Execute(null);
        DrawAt(vm, 100);
        vm.AddFrameCommand.Execute(null);
        DrawAt(vm, 0);
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.InsertInbetweensCommand.Execute(null);
        Report("three keys, playhead on the first:", vm);

        // Only the first interval is filled — the second key closes the run,
        // exactly as before. Filling both would be the change escaping its
        // scope.
        var rows = Sheet(vm).Where(r => r.Role is not null).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal([0, 50, 100, 0], rows.Select(r => Math.Round(r.Y, 4)));
    }

    /// <summary>
    /// A run that ends on a breakdown with no further extreme still fills.
    /// <see cref="SpacingChart"/>'s own rule — a run closes at the next extreme
    /// <em>or at the end of the sheet</em> — is reused precisely so this case
    /// does not silently become a no-op.
    /// </summary>
    [AvaloniaFact]
    public void ASequenceEndingOnABreakdownStillFills()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        DrawAt(vm, 0);
        vm.AddFrameCommand.Execute(null);
        DrawAt(vm, 100);
        ((Frame)vm.PaintLayer().Cels[1].Frame!).Role = FrameRole.Breakdown;
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.InsertInbetweensCommand.Execute(null);
        Report("key then breakdown, nothing after:", vm);

        var rows = Sheet(vm).Where(r => r.Role is not null).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(FrameRole.Inbetween, rows[1].Role);
        Assert.Equal(FrameRole.Breakdown, rows[2].Role);
    }

    /// <summary>
    /// The playhead sitting on the breakdown fills the run it belongs to, not
    /// the half of it that happens to start there.
    /// </summary>
    [AvaloniaFact]
    public void ThePlayheadOnABreakdownStillMeansTheWholeRun()
    {
        var vm = RunWithBreakdown();
        vm.CurrentFrameIndex = 1;
        vm.TweenCount = 1;
        vm.InsertInbetweensCommand.Execute(null);
        Report("playhead on the breakdown:", vm);

        var rows = Sheet(vm).Where(r => r.Role is not null).ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal([0, 12.5, 50, 87.5, 100], rows.Select(r => Math.Round(r.Y, 4)));
    }

    /// <summary>
    /// Q58's ladder, under Q83's answer: a chart authored on the opening
    /// extreme describes positions across the <b>whole run</b>, which is what
    /// <see cref="SpacingChart"/> has always read it as. A rung lands in
    /// whichever gap contains it; a rung that merely describes a drawing the
    /// artist already made asks for nothing.
    /// </summary>
    [AvaloniaFact]
    public void AChartSpansTheRunRatherThanTheFirstGap()
    {
        var vm = RunWithBreakdown();
        // Rungs at a quarter and three quarters of the run, plus one sitting on
        // the breakdown itself — one per gap, and one already satisfied.
        ((Frame)vm.PaintLayer().Cels[0].Frame!).Chart = [0.25, 0.5, 0.75];
        vm.InsertInbetweensCommand.Execute(null);
        Report("chart [0.25, 0.5, 0.75] across the run:", vm);

        var rows = Sheet(vm).Where(r => r.Role is not null).ToList();
        Assert.Equal(5, rows.Count);
        // Linear under a chart — the rungs are the shaping, so a rung at a
        // quarter of the run is a quarter of the way from the key to the
        // breakdown.
        Assert.Equal([0, 25, 50, 75, 100], rows.Select(r => Math.Round(r.Y, 4)));
        Assert.Equal(FrameRole.Breakdown, rows[2].Role);
    }
}
