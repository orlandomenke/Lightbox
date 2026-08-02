using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// When the app turns the canvas quality down for you, and when it must not.
/// </summary>
/// <remarks>
/// <para>
/// Two things can decide the canvas needs help. The graphics backend coming
/// back as software is a well-founded guess made before anything has been
/// measured: no GPU context means presenting the frame will dominate. The
/// measurement is the general case, and it is the one that reaches the machine
/// the guess cannot see — an integrated GPU on a 4K canvas with a dozen onion
/// ghosts is slower than a software rasteriser on a sprite sheet, and it used
/// to get a label saying everything was fine.
/// </para>
/// <para>
/// Both obey the same three rules, which is most of what is asserted here:
/// once per session, only over a default, and never silently.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class CanvasReliefTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null);

    /// <summary>
    /// Feed the monitor until it has settled, at a cost per frame.
    /// </summary>
    /// <remarks>
    /// Both channels, because headroom is the worse of the two and a monitor
    /// with frames but no publishes has not settled.
    /// </remarks>
    private static void Measure(MainViewModel vm, double msPerFrame, int frames = 15)
    {
        for (var i = 0; i < frames; i++)
        {
            vm.Performance.RecordPublish(msPerFrame);
            vm.RecordFrameTime(msPerFrame);
        }
    }

    // ---- the measured path -------------------------------------------------------

    [AvaloniaFact]
    public void ACanvasThatCannotKeepUpGetsItsQualityTurnedDown()
    {
        // 90 ms a frame is about 11 fps. Nothing about that is a transient.
        var vm = Vm();

        Measure(vm, 90);

        Assert.Equal(CanvasQuality.Half, vm.CanvasQuality);
        Assert.Contains("ms a frame", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ACanvasThatIsKeepingUpIsLeftAlone()
    {
        var vm = Vm();

        Measure(vm, 3);

        Assert.Equal(CanvasQuality.Display, vm.CanvasQuality);
    }

    [AvaloniaFact]
    public void ASlowStartIsNotEnoughToActOn()
    {
        // The first repaints of a session pay for JIT, the first surfaces and a
        // cold frame cache. Acting on three samples would turn the quality down
        // on a machine that is fine.
        var vm = Vm();

        Measure(vm, 200, frames: 3);

        Assert.Equal(CanvasQuality.Display, vm.CanvasQuality);
    }

    [AvaloniaFact]
    public void ASoftwareRendererGetsHelpSooner()
    {
        // 45 ms a frame is about 22 fps — uncomfortable rather than hopeless,
        // and it lands between the two thresholds on purpose. A GPU machine
        // keeps its quality and gets advice, because the likelier fix there is
        // something about the document. A software renderer has no other lever,
        // so it gets the one there is.
        var gpu = Vm();
        try
        {
            Rendering.CanvasControl.ForceBackendForTests(software: false);
            Measure(gpu, 45);
            Assert.Equal(CanvasQuality.Display, gpu.CanvasQuality);

            var cpu = Vm();
            Rendering.CanvasControl.ForceBackendForTests(software: true);
            Measure(cpu, 45);
            Assert.Equal(CanvasQuality.Half, cpu.CanvasQuality);
        }
        finally
        {
            Rendering.CanvasControl.ForceBackendForTests(null);
        }
    }

    // ---- the three rules ------------------------------------------------------------

    [AvaloniaFact]
    public void AChosenQualityIsNeverRevised()
    {
        // However slow the machine. The whole point of a preference is that it
        // belongs to the person.
        var vm = Vm();
        vm.ChooseCanvasQuality(CanvasQuality.Full);

        Measure(vm, 400);

        Assert.Equal(CanvasQuality.Full, vm.CanvasQuality);
    }

    [AvaloniaFact]
    public void ChoosingTheDefaultOnPurposeIsStillAChoice()
    {
        // The case the other rules cannot express. Display is what the app
        // starts on, so "only over a default" would let this through — but
        // somebody who went to Configure and picked Display has said what they
        // want, and it happens to be the default.
        var vm = Vm();
        vm.ChooseCanvasQuality(CanvasQuality.Display);

        Measure(vm, 400);

        Assert.Equal(CanvasQuality.Display, vm.CanvasQuality);
    }

    [AvaloniaFact]
    public void ItHappensOnceAndThenLeavesTheArtistAlone()
    {
        // Turn it down, let the artist put it back, and it must stay back —
        // otherwise the setting fights them on every frame.
        var vm = Vm();
        Measure(vm, 90);
        Assert.Equal(CanvasQuality.Half, vm.CanvasQuality);

        vm.ChooseCanvasQuality(CanvasQuality.Display);
        Measure(vm, 90);

        Assert.Equal(CanvasQuality.Display, vm.CanvasQuality);
    }

    [AvaloniaFact]
    public void ItSaysSo()
    {
        // A canvas that silently got softer is a bug report.
        var vm = Vm();

        Measure(vm, 90);

        Assert.Contains("Half", vm.AiStatus);
        Assert.Contains("exports", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ItDoesNotAnnounceALoweringThatDidNotHappen()
    {
        // Half restored from last session's settings, then the app opens on a
        // software machine. Nothing was lowered, so saying so would be a lie
        // about a change the artist did not make — and the quality they are
        // already using is the one being recommended.
        var vm = Vm();
        vm.CanvasQuality = CanvasQuality.Half;   // as loading settings does: not a choice
        var before = vm.AiStatus;
        try
        {
            Rendering.CanvasControl.ForceBackendForTests(software: true);

            vm.NoteGraphicsBackend();

            Assert.Equal(before, vm.AiStatus);
        }
        finally
        {
            Rendering.CanvasControl.ForceBackendForTests(null);
        }
    }

    [AvaloniaFact]
    public void TheBackendPathAndTheMeasuredPathDoNotBothFire()
    {
        // They share one lever, so the second one to notice must find it
        // already pulled rather than announcing it again.
        var vm = Vm();
        try
        {
            Rendering.CanvasControl.ForceBackendForTests(software: true);
            vm.NoteGraphicsBackend();
            Assert.Equal(CanvasQuality.Half, vm.CanvasQuality);
            var afterBackend = vm.AiStatus;

            Measure(vm, 400);

            Assert.Equal(afterBackend, vm.AiStatus);
        }
        finally
        {
            Rendering.CanvasControl.ForceBackendForTests(null);
        }
    }
}
