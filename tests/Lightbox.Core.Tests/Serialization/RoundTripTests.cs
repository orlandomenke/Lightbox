using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

public class RoundTripTests
{
    private static Doc SampleDoc()
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var painted = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        painted.PngBase64 = Convert.ToBase64String([1, 2, 3, 4]);
        painted.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#ff8800",
            Brush = new BrushSettings { Size = 12, Hardness = 0.5, Opacity = 0.9, Spacing = 0.2 },
            Points = [new(10, 20, 0.5), new(30, 40, 0.8)],
            Label = "left-arm",
        });

        var vectorLayer = new Layer
        {
            Name = "Lines",
            Kind = LayerKind.Vector,
            Cels =
            [
                new Cel { Frame = new VectorFrame { Strokes = [new Stroke { Points = [new(1, 2, 0.5)] }] } },
                new Cel(), // hold
                new Cel { Frame = new VectorFrame() },
            ],
        };
        doc.Scene.Layers.Add(vectorLayer);
        doc.Scene.FrameCount = 3;
        doc.Scene.Layers[0].Cels.Add(new Cel());
        doc.Scene.Layers[0].Cels.Add(new Cel());
        return doc;
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var doc = SampleDoc();
        var restored = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Equal(doc.Version, restored.Version);
        Assert.Equal(doc.Scene.Width, restored.Scene.Width);
        Assert.Equal(doc.Scene.Height, restored.Scene.Height);
        Assert.Equal(doc.Scene.Fps, restored.Scene.Fps);
        Assert.Equal(doc.Scene.FrameCount, restored.Scene.FrameCount);
        Assert.Equal(doc.Scene.Layers.Count, restored.Scene.Layers.Count);

        var p0 = Assert.IsType<PaintedFrame>(restored.Scene.Layers[0].Cels[0].Frame);
        var orig = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal(orig.PngBase64, p0.PngBase64);
        var s = Assert.Single(p0.Strokes);
        Assert.Equal("#ff8800", s.Color);
        Assert.Equal("left-arm", s.Label);
        Assert.Equal(ToolKind.Brush, s.Tool);
        Assert.Equal(12, s.Brush.Size);
        Assert.Equal(0.5, s.Brush.Hardness);
        Assert.Equal(new StrokePoint(30, 40, 0.8), s.Points[1]);

        // Hold cels survive as null frames.
        Assert.Null(restored.Scene.Layers[1].Cels[1].Frame);
        Assert.IsType<VectorFrame>(restored.Scene.Layers[1].Cels[0].Frame);
        Assert.IsType<VectorFrame>(restored.Scene.Layers[1].Cels[2].Frame);
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndKindDiscriminator()
    {
        var json = DocJson.Serialize(SampleDoc());
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("scene", out var scene));
        Assert.True(scene.TryGetProperty("frameCount", out _));

        var frame = scene.GetProperty("layers")[0].GetProperty("cels")[0].GetProperty("frame");
        Assert.Equal("painted", frame.GetProperty("kind").GetString());
        Assert.True(frame.TryGetProperty("pngBase64", out _));
        var stroke = frame.GetProperty("strokes")[0];
        Assert.Equal("brush", stroke.GetProperty("tool").GetString());
        Assert.True(stroke.GetProperty("brush").TryGetProperty("hardness", out _));
        Assert.True(stroke.GetProperty("points")[0].TryGetProperty("pressure", out _));
    }

    [Fact]
    public void Deserialize_AcceptsKindAnywhereInObject()
    {
        // LLM-produced JSON doesn't guarantee key order — "kind" may come last.
        var json = """
        {
          "version": 1,
          "scene": {
            "id": "scene_1", "name": "S", "width": 100, "height": 100,
            "fps": 12, "frameCount": 1,
            "layers": [{
              "id": "layer_1", "name": "L", "kind": "vector", "visible": true, "opacity": 1,
              "cels": [{ "frame": { "strokes": [], "id": "f1", "kind": "vector" } }]
            }]
          }
        }
        """;
        var doc = DocJson.Deserialize(json);
        Assert.IsType<VectorFrame>(doc.Scene.Layers[0].Cels[0].Frame);
    }

    [Fact]
    public void OnionEnabled_RoundTrips_AndDefaultsTrueForOlderDocs()
    {
        var doc = SampleDoc();
        doc.Scene.Layers[0].OnionEnabled = false;
        var restored = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.False(restored.Scene.Layers[0].OnionEnabled);
        Assert.True(restored.Scene.Layers[1].OnionEnabled);

        // Documents saved before the field existed keep onion skinning on.
        var legacy = """
        {
          "version": 1,
          "scene": {
            "id": "scene_1", "name": "S", "width": 100, "height": 100,
            "fps": 12, "frameCount": 1,
            "layers": [{
              "id": "layer_1", "name": "L", "kind": "painted", "visible": true, "opacity": 1,
              "cels": [{ "frame": { "kind": "painted", "id": "f1", "strokes": [] } }]
            }]
          }
        }
        """;
        Assert.True(DocJson.Deserialize(legacy).Scene.Layers[0].OnionEnabled);
    }

    [Fact]
    public void Deserialize_UnknownKind_Throws()
    {
        var json = """{ "version": 1, "scene": { "layers": [{ "cels": [{ "frame": { "kind": "hologram" } }] }] } }""";
        Assert.Throws<JsonException>(() => DocJson.Deserialize(json));
    }

    [Fact]
    public void Clone_IsDeepAndIndependent()
    {
        var doc = SampleDoc();
        var clone = DocJson.Clone(doc);
        ((PaintedFrame)clone.Scene.Layers[0].Cels[0].Frame!).Strokes.Clear();
        Assert.Single(((PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes);
    }

    [Fact]
    public void SaveAndLoad_File_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-test-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            var doc = SampleDoc();
            DocJson.Save(doc, path);
            var loaded = DocJson.Load(path);
            Assert.Equal(DocJson.Serialize(doc), DocJson.Serialize(loaded));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
