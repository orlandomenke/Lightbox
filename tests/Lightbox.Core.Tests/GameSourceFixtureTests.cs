using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The game-clip link record against committed files (W2, W8): a document opened
/// from a clip keeps every number write-back needs through a load and a save,
/// keys from a newer build survive (Q230), and an older document gains nothing.
/// </summary>
public class GameSourceFixtureTests
{
    private static Doc Load(string name) =>
        DocJson.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ACommittedClipDocumentLoadsWithEveryNumberWriteBackNeeds()
    {
        var doc = Load("game-source-v1.lightbox.json");
        var src = doc.GameSource!;

        Assert.Equal("unity", src.Engine);
        Assert.Equal("Assets/Hero/Run.anim", src.Clip);
        Assert.Equal((10, 12), (src.CanvasWidth, src.CanvasHeight));
        Assert.Equal((doc.Scene.Width, doc.Scene.Height), (src.CanvasWidth, src.CanvasHeight));

        var track = Assert.Single(src.Tracks);
        Assert.Equal(doc.Scene.Layers[0].Id, track.PlaceholderLayerId);
        Assert.Equal(doc.Scene.Layers[^1].Id, track.DrawingLayerId);

        var second = track.Frames[1];
        Assert.Equal((2, 2, 12L, "Hero_2"), (second.Start, second.Length, second.Sprite, second.Name));
        Assert.Equal([6, 0, 6, 8], second.Cell!);
        Assert.Equal([2, 2], second.CanvasOffset!);
        Assert.Equal([0.5, 0.0], second.Pivot!);
        Assert.Equal(64, second.SourceHash!.Length);
    }

    [Fact]
    public void ARecordSurvivesASaveAndKeepsWhatANewerBuildWrote()
    {
        var doc = Load("game-source-v1.lightbox.json");

        var json = DocJson.Serialize(doc);
        var again = DocJson.Serialize(DocJson.Deserialize(json));

        Assert.Equal(json, again);
        Assert.Contains("\"fromANewerBuild\"", json);
        Assert.Contains("\"alsoNewer\"", json);
    }

    [Fact]
    public void AnOlderDocumentLoadsWithoutARecordAndSavesWithoutTheKey()
    {
        var doc = Load("grouped-v2.lightbox.json");

        Assert.Null(doc.GameSource);
        Assert.DoesNotContain("gameSource", DocJson.Serialize(doc), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACloneOfTheRecordSharesNothingWithIt()
    {
        var doc = Load("game-source-v1.lightbox.json");
        var copy = doc.Clone();

        copy.GameSource!.Tracks[0].Frames[0].CanvasOffset![0] = 99;
        copy.GameSource.Tracks[0].Frames.Clear();

        Assert.Equal(3, doc.GameSource!.Tracks[0].Frames[0].CanvasOffset![0]);
        Assert.Equal(2, doc.GameSource.Tracks[0].Frames.Count);
    }
}
