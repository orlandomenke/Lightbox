using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Lightbox.Import;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Procreate brushes: a <c>.brushset</c> of many and a <c>.brush</c> of one.
/// </summary>
/// <remarks>
/// <para>
/// Every archive here is built by the test, byte by byte, from the layout the
/// design note describes (<c>docs/DESIGN-procreate-import.md</c>). No brush pack is
/// in the repository: a pack is its author's work, and the public repo is not
/// where it goes (SENSITIVITY L4).
/// </para>
/// <para>
/// The owner's own file is the one real case. Point
/// <c>LIGHTBOX_PROCREATE_SAMPLE</c> at a <c>.brushset</c> and
/// <see cref="TheOwnersOwnSetComesAcross"/> reads it and prints what it found;
/// without the variable that test has nothing to read and says so.
/// </para>
/// </remarks>
public class ProcreateBrushImportTests(ITestOutputHelper output)
{
    // ---- what comes across ----------------------------------------------------------

    [Fact]
    public void ABrushsetBringsEachBrushWithItsNameTipAndBasics()
    {
        var set = Zip(
            ("B-SECOND/Brush.archive", Archive("Round Soft", paintSize: 0.5, maxSize: 3.0, plotSpacing: 0.25, maxOpacity: 0.6)),
            ("A-FIRST/Brush.archive", Archive("Dry Bristle", paintSize: 0.25, maxSize: 2.0, plotSpacing: 0.004, maxOpacity: 1.0)),
            ("A-FIRST/Shape.png", DiscPng(64)),
            ("A-FIRST/QuickLook/Thumbnail.png", DiscPng(8)),
            ("brushset.plist", Manifest("Test set", "A-FIRST", "B-SECOND")));

        var brushes = BrushImport.Read("Test.brushset", set);

        // The manifest's order, not the archive's: it is the order the artist arranged.
        Assert.Equal(["Dry Bristle", "Round Soft"], brushes.Select(b => b.Name));

        var dry = brushes[0];
        using var tip = SKBitmap.Decode(Convert.FromBase64String(dry.TipPngBase64!));
        output.WriteLine($"{dry.Name}: {dry.SizePx} px, spacing {dry.Spacing}, opacity {dry.Opacity}, tip {tip.Width}x{tip.Height}");
        Assert.Equal(64, tip.Width);
        Assert.Equal(255, tip.GetPixel(32, 32).Alpha);   // white in the shape is paint
        Assert.Equal(0, tip.GetPixel(1, 1).Alpha);       // black is none
        Assert.Equal(50, dry.SizePx, 6);                 // 0.25 of a 2.0 limit, at 100 px a unit
        Assert.Equal(0.02, dry.Spacing, 6);              // denser than the engine goes: its floor
        Assert.Equal(1, dry.Opacity, 6);

        var round = brushes[1];
        Assert.Null(round.TipPngBase64);                 // its shape lives inside Procreate, not the file
        Assert.Equal(150, round.SizePx, 6);
        Assert.Equal(0.25, round.Spacing, 6);
        Assert.Equal(0.6, round.Opacity, 5);
    }

    [Fact]
    public void ASingleBrushFileIsOneBrush()
    {
        var one = Zip(
            ("Brush.archive", Archive("Solo", paintSize: 1, maxSize: 0.3, plotSpacing: 0.1, maxOpacity: 1)),
            ("Shape.png", DiscPng(32)));

        var brush = Assert.Single(BrushImport.Read("Solo.brush", one));

        Assert.Equal("Solo", brush.Name);
        Assert.Equal(30, brush.SizePx, 4);
        Assert.NotNull(brush.TipPngBase64);
    }

    /// <summary>
    /// The other legal encoding of the same settings: two-byte references, four-byte
    /// offsets, eight-byte numbers. A real file uses the narrow one; nothing says the
    /// next version of Procreate will.
    /// </summary>
    [Fact]
    public void TheWideEncodingReadsTheSame()
    {
        var narrow = Zip(("Brush.archive", Archive("Same", paintSize: 0.5, maxSize: 3, plotSpacing: 0.25, maxOpacity: 0.5)));
        var wide = Zip(("Brush.archive", Archive("Same", paintSize: 0.5, maxSize: 3, plotSpacing: 0.25, maxOpacity: 0.5, wide: true)));

        var a = Assert.Single(BrushImport.Read("a.brush", narrow));
        var b = Assert.Single(BrushImport.Read("b.brush", wide));

        output.WriteLine($"narrow {a.SizePx} px / {a.Spacing} / {a.Opacity}; wide {b.SizePx} px / {b.Spacing} / {b.Opacity}");
        Assert.Equal(150, a.SizePx, 4);
        Assert.Equal(a.SizePx, b.SizePx, 4);
        Assert.Equal(a.Spacing, b.Spacing, 6);
        Assert.Equal(a.Opacity, b.Opacity, 6);
    }

    /// <summary>
    /// A name with a curly apostrophe is stored as UTF-16, not ASCII — the owner's set
    /// has one — and a long one takes its length from a separate integer.
    /// </summary>
    [Theory]
    [InlineData("Thick\u2019n\u2019Thin")]
    [InlineData("Pinceau \u00e0 l\u2019huile tr\u00e8s sec")]
    [InlineData("A plain name that is longer than fifteen characters")]
    public void ANameKeepsItsCharacters(string name)
    {
        var one = Zip(("Brush.archive", Archive(name)));

        Assert.Equal(name, Assert.Single(BrushImport.Read("x.brush", one)).Name);
    }

    /// <summary>
    /// A manifest that lists a folder twice, and one that is not there, neither
    /// repeats a brush nor loses the one it forgot to mention.
    /// </summary>
    [Fact]
    public void AManifestThatRepeatsOrInventsFoldersChangesNothingButTheOrder()
    {
        var set = Zip(
            ("A/Brush.archive", Archive("A")),
            ("B/Brush.archive", Archive("B")),
            ("C/Brush.archive", Archive("C")),
            ("brushset.plist", Manifest("Odd", "B", "B", "NOT-THERE", "A")));

        Assert.Equal(["B", "A", "C"], BrushImport.Read("x.brushset", set).Select(b => b.Name));
    }

    /// <summary>
    /// A shape drawn on transparency rather than on black: what is not there is not
    /// paint, whatever colour the transparent pixels happen to hold.
    /// </summary>
    [Fact]
    public void AShapeOnATransparentBackgroundIsReadByWhatIsVisible()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(33, 33, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(new SKColor(255, 255, 255, 0));          // white, and invisible
        for (var y = 10; y < 23; y++)
        {
            for (var x = 10; x < 23; x++) bitmap.SetPixel(x, y, SKColors.White);
        }
        using var image = SKImage.FromBitmap(bitmap);
        var set = Zip(
            ("A/Brush.archive", Archive("On glass")),
            ("A/Shape.png", image.Encode(SKEncodedImageFormat.Png, 100).ToArray()));

        var brush = Assert.Single(BrushImport.Read("x.brushset", set));

        using var tip = SKBitmap.Decode(Convert.FromBase64String(brush.TipPngBase64!));
        output.WriteLine($"centre alpha {tip.GetPixel(16, 16).Alpha}, corner alpha {tip.GetPixel(1, 1).Alpha}");
        Assert.Equal(255, tip.GetPixel(16, 16).Alpha);
        Assert.Equal(0, tip.GetPixel(1, 1).Alpha);
    }

    /// <summary>
    /// A width that is not a multiple of four, which is where a row's stride and its
    /// width part company.
    /// </summary>
    [Fact]
    public void AnOddSizedShapeKeepsItsRowsStraight()
    {
        var set = Zip(
            ("A/Brush.archive", Archive("Odd")),
            ("A/Shape.png", DiscPng(61)));

        var brush = Assert.Single(BrushImport.Read("x.brushset", set));

        using var tip = SKBitmap.Decode(Convert.FromBase64String(brush.TipPngBase64!));
        Assert.Equal(61, tip.Width);
        // The disc's radius is 0.3 of the side: 18 px. Every row, not only the first.
        for (var y = 0; y < 61; y++)
        {
            Assert.Equal(0, tip.GetPixel(2, y).Alpha);
            Assert.Equal(0, tip.GetPixel(58, y).Alpha);
        }
        Assert.Equal(255, tip.GetPixel(30, 30).Alpha);
        Assert.Equal(255, tip.GetPixel(30, 45).Alpha);
    }

    [Fact]
    public void WithNoManifestTheArchivesOwnOrderIsKept()
    {
        var set = Zip(
            ("Z/Brush.archive", Archive("First in the file")),
            ("A/Brush.archive", Archive("Second in the file")));

        Assert.Equal(
            ["First in the file", "Second in the file"],
            BrushImport.Read("x.brushset", set).Select(b => b.Name));
    }

    [Fact]
    public void AnInvertedShapeIsReadTheOtherWayRound()
    {
        var set = Zip(
            ("A/Brush.archive", Archive("Inverted", shapeInverted: true)),
            ("A/Shape.png", DiscPng(64)));

        var brush = Assert.Single(BrushImport.Read("x.brushset", set));

        using var tip = SKBitmap.Decode(Convert.FromBase64String(brush.TipPngBase64!));
        Assert.Equal(0, tip.GetPixel(32, 32).Alpha);
        Assert.Equal(255, tip.GetPixel(1, 1).Alpha);
    }

    /// <summary>
    /// Procreate ships 2048-pixel shapes. A tip that size is carried in the preset
    /// store and resampled on every dab, so it is brought down on the way in.
    /// </summary>
    [Fact]
    public void ALargeShapeIsBroughtDownToAWorkableTip()
    {
        var set = Zip(
            ("A/Brush.archive", Archive("Big")),
            ("A/Shape.png", DiscPng(2048)));

        var brush = Assert.Single(BrushImport.Read("x.brushset", set));

        using var tip = SKBitmap.Decode(Convert.FromBase64String(brush.TipPngBase64!));
        output.WriteLine($"2048 px shape arrived as {tip.Width}x{tip.Height}");
        Assert.Equal(BrushsetReader.MaxTipSide, Math.Max(tip.Width, tip.Height));
        Assert.Equal(255, tip.GetPixel(tip.Width / 2, tip.Height / 2).Alpha);
        Assert.Equal(0, tip.GetPixel(1, 1).Alpha);
    }

    [Fact]
    public void ABrushWithNoNameTakesTheFilesName()
    {
        var one = Zip(("Brush.archive", Archive(name: null)));

        Assert.Equal("Mystery", Assert.Single(BrushImport.Read("Mystery.brush", one)).Name);
    }

    // ---- hostile input (SENSITIVITY S1) ------------------------------------------------

    [Fact]
    public void FourBytesIsAMessageNotACrash()
    {
        var e = Assert.Throws<FormatException>(() => BrushImport.Read("x.brushset", [1, 2, 3, 4]));
        output.WriteLine(e.Message);
    }

    [Fact]
    public void AnArchiveWithNoBrushInItSaysSo()
    {
        var set = Zip(("readme.txt", "hello"u8.ToArray()));

        Assert.Throws<FormatException>(() => BrushImport.Read("x.brushset", set));
    }

    /// <summary>One unreadable brush costs that brush, not the set.</summary>
    [Fact]
    public void OneBrokenBrushDoesNotTakeTheOthersWithIt()
    {
        var set = Zip(
            ("A/Brush.archive", "bplist00 and then nothing useful"u8.ToArray()),
            ("B/Brush.archive", [0xFF, 0x00]),
            ("C/Brush.archive", Archive("Survivor")));

        Assert.Equal("Survivor", Assert.Single(BrushImport.Read("x.brushset", set)).Name);
    }

    /// <summary>
    /// An entry that inflates far past anything a brush is. The declared size is the
    /// attacker's to write, so the read itself is what has to stop.
    /// </summary>
    [Fact]
    public void AnArchiveThatInflatesWithoutEndIsNotInflated()
    {
        var bomb = Zip(
            ("A/Brush.archive", new byte[80 * 1024 * 1024]),
            ("B/Brush.archive", Archive("Survivor")));
        output.WriteLine($"{bomb.Length / 1024} KB of zip holding an 80 MB settings file");

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var brushes = BrushImport.Read("x.brushset", bomb);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        output.WriteLine($"allocated {allocated / (1024 * 1024)} MB reading it");
        Assert.Equal("Survivor", Assert.Single(brushes).Name);
        Assert.True(allocated < 40L * 1024 * 1024, $"allocated {allocated} bytes: the 80 MB entry was inflated");
    }

    /// <summary>
    /// The same file with its declared size rewritten to a hundred bytes: the archive
    /// says the entry is small and the bytes say otherwise.
    /// </summary>
    /// <remarks>
    /// Two things stand between this file and the memory: the runtime's zip reader
    /// stops an entry at its declared length, and <c>BrushsetReader</c> counts what
    /// it inflates. On .NET 10 the first one answers, so this test holds the outcome
    /// and cannot tell which of the two produced it.
    /// </remarks>
    [Fact]
    public void AnArchiveThatLiesAboutItsSizeIsStillNotInflated()
    {
        var bomb = Zip(
            ("A/Brush.archive", new byte[80 * 1024 * 1024]),
            ("B/Brush.archive", Archive("Survivor")));
        // The central directory's first record: signature PK, uncompressed size at +24.
        var record = bomb.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x50, 0x4B, 0x01, 0x02]);
        Assert.True(record > 0);
        BinaryPrimitives.WriteInt32LittleEndian(bomb.AsSpan(record + 24), 100);

        var before = GC.GetTotalAllocatedBytes(precise: true);
        List<ImportedBrush> brushes;
        try
        {
            brushes = BrushImport.Read("x.brushset", bomb);
        }
        catch (FormatException e)
        {
            // Refusing the whole file is also an acceptable answer to a file that lies.
            output.WriteLine($"refused outright: {e.Message}");
            brushes = [];
        }
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        output.WriteLine($"allocated {allocated / (1024 * 1024)} MB; {brushes.Count} brush(es) read");
        Assert.DoesNotContain(brushes, b => b.Name != "Survivor");
        Assert.True(allocated < 40L * 1024 * 1024, $"allocated {allocated} bytes: the 80 MB entry was inflated");
    }

    /// <summary>
    /// A shape past the size a tip may be is left out, and the brush still arrives.
    /// </summary>
    /// <remarks>
    /// A real, decodable image one pixel-row over the limit, so the only thing that can
    /// refuse it is the limit. (A header with nothing behind it is refused too, but by
    /// the decoder, which would say nothing about the check.) The second case is that
    /// one: a header claiming sixty thousand pixels a side.
    /// </remarks>
    [Fact]
    public void AShapeLargerThanATipCanBeIsLeftOut()
    {
        var justOver = Zip(
            ("A/Brush.archive", Archive("Still a brush")),
            ("A/Shape.png", DiscPng(4097)));
        var absurd = Zip(
            ("A/Brush.archive", Archive("Still a brush")),
            ("A/Shape.png", PngHeaderClaiming(60000, 60000)));
        var atTheLimit = Zip(
            ("A/Brush.archive", Archive("Still a brush")),
            ("A/Shape.png", DiscPng(4096)));

        Assert.Null(Assert.Single(BrushImport.Read("x.brushset", justOver)).TipPngBase64);
        Assert.Null(Assert.Single(BrushImport.Read("x.brushset", absurd)).TipPngBase64);
        Assert.NotNull(Assert.Single(BrushImport.Read("x.brushset", atTheLimit)).TipPngBase64);
    }

    /// <summary>
    /// The counter itself, on a stream that has no declared length to consult: it
    /// stops at the limit, having read no more than one buffer past it.
    /// </summary>
    /// <remarks>
    /// Through a zip this cannot be shown — the runtime's reader stops an entry at
    /// its declared length before the counter is reached — so the counter is handed
    /// an endless stream directly.
    /// </remarks>
    [Fact]
    public void TheInflationCounterStopsAnEndlessStream()
    {
        var endless = new Endless();

        Assert.Throws<FormatException>(() => BrushsetReader.ReadBounded(endless, 1024 * 1024, "a test stream"));

        output.WriteLine($"read {endless.Served} bytes against a limit of {1024 * 1024}");
        Assert.InRange(endless.Served, 1024 * 1024, 1024 * 1024 + 200_000);
    }

    [Fact]
    public void TheInflationCounterReturnsWhatFits()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();

        Assert.Equal(bytes, BrushsetReader.ReadBounded(new MemoryStream(bytes), 300_000, "a test stream"));
    }

    private sealed class Endless : Stream
    {
        public long Served { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            Served += count;
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Served; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Nothing is written to disk, so an escaping name cannot escape — but it is not
    /// a brush folder either, and is not read as one.
    /// </summary>
    [Fact]
    public void AnEntryNamedOutOfTheArchiveIsNotABrush()
    {
        var set = Zip(
            ("../../outside/Brush.archive", Archive("Escaped")),
            ("ok/Brush.archive", Archive("Inside")));

        Assert.Equal("Inside", Assert.Single(BrushImport.Read("x.brushset", set)).Name);
    }

    /// <summary>A property list whose array contains itself is refused, not followed.</summary>
    [Fact]
    public void ASettingsFileThatRefersToItselfIsRefused()
    {
        // One object: an array of one element, which is object 0.
        var cyclic = RawPlist([[0xA1, 0x00, 0x00]], top: 0);
        var set = Zip(
            ("A/Brush.archive", cyclic),
            ("B/Brush.archive", Archive("Survivor")));

        Assert.Equal("Survivor", Assert.Single(BrushImport.Read("x.brushset", set)).Name);
    }

    /// <summary>
    /// A manifest forty thousand elements deep: 280 KB of XML, under two kilobytes
    /// zipped. Reading it through a document tree overflowed the stack, which nothing
    /// can catch. It is refused at the depth limit and the set arrives in archive order.
    /// </summary>
    [Fact]
    public void AManifestNestedWithoutEndIsRefusedNotFollowed()
    {
        var deep = "<plist><dict><key>"
            + string.Concat(Enumerable.Repeat("<a>", 40_000))
            + string.Concat(Enumerable.Repeat("</a>", 40_000))
            + "</key><array/></dict></plist>";
        var set = Zip(
            ("Z/Brush.archive", Archive("First in the file")),
            ("A/Brush.archive", Archive("Second in the file")),
            ("brushset.plist", Encoding.UTF8.GetBytes(deep)));
        output.WriteLine($"{deep.Length / 1024} KB of manifest in a {set.Length} byte file");

        Assert.Equal(
            ["First in the file", "Second in the file"],
            BrushImport.Read("x.brushset", set).Select(b => b.Name));
    }

    /// <summary>
    /// One blob and tens of thousands of references to it. A reference is a byte in
    /// the file; built again for each one it would be the blob's size in memory.
    /// </summary>
    [Fact]
    public void ManyReferencesToOneBlobAreOneBlob()
    {
        const int blob = 600_000;
        const int refs = 60_000;
        // Object 0: an array of `refs` two-byte references, every one to object 1.
        // Object 1: `blob` bytes of data.
        byte[] array = [0xAF, 0x12, .. BigEndian(refs)[4..], .. Enumerable.Repeat(new byte[] { 0x00, 0x01 }, refs).SelectMany(r => r)];
        byte[] data = [0x4F, 0x12, .. BigEndian(blob)[4..], .. new byte[blob]];
        var set = Zip(
            ("A/Brush.archive", RawPlist([array, data], top: 0)),
            ("B/Brush.archive", Archive("Survivor")));
        output.WriteLine($"{refs:N0} references to a {blob / 1000} KB blob, in a {set.Length} byte file");

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var brushes = BrushImport.Read("x.brushset", set);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        output.WriteLine($"allocated {allocated / (1024 * 1024)} MB; built once it is about 2, built per reference 34,000");
        Assert.Equal("Survivor", Assert.Single(brushes).Name);
        Assert.True(allocated < 32L * 1024 * 1024, $"allocated {allocated} bytes");
    }

    /// <summary>
    /// The same blob behind many table entries rather than many references: every
    /// entry is a different object as far as the table says, at one offset. Building
    /// once does not see that; the budget does.
    /// </summary>
    [Fact]
    public void ManyTableEntriesAtOneOffsetAreStoppedByTheBudget()
    {
        const int blob = 500_000;
        const int entries = 2_000;
        using var ms = new MemoryStream();
        ms.Write("bplist00"u8);
        var blobAt = (int)ms.Position;
        ms.Write([0x4F, 0x12, .. BigEndian(blob)[4..], .. new byte[blob]]);
        var arrayAt = (int)ms.Position;
        ms.Write([0xAF, 0x12, .. BigEndian(entries)[4..]]);
        for (var i = 1; i <= entries; i++) ms.Write([(byte)(i >> 8), (byte)i]);
        var table = (int)ms.Position;
        ms.Write(BigEndian(arrayAt)[4..]);                                   // object 0: the array
        for (var i = 0; i < entries; i++) ms.Write(BigEndian(blobAt)[4..]); // objects 1..n: all the blob
        ms.Write(new byte[6]);
        ms.WriteByte(4);
        ms.WriteByte(2);
        ms.Write(BigEndian(entries + 1));
        ms.Write(BigEndian(0));
        ms.Write(BigEndian(table));
        var set = Zip(
            ("A/Brush.archive", ms.ToArray()),
            ("B/Brush.archive", Archive("Survivor")));

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var brushes = BrushImport.Read("x.brushset", set);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        output.WriteLine($"allocated {allocated / (1024 * 1024)} MB; unbudgeted it is {(long)blob * entries / (1024 * 1024)}");
        Assert.Equal("Survivor", Assert.Single(brushes).Name);
        Assert.True(allocated < 48L * 1024 * 1024, $"allocated {allocated} bytes");
    }

    /// <summary>
    /// A dictionary of two hundred thousand pairs whose first key is the
    /// dictionary itself. Sized from its count and followed to the nesting limit, that
    /// was 1.4 GB live from a four-kilobyte file.
    /// </summary>
    [Fact]
    public void ADictionaryKeyedByItselfIsRefusedAtOnce()
    {
        const int pairs = 200_000;
        byte[] dict = [0xDF, 0x12, .. BigEndian(pairs)[4..], .. new byte[pairs * 2 * 2]];   // every reference is to object 0
        var set = Zip(
            ("A/Brush.archive", RawPlist([dict], top: 0)),
            ("B/Brush.archive", Archive("Survivor")));
        output.WriteLine($"a dictionary of {pairs:N0} pairs, all itself, in a {set.Length} byte file");

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var brushes = BrushImport.Read("x.brushset", set);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        output.WriteLine($"allocated {allocated / (1024 * 1024)} MB");
        Assert.Equal("Survivor", Assert.Single(brushes).Name);
        Assert.True(allocated < 16L * 1024 * 1024, $"allocated {allocated} bytes");
    }

    /// <summary>Shape.png is a PNG; a JPEG under that name is not decoded.</summary>
    [Fact]
    public void AShapeThatIsNotAPngIsLeftOut()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888));
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        var set = Zip(
            ("A/Brush.archive", Archive("Still a brush")),
            ("A/Shape.png", image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray()));

        Assert.Null(Assert.Single(BrushImport.Read("x.brushset", set)).TipPngBase64);
    }

    /// <summary>
    /// A file's tips are budgeted together: past the budget the brushes still arrive,
    /// without their shapes, so a small archive cannot fill the preset store.
    /// </summary>
    [Fact]
    public void ASetsTipsShareOneBudget()
    {
        var shape = DiscPng(256);
        var set = Zip(
            ("A/Brush.archive", Archive("A")), ("A/Shape.png", shape),
            ("B/Brush.archive", Archive("B")), ("B/Shape.png", shape),
            ("C/Brush.archive", Archive("C")), ("C/Shape.png", shape));
        var oneTip = Assert.Single(BrushsetReader.Read("x.brush", Zip(("Brush.archive", Archive("A")), ("Shape.png", shape)))).TipPngBase64!.Length;

        var brushes = BrushsetReader.Read("x.brushset", set, tipBudget: oneTip + 1);

        output.WriteLine($"one tip is {oneTip} characters; with a budget of {oneTip + 1}: {string.Join(", ", brushes.Select(b => b.TipPngBase64 is null ? "none" : "tip"))}");
        Assert.Equal(["A", "B", "C"], brushes.Select(b => b.Name));
        Assert.NotNull(brushes[0].TipPngBase64);
        Assert.NotNull(brushes[1].TipPngBase64);     // the one that crosses the line is kept
        Assert.Null(brushes[2].TipPngBase64);
    }

    /// <summary>A container claiming two thousand million entries in a 40-byte file.</summary>
    [Fact]
    public void ASettingsFileClaimingMoreThanItHoldsIsRefused()
    {
        // 0xAF = array, count follows as an int object: 0x12 = 4-byte int, 0x7FFFFFFF.
        var greedy = RawPlist([[0xAF, 0x12, 0x7F, 0xFF, 0xFF, 0xFF]], top: 0);
        var set = Zip(
            ("A/Brush.archive", greedy),
            ("B/Brush.archive", Archive("Survivor")));

        Assert.Equal("Survivor", Assert.Single(BrushImport.Read("x.brushset", set)).Name);
    }

    [Fact]
    public void NumbersThatAreNotNumbersBecomeSaneSettings()
    {
        var set = Zip(("A/Brush.archive", Archive(
            "Odd", paintSize: double.NaN, maxSize: double.PositiveInfinity, plotSpacing: -4, maxOpacity: 900)));

        var brush = Assert.Single(BrushImport.Read("x.brushset", set));

        output.WriteLine($"{brush.SizePx} px, spacing {brush.Spacing}, opacity {brush.Opacity}");
        Assert.InRange(brush.SizePx, 1, 500);
        Assert.InRange(brush.Spacing, 0.02, 2);
        Assert.InRange(brush.Opacity, 0.01, 1);
    }

    // ---- the owner's file ---------------------------------------------------------------

    [Fact]
    public void TheOwnersOwnSetComesAcross()
    {
        var path = Environment.GetEnvironmentVariable("LIGHTBOX_PROCREATE_SAMPLE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            output.WriteLine("LIGHTBOX_PROCREATE_SAMPLE is not set: nothing to read, nothing asserted.");
            return;
        }

        var brushes = BrushImport.Read(Path.GetFileName(path), File.ReadAllBytes(path));

        foreach (var b in brushes)
        {
            var tip = "no shape in the file (round)";
            if (b.TipPngBase64 is { } png)
            {
                using var bitmap = SKBitmap.Decode(Convert.FromBase64String(png));
                tip = $"tip {bitmap.Width}x{bitmap.Height}, {png.Length * 3 / 4 / 1024} KB";
            }
            output.WriteLine($"{b.Name,-16} {b.SizePx,6:0.#} px  spacing {b.Spacing:0.###}  opacity {b.Opacity:0.##}  {tip}");
        }
        Assert.NotEmpty(brushes);
        Assert.All(brushes, b => Assert.False(string.IsNullOrWhiteSpace(b.Name)));
        Assert.All(brushes, b => Assert.InRange(b.SizePx, 1, 500));
    }

    // ---- building the files ---------------------------------------------------------------

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                s.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    private static byte[] Manifest(string name, params string[] folders) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
        + "<plist version=\"1.0\"><dict><key>brushes</key><array>"
        + string.Concat(folders.Select(f => $"<string>{f}</string>"))
        + $"</array><key>name</key><string>{name}</string></dict></plist>");

    /// <summary>A white disc on black: the way a shape is stored, white being paint.</summary>
    private static byte[] DiscPng(int size)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Gray8));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
            canvas.DrawCircle(size / 2f, size / 2f, size * 0.3f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>A PNG signature and an IHDR, and nothing behind them.</summary>
    private static byte[] PngHeaderClaiming(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[25];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, 13);
        "IHDR"u8.CopyTo(ihdr[4..]);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[8..], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[12..], height);
        ihdr[16] = 8;       // depth
        ihdr[17] = 0;       // grayscale
        ms.Write(ihdr);
        return ms.ToArray();
    }

    private readonly record struct Uid(int Index);

    /// <summary>
    /// A keyed archive the way the settings file is laid out: an object table, with
    /// the brush as one dictionary in it and its strings held by reference.
    /// </summary>
    private static byte[] Archive(
        string? name = "Brush", double paintSize = 0.5, double maxSize = 1, double plotSpacing = 0.1,
        double maxOpacity = 1, bool shapeInverted = false, bool wide = false)
    {
        var root = new Dictionary<string, object>
        {
            ["$class"] = new Uid(3),
            ["paintSize"] = paintSize,
            ["maxSize"] = maxSize,
            ["plotSpacing"] = plotSpacing,
            ["maxOpacity"] = maxOpacity,
            ["shapeInverted"] = shapeInverted,
            ["version"] = 5L,
            ["name"] = name is null ? new Uid(0) : new Uid(2),
        };
        var objects = new List<object>
        {
            "$null",
            root,
            name ?? "unused",
            new Dictionary<string, object> { ["$classname"] = "SilicaBrush" },
        };
        return Plist(new Dictionary<string, object>
        {
            ["$archiver"] = "NSKeyedArchiver",
            ["$objects"] = objects,
            ["$top"] = new Dictionary<string, object> { ["root"] = new Uid(1) },
            ["$version"] = 100000L,
        }, wide);
    }

    /// <summary>Flatten a tree into a binary property list.</summary>
    /// <remarks>
    /// Narrow by default because that is what a real <c>Brush.archive</c> is: one-byte
    /// object references, two-byte offsets, four-byte reals and the smallest integer
    /// that holds the value. <paramref name="wide"/> writes the other legal form —
    /// two-byte references, four-byte offsets, eight-byte numbers — so both are read.
    /// </remarks>
    private static byte[] Plist(object top, bool wide = false)
    {
        var encoded = new List<byte[]>();
        int Add(object o)
        {
            var index = encoded.Count;
            encoded.Add([]);
            encoded[index] = o switch
            {
                bool b => [(byte)(b ? 0x09 : 0x08)],
                long l when wide => [0x13, .. BigEndian(l)],
                long l when l is >= 0 and < 256 => [0x10, (byte)l],
                long l => [0x12, .. BigEndian(l)[4..]],
                double d when wide => [0x23, .. BigEndian(BitConverter.DoubleToInt64Bits(d))],
                double d => [0x22, .. BigEndian(BitConverter.SingleToInt32Bits((float)d))[4..]],
                Uid u when wide => [0x81, (byte)(u.Index >> 8), (byte)u.Index],
                Uid u => [0x80, (byte)u.Index],
                // Anything past ASCII is stored as UTF-16, counted in characters.
                string s when s.Any(c => c > 127) => [.. Marker(0x60, s.Length), .. Encoding.BigEndianUnicode.GetBytes(s)],
                string s => [.. Marker(0x50, s.Length), .. Encoding.ASCII.GetBytes(s)],
                List<object> list => Container(0xA0, list.Select(Add).ToList(), wide),
                Dictionary<string, object> dict => Container(
                    0xD0,
                    [.. dict.Keys.Select(k => Add(k)), .. dict.Values.Select(Add)],
                    wide,
                    dict.Count),
                _ => throw new NotSupportedException(o.GetType().Name),
            };
            return index;
        }
        var topIndex = Add(top);
        return RawPlist(encoded, topIndex, offsetSize: wide ? 4 : 2, refSize: wide ? 2 : 1);
    }

    private static byte[] Container(int type, List<int> refs, bool wide, int? count = null)
    {
        var bytes = new List<byte>(Marker(type, count ?? refs.Count));
        foreach (var r in refs)
        {
            if (wide) bytes.Add((byte)(r >> 8));
            bytes.Add((byte)r);
        }
        return [.. bytes];
    }

    private static byte[] Marker(int type, int count) =>
        count < 15 ? [(byte)(type | count)] : [(byte)(type | 0x0F), 0x12, .. BigEndian(count)[4..]];

    private static byte[] BigEndian(long v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        return b;
    }

    /// <summary>Header, objects, the offset table and the trailer.</summary>
    private static byte[] RawPlist(IReadOnlyList<byte[]> objects, int top, int offsetSize = 4, int refSize = 2)
    {
        using var ms = new MemoryStream();
        ms.Write("bplist00"u8);
        var offsets = new List<int>();
        foreach (var o in objects)
        {
            offsets.Add((int)ms.Position);
            ms.Write(o);
        }
        var table = (int)ms.Position;
        foreach (var offset in offsets) ms.Write(BigEndian(offset)[(8 - offsetSize)..]);
        ms.Write(new byte[6]);
        ms.WriteByte((byte)offsetSize);     // bytes per offset
        ms.WriteByte((byte)refSize);        // bytes per object reference
        ms.Write(BigEndian(objects.Count));
        ms.Write(BigEndian(top));
        ms.Write(BigEndian(table));
        return ms.ToArray();
    }
}
