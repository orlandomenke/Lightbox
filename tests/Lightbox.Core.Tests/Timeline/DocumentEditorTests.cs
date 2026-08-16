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
        ed.Doc.Scene.Layers.Add(new Layer { Kind = LayerKind.Vector, Cels = [new Cel { Frame = new Frame() }] });

        ed.AddFrameAfter(0);

        Assert.Equal(2, ed.Doc.Scene.FrameCount);
        Assert.All(ed.Doc.Scene.Layers, l => Assert.Equal(2, l.Cels.Count));
        Assert.NotNull(ed.Doc.Scene.Layers[0].Cels[1].Frame);
        Assert.IsType<Frame>(ed.Doc.Scene.Layers[0].Cels[1].Frame);
        Assert.IsType<Frame>(ed.Doc.Scene.Layers[1].Cels[1].Frame);
    }

    [Fact]
    public void DuplicateFrame_CopiesExposedContent()
    {
        var ed = NewEditor();
        var frame = (Frame)ed.Doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke { Points = [new(1, 1, 0.5)], Label = "x" });

        ed.DuplicateFrame(0);

        var copy = Assert.IsType<Frame>(ed.Doc.Scene.Layers[0].Cels[1].Frame);
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
    public void UndoStack_TrimsOldestBeyondLimit_KeepsNewestHistory()
    {
        var ed = NewEditor();

        // 70 distinct edits — beyond the 64-step undo cap.
        for (var i = 0; i < 70; i++)
        {
            var name = $"edit-{i}";
            ed.Perform(doc => doc.Scene.Layers[0].Name = name);
        }

        // Unwind everything the editor kept.
        var undone = 0;
        while (ed.CanUndo)
        {
            ed.Undo();
            undone++;
        }

        Assert.Equal(64, undone);
        // The oldest snapshots were trimmed, so we land on edit-5's state
        // (70 edits - 64 undos), not the pristine document.
        Assert.Equal("edit-5", ed.Doc.Scene.Layers[0].Name);

        // The trimmed history still redoes back to the newest state.
        while (ed.CanRedo) ed.Redo();
        Assert.Equal("edit-69", ed.Doc.Scene.Layers[0].Name);
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

        var tweens = new List<Frame> { new Frame(), new Frame() };
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

        var tweens = new List<Frame> { new Frame() };
        ed.InsertInbetweens(layer.Id, 0, tweens);

        Assert.Equal(3, ed.Doc.Scene.FrameCount);
        Assert.Same(tweens[0], layer.Cels[1].Frame);
        Assert.All(ed.Doc.Scene.Layers, l => Assert.Equal(3, l.Cels.Count));
    }

    // ---- discarding a step that changed nothing (B236) -----------------------

    /// <summary>
    /// The primitive B236 needed: a caller that pushed a step, did the work and
    /// then found the work came to nothing takes the step back as though it had
    /// never been pushed.
    /// </summary>
    [Fact]
    public void DiscardStep_RollsBackAndLeavesNoTrace()
    {
        var ed = NewEditor();
        var before = ed.Doc.Scene.FrameCount;

        var revision = ed.NextRevision;
        ed.Perform(d => d.Scene.Layers[0].Cels.Add(new Cel()), label: "Speculative");

        Assert.True(ed.DiscardStep(revision));
        Assert.Equal(before, ed.Doc.Scene.FrameCount);
        Assert.False(ed.CanUndo);
    }

    /// <summary>
    /// <b>Not an undo, and this is the assertion that says so.</b> An undo is a
    /// decision and leaves a redo behind; a discard is an admission that the
    /// step should not have existed, so redoing your way back into a state
    /// nobody authored must be impossible.
    /// </summary>
    [Fact]
    public void DiscardStep_LeavesNothingToRedo()
    {
        var ed = NewEditor();
        var revision = ed.NextRevision;
        ed.Perform(d => d.Scene.Layers[0].Cels.Add(new Cel()));

        ed.DiscardStep(revision);

        Assert.False(ed.CanRedo);
        var before = ed.Doc.Scene.Layers[0].Cels.Count;
        ed.Redo();
        Assert.Equal(before, ed.Doc.Scene.Layers[0].Cels.Count);
    }

    /// <summary>
    /// The guard that turns a race into a quiet no. Something else pushed a
    /// step in between, so the revision handed back is no longer on top —
    /// discarding "the last step" there would throw away somebody else's edit.
    /// </summary>
    [Fact]
    public void DiscardStep_RefusesWhenItIsNoLongerTheLastStep()
    {
        var ed = NewEditor();
        var before = ed.Doc.Scene.Layers[0].Cels.Count;
        var mine = ed.NextRevision;
        ed.Perform(d => d.Scene.Layers[0].Cels.Add(new Cel()));
        ed.Perform(d => d.Scene.Layers[0].Cels.Add(new Cel()));   // somebody else

        Assert.False(ed.DiscardStep(mine));
        // Both steps still stand: refusing is the safe answer, not a partial one.
        Assert.Equal(before + 2, ed.Doc.Scene.Layers[0].Cels.Count);
    }

    /// <summary>
    /// A discarded revision is never issued again, so a stale reference to it
    /// can never match a later step and discard the wrong thing.
    /// </summary>
    [Fact]
    public void DiscardStep_DoesNotHandTheRevisionOutTwice()
    {
        var ed = NewEditor();
        var first = ed.NextRevision;
        ed.Perform(d => d.Scene.Layers[0].Cels.Add(new Cel()));
        ed.DiscardStep(first);

        Assert.True(ed.NextRevision > first);
    }
}
