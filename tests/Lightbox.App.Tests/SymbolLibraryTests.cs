using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Global symbols as the app sees them: the grid shows both scopes, placing a
/// library symbol copies it in, and promoting sends one up.
/// </summary>
[Collection("BrushState")]
public sealed class SymbolLibraryTests : BrushStateIsolated, IDisposable
{
    private readonly string _store = Path.Combine(
        Path.GetTempPath(), $"lightbox-symlib-{Guid.NewGuid():N}.json");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-symproj-{Guid.NewGuid():N}.lbproj");

    public SymbolLibraryTests() => SymbolLibrary.PathOverride = _store;

    public new void Dispose()
    {
        SymbolLibrary.PathOverride = null;
        if (File.Exists(_store)) File.Delete(_store);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static Symbol Sword(string name = "Sword", int version = 1)
    {
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#101010",
            Points = [new StrokePoint(4, 4, 1), new StrokePoint(40, 40, 1)],
            Brush = new BrushSettings { Size = 8 },
        });
        return new Symbol { Name = name, Kind = SymbolKind.Prop, Version = version, Layers = Symbol.Flat(name, [frame]) };
    }

    private MainViewModel WithProject()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.NewProject(_root, "Knight");
        return vm;
    }

    // ---- the store -------------------------------------------------------------

    [AvaloniaFact]
    public void TheLibraryRoundTripsWithItsDrawings()
    {
        // Through DocJson's converters, because a Symbol holds polymorphic
        // Frames. Plain options reload it as symbols with nothing in them, which
        // looks like an empty library rather than a serialization bug.
        var sword = Sword();
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [sword.Id] = sword });

        var back = SymbolLibrary.Load();

        var loaded = back.Values.Single();
        Assert.Equal("Sword", loaded.Name);
        Assert.Single(loaded.AllFrames);
        Assert.Single(loaded.AllFrames.First().Strokes);
    }

    [AvaloniaFact]
    public void ACorruptLibraryIsAnEmptyOneRatherThanACrash()
    {
        File.WriteAllText(_store, "{ not json at all");
        Assert.Empty(SymbolLibrary.Load());
    }

    [AvaloniaFact]
    public void NoLibraryYetIsNotAnError()
    {
        Assert.Empty(SymbolLibrary.Load());
    }

    // ---- the grid --------------------------------------------------------------

    [AvaloniaFact]
    public void TheGridShowsBothScopesAndBadgesTheGlobalOnes()
    {
        var global = Sword("Library sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });

        var vm = WithProject();
        vm.MakeSymbolFromDrawingForTests("Project sword");
        vm.SymbolBrowser.Refresh();

        var rows = vm.SymbolBrowser.Rows.ToList();
        Assert.Contains(rows, r => r.Name == "Library sword" && r.IsGlobal);
        Assert.Contains(rows, r => r.Name == "Project sword" && !r.IsGlobal);
        Assert.Equal("◈", rows.First(r => r.IsGlobal).ScopeBadge);
    }

    [AvaloniaFact]
    public void TheScopeFilterNarrowsToOneLibrary()
    {
        var global = Sword("Library sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = WithProject();
        vm.MakeSymbolFromDrawingForTests("Project sword");

        vm.SymbolBrowser.ScopeFilter = SymbolScope.Global;
        Assert.Equal("Library sword", vm.SymbolBrowser.Rows.Single().Name);

        vm.SymbolBrowser.ScopeFilter = SymbolScope.Project;
        Assert.Equal("Project sword", vm.SymbolBrowser.Rows.Single().Name);

        vm.SymbolBrowser.ScopeFilter = null;
        Assert.Equal(2, vm.SymbolBrowser.Rows.Count);
    }

    [AvaloniaFact]
    public void AnAdoptedLibrarySymbolAppearsOnceAsAProjectSymbol()
    {
        // Showing it twice would ask the artist to choose between a thing and
        // itself. The project's copy wins because it is what a placement resolves.
        var global = Sword("Shared sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = WithProject();
        vm.SymbolBrowser.Refresh();
        Assert.True(vm.SymbolBrowser.Rows.Single().IsGlobal);

        SymbolScopes.Adopt(vm.ProjectDocker.Project!, global);
        vm.SymbolBrowser.Refresh();

        var row = vm.SymbolBrowser.Rows.Single();
        Assert.False(row.IsGlobal);
        Assert.Equal("Shared sword", row.Name);
    }

    // ---- placing ---------------------------------------------------------------

    [AvaloniaFact]
    public void PlacingALibrarySymbolCopiesItIntoTheProject()
    {
        var global = Sword("Library sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = WithProject();
        vm.SymbolBrowser.Refresh();
        Assert.Empty(vm.ProjectDocker.Project!.Symbols);

        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows.Single();
        var placement = vm.PlaceSelectedSymbol();

        Assert.NotNull(placement);
        Assert.True(vm.ProjectDocker.Project!.Symbols.ContainsKey(global.Id));
        Assert.Equal(global.Id, placement!.SymbolId);
    }

    [AvaloniaFact]
    public void DraggingALibrarySymbolOntoTheCanvasCopiesItToo()
    {
        // The route that a row-based adopt would have missed: drag-and-drop
        // carries only an id, and the two routes look interchangeable from the
        // panel — so the failure would have looked like "sometimes it works".
        var global = Sword("Dragged sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = WithProject();
        vm.SymbolBrowser.Refresh();

        var placement = vm.PlaceSymbol(global.Id, 120, 90);

        Assert.NotNull(placement);
        Assert.True(vm.ProjectDocker.Project!.Symbols.ContainsKey(global.Id));
    }

    [AvaloniaFact]
    public void APlacedLibrarySymbolStillRendersWithTheLibraryDeleted()
    {
        // The whole reason copy-on-place exists.
        var global = Sword("Library sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = WithProject();
        vm.SymbolBrowser.Refresh();
        vm.PlaceSymbol(global.Id, 100, 100);

        File.Delete(_store);

        var mine = vm.ProjectDocker.Project!.Symbols[global.Id];
        Assert.Single(mine.AllFrames);
        Assert.Single(mine.AllFrames.First().Strokes);
    }

    // ---- promoting and pulling -------------------------------------------------

    [AvaloniaFact]
    public void PromotingPutsAProjectSymbolInTheLibraryOnDisk()
    {
        var vm = WithProject();
        vm.MakeSymbolFromDrawingForTests("Knight's sword");
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows.Single();
        Assert.True(vm.CanPromoteSelectedSymbol);

        vm.PromoteSelectedSymbol();

        var saved = SymbolLibrary.Load();
        Assert.Equal("Knight's sword", saved.Values.Single().Name);
        // Still in the project too — promoting copies, it does not move.
        Assert.Single(vm.ProjectDocker.Project!.Symbols);
    }

    [AvaloniaFact]
    public void AGlobalRowCannotBePromoted()
    {
        var global = Sword();
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = WithProject();
        vm.SymbolBrowser.Refresh();
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows.Single();

        Assert.False(vm.CanPromoteSelectedSymbol);
    }

    [AvaloniaFact]
    public void TheProjectIsOfferedANewerLibraryVersionAndTakesItOnAsking()
    {
        var vm = WithProject();
        vm.MakeSymbolFromDrawingForTests("Sword");
        var id = vm.ProjectDocker.Project!.Symbols.Keys.Single();
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows.Single();
        vm.PromoteSelectedSymbol();
        Assert.False(vm.HasLibraryUpdates);

        // The artist improves it in their library, from elsewhere.
        var newer = Sword("Sharper sword", version: 7);
        newer.Id = id;
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [id] = newer });
        vm.ReloadSymbolLibraryForTests();

        Assert.True(vm.HasLibraryUpdates);
        Assert.Equal(1, vm.UpdateSymbolsFromLibrary());
        Assert.Equal("Sharper sword", vm.ProjectDocker.Project!.Symbols[id].Name);
        Assert.False(vm.HasLibraryUpdates);
    }

    [AvaloniaFact]
    public void WithNothingNewerThereIsNothingToOffer()
    {
        var vm = WithProject();
        vm.MakeSymbolFromDrawingForTests("Sword");
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows.Single();
        vm.PromoteSelectedSymbol();

        Assert.False(vm.HasLibraryUpdates);
        Assert.Equal(0, vm.UpdateSymbolsFromLibrary());
        Assert.Contains("matches your library", vm.AiStatus);
    }

    // ---- reachable without a project -------------------------------------------

    [AvaloniaFact]
    public void TheLibraryIsThereWithNoProjectOpen()
    {
        // It is the artist's own library. It should be there when they open the
        // app to draw one picture, not only inside a project.
        var global = Sword("My sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });

        var vm = new MainViewModel(null) { SmoothStrokes = false };
        Assert.False(vm.HasProject);
        vm.SymbolBrowser.Refresh();

        var row = vm.SymbolBrowser.Rows.Single();
        Assert.True(row.IsGlobal);
        Assert.Equal("My sword", row.Name);
    }

    [AvaloniaFact]
    public void PlacingOneInALooseDocumentCopiesItIntoTheDocument()
    {
        // Without a project the *document* is what has to stand alone, so the
        // copy goes into Doc.Symbols — the same key ProjectIo.Flatten writes when
        // an export has to carry its symbols. Same principle one level down.
        var global = Sword("Loose sword");
        SymbolLibrary.Save(new Dictionary<string, Symbol> { [global.Id] = global });
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.SymbolBrowser.Refresh();

        var placement = vm.PlaceSymbol(global.Id, 60, 60);

        Assert.NotNull(placement);
        Assert.NotNull(vm.Doc.Symbols);
        Assert.True(vm.Doc.Symbols!.ContainsKey(global.Id));
        // And it survives a save and reload with nothing else installed.
        var reloaded = Lightbox.Core.Serialization.DocJson.Deserialize(vm.SerializeDocument());
        Assert.Single(reloaded.Symbols![global.Id].AllFrames);
    }

    [AvaloniaFact]
    public void ProjectScopeStaysProjectOnly()
    {
        // "Project only for projects": there is no project scope to show without
        // one, and making a project symbol still needs somewhere to put it.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(30, 30, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();

        Assert.Null(vm.MakeSymbolFromDrawing("Nope"));
        Assert.Contains("project", vm.AiStatus);

        vm.SymbolBrowser.ScopeFilter = SymbolScope.Project;
        vm.SymbolBrowser.Refresh();
        Assert.Empty(vm.SymbolBrowser.Rows);
    }

    [AvaloniaFact]
    public void WithNoProjectTheEmptyMessageDoesNotTellYouToDoSomethingImpossible()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.SymbolBrowser.Refresh();

        Assert.Contains("library", vm.SymbolBrowser.EmptyMessage);
        Assert.DoesNotContain("Draw something, then Make symbol", vm.SymbolBrowser.EmptyMessage);
    }
}
