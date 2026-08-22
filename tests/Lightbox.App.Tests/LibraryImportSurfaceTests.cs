using Avalonia.Controls;
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
    public void TheImportCommandIsRegisteredSoItCanBeFoundAndRebound()
    {
        // B58's third assertion, asked of the library: a window reachable only
        // from a button is invisible to Configure and cannot be given a key.
        var map = new ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"library-shortcuts-{Guid.NewGuid():N}.json") };
        Assert.Contains(map.Definitions, d => d.Id == "project.libraryWindow");
    }

    [AvaloniaFact]
    public void TheConfigurePageEditsTheSameRootsTheWindowReads()
    {
        var vm = Open();
        var map = new ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"library-cfg-{Guid.NewGuid():N}.json") };
        var window = new ConfigureWindow(map, vm);
        window.FindControl<Avalonia.Controls.ListBox>("CategoryList")!.SelectedIndex = 7;

        Assert.True(window.FindControl<Avalonia.Controls.ScrollViewer>("LibraryPage")!.IsVisible);
        Assert.False(window.FindControl<Avalonia.Controls.ScrollViewer>("AiPage")!.IsVisible);
        // The page's list IS the view model's collection — one owner (the
        // LibraryViewModel), so Configure, the window and the picker cannot
        // come to hold three different answers about where libraries live.
        var list = window.FindControl<Avalonia.Controls.ListBox>("LibraryRootsList")!;
        Assert.Same(vm.Characters.Roots, list.ItemsSource);

        vm.Characters.AddRoot(_library);
        Assert.Contains(_library, vm.Characters.Roots);
        Assert.Contains(_library, File.ReadAllText(AppSettings.Path));
    }

    [AvaloniaFact]
    public void AnAgentImportsThroughTheSameMergeAndAfterPath()
    {
        Shelf();
        var vm = Open();
        var api = new IpcDocumentApi(vm);
        static IpcProtocol.Request Req(string op, object payload) => new()
        {
            Op = op,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
        };

        // No roots configured and no path passed: told what to do, not a crash.
        var unconfigured = api.Handle(Req("import_character", new { character = "Knight" }));
        Assert.False(unconfigured.Ok);
        Assert.Contains("library", unconfigured.Error!, StringComparison.OrdinalIgnoreCase);

        // A wrong name gets the shelf's contents, so the retry is a decision.
        var wrong = api.Handle(Req("import_character", new { library = _library, character = "Dragon" }));
        Assert.False(wrong.Ok);
        Assert.Contains("Knight", wrong.Error!);

        // A path that is not a library reads as a path problem, not a wrong
        // name — or an agent retries name variations against an empty folder.
        var empty = Path.Combine(_library, "not-a-library");
        Directory.CreateDirectory(empty);
        var badPath = api.Handle(Req("import_character", new { library = empty, character = "Knight" }));
        Assert.False(badPath.Ok);
        Assert.Contains("Asset library", badPath.Error!);
        Assert.DoesNotContain("No character named", badPath.Error!);

        // The import lands through the same after-path the UI uses: the
        // docker shows it and the project is saved, not merely mutated.
        var resp = api.Handle(Req("import_character", new { library = _library, character = "Knight" }));
        Assert.True(resp.Ok, resp.Error);
        Assert.Equal("Knight", resp.Payload!.Value.GetProperty("folder").GetString());
        Assert.Equal("Knights", resp.Payload.Value.GetProperty("library").GetString());
        Assert.Equal(1, resp.Payload.Value.GetProperty("added").GetArrayLength());
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Name == "Knight");
        Assert.Contains("Knight", File.ReadAllText(Path.Combine(Root, "project.json")));
    }

    [AvaloniaFact]
    public void TheAgentsDestructiveGateIsTheFlagAndNothingElse()
    {
        // The IPC op has no dialog, so the edited-copy gate is the flag: unset
        // keeps and reports, exactly the UI default; set replaces. A refactor
        // that hardcoded either side would pass every UI-path test — this is
        // the one that drives it through the op.
        Shelf();
        var vm = Open();
        var api = new IpcDocumentApi(vm);
        static IpcProtocol.Request Req(string op, object payload) => new()
        {
            Op = op,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
        };
        Assert.True(api.Handle(Req("import_character", new { library = _library, character = "Knight" })).Ok);

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

        var kept = api.Handle(Req("import_character", new { library = _library, character = "Knight" }));
        Assert.True(kept.Ok, kept.Error);
        Assert.Equal("Walk", kept.Payload!.Value.GetProperty("keptEdited")[0].GetString());
        Assert.Equal(2, ProjectIo.LoadDocument(project, copy)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes.Count);

        var replaced = api.Handle(Req(
            "import_character", new { library = _library, character = "Knight", replaceEdited = true }));
        Assert.True(replaced.Ok, replaced.Error);
        Assert.Equal("Walk", replaced.Payload!.Value.GetProperty("replaced")[0].GetString());
        Assert.Single(ProjectIo.LoadDocument(project, copy)!
            .Scene.Layers[0].Cels[0].Frame!.Strokes);
    }

    [AvaloniaFact]
    public void ANameTwoLibrariesOfferRefusesRatherThanGuessing()
    {
        // The UI path never guesses — the artist clicked one specific entry —
        // so the agent path must not either: the scan's directory order is
        // not a decision anybody made.
        Shelf();
        var second = ProjectIo.Create("Rivals", Path.Combine(_library, "rivals.lbproj"),
            Core.Projects.ProjectType.AssetLibrary);
        ProjectFolders.Add(second.Manifest, "Knight");
        ProjectIo.Save(second);

        var vm = Open();
        var api = new IpcDocumentApi(vm);
        static IpcProtocol.Request Req(string op, object payload) => new()
        {
            Op = op,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
        };
        var resp = api.Handle(Req("import_character", new { library = _library, character = "Knight" }));
        Assert.False(resp.Ok);
        Assert.Contains("Knights", resp.Error!);
        Assert.Contains("Rivals", resp.Error!);
        Assert.DoesNotContain(
            ProjectFolders.All(vm.ProjectDocker.Project!.Manifest), f => f.Name == "Knight");
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
