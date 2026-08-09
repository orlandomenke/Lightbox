using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// What a playback tick is allowed to do (B152).
/// </summary>
/// <remarks>
/// <para>
/// <b>The tick handler runs inside the frame period.</b> <c>StepPlayback</c>
/// advances the playhead, which raises <c>OnCurrentFrameIndexChanged</c>, which
/// publishes — all synchronously, inside <c>PlaybackClock</c>'s timer callback.
/// Anything expensive in that chain does not slow playback down; it makes the
/// clock late, and a late clock drops frames to stay in time. The artist sees
/// the dropping, not the cost.
/// </para>
/// <para>
/// So this guards the rule rather than a duration: <b>work an artist cannot see
/// while frames are flipping must not happen per tick.</b> The first of those to
/// be caught was the layer thumbnails, measured at 159 ms of mean tick lateness
/// on a sixteen-core machine — each tick rasterizing a full-resolution frame per
/// layer to make a 44×26 picture behind a moving playhead.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class PlaybackTickCostTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A short scene with a drawing on every frame, like a real one.</summary>
    private static MainViewModel Animated(int frames = 8)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;

        for (var i = 0; i < frames; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.CurrentFrameIndex = i;
            vm.BeginStroke(10 + i, 10, 1);
            vm.MoveStroke(60 + i, 60, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    /// <summary>
    /// <b>B152.</b> During playback the exposed frame changes on every tick, so
    /// the thumbnail's "is this still the same frame" check missed every time
    /// and every tick rendered one per layer. Each render is a full-resolution
    /// frame — from the stroke record, because the compositor reads tiles while
    /// playing and never fills the bitmap cache these read.
    /// </summary>
    [AvaloniaFact]
    public void PlaybackDoesNotRenderLayerThumbnailsOnEveryTick()
    {
        var vm = Animated();
        vm.IsPlaying = true;
        var before = vm.LayerThumbRenders;

        for (var i = 0; i < 24; i++) vm.StepPlayback();

        var during = vm.LayerThumbRenders - before;
        output.WriteLine($"24 playback ticks -> {during} layer thumbnail render(s)");
        Assert.Equal(0, during);
    }

    /// <summary>
    /// Skipped is not abandoned: the thumbnails are showing whatever frame
    /// playback started on, so stopping has to catch them up. One sweep on the
    /// stop, rather than one per tick.
    /// </summary>
    [AvaloniaFact]
    public void StoppingCatchesTheThumbnailsUp()
    {
        var vm = Animated();
        vm.IsPlaying = true;
        for (var i = 0; i < 6; i++) vm.StepPlayback();
        var before = vm.LayerThumbRenders;

        vm.IsPlaying = false;

        var onStop = vm.LayerThumbRenders - before;
        output.WriteLine($"stopping rendered {onStop} thumbnail(s) to catch up");
        Assert.True(onStop > 0, "the thumbnails were left showing the frame playback started on");
        foreach (var row in vm.LayerRows)
        {
            Assert.NotNull(row.ThumbFrameId);
        }
    }

    /// <summary>
    /// Stepping by hand is not playback, and an artist walking the timeline one
    /// frame at a time <em>is</em> looking at the thumbnails.
    /// </summary>
    [AvaloniaFact]
    public void SteppingByHandStillUpdatesTheThumbnails()
    {
        var vm = Animated();
        var before = vm.LayerThumbRenders;

        vm.CurrentFrameIndex = 3;

        output.WriteLine($"one manual step -> {vm.LayerThumbRenders - before} render(s)");
        Assert.True(vm.LayerThumbRenders > before);
    }

    /// <summary>
    /// What a tick actually costs, on a document shaped like the one that was
    /// reported (B152).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This measures the tick, not the frame rate, and the distinction is the
    /// honest part.</b> The headless dispatcher does not deliver timer ticks in
    /// real time, so anything claiming to be "fps" here would be measuring the
    /// harness — which is why <c>PlaybackClock</c>'s own tests are arithmetic.
    /// What <em>is</em> measurable is how long the handler takes, and that is the
    /// quantity the bug was denominated in: the reported machine showed 159 ms of
    /// mean lateness, which is the handler overrunning the frame period.
    /// </para>
    /// <para>
    /// A budget in absolute milliseconds rather than a ratio, because there is a
    /// real number to hold it against: at 12 fps a frame period is 83 ms, and a
    /// tick that costs a large fraction of that is late whatever else is true.
    /// Deliberately loose — this container is slow and the charter's budgets
    /// catch order-of-magnitude regressions, not drift.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void APlaybackTickCostsFarLessThanAFramePeriod()
    {
        var vm = Animated(frames: 12);
        // A second layer, because the cost this guards is per layer.
        vm.AddPaintedLayerCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 70, 1);
        vm.EndStroke();

        vm.IsPlaying = true;
        for (var i = 0; i < 6; i++) vm.StepPlayback();   // warm whatever caches

        var times = new List<double>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 24; i++)
        {
            clock.Restart();
            vm.StepPlayback();
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var median = times[times.Count / 2];
        var worst = times[^1];

        // Both numbers, always: a median that looks fine beside a worst case
        // that does not is a clock that is late one tick in ten, which is what
        // an artist sees as a stutter rather than as slowness.
        output.WriteLine(
            $"tick cost over 24: median {median:F2} ms, worst {worst:F2} ms "
            + $"(a 12 fps frame period is 83 ms)");

        Assert.True(median < 40, $"a playback tick cost {median:F2} ms at the median");
    }
}
