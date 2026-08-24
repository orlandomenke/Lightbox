using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The layer mask and clipping records (Q147, Q148): a mask is a stroke
/// record like everything else, a clip is a flag, and a document that uses
/// neither serializes exactly as it did before either existed.
/// </summary>
public class LayerMaskRecordTests
{
    private static Doc NewDoc() => DocumentFactory.CreateDoc(64, 48, 12);

    private static Layer ArtLayer(Doc doc) =>
        doc.Scene.Layers.First(l => !l.IsBackground);

    [Fact]
    public void ADocumentThatNeverMasksOrClipsWritesNoKeys()
    {
        var json = DocJson.Serialize(NewDoc());
        Assert.DoesNotContain("\"mask\"", json);
        Assert.DoesNotContain("\"clipToBelow\"", json);
    }

    [Fact]
    public void AFreshMaskWritesOnlyItsDrawing()
    {
        // The camera's rule applies inside the block too: Disabled and
        // Inverted are answers nobody has given yet, so a mask an artist just
        // added carries a frame and nothing else.
        var doc = NewDoc();
        ArtLayer(doc).Mask = new LayerMask();

        var json = DocJson.Serialize(doc);
        Assert.Contains("\"mask\"", json);
        Assert.DoesNotContain("\"disabled\"", json);
        Assert.DoesNotContain("\"inverted\"", json);
    }

    [Fact]
    public void AMaskedStrokeSurvivesTheRoundTrip()
    {
        var doc = NewDoc();
        var mask = new LayerMask();
        mask.Frame.Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(5, 6, 0.5), new StrokePoint(20, 6, 0.9)],
        });
        ArtLayer(doc).Mask = mask;
        ArtLayer(doc).ClipToBelow = true;

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var layer = ArtLayer(back);

        Assert.True(layer.IsClipped);
        Assert.NotNull(layer.Mask);
        var stroke = Assert.Single(layer.Mask!.Frame.Strokes);
        Assert.Equal(2, stroke.Points.Count);
        Assert.Equal(20, stroke.Points[1].X);
    }

    [Fact]
    public void DisabledAndInvertedRoundTripOnceAuthored()
    {
        var doc = NewDoc();
        ArtLayer(doc).Mask = new LayerMask { Disabled = true, Inverted = true };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var mask = ArtLayer(back).Mask!;

        Assert.False(mask.Applies);
        Assert.True(mask.IsInverted);
    }

    [Fact]
    public void ADisabledMaskDoesNotCountAsMasking()
    {
        var layer = new Layer { Mask = new LayerMask { Disabled = true } };
        Assert.False(layer.IsMasked);

        layer.Mask.Disabled = null;
        Assert.True(layer.IsMasked);
    }

    [Fact]
    public void CloningALayerCopiesTheMaskRatherThanSharingIt()
    {
        // The undo path restores from clones, so a shared mask frame would
        // make undo mutate the snapshot it is meant to restore from.
        var layer = new Layer { Mask = new LayerMask() };
        layer.Mask.Frame.Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(1, 2, 0.5)],
        });

        var copy = layer.Clone();
        copy.Mask!.Frame.Strokes.Clear();

        Assert.Single(layer.Mask.Frame.Strokes);
    }
}
