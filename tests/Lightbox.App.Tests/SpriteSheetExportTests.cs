using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using SkiaSharp;

using Lightbox.Core.Projects;
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

    // ---- P5a: rect packing and per-sprite metadata ------------------------------

    [Fact]
    public void TheGridIsStillTheDefaultAndItsBytesAreUnchanged()
    {
        // The safety property for this whole change: an existing export must not
        // move. Written as a byte comparison rather than a shape check, because
        // "the same layout" and "the same file" are different claims and only the
        // second one keeps somebody's importer working.
        var before = SpriteSheetExporter.Export(Walking(6), Path_("before.png"));
        var beforeBytes = File.ReadAllBytes(before.SheetPath);
        var beforeMeta = File.ReadAllText(before.MetadataPath);

        var after = SpriteSheetExporter.Export(
            Walking(6), Path_("after.png"), new SpriteSheetOptions { Pack = SpritePack.Grid });

        Assert.Equal(SpritePack.Grid, before.Pack);
        Assert.Equal(beforeBytes, File.ReadAllBytes(after.SheetPath));
        Assert.Equal(
            beforeMeta.Replace("before", "after"),
            File.ReadAllText(after.MetadataPath));
    }

    [Fact]
    public void APackedSheetIsSmallerThanTheGridOnRaggedFrames()
    {
        // Per-frame trimming is where packing pays: the grid takes the widest by
        // the tallest for every cell whatever the trim said.
        var grid = SpriteSheetExporter.Export(
            Walking(8), Path_("grid.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.PerFrame, Pack = SpritePack.Grid });
        var packed = SpriteSheetExporter.Export(
            Walking(8), Path_("packed.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.PerFrame, Pack = SpritePack.Skyline });

        var gridArea = (long)grid.SheetWidth * grid.SheetHeight;
        var packedArea = (long)packed.SheetWidth * packed.SheetHeight;

        File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lightbox-pack-measurement.txt"),
            $"packed {packed.SheetWidth}x{packed.SheetHeight} = {packedArea} px, occupancy {packed.Occupancy:P1}\n"
            + $"grid   {grid.SheetWidth}x{grid.SheetHeight} = {gridArea} px, occupancy {grid.Occupancy:P1}\n"
            + $"cells  grid {grid.CellWidth}x{grid.CellHeight}, packed max {packed.CellWidth}x{packed.CellHeight}\n");
        Assert.True(
            packedArea < gridArea,
            $"packed {packed.SheetWidth}x{packed.SheetHeight} ({packedArea} px, {packed.Occupancy:P1} full) "
            + $"vs grid {grid.SheetWidth}x{grid.SheetHeight} ({gridArea} px, {grid.Occupancy:P1} full); "
            + $"cells grid {grid.CellWidth}x{grid.CellHeight} packed-max {packed.CellWidth}x{packed.CellHeight}");
        // Occupancy is *reported* but deliberately not compared between the two
        // modes, because it does not mean what it looks like: a grid with no
        // padding is 100% cell-occupied by construction, however empty those
        // cells are. Total sheet area is the honest comparison, and it is the one
        // above. Occupancy earns its place for a *packed* sheet, where it says
        // how much of the image is sprite.
        Assert.True(packed.Occupancy > 0 && packed.Occupancy <= 1);
    }

    [Fact]
    public void APackedSheetReportsNoGridRatherThanAPlausibleOne()
    {
        // An importer that reads columns and divides would be silently wrong.
        // Zero is a value it can check; 3 is a value it would trust.
        var packed = SpriteSheetExporter.Export(
            Walking(6), Path_("nogrid.png"), new SpriteSheetOptions { Pack = SpritePack.Skyline });

        Assert.Equal(0, packed.Columns);
        Assert.Equal(0, packed.Rows);
        var meta = Meta(packed).GetProperty("meta");
        Assert.Equal("skyline", meta.GetProperty("pack").GetString());
        Assert.Equal(0, meta.GetProperty("columns").GetInt32());
    }

    [Fact]
    public void TheSidecarCarriesEverySpritesOwnRect()
    {
        // The half that makes packing usable at all.
        var packed = SpriteSheetExporter.Export(
            Walking(6), Path_("rects.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.PerFrame, Pack = SpritePack.Skyline });

        var frames = Meta(packed).GetProperty("frames").EnumerateArray().ToList();
        Assert.Equal(6, frames.Count);

        var rects = frames.Select(f => f.GetProperty("frame")).Select(r => (
            X: r.GetProperty("x").GetInt32(),
            Y: r.GetProperty("y").GetInt32(),
            W: r.GetProperty("w").GetInt32(),
            H: r.GetProperty("h").GetInt32())).ToList();

        Assert.All(rects, r => Assert.True(r.W > 0 && r.H > 0));
        // Inside the sheet, every one of them.
        Assert.All(rects, r => Assert.True(
            r.X + r.W <= packed.SheetWidth && r.Y + r.H <= packed.SheetHeight,
            $"rect {r.X},{r.Y} {r.W}x{r.H} runs off a {packed.SheetWidth}x{packed.SheetHeight} sheet"));
        // And no two overlap, which is the property the packer exists for.
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i];
                var b = rects[j];
                var apart = a.X + a.W <= b.X || b.X + b.W <= a.X || a.Y + a.H <= b.Y || b.Y + b.H <= a.Y;
                Assert.True(apart, $"sprites {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void PackingTheSameDocumentTwiceProducesTheSameFile()
    {
        // A re-export that reshuffles the atlas makes every downstream diff
        // meaningless.
        var first = SpriteSheetExporter.Export(
            Walking(8), Path_("det1.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.PerFrame, Pack = SpritePack.Skyline });
        var second = SpriteSheetExporter.Export(
            Walking(8), Path_("det2.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.PerFrame, Pack = SpritePack.Skyline });

        Assert.Equal(File.ReadAllBytes(first.SheetPath), File.ReadAllBytes(second.SheetPath));
    }

    [Fact]
    public void APackedSheetStillCarriesThePivotPerCell()
    {
        // Packing must not cost the thing that stops trimming shifting the
        // character. The pivot is measured inside the cell, so where the cell
        // landed on the sheet is irrelevant — and that is worth a test, because
        // it is exactly the kind of offset a new layout quietly breaks.
        var doc = Walking(4);
        doc.Scene.Pivot = new Pivot { X = 100, Y = 90 };

        var grid = SpriteSheetExporter.Export(
            doc, Path_("pg.png"), new SpriteSheetOptions { Trim = SpriteTrim.Union });
        var packed = SpriteSheetExporter.Export(
            doc, Path_("pp.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.Union, Pack = SpritePack.Skyline });

        static (double X, double Y) PivotOf(JsonElement meta) =>
            meta.GetProperty("frames")[0].GetProperty("pivot") is { } p
                ? (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())
                : (0, 0);

        Assert.Equal(PivotOf(Meta(grid)), PivotOf(Meta(packed)));
    }

    [Fact]
    public void PaddingStillSeparatesEverySpriteWhenPacked()
    {
        var packed = SpriteSheetExporter.Export(
            Walking(6), Path_("pad.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.PerFrame, Pack = SpritePack.Skyline, Padding = 2 });

        var rects = Meta(packed).GetProperty("frames").EnumerateArray()
            .Select(f => f.GetProperty("frame"))
            .Select(r => (X: r.GetProperty("x").GetInt32(), Y: r.GetProperty("y").GetInt32(),
                          W: r.GetProperty("w").GetInt32(), H: r.GetProperty("h").GetInt32()))
            .ToList();

        Assert.All(rects, r => Assert.True(r.X >= 2 && r.Y >= 2, $"no gutter at {r.X},{r.Y}"));
        Assert.All(rects, r => Assert.True(
            r.X + r.W + 2 <= packed.SheetWidth && r.Y + r.H + 2 <= packed.SheetHeight,
            "no gutter at the far edge"));
    }

    // ---- P5b: named anchors in the sidecar ---------------------------------------

    [Fact]
    public void ADocumentWithNoAnchorsWritesNoAnchorKey()
    {
        var result = SpriteSheetExporter.Export(Walking(3), Path_("noanchor.png"));
        Assert.DoesNotContain("\"anchors\"", File.ReadAllText(result.MetadataPath));
    }

    [Fact]
    public void AnAnchorIsExportedPerFrameByNameAndInsideTheCell()
    {
        // Measured inside the cell like the pivot, and for the same reason:
        // trimming must not be able to move where a weapon attaches.
        var doc = Walking(4);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var hand = Anchors.Declare(doc.Scene, "leftHand");
        Anchors.SetAcross(layer, 0, 4, hand.Id, new AnchorPoint(70, 60));

        var result = SpriteSheetExporter.Export(
            doc, Path_("anchor.png"), new SpriteSheetOptions { Trim = SpriteTrim.Union });

        var frames = Meta(result).GetProperty("frames").EnumerateArray().ToList();
        Assert.Equal(4, frames.Count);
        foreach (var frame in frames)
        {
            var anchor = frame.GetProperty("anchors").GetProperty("leftHand");
            var source = frame.GetProperty("spriteSourceSize");
            // Cell-relative: the document position minus where the cell starts.
            Assert.Equal(70 - source.GetProperty("x").GetInt32(), anchor.GetProperty("x").GetDouble());
            Assert.Equal(60 - source.GetProperty("y").GetInt32(), anchor.GetProperty("y").GetDouble());
        }
    }

    [Fact]
    public void AnAnchorOnAHeldDrawingIsExportedOnEveryFrameItShows()
    {
        // A socket that vanished on every other frame would read as a rigging bug
        // in the engine rather than an export one.
        var doc = Walking(2);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.Cels[1].Frame = null;   // frame 1 holds frame 0
        var hand = Anchors.Declare(doc.Scene, "muzzle");
        Anchors.SetAcross(layer, 0, 1, hand.Id, new AnchorPoint(50, 50));

        var result = SpriteSheetExporter.Export(doc, Path_("held.png"));

        var frames = Meta(result).GetProperty("frames").EnumerateArray().ToList();
        Assert.All(frames, f => Assert.True(
            f.GetProperty("anchors").TryGetProperty("muzzle", out _),
            "the held frame lost its anchor"));
    }

    [Fact]
    public void PackingDoesNotMoveAnAnchorRelativeToItsCell()
    {
        // The same guard the pivot has, for the same reason: a new layout is
        // exactly what quietly breaks a cell-relative offset.
        var doc = Walking(4);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var hand = Anchors.Declare(doc.Scene, "hand");
        Anchors.SetAcross(layer, 0, 4, hand.Id, new AnchorPoint(70, 60));

        static (double X, double Y) AnchorOf(JsonElement meta) =>
            meta.GetProperty("frames")[0].GetProperty("anchors").GetProperty("hand") is { } a
                ? (a.GetProperty("x").GetDouble(), a.GetProperty("y").GetDouble())
                : (0, 0);

        var grid = SpriteSheetExporter.Export(
            doc, Path_("ag.png"), new SpriteSheetOptions { Trim = SpriteTrim.Union });
        var packed = SpriteSheetExporter.Export(
            doc, Path_("ap.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.Union, Pack = SpritePack.Skyline });

        Assert.Equal(AnchorOf(Meta(grid)), AnchorOf(Meta(packed)));
    }

    // ---- background omission -----------------------------------------------------

    /// <summary>
    /// Adds a layer flooded over the whole canvas, the way an artist does when they
    /// want to see the line against something.
    /// </summary>
    private static Layer AddFloodedLayer(Doc doc, string name, int frames, double inset = 0)
    {
        var layer = new Layer { Name = name };
        for (var i = 0; i < frames; i++)
        {
            var frame = new PaintedFrame();
            frame.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#808080",
                Points =
                [
                    new StrokePoint(-10 + inset, -10 + inset, 1),
                    new StrokePoint(doc.Scene.Width + 10 - inset, -10 + inset, 1),
                    new StrokePoint(doc.Scene.Width + 10 - inset, doc.Scene.Height + 10 - inset, 1),
                    new StrokePoint(-10 + inset, doc.Scene.Height + 10 - inset, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
            layer.Cels.Add(new Cel { Frame = frame });
        }
        // Under the artwork, which is where an artist puts it.
        doc.Scene.Layers.Insert(1, layer);
        return layer;
    }

    private static long OpaquePixels(string pngPath)
    {
        using var data = SKData.Create(pngPath);
        using var image = SKImage.FromEncodedData(data);
        var info = new SKImageInfo(image!.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);

        using var pixels = bitmap.PeekPixels();
        var span = pixels.GetPixelSpan();
        var count = 0L;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (span[y * pixels.RowBytes + x * 4 + 3] > 250) count++;
            }
        }
        return count;
    }

    [Fact]
    public void TheDefaultExportIsByteIdenticalToBeforeBackgroundHandlingExisted()
    {
        // The promise that lets this ship. Somebody's importer, somebody's diff and
        // somebody's build all depend on the default not moving, so this is a byte
        // comparison rather than a shape check — "the same layout" and "the same file"
        // are different claims and only the second keeps their pipeline working.
        var doc = Walking(4, paperColor: "#ffffff");
        AddFloodedLayer(doc, "Grey", 4);

        var implicitDefault = SpriteSheetExporter.Export(doc, Path_("bgd.png"));
        var stated = SpriteSheetExporter.Export(
            doc, Path_("bgs.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.PaperOnly });

        Assert.Equal(File.ReadAllBytes(implicitDefault.SheetPath), File.ReadAllBytes(stated.SheetPath));
        // And the flooded layer is still in it, because PaperOnly is deliberately not
        // the whole feature.
        Assert.DoesNotContain(
            implicitDefault.OmittedLayers, o => o.Signal == BackgroundSignal.FullCanvasFill);
    }

    [Fact]
    public void AFloodedLayerIsOmittedUnderDetectionAndKeptWithoutIt()
    {
        // The case that started this: a character with a grey layer added for
        // visibility. The measurement is the sheet's opaque pixel count — with the
        // flood in, every cell is solid; with it out, only the character is.
        var doc = Walking(4, paperColor: "#ffffff");
        AddFloodedLayer(doc, "Grey", 4);

        var kept = SpriteSheetExporter.Export(
            doc, Path_("kept.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.PaperOnly });
        var dropped = SpriteSheetExporter.Export(
            doc, Path_("dropped.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.Detected });

        var keptPixels = OpaquePixels(kept.SheetPath);
        var droppedPixels = OpaquePixels(dropped.SheetPath);

        // Both numbers printed, because "fewer pixels" with only one of them shown is
        // the assertion that passes on a build where the layer was never drawn at all.
        Assert.True(
            droppedPixels < keptPixels,
            $"kept {keptPixels} opaque px, dropped {droppedPixels} — omission changed nothing");
        // And the character is still there: a fix that exported an empty sheet would
        // also satisfy the line above.
        Assert.True(droppedPixels > 0, "the whole sheet came out empty");

        var omission = Assert.Single(
            dropped.OmittedLayers, o => o.Signal == BackgroundSignal.FullCanvasFill);
        Assert.Equal("Grey", omission.Name);
    }

    [Fact]
    public void APinnedInLayerSurvivesDetectionEvenThoughItFillsTheCanvas()
    {
        // The backdrop escape hatch, end to end: the background *is* the asset.
        var doc = Walking(2, paperColor: "#ffffff");
        AddFloodedLayer(doc, "Sky", 2).OmitFromExport = false;

        var result = SpriteSheetExporter.Export(
            doc, Path_("backdrop.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.Detected });

        Assert.DoesNotContain(result.OmittedLayers, o => o.Name == "Sky");
        // Nor is it warned about — the artist already answered the question.
        Assert.DoesNotContain(result.SuspectedBackgrounds, s => s.Name == "Sky");
    }

    [Fact]
    public void APinnedOutLayerGoesEvenUnderPaperOnly()
    {
        // The reference photo, the colour check, the note to self.
        var doc = Walking(2);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.OmitFromExport = true;

        var result = SpriteSheetExporter.Export(doc, Path_("pinnedout.png"));

        var omission = Assert.Single(
            result.OmittedLayers, o => o.Signal == BackgroundSignal.Pinned);
        Assert.Equal(layer.Name, omission.Name);
    }

    [Fact]
    public void ALayerThatFillsTheCanvasOnOneFrameOnlyIsNotABackground()
    {
        // The false positive that would hurt most: a flash, a whip pan, an impact
        // frame that goes full-bleed for two frames. Detection requires *every*
        // drawing to cover the canvas, and this is what that rule is for.
        var doc = Walking(4, paperColor: "#ffffff");
        var flash = AddFloodedLayer(doc, "Flash", 4);
        // Frame 2 is a small shape rather than a flood, so the layer is art.
        if (flash.Cels[2].Frame is PaintedFrame p)
        {
            p.Strokes.Clear();
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#ffffff",
                Points =
                [
                    new StrokePoint(10, 10, 1), new StrokePoint(30, 10, 1),
                    new StrokePoint(30, 30, 1), new StrokePoint(10, 30, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }

        var result = SpriteSheetExporter.Export(
            doc, Path_("flash.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.Detected });

        Assert.DoesNotContain(result.OmittedLayers, o => o.Name == "Flash");
    }

    [Fact]
    public void AHeldFloodIsStillRecognisedAcrossItsHolds()
    {
        // A background is usually one drawing exposed for the whole sequence, which is
        // the case a per-cel check would get wrong by finding nulls.
        var doc = Walking(4, paperColor: "#ffffff");
        var grey = AddFloodedLayer(doc, "Grey", 4);
        for (var i = 1; i < grey.Cels.Count; i++) grey.Cels[i].Frame = null;

        var result = SpriteSheetExporter.Export(
            doc, Path_("held.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.Detected });

        Assert.Contains(result.OmittedLayers, o => o.Name == "Grey");
    }

    [Fact]
    public void EverythingPutsThePaperBackIn()
    {
        // Not reachable before this existed: the exporter always dropped the paper
        // layer, so a backdrop asset could not be exported at all.
        var doc = Walking(2, paperColor: "#3060a0");

        var without = SpriteSheetExporter.Export(
            doc, Path_("nopaper.png"), new SpriteSheetOptions { Trim = SpriteTrim.None });
        var with = SpriteSheetExporter.Export(
            doc, Path_("paper.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.None, Background = BackgroundHandling.Everything });

        var withoutPixels = OpaquePixels(without.SheetPath);
        var withPixels = OpaquePixels(with.SheetPath);

        Assert.True(
            withPixels > withoutPixels,
            $"paper omitted {withoutPixels} px, paper included {withPixels} — no difference");
        Assert.Contains(without.OmittedLayers, o => o.Signal == BackgroundSignal.Paper);
        Assert.DoesNotContain(with.OmittedLayers, o => o.Signal == BackgroundSignal.Paper);
    }

    [Fact]
    public void ALayerNamedLikeABackgroundIsReportedRatherThanRemoved()
    {
        // The weak signal advises and never acts. A rule that dropped this would
        // eventually ship a sheet with a layer quietly missing.
        var doc = Walking(3);
        doc.Scene.Layers.First(l => !l.IsBackground).Name = "Backdrop sketch";

        var result = SpriteSheetExporter.Export(
            doc, Path_("named.png"),
            new SpriteSheetOptions { Background = BackgroundHandling.Detected });

        Assert.DoesNotContain(result.OmittedLayers, o => o.Name == "Backdrop sketch");
        Assert.Contains(result.SuspectedBackgrounds, s => s.Name == "Backdrop sketch");
    }

    [Fact]
    public void AHiddenLayerIsReportedSoItsAbsenceHasAnAnswer()
    {
        var doc = Walking(2);
        var extra = AddFloodedLayer(doc, "Shadow", 2);
        extra.Visible = false;

        var result = SpriteSheetExporter.Export(doc, Path_("hidden.png"));

        Assert.Contains(
            result.OmittedLayers, o => o.Name == "Shadow" && o.Signal == BackgroundSignal.Hidden);
    }

    // ---- P5c: collision shapes in the sidecar -------------------------------------

    [Fact]
    public void ADocumentWithNoShapesWritesNoShapeKey()
    {
        var result = SpriteSheetExporter.Export(Walking(3), Path_("noshape.png"));
        Assert.DoesNotContain("\"shapes\"", File.ReadAllText(result.MetadataPath));
    }

    [Fact]
    public void AShapeIsExportedWithItsRoleAndInsideTheCell()
    {
        // Inside the cell like the pivot and the anchors, and here the consequence is
        // not cosmetic: a collider that shifted because one frame's ink happened to be
        // tighter is a gameplay bug with no visible cause in the drawing.
        var doc = Walking(4);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var body = CollisionShapes.Declare(doc.Scene, "body");
        var sword = CollisionShapes.Declare(doc.Scene, "sword", ShapeRole.Hitbox);
        CollisionShapes.SetAcross(layer, 0, 4, body.Id, new ShapeBox(45, 35, 25, 50));
        CollisionShapes.SetAcross(layer, 0, 4, sword.Id, new ShapeBox(70, 40, 30, 10));

        var result = SpriteSheetExporter.Export(
            doc, Path_("shapes.png"), new SpriteSheetOptions { Trim = SpriteTrim.Union });

        var frames = Meta(result).GetProperty("frames").EnumerateArray().ToList();
        Assert.Equal(4, frames.Count);
        foreach (var frame in frames)
        {
            var shapes = frame.GetProperty("shapes").EnumerateArray().ToList();
            Assert.Equal(2, shapes.Count);
            // Declaration order, so the same document exports the same order.
            Assert.Equal("body", shapes[0].GetProperty("name").GetString());
            Assert.Equal("hurtbox", shapes[0].GetProperty("role").GetString());
            Assert.Equal("sword", shapes[1].GetProperty("name").GetString());
            Assert.Equal("hitbox", shapes[1].GetProperty("role").GetString());

            var source = frame.GetProperty("spriteSourceSize");
            Assert.Equal(
                45 - source.GetProperty("x").GetInt32(),
                shapes[0].GetProperty("x").GetDouble());
            Assert.Equal(
                35 - source.GetProperty("y").GetInt32(),
                shapes[0].GetProperty("y").GetDouble());
            // The size is not cell-relative and must not be shifted with the origin.
            Assert.Equal(25, shapes[0].GetProperty("w").GetDouble());
            Assert.Equal(50, shapes[0].GetProperty("h").GetDouble());
        }
    }

    [Fact]
    public void AShapeOnlyAppearsOnTheFramesItWasPlacedOn()
    {
        // Absence is the off state, and this is what carries that through to the
        // file: a hitbox on the two contact frames of a four-frame swing.
        var doc = Walking(4);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var sword = CollisionShapes.Declare(doc.Scene, "sword", ShapeRole.Hitbox);
        CollisionShapes.SetAcross(layer, 1, 2, sword.Id, new ShapeBox(60, 40, 30, 10));

        var result = SpriteSheetExporter.Export(doc, Path_("active.png"));

        var frames = Meta(result).GetProperty("frames").EnumerateArray().ToList();
        for (var i = 0; i < 4; i++)
        {
            var present = frames[i].TryGetProperty("shapes", out var s) && s.GetArrayLength() > 0;
            Assert.Equal(i is 1 or 2, present);
        }
    }

    [Fact]
    public void AShapeOnAHeldDrawingIsExportedOnEveryFrameItShows()
    {
        var doc = Walking(2);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.Cels[1].Frame = null;   // frame 1 holds frame 0
        var body = CollisionShapes.Declare(doc.Scene, "body");
        CollisionShapes.SetAcross(layer, 0, 1, body.Id, new ShapeBox(50, 50, 10, 10));

        var result = SpriteSheetExporter.Export(doc, Path_("heldshape.png"));

        var frames = Meta(result).GetProperty("frames").EnumerateArray().ToList();
        Assert.All(frames, f => Assert.True(
            f.TryGetProperty("shapes", out var s) && s.GetArrayLength() == 1,
            "the held frame lost its hurtbox"));
    }

    [Fact]
    public void PackingDoesNotMoveAShapeRelativeToItsCell()
    {
        var doc = Walking(4);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var body = CollisionShapes.Declare(doc.Scene, "body");
        CollisionShapes.SetAcross(layer, 0, 4, body.Id, new ShapeBox(45, 35, 25, 50));

        static (double X, double Y) BoxOf(JsonElement meta) =>
            meta.GetProperty("frames")[0].GetProperty("shapes")[0] is { } s
                ? (s.GetProperty("x").GetDouble(), s.GetProperty("y").GetDouble())
                : (0, 0);

        var grid = SpriteSheetExporter.Export(
            doc, Path_("sg.png"), new SpriteSheetOptions { Trim = SpriteTrim.Union });
        var packed = SpriteSheetExporter.Export(
            doc, Path_("sp.png"),
            new SpriteSheetOptions { Trim = SpriteTrim.Union, Pack = SpritePack.Skyline });

        Assert.Equal(BoxOf(Meta(grid)), BoxOf(Meta(packed)));
    }

    // ---- P5d: tags, clips and events ---------------------------------------------

    [Fact]
    public void ADocumentWithNoTagsOrEventsWritesNeitherKey()
    {
        var json = File.ReadAllText(
            SpriteSheetExporter.Export(Walking(3), Path_("bare.png")).MetadataPath);

        Assert.DoesNotContain("\"frameTags\"", json);
        Assert.DoesNotContain("\"events\"", json);
    }

    [Fact]
    public void ATagIsExportedAsAClipInTheEstablishedShape()
    {
        // Aseprite's own key and field names, because every engine importer that
        // reads a sprite-sheet sidecar already looks there. An animation clip *is*
        // a named frame range, so there is no separate clip record to build.
        var doc = Walking(8);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "walk", Start = 0, End = 3 },
            new AnimationTag { Name = "run", Start = 4, End = 7, Direction = TagDirection.PingPong, Loop = false },
        ];

        var tags = Meta(SpriteSheetExporter.Export(doc, Path_("tags.png")))
            .GetProperty("meta").GetProperty("frameTags").EnumerateArray().ToList();

        Assert.Equal(2, tags.Count);
        Assert.Equal("walk", tags[0].GetProperty("name").GetString());
        Assert.Equal(0, tags[0].GetProperty("from").GetInt32());
        Assert.Equal(3, tags[0].GetProperty("to").GetInt32());
        Assert.Equal("forward", tags[0].GetProperty("direction").GetString());
        Assert.True(tags[0].GetProperty("loop").GetBoolean());

        Assert.Equal("pingpong", tags[1].GetProperty("direction").GetString());
        Assert.False(tags[1].GetProperty("loop").GetBoolean());
    }

    [Fact]
    public void ATagThatRanPastTheEndIsShortenedRatherThanLost()
    {
        // Somebody shortened the animation. The clip still names a real range, and
        // losing it entirely would be the worse answer.
        var doc = Walking(4);
        doc.Scene.Tags = [new AnimationTag { Name = "walk", Start = 0, End = 99 }];

        var tag = Meta(SpriteSheetExporter.Export(doc, Path_("clamp.png")))
            .GetProperty("meta").GetProperty("frameTags")[0];

        Assert.Equal(0, tag.GetProperty("from").GetInt32());
        Assert.Equal(3, tag.GetProperty("to").GetInt32());
    }

    [Fact]
    public void ATagEntirelyPastTheEndIsDropped()
    {
        var doc = Walking(4);
        doc.Scene.Tags = [new AnimationTag { Name = "gone", Start = 10, End = 20 }];

        var json = File.ReadAllText(SpriteSheetExporter.Export(doc, Path_("gone.png")).MetadataPath);

        Assert.DoesNotContain("\"frameTags\"", json);
    }

    [Fact]
    public void OnlyMarkersMarkedAsEventsAreExported()
    {
        // Most markers are notes to the animator — "contact", "check the hand" —
        // and exporting those would fill an AnimationClip with callbacks nothing
        // handles. So it is opt-in, and this is the test that keeps it that way.
        var doc = Walking(6);
        doc.Scene.Markers =
        [
            new FrameMarker { Frame = 1, Label = "check the hand" },
            new FrameMarker { Frame = 3, Label = "OnFootstep", IsEvent = true },
        ];

        var meta = Meta(SpriteSheetExporter.Export(doc, Path_("events.png"))).GetProperty("meta");

        var events = meta.GetProperty("events").EnumerateArray().ToList();
        var single = Assert.Single(events);
        Assert.Equal("OnFootstep", single.GetProperty("name").GetString());
        Assert.Equal(3, single.GetProperty("frame").GetInt32());
    }

    [Fact]
    public void AnEventPastTheEndIsNotExported()
    {
        var doc = Walking(3);
        doc.Scene.Markers = [new FrameMarker { Frame = 40, Label = "OnLate", IsEvent = true }];

        Assert.DoesNotContain(
            "\"events\"",
            File.ReadAllText(SpriteSheetExporter.Export(doc, Path_("late.png")).MetadataPath));
    }
}
