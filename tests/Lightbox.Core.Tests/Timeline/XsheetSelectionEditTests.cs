using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The editor half of the X-sheet's selection verbs (Q196): which selections
/// are columns, the column delete and insert, the row pull and insert across
/// several layers — each one undo step.
/// </summary>
public class XsheetSelectionEditTests
{
    /// <summary>Paper plus two drawing layers, every drawing cel keyed, <paramref name="frames"/> long.</summary>
    private static DocumentEditor Sheet(int frames)
    {
        var doc = DocumentFactory.CreateDoc(40, 10, 12, paperColor: "#ffffff");
        doc.Scene.Layers.Add(new Layer { Name = "Two", Kind = LayerKind.Painted, Cels = [new Cel { Frame = new Frame() }] });
        var editor = new DocumentEditor(doc);
        for (var i = 1; i < frames; i++) editor.AddFrameAfter(i - 1);
        return editor;
    }

    private static Layer Paper(DocumentEditor e) => e.Doc.Scene.Layers[0];

    private static Layer A(DocumentEditor e) => e.Doc.Scene.Layers[1];

    private static Layer B(DocumentEditor e) => e.Doc.Scene.Layers[2];

    private static List<string?> Row(Layer layer) => layer.Cels.Select(c => c.Frame?.Id).ToList();

    private static ReferenceStrip Strip(int cells)
    {
        var strip = new ReferenceStrip { SheetWidth = cells * 10, SheetHeight = 10 };
        for (var i = 0; i < cells; i++) strip.Cells.Add(new ReferenceCell { X = i * 10, Y = 0, Width = 10, Height = 10 });
        strip.LayOutFrom(0);
        return strip;
    }

    // ---- ColumnsOf ----------------------------------------------------------------

    [Fact]
    public void TheSameFramesOnEveryDrawingLayer_IsAColumnSelection_PaperOrNot()
    {
        var e = Sheet(4);
        IEnumerable<(string, int)> Pick(params (Layer L, int I)[] cels) => cels.Select(c => (c.L.Id, c.I));

        Assert.Equal([1, 3], DocumentEditor.ColumnsOf(e.Doc.Scene, Pick((A(e), 1), (A(e), 3), (B(e), 1), (B(e), 3))));
        // The paper's cels add nothing and take nothing away.
        Assert.Equal([1], DocumentEditor.ColumnsOf(e.Doc.Scene, Pick((A(e), 1), (B(e), 1), (Paper(e), 1), (Paper(e), 2))));
        // Different frames on the two rows, or one row only: not a column.
        Assert.Null(DocumentEditor.ColumnsOf(e.Doc.Scene, Pick((A(e), 1), (B(e), 2))));
        Assert.Null(DocumentEditor.ColumnsOf(e.Doc.Scene, Pick((A(e), 1))));
        // Past the end of the scene there is nothing to select.
        Assert.Null(DocumentEditor.ColumnsOf(e.Doc.Scene, Pick((A(e), 9), (B(e), 9))));
    }

    [Fact]
    public void DeleteCelsAcross_WithNothingReachable_RecordsNoUndoStep()
    {
        // An edit that removed nothing must not cost a Ctrl+Z that undoes nothing.
        var e = Sheet(3);
        var before = e.NextRevision;

        Assert.Equal(0, e.DeleteCelsAcross([("no-such-layer", 1), (A(e).Id, 99)]));

        Assert.Equal(before, e.NextRevision);
        Assert.Equal(3, A(e).Cels.Count);
    }

    // ---- DeleteColumns ------------------------------------------------------------

    [Fact]
    public void DeleteColumns_RemovesEveryLayersCels_RipplesReferences_InOneUndoStep()
    {
        var e = Sheet(5);
        var strip = Strip(5);
        e.Doc.Scene.References = [strip];
        var a = Row(A(e));
        var b = Row(B(e));

        var removed = e.DeleteColumns([1, 3]);

        Assert.Equal(2, removed);
        Assert.Equal(3, e.Doc.Scene.FrameCount);
        Assert.Equal([a[0], a[2], a[4]], Row(A(e)));
        Assert.Equal([b[0], b[2], b[4]], Row(B(e)));
        Assert.Equal([0, 20, 40], Enumerable.Range(0, 3).Select(f => e.Doc.Scene.References![0].CellAt(f)!.X));

        Assert.Equal("Delete frames", e.UndoLabel);
        e.Undo(); // once, for both columns
        Assert.Equal(5, e.Doc.Scene.FrameCount);
        Assert.Equal(a, Row(A(e)));
        Assert.Equal(b, Row(B(e)));
    }

    [Fact]
    public void DeleteColumns_NeverLeavesASceneWithNoFrames()
    {
        var e = Sheet(3);
        var a = Row(A(e));

        var removed = e.DeleteColumns([0, 1, 2]);

        Assert.Equal(2, removed);
        Assert.Equal(1, e.Doc.Scene.FrameCount);
        Assert.Equal([a[0]], Row(A(e))); // worked from the end, so the first stays

        Assert.Equal(0, e.DeleteColumns([0]));
        Assert.Equal(1, e.Doc.Scene.FrameCount);
    }

    [Fact]
    public void DeleteColumns_CarriesThePaperOntoTheFrameThatFollows()
    {
        var e = Sheet(3);
        var paper = Paper(e).Cels[0].Frame!.Id;

        e.DeleteColumns([0]);

        Assert.Equal(paper, Paper(e).Cels[0].Frame?.Id);
        Assert.All(Enumerable.Range(0, e.Doc.Scene.FrameCount),
            f => Assert.Equal(paper, ExposureSheet.ExposedFrame(Paper(e), f)?.Id));
    }

    [Fact]
    public void DeleteFrame_IsTheOneColumnCase_AndKeepsThePaperToo()
    {
        var e = Sheet(2);
        var paper = Paper(e).Cels[0].Frame!.Id;

        e.DeleteFrame(0);

        Assert.Equal(1, e.Doc.Scene.FrameCount);
        Assert.Equal(paper, Paper(e).Cels[0].Frame?.Id);
    }

    // ---- DeleteCelsAcross / ClearCelsAcross -----------------------------------------

    [Fact]
    public void DeleteCelsAcross_PullsEachRow_KeepsTheLength_InOneUndoStep()
    {
        var e = Sheet(4);
        var a = Row(A(e));
        var b = Row(B(e));

        e.DeleteCelsAcross([(A(e).Id, 1), (A(e).Id, 2), (B(e).Id, 0)]);

        Assert.Equal(4, e.Doc.Scene.FrameCount);
        Assert.Equal([a[0], a[3], null, null], Row(A(e)));
        Assert.Equal([b[1], b[2], b[3], null], Row(B(e)));

        e.Undo();
        Assert.Equal(a, Row(A(e)));
        Assert.Equal(b, Row(B(e)));
    }

    [Fact]
    public void ClearCelsAcross_MakesHolds_InOneUndoStep_AndRecordsNothingForHolds()
    {
        var e = Sheet(3);
        var a = Row(A(e));
        var b = Row(B(e));

        Assert.Equal(2, e.ClearCelsAcross([(A(e).Id, 1), (B(e).Id, 2)]));
        Assert.Null(A(e).Cels[1].Frame);
        Assert.Null(B(e).Cels[2].Frame);
        Assert.Equal(3, e.Doc.Scene.FrameCount);

        var label = e.UndoLabel;
        Assert.Equal(0, e.ClearCelsAcross([(A(e).Id, 1)])); // already a hold
        Assert.Equal(label, e.UndoLabel);

        e.Undo();
        Assert.Equal(a, Row(A(e)));
        Assert.Equal(b, Row(B(e)));
    }

    // ---- InsertHolds / InsertHoldColumns ----------------------------------------------

    [Fact]
    public void InsertHolds_PutsNHoldsAtTheStartOfEachRun_AndGrowsTheScene()
    {
        var e = Sheet(4);
        var a = Row(A(e));
        var b = Row(B(e));

        var inserted = e.InsertHolds([(A(e).Id, 1), (A(e).Id, 2), (B(e).Id, 3)]);

        Assert.Equal(3, inserted);
        // A: two holds at 1, so 1 and 2 now hold drawing 0 and the run moved right.
        Assert.Equal([a[0], null, null, a[1], a[2], a[3]], Row(A(e)));
        Assert.Equal(a[0], ExposureSheet.ExposedFrame(A(e), 2)?.Id);
        // B: one hold at 3, then padded to the scene's new length.
        Assert.Equal([b[0], b[1], b[2], null, b[3], null], Row(B(e)));
        Assert.Equal(6, e.Doc.Scene.FrameCount);

        e.Undo();
        Assert.Equal(4, e.Doc.Scene.FrameCount);
        Assert.Equal(a, Row(A(e)));
    }

    [Fact]
    public void InsertHolds_LeavesReferencesWhereTheyAre()
    {
        var e = Sheet(3);
        e.Doc.Scene.References = [Strip(3)];

        e.InsertHolds([(A(e).Id, 0)]);

        Assert.Equal(0, e.Doc.Scene.References![0].CellAt(0)!.X);
    }

    [Fact]
    public void InsertHoldColumns_PushesEveryLayer_RipplesReferences_AndThePaperHolds()
    {
        var e = Sheet(3);
        e.Doc.Scene.References = [Strip(3)];
        var paper = Paper(e).Cels[0].Frame!.Id;
        var a = Row(A(e));
        var b = Row(B(e));

        var inserted = e.InsertHoldColumns([0, 1]);

        Assert.Equal(2, inserted);
        Assert.Equal(5, e.Doc.Scene.FrameCount);
        Assert.Equal([null, null, a[0], a[1], a[2]], Row(A(e)));
        Assert.Equal([null, null, b[0], b[1], b[2]], Row(B(e)));
        Assert.All(Enumerable.Range(0, 5),
            f => Assert.Equal(paper, ExposureSheet.ExposedFrame(Paper(e), f)?.Id));
        // The references moved with the frames: what was on frame 0 is on 2.
        var strip = e.Doc.Scene.References![0];
        Assert.Null(strip.CellAt(0));
        Assert.Null(strip.CellAt(1));
        Assert.Equal(0, strip.CellAt(2)!.X);

        e.Undo();
        Assert.Equal(3, e.Doc.Scene.FrameCount);
        Assert.Equal(a, Row(A(e)));
        Assert.Equal(b, Row(B(e)));
    }
}
