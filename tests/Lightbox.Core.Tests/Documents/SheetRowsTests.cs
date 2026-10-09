using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Documents;

/// <summary>
/// The rows the Timeline and the X-sheet show (Q227): the Layers docker's
/// tree, folded by a state of its own.
/// </summary>
public class SheetRowsTests
{
    /// <summary>Top to bottom: f, [Outer: [Inner: e, d], c, b], a.</summary>
    private static (Scene Scene, LayerGroup Outer, LayerGroup Inner) Nested()
    {
        var scene = new Scene();
        var outer = new LayerGroup { Id = "group-outer", Name = "Outer" };
        var inner = new LayerGroup { Id = "group-inner", Name = "Inner", ParentId = outer.Id };
        scene.LayerGroups.AddRange([outer, inner]);
        scene.Layers.Add(new Layer { Name = "a" });
        scene.Layers.Add(new Layer { Name = "b", GroupId = outer.Id });
        scene.Layers.Add(new Layer { Name = "c", GroupId = outer.Id });
        scene.Layers.Add(new Layer { Name = "d", GroupId = inner.Id });
        scene.Layers.Add(new Layer { Name = "e", GroupId = inner.Id });
        scene.Layers.Add(new Layer { Name = "f" });
        return (scene, outer, inner);
    }

    private static string Show(IEnumerable<StackRow> rows) => string.Join(" ", rows.Select(r => r.Item switch
    {
        LayerGroup g => $"[{g.Name}]",
        Layer l => l.Name,
        _ => "?",
    }));

    [Fact]
    public void WithNothingFoldedTheSheetShowsTheWholeTree()
    {
        var (scene, _, _) = Nested();

        Assert.Equal("f [Outer] [Inner] e d c b a", Show(FolderTree.SheetRows(scene)));
    }

    [Fact]
    public void AFolderFoldedOnTheSheetKeepsItsRowAndLosesEverythingInside()
    {
        var (scene, outer, inner) = Nested();

        inner.SheetCollapsed = true;
        Assert.Equal("f [Outer] [Inner] c b a", Show(FolderTree.SheetRows(scene)));

        outer.SheetCollapsed = true;
        Assert.Equal("f [Outer] a", Show(FolderTree.SheetRows(scene)));
    }

    /// <summary>Two states, two surfaces: neither folds the other.</summary>
    [Fact]
    public void TheSheetAndTheLayersDockerFoldSeparately()
    {
        var (scene, outer, _) = Nested();

        outer.Collapsed = true; // the Layers docker's
        Assert.Equal("f [Outer] [Inner] e d c b a", Show(FolderTree.SheetRows(scene)));
        Assert.Equal("f [Outer] a", Show(FolderTree.Rows(scene).Where(r => !r.Hidden)));

        outer.Collapsed = false;
        outer.SheetCollapsed = true; // the sheet's
        Assert.Equal("f [Outer] a", Show(FolderTree.SheetRows(scene)));
        Assert.Equal("f [Outer] [Inner] e d c b a", Show(FolderTree.Rows(scene).Where(r => !r.Hidden)));
    }

    [Fact]
    public void ARowKnowsHowDeepItIs()
    {
        var (scene, _, _) = Nested();

        var depths = FolderTree.SheetRows(scene).Select(r => r.Depth).ToList();

        Assert.Equal([0, 0, 1, 2, 2, 1, 1, 0], depths);
    }

    /// <summary>Optional means absent: a folder never folded on the sheet writes no key.</summary>
    [Fact]
    public void AFolderNeverFoldedOnTheSheetWritesNoKey_AndAFoldedOneRoundTrips()
    {
        var (scene, _, inner) = Nested();
        inner.SheetCollapsed = true;
        var json = DocJson.Serialize(new Doc { Scene = scene });

        using var parsed = JsonDocument.Parse(json);
        var groups = parsed.RootElement.GetProperty("scene").GetProperty("layerGroups");
        Assert.False(groups[0].TryGetProperty("sheetCollapsed", out _), "an unfolded folder wrote the key");
        Assert.True(groups[1].GetProperty("sheetCollapsed").GetBoolean());

        var restored = DocJson.Deserialize(json).Scene.LayerGroups;
        Assert.Null(restored[0].SheetCollapsed);
        Assert.True(restored[1].SheetCollapsed);
    }
}
