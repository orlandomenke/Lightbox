using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B379: an image resize must move the axis a stroke carries along with the
/// stroke, or the reflected copy re-renders about the old centre.
/// </summary>
/// <remarks>
/// Rendered rather than read back as numbers, as the ledger entry asks: the
/// defect is a copy landing in the wrong place on the page, so the test asks
/// the page. <c>ImageResize</c> scaled points, holes, rest points, brush size
/// and baked pixels and left <c>Stroke.Symmetry</c> alone; the scene-level
/// accounting test caught the scene's axis and nothing guarded the stroke's.
/// </remarks>
public class SymmetryResizeTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 140;

    /// <summary>
    /// Two mirrored marks: one across a vertical axis, one across a horizontal
    /// one, so a resize that scales only X, only Y, or swaps the two factors
    /// shows up on the page rather than only in a number.
    /// </summary>
    private static Doc DocWithMirroredMarks()
    {
        var doc = new Doc();
        doc.Scene.Width = W;
        doc.Scene.Height = H;
        var brush = new BrushSettings { Size = 14, Hardness = 0.9, Opacity = 1, Flow = 1, Spacing = 0.2 };
        var vertical = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#1b3f7a",
            Brush = brush.Clone(),
            Points = [new StrokePoint(40, 70, 0.9), new StrokePoint(60, 70, 0.9)],
            Symmetry = new SymmetryAxis { CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 90, Order = 1, Mirror = true },
        };
        var horizontal = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#7a1b1b",
            Brush = brush.Clone(),
            Points = [new StrokePoint(100, 30, 0.9), new StrokePoint(120, 30, 0.9)],
            Symmetry = new SymmetryAxis { CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 0, Order = 1, Mirror = true },
        };
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = new Frame { Strokes = [vertical, horizontal] } }] });
        return doc;
    }

    private static int InkedIn(SKBitmap b, SKRectI box)
    {
        var n = 0;
        for (var y = Math.Max(0, box.Top); y < Math.Min(b.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(b.Width, box.Right); x++)
            {
                if (b.GetPixel(x, y).Alpha != 0) n++;
            }
        }

        return n;
    }

    [Fact]
    public void AResizedDocumentMovesEachStrokesOwnAxisWithIt()
    {
        var doc = DocWithMirroredMarks();
        var strokes = doc.Scene.Layers[0].Cels[0].Frame!.Strokes;
        var vertical = strokes[0];
        var horizontal = strokes[1];

        // Before: the vertical-axis mark at x 40–60 reflects across x = 100 to
        // x 140–160; the horizontal-axis mark at y 30 reflects across y = 70
        // to y = 110.
        using var before = FrameRasterizer.Rasterize(strokes, W, H, 1.0);
        Assert.True(InkedIn(before, new SKRectI(130, 55, 170, 85)) > 50, "the vertical mirror is not working before the resize");
        Assert.True(InkedIn(before, new SKRectI(90, 100, 130, 120)) > 50, "the horizontal mirror is not working before the resize");

        // Non-uniform on purpose: 2x wide, 3x tall, so X and Y cannot be
        // confused and a factor applied to the wrong axis is visible.
        const int sx = 2, sy = 3;
        Assert.True(ImageResize.Apply(doc, W * sx, H * sy, new PixelResampler()));

        using var after = FrameRasterizer.Rasterize(strokes, W * sx, H * sy, 1.0);
        // Vertical axis now at x = 200: the mark at x 80–120 reflects to 280–320
        // (at y 210). Left at x = 100 it would land at −20…20, off the page.
        var verticalCopy = InkedIn(after, new SKRectI(260, 180, 340, 240));
        var verticalStranded = InkedIn(after, new SKRectI(0, 180, 40, 240));
        // Horizontal axis now at y = 210: the mark at y 90 reflects to y 330
        // (at x 200–240). Left at y = 70 it would land at y 50.
        var horizontalCopy = InkedIn(after, new SKRectI(180, 300, 260, 360));
        var horizontalStranded = InkedIn(after, new SKRectI(180, 30, 260, 70));
        output.WriteLine(
            $"axes after resize: vertical ({vertical.Symmetry!.CenterX}, {vertical.Symmetry.CenterY}), "
            + $"horizontal ({horizontal.Symmetry!.CenterX}, {horizontal.Symmetry.CenterY}); "
            + $"copies {verticalCopy}/{horizontalCopy} px where they belong, {verticalStranded}/{horizontalStranded} px at the old centres");

        Assert.Equal(W * sx / 2.0, vertical.Symmetry.CenterX, 6);
        Assert.Equal(H * sy / 2.0, vertical.Symmetry.CenterY, 6);
        Assert.True(verticalCopy > 200, $"the vertical copy did not move with the paper ({verticalCopy} px)");
        Assert.True(horizontalCopy > 200, $"the horizontal copy did not move with the paper ({horizontalCopy} px)");
        Assert.Equal(0, verticalStranded);
        Assert.Equal(0, horizontalStranded);
    }

    /// <summary>
    /// The undo snapshot taken before a resize is a <c>Doc.Clone</c>; its
    /// strokes must own their axes, or the resize scales the snapshot too and
    /// undo brings the points back about a centre that stayed scaled.
    /// </summary>
    [Fact]
    public void TheUndoSnapshotKeepsItsOwnAxisThroughAResize()
    {
        var doc = DocWithMirroredMarks();
        var snapshot = doc.Clone();
        var live = doc.Scene.Layers[0].Cels[0].Frame!.Strokes[0];
        var kept = snapshot.Scene.Layers[0].Cels[0].Frame!.Strokes[0];
        Assert.NotSame(live.Symmetry, kept.Symmetry);

        Assert.True(ImageResize.Apply(doc, W * 2, H * 2, new PixelResampler()));

        Assert.Equal(W, live.Symmetry!.CenterX, 6);
        Assert.Equal(W / 2.0, kept.Symmetry!.CenterX, 6);
        Assert.Equal(H / 2.0, kept.Symmetry.CenterY, 6);
    }
}
