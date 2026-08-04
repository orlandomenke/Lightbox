using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The tiled render against the untiled one, pixel for pixel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bit-identity is the contract.</b> A tiled render is meant to be a
/// different arrangement of the same work; the moment it is a different result,
/// the application has two renderers and they will drift. Fingerprints rather
/// than tolerances, for the reason <c>RuntimeDeterminismTests</c> gives: "near
/// enough" cannot distinguish a rounding difference from a stroke landing in the
/// wrong tile.
/// </para>
/// <para>
/// The brushes here are chosen to be hostile. Scatter, size jitter, rotation
/// jitter and all three colour jitters are seeded from a dab's *document*
/// position through <c>Hash01</c> — so if the tiled path ever subtracts a tile
/// origin from the geometry instead of translating the canvas, every dab
/// re-seeds and every tile boundary becomes a seam. A plain hard round brush
/// would not notice.
/// </para>
/// </remarks>
public class TiledRasterizerTests(ITestOutputHelper output)
{
    private const int W = 900, H = 700;
    private static readonly SKImageInfo Info = new(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static string Fingerprint(SKBitmap bitmap) =>
        Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));

    /// <summary>Everything stochastic on, so a re-seed cannot hide.</summary>
    private static BrushSettings Jittery() => new()
    {
        Size = 26, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.15,
        AntiAlias = true, Scatter = 0.4, SizeJitter = 0.5, MinimumDiameter = 0.3,
        FlowJitter = 0.4, RoundnessJitter = 0.3, RotationJitter = 0.5,
        ColorJitter = 0.4, SecondaryColor = "#c04020",
        HueJitter = 0.3, SaturationJitter = 0.3, BrightnessJitter = 0.3,
    };

    /// <summary>
    /// Strokes that deliberately straddle tile boundaries — a 256 px grid over a
    /// 900x700 document puts seams at 256, 512 and 768.
    /// </summary>
    private static List<Stroke> Drawing()
    {
        var strokes = new List<Stroke>();
        for (var i = 0; i < 12; i++)
        {
            var y = 40 + i * 52;
            strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = i % 2 == 0 ? "#2050b0" : "#b03020",
                Brush = Jittery(),
                // Runs the full width, so every stroke crosses three boundaries.
                Points = Enumerable.Range(0, 20)
                    .Select(k => new StrokePoint(20 + k * 44.0, y + (k % 3) * 9.0, 0.4 + (k % 5) * 0.15))
                    .ToList(),
            });
        }
        return strokes;
    }

    [Fact]
    public void ATiledRenderIsBitIdenticalToAnUntiledOne()
    {
        var strokes = Drawing();

        using var untiled = FrameRasterizer.Rasterize(strokes, W, H);
        using var store = new TileStore(new TileGrid(256));
        TiledRasterizer.Rasterize(store, strokes, Info);
        using var tiled = TiledRasterizer.Flatten(store, W, H);

        var a = Fingerprint(untiled);
        var b = Fingerprint(tiled);
        output.WriteLine($"untiled {a[..16]}…  tiled {b[..16]}…  tiles {store.TileCount}");

        Assert.Equal(a, b);
    }

    /// <summary>
    /// The same, at a tile size that shares no factors with the document — so a
    /// pass is not an artefact of tiles dividing the canvas evenly.
    /// </summary>
    [Theory]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(256)]
    [InlineData(512)]
    public void TheTileSizeDoesNotChangeTheRender(int tileSize)
    {
        var strokes = Drawing();

        using var untiled = FrameRasterizer.Rasterize(strokes, W, H);
        using var store = new TileStore(new TileGrid(tileSize));
        TiledRasterizer.Rasterize(store, strokes, Info);
        using var tiled = TiledRasterizer.Flatten(store, W, H);

        output.WriteLine($"tile {tileSize}: {store.TileCount} tiles, {store.AllocatedBytes / 1024} KB");
        Assert.Equal(Fingerprint(untiled), Fingerprint(tiled));
    }

    /// <summary>
    /// The economy the whole design rests on, measured through the real render
    /// rather than asserted on the store: a drawing that occupies a corner must
    /// not allocate the rest of the document.
    /// </summary>
    [Fact]
    public void EmptyPartsOfTheDocumentAreNeverAllocated()
    {
        var corner = new List<Stroke>
        {
            new()
            {
                Tool = ToolKind.Brush,
                Color = "#000000",
                Brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
                Points = [new StrokePoint(30, 30, 1), new StrokePoint(90, 90, 1)],
            },
        };

        using var store = new TileStore(new TileGrid(256));
        TiledRasterizer.Rasterize(store, corner, Info);

        // 900x700 over 256px tiles is 4x3 = 12 tiles. The mark is in one of them.
        var wholeDocument = store.Grid.CountCovering(0, 0, W, H);
        output.WriteLine($"{store.TileCount} tiles allocated of {wholeDocument} the document spans");

        Assert.Equal(1, store.TileCount);
        Assert.True(wholeDocument >= 12, "the document should span many tiles for this to mean anything");

        // And it still renders the same as the untiled path.
        using var untiled = FrameRasterizer.Rasterize(corner, W, H);
        using var tiled = TiledRasterizer.Flatten(store, W, H);
        Assert.Equal(Fingerprint(untiled), Fingerprint(tiled));
    }

    [Fact]
    public void AnEmptyStrokeListRendersAnEmptyStoreAndAnEmptyImage()
    {
        using var store = new TileStore();
        TiledRasterizer.Rasterize(store, [], Info);

        Assert.Equal(0, store.TileCount);

        using var untiled = FrameRasterizer.Rasterize([], W, H);
        using var tiled = TiledRasterizer.Flatten(store, W, H);
        Assert.Equal(Fingerprint(untiled), Fingerprint(tiled));
    }

    /// <summary>
    /// Effect brushes read the pixels they sit on, and a tile only holds its own —
    /// so this is where bit-identity is most at risk, and the result is recorded
    /// rather than assumed either way.
    /// </summary>
    /// <remarks>
    /// Kept separate from the paint cases on purpose. If this fails it is not the
    /// tiling arithmetic that is wrong, it is that a smudge near a tile edge
    /// cannot see across the seam — a real limitation with a real fix (render
    /// with padded reads), and one worth discovering here rather than in a
    /// drawing.
    /// </remarks>
    [Fact]
    public void AnEffectBrushAcrossATileBoundaryIsMeasuredRatherThanAssumed()
    {
        var strokes = new List<Stroke>
        {
            // Something to smudge, crossing the 256 boundary.
            new()
            {
                Tool = ToolKind.Brush,
                Color = "#3070c0",
                Brush = new BrushSettings { Size = 60, Hardness = 0.4, Opacity = 1, Flow = 1, Spacing = 0.15 },
                Points = [new StrokePoint(180, 200, 1), new StrokePoint(340, 200, 1)],
            },
            // The smudge, dragged straight through the seam at x = 256.
            new()
            {
                Tool = ToolKind.Brush,
                Color = "#000000",
                Brush = new BrushSettings
                {
                    Kind = BrushKind.Smudge, Size = 40, Hardness = 0.5, Flow = 0.5,
                    Spacing = 0.1, SmudgeLength = 0.7, SmudgeRadius = 0.6,
                },
                Points = [new StrokePoint(200, 200, 1), new StrokePoint(320, 220, 1)],
            },
        };

        using var untiled = FrameRasterizer.Rasterize(strokes, W, H);
        using var store = new TileStore(new TileGrid(256));
        TiledRasterizer.Rasterize(store, strokes, Info);
        using var tiled = TiledRasterizer.Flatten(store, W, H);

        var same = Fingerprint(untiled) == Fingerprint(tiled);
        var differing = 0;
        var worst = 0;
        var onTheSmudge = 0;

        // The smudge runs (200,200)→(320,220) with a 40 px brush, so its own
        // footprint is roughly x 180..340, y 180..240. Sampling reaches
        // SmudgeRadius (0.6) × radius further, hence the margin.
        var footprint = SKRectI.Create(150, 150, 220, 120);

        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var p = untiled.GetPixel(x, y);
                var q = tiled.GetPixel(x, y);
                if (p == q) continue;
                differing++;
                worst = Math.Max(worst, Math.Abs(p.Alpha - q.Alpha));
                if (footprint.Contains(x, y)) onTheSmudge++;
            }
        }

        var confined = differing == 0 ? 1.0 : onTheSmudge / (double)differing;
        output.WriteLine(
            $"effect brush across a tile seam: identical={same}, {differing} px differ, " +
            $"worst alpha delta {worst}, {confined:P0} of them inside the smudge's own footprint");

        // B59. This is asserted as the KNOWN-BAD state rather than left as a
        // no-op, because a test that cannot fail is the thing this repo's own
        // measurement rules warn about — "assert the faint mark is present as
        // well as fainter". Two claims, and the second is the diagnosis:
        Assert.False(
            same,
            "the tiled effect-brush render is now identical — B59 has been fixed, so tighten this "
            + "test into an equality and close the bug rather than leaving a guard that passes for "
            + "the wrong reason");
        Assert.True(
            confined > 0.99,
            $"only {confined:P0} of the differences fall inside the smudge's own footprint. B59 is a "
            + "read-neighbourhood defect, so it can only change pixels the effect brush itself "
            + "touched. Differences outside that mean the tiled path is wrong about something "
            + "other than sampling, and the paint-stroke tests above would not catch it.");
    }
}
