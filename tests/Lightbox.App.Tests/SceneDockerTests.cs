using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Scenes in the project panel, and changing what a project is for.
/// </summary>
/// <remarks>
/// The rules of the model live in <c>Lightbox.Core.Tests.SceneAndConversionTests</c>.
/// This is about the panel: which rows appear, what they say, and that neither
/// feature costs a project that does not use it anything.
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

    // ---- absence -----------------------------------------------------------------

    [AvaloniaFact]
    public void AProjectWithNoScenesShowsNoSceneMachinery()
    {
        // Optional means absent. A project making sprite sheets has no running
        // order, so it gets no reorder buttons and no scene rows.
        var vm = WithProject(ProjectType.GameArt);
        ProjectIo.AddCharacter(vm.ProjectDocker.Project!, "Knight");
        vm.ProjectDocker.Project = vm.ProjectDocker.Project;   // rebuild

        Assert.False(vm.ProjectDocker.HasScenes);
        Assert.DoesNotContain(vm.ProjectDocker.Rows, r => r.IsScene);
        Assert.Null(vm.ProjectDocker.TotalRunningTime);
    }

    [AvaloniaFact]
    public void TheFirstSceneBringsTheMachineryWithIt()
    {
        var vm = WithProject();

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);

        Assert.True(vm.ProjectDocker.HasScenes);
        var row = Assert.Single(vm.ProjectDocker.Rows, r => r.IsScene);
        Assert.True(row.IsHeading);
        Assert.False(row.IsCharacter);
    }

    // ---- the tree ------------------------------------------------------------------

    [AvaloniaFact]
    public void ShotsAreIndentedUnderTheirScene()
    {
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);
        var scene = vm.ProjectDocker.SelectedScene!;
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);

        var shotRow = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        Assert.Same(scene, shotRow.Scene);
        Assert.Equal(14, shotRow.Indent);
        Assert.Equal(0, Row(vm, scene.Name).Indent);
    }

    [AvaloniaFact]
    public void CharactersAndScenesAreBothHeadingsAndBothAppear()
    {
        // They cross: one scene holds several characters, one character appears
        // in several scenes. Neither can be a folder inside the other.
        var vm = WithProject();
        ProjectIo.AddCharacter(vm.ProjectDocker.Project!, "Knight");
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);

        Assert.Contains(vm.ProjectDocker.Rows, r => r.IsCharacter && r.Name == "Knight");
        Assert.Contains(vm.ProjectDocker.Rows, r => r.IsScene);
        Assert.All(
            vm.ProjectDocker.Rows.Where(r => r.IsScene || r.IsCharacter),
            r => Assert.True(r.IsHeading));
    }

    [AvaloniaFact]
    public void AddingAShotWithNoSceneMakesTheFirstOne()
    {
        // The same bargain adding an animation with no character makes.
        var vm = WithProject();

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);

        Assert.Single(vm.ProjectDocker.Project!.Scenes);
        Assert.Single(vm.ProjectDocker.Project!.Scenes[0].Shots);
    }

    [AvaloniaFact]
    public void AShotOpensAsATab()
    {
        var vm = WithProject();
        var tabs = vm.Tabs.Count;

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);

        Assert.Equal(tabs + 1, vm.Tabs.Count);
    }

    // ---- running time -----------------------------------------------------------------

    [AvaloniaFact]
    public void ASceneRowShowsHowLongItRuns()
    {
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);
        var scene = vm.ProjectDocker.SelectedScene!;
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);

        var row = Row(vm, scene.Name);

        Assert.True(row.HasDuration);
        Assert.Contains("f", row.Duration);
        Assert.NotNull(vm.ProjectDocker.TotalRunningTime);
    }

    [AvaloniaFact]
    public void AnEmptySceneSaysNothingRatherThanZero()
    {
        // A running time of "0:00.0" on a scene nobody has drawn yet is a
        // number that reads as measured.
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);

        Assert.False(Row(vm, vm.ProjectDocker.SelectedScene!.Name).HasDuration);
    }

    // ---- the running order ---------------------------------------------------------

    [AvaloniaFact]
    public void ScenesMoveUpAndDownAndTheSelectionFollows()
    {
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);
        var first = vm.ProjectDocker.SelectedScene!;
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);
        var second = vm.ProjectDocker.SelectedScene!;
        Assert.Equal([first.Id, second.Id], vm.ProjectDocker.Project!.Scenes.Select(s => s.Id));

        vm.ProjectDocker.MoveSelectedUpCommand.Execute(null);

        Assert.Equal([second.Id, first.Id], vm.ProjectDocker.Project!.Scenes.Select(s => s.Id));
        // The row you moved is still the row you have.
        Assert.Equal(second.Id, vm.ProjectDocker.Selected!.Scene!.Id);
    }

    [AvaloniaFact]
    public void ShotsMoveWithinTheirScene()
    {
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);
        var scene = vm.ProjectDocker.Project!.Scenes[0];
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.IsScene);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);
        var second = scene.Shots[1];

        vm.ProjectDocker.MoveSelectedUpCommand.Execute(null);

        Assert.Same(second, scene.Shots[0]);
    }

    [AvaloniaFact]
    public void ReorderingACharacterRowDoesNothing()
    {
        // The buttons only mean something in a running order, and a character
        // is not one.
        var vm = WithProject();
        ProjectIo.AddCharacter(vm.ProjectDocker.Project!, "Knight");
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);
        vm.ProjectDocker.Selected = Row(vm, "Knight");

        vm.ProjectDocker.MoveSelectedDownCommand.Execute(null);

        // The first row that is not the project itself — B62 put that above
        // everything, so an index into Rows no longer means what it did.
        Assert.Equal("Knight", vm.ProjectDocker.Rows.First(r => !r.IsRoot).Name);
    }

    // ---- deleting a scene -------------------------------------------------------------

    [AvaloniaFact]
    public void DeletingASceneKeepsItsShotsAsLooseDocuments()
    {
        // Reorganising a film must not be the fastest way to delete it.
        var vm = WithProject();
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);
        var shot = vm.ProjectDocker.Project!.Scenes[0].Shots[0];
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.IsScene);

        vm.ProjectDocker.RemoveSceneCommand.Execute(null);

        Assert.False(vm.ProjectDocker.HasScenes);
        Assert.Contains(shot, vm.ProjectDocker.Project!.Manifest.Documents);
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation?.Id == shot.Id);
    }

    // ---- conversion ---------------------------------------------------------------------

    [AvaloniaFact]
    public void ConvertingChangesTheTypeAndRecreatesNoArtwork()
    {
        var vm = WithProject(ProjectType.Illustration);
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);
        vm.SaveProject(everything: true);
        var shot = vm.ProjectDocker.Project!.Scenes[0].Shots[0];
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
        ProjectIo.AddCharacter(vm.ProjectDocker.Project!, "Knight");

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
