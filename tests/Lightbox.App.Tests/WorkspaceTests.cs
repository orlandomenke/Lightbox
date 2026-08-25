using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The workspace as the window actually builds it. The arithmetic is covered
/// by <see cref="DockLayoutTests"/> and <see cref="DockZoneTests"/> without a
/// window; what is left to check here is the part only a real window can get
/// wrong — that a panel ends up in the strip the layout named, that closing
/// one parks it instead of destroying it, and that an emptied edge collapses.
/// </summary>
[Collection("BrushState")]
public sealed class WorkspaceTests : BrushStateIsolated
{
    /// <summary>
    /// A character called Knight owning the adopted document.
    /// </summary>
    /// <remarks>
    /// <b>B83/B84.</b> <c>NewProject</c> used to invent this character from the
    /// project's own name, which put the artist's first drawing at
    /// <c>characters/knight/animations/</c> and created two folders nobody asked
    /// for. These tests are about the menus rather than about that invention, so
    /// they now ask for the arrangement they always assumed.
    /// </remarks>
    private static void WithKnight(MainViewModel vm)
    {
        var project = vm.ProjectDocker.Project!;
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        vm.ProjectDocker.Refresh();
        // Through the docker, so the file moves with the manifest entry.
        foreach (var row in vm.ProjectDocker.Rows.Where(r => r.Animation is not null).ToList())
        {
            vm.ProjectDocker.MoveInto(row, knight);
        }
        vm.SaveProject(everything: true);
        vm.ProjectDocker.Refresh();
        // Selected, because a new document lands in the selected folder.
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.IsFolder);
    }

    /// <summary>A shown window with one document in it.</summary>
    /// <remarks>
    /// The document used to arrive with the window. It no longer does — the
    /// application opens empty and the start screen asks — so the tests that are
    /// about panels, dockers and project rows open one immediately and carry on
    /// meaning what they meant. <c>NewProject</c> in particular forms around the
    /// document already open, so without one it adopts nothing.
    /// </remarks>
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static DockStrip Strip(MainWindow w, DockSide side) => side switch
    {
        DockSide.Left => w.FindControl<DockStrip>("LeftStrip")!,
        DockSide.Right => w.FindControl<DockStrip>("RightStrip")!,
        DockSide.Top => w.FindControl<DockStrip>("TopStrip")!,
        _ => w.FindControl<DockStrip>("BottomStrip")!,
    };

    private static List<DockPanelId> Shown(MainWindow w, DockSide side) =>
        Strip(w, side).Children.OfType<Docker>().Select(d => d.PanelId).ToList();

    private static Panel Pool(MainWindow w) => w.FindControl<Panel>("PanelPool")!;

    [AvaloniaFact]
    public void PanelsLandInTheStripTheLayoutNames()
    {
        var (w, _) = Open();

        // The navigator leads the strip (Q110), then the work group — whose
        // Project tab is absent with no project open, so Reference sheets is
        // the tab that shows (Q109) — then Layers, then colour.
        Assert.Equal(
            [DockPanelId.Navigator, DockPanelId.Sheets, DockPanelId.Layers, DockPanelId.Color],
            Shown(w, DockSide.Right));
        Assert.Equal([DockPanelId.Timeline], Shown(w, DockSide.Bottom));
        Assert.Empty(Shown(w, DockSide.Left));
    }

    [AvaloniaFact]
    public void MovingAPanelMovesTheControl()
    {
        var (w, vm) = Open();

        vm.Workspace.Dock(DockPanelId.Color, DockSide.Left, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([DockPanelId.Color], Shown(w, DockSide.Left));
        Assert.DoesNotContain(DockPanelId.Color, Shown(w, DockSide.Right));
    }

    [AvaloniaFact]
    public void AnEmptyEdgeCollapsesAndAFilledOneOpens()
    {
        // "Optional means absent, not disabled": an area with nothing in it
        // takes no width and shows no splitter.
        var (w, vm) = Open();
        var host = w.FindControl<Border>("LeftHost")!;
        var splitter = w.FindControl<GridSplitter>("LeftSplitter")!;
        Assert.False(host.IsVisible);
        Assert.False(splitter.IsVisible);

        vm.Workspace.Dock(DockPanelId.Color, DockSide.Left, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(host.IsVisible);
        Assert.True(splitter.IsVisible);

        vm.Workspace.SetVisible(DockPanelId.Color, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(host.IsVisible);
        Assert.False(splitter.IsVisible);
    }

    [AvaloniaFact]
    public void ClosingAPanelParksItRatherThanDestroyingIt()
    {
        // Which is what makes closing and reopening a no-op rather than a
        // reset: the same control comes back, scroll position and all.
        var (w, vm) = Open();
        var color = Strip(w, DockSide.Right).Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Color);

        vm.Workspace.SetVisible(DockPanelId.Color, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(color, Pool(w).Children);

        vm.Workspace.SetVisible(DockPanelId.Color, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(color, Strip(w, DockSide.Right).Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Color));
    }

    [AvaloniaFact]
    public void TabbedPanelsShareOneSlotAndOneShows()
    {
        // Replaces the two switcher tests that were here. The switcher traded
        // two panels' places; tabs put several in one slot and show one, so
        // there is nothing left for a trade to mean.
        var (w, vm) = Open();

        vm.Workspace.JoinGroup(DockPanelId.Palette, DockPanelId.Color);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // One control in the strip for the pair, carrying both as tabs.
        var shown = Strip(w, DockSide.Right).Children.OfType<Docker>().ToList();
        var docker = Assert.Single(shown, d => d.PanelId == DockPanelId.Palette);
        Assert.NotNull(docker.Tabs);
        Assert.Contains(docker.Tabs!, t => t.Id == DockPanelId.Color);
        Assert.DoesNotContain(shown, d => d.PanelId == DockPanelId.Color);

        // And the hidden tab is parked, not destroyed — the same rule closing a
        // panel has always followed, so it keeps its scroll and its bindings.
        Assert.Single(Pool(w).Children.OfType<Docker>(), d => d.PanelId == DockPanelId.Color);
    }

    [AvaloniaFact]
    public void TheTabShowingIsTheOneThatLooksLikeItIsShowing()
    {
        // B127. The strip carried `SelectedItem="{TemplateBinding ActiveTab}"`,
        // and ActiveTab is a DockPanelId while the items are DockPanelInfo.
        // Handing an enum to SelectedItem is not an error — nothing ever
        // matches, so no tab is selected, and three tabs render identically
        // whichever panel is actually in front.
        //
        // **Nothing failed.** Every test here asked the model which panel was
        // active and got the right answer; the model was never wrong. What was
        // wrong was the one thing no assertion looked at, and it took a
        // screenshot to see it.
        //
        // So this asserts the *rendered* foreground rather than the selection,
        // because a selection that resolves correctly and paints identically is
        // the bug, not the fix.
        var (w, vm) = Open();

        // Sheets joins Layers: a two-tab group made fresh, now that the
        // colour family already ships tabbed.
        vm.Workspace.JoinGroup(DockPanelId.Sheets, DockPanelId.Layers);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var docker = Strip(w, DockSide.Right).Children.OfType<Docker>()
            .First(d => d.Tabs?.Count() == 2);
        var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
        var items = strip.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(2, items.Count);

        var active = Assert.Single(items, i => ((DockPanelInfo)i.DataContext!).Id == docker.ActiveTab);
        var resting = Assert.Single(items, i => i != active);

        static Color Ink(ListBoxItem item) =>
            (item.GetVisualDescendants().OfType<TextBlock>().First().Foreground
             as ISolidColorBrush)!.Color;

        // Both numbers printed, not just the comparison: "it passed" and
        // "230 against 178" are different amounts of evidence, and the second
        // is the one that says whether the difference is visible or a hair.
        var (a, r) = (Ink(active), Ink(resting));
        Assert.True(a.R + a.G + a.B > r.R + r.G + r.B,
            $"active tab {a} is not brighter than the resting tab {r}");

        // And the resting tab is still legible. A tab nobody can read looks
        // disabled, and these are all one click away.
        Assert.True(r.R + r.G + r.B > 3 * 0x60, $"resting tab {r} is too dim to read");

        // The ground is the actual marker, and text weight only supports it —
        // which is why it is asserted separately rather than trusted to follow.
        // The design's panel tab is a sheet edge: lit at the top, fading into
        // the panel below within the tab's own height. A flat fill would be the
        // segmented-control look these are specifically not.
        static Border Tab(ListBoxItem item) =>
            item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Tab");

        var lit = Assert.IsType<LinearGradientBrush>(Tab(active).Background);
        Assert.True(lit.GradientStops[^1].Color.A == 0,
            "the active tab's gradient must end transparent — a named end colour is a "
            + "visible seam on any ground that is not the one it named");
        // The fade runs the tab's whole height. It ended at 0.62 first; the
        // owner's correction is that the subtlety IS the length of the fade,
        // and what keeps it from being a filled block is the soft start and
        // the transparent end, both asserted, not the early stop.
        Assert.True(lit.GradientStops[0].Color.A < 0xFF,
            "the fade must start soft, or the tab is a filled block with a soft bottom");

        // And the purple gradient line along the active tab's TOP edge — the
        // second marker the reference draws, and the one the owner named.
        static Border TopLine(ListBoxItem item) =>
            item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_TopLine");
        Assert.IsType<LinearGradientBrush>(TopLine(active).Background);
        Assert.IsNotType<LinearGradientBrush>(TopLine(resting).Background);

        // The resting tab takes no lit ground — it merges with the header it
        // sits in. What it does carry is an outline, and that is not incidental:
        // a strip where only the active tab has a shape reads as one tab beside
        // two words, which is the same failure as having no marks at all moved
        // along by one.
        Assert.IsNotType<LinearGradientBrush>(Tab(resting).Background);
        Assert.NotNull(Tab(resting).BorderBrush);

        // **The rule runs along the strip and breaks at the active tab.** Each
        // tab draws its own bottom edge, which is what lets the line stop: a
        // single rule under the strip would have to be *covered*, and the
        // active tab cannot cover anything — its gradient ends transparent so
        // it merges into whatever ground it lands on. Anything opaque enough to
        // hide a line would be a seam.
        Assert.Equal(0, Tab(active).BorderThickness.Bottom);
        Assert.True(Tab(resting).BorderThickness.Bottom > 0,
            "a resting tab must close at the bottom, or the rule has nothing to run along");

        // A resting tab has NO box — just its stretch of the rule. The active
        // one keeps the full sheet edge. (The resting outline existed for one
        // round and the owner had it removed: with the ground and the top line
        // carrying the active state, a box on every resting tab was noise.)
        Assert.Equal(0, Tab(resting).BorderThickness.Top);
        Assert.Equal(0, Tab(resting).BorderThickness.Left);
        Assert.True(Tab(active).BorderThickness.Top > 0,
            "the active tab lost its sheet edge");
    }

    [AvaloniaFact]
    public void ALoneDockerWearsItsTitleAsATab()
    {
        // A reversal, and the owner's words are the spec: "I want a tabbed
        // view even if just only one docker is present." Every panel wears the
        // one header treatment, and a slot of one shows exactly one tab, lit —
        // an unlit lone tab reads as a panel that is somehow not showing.
        // Layers is the default layout's lone docker — Sheets joined the work
        // group under Q109 — and the two real groups are asserted elsewhere.
        var (w, _) = Open();

        var lone = Strip(w, DockSide.Right).Children.OfType<Docker>()
            .Where(d => d.PanelId is DockPanelId.Layers)
            .ToList();
        Assert.NotEmpty(lone);
        foreach (var docker in lone)
        {
            Assert.NotNull(docker.Tabs);
            var only = Assert.Single(docker.Tabs!);
            Assert.Equal(docker.PanelId, only.Id);

            var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
            var lit = Assert.IsType<DockPanelInfo>(strip.SelectedItem);
            Assert.Equal(docker.PanelId, lit.Id);
        }
    }

    [AvaloniaFact]
    public void GroupingAPanelMarksTheWorkspaceUnsaved()
    {
        // The session contract: rearranging is live but not persisted, and the
        // star is what says so. It also proves the change went through Mutate
        // rather than writing to the layout behind its back.
        var (_, vm) = Open();
        Assert.False(vm.Workspace.IsDirty);

        vm.Workspace.JoinGroup(DockPanelId.Palette, DockPanelId.Color);

        Assert.True(vm.Workspace.IsDirty);
        Assert.EndsWith("*", vm.Workspace.CurrentLabel);
    }

    [AvaloniaFact]
    public void TheProjectPanelAppearsAsSoonAsThereIsAProject()
    {
        // It was staying hidden: HasProject is a forwarding property with no
        // notification of its own, and adopting a project is not an edit, so
        // the docker's change callback never fired for it.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-ws-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (w, vm) = Open();
            Assert.DoesNotContain(DockPanelId.Project, Shown(w, DockSide.Right));

            vm.NewProject(root, "Knight");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Contains(DockPanelId.Project, Shown(w, DockSide.Right));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheCanvasGetsTheRoomLeftOverByTheStrips()
    {
        // It did not. The canvas host was left in the left strip's grid column
        // when the docking rework renumbered them — an Auto column, empty by
        // default, so it collapsed to the width of the zoom bar floating on
        // top of it. The canvas had no width, so it drew nothing and took no
        // pointer events, and the app opened on what looked like a dead
        // renderer.
        var (w, _) = Open();
        var canvas = w.FindControl<Panel>("CanvasHost")!;
        var left = w.FindControl<Border>("LeftHost")!;
        var right = w.FindControl<Border>("RightHost")!;

        Assert.NotEqual(Grid.GetColumn(left), Grid.GetColumn(canvas));
        Assert.NotEqual(Grid.GetColumn(right), Grid.GetColumn(canvas));
        // The starred column: whatever the strips leave, and never less than
        // its declared minimum.
        Assert.True(canvas.Bounds.Width >= 240,
            $"the canvas needs the leftover room, got {canvas.Bounds.Width}");
        Assert.True(canvas.Bounds.Height >= 100,
            $"the canvas needs the leftover height, got {canvas.Bounds.Height}");
    }

    [AvaloniaFact]
    public void TheProjectRowMenuActuallyDoesSomethingWhenClicked()
    {
        // The failure this guards is silent. A flyout's items live in a popup
        // rather than in the window's tree, so a `$parent[Window]` binding —
        // the pattern the ＋ menu uses — resolves to nothing here and leaves
        // every item looking correct and doing nothing. Raising the real Click
        // is the only way to tell the difference.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-menu-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (w, vm) = Open();
            vm.NewProject(root, "Knight");
            WithKnight(vm);
            vm.Save();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var list = w.FindControl<ListBox>("ProjectRows")!;
            var host = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0))
                .GetVisualDescendants().OfType<DockPanel>()
                .First(p => p.ContextFlyout is MenuFlyout);
            var flyout = (MenuFlyout)host.ContextFlyout!;
            flyout.ShowAt(host);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToList();
            flyout.Hide();

            Assert.Equal(
                // B87 added "Delete permanently…" beside "Remove from project".
                // This flyout is written out in XAML, so this assertion is what
                // proves a new entry actually reached the menu.
                // The declaration submenus — five shares, an export preset, a
                // template default, reference, reach and unshare — moved to
                // the project manager's Assets tab, where every scope is one
                // row of one table. What stays is what an artist does while
                // drawing, plus the one entry that points at where the rest
                // went; ProjectWindowTests guards the moved surface.
                ["Open", "Open with default app…", "Show in file manager", "Copy path",
                 "Duplicate",
                 // Q143's two override gestures. The first one's header is
                 // dynamic — "Give “Winter Armour” its own “Walk”" — and with
                 // no variant being viewed it resolves empty, which is also
                 // what hides it; the empty string here is that binding
                 // resolving, which is exactly what this test exists to prove.
                 "", "Use the shared drawing again",
                 "Rename…",
                 // Q75. The docker row's road to the history window; the File
                 // menu is the other, and both open the same window.
                 "Version history…",
                 // Q38. The artist says what a folder is; nothing derives it.
                 "Glyph", "Glyph — something else…",
                 // Q143. Which version of the subject is on screen.
                 "Variant",
                 // Q30's last mile: the plan was countable and describable and
                 // no view called either, so these two are what make it real.
                 "Export this folder…", "Test export",
                 // A reading belongs to a folder, and folders live in this
                 // panel — so the gesture is here rather than in the AI bar,
                 // which acts on the open drawing.
                 "Read this folder…",
                 "Share & assets… (project manager)",
                 "Remove from project", "Delete permanently…", "Status"],
                items.Select(i => i.Header?.ToString()).ToList());

            vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
            Click(items, "Copy path");
            Assert.EndsWith(".lightbox.json", vm.ProjectDocker.CopiedPath);

            Click(items, "Duplicate");
            Assert.Equal(2, vm.ProjectDocker.Project!.Manifest.Documents.Count);

            Click(items, "Rename…");
            Assert.True(vm.ProjectDocker.Selected!.IsRenaming);

            // The status items are one level down, and they are exactly the kind of
            // nested flyout item this test exists to catch: they read their status off
            // Tag, so a typo there is inert rather than loud.
            var status = items.Single(i => i.Header?.ToString() == "Status");
            var options = status.Items.OfType<MenuItem>().ToList();
            Assert.Equal(
                ["Not set", "Design", "Draft", "In development", "Review", "Ready", "Reopened"],
                options.Select(i => i.Header?.ToString()).ToList());

            // Duplicate left the selection on the copy, and a copy is explicitly not on
            // disk yet — DuplicateSelected says "Save to write it to disk". So this is the
            // refusal case, through the real menu: a status is a message to a designer
            // about a file, and there is no file. Headless resolves the prompt as
            // dismissed, which is the "Revert status change" answer.
            var copy = vm.ProjectDocker.Selected!;
            Assert.Null(copy.Animation!.Status);
            Click(options, "Ready");
            Assert.Null(copy.Status);
            Assert.Null(copy.Animation!.Status);

            // Once it is written, the same click sticks.
            vm.Save();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Click(options, "Ready");
            Assert.Equal(Lightbox.Core.Projects.AssetStatus.Ready, copy.Status);
            // Written through to the manifest, not only onto the row.
            Assert.Equal(Lightbox.Core.Projects.AssetStatus.Ready, copy.Animation!.Status);

            Click(options, "Not set");
            Assert.Null(copy.Status);
            Assert.Null(copy.Animation!.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheNewMenuActuallyMakesThings()
    {
        // Same failure mode as the row menu, in the header. It used to build
        // its items from NewItemKinds with a `$parent[Window]` command binding,
        // which a popup cannot resolve.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-new-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (w, vm) = Open();
            vm.NewProject(root, "Knight");
            WithKnight(vm);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var button = w.GetVisualDescendants().OfType<Button>()
                .First(b => b.Flyout is MenuFlyout && Equals(b.Content, "＋ New ▾"));
            var flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToList();
            flyout.Hide();

            // B86. The flyout is written out in XAML rather than generated, so
            // this is the check that a new kind actually reached the menu — a
            // create kind the artist cannot click is not reachable at all.
            //
            // B63 added the glyphs and the grouping, and the headers carry them:
            // the menu has to say which entries are containers, and saying it in
            // a tooltip nobody hovers is not saying it.
            Assert.Equal(
                ["🗀  Folder", "▣  Document"],
                items.Select(i => i.Header?.ToString()).ToList());

            // Every entry is wired to a handler. Clicking used to be assertable
            // end-to-end here; B65 put a name prompt in front of each one, and a
            // modal dialog cannot be answered headlessly — so the click half of
            // this test moved to what it can still prove, and the "does it make
            // anything" half moved down to the view model.
            //
            // That is a real loss of coverage and is named rather than hidden:
            // the failure this test was written for — a menu entry bound to
            // nothing — would now show up as a prompt that appears and creates
            // nothing when answered, which only a person can see.
            Assert.All(items, i => Assert.True(i.IsEnabled, $"“{i.Header}” is disabled"));

            // The half that is still mechanical: each kind creates its thing.
            var docker = vm.ProjectDocker;
            docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Walk");
            Assert.Equal(2, docker.Project!.Manifest.Documents.Count);

            docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Squire");
            var squire = ProjectFolders.All(docker.Project!.Manifest).Single(f => f.Name == "Squire");
            Assert.Equal(2, ProjectFolders.All(docker.Project!.Manifest).Count);

            // Creating selects what it made, so the next one lands in it — B85's
            // "created where you are", which after B114 is the one rule for every
            // container rather than one for folders and another for characters.
            docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Colour test");
            Assert.Equal(
                squire.Id,
                docker.Project.Manifest.Documents.Single(d => d.Name == "Colour test").FolderId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Click(IEnumerable<MenuItem> items, string header) =>
        items.First(i => Equals(i.Header, header))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void TheReferencePanelIsAbsentUntilItIsAskedFor()
    {
        // Same rule as the palette and the gradient: an animation reference is
        // something you set up deliberately, and empty it is sidebar height
        // the layers could be using.
        var (w, vm) = Open();
        Assert.DoesNotContain(DockPanelId.Reference, Shown(w, DockSide.Right));
        Assert.False(vm.ReferenceDockerVisible);

        vm.ToggleReferenceDockerCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(DockPanelId.Reference, Shown(w, DockSide.Right));
    }

    [AvaloniaFact]
    public void ACappedStripIsNoWiderThanItsPanelsCanUse()
    {
        // A sidebar of fixed-size controls widened past its cap is just
        // whitespace. A panel with real use for the room removes the ceiling.
        var (w, vm) = Open();
        var work = w.FindControl<Grid>("WorkArea")!;
        var right = work.ColumnDefinitions[6];

        vm.Workspace.SetVisible(DockPanelId.Layers, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(320, right.MaxWidth);   // Color and Sheets both cap at 320

        vm.Workspace.SetVisible(DockPanelId.Layers, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(double.PositiveInfinity, right.MaxWidth);
    }
}
