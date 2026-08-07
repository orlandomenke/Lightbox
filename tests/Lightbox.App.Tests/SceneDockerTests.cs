using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The running order in the project panel, and changing what a project is for.
/// </summary>
/// <remarks>
/// The rules of the model live in <c>Lightbox.Core.Tests.SceneAndConversionTests</c>.
/// This is about the panel: which rows appear, what they say, and that neither
/// feature costs a project that does not use it anything.
/// <para>
/// <b>B114.</b> A scene is a folder with an authored order, so these are folder
/// tests now. What they guard is unchanged — a project that never ordered
/// anything shows no running time and no scene glyph, and the one that did
/// reorders and keeps its selection.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SceneDockerTests : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-scenedock-{Guid.NewGuid():N}.lbproj");

    public new void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private MainViewModel WithProject(ProjectType? type = ProjectType.Animation)
    {
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Film", _root);
        project.Manifest.Type = type;
        vm.ProjectDocker.Project = project;
        return vm;
    }

    private static ProjectRow Row(MainViewModel vm, string name) =>
        vm.ProjectDocker.Rows.First(r => r.Name == name);

    /// <summary>A folder with an order on it — what reads as a scene.</summary>
    private static ProjectFolder Scene(MainViewModel vm, string name)
    {
        var folder = ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, name);
        folder.Order = [];
        vm.ProjectDocker.Refresh();
        return folder;
    }

    // ---- absence -----------------------------------------------------------------

    [AvaloniaFact]
    public void AProjectThatOrderedNothingShowsNoRunningOrder()
    {
        // Optional means absent. A project making sprite sheets has no running
        // order, so no row reads as a scene and there is no total to show.
        var vm = WithProject(ProjectType.GameArt);
        ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Knight");
        vm.ProjectDocker.Refresh();

        Assert.DoesNotContain(vm.ProjectDocker.Rows, r => r.HasOrder);
        Assert.Null(vm.ProjectDocker.TotalRunningTime);
    }

    [AvaloniaFact]
    public void TheFirstFolderBringsTheReorderButtonsWithIt()
    {
        // B114. They appear as soon as there is a tree to arrange, rather than
        // once something is already arranged — hiding them until then would hide
        // the only way to start.
        var vm = WithProject();
        Assert.False(vm.ProjectDocker.CanReorder);

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewFolderItem);

        Assert.True(vm.ProjectDocker.CanReorder);
    }

    [AvaloniaFact]
    public void AFolderCanCarryAReadingAndAnOrderAtOnce()
    {
        // Q40. The old model had to break the tie — a folder with both was "a
        // character, not a scene" — and inventing that rule is the rigidity a
        // designation forces. There is no tie: it carries both, and what it *is*
        // is whatever its glyph says.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        var knight = ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Knight");
        knight.Taxonomy = new SubjectTaxonomy { Kind = "biped" };
        knight.Order = [];
        vm.ProjectDocker.Refresh();

        Assert.True(Row(vm, scene.Name).HasOrder);
        Assert.False(Row(vm, scene.Name).HasReading);
        Assert.True(Row(vm, "Knight").HasOrder);
        Assert.True(Row(vm, "Knight").HasReading);
        Assert.All(
            vm.ProjectDocker.Rows.Where(r => r.IsFolder),
            r => Assert.True(r.IsHeading));
    }

    // ---- the glyph is the artist's (Q38) --------------------------------------

    [AvaloniaFact]
    public void AFolderNobodyLabelledUsesThePlainGlyphAndWritesNoKey()
    {
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.SaveProject(everything: true);

        Assert.Equal("🗀", Row(vm, scene.Name).Glyph);
        Assert.DoesNotContain("\"icon\"", File.ReadAllText(Path.Combine(_root, "project.json")));
    }

    [AvaloniaFact]
    public void TheGlyphIsWhateverTheArtistPicked()
    {
        // Whatever it is. Nothing reads it, so a folder with a reading can wear
        // a clapperboard and a folder with an order can wear a tree.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);

        vm.ProjectDocker.SetIconCommand.Execute("🎬");

        Assert.Equal("🎬", Row(vm, scene.Name).Glyph);
        Assert.Equal("🎬", scene.Icon);
    }

    [AvaloniaFact]
    public void AGlyphOutsideTheGridIsAccepted()
    {
        // The grid is a starting point, not a vocabulary — a production has
        // designations nobody wrote down.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);

        vm.ProjectDocker.SetIconCommand.Execute("🦑");

        Assert.DoesNotContain("🦑", ProjectViewModel.GlyphChoices);
        Assert.Equal("🦑", Row(vm, scene.Name).Glyph);
    }

    [AvaloniaFact]
    public void ClearingTheGlyphTakesTheKeyBackOutOfTheFile()
    {
        // Absent unless used, in both directions: a folder labelled and then
        // unlabelled is byte-identical to one nobody touched.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.SetIconCommand.Execute("🎬");

        vm.ProjectDocker.SetIconCommand.Execute("🗀");

        Assert.Null(scene.Icon);
        vm.SaveProject(everything: true);
        Assert.DoesNotContain("\"icon\"", File.ReadAllText(Path.Combine(_root, "project.json")));
    }

    [AvaloniaFact]
    public void ADocumentRowNeverWearsItsFoldersGlyph()
    {
        // The glyph says what the folder is. A drawing inside it is a drawing.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.SetIconCommand.Execute("🎬");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);

        var document = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        Assert.Equal("▣", document.Glyph);
    }

    // ---- what a folder carries, in the details panel (Q39) ---------------------

    [AvaloniaFact]
    public void AnOrdinaryFolderSaysNothingAboutWhatItCarries()
    {
        var vm = WithProject();
        var folder = ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Scratch");
        vm.ProjectDocker.Refresh();
        vm.ProjectDocker.Selected = Row(vm, folder.Name);

        Assert.False(vm.ProjectDocker.HasFacets);
        Assert.Empty(vm.ProjectDocker.SelectedFacets);
    }

    [AvaloniaFact]
    public void TheDetailsPanelListsFacetsAndNamesNoKind()
    {
        var vm = WithProject();
        var knight = ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Knight");
        knight.Taxonomy = new SubjectTaxonomy
        {
            Kind = "biped",
            Parts = [new SubjectPart { Name = "torso" }, new SubjectPart { Name = "near-arm" }],
        };
        knight.Pivot = new Pivot();
        vm.ProjectDocker.Refresh();
        vm.ProjectDocker.Selected = Row(vm, "Knight");

        var facets = vm.ProjectDocker.SelectedFacets;
        Assert.True(vm.ProjectDocker.HasFacets);
        Assert.Contains(facets, f => f.Contains("biped") && f.Contains("2 parts"));
        Assert.Contains("pivot", facets);
        // It lists what is there and draws no conclusion from the combination.
        Assert.DoesNotContain(facets, f => f.Contains("character", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(facets, f => f.Contains("scene", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void AHandCorrectedReadingSaysSoInTheDetails()
    {
        // Q39 put the facets behind a click, which means this line and Q35's
        // warning are the only two places an artist is told a reading is theirs.
        var vm = WithProject();
        var knight = ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Knight");
        knight.Taxonomy = new SubjectTaxonomy { Kind = "biped", Reviewed = true };
        vm.ProjectDocker.Refresh();
        vm.ProjectDocker.Selected = Row(vm, "Knight");

        Assert.Contains(vm.ProjectDocker.SelectedFacets, f => f.EndsWith("yours"));
    }

    // ---- the tree ------------------------------------------------------------------

    [AvaloniaFact]
    public void DocumentsAreIndentedUnderTheirFolder()
    {
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);

        var shotRow = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        Assert.Same(scene, shotRow.Folder);
        Assert.Equal(14, shotRow.Indent);
        Assert.Equal(0, Row(vm, scene.Name).Indent);
    }

    [AvaloniaFact]
    public void ADocumentWithNoFolderSelectedBelongsToTheProject()
    {
        // No container is invented for it — B83/B84 is the same rule at the
        // other end, and B114 removed the last place one was.
        var vm = WithProject();

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);

        var document = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);
        Assert.Null(document.FolderId);
        Assert.Empty(ProjectFolders.All(vm.ProjectDocker.Project!.Manifest));
    }

    [AvaloniaFact]
    public void ADocumentOpensAsATab()
    {
        var vm = WithProject();
        var tabs = vm.Tabs.Count;

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);

        Assert.Equal(tabs + 1, vm.Tabs.Count);
    }

    // ---- running time -----------------------------------------------------------------

    [AvaloniaFact]
    public void AFolderRowShowsHowLongItRuns()
    {
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);

        var row = Row(vm, scene.Name);

        Assert.True(row.HasDuration);
        Assert.Contains("f", row.Duration);
        Assert.NotNull(vm.ProjectDocker.TotalRunningTime);
    }

    [AvaloniaFact]
    public void AnEmptyFolderSaysNothingRatherThanZero()
    {
        // A running time of "0:00.0" on a scene nobody has drawn yet is a
        // number that reads as measured.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");

        Assert.False(Row(vm, scene.Name).HasDuration);
    }

    // ---- the running order ---------------------------------------------------------

    [AvaloniaFact]
    public void FoldersMoveUpAndDownAndTheSelectionFollows()
    {
        var vm = WithProject();
        var manifest = vm.ProjectDocker.Project!.Manifest;
        var film = ProjectFolders.Add(manifest, "Film");
        var first = ProjectFolders.Add(manifest, "Opening", film);
        var second = ProjectFolders.Add(manifest, "The chase", film);
        vm.ProjectDocker.Refresh();
        Assert.Equal(
            [first.Id, second.Id],
            ProjectFolders.ChildrenInOrder(manifest, film).Select(f => f.Id));

        vm.ProjectDocker.Selected = Row(vm, second.Name);
        vm.ProjectDocker.MoveSelectedUpCommand.Execute(null);

        Assert.Equal(
            [second.Id, first.Id],
            ProjectFolders.ChildrenInOrder(manifest, film).Select(f => f.Id));
        // The row you moved is still the row you have.
        Assert.Equal(second.Id, vm.ProjectDocker.Selected!.Folder!.Id);
    }

    [AvaloniaFact]
    public void DocumentsMoveWithinTheirFolder()
    {
        var vm = WithProject();
        var manifest = vm.ProjectDocker.Project!.Manifest;
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);

        var second = ProjectFolders.InOrder(manifest, scene)[1];
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation?.Id == second.Id);

        vm.ProjectDocker.MoveSelectedUpCommand.Execute(null);

        Assert.Same(second, ProjectFolders.InOrder(manifest, scene)[0]);
    }

    [AvaloniaFact]
    public void ReorderingALooseDocumentDoesNothing()
    {
        // Nothing contains it, so there is no order it is in — said by doing
        // nothing rather than by arranging something else.
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);
        var document = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);

        vm.ProjectDocker.MoveSelectedDownCommand.Execute(null);

        Assert.Same(document, Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents));
    }

    // ---- deleting a folder ------------------------------------------------------------

    [AvaloniaFact]
    public void DeletingAFolderKeepsItsDocumentsAsLooseOnes()
    {
        // Reorganising a film must not be the fastest way to delete it.
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);
        var shot = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);

        vm.ProjectDocker.Selected = Row(vm, scene.Name);
        vm.ProjectDocker.RemoveSelectedCommand.Execute(null);

        Assert.Empty(ProjectFolders.All(vm.ProjectDocker.Project!.Manifest));
        Assert.Contains(shot, vm.ProjectDocker.Project!.Manifest.Documents);
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation?.Id == shot.Id);
    }

    // ---- conversion ---------------------------------------------------------------------

    [AvaloniaFact]
    public void ConvertingChangesTheTypeAndRecreatesNoArtwork()
    {
        var vm = WithProject(ProjectType.Illustration);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewDocumentItem);
        vm.SaveProject(everything: true);
        var shot = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);
        var path = vm.ProjectDocker.Project!.PathOf(shot);
        var before = File.ReadAllBytes(path);

        var report = vm.ConvertProject(ProjectType.GameArt);

        Assert.Equal(ProjectType.GameArt, vm.ProjectDocker.Project!.Manifest.Type);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.NotNull(report);
        Assert.Contains("Project type — GameArt", vm.ProjectTypeLabel);
    }

    [AvaloniaFact]
    public void ConvertingDoesNotRearrangeTheScreenByItself()
    {
        // Which panels somebody wants is a preference; converting is a decision
        // about the project. Rearranging the screen as a side effect of a menu
        // item is how a tool loses trust.
        var vm = WithProject(ProjectType.Illustration);
        vm.Workspace.TimelineVisible = false;

        vm.ConvertProject(ProjectType.Animation);

        Assert.False(vm.Workspace.TimelineVisible);

        // And the separate, asked-for move does change it.
        vm.TakeProjectTypeWorkspace();
        Assert.True(vm.Workspace.TimelineVisible);
    }

    [AvaloniaFact]
    public void ConvertingTellsTheArtistWhatChanged()
    {
        var vm = WithProject(ProjectType.Illustration);
        ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Knight");

        var report = vm.ConvertProject(ProjectType.GameArt)!;

        Assert.Equal(ProjectType.Illustration, report.From);
        Assert.NotEmpty(report.Notes);
        Assert.Contains("sprite sheets", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ConvertingWithNoProjectOpenDoesNothing()
    {
        var vm = new MainViewModel(null);

        Assert.Null(vm.ConvertProject(ProjectType.Animation));
        Assert.Equal("Project type — unset", vm.ProjectTypeLabel);
    }
}
