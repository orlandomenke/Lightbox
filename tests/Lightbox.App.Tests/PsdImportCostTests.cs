using System.Diagnostics;
using Lightbox.App.Services;
using Lightbox.Raster.Tests;

namespace Lightbox.App.Tests;

/// <summary>
/// What importing a PSD costs, and where the cost actually is.
/// </summary>
/// <remarks>
/// <para>
/// Baselines are canvas-sized (the 2026-08-24 decision), so a layer holding a
/// 300×300 patch is still stored as a full-canvas PNG. That was chosen over
/// storing the layer's own rect, and this is where its price is written down
/// rather than assumed — because the first version of the write-up guessed
/// wrong, saying "only decode time and memory pay". Measured, on 2026-08-24:
/// </para>
/// <code>
/// 1920×1080,  8 layers: parse 41ms  baselines  621ms  gzipped 2KB
/// 1920×1080, 24 layers: parse 47ms  baselines 1929ms  gzipped 7KB
/// 3840×2160, 12 layers: parse 21ms  baselines 3805ms  gzipped 12KB
/// </code>
/// <para>
/// <b>Reading the PSD is 1–3% of the work; building the baselines is the rest.</b>
/// The file-size half of the argument held up completely — PNG and gzip crush
/// the transparent margin to nothing, so a 4K import is 12 KB on disk. The time
/// half did not: a 4K file takes about four seconds, and it is spent traversing
/// 8.3M pixels per layer to encode a picture that is mostly empty.
/// </para>
/// <para>
/// <b>There is no cheap way out inside this design</b>, which is worth recording
/// so nobody re-runs the experiment: PNG compression level was the obvious lever
/// and is not one. Dropping zlib from the default to 1 saves about 20% of the
/// encode, and dropping it to 0 takes a 32 KB file to 32 MB. The fix is to store
/// the layer's own rect, which cuts the work from canvas area to content area —
/// filed as B301 with this measurement behind it.
/// </para>
/// <para>
/// <b>The committed tests run smaller documents than the table above</b>, and
/// deliberately. Measuring the 4K case allocates about 400 MB of transient
/// bitmaps — one canvas-sized surface per layer — and the App suite already dies
/// of memory under load often enough to have its own entry (B269). A budget test
/// that makes a known crash likelier is a bad trade when a quarter-size document
/// catches an order-of-magnitude regression just as well. The numbers above are
/// the real measurement, taken once; the numbers below are the guard.
/// </para>
/// <para>
/// The budgets are deliberately loose, as every budget here is: they catch an
/// order of magnitude, not drift.
/// </para>
/// </remarks>
public class PsdImportCostTests(Xunit.ITestOutputHelper output)
{
    private static byte[] Fixture(int width, int height, int layers)
    {
        var fixture = new PsdFixture { Width = width, Height = height };
        for (var i = 0; i < layers; i++)
        {
            // A small patch of content on a big canvas: the realistic shape, and
            // the one where a canvas-sized baseline costs the most.
            fixture.Layers.Add(PsdLayerFixture.Solid(
                $"Layer {i}", (byte)(i * 9), 100, 200, a: 255,
                left: i * 10, top: i * 10, right: i * 10 + 300, bottom: i * 10 + 300,
                compression: PsdCompression.Rle));
        }
        return fixture.Build();
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AMultiLayerImportStaysWithinAnOrderOfMagnitudeOfItsMeasuredCost()
    {
        var bytes = Fixture(1280, 720, 8);

        var watch = Stopwatch.StartNew();
        var result = PsdDocumentImport.Open(bytes, "measured");
        var elapsed = watch.ElapsedMilliseconds;

        output.WriteLine($"1280×720, 8 layers: {elapsed}ms, {result.Document.Scene.Layers.Count} layers");
        Assert.Equal(8, result.Document.Scene.Layers.Count);
        Assert.True(elapsed < 6_000, $"import took {elapsed}ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ReadingThePsdIsASmallFractionOfImportingIt()
    {
        // The attribution, kept as a test so it cannot quietly invert. If parsing
        // ever becomes the expensive half, the reader has regressed and the
        // write-up above stops being true.
        var bytes = Fixture(1280, 720, 12);

        var watch = Stopwatch.StartNew();
        using (var parsed = Lightbox.Import.PsdReader.Read(bytes))
        {
            Assert.Equal(12, parsed.Layers.Count);
        }
        var parse = watch.ElapsedMilliseconds;

        watch.Restart();
        PsdDocumentImport.Open(bytes, "measured");
        var whole = watch.ElapsedMilliseconds;

        output.WriteLine($"parse {parse}ms of {whole}ms total — baselines are {whole - parse}ms");
        Assert.True(parse < whole / 2, $"parse {parse}ms was not the smaller half of {whole}ms");
    }

    [Fact]
    public void TheImportedDocumentIsSmallOnDiskDespiteCanvasSizedBaselines()
    {
        // The half of the canvas-sized argument that did hold: a mostly-empty
        // full-canvas PNG compresses to almost nothing, so the file stays sane
        // even though the pixels do not.
        var bytes = Fixture(1280, 720, 12);
        var doc = PsdDocumentImport.Open(bytes, "measured").Document;

        var json = Lightbox.Core.Serialization.DocJson.Serialize(doc);
        var gzipped = new MemoryStream();
        using (var zip = new System.IO.Compression.GZipStream(
            gzipped, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            zip.Write(System.Text.Encoding.UTF8.GetBytes(json));
        }

        var kb = gzipped.Length / 1024;
        output.WriteLine($"12 layers at 1280×720 → {json.Length / 1024}KB of JSON, {kb}KB gzipped");
        Assert.True(kb < 512, $"a 24-layer import gzipped to {kb}KB");
    }
}
