using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The folder hierarchy: what nests in what, and where everything lands.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant these are all guarding is contiguity.</b> A folder's whole
/// subtree occupies one unbroken run of <c>Scene.Layers</c>, because the docker
/// draws a header once, where its first member appears — so a folder split
/// around a stranger would draw one header with its members scattered under it,
/// and the compositing order would stop matching what the panel shows. Every
/// operation goes through <see cref="LayerTree.Normalise"/> for that reason, and
/// <see cref="EveryFolderIsOneRun"/> is the assertion the rest of the file
/// leans on.
/// </para>
/// <para>
/// Written against the lists rather than through the view model on purpose:
/// this is where the rule lives, and a failure here should point at the rule
/// rather than at a docker three layers of binding away.
/// </para>
/// </remarks>
public class LayerTreeTests(ITestOutputHelper output)
{
    private static Layer L(string name, string? group = null) =>
        new() { Name = name, GroupId = group };

    private static LayerGroup G(string id, string? parent = null) =>
        new() { Id = id, Name = id, ParentId = parent };

    private static LayerGroup Locked(LayerGroup group)
    {
        group.Locked = true;
        return group;
    }

    /// <summary>The stack bottom-first, folders shown as indented headers.</summary>
    private static string Shape(List<Layer> layers, List<LayerGroup> groups) =>
        string.Join(
            " | ",
            LayerTree.Walk(layers, groups).Select(n =>
                new string(' ', n.Depth * 2) + (n.IsGroup ? $"<{n.Group!.Name}>" : n.Layer!.Name)));

    /// <summary>Bottom-first layer names, which is <c>Scene.Layers</c> order.</summary>
    private static string Order(List<Layer> layers) => string.Join(",", layers.Select(l => l.Name));

    /// <summary>
    /// Every folder's subtree is one unbroken run of the layer list.
    /// </summary>
    private static void EveryFolderIsOneRun(
        List<Layer> layers, List<LayerGroup> groups, ITestOutputHelper output)
    {
        var byId = LayerTree.ById(groups);
        foreach (var group in groups)
        {
            var inside = LayerTree.SubtreeGroupIds(group, byId);
            var hits = Enumerable.Range(0, layers.Count)
                .Where(i => layers[i].GroupId is { } id && inside.Contains(id))
                .ToList();
            if (hits.Count == 0) continue;
            var expected = Enumerable.Range(hits[0], hits.Count).ToList();
            if (!hits.SequenceEqual(expected))
            {
                output.WriteLine($"folder {group.Name} occupies {string.Join(",", hits)} — not a run");
                output.WriteLine("order: " + Order(layers));
            }
            Assert.Equal(expected, hits);
        }
    }

    // ---- the walk --------------------------------------------------------------

    [Fact]
    public void AFlatSceneWalksInStackOrderAndNestsNothing()
    {
        List<Layer> layers = [L("paper"), L("a"), L("b")];
        List<LayerGroup> groups = [];
        Assert.Equal("paper | a | b", Shape(layers, groups));
    }

    [Fact]
    public void AFolderHeaderComesImmediatelyBeforeWhatIsInIt()
    {
        List<Layer> layers = [L("paper"), L("in a", "f"), L("in b", "f"), L("over")];
        List<LayerGroup> groups = [G("f")];
        Assert.Equal("paper | <f> |   in a |   in b | over", Shape(layers, groups));
    }

    /// <summary>
    /// The inner folder's header sits inside the outer one's run, and its rows
    /// are indented one further.
    /// </summary>
    [Fact]
    public void ANestedFolderIsDrawnInsideItsParent()
    {
        List<Layer> layers = [L("paper"), L("deep", "inner"), L("shallow", "outer"), L("over")];
        List<LayerGroup> groups = [G("outer"), G("inner", "outer")];
        output.WriteLine(Shape(layers, groups));
        Assert.Equal(
            "paper | <outer> |   <inner> |     deep |   shallow | over",
            Shape(layers, groups));
    }

    /// <summary>
    /// A folder with nothing in it has no layer to be ordered by, so it goes to
    /// the top of whatever contains it — see <see cref="LayerTree.Walk"/>.
    /// </summary>
    [Fact]
    public void AnEmptyFolderGoesToTheTopOfItsContainerRatherThanVanishing()
    {
        List<Layer> layers = [L("paper"), L("a")];
        List<LayerGroup> groups = [G("empty")];
        Assert.Equal("paper | a | <empty>", Shape(layers, groups));
    }

    [Fact]
    public void AnEmptyFolderInsideAFolderStaysInsideIt()
    {
        List<Layer> layers = [L("paper"), L("in", "outer")];
        List<LayerGroup> groups = [G("outer"), G("empty", "outer")];
        Assert.Equal("paper | <outer> |   in |   <empty>", Shape(layers, groups));
    }

    // ---- normalising -----------------------------------------------------------

    /// <summary>
    /// The property that makes it safe to call after every edit: a scene already
    /// in order comes out untouched.
    /// </summary>
    [Fact]
    public void NormalisingAnOrderedSceneChangesNothing()
    {
        List<Layer> layers = [L("paper"), L("deep", "inner"), L("shallow", "outer"), L("over")];
        List<LayerGroup> groups = [G("outer"), G("inner", "outer")];
        var before = Order(layers);
        var groupsBefore = string.Join(",", groups.Select(g => g.Id));
        LayerTree.Normalise(layers, groups);
        Assert.Equal(before, Order(layers));
        Assert.Equal(groupsBefore, string.Join(",", groups.Select(g => g.Id)));
    }

    /// <summary>
    /// A folder scattered through the list — which is what a hand-written file
    /// or an older operation could leave — is pulled back into one run.
    /// </summary>
    [Fact]
    public void AScatteredFolderIsGatheredIntoOneRun()
    {
        List<Layer> layers = [L("paper"), L("in a", "f"), L("stranger"), L("in b", "f")];
        List<LayerGroup> groups = [G("f")];
        LayerTree.Normalise(layers, groups);
        output.WriteLine(Order(layers));
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Equal("paper,in a,in b,stranger", Order(layers));
    }

    /// <summary>
    /// A layer naming a folder that is not there is the one outcome the docker
    /// cannot draw, so it comes back to the top level rather than disappearing.
    /// </summary>
    [Fact]
    public void ALayerNamingAFolderThatIsGoneComesBackToTheTopLevel()
    {
        List<Layer> layers = [L("paper"), L("orphan", "deleted")];
        List<LayerGroup> groups = [];
        LayerTree.Normalise(layers, groups);
        Assert.Null(layers.Single(l => l.Name == "orphan").GroupId);
        Assert.Equal(2, layers.Count);
    }

    /// <summary>
    /// A cycle cannot be authored, and a hand-edited file can carry one. Loading
    /// it must terminate and keep every layer.
    /// </summary>
    [Fact]
    public void ACycleInTheFileIsBrokenRatherThanLoopedOn()
    {
        List<Layer> layers = [L("a", "x"), L("b", "y")];
        List<LayerGroup> groups = [G("x", "y"), G("y", "x")];
        LayerTree.Normalise(layers, groups);
        output.WriteLine(Shape(layers, groups));
        Assert.Equal(2, layers.Count);
        Assert.Equal(2, groups.Count);
        // Whatever the walk decided, no folder may still be its own ancestor.
        var byId = LayerTree.ById(groups);
        foreach (var g in groups) Assert.DoesNotContain(g, LayerTree.AncestorsOf(g, byId));
    }

    [Fact]
    public void AFolderThatIsItsOwnParentIsDetached()
    {
        List<Layer> layers = [L("a", "x")];
        List<LayerGroup> groups = [G("x", "x")];
        LayerTree.Normalise(layers, groups);
        Assert.Null(groups[0].ParentId);
    }

    // ---- moving layers ---------------------------------------------------------

    [Fact]
    public void AFolderFillsFromTheTopWhenALayerIsDroppedIn()
    {
        List<Layer> layers = [L("paper"), L("in a", "f"), L("in b", "f"), L("over")];
        List<LayerGroup> groups = [G("f")];
        LayerTree.MoveLayerInto(layers, groups, layers.Single(l => l.Name == "over"), "f");
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Equal("paper,in a,in b,over", Order(layers));
        Assert.Equal("f", layers.Single(l => l.Name == "over").GroupId);
    }

    [Fact]
    public void ALayerDroppedIntoAnEmptyFolderIsTheOnlyThingInIt()
    {
        List<Layer> layers = [L("paper"), L("a")];
        List<LayerGroup> groups = [G("empty")];
        LayerTree.MoveLayerInto(layers, groups, layers.Single(l => l.Name == "a"), "empty");
        Assert.Equal("paper | <empty> |   a", Shape(layers, groups));
    }

    [Fact]
    public void ALayerDroppedBesideAGroupedRowJoinsThatRowsFolder()
    {
        List<Layer> layers = [L("paper"), L("in a", "f"), L("in b", "f"), L("over")];
        List<LayerGroup> groups = [G("f")];
        LayerTree.PlaceLayerBeside(
            layers, groups,
            layers.Single(l => l.Name == "over"), layers.Single(l => l.Name == "in a"), above: true);
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Equal("f", layers.Single(l => l.Name == "over").GroupId);
        Assert.Equal("paper,in a,over,in b", Order(layers));
    }

    [Fact]
    public void ALayerDroppedBesideALooseRowLeavesTheFolderItWasIn()
    {
        List<Layer> layers = [L("paper"), L("in a", "f"), L("in b", "f"), L("over")];
        List<LayerGroup> groups = [G("f")];
        LayerTree.PlaceLayerBeside(
            layers, groups,
            layers.Single(l => l.Name == "in b"), layers.Single(l => l.Name == "over"), above: true);
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Null(layers.Single(l => l.Name == "in b").GroupId);
        Assert.Equal("paper,in a,over,in b", Order(layers));
    }

    /// <summary>Beside a folder is outside it — including outside a nested one.</summary>
    [Fact]
    public void ALayerDroppedBesideANestedFolderLandsInTheParentNotTheChild()
    {
        List<Layer> layers = [L("paper"), L("deep", "inner"), L("shallow", "outer"), L("over")];
        List<LayerGroup> groups = [G("outer"), G("inner", "outer")];
        LayerTree.PlaceLayerBesideGroup(
            layers, groups,
            layers.Single(l => l.Name == "over"), groups.Single(g => g.Id == "inner"), above: true);
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Equal("outer", layers.Single(l => l.Name == "over").GroupId);
        Assert.Equal("paper | <outer> |   <inner> |     deep |   over |   shallow", Shape(layers, groups));
    }

    // ---- moving folders --------------------------------------------------------

    [Fact]
    public void AFolderDroppedIntoAnotherTakesEverythingWithIt()
    {
        List<Layer> layers = [L("paper"), L("in a", "a"), L("in b", "b")];
        List<LayerGroup> groups = [G("a"), G("b")];
        Assert.True(LayerTree.MoveGroupInto(layers, groups, groups.Single(g => g.Id == "a"), "b"));
        EveryFolderIsOneRun(layers, groups, output);
        output.WriteLine(Shape(layers, groups));
        Assert.Equal("b", groups.Single(g => g.Id == "a").ParentId);
        Assert.Equal("paper | <b> |   in b |   <a> |     in a", Shape(layers, groups));
    }

    [Fact]
    public void AFolderCannotBeDroppedIntoItself()
    {
        List<Layer> layers = [L("in a", "a")];
        List<LayerGroup> groups = [G("a")];
        Assert.False(LayerTree.MoveGroupInto(layers, groups, groups[0], "a"));
        Assert.Null(groups[0].ParentId);
    }

    /// <summary>
    /// The refusal that matters most: filing a folder into its own child would
    /// cut both of them out of the tree, and every layer in them with it.
    /// </summary>
    [Fact]
    public void AFolderCannotBeDroppedIntoItsOwnDescendant()
    {
        List<Layer> layers = [L("deep", "inner")];
        List<LayerGroup> groups = [G("outer"), G("inner", "outer")];
        Assert.False(LayerTree.MoveGroupInto(layers, groups, groups.Single(g => g.Id == "outer"), "inner"));
        Assert.Null(groups.Single(g => g.Id == "outer").ParentId);
        Assert.Equal("outer", groups.Single(g => g.Id == "inner").ParentId);
    }

    [Fact]
    public void NestingStopsAtTheDepthLimit()
    {
        List<Layer> layers = [];
        List<LayerGroup> groups = [];
        string? parent = null;
        for (var i = 0; i <= LayerTree.MaxDepth; i++)
        {
            var g = G($"g{i}", parent);
            groups.Add(g);
            layers.Add(L($"l{i}", g.Id));
            parent = g.Id;
        }
        var byId = LayerTree.ById(groups);
        Assert.Equal(LayerTree.MaxDepth, LayerTree.DepthOf(groups[^1], byId));

        // One more level is refused rather than silently drawn off the edge.
        var extra = G("extra");
        groups.Add(extra);
        layers.Add(L("extra layer", "extra"));
        Assert.False(LayerTree.MoveGroupInto(layers, groups, extra, groups[^2].Id));
    }

    /// <summary>
    /// A folder carrying its own folders cannot be filed somewhere that would
    /// push its deepest child past the limit, even though the folder itself fits.
    /// </summary>
    [Fact]
    public void TheLimitCountsWhatTheFolderIsCarrying()
    {
        List<Layer> layers = [];
        List<LayerGroup> groups = [];
        string? parent = null;
        for (var i = 0; i < LayerTree.MaxDepth; i++)
        {
            var g = G($"chain{i}", parent);
            groups.Add(g);
            layers.Add(L($"l{i}", g.Id));
            parent = g.Id;
        }
        // A two-deep folder of its own, filed at the top level.
        var tall = G("tall");
        var tallChild = G("tallChild", "tall");
        groups.Add(tall);
        groups.Add(tallChild);
        layers.Add(L("inside", tallChild.Id));

        var byId = LayerTree.ById(groups);
        Assert.Equal(1, LayerTree.HeightOf(tall, groups));
        // The chain's last link is at MaxDepth - 1; one more would fit `tall`
        // alone and not the child it is carrying.
        Assert.Equal(LayerTree.MaxDepth - 1, LayerTree.DepthOf(groups[LayerTree.MaxDepth - 1], byId));
        Assert.False(LayerTree.MoveGroupInto(layers, groups, tall, groups[LayerTree.MaxDepth - 1].Id));
    }

    [Fact]
    public void AFolderDroppedBesideALooseRowComesOutToTheTopLevel()
    {
        List<Layer> layers = [L("paper"), L("deep", "inner"), L("shallow", "outer"), L("over")];
        List<LayerGroup> groups = [G("outer"), G("inner", "outer")];
        Assert.True(LayerTree.PlaceGroupBeside(
            layers, groups,
            groups.Single(g => g.Id == "inner"),
            layers.Single(l => l.Name == "over"), null, above: true));
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Null(groups.Single(g => g.Id == "inner").ParentId);
        Assert.Equal("paper | <outer> |   shallow | over | <inner> |   deep", Shape(layers, groups));
    }

    [Fact]
    public void AFolderDroppedBesideAGroupedRowJoinsThatRowsFolder()
    {
        List<Layer> layers = [L("paper"), L("in outer", "outer"), L("in loose", "loose")];
        List<LayerGroup> groups = [G("outer"), G("loose")];
        Assert.True(LayerTree.PlaceGroupBeside(
            layers, groups,
            groups.Single(g => g.Id == "loose"),
            layers.Single(l => l.Name == "in outer"), null, above: true));
        EveryFolderIsOneRun(layers, groups, output);
        Assert.Equal("outer", groups.Single(g => g.Id == "loose").ParentId);
        Assert.Equal("paper | <outer> |   in outer |   <loose> |     in loose", Shape(layers, groups));
    }

    [Fact]
    public void AFolderDroppedBesideOneOfItsOwnRowsIsRefused()
    {
        List<Layer> layers = [L("paper"), L("deep", "inner")];
        List<LayerGroup> groups = [G("outer"), G("inner", "outer")];
        // "deep" is inside "outer", so outer would become its own descendant's sibling.
        Assert.False(LayerTree.PlaceGroupBeside(
            layers, groups,
            groups.Single(g => g.Id == "outer"),
            layers.Single(l => l.Name == "deep"), null, above: true));
    }

    /// <summary>Empty folders reorder too, or a folder made before it is filled could never be moved.</summary>
    [Fact]
    public void AnEmptyFolderCanStillBeMoved()
    {
        List<Layer> layers = [L("paper"), L("in outer", "outer")];
        List<LayerGroup> groups = [G("outer"), G("empty")];
        Assert.Equal("paper | <outer> |   in outer | <empty>", Shape(layers, groups));
        Assert.True(LayerTree.MoveGroupInto(layers, groups, groups.Single(g => g.Id == "empty"), "outer"));
        Assert.Equal("paper | <outer> |   in outer |   <empty>", Shape(layers, groups));
    }

    // ---- gating ----------------------------------------------------------------

    /// <summary>
    /// Hiding an outer folder hides what is in the folders inside it — or the eye
    /// would mean something different depending on how deeply things were filed.
    /// </summary>
    [Fact]
    public void HidingAnOuterFolderHidesADeeplyNestedLayer()
    {
        var scene = new Scene
        {
            Layers = [L("deep", "inner")],
            LayerGroups = [G("outer"), G("inner", "outer")],
        };
        Assert.True(scene.IsLayerVisible(scene.Layers[0]));
        scene.LayerGroups.Single(g => g.Id == "outer").Visible = false;
        Assert.False(scene.IsLayerVisible(scene.Layers[0]));
    }

    [Fact]
    public void LockingAnOuterFolderLocksADeeplyNestedLayerAndNamesItself()
    {
        var scene = new Scene
        {
            Layers = [L("deep", "inner")],
            LayerGroups = [G("outer"), G("inner", "outer")],
        };
        Assert.True(scene.IsLayerEditable(scene.Layers[0]));
        scene.LayerGroups.Single(g => g.Id == "outer").Locked = true;
        Assert.False(scene.IsLayerEditable(scene.Layers[0]));
        Assert.Equal("outer", scene.LockingFolderOver(scene.Layers[0])!.Id);
    }

    /// <summary>The innermost one, so the status line names the folder to unlock first.</summary>
    [Fact]
    public void TheNamedLockIsTheInnermostOne()
    {
        var scene = new Scene
        {
            Layers = [L("deep", "inner")],
            LayerGroups = [Locked(G("outer")), Locked(G("inner", "outer"))],
        };
        Assert.Equal("inner", scene.LockingFolderOver(scene.Layers[0])!.Id);
    }

    // ---- the record ------------------------------------------------------------

    /// <summary>
    /// Absent unless used, both halves: a document that never made a folder
    /// writes no folder key, and a folder that was never nested writes no parent.
    /// </summary>
    [Fact]
    public void AnUnnestedDocumentWritesNeitherKey()
    {
        var flat = new Doc { Scene = new Scene { Layers = [L("a")] } };
        var json = DocJson.Serialize(flat);
        Assert.DoesNotContain("\"layerGroups\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"parentId\"", json, StringComparison.Ordinal);

        var foldered = new Doc
        {
            Scene = new Scene { Layers = [L("a", "f")], LayerGroups = [G("f")] },
        };
        var withFolder = DocJson.Serialize(foldered);
        Assert.Contains("\"layerGroups\"", withFolder, StringComparison.Ordinal);
        Assert.DoesNotContain("\"parentId\"", withFolder, StringComparison.Ordinal);
    }

    [Fact]
    public void NestingSurvivesASaveAndLoad()
    {
        var doc = new Doc
        {
            Scene = new Scene
            {
                Layers = [L("deep", "inner"), L("shallow", "outer")],
                LayerGroups = [G("outer"), G("inner", "outer")],
            },
        };
        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.Equal("outer", back.Scene.LayerGroups.Single(g => g.Id == "inner").ParentId);
        Assert.Equal(
            Shape(doc.Scene.Layers, doc.Scene.LayerGroups),
            Shape(back.Scene.Layers, back.Scene.LayerGroups));
    }

    /// <summary>
    /// A document written before nesting has no parent key at all, which is
    /// exactly what a top-level folder looks like — so it loads unchanged.
    /// </summary>
    [Fact]
    public void ADocumentFromBeforeNestingLoadsAsFlatFolders()
    {
        const string json = """
            {
              "scene": {
                "layers": [
                  { "id": "l1", "name": "in a", "groupId": "g1" },
                  { "id": "l2", "name": "in b", "groupId": "g1" }
                ],
                "layerGroups": [ { "id": "g1", "name": "Folder 1" } ]
              }
            }
            """;
        var doc = DocJson.Deserialize(json);
        Assert.Null(doc.Scene.LayerGroups[0].ParentId);
        Assert.Equal("<Folder 1> |   in a |   in b", Shape(doc.Scene.Layers, doc.Scene.LayerGroups));
    }

    /// <summary>Cloning for undo keeps the nesting, and shares nothing.</summary>
    [Fact]
    public void CloningKeepsTheNestingAndSharesNothing()
    {
        var doc = new Doc
        {
            Scene = new Scene
            {
                Layers = [L("deep", "inner")],
                LayerGroups = [G("outer"), G("inner", "outer")],
            },
        };
        var copy = doc.Clone();
        copy.Scene.LayerGroups.Single(g => g.Id == "inner").ParentId = null;
        Assert.Equal("outer", doc.Scene.LayerGroups.Single(g => g.Id == "inner").ParentId);
    }
}
