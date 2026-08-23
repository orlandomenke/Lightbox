using Lightbox.App.Docking;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The arrangement on screen survives a restart (B288). Reported as "if we
/// have opened the rulers, they should stay open on a re-open" — and the
/// answer chosen was the whole arrangement, not one flag: the app reopens as
/// it was left, while the named workspaces stay snapshots that Apply and
/// Reset return to.
/// </summary>
public sealed class WorkspaceSessionTests
{
    /// <summary>A store round-tripped the way a restart reads it.</summary>
    private static WorkspaceViewModel Restarted(WorkspaceStore store) =>
        new(WorkspaceStore.Deserialize(store.Serialize()));

    [Fact]
    public void TheArrangementSurvivesARestart()
    {
        var store = WorkspaceStore.Default();
        var vm = new WorkspaceViewModel(store);
        Assert.False(vm.RulersVisible); // off by default, so the survival is provable

        vm.RulersVisible = true;

        var reopened = Restarted(store);
        Assert.True(reopened.RulersVisible);
        // The "edited" star survives too — the layout on screen still differs
        // from the saved workspace, and the picker must keep saying so.
        Assert.True(reopened.IsDirty);
    }

    [Fact]
    public void SavingTheWorkspaceClearsTheStarForTheNextLaunchToo()
    {
        var store = WorkspaceStore.Default();
        var vm = new WorkspaceViewModel(store);
        vm.RulersVisible = true;

        vm.SaveCurrent();

        var reopened = Restarted(store);
        Assert.True(reopened.RulersVisible);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public void ResetStillReturnsToTheSavedWorkspace()
    {
        // The session must not defeat reset: going back to the snapshot is a
        // choice, and it has to stick across a restart like any other.
        var store = WorkspaceStore.Default();
        var vm = new WorkspaceViewModel(store);
        vm.RulersVisible = true;

        vm.Reset();

        Assert.False(vm.RulersVisible);
        var reopened = Restarted(store);
        Assert.False(reopened.RulersVisible);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public void AStoreWrittenBeforeTheSessionExistedStartsFromItsWorkspace()
    {
        // An old workspaces.json has no session key; it must open exactly as
        // it always did — the current workspace's snapshot, unstarred.
        var store = WorkspaceStore.Default();
        var vm = new WorkspaceViewModel(store);

        Assert.False(vm.RulersVisible);
        Assert.False(vm.IsDirty);
    }
}
