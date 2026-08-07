using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
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

        Assert.DoesNotContain(vm.ProjectDocker.Rows, r => r.IsScene);
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
    public void AnOrderedFolderReadsAsASceneAndACharacterDoesNot()
    {
        var vm = WithProject();
        var scene = Scene(vm, "Opening");
        var knight = ProjectFolders.Add(vm.ProjectDocker.Project!.Manifest, "Knight");
        knight.Taxonomy = new SubjectTaxonomy { Kind = "biped" };
        knight.Order = [];        // a character may be arranged and stays a character
        vm.ProjectDocker.Refresh();

        Assert.True(Row(vm, scene.Name).IsScene);
        Assert.False(Row(vm, "Knight").IsScene);
        Assert.True(Row(vm, "Knight").IsSubject);
        Assert.All(
            vm.ProjectDocker.Rows.Where(r => r.IsFolder),
            r => Assert.True(r.IsHeading));
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
