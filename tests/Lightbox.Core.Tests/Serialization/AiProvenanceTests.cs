using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// Q31: provenance is stored on the frame and <b>absent unless AI touched
/// it</b>. A document that never used the AI must be byte-identical to one
/// from before the feature existed — the key must be absent, not empty.
/// </summary>
public class AiProvenanceTests
{
    private static Doc DocWithOneStroke()
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Points = [new(10, 20, 0.5), new(30, 40, 0.8)],
        });
        return doc;
    }

    [Fact]
    public void ADocumentThatNeverUsedAiWritesNoAiKey()
    {
        var json = DocJson.Serialize(DocWithOneStroke());

        Assert.DoesNotContain("\"ai\"", json);
    }

    [Fact]
    public void ProvenanceRoundTrips()
    {
        var doc = DocWithOneStroke();
        doc.Scene.Layers[0].Cels[0].Frame!.Ai = new AiProvenance("Claude", "claude-sonnet-5");

        var restored = DocJson.Deserialize(DocJson.Serialize(doc));

        var ai = restored.Scene.Layers[0].Cels[0].Frame!.Ai;
        Assert.NotNull(ai);
        Assert.Equal("Claude", ai.Provider);
        Assert.Equal("claude-sonnet-5", ai.Model);
    }

    [Fact]
    public void ProvenanceWithoutAModelWritesNoModelKey()
    {
        // Optional has two halves: the block is absent when unused, and the
        // fields inside it are too. An MCP agent names no model.
        var doc = DocWithOneStroke();
        doc.Scene.Layers[0].Cels[0].Frame!.Ai = new AiProvenance("MCP agent");

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"ai\"", json);
        Assert.DoesNotContain("\"model\"", json);
    }

    [Fact]
    public void ProvenanceSurvivesTheUndoSnapshot()
    {
        // Frame.Clone is the undo path's deep copy; provenance restored by an
        // undo must still say the AI drew the frame.
        var frame = new Frame { Ai = new AiProvenance("Claude") };

        Assert.Equal("Claude", frame.Clone().Ai!.Provider);
    }
}
