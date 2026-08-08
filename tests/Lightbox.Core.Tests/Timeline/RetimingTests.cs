using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// Two separate re-timing operations, deliberately not one with a mode.
/// Stretching is what an animator means by "animating on 2s" and loses
/// nothing; reducing throws drawings away and keeps the length.
/// </summary>
public class RetimingTests
{
    private static (DocumentEditor Editor, Layer Layer) SceneOf(int drawings)
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var layer = doc.Scene.Layers[0];
        layer.Cels.Clear();
        for (var i = 0; i < drawings; i++)
        {
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes = [new Stroke { Label = $"d{i}", Points = [new StrokePoint(i, i, 1)] }],
                },
            });
        }
        doc.Scene.FrameCount = drawings;
        return (new DocumentEditor(doc), layer);
    }

    private static string Shape(Layer layer) =>
        string.Concat(layer.Cels.Select(c => c.Frame is null ? "-" : "K"));

    private static List<string> Drawings(Layer layer) =>
        layer.Cels.Where(c => c.Frame is not null)
            .Select(c => ((Frame)c.Frame!).Strokes[0].Label ?? "").ToList();

    [Fact]
    public void Stretch_HoldsEachDrawing_AndLosesNone()
    {
        var (editor, layer) = SceneOf(4);
        var grew = editor.StretchExposure(layer.Id, 0, 3, 2);

        Assert.Equal("K-K-K-K-", Shape(layer));
        Assert.Equal(4, grew);
        Assert.Equal(["d0", "d1", "d2", "d3"], Drawings(layer));
    }

    [Fact]
    public void Stretch_OnThrees_HoldsForThree()
    {
        var (editor, layer) = SceneOf(3);
        editor.StretchExposure(layer.Id, 0, 2, 3);
        Assert.Equal("K--K--K--", Shape(layer));
    }

    [Fact]
    public void StretchingTwice_DoesNotCompound()
    {
        // Holds already in the range are absorbed, so asking for 2s twice
        // still leaves you on 2s rather than 4s.
        var (editor, layer) = SceneOf(3);
        editor.StretchExposure(layer.Id, 0, 2, 2);
        var shapeOnce = Shape(layer);
        editor.StretchExposure(layer.Id, 0, layer.Cels.Count - 1, 2);

        Assert.Equal(shapeOnce, Shape(layer));
        Assert.Equal(3, Drawings(layer).Count);
    }

    [Fact]
    public void Reduce_KeepsEveryNth_AndKeepsTheLength()
    {
        var (editor, layer) = SceneOf(6);
        var before = layer.Cels.Count;
        var dropped = editor.ReduceToStep(layer.Id, 0, 5, 2);

        Assert.Equal(before, layer.Cels.Count);
        Assert.Equal("K-K-K-", Shape(layer));
        Assert.Equal(3, dropped);
        Assert.Equal(["d0", "d2", "d4"], Drawings(layer));
    }

    [Fact]
    public void Reduce_IsTheOneThatThrowsWork_Away()
    {
        // Stated plainly because the two commands are easy to confuse: this
        // one loses drawings and the other never does.
        var (stretchEditor, stretched) = SceneOf(6);
        stretchEditor.StretchExposure(stretched.Id, 0, 5, 2);
        Assert.Equal(6, Drawings(stretched).Count);

        var (reduceEditor, reduced) = SceneOf(6);
        reduceEditor.ReduceToStep(reduced.Id, 0, 5, 2);
        Assert.Equal(3, Drawings(reduced).Count);
    }

    [Fact]
    public void BothAreUndoable_AsOneStep()
    {
        var (editor, layer) = SceneOf(4);
        var original = Shape(layer);

        editor.StretchExposure(layer.Id, 0, 3, 2);
        Assert.NotEqual(original, Shape(editor.Doc.Scene.Layers[0]));
        editor.Undo();
        Assert.Equal(original, Shape(editor.Doc.Scene.Layers[0]));

        editor.ReduceToStep(editor.Doc.Scene.Layers[0].Id, 0, 3, 2);
        Assert.NotEqual(original, Shape(editor.Doc.Scene.Layers[0]));
        editor.Undo();
        Assert.Equal(original, Shape(editor.Doc.Scene.Layers[0]));
    }

    [Fact]
    public void AStepOfOneOrLess_IsANoOp()
    {
        var (editor, layer) = SceneOf(4);
        var original = Shape(layer);
        Assert.Equal(0, editor.StretchExposure(layer.Id, 0, 3, 1));
        Assert.Equal(0, editor.ReduceToStep(layer.Id, 0, 3, 1));
        Assert.Equal(original, Shape(editor.Doc.Scene.Layers[0]));
    }
}
