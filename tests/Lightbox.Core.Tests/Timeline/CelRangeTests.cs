using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests.Timeline;

public class CelRangeTests
{
    private static (DocumentEditor Editor, Layer Layer) OneLayerDoc()
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var editor = new DocumentEditor(doc);
        return (editor, doc.Scene.Layers[0]);
    }

    [Fact]
    public void MoveCel_MovesTheDrawing_AndClearsTheSource()
    {
        var (editor, layer) = OneLayerDoc();
        var frame = layer.Cels[0].Frame!;

        editor.MoveCel(layer.Id, 0, 4);

        layer = editor.Doc.Scene.Layers[0];
        Assert.Null(layer.Cels[0].Frame);
        Assert.Same(frame, layer.Cels[4].Frame);
        Assert.Equal(5, editor.Doc.Scene.FrameCount); // dropped past the end → extended
    }

    [Fact]
    public void MoveCel_WithCopy_KeepsTheSource_AndClonesWithAFreshId()
    {
        var (editor, layer) = OneLayerDoc();
        var srcId = layer.Cels[0].Frame!.Id;

        editor.MoveCel(layer.Id, 0, 2, copy: true);

        layer = editor.Doc.Scene.Layers[0];
        Assert.NotNull(layer.Cels[0].Frame);
        Assert.NotNull(layer.Cels[2].Frame);
        Assert.Equal(srcId, layer.Cels[0].Frame!.Id);
        Assert.NotEqual(srcId, layer.Cels[2].Frame!.Id);
    }

    [Fact]
    public void MoveCel_FromAHold_IsANoOp()
    {
        var (editor, layer) = OneLayerDoc();
        editor.SetKeyAt(layer.Id, 3, FrameRole.Key); // cels 1..2 are holds
        var before = editor.Doc.Scene.Layers[0].Cels.Select(c => c.Frame?.Id).ToList();

        editor.MoveCel(layer.Id, 1, 2);

        Assert.Equal(before, editor.Doc.Scene.Layers[0].Cels.Select(c => c.Frame?.Id).ToList());
    }

    [Fact]
    public void ClearCels_ClearsEveryDrawingInTheRange()
    {
        var (editor, layer) = OneLayerDoc();
        editor.SetKeyAt(layer.Id, 1, FrameRole.Key);
        editor.SetKeyAt(layer.Id, 2, FrameRole.Key);
        editor.SetKeyAt(layer.Id, 3, FrameRole.Key);

        editor.ClearCels(layer.Id, 1, 2);

        layer = editor.Doc.Scene.Layers[0];
        Assert.NotNull(layer.Cels[0].Frame);
        Assert.Null(layer.Cels[1].Frame);
        Assert.Null(layer.Cels[2].Frame);
        Assert.NotNull(layer.Cels[3].Frame);
    }

    [Fact]
    public void SetFrameRange_WritesTheSequence_HoldsIncluded()
    {
        var (editor, layer) = OneLayerDoc();
        var a = new PaintedFrame();
        var b = new PaintedFrame();

        editor.SetFrameRange(layer.Id, 2, [a, null, b]); // drawing, hold, drawing

        var scene = editor.Doc.Scene;
        Assert.Equal(5, scene.FrameCount);
        layer = scene.Layers[0];
        Assert.Same(a, layer.Cels[2].Frame);
        Assert.Null(layer.Cels[3].Frame);
        Assert.Same(b, layer.Cels[4].Frame);
    }

    [Fact]
    public void FrameMarkers_SurviveSerialization()
    {
        var doc = DocumentFactory.CreateDoc(32, 32, 12);
        doc.Scene.Markers.Add(new FrameMarker { Frame = 3, Label = "walk starts", Color = "#4caf50" });
        var restored = Lightbox.Core.Serialization.DocJson.Deserialize(
            Lightbox.Core.Serialization.DocJson.Serialize(doc));
        var marker = Assert.Single(restored.Scene.Markers);
        Assert.Equal(3, marker.Frame);
        Assert.Equal("walk starts", marker.Label);
        Assert.Equal("#4caf50", marker.Color);
    }
}
