using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B393: asking for the panel that is already showing must not rebuild the dock.
/// </summary>
/// <remarks>
/// Every transform opens Tool Options (<c>OpenToolOptions</c>), which shows and
/// activates it. <c>SetVisible</c> already returned early when nothing changed;
/// <c>Activate</c> did not, so every transform start rebuilt the whole dock
/// layout — a CPU trace on the owner's 11-layer document measured it at a p90 of
/// 600 ms per transform — and marked the workspace as rearranged, putting a "*"
/// on a layout nobody had touched.
/// </remarks>
public sealed class WorkspaceNoOpActivateTests : BrushStateIsolated
{
    [Fact]
    public void ActivatingThePanelAlreadyShowingChangesNothing()
    {
        var vm = new WorkspaceViewModel(WorkspaceStore.Default());
        Assert.False(vm.IsDirty);
        // Whatever the shipped layout shows, a panel it already shows.
        var showing = DockPanels.All.Select(p => p.Id).First(vm.IsActiveInItsSlot);

        var changes = 0;
        vm.Changed += () => changes++;
        vm.Activate(showing);

        Assert.Equal(0, changes);
        Assert.False(vm.IsDirty, "re-activating the showing panel marked the workspace rearranged");
    }

    /// <summary>
    /// The guard is a no-op guard, not a refusal: a panel behind another tab
    /// still comes forward, and that is still a change.
    /// </summary>
    [Fact]
    public void ActivatingAPanelBehindAnotherTabStillBringsItForward()
    {
        var vm = new WorkspaceViewModel(WorkspaceStore.Default());
        vm.SetVisible(DockPanelId.ToolOptions, true);
        var other = DockPanels.All.Select(p => p.Id)
            .First(id => id != DockPanelId.ToolOptions && vm.Layout.IsVisible(id));
        // Joining makes the joiner the tab showing, so Tool Options is behind it.
        vm.JoinGroup(other, DockPanelId.ToolOptions);
        Assert.False(vm.IsActiveInItsSlot(DockPanelId.ToolOptions));

        var changes = 0;
        vm.Changed += () => changes++;
        vm.Activate(DockPanelId.ToolOptions);

        Assert.True(vm.IsActiveInItsSlot(DockPanelId.ToolOptions));
        Assert.Equal(1, changes);
    }
}
