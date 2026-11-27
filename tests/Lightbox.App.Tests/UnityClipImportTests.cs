using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Import.Unity;
using System.Security.Cryptography;
using SkiaSharp;
using static Lightbox.Raster.Tests.UnityFixture;

namespace Lightbox.App.Tests;

/// <summary>
/// Opening a game clip as a document (Q300, step 2 of docs/DESIGN-unity-link.md):
/// frames lined up by pivot, the placeholder on a locked layer out of export, a
/// drawing layer timed like it, and a record write-back can invert exactly.
/// </summary>
public class UnityClipImportTests
{
    private static readonly string Atlas = Guid(2);

    /// <summary>A cell to paint into an atlas: Unity rect, fill colour, bottom-centre pivot.</summary>
    private sealed record Paint(string Name, long Id, int X, int Y, int W, int H, SKColor Fill);

    // The foot marker: one pixel just above a bottom-centre pivot.
    private static readonly SKColor Foot = new(0, 255, 0, 255);

    /// <summary>
    /// An atlas PNG with each cell filled and a green foot pixel at the middle of
    /// its bottom row — exactly where a bottom-centre pivot puts the feet.
    /// </summary>
    private static byte[] AtlasPng(int w, int h, IEnumerable<Paint> cells)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.Transparent);
        foreach (var c in cells)
        {
            var top = h - c.Y - c.H; // Unity's rect is bottom-left
            for (var y = top; y < top + c.H; y++)
                for (var x = c.X; x < c.X + c.W; x++)
                    bmp.SetPixel(x, y, c.Fill);
            bmp.SetPixel(c.X + c.W / 2, top + c.H - 1, Foot);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Project HeroProject(params Key[] keys)
    {
        var p = new Project();
        Paint[] cells =
        [
            new("Hero_1", 201, 0, 0, 40, 60, new SKColor(200, 0, 0, 255)),   // tall, bottom-left of the atlas
            new("Hero_2", 202, 50, 30, 70, 50, new SKColor(0, 0, 200, 255)), // wide, lifted off the bottom
            new("Hero_3", 203, 130, 0, 21, 33, new SKColor(200, 200, 0, 255)),
        ];
        p.Write("Assets/Hero.png", AtlasPng(160, 100, cells));
        p.Write("Assets/Hero.png.meta", MultipleMeta(Atlas, 100,
            cells.Select(c => new Sprite(c.Name, c.Id, c.X, c.Y, c.W, c.H, Alignment: 7)).ToArray()));
        p.Clip("Assets/Hero/Run.anim", Anim("Run", 12, true, 6 / 12.0,
            [new Track("", keys.Length > 0 ? keys : [new Key(0, Atlas, 201), new Key(2 / 12.0, Atlas, 202), new Key(3 / 12.0, Atlas, 203)])]), 1);
        return p;
    }

    private static UnityClipImportResult Open(Project p, UnityClipImportOptions? o = null)
    {
        var index = UnityProjectScanner.Scan(p.Root);
        return UnityClipImport.Open(index, index.Clips.Single(), o);
    }

    private static SKBitmap Pixels(Frame f) => Lightbox.Raster.PngCodec.Decode(f.PngBase64!);

    // ---- the document's shape ------------------------------------------------

    [Fact]
    public void TheClipBecomesADocumentAtItsOwnRateAndLength()
    {
        using var p = HeroProject();
        var doc = Open(p).Document;

        Assert.Equal("Run", doc.Scene.Name);
        Assert.Equal(12, doc.Scene.Fps);
        Assert.Equal(6, doc.Scene.FrameCount);
        Assert.True(doc.Scene.TransparentBackground);
        Assert.All(doc.Scene.Layers, l => Assert.Equal(6, l.Cels.Count));
    }

    [Fact]
    public void ThePlaceholderIsLockedDimmedAndLeftOutOfExport()
    {
        using var p = HeroProject();
        var doc = Open(p).Document;

        var placeholder = doc.Scene.Layers[0];
        Assert.Equal("Placeholder", placeholder.Name);
        Assert.True(placeholder.Locked);
        Assert.True(placeholder.IsOmittedFromExport);
        Assert.True(placeholder.Opacity < 1);
    }

    [Fact]
    public void TheArtistDrawsOnAnEmptyLayerOnTopTimedLikeThePlaceholder()
    {
        using var p = HeroProject();
        var doc = Open(p).Document;

        var drawing = doc.Scene.Layers[^1];
        Assert.Equal("Drawing", drawing.Name);
        Assert.False(drawing.Locked);
        Assert.Null(drawing.OmitFromExport);
        Assert.Equal(drawing.Id, doc.ActiveLayerId);
        // Keys at frames 0, 2 and 3: a drawing there, holds between.
        Assert.Equal([true, false, true, true, false, false], drawing.Cels.Select(c => c.Frame is not null));
        Assert.Equal(
            doc.Scene.Layers[0].Cels.Select(c => c.Frame is not null),
            drawing.Cels.Select(c => c.Frame is not null));
        Assert.All(drawing.Cels.Where(c => c.Frame is not null), c => Assert.Null(c.Frame!.PngBase64));
    }

    // ---- pixels and alignment ------------------------------------------------

    [Fact]
    public void CellsOfDifferentSizesLineUpByPivot()
    {
        // Three cells, three sizes, one lifted off the atlas's bottom edge: every
        // foot pixel must land on the same canvas pixel, just above the pivot.
        using var p = HeroProject();
        var doc = Open(p).Document;
        var pivot = doc.Scene.Pivot!;
        var drawings = doc.Scene.Layers[0].Cels.Select(c => c.Frame).OfType<Frame>().ToList();

        Assert.Equal(3, drawings.Count);
        foreach (var f in drawings)
        {
            using var bmp = Pixels(f);
            var foot = bmp.GetPixel((int)pivot.X, (int)pivot.Y - 1);
            Assert.Equal(Foot, foot);
        }
    }

    [Fact]
    public void ACellsPixelsArriveFromTheRightPlaceInTheAtlas()
    {
        // Unity measures rects from the bottom; reading from the top by mistake
        // takes the wrong pixels and nothing else would notice.
        using var p = HeroProject();
        var doc = Open(p).Document;
        var record = doc.GameSource!.Tracks[0].Frames;

        foreach (var (frame, colour) in new[] { (0, new SKColor(200, 0, 0, 255)), (2, new SKColor(0, 0, 200, 255)), (3, new SKColor(200, 200, 0, 255)) })
        {
            using var bmp = Pixels(doc.Scene.Layers[0].Cels[frame].Frame!);
            var r = record.Single(f => f.Start == frame);
            var (ox, oy) = (r.CanvasOffset![0], r.CanvasOffset[1]);
            Assert.Equal(colour, bmp.GetPixel(ox, oy));                        // the cell's top-left
            Assert.Equal(colour, bmp.GetPixel(ox + r.Cell![2] - 1, oy));       // its top-right
            Assert.Equal(0, bmp.GetPixel(ox - 1, oy).Alpha);                    // nothing left of it
            Assert.Equal(0, bmp.GetPixel(ox, oy - 1).Alpha);                    // nothing above it
        }
    }

    [Fact]
    public void TheCanvasHoldsEveryFrameWithItsMargin()
    {
        using var p = HeroProject();
        var o = new UnityClipImportOptions { Margin = 5 };
        var doc = Open(p, o).Document;

        foreach (var f in doc.GameSource!.Tracks[0].Frames)
        {
            Assert.True(f.CanvasOffset![0] >= 5 && f.CanvasOffset[1] >= 5);
            Assert.True(f.CanvasOffset[0] + f.Cell![2] <= doc.Scene.Width - 5);
            Assert.True(f.CanvasOffset[1] + f.Cell[3] <= doc.Scene.Height - 5);
        }
        // Tight: some frame touches each margin.
        var frames = doc.GameSource.Tracks[0].Frames;
        Assert.Contains(frames, f => f.CanvasOffset![0] == 5);
        Assert.Contains(frames, f => f.CanvasOffset![1] == 5);
    }

    // ---- timing --------------------------------------------------------------

    [Fact]
    public void ABlankKeyIsAnEmptyDrawingNotAHold()
    {
        using var p = HeroProject(new Key(0, Atlas, 201), new Key(2 / 12.0, null, 0), new Key(4 / 12.0, Atlas, 201));
        var doc = Open(p).Document;
        var cels = doc.Scene.Layers[0].Cels;

        Assert.NotNull(cels[2].Frame);
        Assert.Null(cels[2].Frame!.PngBase64);
        Assert.Null(cels[3].Frame);
        Assert.NotNull(cels[4].Frame!.PngBase64);
    }

    [Fact]
    public void ASpriteShownTwiceIsEncodedOnce()
    {
        using var p = HeroProject(new Key(0, Atlas, 201), new Key(1 / 12.0, Atlas, 202), new Key(2 / 12.0, Atlas, 201));
        var doc = Open(p).Document;
        var cels = doc.Scene.Layers[0].Cels;

        Assert.Same(cels[0].Frame!.PngBase64, cels[2].Frame!.PngBase64);
        Assert.NotSame(cels[0].Frame, cels[2].Frame); // two drawings, one picture
    }

    [Fact]
    public void AClipThatStartsLateHasAnEmptyFirstFrame()
    {
        using var p = HeroProject(new Key(2 / 12.0, Atlas, 201));
        var doc = Open(p).Document;
        Assert.NotNull(doc.Scene.Layers[0].Cels[0].Frame);
        Assert.Null(doc.Scene.Layers[0].Cels[0].Frame!.PngBase64);
    }

    // ---- the record write-back inverts ------------------------------------------

    [Fact]
    public void TheRecordNamesEachFramesCellOffsetAndHash()
    {
        using var p = HeroProject();
        var result = Open(p);
        var src = result.Document.GameSource!;

        Assert.Equal("Assets/Hero/Run.anim", src.Clip);
        Assert.Equal((result.Document.Scene.Width, result.Document.Scene.Height), (src.CanvasWidth, src.CanvasHeight));
        var track = Assert.Single(src.Tracks);
        Assert.Equal(result.Document.Scene.Layers[0].Id, track.PlaceholderLayerId);
        Assert.Equal(result.Document.Scene.Layers[^1].Id, track.DrawingLayerId);

        var second = track.Frames[1];
        Assert.Equal((2, 1, "Hero_2", 202L), (second.Start, second.Length, second.Name, second.Sprite));
        Assert.Equal("Assets/Hero.png", second.TexturePath);
        Assert.Equal([50, 100 - 30 - 50, 70, 50], second.Cell!);

        // The hash is of the cell's own pixels, read independently here.
        using var atlas = SKBitmap.Decode(Path.Combine(p.Root, "Assets", "Hero.png"));
        using var cell = new SKBitmap(new SKImageInfo(70, 50, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < 50; y++)
            for (var x = 0; x < 70; x++)
                cell.SetPixel(x, y, atlas.GetPixel(50 + x, 20 + y));
        Assert.Equal(UnityClipImport.Hash(cell), second.SourceHash);
    }

    [Fact]
    public void TheRecordSurvivesASaveAndADocumentWithoutOneWritesNoKey()
    {
        using var p = HeroProject();
        var doc = Open(p).Document;

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.Equal(DocJson.Serialize(doc), DocJson.Serialize(back));
        Assert.Equal(doc.GameSource!.Tracks[0].Frames[2].SourceHash, back.GameSource!.Tracks[0].Frames[2].SourceHash);

        var plain = DocumentFactory.CreateDoc(64, 64, 12);
        Assert.DoesNotContain("\"gameSource\"", DocJson.Serialize(plain), StringComparison.OrdinalIgnoreCase);
    }

    // ---- what goes wrong --------------------------------------------------------

    [Fact]
    public void AMissingSpriteShowsBlankAndIsNamed()
    {
        using var p = HeroProject(new Key(0, Atlas, 201), new Key(1 / 12.0, Atlas, 999));
        var result = Open(p);

        Assert.Contains(result.Notes, n => n.Contains("could not be used") && n.Contains("shows blank"));
        Assert.Null(result.Document.Scene.Layers[0].Cels[1].Frame!.PngBase64);
    }

    [Fact]
    public void AClipWhoseSpritesAreAllMissingIsRefused()
    {
        using var p = HeroProject(new Key(0, Atlas, 998), new Key(1 / 12.0, Atlas, 999));
        var ex = Assert.Throws<FormatException>(() => Open(p));
        Assert.Contains("none of the clip's sprites", ex.Message);
    }

    [Fact]
    public void AnOverlongClipIsRefusedBeforeAnythingIsBuiltPerFrame()
    {
        // 1e6 seconds at 1000 fps is a billion frames: refuse on the count, not on running out of memory.
        using var p = HeroProject(new Key(0, Atlas, 201), new Key(1e6, Atlas, 202));
        var ex = Assert.Throws<FormatException>(() => Open(p));
        Assert.Contains("frames", ex.Message);
    }

    [Fact]
    public void APivotFarOutsideItsCellIsRefusedRatherThanMakingAHugeCanvas()
    {
        using var p = HeroProject();
        File.WriteAllText(Path.Combine(p.Root, "Assets", "Hero.png.meta"), MultipleMeta(Atlas, 100,
            new Sprite("Hero_1", 201, 0, 0, 40, 60, Alignment: 9, PivotX: 0.5, PivotY: 500),
            new Sprite("Hero_2", 202, 50, 30, 70, 50),
            new Sprite("Hero_3", 203, 130, 0, 21, 33)));

        var ex = Assert.Throws<FormatException>(() => Open(p));
        Assert.Contains("outside its cell", ex.Message);
    }

    [Fact]
    public void ATextureChangedSinceTheScanIsRefusedRatherThanCutWrong()
    {
        using var p = HeroProject();
        var index = UnityProjectScanner.Scan(p.Root);
        index.Resolve(new UnitySpriteRef(Atlas, 201)); // the slicing is read for a 160×100 image…
        p.Write("Assets/Hero.png", AtlasPng(80, 100, [])); // …and the image is then replaced

        var ex = Assert.Throws<FormatException>(() => UnityClipImport.Open(index, index.Clips.Single()));
        Assert.Contains("slicing was read for", ex.Message);
    }

    [Fact]
    public void TwoRenderersGetTwoPlaceholdersAndTheDocumentSaysWhy()
    {
        using var p = HeroProject();
        p.Clip("Assets/Hero/Run.anim", Anim("Run", 12, true, 0.25,
            [new Track("", new Key(0, Atlas, 201)), new Track("Fx", new Key(0, Atlas, 203))]), 1);

        var result = Open(p);

        Assert.Equal(["Placeholder · root", "Placeholder · Fx", "Drawing"], result.Document.Scene.Layers.Select(l => l.Name));
        Assert.Equal(2, result.Document.GameSource!.Tracks.Count);
        Assert.Contains(result.Notes, n => n.Contains("more than one renderer"));
    }

    [Fact]
    public void AFrameRateThatIsNotWholeIsRoundedAndSaysSo()
    {
        using var p = HeroProject();
        p.Clip("Assets/Hero/Run.anim", Anim("Run", 29.97, true, 0.1, [new Track("", new Key(0, Atlas, 201))]), 1);

        var result = Open(p);

        Assert.Equal(30, result.Document.Scene.Fps);
        Assert.Contains(result.Notes, n => n.Contains("29.97"));
    }

    [Fact]
    public void RandomAtlasesComeBackPixelExactAndPivotAligned()
    {
        // Seeded, so a failure reproduces. Random cell sizes, positions and
        // pivots (every alignment, custom ones off the cell included) on random
        // atlases: every cell must arrive pixel for pixel, inside the canvas,
        // with its pivot on the document's pivot.
        var rng = new Random(20261127);
        for (var round = 0; round < 15; round++)
        {
            using var p = new Project();
            var texW = rng.Next(64, 300);
            var texH = rng.Next(64, 300);
            using var atlas = new SKBitmap(new SKImageInfo(texW, texH, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < texH; y++)
                for (var x = 0; x < texW; x++)
                    atlas.SetPixel(x, y, new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 255));

            var sprites = new List<Sprite>();
            for (var k = 0; k < rng.Next(1, 7); k++)
            {
                var w = rng.Next(1, 60);
                var h = rng.Next(1, 60);
                var alignment = rng.Next(10);
                sprites.Add(new Sprite($"S{k}", 1000 + k, rng.Next(0, texW - w + 1), rng.Next(0, texH - h + 1), w, h,
                    alignment, PivotX: rng.NextDouble() * 1.4 - 0.2, PivotY: rng.NextDouble() * 1.4 - 0.2));
            }
            using (var img = SKImage.FromBitmap(atlas))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                p.Write("Assets/A.png", data.ToArray());
            p.Write("Assets/A.png.meta", MultipleMeta(Atlas, 100, sprites.ToArray()));
            p.Clip("Assets/A.anim", Anim("A", 12, true, sprites.Count / 12.0,
                [new Track("", sprites.Select((sp, k) => new Key(k / 12.0, Atlas, sp.Id)).ToArray())]), 1);

            var doc = Open(p).Document;
            var pivot = doc.Scene.Pivot!;
            var frames = doc.GameSource!.Tracks[0].Frames;

            for (var k = 0; k < sprites.Count; k++)
            {
                var f = frames[k];
                var (cx, top, cw, ch) = (f.Cell![0], f.Cell[1], f.Cell[2], f.Cell[3]);
                var (ox, oy) = (f.CanvasOffset![0], f.CanvasOffset[1]);
                var why = $"round {round}, sprite {k}";

                Assert.True(ox >= 0 && oy >= 0 && ox + cw <= doc.Scene.Width && oy + ch <= doc.Scene.Height, why);
                Assert.Equal(pivot.X, ox + UnityClipImport.PivotPixel(f.Pivot![0] * cw));
                Assert.Equal(pivot.Y, oy + UnityClipImport.PivotPixel((1 - f.Pivot[1]) * ch));

                using var placed = Pixels(doc.Scene.Layers[0].Cels[f.Start].Frame!);
                for (var y = 0; y < ch; y++)
                    for (var x = 0; x < cw; x++)
                        if (placed.GetPixel(ox + x, oy + y) != atlas.GetPixel(cx + x, top + y))
                            Assert.Fail($"{why}: pixel ({x}, {y}) of the cell differs");
            }
        }
    }

    // ---- review findings (sensitivity guardian, adversary) -----------------------

    private static void Meta(Project p, params Sprite[] sprites) =>
        File.WriteAllText(Path.Combine(p.Root, "Assets", "Hero.png.meta"), MultipleMeta(Atlas, 100, sprites));

    [Theory]
    [InlineData(1e9)]
    [InlineData(-1e12)]
    public void AnExtremePivotIsRefusedRatherThanWrappingTheCanvas(double pivotX)
    {
        using var p = HeroProject();
        Meta(p, new Sprite("Hero_1", 201, 0, 0, 40, 60, 9, pivotX, 0),
            new Sprite("Hero_2", 202, 50, 30, 70, 50), new Sprite("Hero_3", 203, 130, 0, 21, 33));

        Assert.Contains("far outside its cell", Assert.Throws<FormatException>(() => Open(p)).Message);
    }

    [Fact]
    public void TwoPivotsThatWouldWrapEachOtherAreRefused()
    {
        // The pair that used to open silently as a 30×42 canvas holding 140 of 800 pixels.
        using var p = HeroProject();
        Meta(p, new Sprite("Hero_1", 201, 0, 0, 40, 60, 9, -26843544.5875, 0),
            new Sprite("Hero_2", 202, 50, 30, 70, 50, 9, 1e12, 0), new Sprite("Hero_3", 203, 130, 0, 21, 33));

        Assert.Throws<FormatException>(() => Open(p));
    }

    [Fact]
    public void TheCanvasIsRefusedBeforeAnyPixelIsDecoded()
    {
        // The image data is garbage: a refusal that needed to decode would say so.
        using var p = HeroProject();
        // 25 × 40 px puts this pivot 1 000 px off its cell: inside the pivot bound,
        // but the canvas it needs is over the 1 000 allowed.
        Meta(p, new Sprite("Hero_1", 201, 0, 0, 40, 60, 9, 25, 0),
            new Sprite("Hero_2", 202, 50, 30, 70, 50), new Sprite("Hero_3", 203, 130, 0, 21, 33));
        var png = File.ReadAllBytes(Path.Combine(p.Root, "Assets", "Hero.png"));
        for (var i = 40; i < png.Length - 12; i++) png[i] = 0x5A;
        p.Write("Assets/Hero.png", png);

        var ex = Assert.Throws<FormatException>(() => Open(p, new UnityClipImportOptions { MaxCanvasSide = 1000 }));
        Assert.Contains("canvas", ex.Message);
    }

    [Fact]
    public void TheCellsTotalSizeIsBudgeted()
    {
        using var p = HeroProject();
        var ex = Assert.Throws<FormatException>(() => Open(p, new UnityClipImportOptions { MaxCellPixels = 1000 }));
        Assert.Contains("pixels", ex.Message);
    }

    [Fact]
    public void ADamagedAtlasIsAFormatError()
    {
        using var p = HeroProject();
        var path = Path.Combine(p.Root, "Assets", "Hero.png");
        var png = File.ReadAllBytes(path);
        png[png.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, png);

        Assert.Throws<FormatException>(() => Open(p));
    }

    [Fact]
    public void AnAtlasDeletedSinceTheScanIsAFormatError()
    {
        using var p = HeroProject();
        var index = UnityProjectScanner.Scan(p.Root);
        index.Resolve(new UnitySpriteRef(Atlas, 201));
        File.Delete(Path.Combine(p.Root, "Assets", "Hero.png"));

        Assert.Throws<FormatException>(() => UnityClipImport.Open(index, index.Clips.Single()));
    }

    private static void GreyAtlas(Project p)
    {
        // Grey PNGs are not read row by row (Q301), so this takes the whole-decode path.
        using var bmp = new SKBitmap(new SKImageInfo(160, 100, SKColorType.Gray8, SKAlphaType.Opaque));
        bmp.Erase(new SKColor(90, 90, 90));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        p.Write("Assets/Hero.png", data.ToArray());
    }

    [Fact]
    public void APngTheRowReaderDoesNotHandleOpensAsReferenceWithoutAHash()
    {
        using var p = HeroProject();
        GreyAtlas(p);

        var result = Open(p);

        Assert.Contains(result.Notes, n => n.Contains("write-back will not rewrite them"));
        Assert.All(result.Document.GameSource!.Tracks[0].Frames, f => Assert.Null(f.SourceHash));
        using var bmp = Pixels(result.Document.Scene.Layers[0].Cels[0].Frame!);
        var r = result.Document.GameSource.Tracks[0].Frames[0];
        Assert.Equal(90, bmp.GetPixel(r.CanvasOffset![0], r.CanvasOffset[1]).Red);
    }

    [Fact]
    public void AWholeDecodeIsBudgeted()
    {
        using var p = HeroProject();
        GreyAtlas(p);
        var ex = Assert.Throws<FormatException>(() => Open(p, new UnityClipImportOptions { MaxWholeDecodePixels = 1000 }));
        Assert.Contains("decoded whole", ex.Message);
    }

    [Fact]
    public void OpeningAClipChangesNothingInTheGameProject()
    {
        using var p = HeroProject();
        static Dictionary<string, (string, DateTime)> Snapshot(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
                f => f, f => (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), File.GetLastWriteTimeUtc(f)));
        var before = Snapshot(p.Root);

        Open(p);

        Assert.Equal(before, Snapshot(p.Root));
    }

    [Fact]
    public void ARendererWhoseKeysStopEarlyStopsShowingThere()
    {
        using var p = HeroProject();
        p.Clip("Assets/Hero/Run.anim", Anim("Run", 12, true, 0.1,
            [new Track("", new Key(0, Atlas, 201)), new Track("Fx", new Key(0, Atlas, 203), new Key(0.5, Atlas, 202))]), 1);

        var doc = Open(p).Document;
        var root = doc.GameSource!.Tracks[0].Frames.Single();
        var end = root.Start + root.Length;

        Assert.True(end < doc.Scene.FrameCount);
        Assert.NotNull(doc.Scene.Layers[0].Cels[end].Frame);
        Assert.Null(doc.Scene.Layers[0].Cels[end].Frame!.PngBase64);
        Assert.NotNull(doc.Scene.Layers[^1].Cels[end].Frame); // the drawing layer stops too
    }

    [Fact]
    public void TheRowsDecodedToReachACellAreBudgeted()
    {
        using var p = HeroProject();
        var ex = Assert.Throws<FormatException>(() => Open(p, new UnityClipImportOptions { MaxDecodedBytes = 1000 }));
        Assert.Contains("lowest cell", ex.Message);
    }
}
