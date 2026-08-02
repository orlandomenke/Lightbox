using System.Security.Cryptography;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

public class PressureTests
{
    private static string Hash(SKBitmap bmp) => Convert.ToHexString(SHA256.HashData(bmp.GetPixelSpan()));

    private static Stroke Line(BrushSettings brush, double pressure) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Brush = brush,
        Points = [new(20, 40, pressure), new(60, 40, pressure), new(100, 40, pressure)],
    };

    [Fact]
    public void MasterSwitchOff_IgnoresPenPressure_Entirely()
    {
        var responsive = new BrushSettings
        {
            Size = 20, Hardness = 1, PressureEnabled = true, PressureSizeGamma = 1, PressureFlowGamma = 1,
        };
        var deaf = responsive.Clone();
        deaf.PressureEnabled = false;

        // With the master switch off, a light-pressure stroke matches a full-pressure one.
        using var light = FrameRasterizer.Rasterize([Line(deaf, 0.25)], 128, 80);
        using var full = FrameRasterizer.Rasterize([Line(deaf, 1.0)], 128, 80);
        Assert.Equal(Hash(light), Hash(full));

        // With it on, the same light stroke is visibly thinner.
        using var lightOn = FrameRasterizer.Rasterize([Line(responsive, 0.25)], 128, 80);
        Assert.NotEqual(Hash(lightOn), Hash(full));
        Assert.Equal(0, lightOn.GetPixel(60, 48).Alpha);   // outside the thin line
        Assert.True(full.GetPixel(60, 48).Alpha > 0);       // inside the full-width line
    }

    [Fact]
    public void PressureHardness_SoftensTheEdge_AtLightPressure()
    {
        var brush = new BrushSettings
        {
            Size = 24, Hardness = 1, PressureEnabled = true,
            PressureSizeGamma = 0, PressureHardnessGamma = 1, // only hardness responds
        };
        using var light = FrameRasterizer.Rasterize([Line(brush, 0.3)], 128, 80);
        using var full = FrameRasterizer.Rasterize([Line(brush, 1.0)], 128, 80);

        // Same width (size doesn't respond), but the light stroke's edge fades.
        var edgeLight = light.GetPixel(60, 49).Alpha; // near the dab rim
        var edgeFull = full.GetPixel(60, 49).Alpha;
        Assert.True(edgeLight < edgeFull, $"light edge {edgeLight} must be softer than full {edgeFull}");
        Assert.True(light.GetPixel(60, 40).Alpha > 200, "the core still paints");
    }

    [Fact]
    public void PressureSettings_SurviveCloneAndSerialization()
    {
        var brush = new BrushSettings
        {
            PressureEnabled = false, PressureSizeGamma = 2.5, PressureFlowGamma = 0.5, PressureHardnessGamma = 1.5,
        };
        var clone = brush.Clone();
        Assert.False(clone.PressureEnabled);
        Assert.Equal(2.5, clone.PressureSizeGamma);
        Assert.Equal(0.5, clone.PressureFlowGamma);
        Assert.Equal(1.5, clone.PressureHardnessGamma);

        var doc = Lightbox.Core.Documents.DocumentFactory.CreateDoc(32, 32, 12);
        var frame = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke { Brush = brush, Points = [new(1, 1, 0.5)] });
        var restored = Lightbox.Core.Serialization.DocJson.Deserialize(Lightbox.Core.Serialization.DocJson.Serialize(doc));
        var restoredBrush = ((PaintedFrame)restored.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush;
        Assert.False(restoredBrush.PressureEnabled);
        Assert.Equal(1.5, restoredBrush.PressureHardnessGamma);
    }
}
