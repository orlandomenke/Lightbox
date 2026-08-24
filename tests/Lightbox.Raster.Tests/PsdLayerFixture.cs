namespace Lightbox.Raster.Tests;

/// <summary>How a fixture layer's channels are stored on disk.</summary>
public enum PsdCompression
{
    Raw = 0,
    Rle = 1,
    Zip = 2,
    ZipPredicted = 3,
}

/// <summary>One layer in a <see cref="PsdFixture"/>, with every knob a test needs.</summary>
/// <remarks>
/// The channel values are flat, one byte per pixel per channel in the layer's own
/// bounds, because that is the shape a PSD stores: <b>planar</b>, one full channel
/// after another, not interleaved RGBA. Getting that wrong is the single easiest
/// way to write a reader that produces plausible garbage, so the fixture makes it
/// explicit.
/// </remarks>
internal sealed class PsdLayerFixture
{
    public string Name { get; init; } = "Layer";

    /// <summary>Overrides <see cref="Name"/> when read, as Photoshop's `luni` block does.</summary>
    public string? UnicodeName { get; init; }

    public int Left { get; init; }

    public int Top { get; init; }

    public int Right { get; init; } = 4;

    public int Bottom { get; init; } = 4;

    public string BlendKey { get; init; } = "norm";

    public byte Opacity { get; init; } = 255;

    public bool Visible { get; init; } = true;

    /// <summary>
    /// Raw bytes for a mask data block of a size the reader should not parse.
    /// </summary>
    /// <remarks>
    /// The blunt instrument, kept for the hostile cases. Use <see cref="Mask"/>
    /// for a real one.
    /// </remarks>
    public int MaskLength { get; init; }

    /// <summary>
    /// Coverage bytes for a real layer mask, one per pixel of its own rectangle.
    /// </summary>
    /// <remarks>
    /// A PSD mask's rectangle is independent of its layer's, which is the part
    /// worth being able to test: reading the channel at the layer's stride instead
    /// produces a plausible diagonally-smeared mask rather than a clear failure.
    /// </remarks>
    public byte[]? Mask { get; init; }

    public int MaskLeft { get; init; }

    public int MaskTop { get; init; }

    public int MaskRight { get; init; }

    public int MaskBottom { get; init; }

    /// <summary>Coverage outside the mask rectangle: 255 shows, 0 hides.</summary>
    public byte MaskOutside { get; init; } = 255;

    /// <summary>Shift-clicked off in Photoshop, keeping the drawing.</summary>
    public bool MaskDisabled { get; init; }

    /// <summary>Photoshop's clipping byte: this layer clips to the one below.</summary>
    public bool Clipping { get; init; }

    public int MaskWidth => MaskRight - MaskLeft;

    public int MaskHeight => MaskBottom - MaskTop;

    /// <summary>`lsct`: 1 opens a folder, 2 opens a collapsed one, 3 closes one.</summary>
    public int? SectionType { get; init; }

    /// <summary>`lspf`: any non-zero bit locks the layer.</summary>
    public int? ProtectionFlags { get; init; }

    /// <summary>Tagged blocks to plant, e.g. "lfx2" for layer effects.</summary>
    public string[] ExtraKeys { get; init; } = [];

    public PsdCompression Compression { get; init; } = PsdCompression.Raw;

    /// <summary>Red, or grey in a grayscale document. One byte per pixel.</summary>
    public byte[]? Red { get; init; }

    public byte[]? Green { get; init; }

    public byte[]? Blue { get; init; }

    /// <summary>Transparency. Absent means the layer is opaque throughout its bounds.</summary>
    public byte[]? Alpha { get; init; }

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>A solid layer of one colour, the common case in these tests.</summary>
    public static PsdLayerFixture Solid(
        string name, byte r, byte g, byte b, byte? a = null,
        int left = 0, int top = 0, int right = 4, int bottom = 4,
        string blend = "norm", byte opacity = 255, bool visible = true,
        PsdCompression compression = PsdCompression.Raw)
    {
        var count = (right - left) * (bottom - top);
        return new PsdLayerFixture
        {
            Name = name,
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
            BlendKey = blend,
            Opacity = opacity,
            Visible = visible,
            Compression = compression,
            Red = Fill(count, r),
            Green = Fill(count, g),
            Blue = Fill(count, b),
            Alpha = a is null ? null : Fill(count, a.Value),
        };
    }

    /// <summary>A folder bracket, which carries no pixels.</summary>
    public static PsdLayerFixture Group(string name, int sectionType) => new()
    {
        Name = name,
        SectionType = sectionType,
        Left = 0,
        Top = 0,
        Right = 0,
        Bottom = 0,
    };

    private static byte[] Fill(int count, byte value)
    {
        var bytes = new byte[count];
        Array.Fill(bytes, value);
        return bytes;
    }

    /// <summary>
    /// The channel blocks for this layer, in the order a PSD stores them:
    /// transparency first when present, then colour.
    /// </summary>
    public List<ChannelBlock> ChannelBlocks(int depth, bool psb)
    {
        var blocks = new List<ChannelBlock>();
        if (Alpha is not null) blocks.Add(Encode(-1, Alpha, depth, psb));
        if (Red is not null) blocks.Add(Encode(0, Red, depth, psb));
        if (Green is not null) blocks.Add(Encode(1, Green, depth, psb));
        if (Blue is not null) blocks.Add(Encode(2, Blue, depth, psb));
        if (Mask is not null) blocks.Add(EncodeMask(depth, psb));
        else if (MaskLength > 0) blocks.Add(Encode(-2, Red ?? [], depth, psb));
        return blocks;
    }

    /// <summary>The mask channel, at the mask's own width rather than the layer's.</summary>
    private ChannelBlock EncodeMask(int depth, bool psb)
    {
        var raw = depth == 16 ? Widen(Mask!) : Mask!;
        var rowBytes = MaskWidth * (depth / 8);
        var body = Compression switch
        {
            PsdCompression.Rle => RleRows(raw, rowBytes, MaskHeight, psb),
            PsdCompression.Zip => PsdFixture.Zlib(raw),
            PsdCompression.ZipPredicted => PsdFixture.Zlib(Predict(raw, rowBytes, depth)),
            _ => raw,
        };
        var ms = new MemoryStream();
        PsdFixture.U16(ms, (int)Compression);
        ms.Write(body);
        return new ChannelBlock(-2, ms.ToArray());
    }

    private ChannelBlock Encode(short id, byte[] samples, int depth, bool psb)
    {
        var raw = depth == 16 ? Widen(samples) : samples;
        var rowBytes = Width * (depth / 8);
        var body = Compression switch
        {
            PsdCompression.Raw => raw,
            PsdCompression.Rle => Rle(raw, rowBytes, psb),
            PsdCompression.Zip => PsdFixture.Zlib(raw),
            PsdCompression.ZipPredicted => PsdFixture.Zlib(Predict(raw, rowBytes, depth)),
            _ => raw,
        };

        var ms = new MemoryStream();
        PsdFixture.U16(ms, (int)Compression);
        ms.Write(body);
        return new ChannelBlock(id, ms.ToArray());
    }

    /// <summary>A row-length table, then each row's PackBits stream.</summary>
    /// <param name="psb">
    /// A PSB writes each scanline length as an int32 where a PSD writes an int16.
    /// The fixture has to honour that or it cannot test that the reader does.
    /// </param>
    private byte[] Rle(byte[] raw, int rowBytes, bool psb) =>
        RleRows(raw, rowBytes, Height, psb);

    private static byte[] RleRows(byte[] raw, int rowBytes, int height, bool psb)
    {
        var rows = new List<byte[]>();
        for (var y = 0; y < height; y++)
        {
            var offset = y * rowBytes;
            var length = Math.Min(rowBytes, Math.Max(0, raw.Length - offset));
            rows.Add(PsdFixture.PackRow(raw.AsSpan(offset, length)));
        }
        var ms = new MemoryStream();
        foreach (var row in rows)
        {
            if (psb) PsdFixture.I32(ms, row.Length); else PsdFixture.U16(ms, row.Length);
        }
        foreach (var row in rows) ms.Write(row);
        return ms.ToArray();
    }

    /// <summary>Delta-encode each row, which is what "ZIP with prediction" means.</summary>
    private static byte[] Predict(byte[] raw, int rowBytes, int depth)
    {
        var output = (byte[])raw.Clone();
        var height = rowBytes == 0 ? 0 : raw.Length / rowBytes;
        for (var y = 0; y < height; y++)
        {
            var row = y * rowBytes;
            if (depth == 16)
            {
                for (var x = rowBytes / 2 - 1; x >= 1; x--)
                {
                    var here = row + x * 2;
                    var prev = here - 2;
                    var delta = ((raw[here] << 8) | raw[here + 1]) - ((raw[prev] << 8) | raw[prev + 1]);
                    output[here] = (byte)(delta >> 8);
                    output[here + 1] = (byte)delta;
                }
            }
            else
            {
                for (var x = rowBytes - 1; x >= 1; x--)
                    output[row + x] = (byte)(raw[row + x] - raw[row + x - 1]);
            }
        }
        return output;
    }

    /// <summary>8-bit samples to big-endian 16-bit, duplicating the byte.</summary>
    private static byte[] Widen(byte[] samples)
    {
        var wide = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            wide[i * 2] = samples[i];
            wide[i * 2 + 1] = samples[i];
        }
        return wide;
    }
}

/// <summary>One channel's on-disk bytes: its id, and compression plus payload.</summary>
internal sealed record ChannelBlock(short Id, byte[] Bytes);
