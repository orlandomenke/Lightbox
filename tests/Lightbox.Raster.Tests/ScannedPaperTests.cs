using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The scanned sheets — measured surfaces embedded as height maps
/// (docs/CREDITS-papers.md) — held to what the constructed papers promise: the
/// same answer for the same pixel for ever, no seam where the tile repeats,
/// and a tooth that uses the range.
/// </summary>
public class ScannedPaperTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Scanned() =>
        Enum.GetValues<PaperKind>().Where(PaperField.IsScanned).Select(k => new object[] { k });

    [Fact]
    public void TheFiveScansAreTheScannedKinds()
    {
        var scanned = Enum.GetValues<PaperKind>().Where(PaperField.IsScanned).ToArray();
        Assert.Equal(
            [PaperKind.Plain, PaperKind.Kraft, PaperKind.Fibre, PaperKind.Linen, PaperKind.Plaster], scanned);
        foreach (var built in new[] { PaperKind.Smooth, PaperKind.ColdPress, PaperKind.Rough, PaperKind.Canvas })
        {
            Assert.False(PaperField.IsScanned(built));
        }
    }

    /// <summary>
    /// Every kind in the enum renders. A kind added to the model and not to
    /// the field would otherwise be found by the first artist to pick it.
    /// </summary>
    [Fact]
    public void EveryPaperKindHasASurface()
    {
        foreach (var kind in Enum.GetValues<PaperKind>())
        {
            var h = PaperField.HeightAt(17, 23, kind, 8);
            Assert.InRange(h, 0f, 1f);
        }
    }

    [Theory]
    [MemberData(nameof(Scanned))]
    public void AScanRepeatsWithoutASeamInBothDirections(PaperKind kind)
    {
        foreach (var scale in new[] { 8.0, 3.0, 20.0 })
        {
            int px = PaperField.TilePeriod(kind, scale), py = PaperField.TilePeriodY(kind, scale);
            for (var i = 0; i < 400; i++)
            {
                int x = (i * 37) - 300, y = (i * 53) - 500;
                var here = PaperField.HeightAt(x, y, kind, scale);
                Assert.Equal(here, PaperField.HeightAt(x + px, y, kind, scale));
                Assert.Equal(here, PaperField.HeightAt(x, y + py, kind, scale));
                Assert.Equal(here, PaperField.HeightAt(x - (3 * px), y + (2 * py), kind, scale));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scanned))]
    public void FillAgreesWithHeightAtExactly(PaperKind kind)
    {
        foreach (var scale in new[] { 8.0, 5.0, 14.0 })
        {
            int px = PaperField.TilePeriod(kind, scale), py = PaperField.TilePeriodY(kind, scale);
            foreach (var (ox, oy) in new[] { (0, 0), (-37, -1), (px - 5, py - 3), ((5 * px) + 9, (-2 * py) - 7) })
            {
                const int w = 23, h = 19;
                var buf = new float[w * h];
                PaperField.Fill(buf, w, h, ox, oy, kind, scale);
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        Assert.Equal(PaperField.HeightAt(ox + x, oy + y, kind, scale), buf[(y * w) + x]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A paper that sat in the middle of the range would make Depth and Tooth
    /// do very little, and nothing would be red. Each scan is stretched to the
    /// whole range when it is built (scripts/make_papers.py).
    /// </summary>
    [Theory]
    [MemberData(nameof(Scanned))]
    public void AScanUsesTheWholeRange(PaperKind kind)
    {
        const int n = 384;
        var buf = new float[n * n];
        PaperField.Fill(buf, n, n, 0, 0, kind, 8);
        double sum = 0, sq = 0;
        float min = 1, max = 0;
        foreach (var v in buf)
        {
            sum += v; sq += v * v;
            min = Math.Min(min, v); max = Math.Max(max, v);
        }
        var mean = sum / buf.Length;
        var std = Math.Sqrt((sq / buf.Length) - (mean * mean));
        output.WriteLine($"{kind}: min {min:0.00} max {max:0.00} mean {mean:0.00} std {std:0.000}");
        Assert.True(min < 0.08 && max > 0.92, $"{kind} spans only {min:0.00}..{max:0.00}");
        Assert.True(std > 0.10, $"{kind} has a spread of {std:0.000}: too flat to be a tooth");
        Assert.InRange(mean, 0.25, 0.75);
    }

    /// <summary>
    /// At its native grain size a scan is the embedded file, texel for pixel,
    /// and its proportions are the scan's own.
    /// </summary>
    [Theory]
    [InlineData(PaperKind.Plain, 1024, 600)]
    [InlineData(PaperKind.Kraft, 794, 1024)]
    [InlineData(PaperKind.Fibre, 1024, 1024)]
    [InlineData(PaperKind.Linen, 1024, 1024)]
    [InlineData(PaperKind.Plaster, 1024, 1024)]
    public void AtItsNativeGrainSizeAScanIsItsOwnSize(PaperKind kind, int width, int height)
    {
        Assert.Equal(width, PaperField.TilePeriod(kind, 8));
        Assert.Equal(height, PaperField.TilePeriodY(kind, 8));
        // Twice the grain is twice the sheet, half is half.
        Assert.Equal(width * 2, PaperField.TilePeriod(kind, 16));
        Assert.Equal(width / 2, PaperField.TilePeriod(kind, 4));
        var image = PaperField.TileImage(kind, 8); // cached by the field: not ours to dispose
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    /// <summary>
    /// The surface itself, pinned (invariants 1 and 2): a stroke on one of
    /// these papers has to replay to the same pixels on every machine and in
    /// every later build. Re-recording any of these changes existing art; a
    /// replaced scan needs a new kind, not a new file under an old name.
    /// </summary>
    [Theory]
    [InlineData(PaperKind.Plain, "9536CD9900B2-E542E34A87A6-69577B814F19")]
    [InlineData(PaperKind.Kraft, "87A43F6260FF-12C28E384E5D-F45CB3CCDC7E")]
    [InlineData(PaperKind.Fibre, "CE10CA61DEA9-75DA70D559E5-B38548207CB5")]
    [InlineData(PaperKind.Linen, "01F02A42986A-F110512B6941-D1C47DF779F0")]
    [InlineData(PaperKind.Plaster, "50FBD98B34D3-6C0D7CB25B2E-91D97F62E9D6")]
    public void TheSurfaceIsTheOneThatShipped(PaperKind kind, string expected)
    {
        var hashes = new List<string>();
        foreach (var scale in new[] { 8.0, 5.0, 14.0 })
        {
            int w = PaperField.TilePeriod(kind, scale), h = PaperField.TilePeriodY(kind, scale);
            var buf = new float[w * h];
            PaperField.Fill(buf, w, h, 0, 0, kind, scale);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(buf.AsSpan());
            hashes.Add(Convert.ToHexString(SHA256.HashData(bytes))[..12]);
        }
        var actual = string.Join("-", hashes);
        output.WriteLine($"{kind}: {actual}");
        Assert.Equal(expected, actual);
    }
}
