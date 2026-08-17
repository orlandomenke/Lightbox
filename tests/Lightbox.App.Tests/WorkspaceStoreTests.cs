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
    public void TheShippedArrangementsTabTheColourTools()
    {
        // The rule these follow: tab what you use alternately, never what you
        // use together. Colour, palette and gradient are three answers to one
        // question; layers is consulted while doing something else.
        var illustration = WorkspaceStore.Default().Find("Illustration")!;
        var slot = illustration.Layout.SlotOf(DockPanelId.Color);

        Assert.Contains(DockPanelId.Palette, slot);
        Assert.Contains(DockPanelId.Gradient, slot);
        Assert.DoesNotContain(DockPanelId.Layers, slot);
        Assert.Equal(DockPanelId.Color, illustration.Layout.ActiveOf(slot));
    }

    [Fact]
    public void TabbingOffersMorePanelsWithoutSpendingMoreHeight()
    {
        // What tabs actually buy. The palette and the gradient used to be
        // hidden in most arrangements because neither was worth a slot of
        // sidebar; now they are reachable, and the strip has the same number of
        // slots it would have had with the colour panel alone.
        var illustration = WorkspaceStore.Default().Find("Illustration")!.Layout;

        Assert.True(illustration.IsVisible(DockPanelId.Palette));
        Assert.True(illustration.IsVisible(DockPanelId.Gradient));
        // The work group, Layers, and colour (Q109) — three slots carrying
        // eight panels, which is the whole of what tabs buy.
        Assert.Equal(3, illustration.SlotsIn(DockSide.Right).Count);
    }

    [Fact]
    public void NoArrangementTabsTwoPanelsAnArtistNeedsAtOnce()
    {
        // The rule stated as a guard rather than a comment: never tab what an
        // artist uses at once. **Layers** is the one that matters — it is read
        // and clicked *during* a stroke, so tabbing it with anything trades a
        // scroll for a click on every mark.
        //
        // The project tree used to be named here beside it and is not any more
        // (Q109). It is consulted between pieces of work rather than during
        // one — which document am I on, which sheet am I drawing from, what is
        // this tool set to — so it leads the group of exactly those three. Two
        // groups are the deliberate exceptions of the same kind: the timeline
        // family (Q58) is three views over one set of records, and the colour
        // family is three answers to one question.
        DockPanelId[] timeline = [DockPanelId.Timeline, DockPanelId.Xsheet, DockPanelId.GraphEditor];
        DockPanelId[] work = [DockPanelId.Project, DockPanelId.Sheets, DockPanelId.ToolOptions];
        foreach (var workspace in WorkspaceStore.Default().Workspaces)
        {
            foreach (var side in new[] { DockSide.Right, DockSide.Bottom })
            {
                foreach (var slot in workspace.Layout.SlotsIn(side))
                {
                    if (slot.Count == 1) continue;
                    Assert.DoesNotContain(DockPanelId.Layers, slot);
                    if (slot.Contains(DockPanelId.Timeline))
                    {
                        Assert.All(slot, p => Assert.Contains(p, timeline));
                    }
                    // The project tree may share a slot, and only with the two
                    // it was grouped with on purpose.
                    if (slot.Contains(DockPanelId.Project))
                    {
                        Assert.All(slot, p => Assert.Contains(p, work));
                    }
                }
            }
        }
    }

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
        changed.Dock(DockPanelId.Reference, DockSide.Left, 0);

        var saved = store.Save("Animation", changed);

        Assert.NotEqual("Animation", saved.Name);
        Assert.False(saved.BuiltIn);
        Assert.False(store.Find("Animation")!.Layout.IsVisible(DockPanelId.Reference));
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
        // Reference rather than Gradient: the gradient is one of the default
        // layout's colour tabs now, so showing it would be a no-op.
        vm.SetVisible(DockPanelId.Reference, true);
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
        // Reference rather than Gradient: the gradient is one of Animation's
        // colour tabs now, so it is visible from the start and cannot stand in
        // for "a panel this arrangement keeps hidden".
        vm.SetVisible(DockPanelId.Reference, true);
        Assert.True(vm.Layout.IsVisible(DockPanelId.Reference));

        vm.Reset();

        Assert.False(vm.Layout.IsVisible(DockPanelId.Reference));
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

    [Fact]
    public void SaveCurrentOverwritesABuiltInInPlace()
    {
        // Pressing "save current workspace" while in Animation means "keep
        // Animation like this" — it must not fork an "Animation (edited)"
        // beside it and quietly switch to that.
        var vm = Vm();
        vm.Apply("Animation");
        var count = vm.Store.Workspaces.Count;
        vm.SetVisible(DockPanelId.Reference, true);

        vm.SaveCurrent();

        Assert.Equal("Animation", vm.SelectedName);
        Assert.Equal(count, vm.Store.Workspaces.Count);
        Assert.DoesNotContain(vm.Store.Workspaces, w => w.Name.Contains("(edited)"));
        Assert.True(vm.Store.Find("Animation")!.Layout.IsVisible(DockPanelId.Reference));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ResetOnABuiltInRestoresHowItShipped()
    {
        // Even after the built-in's slot was saved over: "reset" on a
        // workspace the app ships is a promise about the app, not the file.
        var vm = Vm();
        vm.Apply("Animation");
        vm.SetVisible(DockPanelId.Reference, true);
        vm.SaveCurrent();
        Assert.True(vm.Store.Find("Animation")!.Layout.IsVisible(DockPanelId.Reference));

        vm.Reset();

        Assert.Equal("Animation", vm.SelectedName);
        Assert.False(vm.Layout.IsVisible(DockPanelId.Reference));
        Assert.False(vm.Store.Find("Animation")!.Layout.IsVisible(DockPanelId.Reference));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void ResetOnAUserWorkspaceGoesBackToWhatWasSaved()
    {
        var vm = Vm();
        vm.SetVisible(DockPanelId.Reference, true);
        vm.SaveAs("Mine");
        vm.SetVisible(DockPanelId.History, true);

        vm.Reset();

        Assert.Equal("Mine", vm.SelectedName);
        Assert.True(vm.Layout.IsVisible(DockPanelId.Reference));
        Assert.False(vm.Layout.IsVisible(DockPanelId.History));
    }

    [Fact]
    public void SaveAsWithTheUnchangedNameMakesAnEditedCopy()
    {
        // "Save as new workspace" with the name left as it was is still a
        // request for a new workspace — the original keeps its layout.
        var vm = Vm();
        vm.SaveAs("Mine");
        vm.SetVisible(DockPanelId.Reference, true);

        vm.SaveAs("Mine");

        Assert.Equal("Mine (edited)", vm.SelectedName);
        Assert.False(vm.Store.Find("Mine")!.Layout.IsVisible(DockPanelId.Reference));
        Assert.True(vm.Store.Find("Mine (edited)")!.Layout.IsVisible(DockPanelId.Reference));
    }

    [Fact]
    public void SaveAsOverABuiltInsNameStillForks()
    {
        var vm = Vm();
        vm.Apply("Animation");
        vm.SetVisible(DockPanelId.Reference, true);

        vm.SaveAs("Animation");

        Assert.Equal("Animation (edited)", vm.SelectedName);
        Assert.False(vm.Store.Find("Animation")!.Layout.IsVisible(DockPanelId.Reference));
    }
}

/// <summary>
/// Autosave's configuration. Not about pixels, so it lives in the app's
/// settings rather than in any document — invariant 4 is about the other kind.
/// </summary>
public class AutosaveSettingsTests
{
    [Fact]
    public void TheDefaultIsEveryMinuteToTheRecoveryCopyOnly()
    {
        var settings = new Lightbox.App.Services.AppSettings();

        Assert.Equal(TimeSpan.FromMinutes(1), settings.AutosaveInterval);
        // Off by default, and not out of timidity: rewriting the file someone
        // opened takes away the ability to close without saving.
        Assert.False(settings.AutosaveInPlace);
    }

    [Fact]
    public void ZeroTurnsAutosaveOff()
    {
        // A real answer, not a mistake to guard against — someone working on a
        // network drive may well want it.
        Assert.Null(new Lightbox.App.Services.AppSettings { AutosaveMinutes = 0 }.AutosaveInterval);
    }

    [Fact]
    public void AnAbsurdIntervalIsClampedRatherThanHonoured()
    {
        Assert.Equal(TimeSpan.FromMinutes(60),
            new Lightbox.App.Services.AppSettings { AutosaveMinutes = 9999 }.AutosaveInterval);
        Assert.Equal(TimeSpan.FromMinutes(0.25),
            new Lightbox.App.Services.AppSettings { AutosaveMinutes = 0.001 }.AutosaveInterval);
    }

    [Fact]
    public void SettingsRoundTripAndSurviveCorruption()
    {
        var settings = new Lightbox.App.Services.AppSettings { AutosaveMinutes = 5, AutosaveInPlace = true };
        var back = Lightbox.App.Services.AppSettings.Deserialize(settings.Serialize());

        Assert.Equal(5, back.AutosaveMinutes);
        Assert.True(back.AutosaveInPlace);
        Assert.Equal(1, Lightbox.App.Services.AppSettings.Deserialize("nonsense").AutosaveMinutes);
    }
}
