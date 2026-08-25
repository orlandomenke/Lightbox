using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The GPU compositing opt-in as an artist reaches it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It began as an environment variable and became a setting, which is the
/// right move for the right reason.</b> The variable existed on the B130
/// precedent — nobody should find an unmeasured GPU path by accident. But the
/// person who has to run the measurement is the owner, and an environment
/// variable is a poor instrument for someone who wants to try a thing, play a
/// scene and look at the result.
/// </para>
/// <para>
/// The variable stays as an override, because a headless or scripted run has no
/// window to tick a box in.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class GpuCompositeToggleTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly bool? _savedOverride = GpuComposite.OptInOverride;
    private readonly GpuComposeMode _savedMode = GpuComposite.Mode;

    public new void Dispose()
    {
        GpuComposite.OptInOverride = _savedOverride;
        GpuComposite.Mode = _savedMode;
        GpuComposite.ForgetProbeForTests();
        base.Dispose();
    }

    /// <summary>
    /// Automatic unless asked otherwise — and this is a deliberate reversal of
    /// what this test used to assert.
    /// </summary>
    /// <remarks>
    /// <b>It read "off unless asked for", on the argument that an unmeasured
    /// path which may be slower is not something a document should quietly start
    /// using.</b> That argument was right and it has been answered: the leak
    /// that made the path dangerous is fixed (B179), and the same entry's
    /// capture says playback is better with it on — mean lateness 7.85 ms
    /// against 13.06, worst 96 against 1326. What survives of the argument is
    /// that it cannot be right on <em>every</em> machine, which is what
    /// Automatic is, rather than what On would be: the path is now measured on
    /// the machine it will run on instead of being avoided everywhere.
    /// </remarks>
    [AvaloniaFact]
    public void ItIsAutomaticByDefault()
    {
        var settings = new AppSettings();

        Assert.Equal(GpuComposeMode.Auto, settings.GpuCompositingMode);
    }

    /// <summary>
    /// <b>The render thread's mirror follows the setting.</b> The draw op has no
    /// route to the view model, so a toggle that updated only the settings object
    /// would appear to do nothing — the worst outcome for a switch whose entire
    /// purpose is producing a measurement.
    /// </summary>
    [AvaloniaFact]
    public void TogglingItUpdatesWhatTheRenderThreadReads()
    {
        GpuComposite.OptInOverride = null;
        var vm = VmLayers.PaperVm();

        vm.GpuCompositingMode = GpuComposeMode.On;
        Assert.Equal(GpuComposeMode.On, GpuComposite.Mode);
        Assert.True(GpuComposite.OptedIn);

        vm.GpuCompositingMode = GpuComposeMode.Off;
        Assert.Equal(GpuComposeMode.Off, GpuComposite.Mode);
        Assert.False(GpuComposite.OptedIn);
    }

    /// <summary>
    /// It survives a restart. Somebody measuring across sessions should not have
    /// to remember to switch it back on — and the same argument that made the
    /// canvas quality persist applies here.
    /// </summary>
    [AvaloniaFact]
    public void ItIsRemembered()
    {
        var settings = new AppSettings { GpuCompositingMode = GpuComposeMode.On };

        var restored = AppSettings.Deserialize(settings.Serialize());

        Assert.Equal(GpuComposeMode.On, restored.GpuCompositingMode);
    }

    /// <summary>
    /// <b>The old key is gone from what gets written, so two keys can never
    /// disagree about the same decision.</b> The cheap version of that check is
    /// to serialise and look, which is the same move the optional-settings rule
    /// asks for on the document model.
    /// </summary>
    [AvaloniaFact]
    public void ADefaultSettingsFileDoesNotMentionTheOldKey()
    {
        var json = new AppSettings().Serialize();

        output.WriteLine(json.Length > 400 ? json[..400] : json);
        Assert.DoesNotContain("\"GpuCompositing\"", json);
    }

    /// <summary>
    /// Setting it to what it already is does no work — the setter republishes the
    /// canvas, and a redundant full repaint on every Configure-window open would
    /// be a real cost for nothing.
    /// </summary>
    [AvaloniaFact]
    public void SettingItToWhatItAlreadyIsIsANoOp()
    {
        var vm = VmLayers.PaperVm();
        var publishes = 0;
        void Count(RenderSnapshot s) => publishes++;
        vm.SnapshotChanged += Count;
        try
        {
            vm.GpuCompositingMode = GpuComposeMode.Auto;   // already Auto
            Assert.Equal(0, publishes);

            vm.GpuCompositingMode = GpuComposeMode.On;     // a real change republishes
            Assert.True(publishes > 0);
        }
        finally
        {
            vm.SnapshotChanged -= Count;
            vm.GpuCompositingMode = GpuComposeMode.Auto;
        }
    }

    /// <summary>
    /// <b>B184: moving the toggle clears the composite tallies</b>, so a capture
    /// taken after a bisect describes the mode it was taken in.
    /// </summary>
    /// <remarks>
    /// The counters are process-lifetime and nothing else resets them, so every
    /// composite made while the path was off was counted as a fallback the moment
    /// it was switched on. A real capture read <c>160 did, 207 fell back</c> with
    /// no refusal behind a single one of the 207 — they were the previous
    /// playback. Losing the count of the mode you are no longer in costs nothing;
    /// the report only ever prints the one that is running.
    /// </remarks>
    [AvaloniaFact]
    public void SwitchingItOnForgetsWhatHappenedWhileItWasOff()
    {
        GpuComposite.OptInOverride = null;
        var vm = VmLayers.PaperVm();
        vm.GpuCompositingMode = GpuComposeMode.Off;

        GpuComposite.ResetCounters();
        GpuComposite.CountCompositeForTests(onGpu: false, times: 207);
        Assert.Equal(207, GpuComposite.CpuComposites);

        vm.GpuCompositingMode = GpuComposeMode.On;

        output.WriteLine($"after the toggle: {GpuComposite.GpuComposites} gpu, "
                         + $"{GpuComposite.CpuComposites} cpu");
        Assert.Equal(0, GpuComposite.CpuComposites);
        Assert.Equal(0, GpuComposite.GpuComposites);

        vm.GpuCompositingMode = GpuComposeMode.Auto;
    }
}
