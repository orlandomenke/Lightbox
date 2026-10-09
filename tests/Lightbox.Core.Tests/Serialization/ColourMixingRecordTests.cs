using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// The mixing block is optional the way the camera is: absent from the file
/// until a brush uses it, whole when it does.
/// </summary>
public class ColourMixingRecordTests
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
    public void ABrushThatNeverMixesWritesNoMixingKey()
    {
        var json = DocJson.Serialize(DocWith(new BrushSettings { Size = 5 }));
        Assert.DoesNotContain("\"mixing\"", json);
        Assert.DoesNotContain("\"amount\"", json);
        Assert.DoesNotContain("\"length\"", json);
    }

    [Fact]
    public void AMixingBrushRoundTripsEveryField()
    {
        var brush = new BrushSettings
        {
            Size = 5,
            Mixing = new ColourMixing { Amount = 0.35, Length = 0.7, Reach = 0.25 },
        };
        var reloaded = DocJson.Deserialize(DocJson.Serialize(DocWith(brush)));
        var back = ((Frame)reloaded.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush.Mixing;
        Assert.NotNull(back);
        Assert.Equal(0.35, back!.Amount, 6);
        Assert.Equal(0.7, back.Length, 6);
        Assert.Equal(0.25, back.Reach, 6);
    }

    [Fact]
    public void CloneDeepCopiesTheMixingSoAPresetTweakCannotEditPastStrokes()
    {
        var brush = new BrushSettings { Mixing = new ColourMixing { Amount = 0.5 } };
        var clone = brush.Clone();
        clone.Mixing!.Amount = 0.9;
        Assert.Equal(0.5, brush.Mixing!.Amount);
        Assert.Null(new BrushSettings().Clone().Mixing);
    }
}
