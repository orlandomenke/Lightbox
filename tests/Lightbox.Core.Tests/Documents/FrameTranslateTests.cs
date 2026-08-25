using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.Core.Tests.Documents;

/// <summary>
/// The whole-drawing move the spacing nudge rides (Q134). The interesting
/// promises are the coordinate walk's completeness — holes, seeds, path
/// nodes, anchors, placements and shapes all travel, or a nudge tears the
/// drawing — and the refusal: a pixel baseline cannot move, so nothing does.
/// </summary>
public class FrameTranslateTests
{
    private static Frame Loaded()
    {
        var frame = new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Points = [new StrokePoint(10, 20, 1), new StrokePoint(30, 40, 0.5)],
                    Holes = [[new StrokePoint(15, 25, 1)]],
                    RestPoints = [new StrokePoint(11, 21, 1)],
                    Path = new StrokePath { Nodes = [PathNode.At(10, 20)] },
                    Baked = new BakedSample { PngBase64 = "iVBORw0KGgo=", X = 5, Y = 6 },
                },
            ],
            Anchors = new() { ["a1"] = new AnchorPoint(50, 60) },
            Shapes = new() { ["s1"] = new ShapeBox(1, 2, 3, 4) },
            Placements = [new SymbolPlacement { SymbolId = "sym", X = 70, Y = 80 }],
        };
        return frame;
    }

    [Fact]
    public void EveryCoordinateOnTheDrawingMoves()
    {
        var frame = Loaded();

        Assert.True(FrameTranslate.Apply(frame, 100, -10));

        var stroke = frame.Strokes[0];
        Assert.Equal(110, stroke.Points[0].X);
        Assert.Equal(10, stroke.Points[0].Y);
        Assert.Equal(0.5, stroke.Points[1].Pressure);
        Assert.Equal(115, stroke.Holes![0][0].X);
        Assert.Equal(111, stroke.RestPoints![0].X);
        Assert.Equal(110, stroke.Path!.Nodes[0].X);
        Assert.Equal(105, stroke.Baked!.X);
        Assert.Equal(-4, stroke.Baked.Y);
        Assert.Equal(150, frame.Anchors!["a1"].X);
        Assert.Equal(50, frame.Anchors["a1"].Y);
        Assert.Equal(101, frame.Shapes!["s1"].X);
        Assert.Equal(-8, frame.Shapes["s1"].Y);
        Assert.Equal(170, frame.Placements![0].X);
        Assert.Equal(70, frame.Placements[0].Y);
    }

    [Fact]
    public void SizesDoNotChangeUnderAMove()
    {
        var frame = Loaded();
        var brushSize = frame.Strokes[0].Brush.Size;

        FrameTranslate.Apply(frame, 100, -10);

        Assert.Equal(brushSize, frame.Strokes[0].Brush.Size);
        Assert.Equal(3, frame.Shapes!["s1"].W);
        Assert.Equal(4, frame.Shapes["s1"].H);
    }

    [Fact]
    public void ADrawingWithAPixelBaselineIsRefusedWhole()
    {
        // Moving the strokes while a canvas-sized baseline stays put tears
        // the drawing in two; refusing everything is the honest outcome.
        var frame = Loaded();
        frame.PngBase64 = "iVBORw0KGgo=";

        Assert.False(FrameTranslate.Apply(frame, 100, -10));

        Assert.Equal(10, frame.Strokes[0].Points[0].X);
        Assert.Equal(50, frame.Anchors!["a1"].X);
    }

    [Fact]
    public void ABakedSampleAsksForWholePixels()
    {
        var frame = Loaded();

        Assert.True(FrameTranslate.HasBakedRaster(frame));
        frame.Strokes[0].Baked = null;
        Assert.False(FrameTranslate.HasBakedRaster(frame));
    }
}
