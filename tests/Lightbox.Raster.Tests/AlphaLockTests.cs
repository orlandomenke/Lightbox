using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Alpha lock lets an artist recolour a shape without altering its
/// silhouette. It is recorded per stroke rather than read from the layer at
/// render time, so toggling the layer's lock never repaints finished work.
/// </summary>
public class AlphaLockTests
{
    private static SKBitmap LayerWithABlob(int w = 200, int h = 200)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(SKColors.Transparent);
        using (var p = new SKPaint { Color = SKColors.Blue, IsAntialias = false })
        {
            surface.Canvas.DrawRect(new SKRect(50, 50, 150, 150), p);
        }
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        image.ReadPixels(info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
        return bmp;
    }

    private static Stroke RedBar(bool alphaLocked) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#ff0000",
        AlphaLocked = alphaLocked,
        // Runs from empty space, across the blob, and out into empty space.
        Points = [new StrokePoint(20, 100, 1), new StrokePoint(180, 100, 1)],
        Brush = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static SKBitmap Paint(bool alphaLocked)
    {
        var layer = LayerWithABlob();
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, RedBar(alphaLocked), info, layer);
        canvas.Flush();
        return layer;
    }

    [Fact]
    public void PaintOnlyLandsWhereTheLayerAlreadyHadContent()
    {
        using var locked = Paint(alphaLocked: true);

        // Inside the blob: recoloured.
        Assert.Equal(SKColors.Red, locked.GetPixel(100, 100));
        // Outside it, on the same stroke line: still empty.
        Assert.Equal((byte)0, locked.GetPixel(30, 100).Alpha);
        Assert.Equal((byte)0, locked.GetPixel(170, 100).Alpha);
    }

    [Fact]
    public void TheSilhouetteIsUnchanged()
    {
        using var before = LayerWithABlob();
        using var after = Paint(alphaLocked: true);

        var differing = 0;
        for (var x = 0; x < before.Width; x++)
        for (var y = 0; y < before.Height; y++)
            if (before.GetPixel(x, y).Alpha != after.GetPixel(x, y).Alpha) differing++;

        Assert.Equal(0, differing);
    }

    [Fact]
    public void WithoutTheLock_TheSameStrokeSpillsOutside()
    {
        using var free = Paint(alphaLocked: false);
        Assert.Equal(SKColors.Red, free.GetPixel(30, 100));
        Assert.Equal(SKColors.Red, free.GetPixel(170, 100));
    }

    [Fact]
    public void TheFlagSurvivesACloneAndARoundTrip()
    {
        var stroke = RedBar(alphaLocked: true);
        Assert.True(stroke.Clone().AlphaLocked);

        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(stroke);
        var reloaded = Lightbox.Core.Serialization.DocJson.Deserialize(
            Lightbox.Core.Serialization.DocJson.Serialize(doc));
        Assert.True(((Frame)reloaded.Scene.Layers[0].Cels[0].Frame!).Strokes[0].AlphaLocked);
    }

    [Fact]
    public void ReRenderingTheWholeFrame_ReproducesTheMaskWithoutStoringIt()
    {
        // The rasterizer stamps strokes in order, so the content before an
        // alpha-locked stroke is whatever it has already drawn. That must hold
        // on a cold re-render, not just when painting live.
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush, Color = "#0000ff",
            Points = [new StrokePoint(50, 100, 1), new StrokePoint(150, 100, 1)],
            Brush = new BrushSettings { Size = 40, Hardness = 1, Opacity = 1, Flow = 1, AntiAlias = false },
        });
        frame.Strokes.Add(RedBar(alphaLocked: true));

        using var first = FrameRasterizer.Materialize(frame, 200, 200);
        using var again = FrameRasterizer.Materialize(frame, 200, 200);

        for (var x = 0; x < 200; x += 3)
        for (var y = 0; y < 200; y += 3)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));

        // And the locked stroke really was confined to the first stroke's ink.
        Assert.Equal((byte)0, first.GetPixel(20, 100).Alpha);
        Assert.True(first.GetPixel(100, 100).Red > 200, "the locked stroke should have recoloured the bar it sat on");
    }
}
