using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// The on-disk container is gzip (Q65): the readable indented JSON stays, the
/// 6.4× it cost on disk goes. What these pin: the container really is
/// compressed, the compression really pays, a round trip through it loses
/// nothing, and — the half that guards artists' existing work — a plain-JSON
/// document written before the change still loads.
/// </summary>
public sealed class DocJsonCompressionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"lightbox-gzip-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    /// <summary>
    /// A painting-shaped document: many strokes, many points, one brush — the
    /// case Q65 measured, scaled down to test size.
    /// </summary>
    private static Doc PaintedDoc(int strokes = 120, int pointsPerStroke = 40)
    {
        var doc = DocumentFactory.CreateDoc(640, 360, 24);
        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        for (var s = 0; s < strokes; s++)
        {
            var stroke = new Stroke { Color = "#334455" };
            for (var p = 0; p < pointsPerStroke; p++)
            {
                stroke.Points.Add(new StrokePoint(
                    10 + s * 3.17 + p * 1.93, 20 + s * 1.31 + p * 2.71, 0.3 + 0.5 * (p % 7) / 7.0));
            }
            frame.Strokes.Add(stroke);
        }
        return doc;
    }

    [Fact]
    public void TheSavedFileIsAGzipContainerAndRoundTripsExactly()
    {
        var doc = PaintedDoc();
        var path = PathFor("painting.lightbox.json");
        DocJson.Save(doc, path);

        using (var file = File.OpenRead(path))
        {
            Assert.Equal(0x1f, file.ReadByte());
            Assert.Equal(0x8b, file.ReadByte());
        }

        // Byte-for-byte equality of the compact snapshots, not a spot check of
        // a few properties: the container must be a container, changing nothing.
        var loaded = DocJson.Load(path);
        Assert.Equal(DocJson.ToSnapshot(doc), DocJson.ToSnapshot(loaded));
    }

    [Fact]
    public void APlainJsonDocumentFromBeforeTheContainerStillLoads()
    {
        // Every document saved before 2026-08-13 looks like this: indented
        // JSON straight on disk. Written here exactly the way the old Save
        // wrote it, so this is the legacy file, not a simulation of one.
        var doc = PaintedDoc(strokes: 5, pointsPerStroke: 5);
        var path = PathFor("legacy.lightbox.json");
        File.WriteAllText(path, DocJson.Serialize(doc));

        var loaded = DocJson.Load(path);
        Assert.Equal(DocJson.ToSnapshot(doc), DocJson.ToSnapshot(loaded));
    }

    [Fact]
    public void ATruncatedOrEmptyFileThrowsRatherThanReturningSomething()
    {
        // The sniff reads two bytes before deciding which parser gets the
        // file. A file with zero or one byte — a crash at the worst possible
        // moment — must fail the way a corrupt document always has: loudly,
        // from the parser, never as a hang or a silent null.
        var empty = PathFor("empty.lightbox.json");
        File.WriteAllBytes(empty, []);
        Assert.ThrowsAny<Exception>(() => DocJson.Load(empty));

        var oneByte = PathFor("one-byte.lightbox.json");
        File.WriteAllBytes(oneByte, [0x1f]);
        Assert.ThrowsAny<Exception>(() => DocJson.Load(oneByte));
    }

    [Fact]
    public void CompressionEarnsItsKeep()
    {
        // Not a ratio pinned to a decimal — that would fail on a serializer
        // detail — but the reason the change exists: the container must be
        // several times smaller than the JSON it holds, even at Fastest.
        var doc = PaintedDoc();
        var path = PathFor("measured.lightbox.json");
        DocJson.Save(doc, path);

        var plain = System.Text.Encoding.UTF8.GetByteCount(DocJson.Serialize(doc));
        var saved = new FileInfo(path).Length;
        output.WriteLine($"plain {plain:N0} bytes, gzipped {saved:N0} bytes, {(double)plain / saved:F1}x");

        Assert.True(saved * 3 < plain,
            $"expected at least 3x compression, got {plain:N0} -> {saved:N0}");
    }
}
