using System.IO.Compression;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A document that would inflate to gigabytes is refused rather than read
/// until memory runs out (Q235).
/// </summary>
/// <remarks>
/// <para>
/// Every saved document is gzip, and nothing limited how far one could expand
/// on load: a file of a few megabytes made of zeros inflates to gigabytes, and
/// the <c>OutOfMemoryException</c> it ends in is caught by no open path. The
/// owner chose to cap and refuse.
/// </para>
/// <para>
/// <b>The limit is a ratio and a floor, both generous.</b> Real documents
/// compress about 5× (the lab's 30-layer document: 25 MB on disk, 124 MB of
/// JSON); a bomb compresses a thousandfold. A document is refused only when it
/// has inflated past <see cref="DocJson.InflationFloorBytes"/> <em>and</em> past
/// <see cref="DocJson.MaxInflationRatio"/> times its size on disk, so no real
/// document comes near it and a small one can never trip it.
/// </para>
/// </remarks>
[Collection("DocJsonLimits")]
public class InflatingDocumentTests
{
    /// <summary>
    /// A document that never ends: valid JSON whose name is one long run of
    /// letters, gzipped — what a real bomb looks like, not zeros a parser would
    /// refuse at the first byte. Written without ever holding it.
    /// </summary>
    private static string Bomb(long letters)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-bomb-{Guid.NewGuid():N}.lightbox.json");
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        gzip.Write("{\"version\":1,\"scene\":{\"name\":\""u8);
        var block = Enumerable.Repeat((byte)'a', 1 << 20).ToArray();
        for (long written = 0; written < letters; written += block.Length) gzip.Write(block);
        return path;
    }

    /// <summary>
    /// Scaled down so the suite never allocates a gigabyte (CI has been killed
    /// by less): the floor drops to 1 MB for this test, and the bomb is 16 MB
    /// of letters in a few kilobytes on disk — past both limits, as a real one is.
    /// </summary>
    [Fact]
    public void ADocumentThatInflatesFarPastItsSizeIsRefused()
    {
        var floor = DocJson.InflationFloorBytes;
        var path = Bomb(16L << 20);
        try
        {
            DocJson.InflationFloorBytes = 1L << 20;
            var refused = Assert.Throws<InvalidDataException>(() => DocJson.Load(path));
            Assert.Contains("expand", refused.Message);
        }
        finally
        {
            DocJson.InflationFloorBytes = floor;
            File.Delete(path);
        }
    }

    /// <summary>A real document still opens: the cap is far from anything the app writes.</summary>
    [Fact]
    public void AnOrdinaryDocumentStillOpens()
    {
        var doc = new Doc();
        var frame = new Frame();
        for (var s = 0; s < 200; s++)
        {
            frame.Strokes.Add(new Stroke
            {
                Color = "#304050",
                Points = Enumerable.Range(0, 200).Select(i => new StrokePoint(i, s, 0.7)).ToList(),
            });
        }
        doc.Scene.Layers.Add(new Layer { Name = "Ink", Cels = [new Cel { Frame = frame }] });
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-ordinary-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(doc, path);
            Assert.Equal(200, DocJson.Load(path).Scene.Layers[0].Cels[0].Frame!.Strokes.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
