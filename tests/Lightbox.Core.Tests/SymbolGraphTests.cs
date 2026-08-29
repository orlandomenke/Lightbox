using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The dependency graph: which documents place which symbols.
/// </summary>
/// <remarks>
/// The last thing Pillar 3 was missing, and the one that makes deleting a
/// symbol a decision rather than a gamble. Placements of a deleted symbol are
/// left alone on purpose — a delete that quietly edited forty animations is
/// worse than one that leaves marks that stop drawing and say so — but that is
/// only defensible if the artist is told how many there are, and until this
/// existed nothing in the application could count them.
/// </remarks>
public sealed class SymbolGraphTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-graph-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Doc Drawing() => DocumentFactory.CreateDoc(120, 80, 12);

    private static Symbol Sword(string name = "Sword")
    {
        var symbol = new Symbol { Name = name };
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Points = [new StrokePoint(4, 4, 1), new StrokePoint(20, 20, 1)],
            Brush = new BrushSettings { Size = 6, Hardness = 1, Opacity = 1 },
        });
        symbol.Layers = Symbol.Flat(symbol.Name, [frame]);
        return symbol;
    }

    private static Frame FirstFrame(Doc doc) =>
        (Frame)doc.Scene.Layers[0].Cels[0].Frame!;

    private static void Place(Doc doc, Symbol symbol, int times = 1)
    {
        var frame = FirstFrame(doc);
        frame.Placements ??= [];
        for (var i = 0; i < times; i++)
        {
            frame.Placements.Add(new SymbolPlacement { SymbolId = symbol.Id, X = 10 * i, Y = 10 });
        }
    }

    /// <summary>Answer from the project's own cache, so no test needs a folder on disk.</summary>
    private static Doc? FromCache(Project project, DocumentRef reference) =>
        project.Loaded.GetValueOrDefault(reference.Id);

    private Project Knight(out Doc walk, out Doc idle, out Symbol sword)
    {
        var project = ProjectIo.Create("Knight", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        walk = Drawing();
        idle = Drawing();
        ProjectIo.AddDocument(project, "Walk", walk, knight);
        ProjectIo.AddDocument(project, "Idle", idle, knight);
        sword = Sword();
        project.Symbols[sword.Id] = sword;
        return project;
    }

    [Fact]
    public void ASymbolKnowsHowManyPlacementsItHasAndWhere()
    {
        // The headline. Three placements in Walk, one in Idle.
        var project = Knight(out var walk, out var idle, out var sword);
        Place(walk, sword, 3);
        Place(idle, sword);

        var usage = SymbolGraph.Usage(project, FromCache)[sword.Id];

        Assert.Equal(4, usage.Total);
        Assert.Equal(2, usage.Documents.Count);
        Assert.Equal("Walk", usage.Documents[0].Document.Name);   // ordered by count
        Assert.Equal(3, usage.Documents[0].Placements);
        Assert.Equal("Knight", usage.Documents[0].Owner);
    }

    [Fact]
    public void ASymbolNothingPlacesSaysSoRatherThanGoingMissing()
    {
        // "Used nowhere" is the answer that makes a delete safe, and it is not
        // the same answer as "absent from the report".
        var project = Knight(out _, out _, out var sword);

        var usage = SymbolGraph.Usage(project, FromCache);

        Assert.True(usage.ContainsKey(sword.Id));
        Assert.True(usage[sword.Id].IsUnused);
        Assert.Equal(0, usage[sword.Id].Total);
    }

    [Fact]
    public void APlacementLeftBehindByADeleteIsStillReported()
    {
        // Deleting a symbol strands its placements on purpose. They draw
        // nothing, so the only way anybody finds them again is a report that
        // covers symbols the project no longer has.
        var project = Knight(out var walk, out _, out var sword);
        Place(walk, sword, 2);
        project.Symbols.Remove(sword.Id);

        var usage = SymbolGraph.Usage(project, FromCache);

        Assert.True(usage.ContainsKey(sword.Id));
        Assert.Equal(2, usage[sword.Id].Total);
    }

    [Fact]
    public void AHeldCelIsOnePlacementNotOnePerExposure()
    {
        // A drawing exposed across four frames is one drawing. Counting it once
        // per exposure would tell an artist a symbol was placed four times when
        // they placed it once, which is the sort of wrong number that makes a
        // report worse than none.
        var project = Knight(out var walk, out _, out var sword);
        Place(walk, sword);
        var layer = walk.Scene.Layers[0];
        var held = layer.Cels[0].Frame;
        for (var i = 0; i < 3; i++) layer.Cels.Add(new Cel { Frame = held });

        var usage = SymbolGraph.Usage(project, FromCache)[sword.Id];

        Assert.Equal(1, usage.Total);
    }

    [Fact]
    public void PlacementsOnEveryLayerAreCounted()
    {
        var project = Knight(out var walk, out _, out var sword);
        Place(walk, sword);
        var top = new Layer { Name = "Top", Kind = LayerKind.Painted };
        var frame = new Frame { Placements = [new SymbolPlacement { SymbolId = sword.Id }] };
        top.Cels.Add(new Cel { Frame = frame });
        walk.Scene.Layers.Add(top);

        Assert.Equal(2, SymbolGraph.Usage(project, FromCache)[sword.Id].Total);
    }

    [Fact]
    public void TwoSymbolsAreCountedApart()
    {
        var project = Knight(out var walk, out _, out var sword);
        var shield = Sword("Shield");
        project.Symbols[shield.Id] = shield;
        Place(walk, sword, 2);
        Place(walk, shield);

        var usage = SymbolGraph.Usage(project, FromCache);

        Assert.Equal(2, usage[sword.Id].Total);
        Assert.Equal(1, usage[shield.Id].Total);
    }

    [Fact]
    public void ADocumentThatCannotBeReadIsSkippedRatherThanFatal()
    {
        // A project can reference a document that is missing from disk. A
        // usage report that throws is one nobody can use to clean that up.
        var project = Knight(out var walk, out _, out var sword);
        Place(walk, sword);

        var usage = SymbolGraph.Usage(project, (_, r) =>
            r.Name == "Idle" ? null : project.Loaded.GetValueOrDefault(r.Id));

        Assert.Equal(1, usage[sword.Id].Total);
    }

    [Fact]
    public void AVariantsOwnArtIsCountedAsItsOwnDocument()
    {
        // An override is a separate drawing with a separate DocumentRef, not
        // another view of the animation it replaces. A knight whose winter
        // armour holds the sword in its own version of the walk is placing it
        // twice across two documents, and a report that folded them together
        // would undercount exactly the case variants exist for.
        var project = ProjectIo.Create("Knight", _root);
        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var walk = Drawing();
        var baseRef = ProjectIo.AddDocument(project, "Walk", walk, knight);
        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        var winterWalk = Drawing();
        ProjectIo.OverrideDocument(project, knight, winter, baseRef, winterWalk);
        var sword = Sword();
        project.Symbols[sword.Id] = sword;
        Place(walk, sword);
        Place(winterWalk, sword, 2);

        var usage = SymbolGraph.Usage(project, FromCache)[sword.Id];

        Assert.Equal(3, usage.Total);
        Assert.Equal(2, usage.Documents.Count);
        Assert.Contains(usage.Documents, d => d.Document.Name == "Walk (Winter)" && d.Placements == 2);
    }

    [Fact]
    public void OfIsTheSameAnswerForOneSymbol()
    {
        var project = Knight(out var walk, out _, out var sword);
        Place(walk, sword, 2);
        ProjectIo.Save(project);

        // Through the real loader this time, so the disk path is exercised too.
        var reloaded = ProjectIo.Load(_root);
        var usage = SymbolGraph.Of(reloaded, sword.Id);

        Assert.Equal(2, usage.Total);
        Assert.Equal("Walk", usage.Documents[0].Document.Name);
    }
}
