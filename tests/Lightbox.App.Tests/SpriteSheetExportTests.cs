using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The asset target's export. The headline behaviour is the trim default:
/// per-frame trimming is the obvious implementation and makes the character
/// jitter, so the union of every frame's ink is what ships.
/// </summary>
public class SpriteSheetExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-sheet-" + Guid.NewGuid().ToString("N"));

    public SpriteSheetExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Path_(string name) => System.IO.Path.Combine(_dir, name);

    /// <summary>
    /// Four frames of a "character" that moves: a box that shifts right and
    /// changes height, so per-frame ink bounds differ on every frame.
    /// </summary>
    private static Doc Walking(int frames = 4, string? paperColor = null)
    {
        var doc = DocumentFactory.CreateDoc(200, 120, 10, paperColor);
        doc.Scene.FrameCount = frames;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);

        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel { Frame = new PaintedFrame() });

        for (var i = 0; i < frames; i++)
        {
            var cel = layer.Cels[i];
            if (cel.Frame is not PaintedFrame p) continue;
            double x = 40 + i * 12, top = 30 + i * 5;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#c02040",
                Points =
                [
                    new StrokePoint(x, top, 1), new StrokePoint(x + 30, top, 1),
                    new StrokePoint(x + 30, 90, 1), new StrokePoint(x, 90, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }
        return doc;
    }

    private static JsonElement Meta(SpriteSheetResult result) =>
        JsonDocument.Parse(File.ReadAllText(result.MetadataPath)).RootElement;

    [Fact]
    public void TrimmingDefaultsToTheUnion_SoEveryCellIsTheSameSizeAndNothingJitters()
    {
        var result = SpriteSheetExporter.Export(Walking(), Path_("walk.png"));
        var frames = Meta(result).GetProperty("frames");

        // Every cell the same size AND at the same offset into the canvas. It
        // is the offset that matters: equal sizes with different offsets would
        // still make the character jump frame to frame.
        var first = frames[0].GetProperty("spriteSourceSize");
        foreach (var frame in frames.EnumerateArray())
        {
            var box = frame.GetProperty("spriteSourceSize");
            Assert.Equal(first.GetProperty("x").GetInt32(), box.GetProperty("x").GetInt32());
            Assert.Equal(first.GetProperty("y").GetInt32(), box.GetProperty("y").GetInt32());
            Assert.Equal(first.GetProperty("w").GetInt32(), box.GetProperty("w").GetInt32());
            Assert.Equal(first.GetProperty("h").GetInt32(), box.GetProperty("h").GetInt32());
        }

        // And the union really is tighter than the canvas — otherwise this
        // would pass with trimming doing nothing at all.
        Assert.True(result.CellWidth < 200, $"cell was {result.CellWidth} px of a 200 px canvas");
        Assert.True(result.CellHeight < 120, $"cell was {result.CellHeight} px of a 120 px canvas");
    }

    [Fact]
    public void TheUnionCoversEveryFramesInk()
    {
        // The whole point of the union: no frame may be clipped by it.
        var result = SpriteSheetExporter.Export(Walking(), Path_("walk.png"));
        var frames = Meta(result).GetProperty("frames");
        var box = frames[0].GetProperty("spriteSourceSize");
        int left = box.GetProperty("x").GetInt32(), top = box.GetProperty("y").GetInt32();

        // Frame 0's box starts at x=40; frame 3's ends at 12*3+40+30 = 106.
        Assert.True(left <= 40, $"union started at {left}, clipping the first frame");
        Assert.True(left + result.CellWidth >= 106, "union ended before the last frame's ink");
        Assert.True(top <= 30, $"union started at y={top}, clipping the first frame");
    }

    [Fact]
    public void PerFrameTrimmingRecordsWhereEachCellCameFrom()
    {
        var result = SpriteSheetExporter.Export(
            Walking(), Path_("tight.png"), new SpriteSheetOptions { Trim = SpriteTrim.PerFrame });
        var frames = Meta(result).GetProperty("frames");

        // Each frame's own box, and it moves with the drawing — which is
        // exactly why this is not the default.
        var xs = frames.EnumerateArray()
            .Select(f => f.GetProperty("spriteSourceSize").GetProperty("x").GetInt32())
            .ToList();
        Assert.True(xs.Distinct().Count() > 1, "per-frame trim produced identical offsets");
        Assert.Equal(xs.OrderBy(v => v), xs);
    }

    [Fact]
    public void NoTrimGivesEveryCellTheWholeCanvas()
    {
        var result = SpriteSheetExporter.Export(
            Walking(), Path_("full.png"), new SpriteSheetOptions { Trim = SpriteTrim.None });
        Assert.Equal(200, result.CellWidth);
        Assert.Equal(120, result.CellHeight);
        Assert.False(Meta(result).GetProperty("frames")[0].GetProperty("trimmed").GetBoolean());
    }

    [Fact]
    public void TheGridHoldsEveryFrameAndTheSheetIsThatSize()
    {
        var result = SpriteSheetExporter.Export(
            Walking(6), Path_("grid.png"), new SpriteSheetOptions { Columns = 3 });

        Assert.Equal(3, result.Columns);
        Assert.Equal(2, result.Rows);
        Assert.Equal(6, result.FrameCount);

        using var sheet = SKBitmap.Decode(result.SheetPath);
        Assert.Equal(result.CellWidth * 3, sheet.Width);
        Assert.Equal(result.CellHeight * 2, sheet.Height);
    }

    [Fact]
    public void EveryCellActuallyContainsItsFrame()
    {
        // A grid whose maths is right but whose blit is off by a cell would
        // pass every metadata assertion above.
        var result = SpriteSheetExporter.Export(
            Walking(4), Path_("cells.png"), new SpriteSheetOptions { Columns = 4 });
        using var sheet = SKBitmap.Decode(result.SheetPath);

        for (var i = 0; i < 4; i++)
        {
            var found = false;
            for (var y = 0; y < result.CellHeight && !found; y++)
            {
                for (var x = 0; x < result.CellWidth; x++)
                {
                    if (sheet.GetPixel(i * result.CellWidth + x, y).Alpha <= 8) continue;
                    found = true;
                    break;
                }
            }
            Assert.True(found, $"cell {i} was empty");
        }
    }

    [Fact]
    public void PaddingLeavesATransparentGutterWithoutLosingInk()
    {
        var result = SpriteSheetExporter.Export(
            Walking(2), Path_("pad.png"), new SpriteSheetOptions { Columns = 2, Padding = 4 });
        using var sheet = SKBitmap.Decode(result.SheetPath);

        Assert.Equal((result.CellWidth + 8) * 2, sheet.Width);
        // The very corner of the sheet is gutter.
        Assert.Equal((byte)0, sheet.GetPixel(0, 0).Alpha);
        Assert.Equal((byte)0, sheet.GetPixel(1, 1).Alpha);
    }

    [Fact]
    public void WithoutAPivot_TheSidecarCarriesNone()
    {
        var result = SpriteSheetExporter.Export(Walking(), Path_("nopivot.png"));
        var meta = Meta(result);
        Assert.False(meta.GetProperty("meta").TryGetProperty("pivot", out _));
        Assert.False(meta.GetProperty("frames")[0].TryGetProperty("pivot", out _));
    }

    [Fact]
    public void ThePivotIsRecordedPerCell_SoTrimmingCannotShiftTheCharacter()
    {
        var doc = Walking();
        doc.Scene.Pivot = Pivot.BottomCentre(doc.Scene.Width, doc.Scene.Height);

        var trimmed = SpriteSheetExporter.Export(doc, Path_("piv-trim.png"));
        var untrimmed = SpriteSheetExporter.Export(
            doc, Path_("piv-full.png"), new SpriteSheetOptions { Trim = SpriteTrim.None });

        // The pivot lands at a different place inside the cell depending on
        // the trim — that is the point. An engine placing the sprite by its
        // pivot puts the character in the same world position either way,
        // which is what makes trimming safe.
        var a = Meta(trimmed).GetProperty("frames")[0].GetProperty("pivot");
        var b = Meta(untrimmed).GetProperty("frames")[0].GetProperty("pivot");
        Assert.NotEqual(a.GetProperty("x").GetDouble(), b.GetProperty("x").GetDouble());

        // Untrimmed, the cell IS the canvas, so the pivot offset is the pivot.
        Assert.Equal(100.0, b.GetProperty("x").GetDouble());
        Assert.Equal(120.0, b.GetProperty("y").GetDouble());

        // And the scene pivot itself is recorded once, in document coordinates.
        var meta = Meta(trimmed).GetProperty("meta").GetProperty("pivot");
        Assert.Equal(100.0, meta.GetProperty("x").GetDouble());
        Assert.Equal(120.0, meta.GetProperty("y").GetDouble());
    }

    [Fact]
    public void TheSidecarIsAsepriteShaped()
    {
        // Matching a format engine importers already read, rather than
        // inventing one. These are the keys they look for.
        var result = SpriteSheetExporter.Export(Walking(), Path_("shape.png"));
        var root = Meta(result);

        var frame = root.GetProperty("frames")[0];
        foreach (var key in new[] { "filename", "frame", "rotated", "trimmed", "spriteSourceSize", "sourceSize", "duration" })
        {
            Assert.True(frame.TryGetProperty(key, out _), $"frames[0] is missing \"{key}\"");
        }
        foreach (var key in new[] { "x", "y", "w", "h" })
        {
            Assert.True(frame.GetProperty("frame").TryGetProperty(key, out _), $"frame is missing \"{key}\"");
        }

        var meta = root.GetProperty("meta");
        Assert.Equal("RGBA8888", meta.GetProperty("format").GetString());
        Assert.Equal("sheet".Length > 0 ? "shape.png" : "", meta.GetProperty("image").GetString());
        Assert.Equal(10, meta.GetProperty("fps").GetInt32());
        // sourceSize is the untrimmed canvas, so an importer can reconstruct it.
        Assert.Equal(200, frame.GetProperty("sourceSize").GetProperty("w").GetInt32());
        // Duration is milliseconds per frame at the scene's fps.
        Assert.Equal(100, frame.GetProperty("duration").GetInt32());
    }

    [Fact]
    public void AnOpaqueBackgroundLayerDoesNotDefeatTrimming()
    {
        // Trimming has to see the drawing, not the paper. Compositing the
        // Background layer in would make every frame's ink bounds the whole
        // canvas and turn trimming into a no-op that looked like it worked.
        var doc = Walking(paperColor: "#ffffff");
        Assert.Contains(doc.Scene.Layers, l => l.IsBackground);

        var result = SpriteSheetExporter.Export(doc, Path_("bg.png"));
        Assert.True(result.CellWidth < 200, "the background layer swallowed the trim");
    }

    [Fact]
    public void AnEmptyDocumentStillProducesASheet()
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        doc.Scene.FrameCount = 2;

        var result = SpriteSheetExporter.Export(doc, Path_("empty.png"));
        Assert.True(File.Exists(result.SheetPath));
        Assert.Equal(2, result.FrameCount);
        // Nothing to trim to, so the cell falls back to the canvas rather than
        // collapsing to zero.
        Assert.Equal(64, result.CellWidth);
    }
}
