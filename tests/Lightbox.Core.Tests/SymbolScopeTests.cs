using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Global and project symbols: the library is a source to choose from, and
/// placing one copies it into the project.
/// </summary>
public class SymbolScopeTests
{
    private static Symbol Sword(string name = "Sword", int version = 1)
    {
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#202020",
            Points = [new StrokePoint(2, 2, 1), new StrokePoint(30, 30, 1)],
            Brush = new BrushSettings { Size = 6 },
        });
        return new Symbol
        {
            Name = name,
            Kind = SymbolKind.Prop,
            Version = version,
            Frames = { frame },
        };
    }

    private static Project Empty() => ProjectIo.Create("Knight", "/tmp/scope-test.lbproj");

    // ---- adopting: the property the whole design rests on -----------------------

    [Fact]
    public void PlacingAGlobalSymbolCopiesItIntoTheProject()
    {
        var library = new Dictionary<string, Symbol> { ["sym-1"] = Sword() };
        library["sym-1"].Id = "sym-1";
        var project = Empty();

        var adopted = SymbolScopes.Adopt(project, library["sym-1"]);

        // Same id, so a placement resolves and a later pull can match it...
        Assert.Equal("sym-1", adopted.Id);
        Assert.True(project.Symbols.ContainsKey("sym-1"));
        // ...and a different object, so the two cannot alias.
        Assert.NotSame(library["sym-1"], project.Symbols["sym-1"]);
        Assert.NotSame(library["sym-1"].Frames[0], project.Symbols["sym-1"].Frames[0]);
    }

    [Fact]
    public void TheProjectRendersWithTheLibraryGone()
    {
        // The reason copy-on-place exists at all: a .lbproj that reached into an
        // application folder to draw would lose art the moment it moved machine.
        var global = Sword();
        var project = Empty();
        SymbolScopes.Adopt(project, global);

        // The library is deleted, edited, whatever — the project is unaffected.
        global.Frames.Clear();
        global.Name = "gone";

        var mine = project.Symbols[global.Id];
        Assert.Single(mine.Frames);
        Assert.Equal("Sword", mine.Name);
        Assert.Single(((Frame)mine.Frames[0]).Strokes);
    }

    [Fact]
    public void AdoptingTwiceAdoptsOnce()
    {
        // Placing the same library symbol on ten frames must copy it once, and
        // must not overwrite an edit the artist made to their project's copy.
        var global = Sword();
        var project = Empty();
        SymbolScopes.Adopt(project, global);
        project.Symbols[global.Id].Name = "My sword";

        var again = SymbolScopes.Adopt(project, global);

        Assert.Single(project.Symbols);
        Assert.Equal("My sword", again.Name);
    }

    // ---- promoting -------------------------------------------------------------

    [Fact]
    public void PromotingCopiesUpAndKeepsTheId()
    {
        var project = Empty();
        var mine = Sword("Knight's sword");
        project.Symbols[mine.Id] = mine;
        var library = new Dictionary<string, Symbol>();

        var promoted = SymbolScopes.Promote(library, mine);

        Assert.Equal(mine.Id, promoted.Id);
        Assert.Equal("Knight's sword", library[mine.Id].Name);
        // The project keeps its own; promoting is a copy, not a move.
        Assert.True(project.Symbols.ContainsKey(mine.Id));
        Assert.NotSame(mine, library[mine.Id]);
    }

    [Fact]
    public void PromotingAnEditedSymbolReplacesTheLibraryEntry()
    {
        // The alternative is a second entry with the same name and no way to tell
        // which is which.
        var library = new Dictionary<string, Symbol>();
        var mine = Sword();
        SymbolScopes.Promote(library, mine);

        mine.Name = "Sword v2";
        mine.Version = 2;
        SymbolScopes.Promote(library, mine);

        Assert.Single(library);
        Assert.Equal("Sword v2", library[mine.Id].Name);
    }

    // ---- the pull --------------------------------------------------------------

    [Fact]
    public void EditingALibrarySymbolDoesNotReachIntoAProjectThatPlacedIt()
    {
        // The trade copy-on-place makes, and it has to be explicit rather than
        // discovered: the safety costs you the automatic update.
        var library = new Dictionary<string, Symbol>();
        var original = Sword();
        SymbolScopes.Promote(library, original);
        var project = Empty();
        SymbolScopes.Adopt(project, library[original.Id]);

        library[original.Id].Name = "Sharper sword";
        library[original.Id].Version = 2;

        Assert.Equal("Sword", project.Symbols[original.Id].Name);
    }

    [Fact]
    public void AskingForTheUpdateTakesIt()
    {
        var library = new Dictionary<string, Symbol>();
        var original = Sword();
        SymbolScopes.Promote(library, original);
        var project = Empty();
        SymbolScopes.Adopt(project, library[original.Id]);

        library[original.Id] = Sword("Sharper sword", version: 4);
        library[original.Id].Id = original.Id;

        var stale = SymbolScopes.Outdated(project, library);
        Assert.Single(stale);
        Assert.Equal(1, stale[0].FromVersion);
        Assert.Equal(4, stale[0].ToVersion);

        Assert.True(SymbolScopes.Pull(project, library, original.Id));
        Assert.Equal("Sharper sword", project.Symbols[original.Id].Name);
        Assert.Equal(4, project.Symbols[original.Id].Version);
    }

    [Fact]
    public void APullNeverGoesBackwards()
    {
        // A library entry older than the project's copy is not offered, because
        // taking it would undo the artist's own work — which is never what
        // "update" means.
        var library = new Dictionary<string, Symbol>();
        var mine = Sword("Mine", version: 5);
        var project = Empty();
        project.Symbols[mine.Id] = mine;
        library[mine.Id] = Sword("Older", version: 2);
        library[mine.Id].Id = mine.Id;

        Assert.Empty(SymbolScopes.Outdated(project, library));
        Assert.False(SymbolScopes.Pull(project, library, mine.Id));
        Assert.Equal("Mine", project.Symbols[mine.Id].Name);
    }

    [Fact]
    public void APullLeavesPlacementsAlone()
    {
        // And does not need to touch them: a placement references the symbol by
        // id, so replacing what that id resolves to *is* the update. That is the
        // property that made symbols worth having in the first place.
        var library = new Dictionary<string, Symbol>();
        var original = Sword();
        SymbolScopes.Promote(library, original);
        var project = Empty();
        SymbolScopes.Adopt(project, library[original.Id]);

        var frame = new Frame { Placements = [new SymbolPlacement { SymbolId = original.Id, X = 10, Y = 20 }] };
        var placementId = frame.Placements![0].Id;

        library[original.Id] = Sword("New", version: 9);
        library[original.Id].Id = original.Id;
        SymbolScopes.Pull(project, library, original.Id);

        Assert.Equal(placementId, frame.Placements[0].Id);
        Assert.Equal(original.Id, frame.Placements[0].SymbolId);
        Assert.Equal(10, frame.Placements[0].X);
    }

    [Fact]
    public void ASymbolTheLibraryDoesNotHaveIsNotStale()
    {
        var project = Empty();
        var mine = Sword();
        project.Symbols[mine.Id] = mine;

        Assert.Empty(SymbolScopes.Outdated(project, new Dictionary<string, Symbol>()));
        Assert.False(SymbolScopes.Pull(project, new Dictionary<string, Symbol>(), mine.Id));
    }

    [Fact]
    public void PullingSomethingTheProjectDoesNotHaveIsANoOp()
    {
        var library = new Dictionary<string, Symbol> { ["nope"] = Sword() };
        Assert.False(SymbolScopes.Pull(Empty(), library, "nope"));
    }

    [Fact]
    public void ALibrarySymbolRoundTripsThroughTheDocumentsOwnSerializer()
    {
        // A Symbol holds polymorphic Frames, so only DocJson's converter can
        // rebuild them. A library written with plain options reloads as symbols
        // with no drawings in them, which looks like an empty library rather than
        // a serialization bug.
        var library = new Dictionary<string, Symbol>();
        SymbolScopes.Promote(library, Sword());

        var json = System.Text.Json.JsonSerializer.Serialize(library, DocJson.Options);
        var back = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, Symbol>>(json, DocJson.Options)!;

        var symbol = back.Values.Single();
        Assert.Single(symbol.Frames);
        Assert.Single(((Frame)symbol.Frames[0]).Strokes);
    }


    // ---- the tree axis (Q30 step 5) -----------------------------------------
    //
    // Last of the five because it NARROWS. Every other kind went from nothing to
    // somewhere; a symbol is already project-wide, so folder depth can only take
    // something away — and an existing project must keep meaning "everything".

    private static (ProjectManifest Manifest, ProjectFolder Knight, ProjectFolder Goblin,
        DocumentRef Walk, DocumentRef Lurk) TwoCharacters()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        var knight = ProjectFolders.Add(manifest, "Knight", null);
        var goblin = ProjectFolders.Add(manifest, "Goblin", null);
        var walk = new DocumentRef { Name = "Walk", FolderId = knight.Id };
        var lurk = new DocumentRef { Name = "Lurk", FolderId = goblin.Id };
        manifest.Documents.Add(walk);
        manifest.Documents.Add(lurk);
        return (manifest, knight, goblin, walk, lurk);
    }

    /// <summary>
    /// A project that declares nothing offers every symbol, exactly as before.
    /// </summary>
    /// <remarks>
    /// The migration rule, and the one that matters most here: every project in
    /// existence is in this state, and a narrowing that took effect by default
    /// would empty their symbol pickers. Null rather than a list of everything,
    /// so <em>unscoped</em> cannot be confused with <em>scoped to all of them</em>.
    /// </remarks>
    [Fact]
    public void WithNothingDeclaredEverySymbolIsStillOffered()
    {
        var (manifest, _, _, walk, _) = TwoCharacters();
        Assert.False(SymbolScopes.AnyDeclared(manifest));
        Assert.Null(SymbolScopes.VisibleTo(manifest, walk));
        Assert.True(SymbolScopes.CanPlace(manifest, walk, "sym-anything"));
    }

    /// <summary>Declaring on a folder narrows it to that subtree.</summary>
    [Fact]
    public void ASymbolDeclaredOnAFolderIsOfferedThereAndNotElsewhere()
    {
        var (manifest, knight, _, walk, lurk) = TwoCharacters();
        ResourceScopes.Declare(manifest, knight, SymbolScopes.Kind, "sym-sword");

        Assert.True(SymbolScopes.AnyDeclared(manifest));
        Assert.Equal(["sym-sword"], SymbolScopes.VisibleTo(manifest, walk)!);
        Assert.True(SymbolScopes.CanPlace(manifest, walk, "sym-sword"));

        // The goblin sees nothing, which is the narrowing working — and the
        // reason the first declaration is worth saying out loud in the UI.
        Assert.Empty(SymbolScopes.VisibleTo(manifest, lurk)!);
        Assert.False(SymbolScopes.CanPlace(manifest, lurk, "sym-sword"));
    }

    /// <summary>It reaches all the way down, however deep the tree gets.</summary>
    [Fact]
    public void ADeclarationReachesEveryDepthBeneathIt()
    {
        var (manifest, knight, _, _, _) = TwoCharacters();
        var locomotion = ProjectFolders.Add(manifest, "Locomotion", knight);
        var runs = ProjectFolders.Add(manifest, "Runs", locomotion);
        var sprint = new DocumentRef { Name = "Sprint", FolderId = runs.Id };
        manifest.Documents.Add(sprint);

        ResourceScopes.Declare(manifest, knight, SymbolScopes.Kind, "sym-sword");
        Assert.Contains("sym-sword", SymbolScopes.VisibleTo(manifest, sprint)!);
    }

    /// <summary>Published from the asset library, everything can place it.</summary>
    /// <remarks>
    /// Workflow 4 — the sword in the asset library that characters, environments
    /// and props all place. A cascade alone cannot express it: the folder the
    /// sword lives in is an ancestor of nothing that wants it.
    /// </remarks>
    [Fact]
    public void APublishedSymbolReachesTheWholeProject()
    {
        var (manifest, knight, _, walk, lurk) = TwoCharacters();
        var library = ProjectFolders.Add(manifest, "Assets", null);
        ResourceScopes.Declare(manifest, knight, SymbolScopes.Kind, "sym-helm");
        var sword = ResourceScopes.Declare(manifest, library, SymbolScopes.Kind, "sym-sword");

        Assert.False(SymbolScopes.CanPlace(manifest, lurk, "sym-sword"));
        ResourceScopes.Promote(sword);

        Assert.True(SymbolScopes.CanPlace(manifest, lurk, "sym-sword"));
        Assert.True(SymbolScopes.CanPlace(manifest, walk, "sym-sword"));
        // And the knight keeps its own as well as the published one.
        Assert.True(SymbolScopes.CanPlace(manifest, walk, "sym-helm"));
    }

    /// <summary>
    /// A document that already places a symbol keeps drawing it when the scope
    /// no longer offers it.
    /// </summary>
    /// <remarks>
    /// <b>The line this whole step had to stay on the right side of.</b> Scoping
    /// governs the picker, never the renderer: a placement resolves by id through
    /// <c>SymbolRegistry</c>, so dragging a document into a folder that declares
    /// nothing must change what it is <em>offered</em> and not one pixel of what
    /// it <em>draws</em>. Rendering that consulted the scope would break
    /// invariant 1 and invariant 4 at once — the same rule
    /// <c>TheProjectRendersWithTheLibraryGone</c> states from the other side.
    /// </remarks>
    [Fact]
    public void ScopingNarrowsThePickerAndNeverWhatIsAlreadyDrawn()
    {
        var (manifest, knight, goblin, _, lurk) = TwoCharacters();
        ResourceScopes.Declare(manifest, knight, SymbolScopes.Kind, "sym-sword");

        // The goblin cannot be offered the sword...
        Assert.False(SymbolScopes.CanPlace(manifest, lurk, "sym-sword"));

        // ...and the thing a render resolves through is untouched by any of it.
        // A placement looks the symbol up by id in the project's own dictionary,
        // which the scope never filters — the declaration is a separate list
        // that only the picker reads.
        var project = new Project(manifest, "/tmp/does-not-matter");
        var sword = Sword();
        project.Symbols[sword.Id] = sword;

        Assert.True(project.Symbols.ContainsKey(sword.Id));
        Assert.Same(sword, project.Symbols[sword.Id]);
        // And declaring the sword somewhere else does not remove it either.
        ResourceScopes.Declare(manifest, goblin, SymbolScopes.Kind, "sym-club");
        Assert.Same(sword, project.Symbols[sword.Id]);
    }
}
