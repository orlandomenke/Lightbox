using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q204: folders that can be empty and can nest — the tree over the flat layer
/// list, the docker's rows, and an empty folder keeping its place.
/// </summary>
public class FolderTreeTests
{
    /// <summary>Layers bottom-first, each with its id equal to its name.</summary>
    private static Scene Stack(params string[] bottomFirst)
    {
        var scene = new Scene();
        foreach (var name in bottomFirst) scene.Layers.Add(new Layer { Id = name, Name = name });
        return scene;
    }

    private static LayerGroup Folder(Scene scene, string id, string? parent = null, string? under = null)
    {
        var folder = new LayerGroup { Id = id, Name = id, ParentId = parent, Under = under };
        scene.LayerGroups.Add(folder);
        return folder;
    }

    private static Layer L(Scene scene, string id) => scene.Layers.Single(l => l.Id == id);

    /// <summary>The docker as text, top first: indent per depth, folders in brackets, a repeat marked +.</summary>
    private static string Docker(Scene scene) => string.Join(" ", FolderTree.Rows(scene).Select(r =>
        new string('.', r.Depth) + r.Item switch
        {
            LayerGroup g => $"[{g.Id}{(r.Continued ? "+" : "")}]",
            Layer l => l.Id,
            _ => "?",
        } + (r.Hidden ? "~" : "")));

    // ---- what is written -----------------------------------------------------------

    [Fact]
    public void AFileWithOnlyTopLevelFoldersThatHoldLayersWritesNoNewKey()
    {
        var doc = new Doc();
        doc.Scene.Layers.AddRange([new Layer { Name = "a" }, new Layer { Name = "b" }]);
        var folder = new LayerGroup();
        doc.Scene.LayerGroups.Add(folder);
        doc.Scene.Layers[1].GroupId = folder.Id;
        FolderTree.Settle(doc.Scene, null);

        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"parentId\"", json);
        Assert.DoesNotContain("\"under\"", json);
    }

    [Fact]
    public void AnEmptyFolderAtTheTopWritesNoSlot_AndANestedOneWritesItsParent()
    {
        var doc = new Doc();
        doc.Scene = Stack("paper", "a");
        Folder(doc.Scene, "outer");
        Folder(doc.Scene, "inner", parent: "outer");
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"under\"", json);
        Assert.Contains("\"parentId\": \"outer\"", json);

        var back = DocJson.Deserialize(json);
        Assert.Equal(Docker(doc.Scene), Docker(back.Scene));
    }

    // ---- gating through every level ------------------------------------------------

    [Fact]
    public void VisibilityAndLockInheritThroughEveryFolderAbove()
    {
        var scene = Stack("paper", "a");
        var top = Folder(scene, "top");
        Folder(scene, "mid", parent: "top");
        Folder(scene, "low", parent: "mid");
        L(scene, "a").GroupId = "low";

        Assert.True(scene.IsLayerVisible(L(scene, "a")));
        top.Visible = false;
        Assert.False(scene.IsLayerVisible(L(scene, "a")));
        top.Visible = true;

        Assert.True(scene.IsLayerEditable(L(scene, "a")));
        top.Locked = true;
        Assert.False(scene.IsLayerEditable(L(scene, "a")));
        Assert.Equal("top", FolderTree.LockedFolderOf(scene, L(scene, "a"))?.Id);
    }

    [Fact]
    public void AParentLoopNeitherHangsNorLosesAFolder()
    {
        var scene = Stack("paper", "a");
        Folder(scene, "x", parent: "y");
        Folder(scene, "y", parent: "x");
        Folder(scene, "z", parent: "nowhere");
        L(scene, "a").GroupId = "x";

        Assert.True(scene.IsLayerVisible(L(scene, "a")));
        var shown = FolderTree.Rows(scene).Select(r => r.Item).OfType<LayerGroup>().Select(g => g.Id).ToHashSet();
        Assert.Equal(["x", "y", "z"], shown.Order());
    }

    // ---- the docker's rows ---------------------------------------------------------

    [Fact]
    public void AnEmptyFolderShowsAtTheTop_UnderALayer_AndInsideAnother()
    {
        var scene = Stack("paper", "a", "b", "c");
        Folder(scene, "top");
        Folder(scene, "mid", under: "c");
        Folder(scene, "holder");
        L(scene, "b").GroupId = "holder";
        L(scene, "a").GroupId = "holder";
        Folder(scene, "deep", parent: "holder", under: "b");

        Assert.Equal("[top] c [mid] [holder] .b .[deep] .a paper", Docker(scene));
    }

    [Fact]
    public void FoldersNest_AndAnEmptyFolderInsideAnEmptyOneIsShownInsideIt()
    {
        var scene = Stack("paper", "a");
        Folder(scene, "outer");
        Folder(scene, "inner", parent: "outer");
        L(scene, "a").GroupId = "inner";
        Folder(scene, "box");
        Folder(scene, "boxed", parent: "box");

        Assert.Equal("[box] .[boxed] [outer] .[inner] ..a paper", Docker(scene));
    }

    [Fact]
    public void ACollapsedFolderHidesEverythingInsideIt()
    {
        var scene = Stack("paper", "a");
        Folder(scene, "outer").Collapsed = true;
        Folder(scene, "inner", parent: "outer");
        L(scene, "a").GroupId = "inner";
        Folder(scene, "empty", parent: "outer");

        Assert.Equal("[outer] .[empty]~ .[inner]~ ..a~ paper", Docker(scene));
    }

    [Fact]
    public void AFolderSplitAroundAnotherLayerShowsItsHeaderAgainRatherThanReorderingTheStack()
    {
        var scene = Stack("paper", "a", "b", "c");
        Folder(scene, "f");
        L(scene, "c").GroupId = "f";
        L(scene, "a").GroupId = "f";

        Assert.Equal("[f] .c b [f+] .a paper", Docker(scene));
        Assert.Equal(["paper", "a", "b", "c"], scene.Layers.Select(l => l.Id));
    }

    [Fact]
    public void ADocumentWithNoFoldersListsItsLayersTopFirst()
    {
        Assert.Equal("c b a", Docker(Stack("a", "b", "c")));
    }

    // ---- an empty folder keeps its place --------------------------------------------

    [Fact]
    public void AFolderWhoseLastLayerIsDraggedOutStaysWhereItWas()
    {
        var doc = new Doc { Scene = Stack("paper", "a", "b", "c") };
        Folder(doc.Scene, "f");
        L(doc.Scene, "b").GroupId = "f";
        var editor = new DocumentEditor(doc);
        Assert.Equal("c [f] .b a paper", Docker(editor.Doc.Scene));

        editor.Perform(d =>
        {
            var b = L(d.Scene, "b");
            d.Scene.Layers.Remove(b);
            d.Scene.Layers.Add(b);
            b.GroupId = null;
        });

        Assert.Equal("b c [f] a paper", Docker(editor.Doc.Scene));
        editor.Undo();
        Assert.Equal("c [f] .b a paper", Docker(editor.Doc.Scene));
    }

    [Fact]
    public void AnEmptyFolderStaysPutWhenTheLayerAboveItIsDeletedOrMovedAway()
    {
        var doc = new Doc { Scene = Stack("paper", "a", "b", "c") };
        Folder(doc.Scene, "f", under: "b");
        var editor = new DocumentEditor(doc);
        Assert.Equal("c b [f] a paper", Docker(editor.Doc.Scene));

        editor.Perform(d => d.Scene.Layers.RemoveAll(l => l.Id == "b"));
        Assert.Equal("c [f] a paper", Docker(editor.Doc.Scene));

        editor.Perform(d =>
        {
            var c = L(d.Scene, "c");
            d.Scene.Layers.Remove(c);
            d.Scene.Layers.Insert(1, c);
        });
        // It sat on "a", and still does — "c" leaving from above it is not a
        // reason to follow "c" down.
        Assert.Equal("[f] a c paper", Docker(editor.Doc.Scene));
    }

    [Fact]
    public void AFolderThatGainsALayerForgetsItsSlot()
    {
        var doc = new Doc { Scene = Stack("paper", "a", "b") };
        Folder(doc.Scene, "f", under: "b");
        var editor = new DocumentEditor(doc);

        editor.Perform(d => L(d.Scene, "a").GroupId = "f");

        Assert.Null(editor.Doc.Scene.LayerGroups.Single().Under);
        Assert.Equal("b [f] .a paper", Docker(editor.Doc.Scene));
    }

    // ---- operations ------------------------------------------------------------------

    private static StackRef Ref(string id, bool folder = false) => new(id, folder);

    [Fact]
    public void NewFolderIsEmptyAndLandsDirectlyAboveTheActiveItem_InItsContainer()
    {
        var scene = Stack("paper", "a", "b", "c");
        Folder(scene, "holder");
        L(scene, "b").GroupId = "holder";
        L(scene, "a").GroupId = "holder";

        FolderTree.AddFolder(scene, new LayerGroup { Id = "n1" }, Ref("a"));
        Assert.Equal("c [holder] .b .[n1] .a paper", Docker(scene));

        FolderTree.AddFolder(scene, new LayerGroup { Id = "n2" }, Ref("b"));
        Assert.Equal("c [holder] .[n2] .b .[n1] .a paper", Docker(scene));

        FolderTree.AddFolder(scene, new LayerGroup { Id = "n3" }, Ref("holder", folder: true));
        Assert.Equal("c [n3] [holder] .[n2] .b .[n1] .a paper", Docker(scene));

        FolderTree.AddFolder(scene, new LayerGroup { Id = "n4" }, null);
        Assert.Equal("[n4] c [n3] [holder] .[n2] .b .[n1] .a paper", Docker(scene));

        FolderTree.AddFolder(scene, new LayerGroup { Id = "n5" }, Ref("n4", folder: true));
        Assert.Equal("[n5] [n4] c [n3] [holder] .[n2] .b .[n1] .a paper", Docker(scene));
        Assert.Equal(["paper", "a", "b", "c"], scene.Layers.Select(l => l.Id));
    }

    [Fact]
    public void GroupGathersAScatteredSelectionWhereTheTopmostWas()
    {
        var scene = Stack("paper", "a", "b", "c", "d");

        Assert.Null(FolderTree.Group(scene, new LayerGroup { Id = "g" }, [Ref("a"), Ref("c")]));

        Assert.Equal("d [g] .c .a b paper", Docker(scene));
    }

    [Fact]
    public void LayersAndFoldersMoveIntoOutOfAndBetweenFolders()
    {
        var scene = Stack("paper", "a", "b", "c");
        Folder(scene, "f");
        Folder(scene, "e", under: "a");

        Assert.Null(FolderTree.Move(scene, [Ref("c")], Ref("f", true), StackDrop.Into));
        Assert.Equal("[f] .c b a [e] paper", Docker(scene));

        Assert.Null(FolderTree.Move(scene, [Ref("e", true)], Ref("f", true), StackDrop.Into));
        Assert.Equal("[f] .[e] .c b a paper", Docker(scene));

        Assert.Null(FolderTree.Move(scene, [Ref("a")], Ref("e", true), StackDrop.Into));
        Assert.Equal("[f] .[e] ..a .c b paper", Docker(scene));

        Assert.Null(FolderTree.Move(scene, [Ref("e", true)], Ref("b"), StackDrop.Below));
        Assert.Equal("[f] .c b [e] .a paper", Docker(scene));
    }

    [Fact]
    public void AFolderCannotGoInsideItself_AndThePaperStaysAtTheBottom()
    {
        var scene = Stack("paper", "a");
        L(scene, "paper").IsBackground = true;
        Folder(scene, "outer");
        Folder(scene, "inner", parent: "outer");
        var before = FolderTree.Signature(scene);

        Assert.NotNull(FolderTree.Move(scene, [Ref("outer", true)], Ref("inner", true), StackDrop.Into));
        Assert.NotNull(FolderTree.Move(scene, [Ref("paper")], Ref("outer", true), StackDrop.Into));
        Assert.NotNull(FolderTree.Move(scene, [Ref("a")], Ref("paper"), StackDrop.Below));
        Assert.Equal(before, FolderTree.Signature(scene));
    }

    [Fact]
    public void UngroupLeavesTheContentsInTheFoldersPlace_DeleteTakesThemWithIt()
    {
        var scene = Stack("paper", "a", "b");
        Folder(scene, "outer");
        Folder(scene, "inner", parent: "outer");
        L(scene, "a").GroupId = "inner";
        Folder(scene, "spare", parent: "outer");

        FolderTree.Ungroup(scene, FolderTree.Folder(scene, "outer")!);
        Assert.Equal("b [spare] [inner] .a paper", Docker(scene));

        var gone = FolderTree.DeleteWithContents(scene, FolderTree.Folder(scene, "inner")!);
        Assert.Equal(["a"], gone.Select(l => l.Id));
        Assert.Equal("b [spare] paper", Docker(scene));
    }
}
