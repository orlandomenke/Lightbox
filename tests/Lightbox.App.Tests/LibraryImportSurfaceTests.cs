using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The character library's way in (Q138 slice 2): one view model feeds the
/// picker and the window, importing lands in the open project and on disk,
/// the roots persist with the artist, and the edited-copy gate asks before
/// the one destructive act instead of after.
/// </summary>
public sealed class LibraryImportSurfaceTests(ITestOutputHelper output) : ProjectPanelFixture
{
    private readonly string _library = Path.Combine(
        Path.GetTempPath(), $"lightbox-libsurf-{Guid.NewGuid():N}");

    public new void Dispose()
    {
        if (Directory.Exists(_library)) Directory.Delete(_library, recursive: true);
        base.Dispose();
    }

    /// <summary>An asset library on disk offering one knight with one walk.</summary>
    private Project Shelf()
    {
        var source = ProjectIo.Create("Knights", Path.Combine(_library, "knights.lbproj"),
            ProjectType.AssetLibrary);
        var palette = new Palette { Name = "Knight", Swatches = [new Swatch { Color = "#8090a0" }] };
        source.Palettes.Add(palette);
        var knight = ProjectFolders.Add(source.Manifest, "Knight");
        ResourceScopes.Declare(source.Manifest, knight, PaletteScopes.Kind, palette.Id);
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            SwatchId = palette.Swatches[0].Id,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(80, 80, 1)],
            Brush = new BrushSettings { Size = 10, Opacity = 1 },
        });
        ProjectIo.AddDocument(source, "Walk", doc, knight);
        ProjectIo.Save(source);
        return source;
    }

    [AvaloniaFact]
    public async Task ImportLandsInTheOpenProjectTheDockerAndTheDisk()
    {
        Shelf();
        var vm = Open();
        vm.Characters.AddRoot(_library);

        var row = Assert.Single(vm.Characters.Entries);
        Assert.Equal("Knight", row.Name);
        var result = await vm.Characters.Import(row);
        Assert.NotNull(result);
        output.WriteLine(vm.AiStatus);
        Assert.Contains("Imported", vm.AiStatus);

        // The docker shows it without being reopened…
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Name == "Knight");
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Name == "Walk");

        // …and the import is already on disk: a cold reopen still has it,
        // which is slice 1's round-trip promise kept by the surface too.
        var reopened = Reopen();
        var folder = Assert.Single(reopened.ProjectDocker.Project!.WithReading.Concat(
            ProjectFolders.All(reopened.ProjectDocker.Project!.Manifest).Where(f => f.Name == "Knight")));
        Assert.Equal("Knight", folder.Name);
        Assert.NotNull(folder.Origin);
    }

    [AvaloniaFact]
    public async Task TheEditedCopyGateAsksFirstAndKeepingIsTheDefault()
    {
        Shelf();
        var vm = Open();
        vm.Characters.AddRoot(_library);
        var row = Assert.Single(vm.Characters.Entries);
        await vm.Characters.Import(row);

        // The artist edits the imported walk.
        var project = vm.ProjectDocker.Project!;
        var knight = ProjectFolders.All(project.Manifest).Single(f => f.Name == "Knight");
        var copy = ProjectFolders.DocumentsIn(project.Manifest, knight).Single();
        ProjectIo.LoadDocument(project, copy)!.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#ff0000",
            Points = [new StrokePoint(5, 5, 1), new StrokePoint(6, 6, 1)],
            Brush = new BrushSettings { Size = 2, Opacity = 1 },
        });

        // Re-import with the gate answering "keep": the edit survives, and the
        // gate saw the edited copy's name — that is what makes it a decision.
        IReadOnlyList<string>? asked = null;
        vm.Characters.ConfirmReplaceEdited = names => { asked = names; return Task.FromResult(false); };
        var kept = await vm.Characters.Import(row);
        Assert.Equal(["Walk"], asked);
        Assert.Equal(["Walk"], kept!.KeptEdited);
        Assert.Equal(2, ProjectIo.LoadDocument(project, copy)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes.Count);

        // And answering "replace" takes the library's version.
        vm.Characters.ConfirmReplaceEdited = _ => Task.FromResult(true);
        var replaced = await vm.Characters.Import(row);
        Assert.Equal(["Walk"], replaced!.Replaced);
        Assert.Single(ProjectIo.LoadDocument(project, copy)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes);
    }

    [AvaloniaFact]
    public void RootsPersistWithTheArtistAndScanOnlyWhenAsked()
    {
        Shelf();
        var vm = Open();
        Assert.False(vm.Characters.HasRoots);
        Assert.Empty(vm.Characters.Entries);

        vm.Characters.AddRoot(_library);
        Assert.True(vm.Characters.HasRoots);
        Assert.Single(vm.Characters.Entries);
        // Persisted app-side, like onion depths: which disks hold libraries is
        // a property of the machine, not of any artwork.
        Assert.Contains(_library, File.ReadAllText(AppSettings.Path));

        vm.Characters.RemoveRoot(_library);
        Assert.False(vm.Characters.HasRoots);
        Assert.Empty(vm.Characters.Entries);
        Assert.DoesNotContain(_library, File.ReadAllText(AppSettings.Path));
    }

    [AvaloniaFact]
    public void TheWindowIsAViewOverTheSameViewModel()
    {
        Shelf();
        var vm = Open();
        vm.Characters.AddRoot(_library);

        var window = new LibraryWindow(vm.Characters);
        window.Show();
        try
        {
            Assert.Same(vm.Characters, window.DataContext);
            // Opening scanned: the shelf is filled from the same entries the
            // picker reads, so the two surfaces cannot disagree.
            Assert.Single(vm.Characters.Entries);
            // And the window wired the Q35 gate, so an import through either
            // surface can ask before replacing edited work.
            Assert.NotNull(vm.Characters.ConfirmReplaceEdited);
        }
        finally
        {
            window.Close();
        }
    }
}
