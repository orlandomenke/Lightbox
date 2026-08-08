using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

public class RoundTripTests
{
    private static Doc SampleDoc()
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var painted = doc.Scene.Layers[0].Cels[0].Frame!;
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
                new Cel { Frame = new Frame { Strokes = [new Stroke { Points = [new(1, 2, 0.5)] }] } },
                new Cel(), // hold
                new Cel { Frame = new Frame() },
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

        var p0 = restored.Scene.Layers[0].Cels[0].Frame!;
        var orig = doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal(orig.PngBase64, p0.PngBase64);
        var s = Assert.Single(p0.Strokes);
        Assert.Equal("#ff8800", s.Color);
        Assert.Equal("left-arm", s.Label);
        Assert.Equal(ToolKind.Brush, s.Tool);
        Assert.Equal(12, s.Brush.Size);
        Assert.Equal(0.5, s.Brush.Hardness);
        Assert.Equal(new StrokePoint(30, 40, 0.8), s.Points[1]);

        // Hold cels survive as null frames. `Assert.IsType<Frame>` used to stand
        // here and said nothing once there was one class — what actually needs
        // pinning is that a keyed cel comes back keyed with its content, and a
        // hold comes back a hold.
        Assert.Null(restored.Scene.Layers[1].Cels[1].Frame);
        Assert.Single(restored.Scene.Layers[1].Cels[0].Frame!.Strokes);
        Assert.Empty(restored.Scene.Layers[1].Cels[2].Frame!.Strokes);

        // These two were a `VectorFrame`, which could not hold a baseline at all.
        // The class can now; the drawing still has not got one, and that is the
        // distinction that survived the merge.
        Assert.False(restored.Scene.Layers[1].Cels[0].Frame!.HasBaseline);
        Assert.True(p0.HasBaseline);
    }

    [Fact]
    public void Serialize_UsesCamelCase_AndWritesNoFrameKind()
    {
        var json = DocJson.Serialize(SampleDoc());
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("scene", out var scene));
        Assert.True(scene.TryGetProperty("frameCount", out _));

        var frame = scene.GetProperty("layers")[0].GetProperty("cels")[0].GetProperty("frame");
        // There is one frame class, so there is nothing to discriminate. This
        // assertion read `Assert.Equal("painted", …GetProperty("kind"))` before the
        // merge; keeping it inverted is what stops the key coming back.
        Assert.False(frame.TryGetProperty("kind", out _));
        Assert.True(frame.TryGetProperty("pngBase64", out _));   // this one was imported into
        var stroke = frame.GetProperty("strokes")[0];
        Assert.Equal("brush", stroke.GetProperty("tool").GetString());
        Assert.True(stroke.GetProperty("brush").TryGetProperty("hardness", out _));
        Assert.True(stroke.GetProperty("points")[0].TryGetProperty("pressure", out _));
    }

    [Fact]
    public void Serialize_OmitsPngBase64_WhenTheFrameHasNoBaseline()
    {
        // The other half of "absent unless used". A hand-drawn frame — every frame
        // in a document nobody imported into — must not carry an empty string for
        // a pixel baseline it never had. `""` was written on every frame of every
        // document before the merge.
        var json = DocJson.Serialize(SampleDoc());
        using var parsed = JsonDocument.Parse(json);
        var drawn = parsed.RootElement
            .GetProperty("scene").GetProperty("layers")[1]
            .GetProperty("cels")[0].GetProperty("frame");

        Assert.False(drawn.TryGetProperty("pngBase64", out _));
        // Not `Assert.DoesNotContain("\"kind\"", json)` — `Layer.Kind` is still
        // written, deliberately, as import provenance. The frame's is what went.
        Assert.False(drawn.TryGetProperty("kind", out _));
    }

    [Fact]
    public void Deserialize_AcceptsAndIgnoresTheOldFrameKind_Anywhere()
    {
        // Two things at once. Every file written before the merge carries a
        // `"kind"`, so it has to be accepted — and LLM-produced JSON does not
        // guarantee key order, which is why the converter is hand-rolled rather
        // than `[JsonPolymorphic]`, so it has to be accepted last as well as first.
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
        var frame = doc.Scene.Layers[0].Cels[0].Frame;
        Assert.NotNull(frame);
        Assert.Equal("f1", frame.Id);

        // And it is ignored rather than preserved: the kind is gone on the way back
        // out. Checked on the frame element, because `Layer.Kind` writes a `"kind"`
        // of its own and is meant to.
        using var written = JsonDocument.Parse(DocJson.Serialize(doc));
        var element = written.RootElement
            .GetProperty("scene").GetProperty("layers")[0]
            .GetProperty("cels")[0].GetProperty("frame");
        Assert.False(element.TryGetProperty("kind", out _));
        Assert.Equal("vector", doc.Scene.Layers[0].Kind.ToString().ToLowerInvariant());
    }

    [Fact]
    public void Deserialize_NormalisesAnEmptyBaseline_ToAbsent()
    {
        // A pre-merge document's `"pngBase64": ""` means "no baseline", so it must
        // not round-trip back out as a key. Without the normalisation on read,
        // every old file would keep the key it was supposed to lose.
        var json = """
        {
          "version": 1,
          "scene": {
            "id": "scene_1", "name": "S", "width": 100, "height": 100,
            "fps": 12, "frameCount": 1,
            "layers": [{
              "id": "layer_1", "name": "L", "kind": "painted", "visible": true, "opacity": 1,
              "cels": [{ "frame": { "kind": "painted", "id": "f1", "pngBase64": "", "strokes": [] } }]
            }]
          }
        }
        """;
        var doc = DocJson.Deserialize(json);
        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Null(frame.PngBase64);
        Assert.False(frame.HasBaseline);
        Assert.DoesNotContain("\"pngBase64\"", DocJson.Serialize(doc));
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
        clone.Scene.Layers[0].Cels[0].Frame!.Strokes.Clear();
        Assert.Single(doc.Scene.Layers[0].Cels[0].Frame!.Strokes);
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
