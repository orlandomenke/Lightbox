using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Automatic GPU compositing: the verdict, the modes, and the migration off
/// the old checkbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these cannot do, said plainly rather than implied.</b> There is no
/// graphics context in this repository's test environment — the same reason
/// B122 shipped as an inference and the render report exists at all. So nothing
/// here asserts that <see cref="GpuComposeProbe.Run"/> measures anything, or
/// that a real card is faster. They assert the <em>decision</em>: what a given
/// measurement means, which mode wins over which, and that an install carrying
/// the old key lands where it should. The measuring is separated from the
/// deciding precisely so the deciding is reachable, which is the same shape
/// <c>GpuCompositeTests</c> already uses for the policy.
/// </para>
/// </remarks>
public class GpuComposeAutoTests
{
    private static GpuProbeResult Measured(double gpuMs, double cpuMs) =>
        new(HadContext: true, SurfaceRefused: false, gpuMs, cpuMs, 1024, 3);

    [Fact]
    public void ACardThatIsClearlyFasterIsUsed()
    {
        Assert.True(GpuComposeProbe.Decide(Measured(gpuMs: 1.0, cpuMs: 8.0)));
    }

    /// <summary>
    /// The software-rasteriser case, which is the whole reason the probe times
    /// rather than asks: `llvmpipe` provides a real GL context, so every check
    /// short of a measurement says "GPU" while every pixel is drawn on the
    /// processor.
    /// </summary>
    [Theory]
    [InlineData(4.0, 4.0)]   // parity — a software rasteriser racing itself
    [InlineData(4.0, 4.4)]   // noise either side of parity
    [InlineData(4.0, 2.0)]   // actively slower than the path it would replace
    public void ACardThatIsNotClearlyFasterIsRefused(double gpuMs, double cpuMs)
    {
        Assert.False(GpuComposeProbe.Decide(Measured(gpuMs, cpuMs)));
    }

    /// <summary>
    /// The margin exists so a machine does not flip between sessions on noise.
    /// Pinned from both sides, because a threshold nothing measures is a
    /// constant nobody may change safely.
    /// </summary>
    [Fact]
    public void TheMarginIsWhereItSaysItIs()
    {
        Assert.True(GpuComposeProbe.Decide(
            Measured(gpuMs: 1.0, cpuMs: GpuComposeProbe.RequiredSpeedup)));
        Assert.False(GpuComposeProbe.Decide(
            Measured(gpuMs: 1.0, cpuMs: GpuComposeProbe.RequiredSpeedup - 0.01)));
    }

    [Fact]
    public void NoContextIsARefusalRatherThanAnError()
    {
        Assert.False(GpuComposeProbe.Decide(
            new GpuProbeResult(HadContext: false, SurfaceRefused: false, 0, 0, 1024, 3)));
    }

    [Fact]
    public void ARefusedSurfaceIsARefusal()
    {
        Assert.False(GpuComposeProbe.Decide(
            new GpuProbeResult(HadContext: true, SurfaceRefused: true, 0, 0, 1024, 3)));
    }

    /// <summary>A probe that faulted must not be readable as a fast card.</summary>
    [Fact]
    public void AProbeWithNoTimingIsARefusal()
    {
        Assert.False(GpuComposeProbe.Decide(Measured(gpuMs: 0, cpuMs: 0)));
    }

    [Fact]
    public void RunningWithoutAContextAnswersNoRatherThanThrowing()
    {
        var result = GpuComposeProbe.Run(null);
        Assert.False(result.HadContext);
        Assert.False(GpuComposeProbe.Decide(result));
    }

    // ---- how the modes resolve ------------------------------------------------

    private static void WithMode(GpuComposeMode mode, Action body)
    {
        var previousMode = GpuComposite.Mode;
        var previousOverride = GpuComposite.OptInOverride;
        GpuComposite.OptInOverride = null;
        GpuComposite.Mode = mode;
        GpuComposite.ForgetProbeForTests();
        try { body(); }
        finally
        {
            GpuComposite.Mode = previousMode;
            GpuComposite.ForgetProbeForTests();
            GpuComposite.OptInOverride = previousOverride;
        }
    }

    /// <summary>
    /// Auto before the probe has run is the processor. The frames between
    /// launch and the first render are few, and being briefly right beats being
    /// briefly fast.
    /// </summary>
    [Fact]
    public void AutomaticBeforeTheProbeStaysOnTheProcessor()
    {
        WithMode(GpuComposeMode.Auto, () => Assert.False(GpuComposite.OptedIn));
    }

    [Fact]
    public void AutomaticFollowsTheProbeBothWays()
    {
        WithMode(GpuComposeMode.Auto, () =>
        {
            GpuComposite.NoteProbe(Measured(gpuMs: 1.0, cpuMs: 8.0));
            Assert.True(GpuComposite.OptedIn);

            GpuComposite.NoteProbe(Measured(gpuMs: 4.0, cpuMs: 4.0));
            Assert.False(GpuComposite.OptedIn);
        });
    }

    /// <summary>
    /// An explicit choice is a choice: the probe does not get to overrule it in
    /// either direction.
    /// </summary>
    [Fact]
    public void AnExplicitChoiceOutranksTheProbe()
    {
        WithMode(GpuComposeMode.On, () =>
        {
            GpuComposite.NoteProbe(Measured(gpuMs: 4.0, cpuMs: 4.0));
            Assert.True(GpuComposite.OptedIn);
        });
        WithMode(GpuComposeMode.Off, () =>
        {
            GpuComposite.NoteProbe(Measured(gpuMs: 1.0, cpuMs: 8.0));
            Assert.False(GpuComposite.OptedIn);
        });
    }

    /// <summary>
    /// B184: a tally that spans a change of answer reads the old mode's
    /// composites as the new one's fallbacks, which is the exact misreading a
    /// discriminating experiment cannot survive.
    /// </summary>
    [Fact]
    public void TheVerdictLandingClearsTheCounters()
    {
        WithMode(GpuComposeMode.Auto, () =>
        {
            GpuComposite.CountCompositeForTests(onGpu: false, times: 7);
            Assert.Equal(7, GpuComposite.CpuComposites);
            GpuComposite.NoteProbe(Measured(gpuMs: 1.0, cpuMs: 8.0));
            Assert.Equal(0, GpuComposite.CpuComposites);
        });
    }

    /// <summary>A verdict that does not change the answer must not clear them.</summary>
    [Fact]
    public void ARepeatedVerdictLeavesTheCountersAlone()
    {
        WithMode(GpuComposeMode.Auto, () =>
        {
            GpuComposite.NoteProbe(Measured(gpuMs: 1.0, cpuMs: 8.0));
            GpuComposite.CountCompositeForTests(onGpu: true, times: 4);
            GpuComposite.NoteProbe(Measured(gpuMs: 1.0, cpuMs: 9.0));
            Assert.Equal(4, GpuComposite.GpuComposites);
        });
    }

    // ---- the setting, and the install that predates it ------------------------

    [Fact]
    public void AFreshInstallIsAutomatic()
    {
        Assert.Equal(GpuComposeMode.Auto, new AppSettings().GpuCompositingMode);
    }

    /// <summary>
    /// The old key was written at false by every install whether or not anyone
    /// chose it, so only true carries a decision. Reading false as one would
    /// pin every existing install to the processor for good — the exact
    /// outcome defaulting to Automatic exists to prevent.
    /// </summary>
    [Fact]
    public void AnInstallThatTickedTheOldBoxIsOn()
    {
        var settings = AppSettings.Deserialize("""{"GpuCompositing": true}""");
        Assert.Equal(GpuComposeMode.On, settings.GpuCompositingMode);
    }

    [Fact]
    public void AnInstallThatNeverTickedItIsAutomaticRatherThanOff()
    {
        var settings = AppSettings.Deserialize("""{"GpuCompositing": false}""");
        Assert.Equal(GpuComposeMode.Auto, settings.GpuCompositingMode);
    }

    [Fact]
    public void AnInstallWithNoSuchKeyIsAutomatic()
    {
        Assert.Equal(GpuComposeMode.Auto, AppSettings.Deserialize("{}").GpuCompositingMode);
    }

    /// <summary>An explicit new-key choice is not overwritten by the old one.</summary>
    [Fact]
    public void TheNewKeyWinsOverTheOld()
    {
        var settings = AppSettings.Deserialize(
            """{"GpuCompositing": true, "GpuCompositingMode": "Off"}""");
        Assert.Equal(GpuComposeMode.Off, settings.GpuCompositingMode);
    }

    /// <summary>
    /// The old key is cleared as it is read, so the next save drops it rather
    /// than leaving two keys that can disagree.
    /// </summary>
    [Fact]
    public void ASettingsFileWritesNoLegacyGpuKey()
    {
        Assert.DoesNotContain("\"GpuCompositing\"", new AppSettings().Serialize());
        Assert.DoesNotContain(
            "\"GpuCompositing\"",
            AppSettings.Deserialize("""{"GpuCompositing": true}""").Serialize());
    }

    /// <summary>Readable in the file an artist might open, rather than 0, 1, 2.</summary>
    [Fact]
    public void TheModeIsWrittenAsAWord()
    {
        Assert.Contains("\"GpuCompositingMode\": \"Auto\"", new AppSettings().Serialize());
    }

    /// <summary>A round trip keeps the choice.</summary>
    [Theory]
    [InlineData(GpuComposeMode.Auto)]
    [InlineData(GpuComposeMode.On)]
    [InlineData(GpuComposeMode.Off)]
    public void TheModeSurvivesASaveAndLoad(GpuComposeMode mode)
    {
        var settings = new AppSettings { GpuCompositingMode = mode };
        Assert.Equal(mode, AppSettings.Deserialize(settings.Serialize()).GpuCompositingMode);
    }
}
