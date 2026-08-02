using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The project container as the app sees it: absent until asked for, a tree
/// when present, and one palette shared by everything under a character.
/// </summary>
[Collection("BrushState")]
public sealed class ProjectDockerTests : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-app-proj-{Guid.NewGuid():N}.lbproj");

    public new void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    // ---- absence ------------------------------------------------------------

    [AvaloniaFact]
    public void TheAppOpensWithNoProject()
    {
        // Document-first. Optional means absent, not disabled — the same rule
        // the camera follows. Someone who opened the app to draw one picture
        // must never be shown a character tree.
        var vm = Vm();
        Assert.False(vm.HasProject);
        Assert.Null(vm.ProjectDocker.Project);
        Assert.Empty(vm.ProjectDocker.Rows);
        Assert.Null(vm.ActiveTab!.Source);
    }

    [AvaloniaFact]
    public void WithNoProjectADocumentSavesAndLoadsExactlyAsBefore()
    {
        var vm = Vm();
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();

        var json = vm.SerializeDocument();
        // No project keys leak into a document that never joined one.
        Assert.DoesNotContain("\"characters\"", json);
        Assert.Equal(json, vm.ExportStandaloneDocument());
    }

    // ---- creation -----------------------------------------------------------

    [AvaloniaFact]
    public void NewProjectAdoptsTheDocumentAlreadyOpen()
    {
        // Adopting rather than starting empty: the artist has been drawing, and
        // the container should form around that work.
        var vm = Vm();
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();

        vm.NewProject(_root, "Knight");

        Assert.True(vm.HasProject);
        var character = Assert.Single(vm.ProjectDocker.Project!.Characters);
        var animation = Assert.Single(character.Animations);
        Assert.Equal(animation.Id, vm.ActiveTab!.Source?.Id);

        // And it landed on disk with the work in it.
        var saved = Lightbox.Core.Serialization.DocJson.Load(vm.ProjectDocker.Project!.PathOf(animation));
        Assert.Single(((PaintedFrame)saved.Scene.Layers[^1].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void TheDockerListsCharactersWithTheirAnimationsUnderThem()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);

        var rows = vm.ProjectDocker.Rows;
        Assert.Equal(3, rows.Count); // character, adopted animation, new one
        Assert.True(rows[0].IsCharacter);
        Assert.False(rows[1].IsCharacter);
        Assert.False(rows[2].IsCharacter);
    }

    [AvaloniaFact]
    public void AddingAnAnimationOpensItAsATabBoundToItsSlot()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var before = vm.Tabs.Count;

        vm.ProjectDocker.AddAnimationCommand.Execute(null);

        Assert.Equal(before + 1, vm.Tabs.Count);
        var reference = vm.ProjectDocker.Rows[^1].Animation!;
        Assert.Equal(reference.Id, vm.ActiveTab!.Source?.Id);
    }

    [AvaloniaFact]
    public void OpeningAnAnimationTwiceFocusesTheTabRatherThanDuplicatingIt()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        var count = vm.Tabs.Count;

        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows[1]; // the adopted one
        vm.ProjectDocker.OpenSelected();
        Assert.Equal(count, vm.Tabs.Count);

        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows[2];
        vm.ProjectDocker.OpenSelected();
        Assert.Equal(count, vm.Tabs.Count);
    }

    [AvaloniaFact]
    public void FileNewStillMakesAStandaloneDocumentWithAProjectOpen()
    {
        // The most common action in the app must not change meaning based on
        // which row happens to be selected.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var animations = vm.ProjectDocker.Project!.AllDocuments.Count();

        vm.NewDocument(new NewDocumentSettings("Loose", 128, 128, 12, 72, "#ffffff", false));

        Assert.Null(vm.ActiveTab!.Source);
        Assert.Equal(animations, vm.ProjectDocker.Project!.AllDocuments.Count());
    }

    // ---- sharing ------------------------------------------------------------

    [AvaloniaFact]
    public void TwoAnimationsUnderOneCharacterPaintFromOnePalette()
    {
        // The promise Pillar 1 is named for.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var swatch = new Swatch { Color = "#20c040" };
        vm.ProjectDocker.Project!.Palettes.Add(new Palette { Name = "Knight", Swatches = [swatch] });
        vm.RefreshProjectResources();

        var first = StrokeUsing(vm, swatch.Id);
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        var second = StrokeUsing(vm, swatch.Id);

        Assert.Equal(new SKColor(0x20, 0xc0, 0x40), BrushEngine.StrokeColor(first));
        Assert.Equal(new SKColor(0x20, 0xc0, 0x40), BrushEngine.StrokeColor(second));

        swatch.Color = "#c02040";
        Assert.Equal(new SKColor(0xc0, 0x20, 0x40), BrushEngine.StrokeColor(first));
        Assert.Equal(new SKColor(0xc0, 0x20, 0x40), BrushEngine.StrokeColor(second));
    }

    private static Stroke StrokeUsing(MainViewModel vm, string swatchId)
    {
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            SwatchId = swatchId,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(60, 60, 1)],
            Brush = new BrushSettings { Size = 12, Opacity = 1 },
        };
        ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Add(stroke);
        return stroke;
    }

    // ---- saving -------------------------------------------------------------

    [AvaloniaFact]
    public void SaveWritesTheProjectWithoutAPicker()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        Assert.True(vm.CanSaveInPlace);

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
        vm.Save();

        var animation = vm.ProjectDocker.Project!.AllDocuments.First();
        var saved = Lightbox.Core.Serialization.DocJson.Load(vm.ProjectDocker.Project!.PathOf(animation));
        Assert.Single(((PaintedFrame)saved.Scene.Layers[^1].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void WithoutAProjectOrAPathThereIsNothingToSaveInPlace()
    {
        // Which is what makes Ctrl+S fall through to Save as… instead of
        // appearing to work and writing nothing.
        Assert.False(Vm().CanSaveInPlace);
    }

    [AvaloniaFact]
    public void AProjectReopensWithItsCharactersAndAnimations()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddAnimationCommand.Execute(null);
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
        vm.Save();

        var reopened = Vm();
        reopened.OpenProject(_root);

        Assert.True(reopened.HasProject);
        var character = Assert.Single(reopened.ProjectDocker.Project!.Characters);
        Assert.Equal(2, character.Animations.Count);
        // And it opened one, so the project is not an empty shell.
        Assert.Contains(reopened.Tabs, t => t.Source is not null);
    }

    [AvaloniaFact]
    public void RemovingAnAnimationLeavesItsFileOnDisk()
    {
        // Removing a row from an index is cheap to undo by hand; deleting an
        // artist's drawing because they clicked the wrong row is not.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var animation = vm.ProjectDocker.Project!.AllDocuments.First();
        var path = vm.ProjectDocker.Project!.PathOf(animation);
        Assert.True(File.Exists(path));

        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => !r.IsCharacter);
        vm.ProjectDocker.RemoveSelectedCommand.Execute(null);

        Assert.Empty(vm.ProjectDocker.Project!.AllDocuments);
        Assert.True(File.Exists(path));
    }

    // ---- making things inside the project -----------------------------------

    [AvaloniaFact]
    public void TheNewMenuOffersAnimationCharacterAndALooseDocument()
    {
        // Each lands somewhere specific. Creating work inside a project should
        // not be "make it, then file it".
        var vm = Vm();
        vm.NewProject(_root, "Knight");

        Assert.Equal(
            ["Animation", "Character", "Document"],
            vm.ProjectDocker.NewItemKinds.Select(k => k.Label));
    }

    [AvaloniaFact]
    public void ADocumentCreatedFromTheDockerBelongsToTheProjectNotACharacter()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");

        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewLooseDocument);

        var loose = Assert.Single(vm.ProjectDocker.Project!.Manifest.Documents);
        Assert.DoesNotContain(
            vm.ProjectDocker.Project!.Characters.SelectMany(c => c.Animations),
            a => a.Id == loose.Id);
        Assert.StartsWith("documents/", loose.Path);
        // And it opened, bound to its slot — the same as adding an animation.
        Assert.Equal(loose.Id, vm.ActiveTab!.Source?.Id);
    }

    [AvaloniaFact]
    public void ALooseDocumentGetsItsOwnRowWithNoCharacterAboveIt()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewLooseDocument);

        var row = vm.ProjectDocker.Rows[^1];
        Assert.True(row.IsLoose);
        Assert.Null(row.Character);
        Assert.Equal(0, row.Indent);
    }

    // ---- re-filing ------------------------------------------------------------

    [AvaloniaFact]
    public void MovingADocumentToAnotherCharacterRepathsItAndKeepsItsId()
    {
        // The id has to survive: a tab already showing the document stays
        // bound to it, so rearranging the tree does not orphan the window you
        // are drawing in.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddCharacterCommand.Execute(null);
        var project = vm.ProjectDocker.Project!;
        var from = project.Characters.First();
        var to = project.Characters.Last();
        Assert.NotSame(from, to);

        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null && r.Character == from);
        var id = row.Animation!.Id;

        Assert.True(vm.ProjectDocker.Move(row, to));

        Assert.Empty(from.Animations);
        var moved = Assert.Single(to.Animations);
        Assert.Equal(id, moved.Id);
        Assert.Contains(to.Slug, moved.Path);
    }

    [AvaloniaFact]
    public void MovingADocumentToTheProjectTakesItOutOfEveryCharacter()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var project = vm.ProjectDocker.Project!;
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);

        Assert.True(vm.ProjectDocker.Move(row, null));

        Assert.Empty(project.Characters.SelectMany(c => c.Animations));
        Assert.Single(project.Manifest.Documents);
    }

    [AvaloniaFact]
    public void MovingADocumentWhereItAlreadyIsDoesNothing()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);

        Assert.False(vm.ProjectDocker.Move(row, row.Character));
    }

    [AvaloniaFact]
    public void AMovedDocumentSurvivesASaveAndReopen()
    {
        // The end-to-end claim: the manifest, the file and the reload agree.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.ProjectDocker.AddCharacterCommand.Execute(null);
        var to = vm.ProjectDocker.Project!.Characters.Last();
        var row = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
        vm.ProjectDocker.Move(row, to);
        vm.Save();

        var reopened = Vm();
        reopened.OpenProject(_root);

        var characters = reopened.ProjectDocker.Project!.Characters.ToList();
        Assert.Empty(characters[0].Animations);
        Assert.Single(characters[1].Animations);
    }

    [AvaloniaFact]
    public void RenamingARowWritesThrough()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        var row = vm.ProjectDocker.Rows[0];

        vm.ProjectDocker.Rename(row, "Sir Reginald");

        Assert.Equal("Sir Reginald", vm.ProjectDocker.Project!.Characters.First().Name);
        Assert.Equal("Sir Reginald", row.Name);
    }
}
