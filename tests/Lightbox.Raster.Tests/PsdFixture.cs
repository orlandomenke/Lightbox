using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Builds real .psd / .psb bytes in memory, the way <c>BrushImportTests</c>
/// builds .abr and .gbr bytes.
/// </summary>
/// <remarks>
/// <para>
/// A format reader is only as trustworthy as the files it is tested against, and
/// there is no Photoshop here to make them. Writing them by hand is the same
/// choice the brush-format tests already made, and it has one property a
/// committed binary blob does not: <b>the fixture states what it contains</b>, so
/// a test that fails says which field it disagreed about instead of "the bytes
/// changed".
/// </para>
/// <para>
/// The obvious hazard is that a hand-built fixture and a hand-written reader are
/// wrong <em>the same way</em>, which would leave every test green over a parser
/// no real file survives. So these were cross-checked against <c>psd_tools</c>, an
/// independent implementation, on 2026-08-24 via <see cref="PsdFixtureDump"/>: it
/// opens every fixture and reports the same canvas, depth, layer names, bounds,
/// visibility, opacity, blend modes and pixel values this reader does — the
/// translucent layer included, at <c>(200,100,50,128)</c> unpremultiplied in both.
/// </para>
/// <para>
/// <b>That check immediately found a real defect</b>, which is the argument for
/// doing it: every fixture here omitted the trailing image data section, and
/// <c>psd_tools</c> rejects such a file as corrupt. The reader's tests had all
/// been passing against PSDs no other application would open. The reverse
/// direction was checked too — a PSD written by <c>psd_tools</c> from a PIL image
/// reads back through <see cref="Lightbox.Import.PsdReader"/> pixel-for-pixel.
/// </para>
/// </remarks>
internal sealed class PsdFixture
{
    public int Width { get; init; } = 4;

    public int Height { get; init; } = 4;

    /// <summary>Bits per channel: 8 and 16 are readable, 32 is refused.</summary>
    public int Depth { get; init; } = 8;

    /// <summary>3 = RGB, 1 = Grayscale, 4 = CMYK (refused), and so on.</summary>
    public int ColorMode { get; init; } = 3;

    /// <summary>True writes a .psb, whose section and channel lengths are 64-bit.</summary>
    public bool Psb { get; init; }

    /// <summary>Channels in the flattened composite at the end of the file.</summary>
    public int CompositeChannels { get; init; } = 4;

    /// <summary>The colour every composite pixel gets in the flattened image.</summary>
    public byte[] CompositeFill { get; init; } = [0, 0, 0, 0];

    /// <summary>
    /// Leave out the image data section entirely, which no real PSD does.
    /// </summary>
    /// <remarks>
    /// Every Photoshop file ends with a flattened composite, even one whose layers
    /// carry all the content — <c>psd_tools</c> rejects a file without it outright,
    /// which is how this fixture's first draft was caught writing PSDs no other
    /// implementation would open. Kept as a switch only so the reader's tolerance
    /// of a truncated tail stays tested.
    /// </remarks>
    public bool OmitComposite { get; init; }

    public List<PsdLayerFixture> Layers { get; init; } = [];

    public byte[] Build()
    {
        var ms = new MemoryStream();
        ms.Write("8BPS"u8);
        U16(ms, Psb ? 2 : 1);
        ms.Write(new byte[6]);
        U16(ms, CompositeChannels);
        I32(ms, Height);
        I32(ms, Width);
        U16(ms, Depth);
        U16(ms, ColorMode);

        I32(ms, 0); // colour mode data
        I32(ms, 0); // image resources

        var layerAndMask = BuildLayerAndMask();
        // Both this length and the layer-info length inside it widen to 64 bits
        // in a PSB, which is the difference that makes a PSB reader a PSB reader.
        Size(ms, layerAndMask.Length);
        ms.Write(layerAndMask);

        if (!OmitComposite) WriteComposite(ms);
        return ms.ToArray();
    }

    private byte[] BuildLayerAndMask()
    {
        if (Layers.Count == 0) return [];
        var section = new MemoryStream();
        var info = BuildLayerInfo();
        Size(section, info.Length);
        section.Write(info);
        I32(section, 0); // global layer mask info
        return section.ToArray();
    }

    private byte[] BuildLayerInfo()
    {
        var info = new MemoryStream();
        I16(info, (short)Layers.Count);

        // Every record first, then every channel's bytes: the two-pass layout
        // that makes a PSD reader walk the section twice.
        var channelData = new List<byte[]>();
        foreach (var layer in Layers)
        {
            var blocks = layer.ChannelBlocks(Depth, Psb);
            WriteRecord(info, layer, blocks);
            channelData.AddRange(blocks.Select(b => b.Bytes));
        }
        foreach (var bytes in channelData) info.Write(bytes);
        if (info.Length % 2 != 0) info.WriteByte(0);
        return info.ToArray();
    }

    private void WriteRecord(MemoryStream info, PsdLayerFixture layer, List<ChannelBlock> blocks)
    {
        I32(info, layer.Top);
        I32(info, layer.Left);
        I32(info, layer.Bottom);
        I32(info, layer.Right);

        U16(info, blocks.Count);
        foreach (var block in blocks)
        {
            I16(info, block.Id);
            Size(info, block.Bytes.Length);
        }

        info.Write("8BIM"u8);
        info.Write(Encoding.ASCII.GetBytes(layer.BlendKey));
        info.WriteByte(layer.Opacity);
        info.WriteByte(layer.Clipping);
        info.WriteByte((byte)(layer.Visible ? 0 : 0x02));
        info.WriteByte(0); // filler

        var extra = new MemoryStream();
        I32(extra, layer.MaskLength);
        extra.Write(new byte[layer.MaskLength]);
        I32(extra, 0); // blending ranges
        WritePascal(extra, layer.Name);
        if (layer.SectionType is not null) WriteTagged(extra, "lsct", Int32Bytes(layer.SectionType.Value));
        if (layer.UnicodeName is not null) WriteTagged(extra, "luni", UnicodeNameBytes(layer.UnicodeName));
        if (layer.ProtectionFlags is not null) WriteTagged(extra, "lspf", Int32Bytes(layer.ProtectionFlags.Value));
        foreach (var key in layer.ExtraKeys) WriteTagged(extra, key, new byte[4]);

        I32(info, (int)extra.Length);
        extra.WriteTo(info);
    }

    private void WriteComposite(MemoryStream ms)
    {
        U16(ms, 0); // raw
        var bytesPerSample = Depth / 8;
        for (var c = 0; c < CompositeChannels; c++)
        {
            var value = c < CompositeFill.Length ? CompositeFill[c] : (byte)255;
            for (var i = 0; i < Width * Height; i++)
            {
                ms.WriteByte(value);
                for (var extra = 1; extra < bytesPerSample; extra++) ms.WriteByte(value);
            }
        }
    }

    // ---- primitives ------------------------------------------------------------

    private void Size(Stream s, long value)
    {
        if (Psb) I64(s, value); else I32(s, (int)value);
    }

    internal static void U16(Stream s, int v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v);
        s.Write(b);
    }

    internal static void I16(Stream s, short v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        s.Write(b);
    }

    internal static void I32(Stream s, int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b);
    }

    internal static void I64(Stream s, long v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        s.Write(b);
    }

    private static byte[] Int32Bytes(int v)
    {
        var ms = new MemoryStream();
        I32(ms, v);
        return ms.ToArray();
    }

    private static byte[] UnicodeNameBytes(string name)
    {
        var ms = new MemoryStream();
        I32(ms, name.Length);
        ms.Write(Encoding.BigEndianUnicode.GetBytes(name));
        return ms.ToArray();
    }

    private static void WritePascal(Stream s, string name)
    {
        var bytes = Encoding.Latin1.GetBytes(name);
        s.WriteByte((byte)bytes.Length);
        s.Write(bytes);
        var written = 1 + bytes.Length;
        for (var pad = (4 - written % 4) % 4; pad > 0; pad--) s.WriteByte(0);
    }

    /// <summary>An <c>8BIM</c> tagged block, padded to four bytes as Photoshop writes them.</summary>
    private static void WriteTagged(Stream s, string key, byte[] data)
    {
        s.Write("8BIM"u8);
        s.Write(Encoding.ASCII.GetBytes(key));
        I32(s, data.Length);
        s.Write(data);
        for (var pad = (4 - data.Length % 4) % 4; pad > 0; pad--) s.WriteByte(0);
    }

    // ---- compression -----------------------------------------------------------

    /// <summary>
    /// PackBits a scanline, preferring a repeat run wherever three bytes match.
    /// </summary>
    /// <remarks>
    /// Emitting only literals would be valid PackBits and would leave the
    /// decoder's repeat branch untested, so this deliberately produces both kinds
    /// of run.
    /// </remarks>
    internal static byte[] PackRow(ReadOnlySpan<byte> row)
    {
        var output = new MemoryStream();
        var i = 0;
        while (i < row.Length)
        {
            var run = 1;
            while (i + run < row.Length && run < 128 && row[i + run] == row[i]) run++;
            if (run >= 3)
            {
                output.WriteByte((byte)(sbyte)(1 - run));
                output.WriteByte(row[i]);
                i += run;
                continue;
            }
            var literal = 1;
            while (i + literal < row.Length && literal < 128)
            {
                // Stop a literal run just before three equal bytes start.
                if (i + literal + 2 < row.Length
                    && row[i + literal] == row[i + literal + 1]
                    && row[i + literal] == row[i + literal + 2]) break;
                literal++;
            }
            output.WriteByte((byte)(literal - 1));
            output.Write(row.Slice(i, literal));
            i += literal;
        }
        return output.ToArray();
    }

    internal static byte[] Zlib(byte[] raw)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }
        return output.ToArray();
    }
}
