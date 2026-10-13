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
    public void ResizingAReopenedHoldScalesItOnce()
    {
        // The sensitivity review's block: with holds now surviving a reopen,
        // a resize that scaled a drawing once per cel would shrink every saved
        // hold twice over. B463 is the fix this branch stands on; this is the
        // case that makes it matter.
        var reopened = DocJson.Deserialize(DocJson.Serialize(WithAHold(Drawing(50))));
        Assert.Same(Held(reopened).Cels[0].Frame, Held(reopened).Cels[1].Frame);

        ImageResize.Apply(reopened, reopened.Scene.Width * 2, reopened.Scene.Height * 2, new NoPixels());

        Assert.Equal(100, Held(reopened).Cels[0].Frame!.Strokes[0].Points[0].X);
    }

    private sealed class NoPixels : IPixelResampler
    {
        public string? Resample(string pngBase64, double scaleX, double scaleY) => pngBase64;
    }

    [Fact]
    public void TheSameDrawingOnTwoLayersIsTwoDrawings()
    {
        // A hold is one layer's cels. Two layers carrying an identical drawing
        // under one id — which nothing in the app makes — are kept apart:
        // joined, drawing on one layer would draw on the other.
        var drawing = Drawing(50);
        var copy = Drawing(50);
        copy.Id = drawing.Id;
        copy.Strokes[0].Id = drawing.Strokes[0].Id;
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Id = "a", Cels = [new Cel { Frame = drawing }] });
        doc.Scene.Layers.Add(new Layer { Id = "b", Cels = [new Cel { Frame = copy }] });

        var reopened = DocJson.Deserialize(DocJson.Serialize(doc));

        var (onA, onB) = (reopened.Scene.Layers.Single(l => l.Id == "a").Cels[0].Frame!, reopened.Scene.Layers.Single(l => l.Id == "b").Cels[0].Frame!);
        Assert.NotSame(onA, onB);
        Assert.NotEqual(onA.Id, onB.Id);
    }

    [Fact]
    public void AMaskIsNeverJoinedToADrawing()
    {
        var drawing = Drawing(50);
        var mask = Drawing(50);
        mask.Id = drawing.Id;
        mask.Strokes[0].Id = drawing.Strokes[0].Id;
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Id = "layer-held", Cels = [new Cel { Frame = drawing }], Mask = new LayerMask { Frame = mask } });

        var reopened = DocJson.Deserialize(DocJson.Serialize(doc));

        var layer = Held(reopened);
        Assert.NotSame(layer.Cels[0].Frame, layer.Mask!.Frame);
        Assert.NotEqual(layer.Cels[0].Frame!.Id, layer.Mask.Frame.Id);
    }

    [Fact]
    public void AHoldThatCollidedWithAnotherDrawingIsStillJoinedToItself()
    {
        // First a drawing with id x, then a hold of a different drawing that
        // also says x. The hold is given a new id — and both of its cels must
        // get the same one, or the hold is split after all.
        var first = Drawing(10);
        var held = Drawing(90);
        held.Id = first.Id;
        var heldAgain = Drawing(90);
        heldAgain.Id = first.Id;
        heldAgain.Strokes[0].Id = held.Strokes[0].Id;
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer
        {
            Id = "layer-held",
            Cels = [new Cel { Frame = first }, new Cel { Frame = held }, new Cel { Frame = heldAgain }],
        });

        var reopened = DocJson.Deserialize(DocJson.Serialize(doc));

        var cels = Held(reopened).Cels;
        Assert.NotEqual(cels[0].Frame!.Id, cels[1].Frame!.Id);
        Assert.Same(cels[1].Frame, cels[2].Frame);
    }

    [Fact]
    public void AFileWithAnEmptyCelOrAMaskWithNoDrawingStillOpens()
    {
        // Odd input, not a crash: a file can say "cels": [null] or give a mask
        // no drawing, and joining must not be what falls over on it.
        var json = DocJson.Serialize(WithAHold(Drawing()));
        var odd = json.Replace("\"cels\": [", "\"cels\": [null, ", StringComparison.Ordinal);
        Assert.NotEqual(json, odd);

        var doc = DocJson.Deserialize(odd);
        Assert.NotNull(doc);

        var masked = new Doc();
        masked.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = Drawing() }], Mask = new LayerMask { Frame = null! } });
        _ = HeldDrawings.Relink(masked);
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
