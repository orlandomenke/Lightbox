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
    private readonly bool _savedSetting = GpuComposite.SettingEnabled;

    public new void Dispose()
    {
        GpuComposite.OptInOverride = _savedOverride;
        GpuComposite.SettingEnabled = _savedSetting;
        base.Dispose();
    }

    /// <summary>
    /// Off unless asked for. An unmeasured path that may be slower is not
    /// something a document should quietly start using.
    /// </summary>
    [AvaloniaFact]
    public void ItIsOffByDefault()
    {
        var settings = new AppSettings();

        Assert.False(settings.GpuCompositing);
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

        vm.GpuCompositing = true;
        Assert.True(GpuComposite.SettingEnabled);
        Assert.True(GpuComposite.OptedIn);

        vm.GpuCompositing = false;
        Assert.False(GpuComposite.SettingEnabled);
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
        var settings = new AppSettings { GpuCompositing = true };

        var restored = AppSettings.Deserialize(settings.Serialize());

        Assert.True(restored.GpuCompositing);
    }

    /// <summary>
    /// <b>A document that never touches it writes no key.</b> "Optional means
    /// absent, not disabled" — and the cheap version of that check is to
    /// serialise something that does not use the setting and look.
    /// </summary>
    [AvaloniaFact]
    public void ADefaultSettingsFileDoesNotMentionIt()
    {
        var json = new AppSettings().Serialize();

        output.WriteLine(json.Length > 400 ? json[..400] : json);
        Assert.DoesNotContain("\"gpuCompositing\": true", json);
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
            vm.GpuCompositing = false;   // already false
            Assert.Equal(0, publishes);

            vm.GpuCompositing = true;    // a real change republishes
            Assert.True(publishes > 0);
        }
        finally
        {
            vm.SnapshotChanged -= Count;
            vm.GpuCompositing = false;
        }
    }
}
