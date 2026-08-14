using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Asking for more than one drawing each way, and getting it.
/// </summary>
/// <remarks>
/// Reported as "onion skinning multiple frames is not working — I see the
/// previous frame and the next frame, and cannot view 2 back and 2 forward".
/// Every ghost was in fact being selected and rendered; two separate things
/// made the depth look inert, and neither was the ghost planner:
/// <list type="number">
/// <item>the depth spinners existed only on the X-sheet tab's bar, so on the
/// Timeline tab there was nothing to raise;</item>
/// <item>at the old falloff of 0.5 the second ghost rendered at 0.175 alpha
/// and the third at 0.0875, which against paper is nothing.</item>
/// </list>
/// So the tests below are in three parts — the planner still selects them, the
/// controls are reachable from both tabs, and what comes out is visible.
/// <para>
/// <b>They build their own <see cref="OnionSettings"/> rather than reading the
/// view model's.</b> A <c>MainViewModel</c> loads <c>AppSettings</c> from disk
/// and every onion setter saves it back, so a test that sets one through the
/// view model writes the real settings file and every view model built after it
/// — in this run or the next — reads that value. The first version of this file
/// did exactly that and turned green locally and red on CI, which is the
/// signature of shared state rather than of a flaky test. Constructing the
/// settings here also makes the assertion honest: "the falloff Lightbox ships
/// with gives readable ghosts" is a claim about the default, and reading an
/// ambient value cannot make it.
/// </para>
/// </remarks>
public class OnionDepthTests(Xunit.ITestOutputHelper output)
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    private static MainViewModel VmWithFrames(int frames)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < frames; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        return vm;
    }

    // ---- the ghosts are selected, and reach the compositor ---------------------

    [AvaloniaFact]
    public void ADepthOfThree_GhostsThreeDrawingsEachWay()
    {
        var vm = VmWithFrames(7);

        var ghosts = OnionSkin.Ghosts(vm.PaintLayer(), 3, before: 3, after: 3, keysOnly: false);

        output.WriteLine(string.Join(", ",
            ghosts.Select(g => $"{(g.Before ? "-" : "+")}{g.Steps}@{g.Index}")));
        Assert.Equal(3, ghosts.Count(g => g.Before));
        Assert.Equal(3, ghosts.Count(g => !g.Before));
        Assert.Equal([0, 1, 2, 4, 5, 6], ghosts.Select(g => g.Index).Order());
    }

    [AvaloniaFact]
    public void EveryGhostBecomesARenderPass()
    {
        var vm = VmWithFrames(7);
        var layer = vm.PaintLayer();

        var onion = new OnionSettings { Before = 3, After = 3 };
        var state = new ScenePassBuilder.State(3, layer.Id, false, false, true, onion);
        using var cache = new FrameBitmapCache();
        var passes = ScenePassBuilder.GhostPassesFor(layer, vm.Doc.Scene, state, cache);

        // Six, not two: nothing between the planner and the compositor caps
        // the depth, which is what the report would have implied.
        Assert.Equal(6, passes.Count);
    }

    // ---- and they are visible once they get there ------------------------------

    [AvaloniaFact]
    public void TheThirdGhostIsStillLegible()
    {
        var vm = VmWithFrames(7);
        var layer = vm.PaintLayer();

        // The shipped defaults, deliberately — this is a claim about what an
        // artist gets before touching anything.
        var onion = new OnionSettings { Before = 3, After = 3 };
        var state = new ScenePassBuilder.State(3, layer.Id, false, false, true, onion);
        using var cache = new FrameBitmapCache();
        var alphas = ScenePassBuilder.GhostPassesFor(layer, vm.Doc.Scene, state, cache)
            .Select(p => p.Opacity).Order().ToList();

        output.WriteLine($"opacity {onion.Opacity:F3}, falloff {onion.Falloff:F3} -> "
            + string.Join(", ", alphas.Select(a => a.ToString("F4"))));

        // The faintest is the third step each way. At the old falloff of 0.5
        // it was 0.0875; the number that matters is that it is now well clear
        // of the ~0.10 an artist can actually see against paper.
        Assert.True(alphas[0] > 0.15, $"faintest ghost is {alphas[0]:F4}");
        // Still ranked by distance — an equal-opacity stack would be a
        // different bug, and is what falloff 1 is for.
        Assert.True(alphas[0] < alphas[^1], $"faintest {alphas[0]:F4}, nearest {alphas[^1]:F4}");
    }

    // ---- the defaults, and what happens to a saved one --------------------------

    [Fact]
    public void ANewInstallGetsTheReadableFalloff()
    {
        Assert.Equal(0.75, new OnionSettings().Falloff, 3);
    }

    [Fact]
    public void ASavedFalloffStillOnTheOldDefaultIsMovedForward()
    {
        // These settings persist, so an install that has ever saved them holds
        // an explicit 0.5 and would never see a changed default. Without this
        // the fix reaches only new installs — that is, everyone except the
        // people who hit the problem.
        var settings = AppSettings.Deserialize("""{"Onion":{"Falloff":0.5}}""");

        Assert.Equal(0.75, settings.Onion.Falloff, 3);
    }

    [Fact]
    public void AFalloffTheArtistChoseIsLeftAlone()
    {
        var settings = AppSettings.Deserialize("""{"Onion":{"Falloff":0.5,"FalloffChosen":true}}""");

        Assert.Equal(0.5, settings.Onion.Falloff, 3);
    }

    [Fact]
    public void AFalloffSetToSomethingElseIsNeverTouched()
    {
        var settings = AppSettings.Deserialize("""{"Onion":{"Falloff":0.9}}""");

        Assert.Equal(0.9, settings.Onion.Falloff, 3);
    }

    /// <summary>
    /// The view-model setter records the choice, so the migration never
    /// second-guesses a falloff the artist set by hand.
    /// </summary>
    /// <remarks>
    /// The only test here that touches the shared settings, because the
    /// behaviour under test <em>is</em> the setter. It puts the file back the
    /// way it found it: an onion setter calls <c>Settings.Save()</c>, so
    /// without this the run leaves 0.5 on the machine and every view model
    /// built afterwards — including in the next run — loads it.
    /// </remarks>
    [AvaloniaFact]
    public void SettingTheFalloffMarksItChosen()
    {
        var vm = VmLayers.BareVm();
        var (falloff, chosen) = (vm.Onion.Falloff, vm.Onion.FalloffChosen);
        try
        {
            vm.OnionFalloff = 0.5;

            Assert.True(vm.Onion.FalloffChosen);
            Assert.Equal(0.5, vm.Onion.Falloff, 3);
        }
        finally
        {
            vm.Onion.Falloff = falloff;
            vm.Onion.FalloffChosen = chosen;
            vm.Settings.Save();
        }
    }

    [Fact]
    public void AChosenFalloffSurvivesTheRoundTrip()
    {
        // The other half of the setter's promise, checked where it costs
        // nothing: what it writes is what comes back, unmigrated.
        Assert.Equal(0.5, AppSettings.Deserialize(
            """{"Onion":{"Falloff":0.5,"FalloffChosen":true}}""").Onion.Falloff, 3);
    }

    // ---- reachable from both tabs ----------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lightbox.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void BothTimelineTabsCarryTheOnionControls()
    {
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views", "MainWindow.axaml"));

        // Two usages, one per docker. A control on one tab and not the other is
        // a dead end rather than a layout choice: onion skin belongs to the
        // artist, not to whichever view of the animation they happen to have up.
        var uses = System.Text.RegularExpressions.Regex.Matches(xaml, "<views:OnionBar */>").Count;
        Assert.Equal(2, uses);

        // And the depth spinners are inside that shared control rather than
        // copied into either bar, so the two cannot drift apart.
        var bar = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views", "OnionBar.axaml"));
        Assert.Contains("OnionBefore", bar);
        Assert.Contains("OnionAfter", bar);
        Assert.DoesNotContain("OnionBefore", xaml);
        Assert.DoesNotContain("OnionAfter", xaml);
    }
}
