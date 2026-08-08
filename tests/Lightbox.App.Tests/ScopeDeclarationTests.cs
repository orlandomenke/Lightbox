using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Q30: declaring a scope from the project docker, and the two recolour defects
/// that scoping made worth fixing (B102, B103).
/// </summary>
[Collection("BrushState")]
public sealed class ScopeDeclarationTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-scope-{Guid.NewGuid():N}.lbproj");

    private readonly List<MainViewModel> _built = [];

    public new void Dispose()
    {
        foreach (var vm in _built) vm.ProjectDocker.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        _built.Add(vm);
        vm.NewProject(_root, "Production");
        return vm;
    }

    /// <summary>Sharing a palette onto a folder is what scopes the project.</summary>
    /// <remarks>
    /// The moment an old project becomes a new one: before the first
    /// declaration every palette is offered to every document, after it the
    /// resolver is taken at its word. Reversible by taking the last one back,
    /// because a project with no declarations reads as unscoped.
    /// </remarks>
    [AvaloniaFact]
    public void SharingAPaletteOntoAFolderScopesTheProject()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        var manifest = docker.Project!.Manifest;
        Assert.False(PaletteScopes.AnyDeclared(manifest));

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var palette = Assert.Single(docker.ShareablePalettes);
        output.WriteLine($"sharing “{palette.Name}” with {docker.ShareScopeLabel}");

        docker.SharePalette(palette.Id);

        Assert.True(PaletteScopes.AnyDeclared(manifest));
        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(palette.Id, declared.Id);
        Assert.Equal(ResourceReach.Subtree, declared.ReachOrDefault);

        // And taking it back leaves the project as it was, rather than scoped
        // to nothing — which would hide every swatch from every document.
        docker.UnshareDeclaration(declared);
        Assert.False(PaletteScopes.AnyDeclared(manifest));
        Assert.Empty(docker.DeclarationsOnSelected);
    }

    /// <summary>Sharing the same palette twice does not declare it twice.</summary>
    [AvaloniaFact]
    public void SharingTheSamePaletteTwiceIsOneDeclaration()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var palette = docker.ShareablePalettes[0];

        docker.SharePalette(palette.Id);
        docker.SharePalette(palette.Id);
        Assert.Single(docker.DeclarationsOnSelected);
    }

    /// <summary>Promoting a declaration lets the whole project see it.</summary>
    [AvaloniaFact]
    public void PromotingADeclarationReachesTheWholeProject()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Library");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Library");
        docker.SharePalette(docker.ShareablePalettes[0].Id);

        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(ResourceReach.Subtree, declared.ReachOrDefault);
        docker.PromoteDeclaration(declared);
        Assert.Equal(ResourceReach.Project, declared.ReachOrDefault);
    }

    /// <summary>
    /// <b>B102.</b> A recolour repaints every open document that uses the swatch.
    /// </summary>
    /// <remarks>
    /// Two documents open, both painted from the shared palette, and the
    /// unfocused one used to keep the old colour from cache. Asserted through
    /// the frame cache rather than pixels: the claim is that the other tab's
    /// frames were <em>invalidated</em>, and reading pixels would also pass on a
    /// build that repainted everything unconditionally — which is the thing the
    /// stroke-walk exists to avoid.
    /// </remarks>
    [AvaloniaFact]
    public void RecolouringRepaintsEveryOpenDocumentThatUsesTheSwatch()
    {
        var vm = Vm();
        var palette = Assert.Single(vm.ProjectDocker.Project!.Palettes);
        var swatch = palette.Swatches[0];

        // Paint from the swatch in the first document, then in a second.
        vm.PickSwatchForTest(swatch.Id);
        vm.BeginStroke(20, 20, 0.5);
        vm.MoveStroke(60, 60, 0.5);
        vm.EndStroke();
        var first = vm.ActiveTab!;

        vm.NewDocument(new NewDocumentSettings("Second", 320, 240, 12, 72, "#ffffff", false));
        vm.PickSwatchForTest(swatch.Id);
        vm.BeginStroke(20, 20, 0.5);
        vm.MoveStroke(60, 60, 0.5);
        vm.EndStroke();
        var second = vm.ActiveTab!;
        Assert.NotSame(first, second);

        var offscreen = first.Doc.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .OfType<Frame>()
            .Single(f => f.Strokes.Any(st => st.SwatchId == swatch.Id));

        var row = Assert.Single(vm.PaletteDocker.Swatches, r => r.Id == swatch.Id);
        row.Color = "#00ff00";
        vm.CommitSwatchEdit();

        output.WriteLine($"active tab “{second.Title}”, other tab “{first.Title}” frame {offscreen.Id}");
        // The unfocused document's frame was dropped from the cache, so it will
        // re-render from the record with the new colour rather than serving the
        // pixels it had.
        Assert.False(vm.IsFrameCached(offscreen.Id));
    }

    /// <summary>
    /// <b>B103.</b> Undoing a recolour of a <em>project</em> palette restores it.
    /// </summary>
    /// <remarks>
    /// Deliberately a project palette rather than a document one. The document
    /// case passed while this was broken, because the undo step walked
    /// <c>doc.Palettes</c> — a test that used one would have gone on passing and
    /// said nothing.
    /// </remarks>
    [AvaloniaFact]
    public void UndoingARecolourOfAProjectPaletteRestoresIt()
    {
        var vm = Vm();
        var palette = Assert.Single(vm.ProjectDocker.Project!.Palettes);
        var swatch = palette.Swatches[0];
        var before = swatch.Color;

        // Through the docker's own row, which is how the colour wheel drives it:
        // the swatch object is mutated in place and the edit is closed off as
        // one undo step. That in-place mutation is exactly what made the bug
        // invisible until somebody pressed undo.
        var row = Assert.Single(vm.PaletteDocker.Swatches, r => r.Id == swatch.Id);
        row.Color = "#123456";
        vm.CommitSwatchEdit();
        Assert.Equal("#123456", swatch.Color);

        vm.UndoCommand.Execute(null);
        output.WriteLine($"{before} -> #123456 -> {swatch.Color}");
        Assert.Equal(before, swatch.Color);
    }

    // ---- the other four kinds, declarable at last ---------------------------
    //
    // The mechanism resolved six kinds and the menu could declare two. These
    // are the four that could not be reached, and the two verbs — take back,
    // change reach — that every kind needs and none had.

    private ProjectViewModel WithFolder(MainViewModel vm, string name)
    {
        var docker = vm.ProjectDocker;
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, name);
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == name);
        return docker;
    }

    [AvaloniaFact]
    public void AGradientCanBeSharedWithAFolder()
    {
        var vm = Vm();
        var gradient = new Gradient { Name = "Sunset" };
        vm.ProjectDocker.Project!.Gradients[gradient.Id] = gradient;

        var docker = WithFolder(vm, "Backgrounds");
        Assert.False(GradientScopes.AnyDeclared(docker.Project!.Manifest));

        docker.ShareGradientEntryCommand.Execute(
            Assert.Single(docker.ShareableGradients, g => g.Name == "Sunset"));

        Assert.True(GradientScopes.AnyDeclared(docker.Project!.Manifest));
        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(gradient.Id, declared.Id);
        Assert.Equal(GradientScopes.Kind, declared.Kind);
    }

    [AvaloniaFact]
    public void GuidesCanBeSharedWithAFolder()
    {
        // The roadmap's character height guide, which was never anything other
        // than a guide set plus a declaration — and had no gesture for either.
        var vm = Vm();
        var guides = new GuideSet { Name = "Knight height" };
        (vm.ProjectDocker.Project!.Manifest.GuideSets ??= []).Add(guides);

        var docker = WithFolder(vm, "Knight");
        docker.ShareGuideSetEntryCommand.Execute(Assert.Single(docker.ShareableGuideSets));

        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(guides.Id, declared.Id);
        Assert.Equal(GuideScopes.Kind, declared.Kind);
    }

    /// <summary>A scope has one default template, so declaring twice replaces.</summary>
    [AvaloniaFact]
    public void ADefaultTemplateReplacesRatherThanAccumulating()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;

        // Two templates, so "replaces" is distinguishable from "adds".
        var first = ProjectIo.AddDocument(docker.Project!, "Rough sheet", DocumentFactory.CreateDoc(64, 64));
        var second = ProjectIo.AddDocument(docker.Project!, "Clean sheet", DocumentFactory.CreateDoc(64, 64));
        foreach (var reference in new[] { first, second })
        {
            Templates.SetTemplate(ProjectIo.LoadDocument(docker.Project!, reference)!, true);
        }
        vm.SaveProject();

        docker = WithFolder(vm, "Locomotion");
        var folder = docker.Selected!.Folder!;
        Assert.Equal(2, docker.ShareableTemplates.Count);

        docker.SetDefaultTemplateEntryCommand.Execute(
            docker.ShareableTemplates.Single(t => t.Name == "Rough sheet"));
        docker.SetDefaultTemplateEntryCommand.Execute(
            docker.ShareableTemplates.Single(t => t.Name == "Clean sheet"));

        // One declaration, and it is the second — two of a one-at-a-time kind
        // would make which-one-wins depend on insertion order.
        var declared = Assert.Single(docker.DeclarationsOnSelected);
        Assert.Equal(second.Id, declared.Id);
        Assert.Equal(second.Id, TemplateScopes.DefaultFor(docker.Project!.Manifest, folder));
    }

    /// <summary>
    /// A drawing becomes reference for its neighbours, or for everything.
    /// </summary>
    /// <remarks>
    /// Workflows 3 and 4 — the environment layout everything draws against, the
    /// sword in the asset library. Both are <see cref="ResourceReach.Project"/>,
    /// which existed in the record and in the resolver and had no gesture, so
    /// the two workflows the design was written for were the two that could not
    /// be performed.
    /// </remarks>
    [AvaloniaFact]
    public void ADrawingCanBeMadeReferenceForItsNeighboursOrForEverything()
    {
        var vm = Vm();
        var docker = WithFolder(vm, "Environments");
        var folder = docker.Selected!.Folder!;
        var layout = ProjectIo.AddDocument(
            docker.Project!, "World layout", DocumentFactory.CreateDoc(64, 64));
        ProjectFolders.FileDocument(docker.Project!.Manifest, layout, folder);
        docker.Refresh();
        docker.Selected = Assert.Single(docker.Rows, r => r.Animation?.Id == layout.Id);

        docker.ShareSelectedAsReference(projectWide: false);

        // Declared on the drawing's own folder, not on the selection — the
        // selection is the drawing.
        var declared = Assert.Single(folder.Resources!);
        Assert.Equal(layout.Id, declared.Id);
        Assert.Equal(ReferenceScopes.Kind, declared.Kind);
        Assert.Equal(ReferenceTargets.Document, declared.Target);
        Assert.Equal(ResourceReach.Subtree, declared.ReachOrDefault);

        // A drawing filed somewhere else entirely cannot see it yet…
        var elsewhere = ProjectIo.AddDocument(
            docker.Project!, "Knight walk", DocumentFactory.CreateDoc(64, 64));
        Assert.DoesNotContain(
            layout.Id,
            ReferenceScopes.VisibleTo(docker.Project!.Manifest, elsewhere)?.Select(r => r.Id) ?? []);

        // …and does once it is published, which is the whole of workflow 3.
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Environments");
        docker.PublishEntryCommand.Execute(Assert.Single(docker.Declarations));
        output.WriteLine($"reach is now {declared.ReachOrDefault}");
        Assert.Equal(ResourceReach.Project, declared.ReachOrDefault);
        Assert.Contains(
            layout.Id,
            ReferenceScopes.VisibleTo(docker.Project!.Manifest, elsewhere)!.Select(r => r.Id));
    }

    /// <summary>Publishing is a toggle, and unpublishing writes no key.</summary>
    /// <remarks>
    /// The second half is the one worth asserting: setting the reach back to
    /// <see cref="ResourceReach.Subtree"/> explicitly would serialize a
    /// <c>reach</c> key saying nothing, so a publish-then-unpublish round trip
    /// would be visible in the file. Optional means absent.
    /// </remarks>
    [AvaloniaFact]
    public void UnpublishingLeavesTheRecordAsAnOrdinaryDeclaration()
    {
        var vm = Vm();
        var docker = WithFolder(vm, "Library");
        docker.SharePalette(docker.ShareablePalettes[0].Id);
        var row = Assert.Single(docker.Declarations);

        docker.PublishEntryCommand.Execute(row);
        Assert.Equal(ResourceReach.Project, row.Resource.ReachOrDefault);
        Assert.Contains("project-wide", Assert.Single(docker.Declarations).LabelWithReach);

        docker.PublishEntryCommand.Execute(Assert.Single(docker.Declarations));
        Assert.Null(row.Resource.Reach);
        Assert.DoesNotContain("project-wide", Assert.Single(docker.Declarations).LabelWithReach);
    }

    /// <summary>Every declaration reads as its name, and can be taken back.</summary>
    [AvaloniaFact]
    public void ADeclarationSaysWhatItIsAndCanBeUndone()
    {
        var vm = Vm();
        var docker = WithFolder(vm, "Knight");
        var palette = docker.ShareablePalettes[0];
        docker.SharePalette(palette.Id);

        var row = Assert.Single(docker.Declarations);
        output.WriteLine(row.Label);
        Assert.Equal($"Palette: {palette.Name}", row.Label);

        docker.UnshareEntryCommand.Execute(row);
        Assert.Empty(docker.Declarations);
        Assert.False(docker.HasDeclarations);
        // Back to unscoped, so every document sees every palette again rather
        // than being scoped to nothing.
        Assert.False(PaletteScopes.AnyDeclared(docker.Project!.Manifest));
    }

    /// <summary>
    /// A declaration whose resource was deleted is still visible and removable.
    /// </summary>
    /// <remarks>
    /// The failure otherwise: the resolver silently drops it, the artist sees a
    /// picker missing a palette, and there is nothing in the panel pointing at
    /// the cause. Falling back to the id is ugly and legible, which is the
    /// right trade for a state that should not happen.
    /// </remarks>
    [AvaloniaFact]
    public void ADeclarationPointingAtNothingStillShowsAndStillClears()
    {
        var vm = Vm();
        var docker = WithFolder(vm, "Knight");
        ResourceScopes.Declare(
            docker.Project!.Manifest, docker.Selected!.Folder, PaletteScopes.Kind, "pal-deleted");

        docker.Selected = docker.Selected;  // re-raise, as a real selection would
        var row = Assert.Single(docker.Declarations);
        Assert.Equal("Palette: pal-deleted", row.Label);

        docker.UnshareEntryCommand.Execute(row);
        Assert.Empty(docker.Declarations);
    }


    // ---- symbols gain the tree axis (Q30 step 5) ----------------------------

    /// <summary>
    /// Declaring a symbol on a folder narrows the picker, and only there.
    /// </summary>
    /// <remarks>
    /// Through the browser rather than through <c>SymbolScopes</c>, because the
    /// resolver was already right and what was missing was anything reading it.
    /// The grid is the whole point of the step: it is where a narrowing is
    /// either visible or pointless.
    /// </remarks>
    [AvaloniaFact]
    public void DeclaringASymbolNarrowsTheGridForOtherFolders()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        var project = docker.Project!;

        var sword = new Symbol { Name = "Sword", Kind = SymbolKind.Prop };
        var club = new Symbol { Name = "Club", Kind = SymbolKind.Prop };
        project.Symbols[sword.Id] = sword;
        project.Symbols[club.Id] = club;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var knight = docker.Selected!.Folder!;
        docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Walk");
        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Goblin");

        // Unscoped: the grid offers both, which is every project today.
        vm.SymbolBrowser.Refresh();
        Assert.Equal(2, vm.SymbolBrowser.Rows.Count);

        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.ShareSymbolEntryCommand.Execute(
            Assert.Single(docker.ShareableSymbols, s => s.Name == "Sword"));
        output.WriteLine(docker.Status);
        // The first declaration is the one that changes what everything sees,
        // so it says so rather than leaving the artist to notice.
        Assert.Contains("now scoped", docker.Status);

        // The knight's drawing sees the sword and not the club.
        var walk = project.Manifest.Documents.Single(d => d.Name == "Walk");
        Assert.Equal(knight.Id, walk.FolderId);
        Assert.Equal([sword.Id], SymbolScopes.VisibleTo(project.Manifest, walk)!);
        Assert.True(SymbolScopes.CanPlace(project.Manifest, walk, sword.Id));
        Assert.False(SymbolScopes.CanPlace(project.Manifest, walk, club.Id));
    }

    /// <summary>
    /// A symbol declaration reads as a symbol, and can be taken back.
    /// </summary>
    [AvaloniaFact]
    public void ASymbolDeclarationSaysWhatItIsAndUndoesToUnscoped()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        var sword = new Symbol { Name = "Sword", Kind = SymbolKind.Prop };
        docker.Project!.Symbols[sword.Id] = sword;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.ShareSymbolEntryCommand.Execute(docker.ShareableSymbols[0]);

        var row = Assert.Single(docker.Declarations);
        Assert.Equal("Symbol: Sword", row.Label);

        docker.UnshareEntryCommand.Execute(row);
        // Back to project-wide, which is what an old project means — not scoped
        // to nothing, which would empty every picker in the project.
        Assert.False(SymbolScopes.AnyDeclared(docker.Project!.Manifest));
        Assert.Null(SymbolScopes.VisibleTo(
            docker.Project!.Manifest, docker.Project!.Manifest.Documents.FirstOrDefault()));
    }


    // ---- brush tips, the design doc's loose end ------------------------------

    /// <summary>
    /// Declaring a tip narrows the picker, and leaves the user library alone.
    /// </summary>
    /// <remarks>
    /// Through <c>TipStore.Available</c> rather than through <c>TipScopes</c>,
    /// because the resolver was the easy half — what this closes is a promise
    /// the design doc made and nothing read. The second assertion is the one
    /// worth having: a scope must not reach the artist's own library, which
    /// follows them between projects and is not the project's to narrow.
    /// </remarks>
    [AvaloniaFact]
    public void DeclaringABrushTipNarrowsTheProjectsOwnAndNotTheUserLibrary()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        var manifest = docker.Project!.Manifest;

        var nib = new BrushTip { Name = "Nib", Png = "x" };
        var wash = new BrushTip { Name = "Wash", Png = "x" };
        (manifest.Tips ??= []).AddRange([nib, wash]);
        var mine = new Lightbox.App.Services.TipStore.State
        {
            Tips = { new BrushTip { Name = "My marker", Png = "x" } },
        };

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Inking");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Inking");
        docker.AddItemNamed(ProjectViewModel.NewDocumentItem, "Clean-up");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Inking");

        // Unscoped: both project tips offered, plus the user's.
        var before = Lightbox.App.Services.TipStore.Available(
            docker.Project!, mine, includeBuiltIn: false).Select(t => t.Name).ToList();
        Assert.Contains("Nib", before);
        Assert.Contains("Wash", before);

        docker.ShareTipEntryCommand.Execute(
            Assert.Single(docker.ShareableTips, t => t.Name == "Nib"));
        output.WriteLine(docker.Status);
        Assert.Contains("now scoped", docker.Status);

        var line = manifest.Documents.Single(d => d.Name == "Clean-up");
        var after = Lightbox.App.Services.TipStore.Available(
            docker.Project!, mine, includeBuiltIn: false, inView: line).Select(t => t.Name).ToList();

        output.WriteLine($"before: {string.Join(", ", before)} / after: {string.Join(", ", after)}");
        Assert.Contains("Nib", after);
        Assert.DoesNotContain("Wash", after);
        // The artist's own library is not the project's to narrow.
        Assert.Contains("My marker", after);
    }

    /// <summary>A tip declaration reads as one, and undoes to unscoped.</summary>
    [AvaloniaFact]
    public void ATipDeclarationSaysWhatItIsAndUndoesToUnscoped()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        var nib = new BrushTip { Name = "Nib", Png = "x" };
        (docker.Project!.Manifest.Tips ??= []).Add(nib);

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Inking");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Inking");
        docker.ShareTipEntryCommand.Execute(docker.ShareableTips[0]);

        var row = Assert.Single(docker.Declarations);
        Assert.Equal("Brush tip: Nib", row.Label);

        docker.UnshareEntryCommand.Execute(row);
        Assert.False(TipScopes.AnyDeclared(docker.Project!.Manifest));
    }
}
