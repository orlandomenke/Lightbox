using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Named workspaces. Global, never stored in a project: a layout is a property
/// of the person, not of the artwork, and opening someone else's file must not
/// rearrange your screen.
/// </summary>
public class WorkspaceStoreTests
{
    [Fact]
    public void EveryProjectTypeHasABuiltInWorkspace()
    {
        var store = WorkspaceStore.Default();

        foreach (var type in Enum.GetValues<ProjectType>())
        {
            var workspace = store.DefaultFor(type);
            Assert.Equal(type, workspace.DefaultFor);
            Assert.True(workspace.BuiltIn);
        }
        // And something to fall back on for a document that is in no project.
        Assert.Null(store.DefaultFor(null).DefaultFor);
    }

    [Fact]
    public void TheBuiltInsDifferFromEachOther()
    {
        // If they were all the same layout the type picker would be decoration.
        var store = WorkspaceStore.Default();
        var illustration = store.DefaultFor(ProjectType.Illustration).Layout;
        var animation = store.DefaultFor(ProjectType.Animation).Layout;

        Assert.False(illustration.IsVisible(DockPanelId.Timeline));
        Assert.True(animation.IsVisible(DockPanelId.Timeline));
        Assert.True(illustration.IsVisible(DockPanelId.Palette));
    }

    [Fact]
    public void SavingUnderANewNameAddsAWorkspaceAndSelectsIt()
    {
        var store = WorkspaceStore.Default();
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);

        store.Save("Mine", layout);

        Assert.Equal("Mine", store.Current);
        Assert.Equal([DockPanelId.Palette], store.Find("Mine")!.Layout.PanelsIn(DockSide.Left));
    }

    [Fact]
    public void SavingOverYourOwnWorkspaceReplacesIt()
    {
        var store = WorkspaceStore.Default();
        store.Save("Mine", DockLayout.Default());
        var count = store.Workspaces.Count;

        var changed = DockLayout.Default();
        changed.Dock(DockPanelId.Gradient, DockSide.Left, 0);
        store.Save("Mine", changed);

        Assert.Equal(count, store.Workspaces.Count);
        Assert.True(store.Find("Mine")!.Layout.IsVisible(DockPanelId.Gradient));
    }

    [Fact]
    public void SavingOverABuiltInForksItInstead()
    {
        // Reset has to have something to reset *to*, and DefaultFor has to keep
        // meaning something — so a built-in is never overwritten.
        var store = WorkspaceStore.Default();
        var changed = DockLayout.Default();
        changed.Dock(DockPanelId.Gradient, DockSide.Left, 0);

        var saved = store.Save("Animation", changed);

        Assert.NotEqual("Animation", saved.Name);
        Assert.False(saved.BuiltIn);
        Assert.False(store.Find("Animation")!.Layout.IsVisible(DockPanelId.Gradient));
    }

    [Fact]
    public void OnlyYourOwnWorkspacesCanBeDeleted()
    {
        var store = WorkspaceStore.Default();
        store.Save("Mine", DockLayout.Default());

        Assert.False(store.Delete("Animation"));
        Assert.True(store.Delete("Mine"));
        Assert.Null(store.Find("Mine"));
    }

    [Fact]
    public void DeletingTheSelectedWorkspaceSelectsAnother()
    {
        var store = WorkspaceStore.Default();
        store.Save("Mine", DockLayout.Default());
        Assert.Equal("Mine", store.Current);

        store.Delete("Mine");

        Assert.NotNull(store.Find(store.Current));
    }

    [Fact]
    public void AStoreRoundTripsAndGainsBuiltInsItPredates()
    {
        // A file written before a project type existed must not leave that
        // type without a default.
        var store = WorkspaceStore.Default();
        store.Save("Mine", DockLayout.Default());
        var json = store.Serialize();

        var trimmed = WorkspaceStore.Deserialize(json);
        trimmed.Workspaces.RemoveAll(w => w.Name == "Comic");
        var back = WorkspaceStore.Deserialize(trimmed.Serialize());

        Assert.NotNull(back.Find("Mine"));
        Assert.NotNull(back.Find("Comic"));
        Assert.Equal(ProjectType.Comic, back.DefaultFor(ProjectType.Comic).DefaultFor);
    }

    [Fact]
    public void ACorruptStoreFallsBackRatherThanThrowing()
    {
        Assert.NotEmpty(WorkspaceStore.Deserialize("}{").Workspaces);
    }

    // ---- the view model on top of it ----------------------------------------

    private static WorkspaceViewModel Vm() => new(WorkspaceStore.Default());

    [Fact]
    public void ApplyingAWorkspaceReplacesTheLayoutAndClearsTheStar()
    {
        var vm = Vm();
        vm.SetVisible(DockPanelId.Gradient, true);
        Assert.True(vm.IsDirty);
        Assert.EndsWith("*", vm.CurrentLabel);

        vm.Apply("Illustration");

        Assert.False(vm.IsDirty);
        Assert.Equal("Illustration", vm.SelectedName);
        Assert.False(vm.Layout.IsVisible(DockPanelId.Timeline));
    }

    [Fact]
    public void ResetGoesBackToWhatTheWorkspaceSays()
    {
        var vm = Vm();
        vm.Apply("Animation");
        vm.SetVisible(DockPanelId.Gradient, true);
        Assert.True(vm.Layout.IsVisible(DockPanelId.Gradient));

        vm.Reset();

        Assert.False(vm.Layout.IsVisible(DockPanelId.Gradient));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void TakingAProjectTypesDefaultsSwitchesWorkspace()
    {
        // The offer made when a project of that type is created. Taking it is
        // a choice at that moment, not something the project remembers.
        var vm = Vm();

        vm.UseDefaultFor(ProjectType.Storyboard);

        Assert.Equal("Storyboard", vm.SelectedName);
        Assert.True(vm.Layout.IsVisible(DockPanelId.Sheets));
    }

    [Fact]
    public void OnlySavedWorkspacesOfferABin()
    {
        var vm = Vm();
        vm.SaveAs("Mine");

        var rows = vm.WorkspaceChoices.ToList();
        Assert.True(rows.Single(r => r.Name == "Mine").CanDelete);
        Assert.All(rows.Where(r => r.BuiltIn), r => Assert.False(r.CanDelete));
    }

    [Fact]
    public void TheLabelMarksAWorkspaceTheUserHasSinceRearranged()
    {
        // "Animation" on screen next to a layout that is not Animation's is
        // the one thing a picker must not claim.
        var vm = Vm();
        vm.Apply("Animation");
        Assert.Equal("Animation", vm.CurrentLabel);

        vm.Dock(DockPanelId.Color, DockSide.Left, 0);
        Assert.Equal("Animation *", vm.CurrentLabel);

        vm.SaveCurrent();
        Assert.DoesNotContain("*", vm.CurrentLabel);
    }
}
