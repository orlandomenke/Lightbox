using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests.Timeline;

public class ExposureTests
{
    private static Layer LayerWith(params bool[] keyed)
    {
        var layer = new Layer { Kind = LayerKind.Vector };
        foreach (var k in keyed)
            layer.Cels.Add(new Cel { Frame = k ? new VectorFrame() : null });
        return layer;
    }

    [Fact]
    public void ExposedFrame_WalksHoldsBackward()
    {
        var layer = LayerWith(true, false, false, true, false);
        Assert.Same(layer.Cels[0].Frame, ExposureSheet.ExposedFrame(layer, 0));
        Assert.Same(layer.Cels[0].Frame, ExposureSheet.ExposedFrame(layer, 1));
        Assert.Same(layer.Cels[0].Frame, ExposureSheet.ExposedFrame(layer, 2));
        Assert.Same(layer.Cels[3].Frame, ExposureSheet.ExposedFrame(layer, 3));
        Assert.Same(layer.Cels[3].Frame, ExposureSheet.ExposedFrame(layer, 4));
    }

    [Fact]
    public void ExposedFrame_PastEnd_UsesLastKey()
    {
        var layer = LayerWith(true, false);
        Assert.Same(layer.Cels[0].Frame, ExposureSheet.ExposedFrame(layer, 99));
    }

    [Fact]
    public void ExposedFrame_NothingKeyed_ReturnsNull()
    {
        var layer = LayerWith(false, false);
        Assert.Null(ExposureSheet.ExposedFrame(layer, 1));
    }

    [Fact]
    public void FrameAtExactIndex_IgnoresHolds()
    {
        var layer = LayerWith(true, false, true);
        Assert.NotNull(ExposureSheet.FrameAtExactIndex(layer, 0));
        Assert.Null(ExposureSheet.FrameAtExactIndex(layer, 1));
        Assert.NotNull(ExposureSheet.FrameAtExactIndex(layer, 2));
        Assert.Null(ExposureSheet.FrameAtExactIndex(layer, -1));
        Assert.Null(ExposureSheet.FrameAtExactIndex(layer, 3));
    }

    [Fact]
    public void NextKeyIndex_FindsStrictlyAfter()
    {
        var layer = LayerWith(true, false, true, false);
        Assert.Equal(2, ExposureSheet.NextKeyIndex(layer, 0));
        Assert.Equal(2, ExposureSheet.NextKeyIndex(layer, 1));
        Assert.Equal(-1, ExposureSheet.NextKeyIndex(layer, 2));
    }

    [Fact]
    public void KeyIndexAtOrBefore_Works()
    {
        var layer = LayerWith(true, false, true, false);
        Assert.Equal(0, ExposureSheet.KeyIndexAtOrBefore(layer, 1));
        Assert.Equal(2, ExposureSheet.KeyIndexAtOrBefore(layer, 3));
        Assert.Equal(2, ExposureSheet.KeyIndexAtOrBefore(layer, 2));
    }
}
