using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests.Timeline;

public class DocumentEditorTests
{
    private static DocumentEditor NewEditor() => new(DocumentFactory.CreateDoc(100, 100, 12));

    [Fact]
    public void AddFrame_GrowsAllLayersAndFrameCount()
    {
        var ed = NewEditor();
        ed.Doc.Scene.Layers.Add(new Layer { Kind = LayerKind.Vector, Cels = [new Cel { Frame = new VectorFrame() }] });

        ed.AddFrameAfter(0);

        Assert.Equal(2, ed.Doc.Scene.FrameCount);
        Assert.All(ed.Doc.Scene.Layers, l => Assert.Equal(2, l.Cels.Count));
        Assert.NotNull(ed.Doc.Scene.Layers[0].Cels[1].Frame);
        Assert.IsType<PaintedFrame>(ed.Doc.Scene.Layers[0].Cels[1].Frame);
        Assert.IsType<VectorFrame>(ed.Doc.Scene.Layers[1].Cels[1].Frame);
    }

    [Fact]
    public void DuplicateFrame_CopiesExposedContent()
    {
        var ed = NewEditor();
        var frame = (PaintedFrame)ed.Doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke { Points = [new(1, 1, 0.5)], Label = "x" });

        ed.DuplicateFrame(0);

        var copy = Assert.IsType<PaintedFrame>(ed.Doc.Scene.Layers[0].Cels[1].Frame);
        Assert.NotSame(frame, copy);
        Assert.NotEqual(frame.Id, copy.Id);
        var s = Assert.Single(copy.Strokes);
        Assert.Equal("x", s.Label);
        Assert.NotEqual(frame.Strokes[0].Id, s.Id);
    }

    [Fact]
    public void DeleteFrame_RefusesLastFrame()
    {
        var ed = NewEditor();
        ed.DeleteFrame(0);
        Assert.Equal(1, ed.Doc.Scene.FrameCount);
    }

    [Fact]
    public void DeleteFrame_RemovesCelOnEveryLayer()
    {
        var ed = NewEditor();
        ed.AddFrameAfter(0);
        ed.AddFrameAfter(1);
        ed.DeleteFrame(1);
        Assert.Equal(2, ed.Doc.Scene.FrameCount);
        Assert.All(ed.Doc.Scene.Layers, l => Assert.Equal(2, l.Cels.Count));
    }

    [Fact]
    public void UndoRedo_RestoresState()
    {
        var ed = NewEditor();
        Assert.False(ed.CanUndo);

        ed.AddFrameAfter(0);
        Assert.True(ed.CanUndo);
        Assert.Equal(2, ed.Doc.Scene.FrameCount);

        ed.Undo();
        Assert.Equal(1, ed.Doc.Scene.FrameCount);
        Assert.True(ed.CanRedo);

        ed.Redo();
        Assert.Equal(2, ed.Doc.Scene.FrameCount);
    }

    [Fact]
    public void Perform_ClearsRedoStack()
    {
        var ed = NewEditor();
        ed.AddFrameAfter(0);
        ed.Undo();
        Assert.True(ed.CanRedo);
        ed.AddFrameAfter(0);
        Assert.False(ed.CanRedo);
    }

    [Fact]
    public void InsertInbetweens_ReplacesHoldCelsBetweenKeys()
    {
        var ed = NewEditor();
        var layer = ed.Doc.Scene.Layers[0];
        // Key at 0, holds at 1..2, key at 3.
        ed.AddFrameAfter(0);
        ed.AddFrameAfter(1);
        ed.AddFrameAfter(2);
        ed.Perform(doc =>
        {
            layer.Cels[1].Frame = null;
            layer.Cels[2].Frame = null;
        });

        var tweens = new List<Frame> { new PaintedFrame(), new PaintedFrame() };
        ed.InsertInbetweens(layer.Id, 0, tweens);

        Assert.Equal(4, ed.Doc.Scene.FrameCount);
        Assert.Same(tweens[0], layer.Cels[1].Frame);
        Assert.Same(tweens[1], layer.Cels[2].Frame);
    }

    [Fact]
    public void InsertInbetweens_NoGap_InsertsNewCels()
    {
        var ed = NewEditor();
        var layer = ed.Doc.Scene.Layers[0];
        ed.AddFrameAfter(0); // keys at 0 and 1, no gap

        var tweens = new List<Frame> { new PaintedFrame() };
        ed.InsertInbetweens(layer.Id, 0, tweens);

        Assert.Equal(3, ed.Doc.Scene.FrameCount);
        Assert.Same(tweens[0], layer.Cels[1].Frame);
        Assert.All(ed.Doc.Scene.Layers, l => Assert.Equal(3, l.Cels.Count));
    }
}
