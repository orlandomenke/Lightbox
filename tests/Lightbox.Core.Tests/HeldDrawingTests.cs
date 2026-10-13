using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// A hold — one drawing standing in several cels — survives being saved,
/// reopened and copied (B467, Q260). An effect baked on 2s is stored that way.
/// The file writes the drawing out at each cel, and the reader and undo's copy
/// used to hand back one drawing per cel, all with the same id: the hold was
/// gone, and every lookup by id found the first of them.
/// </summary>
public class HeldDrawingTests
{
    private static Frame Drawing(double x = 50) =>
        new() { Strokes = [new Stroke { Points = [new StrokePoint(x, 50, 1), new StrokePoint(x + 30, 50, 1)] }] };

    /// <summary>A document whose one layer holds <paramref name="held"/> in its first two cels and something else in the third.</summary>
    private static Doc WithAHold(Frame held, Frame? third = null)
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer
        {
            Id = "layer-held",
            Cels = [new Cel { Frame = held }, new Cel { Frame = held }, new Cel { Frame = third ?? Drawing(200) }],
        });
        return doc;
    }

    private static Layer Held(Doc doc) => doc.Scene.Layers.Single(l => l.Id == "layer-held");

    [Fact]
    public void AHoldIsStillAHoldAfterASaveAndAReopen()
    {
        var doc = WithAHold(Drawing());

        var reopened = DocJson.Deserialize(DocJson.Serialize(doc));

        var cels = Held(reopened).Cels;
        Assert.Same(cels[0].Frame, cels[1].Frame);
        Assert.NotSame(cels[1].Frame, cels[2].Frame);
    }

    [Fact]
    public void AHoldIsStillAHoldThroughTheCompressedFileAndTheUndoSnapshot()
    {
        var doc = WithAHold(Drawing());
        var path = Path.Combine(Path.GetTempPath(), $"held-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(doc, path);
            var loaded = DocJson.Load(path);
            Assert.Same(Held(loaded).Cels[0].Frame, Held(loaded).Cels[1].Frame);
        }
        finally
        {
            File.Delete(path);
        }

        var fromSnapshot = DocJson.FromSnapshot(DocJson.ToSnapshot(doc));
        Assert.Same(Held(fromSnapshot).Cels[0].Frame, Held(fromSnapshot).Cels[1].Frame);
    }

    [Fact]
    public void ACopyOfTheDocumentKeepsAHoldAHold()
    {
        // Undo keeps a copy of the document and hands a copy back; a copy that
        // split the hold turned one drawing into twins on every undo.
        var doc = WithAHold(Drawing());

        var plain = doc.Clone();
        Assert.Same(Held(plain).Cels[0].Frame, Held(plain).Cels[1].Frame);
        Assert.NotSame(Held(doc).Cels[0].Frame, Held(plain).Cels[0].Frame);

        // Through a frame function too, which is how undo freezes a document.
        var frozen = doc.Clone(f => f.Clone());
        Assert.Same(Held(frozen).Cels[0].Frame, Held(frozen).Cels[1].Frame);
    }

    [Fact]
    public void TwoDrawingsThatOnlyShareAnIdAreKeptApartAndGivenTheirOwn()
    {
        // Only a bug or a hand-edited file makes two different drawings with
        // one id. Joining them would throw one away; they are kept, and the
        // second gets an id of its own so nothing can mistake it for the first.
        var first = Drawing(50);
        var second = Drawing(90);
        second.Id = first.Id;
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Id = "layer-held", Cels = [new Cel { Frame = first }, new Cel { Frame = second }] });

        var reopened = DocJson.Deserialize(DocJson.Serialize(doc));

        var cels = Held(reopened).Cels;
        Assert.NotSame(cels[0].Frame, cels[1].Frame);
        Assert.Equal(first.Id, cels[0].Frame!.Id);
        Assert.NotEqual(first.Id, cels[1].Frame!.Id);
        Assert.Equal(90, cels[1].Frame!.Strokes[0].Points[0].X);
    }

    [Fact]
    public void ADocumentWithNoHoldsComesBackExactlyAsItWent()
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = Drawing(10) }, new Cel(), new Cel { Frame = Drawing(70) }] });
        var json = DocJson.Serialize(doc);

        Assert.Equal(json, DocJson.Serialize(DocJson.Deserialize(json)));
    }
}
