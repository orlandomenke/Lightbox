using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A sprite sheet of seamless tiles (Q192): never trimmed, and its gutter is
/// the tile's own far edges rather than transparent.
/// </summary>
/// <remarks>
/// Both are correctness, not features. Union trim would cut a tile to its ink
/// and destroy the seam; a transparent gutter makes an engine's bilinear
/// filter draw a dark line at every tile boundary, because the pixel beyond
/// the right edge of a tile is its left edge and the gutter said it was
/// nothing.
/// </remarks>
public class TileWrapExportTests(ITestOutputHelper output) : IDisposable
{
    private const int W = 200;
    private const int H = 120;

    private readonly string _dir = FreshDir();

    private static string FreshDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lightbox-tiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>One frame: a mark that runs off the right edge, tiled or not.</summary>
    private static Doc Tile(bool wrapped)
    {
        var doc = DocumentFactory.CreateDoc(W, H, 10);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#c02040",
            Brush = new BrushSettings { Size = 14, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            Points = [new StrokePoint(170, 60, 1), new StrokePoint(215, 60, 1)],
            Wrap = wrapped ? TileWrap.OfScene(doc.Scene) : null,
        });
        return doc;
    }

    private static int InkedIn(SKBitmap b, SKRectI box)
    {
        var n = 0;
        for (var y = Math.Max(0, box.Top); y < Math.Min(b.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(b.Width, box.Right); x++)
            {
                if (b.GetPixel(x, y).Alpha != 0) n++;
            }
        }

        return n;
    }

    [Fact]
    public void AWrappedExportIsNeverTrimmedAndItsGutterIsExtruded()
    {
        var opts = new SpriteSheetOptions { Trim = SpriteTrim.Union, Padding = 4 };
        var result = SpriteSheetExporter.Export(Tile(wrapped: true), Path.Combine(_dir, "tile.png"), opts);

        Assert.True(result.Wrapped);
        var frame = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)).RootElement
            .GetProperty("frames")[0];
        // The whole page, whatever the preset asked for.
        Assert.Equal(W, frame.GetProperty("frame").GetProperty("w").GetInt32());
        Assert.Equal(H, frame.GetProperty("frame").GetProperty("h").GetInt32());
        Assert.False(frame.GetProperty("trimmed").GetBoolean());

        using var sheet = SKBitmap.Decode(result.SheetPath);
        var x = frame.GetProperty("frame").GetProperty("x").GetInt32();
        var y = frame.GetProperty("frame").GetProperty("y").GetInt32();
        // Left gutter = the tile's right edge (ink at x 186–199); right gutter
        // = the tile's left edge (the wrapped-in ink at x 0–15). Both carry it.
        var leftGutter = InkedIn(sheet, new SKRectI(x - 4, y + 50, x, y + 70));
        var rightGutter = InkedIn(sheet, new SKRectI(x + W, y + 50, x + W + 4, y + 70));
        var topGutter = InkedIn(sheet, new SKRectI(x, y - 4, x + W, y));
        output.WriteLine($"gutter ink: left {leftGutter}, right {rightGutter}, top {topGutter} (of 4×20 each side)");
        Assert.True(leftGutter >= 40, $"the left gutter is not the tile's right edge ({leftGutter} px)");
        Assert.True(rightGutter >= 40, $"the right gutter is not the tile's left edge ({rightGutter} px)");
        // Nothing wraps vertically here, so the top gutter is the tile's own
        // bottom row — empty, not invented.
        Assert.Equal(0, topGutter);
    }

    [Fact]
    public void AnUntiledExportStillTrimsAndKeepsItsGutterTransparent()
    {
        // The same document without the wrap takes the preset at its word, so
        // existing exports are untouched by any of this.
        var opts = new SpriteSheetOptions { Trim = SpriteTrim.Union, Padding = 4 };
        var result = SpriteSheetExporter.Export(Tile(wrapped: false), Path.Combine(_dir, "plain.png"), opts);

        Assert.False(result.Wrapped);
        var frame = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)).RootElement
            .GetProperty("frames")[0];
        Assert.True(frame.GetProperty("trimmed").GetBoolean());
        Assert.True(frame.GetProperty("frame").GetProperty("w").GetInt32() < W);

        using var sheet = SKBitmap.Decode(result.SheetPath);
        var x = frame.GetProperty("frame").GetProperty("x").GetInt32();
        var y = frame.GetProperty("frame").GetProperty("y").GetInt32();
        var h = frame.GetProperty("frame").GetProperty("h").GetInt32();
        Assert.Equal(0, InkedIn(sheet, new SKRectI(x - 4, y, x, y + h)));
    }
}
