using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// A soft brush's ceiling is applied under the pen, not by a pass that lands
/// afterwards (B293).
/// </summary>
/// <remarks>
/// <para>
/// <b>The entry's title is the assertion, and it is about <em>when</em> rather
/// than <em>what</em>.</b> `LiveMatchesCommittedTests` already holds that the
/// preview and the commit reach the same pixels; it reaches them by pumping the
/// dispatcher eight times so every queued pass has landed. That is the right
/// question for correctness and the wrong one here. What an artist feels is the
/// mark converging behind the hand, and a test that waits for the convergence
/// cannot see it.
/// </para>
/// <para>
/// <b>So this counts passes instead of pixels.</b> For a brush whose only
/// post-process is the ceiling, the live path now caps in place, band-local, on
/// the thread that already holds both buffers - so the count is <b>zero</b>, and
/// there is nothing left to converge. The measurement that prompted it: one
/// pass cost <b>59.63 ms</b> at 800 events on a 4K document, against 0.02 ms for
/// the in-place cap, and the pen delivers a move every 8.9 ms.
/// </para>
/// <para>
/// <b>Pencil is the control, and it is not a formality.</b> An assertion that
/// no pass ran would pass just as well on a build where the runner was never
/// wired, or where the brush failed to need one - the shape of mistake
/// <c>.claude/skills/brush-measurement</c> exists for. Pencil carries
/// granulation at 0.15, so it still needs the pass and must still queue one.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class SoftBrushLiveIsExactTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainViewModel Vm, int[] Passes) Ready(string preset)
    {
        var passes = new int[1];
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#101010",
            LivePostRunner = work =>
            {
                passes[0]++;
                work();
                return Task.CompletedTask;
            },
        };
        vm.NewDocument(new NewDocumentSettings(preset, 1280, 720, 12, 72, "#ffffff", false));
        vm.ApplyPreset(BuiltInPresets.Create().First(p => p.Name == preset));
        Dispatcher.UIThread.RunJobs();
        return (vm, passes);
    }

    private static void Draw(MainViewModel vm, int events = 120)
    {
        vm.BeginStroke(80, 360, 1);
        Dispatcher.UIThread.RunJobs();
        var x = 80.0;
        for (var i = 0; i < events; i++)
        {
            x += 9;
            if (x > 1200) x = 80;
            vm.MoveStroke(x, 360 + Math.Sin(x / 55) * 120, 0.9);
            Dispatcher.UIThread.RunJobs();
        }

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ASoftBrushNeedsNoDeferredPassToBeExact()
    {
        var (vm, passes) = Ready("Soft round");
        Assert.True(
            BrushEngine.NeedsFootprintCap(vm.CurrentToolSettingsForTest),
            "Soft round must want the ceiling, or this test is about nothing");

        Draw(vm);

        output.WriteLine($"Soft round queued {passes[0]} live post-process pass(es)");
        Assert.Equal(0, passes[0]);
    }

    /// <summary>
    /// The sensitivity half: a brush that really does need the pass still gets
    /// one, so a zero above cannot be the runner never being reached.
    /// </summary>
    [AvaloniaFact]
    public void AGranulatedBrushStillQueuesThePass()
    {
        var (vm, passes) = Ready("Pencil");
        Assert.True(
            vm.CurrentToolSettingsForTest.Granulation > 0,
            "Pencil must carry granulation, or it is not the control this needs");

        Draw(vm);

        output.WriteLine($"Pencil queued {passes[0]} live post-process pass(es)");
        Assert.True(passes[0] > 0, "the granulated brush queued no pass at all");
    }
}
