using Lightbox.App.Docking;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Rulers and guide visibility survive a restart.
/// </summary>
/// <remarks>
/// Reported as *"reopening a file where the rulers and guides were visible
/// opens without them active"*. Both are workspace state rather than document
/// state — deliberately, the way Photoshop's are: they answer "how am I
/// working" rather than "what is this drawing" — so what has to survive is the
/// session snapshot the next launch restores from, not the document.
/// </remarks>
public class RulerVisibilityTests
{
    /// <summary>A store with no file behind it, so nothing here writes the owner's own.</summary>
    private static WorkspaceStore Store() => WorkspaceStore.Default();

    [Fact]
    public void TheDefaultsAreRulersOffAndGuidesOn()
    {
        // Stated so the assertions below are visibly toggling *away* from the
        // default: a test that stores a value equal to the default proves
        // nothing about whether it was stored.
        var layout = DockLayout.Default();

        Assert.False(layout.Rulers);
        Assert.True(layout.GuidesVisible);
    }

    [Fact]
    public void ShowingTheRulersSurvivesTheNextLaunch()
    {
        var store = Store();
        var vm = new WorkspaceViewModel(store);
        Assert.False(vm.RulersVisible);

        vm.RulersVisible = true;

        // What the next launch reads: the session snapshot, through the file's
        // own text rather than the live object.
        var reloaded = new WorkspaceViewModel(WorkspaceStore.Deserialize(store.Serialize()));

        Assert.True(reloaded.RulersVisible);
    }

    [Fact]
    public void HidingTheGuidesSurvivesTheNextLaunch()
    {
        var store = Store();
        var vm = new WorkspaceViewModel(store);
        Assert.True(vm.GuidesVisible);

        vm.GuidesVisible = false;

        var reloaded = new WorkspaceViewModel(WorkspaceStore.Deserialize(store.Serialize()));

        Assert.False(reloaded.GuidesVisible);
    }

    [Fact]
    public void TheGuideLockSurvivesToo()
    {
        // The third of the trio the View menu offers, and the one no report
        // has named — which is the reason to assert it here rather than wait.
        var store = Store();
        var vm = new WorkspaceViewModel(store);
        Assert.False(vm.GuidesLocked);

        vm.GuidesLocked = true;

        var reloaded = new WorkspaceViewModel(WorkspaceStore.Deserialize(store.Serialize()));

        Assert.True(reloaded.GuidesLocked);
    }

    [Fact]
    public void AndTheSessionSnapshotIsWhatCarriesThem()
    {
        // The mechanism, pinned separately from the outcome: the toggle goes
        // into Session, not into the named workspace's own layout. If a future
        // change made Apply() or Replace() the only writer, the assertions
        // above would still pass while a workspace switch silently reset them.
        var store = Store();
        var vm = new WorkspaceViewModel(store);

        vm.RulersVisible = true;

        Assert.NotNull(store.Session);
        Assert.True(store.Session!.Rulers);
    }
}
