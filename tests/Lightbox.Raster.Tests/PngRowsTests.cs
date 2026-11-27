using System.Buffers.Binary;
using System.IO.Compression;
using Lightbox.Import.Unity;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The row-at-a-time PNG reader (Q301): it reads what Skia reads, every filter,
/// split image data, and refuses — by kind — what it does not read; damaged
/// files are a <see cref="FormatException"/> and never anything else.
/// </summary>
public class PngRowsTests(ITestOutputHelper output)
{
    // ---- a deliberate encoder, so every case can be made on purpose -----------

    private sealed record Png(int Width, int Height, byte[] Pixels, int ColourType = 6, int Depth = 8)
    {
        public int Interlace { get; init; }
        public Func<int, int> Filter { get; init; } = y => y % 5;
        public int IdatChunk { get; init; } = 1 << 20;
        public List<(string Type, byte[] Data)> Before { get; init; } = [];

        private int Bpp => ColourType switch { 6 => 4, 2 => 3, _ => 1 } * (Depth / 8);

        public byte[] Encode()
        {
            var raw = new MemoryStream();
            var stride = Width * Bpp;
            var prior = new byte[stride];
            for (var y = 0; y < Height; y++)
            {
                var row = Pixels.AsSpan(y * stride, stride).ToArray();
                var f = Filter(y);
                raw.WriteByte((byte)f);
                for (var i = 0; i < stride; i++)
                {
                    int a = i >= Bpp ? row[i - Bpp] : 0, b = y > 0 ? prior[i] : 0, c = i >= Bpp && y > 0 ? prior[i - Bpp] : 0;
                    var predicted = f switch
                    {
                        1 => a,
                        2 => b,
                        3 => (a + b) >> 1,
                        4 => Paeth(a, b, c),
                        _ => 0,
                    };
                    raw.WriteByte((byte)(row[i] - predicted));
                }
                prior = row;
            }
            var z = new MemoryStream();
            using (var zl = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zl.Write(raw.ToArray());
            var data = z.ToArray();

            var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var ihdr = new byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)Width);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)Height);
            ihdr[8] = (byte)Depth;
            ihdr[9] = (byte)ColourType;
            ihdr[12] = (byte)Interlace;
            Chunk(png, "IHDR", ihdr);
            foreach (var (t, d) in Before) Chunk(png, t, d);
            for (var at = 0; at < data.Length || at == 0; at += IdatChunk)
                Chunk(png, "IDAT", data.AsSpan(at, Math.Min(IdatChunk, data.Length - at)).ToArray());
            Chunk(png, "IEND", []);
            return png.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> n = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(n, (uint)data.Length);
        s.Write(n);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = PngRows.Crc32.Finish(PngRows.Crc32.Update(PngRows.Crc32.Update(PngRows.Crc32.Seed, t), data));
        BinaryPrimitives.WriteUInt32BigEndian(n, crc);
        s.Write(n);
    }

    private static byte[] Random(Random rng, int n)
    {
        var b = new byte[n];
        rng.NextBytes(b);
        return b;
    }

    private static List<byte[]> ReadAll(byte[] png)
    {
        using var r = PngRows.Open(new MemoryStream(png), "t");
        var rows = new List<byte[]>();
        var buf = new byte[r.Width * 4];
        while (r.ReadRow(buf)) rows.Add(buf.ToArray());
        return rows;
    }

    // ---- what it reads ----------------------------------------------------------

    [Theory]
    [InlineData(6)]
    [InlineData(2)]
    public void EveryFilterTypeComesBackToTheStoredPixels(int colourType)
    {
        var rng = new Random(1);
        var bpp = colourType == 6 ? 4 : 3;
        const int w = 37, h = 23;
        var pixels = Random(rng, w * h * bpp);

        var rows = ReadAll(new Png(w, h, pixels, colourType).Encode());

        Assert.Equal(h, rows.Count);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * bpp;
                Assert.Equal(pixels[i], rows[y][x * 4]);
                Assert.Equal(pixels[i + 1], rows[y][x * 4 + 1]);
                Assert.Equal(pixels[i + 2], rows[y][x * 4 + 2]);
                Assert.Equal(bpp == 4 ? pixels[i + 3] : (byte)255, rows[y][x * 4 + 3]);
            }
    }

    [Fact]
    public void ImageDataSplitAcrossManyChunksReadsTheSame()
    {
        var px = Random(new Random(2), 20 * 9 * 4);
        Assert.Equal(ReadAll(new Png(20, 9, px).Encode()), ReadAll(new Png(20, 9, px) { IdatChunk = 7 }.Encode()));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 200)]
    [InlineData(257, 2)]
    public void WhatSkiaWritesThisReadsAsSkiaReadsIt(int w, int h)
    {
        // Semi-transparent pixels included: nothing may be premultiplied on the way.
        var rng = new Random(w * 1000 + h);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                bmp.SetPixel(x, y, new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        using var skia = SKBitmap.Decode(bytes, new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var rows = ReadAll(bytes);

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = skia.GetPixel(x, y);
                Assert.Equal((c.Red, c.Green, c.Blue, c.Alpha), (rows[y][x * 4], rows[y][x * 4 + 1], rows[y][x * 4 + 2], rows[y][x * 4 + 3]));
            }
    }

    [Fact]
    public void AnAncillaryChunkItDoesNotKnowIsSkipped()
    {
        var px = Random(new Random(3), 4 * 4 * 4);
        var png = new Png(4, 4, px) { Before = [("tEXt", "Comment\0hello"u8.ToArray()), ("sRGB", [0])] }.Encode();
        Assert.Equal(ReadAll(new Png(4, 4, px).Encode()), ReadAll(png));
    }

    // ---- what it hands to another decoder ----------------------------------------

    [Theory]
    [InlineData(3, 8, 0, "colour type 3")]
    [InlineData(6, 16, 0, "16-bit")]
    [InlineData(0, 8, 0, "colour type 0")]
    [InlineData(6, 8, 1, "interlaced")]
    public void APngOfAnotherKindIsUnsupportedNotDamaged(int colourType, int depth, int interlace, string named)
    {
        var bpp = colourType switch { 6 => 4, 2 => 3, _ => 1 } * (depth / 8);
        var png = new Png(4, 4, new byte[4 * 4 * bpp], colourType, depth) { Interlace = interlace }.Encode();

        var ex = Assert.Throws<PngUnsupportedException>(() => PngRows.Open(new MemoryStream(png), "t"));
        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void AColourKeyOrAnUnknownCriticalChunkIsUnsupported()
    {
        var px = new byte[4 * 4 * 3];
        Assert.Throws<PngUnsupportedException>(() => PngRows.Open(new MemoryStream(
            new Png(4, 4, px, 2) { Before = [("tRNS", [0, 0, 0, 0, 0, 0])] }.Encode()), "t"));
        Assert.Throws<PngUnsupportedException>(() => PngRows.Open(new MemoryStream(
            new Png(4, 4, px, 2) { Before = [("ZZZZ", [1])] }.Encode()), "t"));
    }

    // ---- what it refuses ------------------------------------------------------------

    [Fact]
    public void ADamagedChecksumIsNamed()
    {
        var png = new Png(4, 4, new byte[64]) { Before = [("tEXt", "a\0b"u8.ToArray())] }.Encode();
        png[33 + 12 + 2] ^= 0xFF; // inside the tEXt chunk's data
        var ex = Assert.Throws<FormatException>(() => PngRows.Open(new MemoryStream(png), "t"));
        Assert.Contains("checksum", ex.Message);
    }

    [Fact]
    public void AnUnknownFilterTypeIsNamed()
    {
        var png = new Png(4, 2, new byte[32]) { Filter = y => y == 1 ? 9 : 0 }.Encode();
        using var r = PngRows.Open(new MemoryStream(png), "t");
        var buf = new byte[16];
        Assert.True(r.ReadRow(buf));
        Assert.Contains("filter type 9", Assert.Throws<FormatException>(() => r.ReadRow(buf)).Message);
    }

    [Fact]
    public void AHugeHeaderOverTinyDataCostsOneRowAndThenSaysItEnded()
    {
        // 65 536² RGBA would be 16 GiB decoded; the reader holds one row, and the
        // data runs out on the third.
        var twoRows = new Png(65536, 2, new byte[65536 * 2 * 4]).Encode();
        var ihdrHeight = 8 + 8 + 4;
        BinaryPrimitives.WriteUInt32BigEndian(twoRows.AsSpan(ihdrHeight), 65536);
        var crc = PngRows.Crc32.Finish(PngRows.Crc32.Update(PngRows.Crc32.Seed, twoRows.AsSpan(12, 17)));
        BinaryPrimitives.WriteUInt32BigEndian(twoRows.AsSpan(29), crc);

        var before = GC.GetTotalAllocatedBytes();
        using var r = PngRows.Open(new MemoryStream(twoRows), "huge.png");
        var buf = new byte[r.Width * 4];
        Assert.True(r.ReadRow(buf));
        Assert.True(r.ReadRow(buf));
        Assert.Contains("ends at row 2", Assert.Throws<FormatException>(() => r.ReadRow(buf)).Message);
        var allocated = GC.GetTotalAllocatedBytes() - before;
        output.WriteLine($"{allocated / 1024} KB allocated for a 65536² header");
        Assert.True(allocated < 16 * 1024 * 1024);
    }

    [Fact]
    public void EveryPrefixAndManyCorruptionsAreRefusedNeverACrash()
    {
        var rng = new Random(4);
        var whole = new Png(13, 11, Random(rng, 13 * 11 * 4)) { IdatChunk = 64, Before = [("tEXt", "k\0v"u8.ToArray())] }.Encode();
        var cases = new List<byte[]>();
        for (var cut = 0; cut < whole.Length; cut++) cases.Add(whole[..cut]);
        for (var k = 0; k < 2000; k++)
        {
            var c = whole.ToArray();
            for (var m = rng.Next(1, 4); m > 0; m--) c[rng.Next(c.Length)] = (byte)rng.Next(256);
            cases.Add(c);
        }

        foreach (var c in cases)
        {
            var e = Record.Exception(() => ReadAll(c));
            Assert.True(e is null or FormatException, $"{e?.GetType().Name}: {e?.Message}");
        }
    }

    [Fact]
    public void AReaderThatStopsEarlyStillCatchesADamagedChunk()
    {
        // Rows past the ones read sit in the same chunk; their damage must not pass
        // just because reading stopped before the chunk's checksum.
        var png = new Png(16, 16, Random(new Random(5), 16 * 16 * 4)).Encode();
        png[png.Length - 12 - 4 - 3] ^= 0xFF; // inside the one IDAT, near its end

        using var r = PngRows.Open(new MemoryStream(png), "t");
        r.ReadRow(new byte[64]);
        Assert.Contains("checksum", Assert.Throws<FormatException>(() => r.VerifyChunk()).Message);
    }

    /// <summary>Recomputes every chunk's CRC, so corruption reaches inflate and the filters.</summary>
    private static byte[] Reseal(byte[] png)
    {
        var at = 8;
        while (at + 12 <= png.Length)
        {
            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            if (len < 0 || at + 12 + len > png.Length) break;
            var crc = PngRows.Crc32.Finish(PngRows.Crc32.Update(PngRows.Crc32.Seed, png.AsSpan(at + 4, 4 + len)));
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(at + 8 + len), crc);
            at += 12 + len;
        }
        return png;
    }

    [Fact]
    public void DamageBehindAValidChecksumIsStillOnlyAFormatError()
    {
        // The checksums are recomputed after each corruption, so it gets past them
        // to inflate and the filters — where a raw zlib error once escaped.
        var rng = new Random(6);
        var whole = new Png(17, 13, Random(rng, 17 * 13 * 4)) { IdatChunk = 50 }.Encode();
        var read = 0;
        for (var k = 0; k < 20000; k++)
        {
            var c = whole.ToArray();
            for (var m = rng.Next(1, 4); m > 0; m--) c[rng.Next(33, c.Length)] ^= (byte)(1 << rng.Next(8));
            var e = Record.Exception(() => ReadAll(Reseal(c)));
            Assert.True(e is null or FormatException, $"case {k}: {e?.GetType().FullName}: {e?.Message}");
            if (e is null) read++;
        }
        output.WriteLine($"{read} of 20000 still decoded (damage that is valid data)");
    }

    [Fact]
    public void AHeaderClaimingToBeHugeIsRefusedBeforeItIsRead()
    {
        var png = new Png(2, 2, new byte[16]).Encode()[..33];
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 60_000_000);
        var before = GC.GetTotalAllocatedBytes();
        Assert.Throws<FormatException>(() => PngRows.Open(new MemoryStream(png), "t"));
        Assert.True(GC.GetTotalAllocatedBytes() - before < 1024 * 1024);
    }
}
