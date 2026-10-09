using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Documents;

/// <summary>
/// A folder's colour is its own once chosen and its parent's until then
/// (Q226), and a folder that never chose one writes no key.
/// </summary>
public class FolderColourTests
{
    /// <summary>Outer > Inner > Innermost, with one layer in the innermost and one loose.</summary>
    private static (Scene Scene, LayerGroup Outer, LayerGroup Inner, LayerGroup Innermost) Nested()
    {
        var scene = new Scene();
        var outer = new LayerGroup { Id = "group-outer" };
        var inner = new LayerGroup { Id = "group-inner", ParentId = outer.Id };
        var innermost = new LayerGroup { Id = "group-innermost", ParentId = inner.Id };
        scene.LayerGroups.AddRange([outer, inner, innermost]);
        scene.Layers.Add(new Layer { Name = "Loose" });
        scene.Layers.Add(new Layer { Name = "Deep", GroupId = innermost.Id });
        return (scene, outer, inner, innermost);
    }

    [Fact]
    public void AFolderThatNeverChoseAColourShowsTheDefault()
    {
        var (scene, outer, _, innermost) = Nested();

        Assert.Equal(LayerGroup.DefaultColor, FolderTree.ColorOf(scene, outer));
        Assert.Equal(LayerGroup.DefaultColor, FolderTree.ColorOf(scene, innermost));
    }

    [Fact]
    public void ASubfolderShowsItsParentsColour_AtAnyDepth()
    {
        var (scene, outer, inner, innermost) = Nested();
        outer.Color = "#c25050";

        Assert.Equal("#c25050", FolderTree.ColorOf(scene, inner));
        Assert.Equal("#c25050", FolderTree.ColorOf(scene, innermost));
    }

    [Fact]
    public void AColourChosenOnASubfolderOverridesFromThereDown()
    {
        var (scene, outer, inner, innermost) = Nested();
        outer.Color = "#c25050";
        inner.Color = "#4a9a5e";

        Assert.Equal("#c25050", FolderTree.ColorOf(scene, outer));
        Assert.Equal("#4a9a5e", FolderTree.ColorOf(scene, inner));
        Assert.Equal("#4a9a5e", FolderTree.ColorOf(scene, innermost));
    }

    [Fact]
    public void ALayerTakesTheColourOfTheFolderItIsIn_AndALooseLayerHasNone()
    {
        var (scene, outer, _, _) = Nested();
        outer.Color = "#c25050";

        Assert.Null(FolderTree.ColorOf(scene, scene.Layers[0]));
        Assert.Equal("#c25050", FolderTree.ColorOf(scene, scene.Layers[1]));
    }

    /// <summary>A crafted file with a folder inside itself must not hang the docker.</summary>
    [Fact]
    public void AFolderLoopStillResolves()
    {
        var (scene, outer, inner, _) = Nested();
        outer.ParentId = inner.Id;

        Assert.Equal(LayerGroup.DefaultColor, FolderTree.ColorOf(scene, inner));
    }

    /// <summary>Optional means absent: the key appears when a colour is chosen and not before.</summary>
    [Fact]
    public void AFolderThatNeverChoseAColourWritesNoKey_AndAChosenOneRoundTrips()
    {
        var (scene, _, inner, _) = Nested();
        inner.Color = "#4a9a5e";
        var json = DocJson.Serialize(new Doc { Scene = scene });

        using var parsed = JsonDocument.Parse(json);
        var groups = parsed.RootElement.GetProperty("scene").GetProperty("layerGroups");
        Assert.False(groups[0].TryGetProperty("color", out _), "an unset colour was written");
        Assert.Equal("#4a9a5e", groups[1].GetProperty("color").GetString());

        var restored = DocJson.Deserialize(json).Scene.LayerGroups;
        Assert.Null(restored[0].Color);
        Assert.Equal("#4a9a5e", restored[1].Color);
    }

    /// <summary>A file from before this wrote every folder's colour, and still reads as it did.</summary>
    [Fact]
    public void AnOlderFilesExplicitColourIsKept()
    {
        var (scene, outer, _, _) = Nested();
        outer.Color = "#4a6ea9"; // what every folder used to be written with
        var json = DocJson.Serialize(new Doc { Scene = scene });

        Assert.Equal("#4a6ea9", DocJson.Deserialize(json).Scene.LayerGroups[0].Color);
    }
}
