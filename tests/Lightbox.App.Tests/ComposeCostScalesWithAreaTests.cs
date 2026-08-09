using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Where a playback tick's time actually goes: the composite, in proportion to
/// the surface it composites onto.
/// </summary>
/// <remarks>
/// <para>
/// <b>B158, and it exists because I talked myself out of the right answer.</b>
/// B156 attributed ~35 ms a tick to the CPU composite at 1080p. B157 retracted
/// that, on the strength of four captures in which the run with the *smallest*
/// compose surface — 1440×810 — spent the most time in the phase: 40.22 ms
/// against 34.83, 36.64 and 35.52 at 1920×1080. "A cost that does not fall when
/// the surface nearly halves is not paid per pixel" is sound reasoning about
/// four numbers that could not carry it.
/// </para>
/// <para>
/// <b>The comparison was confounded and I should have said so.</b> The four
/// captures came from three different sessions with different amounts of
/// stalling — the 1440×810 run reported 56 dropped frames and three occasions
/// where the clock gave up and re-based, against 4 drops and one re-base in the
/// run beside it. Its mean was over 76 ticks with a 55.82 ms worst. That is a
/// machine having a bad minute, not a surface size, and comparing means across
/// sessions with uncontrolled stalls is exactly the trap
/// <c>docs/DESIGN-performance.md</c> records: <b>the number was real and the
/// attribution was not.</b>
/// </para>
/// <para>
/// Measured properly — one process, one scene, three sizes, forty ticks each,
/// warmed first — the composite is linear in area at about 16 ms per megapixel,
/// and the handoff is nothing. So the original attribution was right, the
/// retraction was wrong, and the honest lesson is that the fix for the fix
/// needed a controlled measurement rather than more captures.
/// </para>
/// <para>
/// <b>What this asserts is the shape, not the constant.</b> Absolute
/// milliseconds vary by an order of magnitude across machines and CI runners, so
/// a test pinned to 16 ms/Mpx would be a flake generator. The two things that
/// are true everywhere and that the argument rests on: the composite grows with
/// area, and the handoff does not dominate.
/// </para>
/// </remarks>
public class ComposeCostScalesWithAreaTests(ITestOutputHelper output)
{
    private static MainViewModel Animated(int w, int h, int frames = 5)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", w, h, 12, 72, Scene.DefaultBackgroundColor, false));
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
        // Explicit: LoopPlayback is persisted in AppSettings and shared across
        // every view model in the run, so a test that turns it off elsewhere
        // would stop this one measuring anything at all — which is exactly how
        // this measured 0.00 ms on CI once.
        vm.LoopPlayback = true;
        return vm;
    }

    private static (double Compose, double Handoff) PerTick(int w, int h)
    {
        var vm = Animated(w, h);
        vm.IsPlaying = true;
        // Warm first: the opening passes rasterize, and this is about the steady
        // state a looping scene is actually in.
        for (var i = 0; i < 5; i++) vm.StepPlayback();
        vm.TickProfile.Reset();
        for (var i = 0; i < 40; i++) vm.StepPlayback();

        var phases = vm.TickProfile.Snapshot();
        var ticks = Math.Max(1, vm.TickProfile.Ticks);
        double Of(TickProfile.Phase p) =>
            phases.Single(x => x.Phase == p).TotalMs / ticks;
        return (Of(TickProfile.Phase.Compose), Of(TickProfile.Phase.Handoff));
    }

    /// <summary>
    /// The composite is what a playback tick spends itself on, and it is paid
    /// per pixel — which is the whole case for B125 and the reason a bigger
    /// canvas stutters when a small one does not.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void TheCompositeGrowsWithTheSurfaceAndTheHandoffDoesNot()
    {
        var small = PerTick(960, 540);
        var large = PerTick(1920, 1080);

        output.WriteLine($"960x540    compose {small.Compose:0.00} ms/tick, handoff {small.Handoff:0.00} ms/tick");
        output.WriteLine($"1920x1080  compose {large.Compose:0.00} ms/tick, handoff {large.Handoff:0.00} ms/tick");
        output.WriteLine($"per megapixel: {small.Compose / 0.518:0.0} and {large.Compose / 2.074:0.0} ms/Mpx");

        // Four times the area. A wide band because this is a shape assertion on
        // a shared runner, not a budget: anything from 2x up says "grows with
        // area", and the measured figure is a clean 3.9x.
        Assert.True(large.Compose > small.Compose * 2,
            $"the composite did not grow with the surface: {small.Compose:0.00} ms at 0.5 Mpx "
            + $"vs {large.Compose:0.00} ms at 2.1 Mpx — B157 retracted the per-pixel "
            + "attribution on exactly this reasoning, so if this fails the retraction was right after all");

        // The half B157 split out, and the reason the split was still worth
        // doing: it is what proves the cost is NOT here.
        Assert.True(large.Handoff < large.Compose / 4,
            $"the handoff is {large.Handoff:0.00} ms against {large.Compose:0.00} ms of composite — "
            + "it was supposed to be negligible, and a report that says otherwise "
            + "means the snapshot swap or the retired-image disposal has grown a cost");
    }
}
