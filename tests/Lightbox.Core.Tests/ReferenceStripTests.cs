using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// A reference laid against the timeline: which frame shows which cell, and
/// what happens to that mapping when the timing changes underneath it.
/// </summary>
public class ReferenceStripTests
{
    private static ReferenceStrip Strip(int cells = 4)
    {
        var strip = new ReferenceStrip { SheetWidth = 40, SheetHeight = 10 };
        for (var i = 0; i < cells; i++)
        {
            strip.Cells.Add(new ReferenceCell { X = i * 10, Y = 0, Width = 10, Height = 10 });
        }
        strip.LayOutFrom(0);
        return strip;
    }

    private static (DocumentEditor Editor, ReferenceStrip Strip) Doc(int frames = 4)
    {
        var doc = DocumentFactory.CreateDoc(40, 10, 12);
        doc.Scene.FrameCount = frames;
        var strip = Strip();
        doc.Scene.References = [strip];
        return (new DocumentEditor(doc), strip);
    }

    // ---- the mapping ------------------------------------------------------------

    [Fact]
    public void EachFrameShowsItsOwnCellByDefault()
    {
        var strip = Strip();

        Assert.Equal(0, strip.CellAt(0)!.X);
        Assert.Equal(30, strip.CellAt(3)!.X);
    }

    [Fact]
    public void PastTheEndOfTheReferenceThereIsNothing()
    {
        // Deliberately not a hold. A held reference on a frame you have just
        // added is a drawing you have already done, presented as one you have
        // not — the animator would draw the same pose twice.
        var strip = Strip();

        Assert.Null(strip.CellAt(4));
        Assert.Null(strip.CellAt(-1));
    }

    [Fact]
    public void AReferenceCanStartLaterThanTheFirstFrame()
    {
        var strip = Strip();
        strip.LayOutFrom(2);

        Assert.Null(strip.CellAt(0));
        Assert.Null(strip.CellAt(1));
        Assert.Equal(0, strip.CellAt(2)!.X);
        Assert.Equal(30, strip.CellAt(5)!.X);
    }

    [Fact]
    public void AnyCellCanBeAssignedToAnyFrame()
    {
        // Retiming by hand: hold the reference's first pose for three frames.
        var strip = Strip();
        strip.Assign(1, 0);
        strip.Assign(2, 0);

        Assert.Equal(0, strip.CellAt(1)!.X);
        Assert.Equal(0, strip.CellAt(2)!.X);
        Assert.Equal(30, strip.CellAt(3)!.X);
    }

    [Fact]
    public void AssigningPastTheEndFillsTheGapWithNothing()
    {
        var strip = Strip();
        strip.Assign(6, 2);

        Assert.Null(strip.CellAt(5));
        Assert.Equal(20, strip.CellAt(6)!.X);
    }

    [Fact]
    public void CentringPutsTheFirstCellInTheMiddleOfTheCanvas()
    {
        var strip = Strip();
        strip.Scale = 2;
        strip.CentreOn(100, 50);

        Assert.Equal(40, strip.OffsetX, 5); // (100 - 10*2) / 2
        Assert.Equal(15, strip.OffsetY, 5);
    }

    // ---- following the timeline -----------------------------------------------

    [Fact]
    public void InsertingAFrameMovesTheLaterReferencesAlong()
    {
        // The ordinary case: you add a frame to draw an inbetween, so the
        // reference for the next extreme belongs after it.
        var (editor, strip) = Doc();

        editor.AddFrameAfter(0); // new frame at index 1

        Assert.Equal(0, strip.CellAt(0)!.X);
        Assert.Null(strip.CellAt(1));
        Assert.Equal(10, strip.CellAt(2)!.X);
        Assert.Equal(30, strip.CellAt(4)!.X);
    }

    [Fact]
    public void DuplicatingAFrameMovesTheReferenceToo()
    {
        var (editor, strip) = Doc();

        editor.DuplicateFrame(0);

        Assert.Null(strip.CellAt(1));
        Assert.Equal(10, strip.CellAt(2)!.X);
    }

    [Fact]
    public void DeletingAFrameClosesTheGapInTheReference()
    {
        var (editor, strip) = Doc();

        editor.DeleteFrame(1);

        Assert.Equal(0, strip.CellAt(0)!.X);
        Assert.Equal(20, strip.CellAt(1)!.X);
        Assert.Equal(30, strip.CellAt(2)!.X);
    }

    [Fact]
    public void InsertedInbetweensPushTheReferenceAlong()
    {
        // The headline feature meets the reference: three inbetweens between
        // two extremes must not leave the second extreme's reference sitting
        // on an inbetween.
        var doc = DocumentFactory.CreateDoc(40, 10, 12);
        doc.Scene.FrameCount = 2;
        var strip = Strip(2);
        doc.Scene.References = [strip];
        var editor = new DocumentEditor(doc);
        var layer = doc.Scene.Layers[0];
        editor.SetKeyAt(layer.Id, 0, FrameRole.Key);
        editor.SetKeyAt(layer.Id, 1, FrameRole.Key);

        editor.InsertInbetweens(layer.Id, 0, [new PaintedFrame(), new PaintedFrame(), new PaintedFrame()]);

        Assert.Equal(0, strip.CellAt(0)!.X);
        Assert.Null(strip.CellAt(1));
        Assert.Null(strip.CellAt(2));
        Assert.Null(strip.CellAt(3));
        Assert.Equal(10, strip.CellAt(4)!.X);
    }

    [Fact]
    public void AStripPinnedToAbsoluteTimingStaysWhereItIs()
    {
        // Matching a shot frame for frame, or a sheet cut to a soundtrack.
        // Here the reference must NOT move when the drawing timing changes.
        var (editor, strip) = Doc();
        strip.FollowsTimeline = false;

        editor.AddFrameAfter(0);

        Assert.Equal(10, strip.CellAt(1)!.X);
        Assert.Equal(30, strip.CellAt(3)!.X);
    }

    [Fact]
    public void UndoingAFrameInsertPutsTheReferenceBack()
    {
        var (editor, strip) = Doc();

        editor.AddFrameAfter(0);
        editor.Undo();

        // Undo restores the whole document by snapshot, so the strip the test
        // holds is stale — read the one the editor has.
        var restored = editor.Doc.Scene.References![0];
        Assert.Equal(10, restored.CellAt(1)!.X);
        Assert.Equal(4, restored.Slots.Count);
        Assert.NotSame(strip, restored);
    }

    // ---- absence ------------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoReferenceWritesNoKeyForOne()
    {
        // The camera's rule again. A document that never imports a reference
        // must serialize exactly as it did before the feature existed.
        var doc = DocumentFactory.CreateDoc(40, 10, 12);

        Assert.Null(doc.Scene.References);
        Assert.False(doc.Scene.HasReferences);
        Assert.DoesNotContain("references", DocJson.Serialize(doc));
    }

    [Fact]
    public void EditingTheTimelineOfADocumentWithNoReferenceIsUntouched()
    {
        var doc = DocumentFactory.CreateDoc(40, 10, 12);
        var editor = new DocumentEditor(doc);

        editor.AddFrameAfter(0);
        editor.DeleteFrame(1);

        Assert.Null(editor.Doc.Scene.References);
    }

    [Fact]
    public void AReferenceRoundTripsThroughJson()
    {
        var doc = DocumentFactory.CreateDoc(40, 10, 12);
        var strip = Strip();
        strip.Name = "Run cycle";
        strip.Png = "iVBORw0KGgo=";
        strip.Scale = 1.5;
        strip.Opacity = 0.4;
        strip.OffsetX = 12;
        strip.OffsetY = -3;
        strip.FollowsTimeline = false;
        strip.Cells[2].Dx = 4;
        strip.Cells[2].Dy = -7;
        doc.Scene.References = [strip];

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Scene.References![0];

        Assert.Equal("Run cycle", back.Name);
        Assert.Equal("iVBORw0KGgo=", back.Png);
        Assert.Equal(1.5, back.Scale, 5);
        Assert.Equal(0.4, back.Opacity, 5);
        Assert.Equal(12, back.OffsetX, 5);
        Assert.Equal(-3, back.OffsetY, 5);
        Assert.False(back.FollowsTimeline);
        Assert.Equal(4, back.Cells[2].Dx, 5);
        Assert.Equal(-7, back.Cells[2].Dy, 5);
        Assert.Equal([0, 1, 2, 3], back.Slots);
    }
}
