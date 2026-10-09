using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// The pressure-gated tooth is optional the way the camera is: absent from
/// the file until a brush uses it (the optional-settings rule, checked by
/// dumping the JSON rather than by reading the model).
/// </summary>
public class PencilToothRecordTests
{
    private static Doc DocWith(BrushSettings brush)
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [new StrokePoint(1, 1, 1), new StrokePoint(10, 10, 1)],
            Brush = brush,
        });
        return doc;
    }

    [Fact]
    public void ABrushWhoseToothIgnoresPressureWritesNoKey()
    {
        var json = DocJson.Serialize(DocWith(new BrushSettings { Size = 5, TextureSurface = PaperKind.Rough, TextureDepth = 0.5 }));
        Assert.DoesNotContain("\"texturePressure\"", json);
    }

    [Fact]
    public void ThePressureGateRoundTrips()
    {
        var brush = new BrushSettings { Size = 5, TextureSurface = PaperKind.ColdPress, TextureDepth = 0.9, TexturePressure = 0.85 };
        var reloaded = DocJson.Deserialize(DocJson.Serialize(DocWith(brush)));
        var back = ((Frame)reloaded.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush;
        Assert.Equal(0.85, back.TexturePressure!.Value, 6);
    }

    [Fact]
    public void CloneCarriesTheGateAndADefaultBrushHasNone()
    {
        var brush = new BrushSettings { TexturePressure = 0.6 };
        Assert.Equal(0.6, brush.Clone().TexturePressure!.Value, 6);
        Assert.Null(new BrushSettings().Clone().TexturePressure);
    }
}
