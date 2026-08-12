using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The project window — Q29's second surface, in its own window by Q41.
/// </summary>
/// <remarks>
/// Driven through the view model with no window at all, which is the point of
/// the split: a bulk edit, a filter and a count are all answerable here, and
/// only the dialog needs a person. The one thing the window owns is the
/// multi-selection, handed over by <c>SetSelection</c>.
/// </remarks>
public sealed class ProjectWindowTests(ITestOutputHelper output)
{
    private static (ProjectWindowViewModel Vm, Project P, ProjectFolder Knight,
                    DocumentRef Walk, DocumentRef Idle, DocumentRef Loose) Open(
        Action? changed = null)
    {
        var project = ProjectIo.Create("Production", "/nowhere.lbproj");
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var walk = Add(project, "walk", knight);
        var idle = Add(project, "idle", knight);
        var loose = Add(project, "background", null);
        return (new ProjectWindowViewModel(project, changed), project, knight, walk, idle, loose);
    }

    private static DocumentRef Add(Project project, string name, ProjectFolder? folder)
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        return ProjectIo.AddDocument(project, name, doc, folder);
    }

    // ---- it reads the one tree ------------------------------------------------------

    [Fact]
    public void TheWindowAndTheDockerListTheSameDocuments()
    {
        // Q29's whole point: the hierarchy is Core model code that both surfaces
        // read, so the window cannot grow a second implementation of the tree.
        var (vm, project, _, _, _, _) = Open();
        var docker = new ProjectViewModel(
            () => DocumentFactory.CreateDoc(64, 64, 12), (_, _) => { }, () => { })
        {
            Project = project,
        };

        Assert.Equal(
            docker.Rows.Where(r => r.Animation is not null).Select(r => r.Animation!.Id),
            vm.Rows.Where(r => r.Document is not null).Select(r => r.Document!.Id));
    }

    [Fact]
    public void AFolderIsFollowedByWhatIsInIt()
    {
        var (vm, _, knight, _, _, loose) = Open();

        Assert.Equal(knight.Id, vm.Rows[0].Folder!.Id);
        Assert.True(vm.Rows[0].IsFolder);
        Assert.All(vm.Rows.Skip(1).Take(2), r => Assert.Equal(knight.Id, r.Folder!.Id));
        // A document filed nowhere comes last, unindented.
        Assert.Equal(loose.Id, vm.Rows[^1].Document!.Id);
        Assert.Equal(0, vm.Rows[^1].Indent);
    }

    // ---- bulk edits (Q44: no undo, so each one says what it did) -----------------------

    [Fact]
    public void SettingAStatusOnASelectionChangesEveryDocumentInIt()
    {
        var (vm, _, _, walk, idle, _) = Open();
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));

        vm.SetStatus(AssetStatus.Ready);

        Assert.Equal(AssetStatus.Ready, walk.Status);
        Assert.Equal(AssetStatus.Ready, idle.Status);
        output.WriteLine(vm.Status);
        Assert.Contains("3 rows", vm.Status);
    }

    [Fact]
    public void AFolderInTheSelectionIsSkippedByAStatusEdit()
    {
        // A folder has no status, and inventing one for it would put a value on
        // a row the model has no field for.
        var (vm, _, knight, _, _, _) = Open();
        vm.SetSelection(vm.Rows);

        vm.SetStatus(AssetStatus.Ready);

        Assert.Contains("3 rows", vm.Status);
        Assert.Equal(3, vm.Rows.Count(r => r.Status == AssetStatus.Ready));
        _ = knight;
    }

    [Fact]
    public void TaggingASelectionTakesFoldersAndDocumentsAlike()
    {
        var (vm, _, knight, walk, _, _) = Open();
        // By id rather than by position: an unarranged folder lists by name, so
        // "the first document" is idle rather than walk.
        vm.SetSelection(vm.Rows.Where(
            r => (r.IsFolder && r.Folder!.Id == knight.Id) || r.Document?.Id == walk.Id));

        vm.TagSelection("hero");

        Assert.Equal(["hero"], knight.Tags);
        Assert.Equal(["hero"], walk.Tags);
        Assert.Contains("2 rows", vm.Status);
    }

    [Fact]
    public void ABulkEditThatChangedNothingSaysSo()
    {
        // Q44 chose no undo on the grounds that the window says what it did —
        // which only holds if it also says when it did nothing.
        var (vm, _, _, _, _, _) = Open();
        vm.SetSelection(vm.Rows.Take(1));
        vm.TagSelection("hero");

        vm.TagSelection("hero");

        Assert.Equal("Nothing changed.", vm.Status);
    }

    [Fact]
    public void AnEditTellsTheOwnerTheProjectMoved()
    {
        // The docker is showing the same manifest, and two surfaces writing one
        // project with neither knowing is the class of bug B61 was.
        var told = 0;
        var (vm, _, _, _, _, _) = Open(changed: () => told++);
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));

        vm.SetStatus(AssetStatus.Draft);

        Assert.Equal(1, told);
    }

    [Fact]
    public void TheSelectionSurvivesTheRebuildAnEditCauses()
    {
        // Otherwise setting a status would clear the selection and the second
        // bulk edit would act on nothing — which reads as the button being
        // broken rather than as the rows having been replaced.
        var (vm, _, _, _, _, _) = Open();
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));

        vm.SetStatus(AssetStatus.Draft);
        vm.SetStatus(AssetStatus.Ready);

        Assert.Equal(3, vm.Rows.Count(r => r.Status == AssetStatus.Ready));
    }

    // ---- filters -----------------------------------------------------------------------

    [Fact]
    public void ATagFilterKeepsAFolderOnlyForWhatIsUnderIt()
    {
        var (vm, _, _, walk, _, _) = Open();
        vm.SetSelection([vm.Rows.First(r => r.Document?.Id == walk.Id)]);
        vm.TagSelection("rough");

        vm.TagFilter = "rough";

        Assert.Equal([walk.Id], vm.Rows.Where(r => r.Document is not null).Select(r => r.Document!.Id));
        // The folder stays, because something under it matched — a matching
        // document with no path back to the root reads as a broken tree.
        Assert.Contains(vm.Rows, r => r.IsFolder);
    }

    [Fact]
    public void AFolderTagReachesTheDocumentsUnderItInTheFilterToo()
    {
        // Q31's point at the surface: tagging `characters/` once is how "every
        // character animation" is expressible without listing them.
        var (vm, _, knight, _, _, loose) = Open();
        vm.SetSelection([vm.Rows.First(r => r.Folder?.Id == knight.Id && r.IsFolder)]);
        vm.TagSelection("hero");

        vm.TagFilter = "hero";

        Assert.Equal(2, vm.Rows.Count(r => r.Document is not null));
        Assert.DoesNotContain(vm.Rows, r => r.Document?.Id == loose.Id);
    }

    [Fact]
    public void ClearingAFilterBringsEverythingBack()
    {
        var (vm, _, _, _, _, _) = Open();
        var all = vm.Rows.Count;
        vm.TagFilter = "nothing-is-tagged-this";
        Assert.Empty(vm.Rows);

        vm.ClearTagFilterCommand.Execute(null);

        Assert.Equal(all, vm.Rows.Count);
    }

    [Fact]
    public void TheStatusBoardNarrowsWithTheFilter()
    {
        // Otherwise filtering the tree and then looking at the board would show
        // two different projects.
        var (vm, _, _, walk, _, _) = Open();
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));
        vm.SetStatus(AssetStatus.Ready);
        vm.SetSelection([vm.Rows.First(r => r.Document?.Id == walk.Id)]);
        vm.TagSelection("rough");

        vm.TagFilter = "rough";

        var ready = vm.Columns.Single(c => c.Status == AssetStatus.Ready);
        Assert.Single(ready.Rows);
    }

    // ---- the status board ----------------------------------------------------------------

    [Fact]
    public void NoStatusIsItsOwnColumnAndComesLast()
    {
        // The distinction DocumentRef.Status exists to keep: a project imported
        // from loose files has no statuses, and folding them into Design would
        // invent a pipeline stage they were never in.
        var (vm, _, _, _, _, _) = Open();

        Assert.Equal(AssetStatuses.InOrder.Count + 1, vm.Columns.Count);
        Assert.Null(vm.Columns[^1].Status);
        Assert.Equal(3, vm.Columns[^1].Count);
    }

    [Fact]
    public void DraggingBetweenColumnsMovesOneDocument()
    {
        var (vm, _, _, walk, idle, _) = Open();
        var row = vm.Rows.First(r => r.Document?.Id == walk.Id);

        vm.MoveToStatus((row, AssetStatus.Review));

        Assert.Equal(AssetStatus.Review, walk.Status);
        Assert.Null(idle.Status);
        Assert.Contains("Review", vm.Status);
    }

    // ---- people (Q43's registry, Q45's boundary) --------------------------------------------

    [Fact]
    public void RenamingSomebodyFixesEveryRowAtOnce()
    {
        // The whole reason a registry won over a typed name.
        var (vm, _, _, _, _, _) = Open();
        vm.NewPersonName = "Ana";
        vm.AddPersonCommand.Execute(null);
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));
        vm.AssignSelection(vm.People[0]);

        vm.People[0].Name = "Ana Ruiz";
        vm.Rebuild();

        Assert.All(
            vm.Rows.Where(r => r.Document is not null),
            r => Assert.Equal("Ana Ruiz", r.AssigneeName));
    }

    [Fact]
    public void RemovingSomebodySaysWhatItCostsBeforeItHappens()
    {
        // Q35's pattern: the specific list, never a bare "are you sure".
        var (vm, _, _, _, _, _) = Open();
        vm.NewPersonName = "Ana";
        vm.AddPersonCommand.Execute(null);
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));
        vm.AssignSelection(vm.People[0]);

        var cost = vm.WhatRemovingCosts(vm.People[0].Person);
        output.WriteLine(cost);

        Assert.Contains("3 documents", cost);
        Assert.Contains("unassigned", cost);
    }

    [Fact]
    public void UnassigningIsInTheSameListAsAssigning()
    {
        // Taking somebody off nine rows must not be nine right-clicks, and a
        // ComboBox cannot offer a null item — hence the sentinel.
        var (vm, _, _, walk, _, _) = Open();
        vm.NewPersonName = "Ana";
        vm.AddPersonCommand.Execute(null);
        vm.SetSelection(vm.Rows.Where(r => r.Document is not null));
        vm.AssignSelection(vm.People[0]);
        Assert.NotNull(walk.AssigneeId);

        vm.AssignSelection(vm.AssignChoices[0]);   // "— nobody —"

        Assert.Null(walk.AssigneeId);
        Assert.Contains("unassigned", vm.Status);
    }

    [Fact]
    public void RemovingTheFilteredPersonClearsTheFilter()
    {
        // Otherwise the window shows an empty tree and nothing says why.
        var (vm, _, _, _, _, _) = Open();
        vm.NewPersonName = "Ana";
        vm.AddPersonCommand.Execute(null);
        var ana = vm.People[0];
        vm.AssigneeFilter = ana;

        vm.RemovePersonCommand.Execute(ana);

        Assert.Null(vm.AssigneeFilter);
        Assert.NotEmpty(vm.Rows);
    }

    // ---- the assets tab -----------------------------------------------------------------

    [Fact]
    public void TheAssetsTabShowsAllThreeLevelsAtOnce()
    {
        // The tab a context menu cannot be: a menu declares on one scope and
        // shows nothing about the others.
        var (vm, project, knight, walk, _, _) = Open();
        ResourceScopes.Declare(project.Manifest, null, PaletteScopes.Kind, "studio");
        ResourceScopes.Declare(project.Manifest, knight, PaletteScopes.Kind, "knight-red");
        ResourceScopes.DeclareOn(walk, PaletteScopes.Kind, "just-this-one");
        vm.Rebuild();

        var scopes = vm.Assets;

        Assert.Equal("Production", scopes[0].Name);
        Assert.Contains("studio", Palette(scopes[0]));
        Assert.Contains("knight-red", Palette(scopes.Single(s => s.Name == "Knight")));
        Assert.Contains("just-this-one", Palette(scopes.Single(s => s.Name == "walk")));

        static string Palette(AssetScope scope) =>
            scope.Cells.Single(c => c.Kind == PaletteScopes.Kind).Text;
    }

    [Fact]
    public void AScopeThatDeclaresNothingSaysSo()
    {
        var (vm, _, _, _, _, _) = Open();

        Assert.All(vm.Assets, s => Assert.True(s.DeclaresNothing));
    }

    // ---- the Assets tab writes (gap 1) ------------------------------------------------------

    [Fact]
    public void SharingSomethingWithAScopeDeclaresItThere()
    {
        var (vm, project, knight, _, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Studio warms" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "Knight");

        var offer = vm.OfferChoices.Single(o => o.Label.Contains("Studio warms"));
        vm.DeclareOnScope(offer);

        var declared = Assert.Single(knight.Resources!);
        Assert.Equal(PaletteScopes.Kind, declared.Kind);
        Assert.Equal(project.Palettes[0].Id, declared.Id);
    }

    [Fact]
    public void TheFirstDeclarationOfAKindSaysThatScopingIsNowOn()
    {
        // A picker that quietly shrinks is a bad way to learn that everything
        // stopped applying everywhere.
        var (vm, project, knight, _, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Studio warms" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "Knight");

        vm.DeclareOnScope(vm.OfferChoices.First());
        output.WriteLine(vm.Status);

        Assert.Contains("now scoped", vm.Status);
    }

    [Fact]
    public void ADocumentCanDeclareSomethingOfItsOwn()
    {
        // The narrowest tier, which had no target at all before this branch.
        var (vm, project, _, walk, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Just this one" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "walk");

        vm.DeclareOnScope(vm.OfferChoices.First());

        Assert.Single(walk.Resources!);
        Assert.Equal(project.Palettes[0].Id, ResourceScopes.Nearest(
            project.Manifest, walk, PaletteScopes.Kind)!.Id);
    }

    [Fact]
    public void TakingBackTheLastDeclarationSaysEverythingAppliesAgain()
    {
        // The other half of the sentence: `AnyDeclared` is the switch, so
        // undeclaring the last one is also a change of behaviour from one click.
        var (vm, project, _, _, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Studio warms" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "Knight");
        vm.DeclareOnScope(vm.OfferChoices.First());

        var declaration = vm.Assets.Single(s => s.Name == "Knight").All[0];
        vm.UndeclareOnScope(declaration);
        output.WriteLine(vm.Status);

        Assert.Contains("everything applies again", vm.Status);
        Assert.Empty(vm.Assets.Single(s => s.Name == "Knight").All);
    }

    [Fact]
    public void UndeclaringEmptiesBackToNothing()
    {
        // Absent unless used, in both directions.
        var (vm, project, knight, _, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Studio warms" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "Knight");
        vm.DeclareOnScope(vm.OfferChoices.First());

        vm.UndeclareOnScope(vm.Assets.Single(s => s.Name == "Knight").All[0]);

        Assert.Null(knight.Resources);
    }

    [Fact]
    public void SomethingAlreadySharedHereIsNotOfferedTwice()
    {
        var (vm, project, _, _, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Studio warms" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "Knight");
        var before = vm.OfferChoices.Count;

        vm.DeclareOnScope(vm.OfferChoices.First());

        Assert.Equal(before - 1, vm.OfferChoices.Count);
    }

    [Fact]
    public void AChipReadsAsANameRatherThanAnId()
    {
        var (vm, project, _, _, _, _) = Open();
        project.Palettes.Add(new Palette { Name = "Studio warms" });
        vm.Rebuild();
        vm.SelectedScope = vm.Assets.Single(s => s.Name == "Knight");
        vm.DeclareOnScope(vm.OfferChoices.First());

        Assert.Equal("Studio warms", vm.Assets.Single(s => s.Name == "Knight").All[0].Name);
    }

    // ---- the facet editor (gap 2, and Q39's cost) --------------------------------------------

    [Fact]
    public void TheFacetEditorAppearsForExactlyOneFolder()
    {
        // Notes, a pivot and a reading are per-folder values, and applying one
        // across nine folders would mean deciding what "the notes" of nine are.
        var (vm, _, knight, _, _, _) = Open();

        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));
        Assert.True(vm.IsEditingFolder);
        Assert.Same(knight, vm.EditingFolder);

        vm.SetSelection(vm.Rows);
        Assert.False(vm.IsEditingFolder);

        vm.SetSelection(vm.Rows.Where(r => r.Document is not null).Take(1));
        Assert.False(vm.IsEditingFolder);
    }

    [Fact]
    public void NotesAreWrittenThroughAndEmptyBackToNothing()
    {
        var (vm, _, knight, _, _, _) = Open();
        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));

        vm.FolderNotes = "Rain on the window.";
        Assert.Equal("Rain on the window.", knight.Notes);

        vm.FolderNotes = "   ";
        Assert.Null(knight.Notes);
    }

    [Fact]
    public void APivotIsGivenAndTakenAway()
    {
        var (vm, _, knight, _, _, _) = Open();
        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));

        vm.TogglePivotCommand.Execute(null);
        Assert.NotNull(knight.Pivot);
        Assert.Contains("registers its frames on it", vm.Status);

        vm.TogglePivotCommand.Execute(null);
        Assert.Null(knight.Pivot);
    }

    [Fact]
    public void ClearingAReadingNamesWhatItCostsFirst()
    {
        // Q35's condition, and with Q39 keeping the facets behind a click this
        // sentence is the only place an artist is told.
        var (vm, _, knight, _, _, _) = Open();
        knight.Taxonomy = new SubjectTaxonomy
        {
            Kind = "biped",
            Parts = [new SubjectPart { Name = "torso" }],
            Reviewed = true,
        };
        knight.Pivot = new Pivot();
        vm.Rebuild();
        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));

        output.WriteLine(vm.WhatClearingCosts);
        Assert.Contains("corrected by hand", vm.WhatClearingCosts);
        Assert.Contains("pivot", vm.WhatClearingCosts);

        vm.ClearReadingCommand.Execute(null);

        Assert.Null(knight.Taxonomy);
        // Only the reading. A pivot on a folder nothing has read is still a
        // pivot — the warning says what stops meaning anything, not what is
        // deleted.
        Assert.NotNull(knight.Pivot);
    }

    [Fact]
    public void TheReviewedFlagCanFinallyBeSet()
    {
        // It shipped in PR #48 with nothing that could set it, and the refusal
        // message said "clear it first" about a control that did not exist.
        var (vm, _, knight, _, _, _) = Open();
        knight.Taxonomy = new SubjectTaxonomy { Kind = "biped" };
        vm.Rebuild();
        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));

        vm.MarkReadingReviewedCommand.Execute(null);

        Assert.True(knight.Taxonomy!.Reviewed);
        Assert.Contains("A re-read will refuse", vm.Status);
    }

    [Fact]
    public void RemovingAVariantKeepsTheArtItReplaced()
    {
        // Removing a variant must not be the fastest way to delete the drawings
        // made for it.
        var (vm, project, knight, walk, _, _) = Open();
        var variant = ProjectIo.AddVariant(project, knight, "Winter");
        var over = ProjectIo.OverrideDocument(
            project, knight, variant, walk, DocumentFactory.CreateDoc(64, 64, 12));
        vm.Rebuild();
        vm.SetSelection(vm.Rows.Where(r => r.IsFolder));

        vm.RemoveVariantCommand.Execute(variant);

        Assert.Null(knight.Variants);
        Assert.Contains(over, project.Manifest.Documents);
        Assert.Contains("stay in the folder", vm.Status);
    }

    // ---- the Export tab (gap 4) -------------------------------------------------------------

    [Fact]
    public void TheExportTabShowsWhatWouldBeWritten()
    {
        // ExportPlan produced this already and only a confirmation dialog read
        // it — a plan visible for half a second is a plan nobody checks.
        var (vm, project, knight, _, _, _) = Open();
        ExportScopes.SetPreset(
            project.Manifest, knight,
            (project.Manifest.ExportPresets ??= [new ExportPreset
            {
                Name = "Sheet", Grouping = ExportGrouping.OneArtifact,
            }])[0].Id);
        vm.Rebuild();

        var sheet = Assert.Single(vm.ExportRows, r => r.Scope == "Knight");
        Assert.Equal("Sheet", sheet.Preset);
        Assert.Equal(2, sheet.Count);
        output.WriteLine(vm.ExportSummary);
        Assert.Contains("document", vm.ExportSummary);
    }

    [Fact]
    public void TheExportTabSaysWhatIsHeldBackByStatus()
    {
        // The number worth checking: it is how you learn most of a scope is
        // excluded before wondering why the sheet came out half empty.
        var (vm, project, knight, walk, _, _) = Open();
        var preset = new ExportPreset
        {
            Name = "Ready only",
            Grouping = ExportGrouping.OneArtifact,
            IncludeStatuses = [AssetStatus.Ready],
        };
        project.Manifest.ExportPresets = [preset];
        ExportScopes.SetPreset(project.Manifest, knight, preset.Id);
        walk.Status = AssetStatus.Ready;
        vm.Rebuild();

        var sheet = Assert.Single(vm.ExportRows, r => r.Scope == "Knight");
        Assert.True(sheet.HasExcluded);
        Assert.Equal(1, sheet.Count);
        Assert.Contains("held back by status", vm.ExportSummary);
    }

    // ---- the footer ------------------------------------------------------------------------

    [Fact]
    public void TheFooterCountsWhatIsTrue()
    {
        var (vm, _, _, walk, _, _) = Open();
        vm.SetSelection([vm.Rows.First(r => r.Document?.Id == walk.Id)]);
        vm.SetStatus(AssetStatus.Ready);

        output.WriteLine(vm.Summary);

        Assert.Contains("3 documents", vm.Summary);
        Assert.Contains("1 Ready", vm.Summary);
        Assert.Contains("2 with no status", vm.Summary);
        Assert.Contains("3 unassigned", vm.Summary);
        // Absent rather than zero, so the footer reads as facts.
        Assert.DoesNotContain("Draft", vm.Summary);
    }

    // ---- character sheets (Q25 re-answered) --------------------------------------------------

    [Fact]
    public void ASheetIsARowUnderItsFolder()
    {
        var (vm, project, knight, _, _, _) = Open();
        var sheet = ProjectSheets.Add(project, "Knight", knight);
        vm.Rebuild();

        var row = vm.Rows.Single(r => r.Sheet?.Id == sheet.Id);
        Assert.Equal(knight.Id, row.Folder!.Id);
        Assert.True(row.IsSheet);
        Assert.False(row.IsFolder);
        // Reference art carries no production facets.
        Assert.False(row.HasStatus);
        Assert.Empty(row.OwnTags);
    }

    /// <summary>The owner's sentence: "through the project manager we can re-assign".</summary>
    [Fact]
    public void FilingASelectedSheetSomewhereElseMovesItsVisibility()
    {
        var (vm, project, knight, _, _, _) = Open();
        var goblin = ProjectFolders.Add(project.Manifest, "Goblin");
        var sheet = ProjectSheets.Add(project, "Knight", knight);
        vm.Rebuild();

        vm.SetSelection(vm.Rows.Where(r => r.Sheet is not null));
        Assert.True(vm.HasSheetSelection);
        var target = vm.SheetHomeChoices.Single(c => c.Folder?.Id == goblin.Id);
        vm.FileSelectedSheets(target);

        output.WriteLine(vm.Status);
        Assert.Equal(goblin.Id, ProjectSheets.FindRef(project.Manifest, sheet.Id)!.FolderId);
        Assert.Equal(goblin.Id, vm.Rows.Single(r => r.Sheet is not null).Folder!.Id);
    }

    /// <summary>A bulk tag over a mixed selection leaves the sheet's folder alone.</summary>
    /// <remarks>
    /// The trap is the fallback: a sheet row carries its folder so the window can
    /// re-assign it, and the tag command's "not a document, so tag the folder"
    /// branch would have tagged that folder for a row that only *lives* there.
    /// </remarks>
    [Fact]
    public void TaggingASelectionWithASheetInItDoesNotTagTheSheetsFolder()
    {
        var (vm, project, knight, _, _, _) = Open();
        ProjectSheets.Add(project, "Knight", knight);
        vm.Rebuild();

        vm.SetSelection(vm.Rows.Where(r => r.Sheet is not null));
        vm.TagSelection("hero");

        Assert.Null(knight.Tags);
        Assert.Contains("Nothing changed", vm.Status);
    }

    // ---- the window itself, which is the half no view-model test reaches ---------------------

    /// <summary>
    /// B163: opening the project manager crashed, and every test above it passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split that makes this file cheap — drive the view model, never make a
    /// window — left the window with no test at all, so a bad static field
    /// initialiser (<c>DataFormat.CreateStringApplicationFormat("lightbox/status-card")</c>,
    /// whose identifier Avalonia rejects) shipped as a <c>TypeInitializationException</c>
    /// on the first click of Project.
    /// </para>
    /// <para>
    /// A type initialiser throws <em>once</em> and is then cached, so this has to
    /// be the construction that happens first in the process — which it is, being
    /// the only test here that touches the type.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheWindowOpens()
    {
        var project = ProjectIo.Create("Production", "/nowhere.lbproj");
        Add(project, "walk", null);

        var window = new ProjectWindow(project);

        Assert.NotNull(window.DataContext);
    }
}

/// <summary>
/// The same failure as B163, asked of every window rather than of one.
/// </summary>
/// <remarks>
/// A static field initialiser that throws is invisible until somebody opens that
/// window: nothing references the type at startup, the compiler is happy, and the
/// crash is a <c>TypeInitializationException</c> from a click. Running every
/// window's class constructor costs milliseconds and needs no UI thread, so the
/// next one is caught by a suite nobody had to remember to extend.
/// </remarks>
public sealed class WindowInitialiserTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryWindowsStaticInitialiserRuns()
    {
        var windows = typeof(ProjectWindow).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }
                        && typeof(Window).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        output.WriteLine($"{windows.Count} windows: {string.Join(", ", windows.Select(t => t.Name))}");
        Assert.NotEmpty(windows);

        var broken = new List<string>();
        foreach (var window in windows)
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(window.TypeHandle);
            }
            catch (Exception ex)
            {
                broken.Add($"{window.Name}: {(ex.InnerException ?? ex).Message}");
            }
        }

        Assert.Empty(broken);
    }
}
