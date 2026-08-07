using Lightbox.App.ViewModels;
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
}
