using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// Medium parameters reach pixels, so they live on the stroke (invariant 4).
/// That only holds if they survive a save and reload — a document that
/// forgets its paint physics re-renders as something else.
/// </summary>
public class MediumSettingsTests
{
    private static Doc DocWithMedium(MediumSettings medium)
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#204080",
            Points = [new StrokePoint(4, 4, 1), new StrokePoint(40, 30, 0.6)],
            Brush = new BrushSettings { Size = 12, Medium = medium },
        });
        return doc;
    }

    [Fact]
    public void EveryMediumParameter_SurvivesARoundTrip()
    {
        var medium = new MediumSettings
        {
            Kind = MediumKind.Oil,
            Wetness = 0.17, Viscosity = 0.83, Drag = 0.29, FlowSteps = 7,
            Absorbency = 0.61, EdgePull = 0.44,
            PigmentDensity = 0.72, Granularity = 0.38, Hiding = 0.91,
            PhysicalMixing = false,
            Paper = PaperKind.Canvas, PaperScale = 9.5, PaperInfluence = 0.66,
            Body = 0.55, Relief = 0.48, BristleDrag = 0.23,
            PaintLoad = 0.31, Pickup = 0.19,
        };

        var reloaded = DocJson.Deserialize(DocJson.Serialize(DocWithMedium(medium)));
        var back = ((Frame)reloaded.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush.Medium;

        Assert.Equal(MediumKind.Oil, back.Kind);
        Assert.Equal(0.17, back.Wetness, 6);
        Assert.Equal(0.83, back.Viscosity, 6);
        Assert.Equal(0.29, back.Drag, 6);
        Assert.Equal(7, back.FlowSteps);
        Assert.Equal(0.61, back.Absorbency, 6);
        Assert.Equal(0.44, back.EdgePull, 6);
        Assert.Equal(0.72, back.PigmentDensity, 6);
        Assert.Equal(0.38, back.Granularity, 6);
        Assert.Equal(0.91, back.Hiding, 6);
        Assert.False(back.PhysicalMixing);
        Assert.Equal(PaperKind.Canvas, back.Paper);
        Assert.Equal(9.5, back.PaperScale, 6);
        Assert.Equal(0.66, back.PaperInfluence, 6);
        Assert.Equal(0.55, back.Body, 6);
        Assert.Equal(0.48, back.Relief, 6);
        Assert.Equal(0.23, back.BristleDrag, 6);
        Assert.Equal(0.31, back.PaintLoad, 6);
        Assert.Equal(0.19, back.Pickup, 6);
    }

    [Fact]
    public void ADocumentSavedBeforeMediaExisted_LoadsAsNoMedium()
    {
        // Older files have no medium key at all. They must come back as the
        // plain dab-stamping brush rather than as a half-initialised
        // simulation. Remove the property through the JSON DOM rather than by
        // regex, so the test proves absence rather than proving that a
        // hand-rolled string edit happened to parse.
        var json = DocJson.Serialize(DocWithMedium(new MediumSettings()));
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var stripped = StripProperty(parsed.RootElement, "medium");

        var reloaded = DocJson.Deserialize(stripped);
        var back = ((Frame)reloaded.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush.Medium;

        Assert.NotNull(back);
        Assert.Equal(MediumKind.None, back.Kind);
    }

    /// <summary>Re-emit JSON with every occurrence of a property removed, at any depth.</summary>
    private static string StripProperty(System.Text.Json.JsonElement element, string name)
    {
        var buffer = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            Write(element, writer);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        void Write(System.Text.Json.JsonElement node, System.Text.Json.Utf8JsonWriter writer)
        {
            switch (node.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in node.EnumerateObject())
                    {
                        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, writer);
                    }
                    writer.WriteEndObject();
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in node.EnumerateArray()) Write(item, writer);
                    writer.WriteEndArray();
                    break;
                default:
                    node.WriteTo(writer);
                    break;
            }
        }
    }

    [Fact]
    public void Clone_DeepCopiesTheMedium_SoTweakingAPresetCannotEditPastStrokes()
    {
        var original = new BrushSettings { Medium = { Kind = MediumKind.Watercolour, Wetness = 0.9 } };
        var copy = original.Clone();

        copy.Medium.Wetness = 0.1;
        copy.Medium.Kind = MediumKind.Ink;

        Assert.Equal(0.9, original.Medium.Wetness, 6);
        Assert.Equal(MediumKind.Watercolour, original.Medium.Kind);
    }
}
