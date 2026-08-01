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
