using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B290 — a region-limited transform that catches an eraser stroke must not
/// resurrect the ink that eraser rubbed out. The marquee's majority test reads
/// geometry, so it picks up erasures like any line; carrying one away used to
/// reveal every stroke it had been holding down, as a ghost in the preview and
/// permanently on apply. The commit now leaves a stay copy behind wherever the
/// moved erasure was still erasing something that stays.
/// </summary>
public class TransformErasuresTests(ITestOutputHelper output)
{
    private static Stroke Line(double x0, double y0, double x1, double y1, ToolKind tool = ToolKind.Brush)
        => new()
        {
            Color = "#000000",
            Tool = tool,
            Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.15 },
            Points = [new(x0, y0, 1), new((x0 + x1) / 2, (y0 + y1) / 2, 1), new(x1, y1, 1)],
        };

    private static readonly TransformOps.PointMap MoveRight = (x, y) => (x + 200, y);

    [Fact]
    public void AMovedErasureLeavesACopyHoldingStayingInkDown()
    {
        var ink = Line(10, 50, 90, 50);
        var erasure = Line(40, 10, 40, 90, ToolKind.Eraser);
        var frame = new Frame { Strokes = [ink, erasure] };

        var moved = TransformErasures.TransformFrame(
            frame, MoveRight, 1, s => ReferenceEquals(s, erasure));

        Assert.Equal(1, moved);
        Assert.Equal(3, frame.Strokes.Count);
        // The copy sits directly beneath the moved original, at the original spot.
        Assert.Equal(ToolKind.Eraser, frame.Strokes[1].Tool);
        Assert.Equal(40, frame.Strokes[1].Points[0].X, 3);
        Assert.Equal(240, frame.Strokes[2].Points[0].X, 3);
        Assert.NotEqual(frame.Strokes[1].Id, frame.Strokes[2].Id);

        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 100);
        var crossing = bmp.GetPixel(40, 50).Alpha;
        var intact = bmp.GetPixel(15, 50).Alpha;
        output.WriteLine($"erased crossing {crossing}, intact ink {intact}");
        Assert.Equal(0, crossing);     // the ghost this bug was about
        Assert.True(intact > 200);     // the staying line is otherwise untouched
    }

    [Fact]
    public void AnErasureCarvingOnlyMovingInkLeavesNoStrayCopy()
    {
        // An artist carves a shape with the eraser and moves the whole thing:
        // the carving travels, and nothing stays behind — a copy that erases
        // nothing is exactly the stray Q102 exists to stop.
        var ink = Line(10, 50, 90, 50);
        var erasure = Line(40, 10, 40, 90, ToolKind.Eraser);
        var frame = new Frame { Strokes = [ink, erasure] };

        var moved = TransformErasures.TransformFrame(frame, MoveRight, 1, _ => true);

        Assert.Equal(2, moved);
        Assert.Equal(2, frame.Strokes.Count);

        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 100);
        var carved = bmp.GetPixel(240, 50).Alpha;
        var movedInk = bmp.GetPixel(215, 50).Alpha;
        output.WriteLine($"carved hole {carved}, moved ink {movedInk}");
        Assert.Equal(0, carved);          // the carving followed the shape
        Assert.True(movedInk > 200);      // and the shape itself arrived
        Assert.Equal(0, bmp.GetPixel(40, 50).Alpha);  // nothing left behind
        Assert.Equal(0, bmp.GetPixel(15, 50).Alpha);
    }

    [Fact]
    public void ABaselineFrameAlwaysKeepsTheCopy()
    {
        // Baseline pixels never move under a region-limited transform, and the
        // stroke record cannot say where their ink is — so the safe answer is
        // the only answer: the erasure's copy stays.
        using var paper = new SKBitmap(new SKImageInfo(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        paper.Erase(SKColors.Black);
        var erasure = Line(40, 10, 40, 90, ToolKind.Eraser);
        var frame = new Frame { PngBase64 = PngCodec.Encode(paper), Strokes = [erasure] };

        TransformErasures.TransformFrame(frame, MoveRight, 1, _ => true);

        Assert.Equal(2, frame.Strokes.Count);
        Assert.Equal(40, frame.Strokes[0].Points[0].X, 3);
        Assert.Equal(240, frame.Strokes[1].Points[0].X, 3);
    }

    [Fact]
    public void AClearedRegionGetsTheSameTreatment()
    {
        // ClearRegion is the area form of the same act (B233's grouping): a
        // moved clear leaves its copy exactly as a moved eraser path does.
        var ink = Line(10, 50, 90, 50);
        var clear = new Stroke
        {
            Tool = ToolKind.ClearRegion,
            Points = [new(30, 30, 1), new(60, 30, 1), new(60, 70, 1), new(30, 70, 1)],
        };
        var frame = new Frame { Strokes = [ink, clear] };

        TransformErasures.TransformFrame(frame, MoveRight, 1, s => ReferenceEquals(s, clear));

        Assert.Equal(3, frame.Strokes.Count);
        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 100);
        var cleared = bmp.GetPixel(45, 50).Alpha;
        var intact = bmp.GetPixel(15, 50).Alpha;
        output.WriteLine($"cleared {cleared}, intact ink {intact}");
        Assert.Equal(0, cleared);
        Assert.True(intact > 200);
    }
}
