using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Documents;

/// <summary>
/// Pinning rows to the Timeline and the X-sheet (Q227), Krita's way: with
/// <em>Pinned only</em> on, the sheet shows what is pinned, the layer being
/// drawn on, and the folders those sit in.
/// </summary>
public class SheetPinTests
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

    private static Layer L(Scene scene, string name) => scene.Layers.Single(l => l.Name == name);

    private static string Show(Scene scene, string? active = null) => string.Join(" ",
        FolderTree.SheetRows(scene, active is null ? null : L(scene, active)).Select(r => r.Item switch
        {
            LayerGroup g => $"[{g.Name}]",
            Layer l => l.Name,
            _ => "?",
        }));

    private const string Everything = "f [Outer] [Inner] e d c b a";

    [Fact]
    public void PinsDoNothingUntilTheSwitchIsOn()
    {
        var (scene, _, _) = Nested();
        L(scene, "c").SheetPinned = true;

        Assert.Equal(Everything, Show(scene, "a"));
    }

    /// <summary>The switch with nothing pinned must not empty the sheet.</summary>
    [Fact]
    public void TheSwitchWithNothingPinnedShowsEverything()
    {
        var (scene, _, _) = Nested();
        scene.SheetPinnedOnly = true;

        Assert.Equal(Everything, Show(scene, "a"));
    }

    [Fact]
    public void PinnedOnlyShowsThePinnedLayers_AndTheFoldersTheyAreIn()
    {
        var (scene, _, _) = Nested();
        scene.SheetPinnedOnly = true;
        L(scene, "f").SheetPinned = true;
        L(scene, "d").SheetPinned = true;

        // d is two folders down: both stay, so the tree is still a tree and
        // can still be folded. Their other layers do not.
        Assert.Equal("f [Outer] [Inner] d", Show(scene));
    }

    [Fact]
    public void TheLayerBeingDrawnOnIsAlwaysShown()
    {
        var (scene, _, _) = Nested();
        scene.SheetPinnedOnly = true;
        L(scene, "f").SheetPinned = true;

        Assert.Equal("f a", Show(scene, "a"));
        Assert.Equal("f [Outer] c", Show(scene, "c"));
        Assert.Equal("f", Show(scene, "f"));
    }

    [Fact]
    public void APinnedFolderBringsEverythingInsideIt()
    {
        var (scene, outer, inner) = Nested();
        scene.SheetPinnedOnly = true;

        inner.SheetPinned = true;
        Assert.Equal("[Outer] [Inner] e d", Show(scene));

        inner.SheetPinned = null;
        outer.SheetPinned = true;
        Assert.Equal("[Outer] [Inner] e d c b", Show(scene));
    }

    [Fact]
    public void AFoldedFolderStillFolds()
    {
        var (scene, outer, _) = Nested();
        scene.SheetPinnedOnly = true;
        outer.SheetPinned = true;
        outer.SheetCollapsed = true;

        Assert.Equal("[Outer]", Show(scene));
    }

    /// <summary>Optional means absent: nothing pinned and the switch off write no key.</summary>
    [Fact]
    public void ADocumentThatPinsNothingWritesNoKey_AndPinsRoundTrip()
    {
        var (scene, _, _) = Nested();
        var plain = DocJson.Serialize(new Doc { Scene = scene });
        Assert.DoesNotContain("sheetPinned", plain, StringComparison.OrdinalIgnoreCase);

        scene.SheetPinnedOnly = true;
        L(scene, "c").SheetPinned = true;
        scene.LayerGroups[1].SheetPinned = true;
        var json = DocJson.Serialize(new Doc { Scene = scene });
        using var parsed = JsonDocument.Parse(json);
        Assert.True(parsed.RootElement.GetProperty("scene").GetProperty("sheetPinnedOnly").GetBoolean());

        var restored = DocJson.Deserialize(json).Scene;
        Assert.True(restored.SheetPinnedOnly);
        Assert.True(restored.Layers.Single(l => l.Name == "c").SheetPinned);
        Assert.Null(restored.Layers.Single(l => l.Name == "b").SheetPinned);
        Assert.True(restored.LayerGroups[1].SheetPinned);
        Assert.Null(restored.LayerGroups[0].SheetPinned);
    }
}
