using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A frame the clock dropped to catch up is not composited.
/// </summary>
/// <remarks>
/// <para>
/// <b>B162, and it is the bug six rounds of instrumentation were circling.</b>
/// <c>PlaybackClock.Pace</c> returns a frame count — 1 normally, up to
/// <c>MaxCatchUpFrames</c> when a tick is late — and the handler implemented
/// "skip 3 to catch up" as three full <c>StepPlayback</c> calls, each of which
/// writes <c>CurrentFrameIndex</c> and so runs a whole publish. <b>Dropping
/// frames cost four times as much as keeping them.</b>
/// </para>
/// <para>
/// <b>A feedback loop rather than a constant overhead, which is why it ran
/// away.</b> Late tick asks for more frames → more frames cost more → next tick
/// is later. The capture that found it: 178 ticks advanced the playhead and
/// <b>568 composites</b> came out of them, mean lateness climbing to 232 ms,
/// with 104 occasions where the clock gave up and re-based — on a five-frame
/// scene.
/// </para>
/// <para>
/// <b>And it is why the report kept saying the budget was fine.</b> The tick
/// breakdown divides by profiled ticks, one per <c>StepPlayback</c>, so it
/// reported 38 ms against an 83 ms period and called it comfortable — while a
/// single timer fire did 3.2 of them, which is 138 ms of an 83.3 ms period. The
/// per-tick number was true; the per-frame-period number was never shown.
/// </para>
/// </remarks>
public class DroppedFramesAreNotRenderedTests(ITestOutputHelper output)
{
    private static MainViewModel Animated(int frames = 8)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", 640, 360, 12, 72, Scene.DefaultBackgroundColor, false));
        vm.SmoothStrokes = false;
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        for (var i = 0; i < frames; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.CurrentFrameIndex = i;
            vm.BeginStroke(10 + i, 10, 1);
            vm.MoveStroke(200 + i, 200, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    /// <summary>Composites, counted the only way the profile allows: profiled ticks.</summary>
    private static long Composites(MainViewModel vm) =>
        vm.TickProfile.Snapshot()
            .Single(p => p.Phase == TickProfile.Phase.Compose).Calls;

    /// <summary>
    /// The fix, stated as the thing that was false before it: a tick that owes
    /// four frames composites <b>one</b>.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ATickThatOwesSeveralFramesCompositesExactlyOne(int owed)
    {
        var vm = Animated();
        vm.IsPlaying = true;
        vm.TickProfile.Reset();

        vm.HandlePlaybackTickForTests(owed);

        var composites = Composites(vm);
        output.WriteLine($"owed {owed} frame(s) -> {composites} composite(s)");

        Assert.Equal(1L, composites);
    }

    /// <summary>
    /// The playhead still moves by everything it owed. Skipping the *render* is
    /// the point; skipping the *advance* would play the scene slowly, which is
    /// the defect `Pace` exists to prevent.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ThePlayheadStillAdvancesByEveryFrameItOwed(int owed)
    {
        var vm = Animated();
        vm.IsPlaying = true;
        var before = vm.CurrentFrameIndex;

        vm.HandlePlaybackTickForTests(owed);

        Assert.Equal(before + owed, vm.CurrentFrameIndex);
    }

    /// <summary>
    /// Catching up must not cost more than not catching up. This is the
    /// arithmetic the old loop got backwards, and the reason lateness grew
    /// instead of recovering.
    /// </summary>
    [AvaloniaFact]
    public void CatchingUpCostsNoMoreThanAnOrdinaryTick()
    {
        var ordinary = Animated();
        ordinary.IsPlaying = true;
        ordinary.TickProfile.Reset();
        for (var i = 0; i < 8; i++) ordinary.HandlePlaybackTickForTests(1);

        var catchingUp = Animated();
        catchingUp.IsPlaying = true;
        catchingUp.TickProfile.Reset();
        for (var i = 0; i < 8; i++) catchingUp.HandlePlaybackTickForTests(4);

        output.WriteLine($"8 ticks at 1 frame  -> {Composites(ordinary)} composites");
        output.WriteLine($"8 ticks at 4 frames -> {Composites(catchingUp)} composites");

        Assert.Equal(Composites(ordinary), Composites(catchingUp));
    }

    /// <summary>
    /// Stopping at the end of a range still stops, even when the frame that
    /// would have ended it was one of the skipped ones — otherwise catching up
    /// could run the playhead off the end of an unlooped range.
    /// </summary>
    [AvaloniaFact]
    public void CatchingUpStillHonoursTheEndOfAnUnloopedRange()
    {
        var vm = Animated(frames: 4);
        vm.LoopPlayback = false;
        vm.CurrentFrameIndex = 0;
        vm.IsPlaying = true;

        vm.HandlePlaybackTickForTests(4);

        Assert.False(vm.IsPlaying, "playback ran past the end of an unlooped range while catching up");
        Assert.True(vm.CurrentFrameIndex <= 3, $"playhead landed on {vm.CurrentFrameIndex}");
    }
}
