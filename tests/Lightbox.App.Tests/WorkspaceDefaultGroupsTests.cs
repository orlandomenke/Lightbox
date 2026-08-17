using Lightbox.App.Docking;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// What every shipped arrangement puts on screen, and how it is grouped (Q109).
/// </summary>
/// <remarks>
/// Three rules, asserted across every built-in rather than spot-checked in one:
/// the work group everywhere, the colour family everywhere, and the timeline
/// family in the workspaces whose deliverable moves. A default that holds in
/// five workspaces and not the sixth is the kind of thing nobody notices until
/// they are working in the sixth.
/// </remarks>
public class WorkspaceDefaultGroupsTests
{
    private static IEnumerable<Workspace> All() => WorkspaceStore.Default().Workspaces;

    /// <summary>The workspaces whose deliverable moves — animation, by any name.</summary>
    private static readonly string[] Moving = ["Default", "Animation", "Game art", "Storyboard", "Asset library"];

    [Fact]
    public void EveryWorkspaceTabsTheProjectTreeWithReferenceAndToolOptions()
    {
        foreach (var workspace in All())
        {
            var slot = workspace.Layout.SlotOf(DockPanelId.Project);

            Assert.Equal(
                [DockPanelId.Project, DockPanelId.Sheets, DockPanelId.ToolOptions],
                slot);
            // The project tree leads: it is the one you reach for to change
            // what you are working on at all.
            Assert.Equal(DockPanelId.Project, workspace.Layout.ActiveOf(slot));
            Assert.True(
                workspace.Layout.IsVisible(DockPanelId.ToolOptions),
                $"{workspace.Name} does not open Tool options");
        }
    }

    [Fact]
    public void EveryWorkspaceTabsTheWholeColourFamily()
    {
        // Game art and Asset library shipped a hand-picked three that left
        // Gradient out, and Storyboard offered no colour panel at all — so
        // three of six arrangements could not reach a gradient without going
        // to the View menu for it.
        foreach (var workspace in All())
        {
            var slot = workspace.Layout.SlotOf(DockPanelId.Color);

            Assert.Equal(
                [DockPanelId.Color, DockPanelId.Palette, DockPanelId.Gradient, DockPanelId.Channels],
                slot);
            Assert.Equal(DockPanelId.Color, workspace.Layout.ActiveOf(slot));
        }
    }

    [Fact]
    public void TheWorkspacesWhoseDeliverableMovesOpenTheTimelineFamily()
    {
        foreach (var workspace in All())
        {
            var moves = Moving.Contains(workspace.Name);
            var slot = workspace.Layout.SlotOf(DockPanelId.Timeline);

            if (!moves)
            {
                // Illustration and Comic are single-image work: the bottom
                // strip is screen they get back.
                Assert.False(
                    workspace.Layout.IsVisible(DockPanelId.Timeline),
                    $"{workspace.Name} opens a timeline for work that has one frame");
                continue;
            }

            Assert.Equal(
                [DockPanelId.Timeline, DockPanelId.Xsheet, DockPanelId.GraphEditor],
                slot);
            Assert.Equal(DockPanelId.Timeline, workspace.Layout.ActiveOf(slot));
            Assert.All(slot, p => Assert.True(
                workspace.Layout.IsVisible(p), $"{workspace.Name} does not open {p}"));
        }
    }

    [Fact]
    public void AnAssetLibraryGetsATimelineBecauseASpriteSheetIsACycle()
    {
        // The one this rule added, and the reason it is not obvious: an asset
        // library reads as a drawer of stills, and its deliverable is a
        // character cycle.
        var library = WorkspaceStore.Default().DefaultFor(ProjectType.AssetLibrary).Layout;

        Assert.True(library.IsVisible(DockPanelId.Timeline));
        Assert.True(library.IsVisible(DockPanelId.Xsheet));
    }

    [Fact]
    public void LayersIsNeverTabbedWithAnything()
    {
        // The rule the groups are chosen against, and the one panel it still
        // protects: layers is clicked *during* a stroke.
        foreach (var workspace in All())
        {
            foreach (var side in new[] { DockSide.Right, DockSide.Bottom })
            {
                foreach (var slot in workspace.Layout.SlotsIn(side))
                {
                    if (slot.Count > 1) Assert.DoesNotContain(DockPanelId.Layers, slot);
                }
            }
        }
    }

    [Fact]
    public void NoArrangementSpendsMoreThanFourSlotsASide()
    {
        // The owner's cap. Three groups plus Layers is exactly four on the
        // right, which is the whole budget — worth a guard, because the next
        // panel added to a built-in has nowhere to go.
        foreach (var workspace in All())
        {
            foreach (var side in new[] { DockSide.Right, DockSide.Bottom, DockSide.Left, DockSide.Top })
            {
                Assert.True(
                    workspace.Layout.SlotsIn(side).Count <= DockLayout.MaxSlotsPerSide,
                    $"{workspace.Name} opens {workspace.Layout.SlotsIn(side).Count} slots on {side}");
            }
        }
    }
}
