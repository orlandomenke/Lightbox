using System.Text;
using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// Opening a document reads each drawing once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by profiling the open of a 30-layer, 200-drawing document</b>
/// (2026-10-09). <c>DocJson.Load</c> took 7.1 s of a 20 s freeze, 4.5 s of it in
/// <see cref="FrameConverter"/>. That converter parsed each drawing into a
/// <c>JsonDocument</c>, then copied every field back out as a UTF-16 string
/// (<c>GetRawText</c>) and parsed that string a second time.
/// </para>
/// <para>
/// <b>Measured as allocation, not time</b>, because allocation is the same on
/// every machine and every run. A second parse of a drawing's strokes shows up
/// as several times the document's own size in garbage.
/// </para>
/// </remarks>
public class FrameReadCostTests(ITestOutputHelper output)
{
    private static string DocumentOf(int drawings, int strokes, int points)
    {
        var doc = new Doc();
        var layer = new Layer { Name = "L" };
        for (var d = 0; d < drawings; d++)
        {
            var frame = new Frame();
            for (var s = 0; s < strokes; s++)
            {
                frame.Strokes.Add(new Stroke
                {
                    Color = "#3a5a8a",
                    Brush = new BrushSettings { Size = 5, Hardness = 0.8, Opacity = 1, Flow = 1 },
                    Points = Enumerable.Range(0, points)
                        .Select(i => new StrokePoint(10 + i * 1.5 + s, 20 + Math.Sin(i * 0.1) * 30 + d, 0.6))
                        .ToList(),
                });
            }
            layer.Cels.Add(new Cel { Frame = frame });
        }
        doc.Scene.Layers.Add(layer);
        return DocJson.Serialize(doc);
    }

    [Fact]
    public void ReadingADocumentAllocatesLittleMoreThanTheDocumentItself()
    {
        var json = DocumentOf(drawings: 20, strokes: 20, points: 150);
        var bytes = Encoding.UTF8.GetByteCount(json);
        DocJson.Deserialize(json); // warm up the serializer's metadata

        var before = GC.GetAllocatedBytesForCurrentThread();
        var doc = DocJson.Deserialize(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var ratio = (double)allocated / bytes;
        output.WriteLine($"document {bytes / 1024} KB, read allocated {allocated / 1024} KB: {ratio:0.00}x");
        Assert.Equal(20, doc.Scene.Layers[0].Cels.Count);
        // 3.9x when each drawing was parsed twice, 1.9x read once (2026-10-09).
        Assert.True(ratio < 2.8, $"reading allocated {ratio:0.00}x the document");
    }

    [Fact]
    public void AKeyThisBuildDoesNotKnowIsSkipped()
    {
        // A file from a later build may carry a block this one has never heard
        // of. It opens, and the fields around the unknown one still read.
        var json = """
        {
          "version": 1,
          "scene": {
            "id": "scene_1", "name": "S", "width": 100, "height": 100,
            "fps": 12, "frameCount": 1,
            "layers": [{
              "id": "layer_1", "name": "L", "visible": true, "opacity": 1,
              "cels": [{ "frame": {
                "id": "f1",
                "future": { "nested": [1, 2, { "deeper": "yes" }], "flag": true },
                "strokes": [{ "id": "s1", "tool": "brush", "color": "#102030", "points": [{ "x": 1, "y": 2, "p": 1 }] }],
                "alsoFuture": "text"
              } }]
            }]
          }
        }
        """;
        var frame = DocJson.Deserialize(json).Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal("f1", frame.Id);
        Assert.Single(frame.Strokes);
        Assert.Equal("#102030", frame.Strokes[0].Color);
    }

    /// <summary>
    /// The same, through a saved file. A saved document is gzip, which the
    /// serializer reads as a stream, so the converter's reader is not the final
    /// block and <c>Utf8JsonReader.Skip</c> throws there while working on a
    /// string. Found by the sensitivity review: a large file with a key from a
    /// later build would have been refused (2026-10-09).
    /// </summary>
    [Fact]
    public void AKeyThisBuildDoesNotKnowIsSkippedInASavedFile()
    {
        using var written = JsonDocument.Parse(DocumentOf(drawings: 60, strokes: 5, points: 100));
        var json = Encoding.UTF8.GetString(WithUnknownFrameKeys(written.RootElement));
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-unknown-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            // Gzip, as DocJson.Save writes it, with the keys a later build would have added.
            using (var file = File.Create(path))
            using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
            {
                gzip.Write(Encoding.UTF8.GetBytes(json));
            }
            Assert.True(new FileInfo(path).Length > 16 * 1024, "too small to be read as a stream");

            var doc = DocJson.Load(path);
            Assert.Equal(60, doc.Scene.Layers[0].Cels.Count);
            Assert.All(doc.Scene.Layers[0].Cels, c => Assert.Equal(5, c.Frame!.Strokes.Count));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Every frame given a string and an object key nobody has defined, before its strokes.</summary>
    private static byte[] WithUnknownFrameKeys(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Copy(root, writer, inFrame: false);
        }
        return stream.ToArray();

        static void Copy(JsonElement e, Utf8JsonWriter w, bool inFrame)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    w.WriteStartObject();
                    if (inFrame)
                    {
                        w.WriteString("future", "text");
                        w.WritePropertyName("alsoFuture");
                        w.WriteStartObject();
                        w.WritePropertyName("nested");
                        w.WriteStartArray();
                        w.WriteNumberValue(1);
                        w.WriteStartObject();
                        w.WriteBoolean("deeper", true);
                        w.WriteEndObject();
                        w.WriteEndArray();
                        w.WriteEndObject();
                    }
                    foreach (var p in e.EnumerateObject())
                    {
                        w.WritePropertyName(p.Name);
                        Copy(p.Value, w, inFrame: p.Name == "frame");
                    }
                    w.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    w.WriteStartArray();
                    foreach (var item in e.EnumerateArray()) Copy(item, w, inFrame: false);
                    w.WriteEndArray();
                    break;
                default:
                    e.WriteTo(w);
                    break;
            }
        }
    }
}
