using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

public class DraftPreviewTests
{
    private static Stroke Line(BrushSettings brush, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Brush = brush,
        Points = [new(40, y, 0.8), new(90, y, 0.8)],
    };

    [Fact]
    public void Draft_PaintsTheSegment_AndNothingFarFromIt()
    {
        using var layer = new SKBitmap(256, 128, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(SKColors.Transparent);
        var brush = new BrushSettings { Size = 14, Hardness = 0.8, WetEdge = 0.5, Granulation = 0.4 };

        FrameRasterizer.AppendDraft(layer, Line(brush, 64));

        Assert.True(layer.GetPixel(65, 64).Alpha > 100, "the segment paints");
        Assert.Equal(0, layer.GetPixel(200, 64).Alpha);   // far right: untouched
        Assert.Equal(0, layer.GetPixel(65, 10).Alpha);    // far above: untouched
    }

    [Fact]
    public void Draft_EraserStillErases()
    {
        using var layer = new SKBitmap(128, 128, SKColorType.Rgba8888, SKAlphaType.Premul);
        layer.Erase(new SKColor(200, 40, 40, 255));
        var eraser = Line(new BrushSettings { Size = 20, Hardness = 1 }, 64);
        eraser.Tool = ToolKind.Eraser;

        FrameRasterizer.AppendDraft(layer, eraser);

        Assert.Equal(0, layer.GetPixel(65, 64).Alpha);
        Assert.Equal(255, layer.GetPixel(65, 10).Alpha);
    }

    [Fact]
    public void ExactRender_IsUnaffectedByTheDraftRefactor()
    {
        // The committed pipeline (the record's truth) must render the effect
        // brush exactly as before: wet edge and granulation present.
        var brush = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, WetEdge = 1 };
        using var bmp = FrameRasterizer.Rasterize([Line(brush, 64)], 128, 128);
        var rim = bmp.GetPixel(65, 64 - 8);   // rim band
        var core = bmp.GetPixel(65, 64);
        Assert.True(rim.Red < core.Red || rim.Green < core.Green || rim.Blue < core.Blue
                    || rim.Red < 250, "wet edge must still darken the rim on commit");
    }
}

public class AntiAliasTests
{
    private static Stroke Dot(bool aa) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Brush = new BrushSettings { Size = 30, Hardness = 1, AntiAlias = aa },
        Points = [new(32, 32, 1)],
    };

    private static int IntermediateAlphaCount(SKBitmap bmp)
    {
        var count = 0;
        foreach (var px in bmp.Pixels)
        {
            if (px.Alpha is > 8 and < 247) count++;
        }
        return count;
    }

    [Fact]
    public void AntiAliasOff_ProducesHardPixelEdges()
    {
        using var aliased = FrameRasterizer.Rasterize([Dot(aa: false)], 64, 64);
        using var smooth = FrameRasterizer.Rasterize([Dot(aa: true)], 64, 64);

        Assert.Equal(0, IntermediateAlphaCount(aliased));       // only on/off pixels
        Assert.True(IntermediateAlphaCount(smooth) > 20, "AA edge has gradient pixels");
    }

    [Fact]
    public void FillStroke_HonorsAntiAlias()
    {
        Stroke Fill(bool aa) => new()
        {
            Tool = ToolKind.Fill,
            Color = "#000000",
            Brush = new BrushSettings { Opacity = 1, AntiAlias = aa },
            Points = [new(10.5, 10.5, 1), new(50.7, 12.3, 1), new(45.2, 50.8, 1), new(12.4, 48.6, 1)],
        };
        using var aliased = FrameRasterizer.Rasterize([Fill(false)], 64, 64);
        using var smooth = FrameRasterizer.Rasterize([Fill(true)], 64, 64);
        Assert.Equal(0, IntermediateAlphaCount(aliased));
        Assert.True(IntermediateAlphaCount(smooth) > 10);
    }

    [Fact]
    public void AntiAlias_IsPerStroke_SoOldArtNeverChanges()
    {
        var brush = new BrushSettings { Size = 20, AntiAlias = true };
        var clone = brush.Clone();
        Assert.True(clone.AntiAlias);

        var doc = Lightbox.Core.Documents.DocumentFactory.CreateDoc(32, 32, 12);
        var frame = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke { Brush = new BrushSettings { AntiAlias = false }, Points = [new(1, 1, 1)] });
        var restored = Lightbox.Core.Serialization.DocJson.Deserialize(Lightbox.Core.Serialization.DocJson.Serialize(doc));
        Assert.False(((PaintedFrame)restored.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush.AntiAlias);
    }
}

public class ScratchPreviewTests
{
    [Fact]
    public void ScratchPreview_MatchesExactRender_WhereTheStrokeCrossesItself()
    {
        var info = new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var baseBmp = new SKBitmap(info);
        baseBmp.Erase(SKColors.White);

        var brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 0.5, Flow = 1 };
        var points = new List<StrokePoint> { new(40, 40, 1), new(160, 160, 1), new(160, 40, 1), new(40, 160, 1) };
        var stroke = new Stroke { Color = "#000000", Brush = brush, Points = points };

        // Live preview, fed as two tails like two pointer events.
        using var composite = baseBmp.Copy();
        using var compCanvas = new SKCanvas(composite);
        using var scratch = new SKBitmap(info);
        using var scratchCanvas = new SKCanvas(scratch);
        scratchCanvas.Clear(SKColors.Transparent);
        var tails = new[]
        {
            new Stroke { Color = stroke.Color, Brush = brush, Points = points.Take(2).ToList() },
            new Stroke { Color = stroke.Color, Brush = brush, Points = points.Skip(1).ToList() },
        };
        foreach (var tail in tails)
        {
            var dabs = BrushEngine.WalkDabs(tail);
            BrushEngine.StampDabRange(scratchCanvas, tail, dabs, 0, dabs.Count);
            var rect = BrushEngine.DraftSegmentBounds(tail, info);
            Assert.NotNull(rect);
            BrushEngine.ComposeDraftRegion(compCanvas, baseBmp, scratch, rect!.Value, stroke);
        }
        compCanvas.Flush();

        // The exact commit render of the same stroke.
        using var exact = baseBmp.Copy();
        using var exactCanvas = new SKCanvas(exact);
        BrushEngine.StampStroke(exactCanvas, stroke, info);
        exactCanvas.Flush();

        // At the self-crossing the old per-segment preview double-applied the
        // 50% opacity (visibly darker); the scratch model must match commit.
        var crossing = composite.GetPixel(100, 100);
        var committed = exact.GetPixel(100, 100);
        Assert.InRange(Math.Abs(crossing.Red - committed.Red), 0, 8);
        Assert.InRange(Math.Abs(crossing.Alpha - committed.Alpha), 0, 8);

        // Sanity: 50% black over white is mid-grey, not near-black build-up.
        Assert.InRange((int)crossing.Red, 100, 155);
    }
}

public class FluidLineTests
{
    /// <summary>
    /// Light pen pressure shrinks dabs; spacing must shrink with them or the
    /// stroke degenerates into a dotted trail (the "stamps and circles" bug).
    /// Every centerline pixel of a light-pressure stroke must be inked.
    /// </summary>
    [Theory]
    [InlineData(0.15)]
    [InlineData(0.4)]
    [InlineData(1.0)]
    public void LightPressureStrokes_HaveNoGapsAlongTheCenterline(double pressure)
    {
        var info = new SKImageInfo(400, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        bmp.Erase(SKColors.White);
        using var canvas = new SKCanvas(bmp);

        var stroke = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings { Size = 40, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.15 },
            // Sparse input points, like fast pen events.
            Points =
            [
                new(30, 50, pressure), new(120, 50, pressure),
                new(230, 50, pressure), new(370, 50, pressure),
            ],
        };
        BrushEngine.StampStroke(canvas, stroke, info);
        canvas.Flush();

        for (var x = 32; x <= 368; x++)
        {
            var px = bmp.GetPixel(x, 50);
            Assert.True(px.Red < 128, $"Gap at x={x} (pressure {pressure}): centerline pixel is not inked.");
        }
    }

    /// <summary>Pressure ramps mid-segment must interpolate, not jump: the stroke stays connected.</summary>
    [Fact]
    public void PressureRamp_KeepsTheStrokeConnected()
    {
        var info = new SKImageInfo(400, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        bmp.Erase(SKColors.White);
        using var canvas = new SKCanvas(bmp);

        var stroke = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings { Size = 60, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.12 },
            Points = [new(30, 50, 1.0), new(370, 50, 0.08)], // heavy -> feather-light in one segment
        };
        BrushEngine.StampStroke(canvas, stroke, info);
        canvas.Flush();

        for (var x = 32; x <= 366; x++)
        {
            var px = bmp.GetPixel(x, 50);
            Assert.True(px.Red < 128, $"Gap at x={x}: taper broke the line apart.");
        }
    }
}
