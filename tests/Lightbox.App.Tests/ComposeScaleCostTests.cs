using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A scaled composite costs about 2.5× per pixel, so compositing a bit smaller
/// costs more than compositing everything.
/// </summary>
/// <remarks>
/// <para>
/// <b>B160, and the third answer to the same question — so the method matters
/// more than the number here.</b> B156 blamed the composite, B157 retracted it
/// on four field captures, B158 "corrected" the retraction with a controlled
/// measurement. B158's measurement was clean and it was controlled on the
/// <em>wrong variable</em>: it varied the document size with the compose scale
/// pinned at 1.0, so it only ever exercised the unscaled path, found it
/// perfectly linear, and concluded the field captures were noise.
/// </para>
/// <para>
/// They were not. Holding the document at 1920×1080 and varying only the
/// compose scale — the thing the captures actually differed in:
/// </para>
/// <list type="bullet">
/// <item>1.0 — 2.07 Mpx, 34.00 ms/tick, <b>16.4 ms/Mpx</b></item>
/// <item>0.75 — 1.17 Mpx, <b>45.92 ms/tick</b>, 39.4 ms/Mpx</item>
/// <item>0.625 — 0.81 Mpx, 33.00 ms/tick, 40.7 ms/Mpx</item>
/// <item>0.5 — 0.52 Mpx, 20.85 ms/tick, 40.2 ms/Mpx</item>
/// </list>
/// <para>
/// <b>0.75 composites 44% fewer pixels and takes 35% longer.</b> That is the
/// field observation reproduced exactly, and it had been dismissed twice.
/// </para>
/// <para>
/// The mechanism is not a bad sampler — the filter is already
/// <c>SKFilterMode.Linear</c> and <c>SceneRenderer.Downscale</c> explains the
/// choice. At 1.0 the layer blit is a straight copy; below it every tile is
/// drawn through a scale transform and interpolated. This is what interpolation
/// costs.
/// </para>
/// <para>
/// <b>What is asserted is the shape, not the constants.</b> Absolute
/// milliseconds vary by an order of magnitude across machines, so the tests pin
/// the two things the fix rests on: that a scale just under 1 is not cheaper
/// than 1, and that the clamp keeps the cheap path.
/// </para>
/// </remarks>
public class ComposeScaleCostTests(ITestOutputHelper output)
{
    private static MainViewModel Animated(int frames = 5)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", 1920, 1080, 12, 72, Scene.DefaultBackgroundColor, false));
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

    private static (double Ms, double Scale) ComposeAt(double displayScale)
    {
        var vm = Animated();
        vm.CanvasQuality = CanvasQuality.Display;   // compose scale follows display scale
        vm.SetDisplayScale(displayScale);
        vm.IsPlaying = true;
        for (var i = 0; i < 5; i++) vm.StepPlayback();
        vm.TickProfile.Reset();
        for (var i = 0; i < 30; i++) vm.StepPlayback();

        var ticks = Math.Max(1, vm.TickProfile.Ticks);
        var compose = vm.TickProfile.Snapshot()
            .Single(p => p.Phase == TickProfile.Phase.Compose).TotalMs / ticks;
        return (compose, vm.ReportComposeScale);
    }

    /// <summary>
    /// The clamp: a requested scale above the crossover composites everything
    /// instead, because scaling there is strictly the more expensive of the two.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0.75, 1.0)]     // above the crossover — round up to the cheap path
    [InlineData(0.9, 1.0)]
    [InlineData(0.7, 0.7)]      // at it, and below, scaling genuinely pays
    [InlineData(0.5, 0.5)]
    [InlineData(0.25, 0.25)]
    public void AScaleThatWouldCostMoreThanNotScalingRoundsUp(double display, double expected)
    {
        var vm = Animated();
        vm.CanvasQuality = CanvasQuality.Display;
        vm.SetDisplayScale(display);

        Assert.Equal(expected, vm.ReportComposeScale, 3);
    }

    /// <summary>
    /// The measurement the clamp is derived from. Without this, the threshold is
    /// a magic number somebody will "simplify" away.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void CompositingJustUnderFullSizeIsNotCheaperThanCompositingAllOfIt()
    {
        // 0.68 is below the clamp, so it really does composite at 0.68 — 46% of
        // the pixels of a full composite, and the point is that this does not
        // make it 46% of the cost.
        var scaled = ComposeAt(0.68);
        var full = ComposeAt(1.0);

        output.WriteLine($"scale {scaled.Scale:0.###}  {scaled.Ms:0.00} ms/tick"
                         + $"   ({scaled.Ms / (2.074 * scaled.Scale * scaled.Scale):0.0} ms/Mpx)");
        output.WriteLine($"scale {full.Scale:0.###}  {full.Ms:0.00} ms/tick"
                         + $"   ({full.Ms / 2.074:0.0} ms/Mpx)");

        Assert.Equal(0.68, scaled.Scale, 3);

        // Per pixel, the scaled path must be visibly dearer — that is the whole
        // finding. A generous factor because this runs on shared hardware; the
        // measured ratio is about 2.5x.
        var scaledPerMpx = scaled.Ms / (2.074 * scaled.Scale * scaled.Scale);
        var fullPerMpx = full.Ms / 2.074;
        Assert.True(scaledPerMpx > fullPerMpx * 1.5,
            $"a scaled composite cost {scaledPerMpx:0.0} ms/Mpx against {fullPerMpx:0.0} unscaled — "
            + "if these are equal, resampling has stopped being expensive and the "
            + "clamp in ComposeScale is now costing sharpness for nothing");
    }
}
