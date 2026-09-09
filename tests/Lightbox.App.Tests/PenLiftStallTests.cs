using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The gap the artist feels when the pen lifts: what the commit path costs on
/// the UI thread, and how many whole-frame renders it commissions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raised from the owner's capture of 2026-09-09 23:07</b> on a 3840x2160
/// document — <c>building each frame</c> median <b>1.05 ms</b>, worst
/// <b>2103.44 ms</b>, of which <b>98% was "describing it"</b> with <b>3
/// frame-cache misses inside it</b> and — the load-bearing detail — <b>0 points
/// and 0 dabs under the pen</b>. The report's own verdict: <em>"NO stroke was in
/// flight, so this is not a cost that grows with the mark. Look at what happens
/// between strokes: opening, committing, clearing."</em> The eight recorded
/// misses are all <c>3840x2160@1</c> and are asked by <c>ThumbSource</c> and
/// <c>Materialize</c>.
/// </para>
/// <para>
/// <b>B332 is marked fixed and this is its mechanism, still live.</b> The fix
/// warms the frames the next stroke needs during the idle
/// (<c>WarmWhatTheNextStrokeWillNeed</c>) — but it warms only
/// <c>CurrentFrameIndex</c>, bails while <c>_prewarm.IsBusy</c>, and cannot
/// cover a lookup at a cel index the canvas never publishes.
/// </para>
/// <para>
/// <b>Why the existing guard did not catch it, which is the point of this
/// file.</b> <c>LargeCanvasPerformanceTests.FourK_WholeStrokeIncludingCommit_HasNoPenLiftStall</c>
/// asserts the <b>median of six consecutive commits</b>, and <c>MedianMs</c>
/// runs the action <b>once untimed first</b> as a warm-up. The fault is a cold
/// lookup on the <em>first</em> commit after an idle, so it lands squarely in
/// the untimed run and is then absent from all six timed ones. A median over a
/// warm cache cannot see a once-per-idle stall — which is why every
/// drawing-latency fault so far has been found in a capture while the suite
/// stayed green.
/// </para>
/// <para>
/// <b>These are deliberately measurements before they are budgets.</b> Each one
/// prints both numbers and asserts a bound set from what the broken and the
/// warm paths actually measure here, never from a figure that looked safe.
/// 1920x1080 rather than the owner's 4K on purpose: rendering eight 33 MB
/// frames in this suite killed CI twice with MSB4166, which is the reason
/// <c>FrameCacheMissCostTests</c> is at this size too. The shape of the fault
/// does not need 4K; the cost of one full-document render does not either.
/// </para>
/// </remarks>
[Trait("Category", "Performance")]
[Collection("LargeCanvasPerformance")]
public class PenLiftStallTests(ITestOutputHelper output)
{
    // Half the owner's document per side, a quarter of the pixels — the size
    // FrameCacheMissCostTests settled on after CI died on 4K renders.
    private const int W = 1920;
    private const int H = 1080;

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("stall", W, H, 12, 180, "#808080", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#204080";
        vm.BrushSize = 120;
        vm.BrushHardness = 0.6;
        vm.BrushOpacity = 0.9;
        vm.BrushFlow = 0.8;
        vm.BrushGranulation = 0;
        vm.BrushWetEdge = 0;
        vm.BrushScatter = 0;
        return vm;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>One stroke, drawn and released, timed as a whole.</summary>
    private static double OneStrokeMs(MainViewModel vm, double y)
    {
        var sw = Stopwatch.StartNew();
        vm.BeginStroke(300, y, 1);
        for (var i = 1; i <= 12; i++)
        {
            vm.MoveStroke(300 + i * 60, y + i * 4, 0.9);
            Pump();
        }
        vm.EndStroke();
        Pump();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// <b>The first commit after an idle must not cost more than the ones after
    /// it.</b> This is the number the median in the existing guard throws away.
    /// </summary>
    /// <remarks>
    /// A ratio rather than an absolute, because a ratio survives a slow shared
    /// runner: the question is not "is the first commit fast" but "is the first
    /// commit like the others". The absolutes are printed so a capture can be
    /// compared against them by hand.
    /// </remarks>
    [AvaloniaFact]
    public void TheFirstCommitAfterAnIdleCostsWhatTheNextOnesDo()
    {
        var vm = Vm();

        // No warm-up. That is the whole point of this test.
        var first = OneStrokeMs(vm, 300);

        var rest = new List<double>();
        for (var i = 1; i <= 5; i++) rest.Add(OneStrokeMs(vm, 300 + i * 40));
        rest.Sort();
        var typical = rest[rest.Count / 2];

        output.WriteLine($"first commit {first:0.00} ms");
        output.WriteLine($"typical of the next 5 {typical:0.00} ms (min {rest[0]:0.00}, max {rest[^1]:0.00})");
        output.WriteLine($"ratio {first / Math.Max(typical, 0.01):0.0}x");

        // **Measured before it was set, and 8x would have been decoration.**
        // At this size the first commit is 1.4x the typical one, and the typical
        // band itself spans 247-359 ms — so 3x is above the noise and well under
        // anything an artist would feel. The capture's ratio is 2103 / 1.05 =
        // 2003x, which is what this shape looks like when it is real.
        Assert.True(
            first < typical * 3,
            $"the first commit cost {first:0.00} ms against a typical {typical:0.00} ms "
            + $"({first / Math.Max(typical, 0.01):0.0}x) — a stall on the first stroke after an idle");
    }

    /// <summary>
    /// <b>Committing a stroke must not commission a fresh whole-frame render.</b>
    /// </summary>
    /// <remarks>
    /// The canvas has just composited this frame, so its pixels exist. A miss
    /// here means something on the commit path asked the frame cache a question
    /// it had not been asked before — a different cel index, a different size —
    /// and <c>FrameBitmapCache.Get</c> answers that by rendering the whole frame
    /// synchronously on the calling thread, which at pen-lift is the UI thread.
    /// That is the mechanism, stated as a count so it cannot go flaky.
    /// </remarks>
    [AvaloniaFact]
    public void CommittingAStrokeCommissionsNoWholeFrameRender()
    {
        var vm = Vm();
        // One stroke first, so nothing here is measuring a cold document.
        OneStrokeMs(vm, 300);

        var before = vm.FrameCacheTraffic;
        OneStrokeMs(vm, 420);
        var after = vm.FrameCacheTraffic;

        var misses = after.Misses - before.Misses;
        output.WriteLine($"misses across one stroke + commit: {misses}");
        output.WriteLine($"hits {after.Hits - before.Hits}, bytes held {after.Bytes / (1024 * 1024)} MB");
        output.WriteLine($"layer thumbnails rasterized: {vm.LayerThumbRenders}");

        Assert.Equal(0, misses);
    }

    /// <summary>
    /// <b>A thumbnail must never commission a document-sized render.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is B364 stated exactly, and the two earlier versions of this test
    /// were both wrong in ways worth recording.</b> Counting misses cannot tell
    /// the defect from the fix: a thumbnail rendered at thumbnail scale is still
    /// a miss, so the count is unchanged while the cost falls by two orders of
    /// magnitude. Counting bytes held is worse — it fails on a correct build,
    /// because <c>AddFrameCommand</c> moves the playhead onto each new drawing
    /// and the canvas publish caches it at full size through
    /// <c>Materialize</c>. <b>That is the frame cache doing its job</b>, and a
    /// guard that forbids it forbids instant scrubbing.
    /// </para>
    /// <para>
    /// So the assertion is on the asker and the scale, which is the defect and
    /// nothing else: the cache records who asked (<c>CallerMemberName</c>, added
    /// under B332 for exactly this), so a lookup by <c>ThumbSource</c> at scale
    /// 1.0 is the bug and is unambiguous. It cannot go flaky and it cannot pass
    /// on a build that reintroduces the fault by another route.
    /// </para>
    /// <para>
    /// Before the fix every <c>ThumbSource</c> miss was at <c>@1</c> — 7.9 MB
    /// and ~37 ms here, 33 MB and ~700 ms at the owner's 4K, once per drawing.
    /// After it they are at <c>@0.1333</c>, which is 256 px on the longest side.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AThumbnailNeverCommissionsADocumentSizedRender()
    {
        var vm = Vm();
        // Several drawings, so the thumbnail refresh has more than the one on
        // screen to make a picture of — which is the whole point: the canvas
        // publishes one frame and the timeline asks about all of them.
        for (var i = 0; i < 3; i++) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        Pump();
        OneStrokeMs(vm, 300);

        var asks = vm.FrameCacheMissLog.ToList();
        output.WriteLine($"the last {asks.Count} misses, by asker and scale:");
        foreach (var m in asks)
        {
            output.WriteLine($"    {m.Width}x{m.Height}@{m.Scale:0.####} cel {m.Cel}  {m.Why}");
        }

        var thumbAsks = asks.Where(m => m.Why.Contains("ThumbSource", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(thumbAsks);

        var atDocumentSize = thumbAsks.Where(m => m.Scale >= 1.0).ToList();
        output.WriteLine(
            $"thumbnail lookups: {thumbAsks.Count}, of those at document scale: {atDocumentSize.Count}");
        output.WriteLine(
            $"a thumbnail source is at most {256} px on its longest side, so "
            + $"{W}x{H} renders at {PenLiftScaleFor(W, H):0.####}");

        Assert.Empty(atDocumentSize);
    }

    /// <summary>What <c>ThumbSource</c> should be asking at, restated here.</summary>
    /// <remarks>
    /// Deliberately a second implementation rather than a call into the one
    /// under test: a test that computes the expected value with the code it is
    /// checking asserts only that the code equals itself.
    /// </remarks>
    private static double PenLiftScaleFor(int width, int height)
    {
        var forLongSide = 256.0 / Math.Max(width, height);
        var forShortSide = 64.0 / Math.Min(width, height);
        return Math.Min(1.0, Math.Max(forLongSide, forShortSide));
    }

    /// <summary>
    /// What one whole-frame render costs here, recorded rather than asserted.
    /// </summary>
    /// <remarks>
    /// The number that turns a miss count into a felt delay, and the one to
    /// multiply when reading a capture: the owner's document is four times these
    /// pixels. Printed with no bound because it is hardware, not behaviour —
    /// asserting it would be asserting the runner.
    /// </remarks>
    [AvaloniaFact]
    public void WhatAWholeFrameRenderCosts()
    {
        var vm = Vm();
        OneStrokeMs(vm, 300);
        var frame = vm.Doc.Scene.Layers[^1].Cels[0].Frame!;

        using var cold = new Lightbox.Raster.FrameBitmapCache();
        var sw = Stopwatch.StartNew();
        cold.Get(frame, W, H, celIndex: 0);
        sw.Stop();

        output.WriteLine($"one {W}x{H} whole-frame render: {sw.Elapsed.TotalMilliseconds:0.00} ms");
        output.WriteLine($"the owner's 3840x2160 is 4x these pixels");
        Assert.True(cold.Misses == 1, "the cold lookup should have been the miss");
    }
}
