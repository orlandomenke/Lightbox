using Lightbox.Core.Documents;

namespace Lightbox.Core.Tests;

/// <summary>
/// A pasted layer is a new layer: nothing it holds may answer to an id the
/// original already has, and nothing it holds may be the original's object.
/// </summary>
public class LayerCopyTests
{
    private static Layer Sample()
    {
        var layer = new Layer
        {
            Name = "Ink",
            LinkId = "link-1",
            SimId = "sim-1",
            IsBackground = true,
            BoneId = "bone-1",
            Opacity = 0.5,
            GroupId = "group-1",
            Mask = new LayerMask { Frame = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(1, 1, 1)] }] } },
            Cels =
            [
                new Cel { Frame = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(0, 0, 1), new StrokePoint(9, 9, 1)] }] } },
                new Cel(),
                new Cel { Frame = new Frame() },
            ],
        };
        return layer;
    }

    private static IEnumerable<string> Ids(Layer l)
    {
        yield return l.Id;
        foreach (var f in l.Cels.Select(c => c.Frame).Append(l.Mask?.Frame).Where(f => f is not null))
        {
            yield return f!.Id;
            foreach (var s in f.Strokes) yield return s.Id;
        }
    }

    [Fact]
    public void ACopyHasNoIdInCommonWithTheOriginal()
    {
        var source = Sample();

        var copy = LayerCopy.Duplicate(source, "Ink copy", 3, null);

        var shared = Ids(source).Intersect(Ids(copy)).ToList();
        Assert.Empty(shared);
    }

    [Fact]
    public void ACopyIsIndependentOfTheOriginal()
    {
        var source = Sample();
        var copy = LayerCopy.Duplicate(source, "Ink copy", 3, null);

        copy.Cels[0].Frame!.Strokes[0].Points.Clear();
        copy.Opacity = 1;

        Assert.Equal(2, source.Cels[0].Frame!.Strokes[0].Points.Count);
        Assert.Equal(0.5, source.Opacity);
        Assert.NotSame(source.Mask!.Frame, copy.Mask!.Frame);
    }

    [Fact]
    public void ACopyDoesNotJoinTheOriginalsLinkOrFluidGroupOrBecomeThePaper()
    {
        var copy = LayerCopy.Duplicate(Sample(), "Ink copy", 3, "group-2");

        Assert.Null(copy.LinkId);
        Assert.Null(copy.SimId);
        Assert.False(copy.IsBackground);
        Assert.Equal("group-2", copy.GroupId);
        // What describes the layer itself is kept, the rig included.
        Assert.Equal("bone-1", copy.BoneId);
        Assert.Equal(0.5, copy.Opacity);
        Assert.NotNull(copy.Mask);
    }

    [Fact]
    public void HoldsStayHoldsAndTheLayerIsFittedToTheScene()
    {
        var source = Sample();

        var longer = LayerCopy.Duplicate(source, "n", 5, null);
        var shorter = LayerCopy.Duplicate(source, "n", 2, null);

        Assert.Equal(5, longer.Cels.Count);
        Assert.Null(longer.Cels[1].Frame);
        Assert.Null(longer.Cels[4].Frame);
        Assert.Equal(2, shorter.Cels.Count);
    }

    [Fact]
    public void CopyNamesAreNumberedWhenTheyClash()
    {
        Assert.Equal("Ink copy", LayerCopy.CopyName("Ink", ["Ink", "Paper"]));
        Assert.Equal("Ink copy 2", LayerCopy.CopyName("Ink", ["Ink", "Ink copy"]));
        Assert.Equal("Ink copy 3", LayerCopy.CopyName("Ink", ["Ink copy", "Ink copy 2"]));
    }
}
