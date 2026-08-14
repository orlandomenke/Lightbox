using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The Quick options bar as a property of the workspace (Q74's sharpening of
/// Q70): each workspace chooses the bar's contents from
/// <see cref="QuickBarCatalog"/>, size and opacity stay fixed outside the
/// choice, and the choice saves, resets and switches with the layout.
/// </summary>
public sealed class QuickBarWorkspaceTests(ITestOutputHelper output)
{
    private static string MainWindowXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));
    }

    // ---- the catalogue and the bar cannot drift apart ------------------------------------

    /// <summary>
    /// Every catalogue entry has a gate in the bar's XAML. Without this, the
    /// customize flyout can offer a checkbox that changes nothing on screen —
    /// the option exists in the list and nowhere else.
    /// </summary>
    [Fact]
    public void EveryCatalogueEntryGatesASectionOfTheBar()
    {
        var xaml = MainWindowXaml();
        var unwired = QuickBarCatalog.All
            .Where(option => !xaml.Contains(
                "Workspace." + WorkspaceViewModel.QuickNames[option.Id], StringComparison.Ordinal))
            .Select(option => option.Id)
            .ToList();

        output.WriteLine(unwired.Count == 0
            ? "every catalogue entry is wired to the bar"
            : $"in the catalogue but not the XAML: {string.Join(", ", unwired)}");
        Assert.Empty(unwired);
    }

    [Fact]
    public void TheCatalogueHasNoDuplicateIdsAndTheNameMapCoversIt()
    {
        Assert.Equal(QuickBarCatalog.All.Count, QuickBarCatalog.All.Select(o => o.Id).Distinct().Count());
        foreach (var option in QuickBarCatalog.All)
        {
            Assert.True(WorkspaceViewModel.QuickNames.ContainsKey(option.Id),
                $"no view-model gate name for '{option.Id}'");
        }
    }

    // ---- optional means absent ------------------------------------------------------------

    /// <summary>
    /// A workspace that never chose writes no key — the serialized store looks
    /// exactly as it did before the quick bar knew about workspaces.
    /// </summary>
    [Fact]
    public void AWorkspaceThatNeverChoseSerializesNoQuickBarKey()
    {
        var store = WorkspaceStore.Default();
        store.Workspaces.RemoveAll(w => w.Name != "Default");

        var json = store.Serialize();

        Assert.DoesNotContain("\"QuickBar\"", json);
    }

    [Fact]
    public void AChosenListSurvivesTheRoundTrip()
    {
        var store = WorkspaceStore.Default();
        store.Find("Default")!.Layout.QuickBar =
            [QuickBarCatalog.BrushOptions, QuickBarCatalog.Transport];

        var reloaded = WorkspaceStore.Deserialize(store.Serialize());

        Assert.Equal(
            [QuickBarCatalog.BrushOptions, QuickBarCatalog.Transport],
            reloaded.Find("Default")!.Layout.QuickBar);
    }

    // ---- the built-ins choose by the work ---------------------------------------------------

    [Fact]
    public void AnimationCarriesTheTransportAndIllustrationDoesNot()
    {
        var store = WorkspaceStore.Default();

        var animation = store.Find("Animation")!.Layout.QuickBarContents;
        var illustration = store.Find("Illustration")!.Layout.QuickBarContents;

        Assert.Contains(QuickBarCatalog.Transport, animation);
        Assert.Contains(QuickBarCatalog.AddFrame, animation);
        Assert.DoesNotContain(QuickBarCatalog.Transport, illustration);
        Assert.Contains(QuickBarCatalog.SelectOptions, illustration);
    }

    /// <summary>
    /// A workspaces.json saved before the bar could be chosen carries every
    /// built-in without a quickBar key — the app wrote those nulls, not the
    /// artist, and the store saves built-ins alongside the user's own. Left
    /// alone they shadow the new defaults forever: Animation never gets its
    /// transport and every workspace resolves to the tool defaults, which is
    /// exactly the bar looking "not wired" (B203). Loading fills a built-in
    /// that never chose from the built-in's own choice.
    /// </summary>
    [Fact]
    public void AStoreSavedBeforeTheBarExistedStillGivesAnimationItsTransport()
    {
        var stale = WorkspaceStore.Default();
        foreach (var workspace in stale.Workspaces) workspace.Layout.QuickBar = null;

        var reloaded = WorkspaceStore.Deserialize(stale.Serialize());

        var animation = reloaded.Find("Animation")!.Layout.QuickBarContents;
        output.WriteLine($"Animation resolves to: {string.Join(", ", animation)}");
        Assert.Contains(QuickBarCatalog.Transport, animation);
        Assert.Contains(QuickBarCatalog.AddFrame, animation);
        // "Default" genuinely never chose — it stays on the tool defaults.
        Assert.Null(reloaded.Find("Default")!.Layout.QuickBar);
    }

    /// <summary>
    /// The migration only fills a null. A built-in the artist customised
    /// after the feature arrived has a materialised list, and that choice —
    /// including having dropped an entry the default carries — must survive.
    /// </summary>
    [Fact]
    public void ACustomisedBuiltInKeepsItsChoiceThroughTheRoundTrip()
    {
        var store = WorkspaceStore.Default();
        store.Find("Animation")!.Layout.QuickBar = [QuickBarCatalog.BrushOptions];

        var reloaded = WorkspaceStore.Deserialize(store.Serialize());

        Assert.Equal([QuickBarCatalog.BrushOptions],
            reloaded.Find("Animation")!.Layout.QuickBar);
    }

    /// <summary>
    /// A workspace the artist saved themselves is theirs even when it never
    /// chose: null means the tool defaults, not "adopt a built-in's list".
    /// </summary>
    [Fact]
    public void AUsersOwnWorkspaceIsNotFilledInOnLoad()
    {
        var store = WorkspaceStore.Default();
        store.Workspaces.Add(new Workspace { Name = "Mine", Layout = new DockLayout() });

        var reloaded = WorkspaceStore.Deserialize(store.Serialize());

        Assert.Null(reloaded.Find("Mine")!.Layout.QuickBar);
    }

    /// <summary>
    /// The "Default" workspace never chose, so it resolves to the bar as it
    /// was before any of this — every tool group, no timeline buttons. That
    /// is also what any workspaces.json written before the property existed
    /// resolves to.
    /// </summary>
    [Fact]
    public void TheDefaultWorkspaceResolvesToTheToolDefaults()
    {
        var layout = WorkspaceStore.Default().Find("Default")!.Layout;

        Assert.Null(layout.QuickBar);
        Assert.Equal(QuickBarCatalog.ToolDefaults, layout.QuickBarContents);
    }

    [Fact]
    public void CloneCopiesTheChoiceWithoutSharingIt()
    {
        var layout = new DockLayout { QuickBar = [QuickBarCatalog.Transport] };

        var clone = layout.Clone();
        clone.QuickBar!.Add(QuickBarCatalog.AddFrame);

        Assert.Single(layout.QuickBar!);
    }

    // ---- customizing is a workspace edit like any other -------------------------------------

    [Fact]
    public void TogglingAnOptionMarksTheWorkspaceDirtyAndResetUndoesIt()
    {
        var vm = new WorkspaceViewModel(WorkspaceStore.Default());

        Assert.False(vm.QuickTransport);
        vm.SetQuickOption(QuickBarCatalog.Transport, true);

        Assert.True(vm.QuickTransport);
        Assert.True(vm.IsDirty);
        Assert.EndsWith("*", vm.CurrentLabel);

        vm.Reset();

        Assert.False(vm.QuickTransport);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SavingTheWorkspaceKeepsTheChoice()
    {
        var store = WorkspaceStore.Default();
        var vm = new WorkspaceViewModel(store);

        vm.SetQuickOption(QuickBarCatalog.Transport, true);
        vm.SaveAs("Mine");

        Assert.Contains(QuickBarCatalog.Transport, store.Find("Mine")!.Layout.QuickBar!);
    }

    [Fact]
    public void SwitchingWorkspaceAnnouncesTheGatesAndReChecksTheFlyout()
    {
        var vm = new WorkspaceViewModel(WorkspaceStore.Default());
        var announced = new List<string?>();
        vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        var transportRow = vm.QuickBarChoices.Single(c => c.Label == "Play / pause");
        var rowAnnounced = false;
        transportRow.PropertyChanged += (_, _) => rowAnnounced = true;

        vm.Apply("Animation");

        // Without these the bar keeps showing the previous workspace's picks —
        // the same silent staleness the tool icon test guards one level down.
        Assert.Contains(nameof(WorkspaceViewModel.QuickTransport), announced);
        Assert.True(vm.QuickTransport);
        Assert.True(transportRow.IsOn);
        Assert.True(rowAnnounced);
    }

    /// <summary>
    /// The pinned pair is not a catalogue entry: what Q70 fixed on the bar,
    /// Q74's customisation must not be able to remove.
    /// </summary>
    [Fact]
    public void SizeAndOpacityAreNotOnOffer()
    {
        foreach (var option in QuickBarCatalog.All)
        {
            Assert.DoesNotContain("size", option.Id, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("opacity", option.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The preset picker joined the pinned section beside the colour pair
    /// (owner's ask, 2026-08-14), so like size and opacity it is not on
    /// offer: a checkbox for a control the workspace cannot move would be a
    /// checkbox that changes nothing. The retired id may still sit in saved
    /// lists; no gate reads it, and the XAML must not grow one back.
    /// </summary>
    [Fact]
    public void ThePinnedBrushPresetIsNotOnOffer()
    {
        Assert.DoesNotContain(QuickBarCatalog.All, o => o.Id == QuickBarCatalog.BrushPreset);
        Assert.DoesNotContain(QuickBarCatalog.BrushPreset, QuickBarCatalog.ToolDefaults);
        Assert.DoesNotContain("Workspace.QuickBrushPreset", MainWindowXaml(), StringComparison.Ordinal);
    }
}
