using Lightbox.Import;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// PSDs crafted to break the reader, and the promise that none of them can.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these was a real defect.</b> An adversarial pass on
/// 2026-08-24 refuted the reader's safety claim four ways, and the existing
/// suite — thirty-five tests including a byte-by-byte truncation fuzz — caught
/// none of them, because it only ever <em>truncated</em> well-formed files. It
/// never corrupted a length field to a value that survives the length check,
/// which is the entire attack surface of a format built out of
/// attacker-supplied lengths.
/// </para>
/// <para>
/// The four, and the shape they share: three of them were an
/// attacker-controlled 64-bit PSB length surviving a bounds check and then being
/// truncated to a negative <c>int</c>. Once a cursor position goes negative,
/// <c>Pos + count &lt;= End</c> is true for every count, so the bounds check
/// stops being one and the next read indexes the array out of range. The fix is
/// in three places rather than one — <c>PsdCursor.Has</c> compares by
/// subtraction, <c>PsdCursor.Pos</c> refuses to leave its section at all, and
/// <c>PackBits</c> accumulates its scanline cursor in 64 bits — because each
/// closes the hole at a different distance from the caller.
/// </para>
/// <para>
/// The fourth was different in kind and is the more interesting one: a layer
/// mask is announced <em>twice</em> in a PSD, by a length field in the layer's
/// extra data and by a channel id in its channel table. The reader believed only
/// the first, so a layer carrying real mask pixels with <c>maskLength = 0</c>
/// imported as a plain opaque layer — silently discarding everything the mask
/// hid. That is precisely the failure refusing exists to prevent, reached from
/// the side nobody was watching.
/// </para>
/// </remarks>
public class PsdHostileInputTests(ITestOutputHelper output)
{
    /// <summary>
    /// The contract, in one place: a PSD may be read, or refused, or reported
    /// malformed. It may never do anything else.
    /// </summary>
    private void AssertHandledGracefully(byte[] bytes, string what)
    {
        var thrown = Record.Exception(() => PsdReader.Read(bytes)?.Dispose());
        output.WriteLine($"{what}: {thrown?.GetType().Name ?? "read without complaint"}"
            + (thrown is null ? "" : $" — {thrown.Message.Split('\n')[0]}"));

        Assert.True(
            thrown is null or FormatException or PsdUnsupportedException,
            $"{what} threw {thrown?.GetType().FullName}, which is neither a refusal nor a "
            + "format error — a malformed file must never crash the import.");
    }

    [Fact]
    public void AMaskChannelWithNoRectangleIsStillRefused()
    {
        // maskLength = 0 in the extra data, but a genuine channel -2 carrying
        // mask pixels. Believing only the length field imported this as a plain
        // opaque layer and threw the mask away without a word. Now that masks are
        // read, this file is still refused — for the narrower and correct reason
        // that its coverage has no rectangle to apply in.
        var bytes = BuildHostilePsd();

        var refused = Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(bytes));

        // Masks themselves are imported now (Q147). What is still refused is a
        // mask channel the file gave no rectangle for: coverage with no bounds
        // could apply anywhere, so there is nothing faithful to do with it.
        var reason = Assert.Single(refused.Reasons);
        Assert.Equal("A layer mask with no bounds recorded", reason.Feature);
        Assert.Equal("Attack", reason.LayerName);
    }

    [Fact]
    public void APsbChannelLengthThatTruncatesToANegativeIntIsRefused()
    {
        // 0x0000000080000000 is int.MinValue in its low 32 bits. The recovery
        // arithmetic cast it before using it, driving the cursor two billion
        // bytes negative and making every later bounds check vacuous.
        AssertHandledGracefully(BuildHostilePsb(), "PSB channel length truncating to int.MinValue");
    }

    [Fact]
    public void APsbRleRowLengthThatOverflowsTheScanlineCursorIsRefused()
    {
        // A PSB row-length table is int32 per row, so row 0 may claim
        // int.MaxValue bytes. `pos + length` wrapped negative, and the guard
        // only ever tested for "too large".
        AssertHandledGracefully(
            BuildHostilePsbWithRleChannel(), "PSB RLE row length of int.MaxValue");
    }

    [Fact]
    public void AnOverflowingLayerAndMaskSectionLengthCannotHideTheLayerStack()
    {
        // A section length of long.MaxValue overflowed the bounds check, then
        // truncated to -1 and moved the outer cursor *backwards*. The reader then
        // saw an empty section and returned zero layers — silently discarding
        // every layer in the file, mask and all, without an exception.
        var sound = BuildHostilePsd();
        Assert.Throws<PsdUnsupportedException>(() => PsdReader.Read(sound));

        var corrupted = (byte[])sound.Clone();
        var lengthAt = 26 + 4 + 4; // header, colour mode data, image resources
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(
            corrupted.AsSpan(lengthAt), long.MaxValue);

        var thrown = Record.Exception(() => PsdReader.Read(corrupted)?.Dispose());
        output.WriteLine($"corrupted section length: {thrown?.GetType().Name ?? "(none)"}");
        Assert.IsType<FormatException>(thrown);
    }

    [Fact]
    public void ALayerFarLargerThanItsCanvasIsRefused()
    {
        // The per-layer ceiling was no ceiling: layer bounds are independent of
        // the canvas, so four 10,000x7,000 layers — each comfortably under the
        // per-layer cap — asked for roughly 3 GB from an 800 KB file on a 4x4
        // document. Nothing summed them.
        const int width = 10_000;
        const int height = 7_000;
        const int layers = 4;

        var fixture = new PsdFixture { Width = 4, Height = 4 };
        for (var i = 0; i < layers; i++)
        {
            fixture.Layers.Add(PsdLayerFixture.Solid(
                $"Bomb{i}", 10, 20, 30,
                left: 0, top: 0, right: width, bottom: height,
                compression: PsdCompression.Zip));
        }
        var bytes = fixture.Build();
        output.WriteLine($"{bytes.Length:N0} bytes on disk claiming "
            + $"{(long)width * height * layers:N0} decoded pixels");

        var thrown = Assert.Throws<FormatException>(() => PsdReader.Read(bytes));

        output.WriteLine(thrown.Message);
        Assert.Contains("too far past the canvas", thrown.Message);
    }

    [Fact]
    public void OneLargeLayerIsStillAllowed()
    {
        // The budget must not have turned into a refusal of ordinary big files.
        // A single 4000x3000 layer is 12 megapixels — an unremarkable painting.
        var bytes = new PsdFixture
        {
            Width = 4000,
            Height = 3000,
            Layers =
            {
                PsdLayerFixture.Solid("Big but ordinary", 40, 50, 60, a: 255,
                    right: 4000, bottom: 3000, compression: PsdCompression.Zip),
            },
        }.Build();

        using var psd = PsdReader.Read(bytes);

        Assert.Single(psd.Layers);
        Assert.Equal(4000, psd.Layers[0].Pixels!.Width);
    }

    // ---- the hostile files ----------------------------------------------------

    private static byte[] BuildHostilePsd()
    {
        const int w = 2, h = 2;
        var ms = new MemoryStream();
        ms.Write("8BPS"u8);
        PsdFixture.U16(ms, 1); // version 1 = PSD
        ms.Write(new byte[6]);
        PsdFixture.U16(ms, 3); // channels (composite header count; unrelated to layer channel count)
        PsdFixture.I32(ms, h);
        PsdFixture.I32(ms, w);
        PsdFixture.U16(ms, 8); // depth
        PsdFixture.U16(ms, 3); // RGB

        PsdFixture.I32(ms, 0); // colour mode data
        PsdFixture.I32(ms, 0); // image resources

        // ---- layer and mask information section ----
        var layerInfo = new MemoryStream();
        PsdFixture.I16(layerInfo, 1); // one layer

        byte[] red = [255, 0, 0, 0];
        byte[] green = [0, 255, 0, 0];
        byte[] blue = [0, 0, 255, 0];
        byte[] mask = [128, 128, 128, 128]; // real mask pixel data, channel id -2

        byte[] Chan(byte[] raw)
        {
            var s = new MemoryStream();
            PsdFixture.U16(s, 0); // raw compression
            s.Write(raw);
            return s.ToArray();
        }
        var rC = Chan(red); var gC = Chan(green); var bC = Chan(blue); var mC = Chan(mask);

        // layer record
        PsdFixture.I32(layerInfo, 0); PsdFixture.I32(layerInfo, 0); // top, left
        PsdFixture.I32(layerInfo, h); PsdFixture.I32(layerInfo, w); // bottom, right
        PsdFixture.U16(layerInfo, 4); // channel count: R, G, B, mask
        PsdFixture.I16(layerInfo, 0); PsdFixture.I32(layerInfo, rC.Length);
        PsdFixture.I16(layerInfo, 1); PsdFixture.I32(layerInfo, gC.Length);
        PsdFixture.I16(layerInfo, 2); PsdFixture.I32(layerInfo, bC.Length);
        PsdFixture.I16(layerInfo, -2); PsdFixture.I32(layerInfo, mC.Length); // <-- real mask channel

        layerInfo.Write("8BIM"u8);
        layerInfo.Write("norm"u8);
        layerInfo.WriteByte(255); // opacity
        layerInfo.WriteByte(0);   // clipping
        layerInfo.WriteByte(0);   // flags (visible)
        layerInfo.WriteByte(0);   // filler

        var extra = new MemoryStream();
        PsdFixture.I32(extra, 0); // maskLength = 0 -- the field the reader actually checks
        PsdFixture.I32(extra, 0); // blending ranges
        var name = "Attack"u8.ToArray();
        extra.WriteByte((byte)name.Length);
        extra.Write(name);
        var written = 1 + name.Length;
        for (var pad = (4 - written % 4) % 4; pad > 0; pad--) extra.WriteByte(0);

        PsdFixture.I32(layerInfo, (int)extra.Length);
        extra.WriteTo(layerInfo);

        // channel bytes follow every record, in record/channel order
        layerInfo.Write(rC); layerInfo.Write(gC); layerInfo.Write(bC); layerInfo.Write(mC);
        if (layerInfo.Length % 2 != 0) layerInfo.WriteByte(0);

        var layerAndMask = new MemoryStream();
        PsdFixture.I32(layerAndMask, (int)layerInfo.Length);
        layerInfo.WriteTo(layerAndMask);
        PsdFixture.I32(layerAndMask, 0); // global layer mask info

        PsdFixture.I32(ms, (int)layerAndMask.Length);
        layerAndMask.WriteTo(ms);

        // ---- composite (flattened) image data, so the file is well-formed ----
        PsdFixture.U16(ms, 0); // raw
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < w * h; i++)
                ms.WriteByte(0);

        return ms.ToArray();
    }

    private static byte[] BuildHostilePsb()
    {
        const int w = 2, h = 2;
        var ms = new MemoryStream();
        ms.Write("8BPS"u8);
        PsdFixture.U16(ms, 2); // version 2 = PSB
        ms.Write(new byte[6]);
        PsdFixture.U16(ms, 3);
        PsdFixture.I32(ms, h);
        PsdFixture.I32(ms, w);
        PsdFixture.U16(ms, 8);
        PsdFixture.U16(ms, 3); // RGB

        PsdFixture.I32(ms, 0); // colour mode data (always narrow)
        PsdFixture.I32(ms, 0); // image resources (always narrow)

        var layerInfo = new MemoryStream();
        PsdFixture.I16(layerInfo, 1); // one layer

        PsdFixture.I32(layerInfo, 0); PsdFixture.I32(layerInfo, 0); // top, left
        PsdFixture.I32(layerInfo, h); PsdFixture.I32(layerInfo, w); // bottom, right
        PsdFixture.U16(layerInfo, 2); // two channels

        PsdFixture.I16(layerInfo, 0);
        PsdFixture.I64(layerInfo, 0x0000000080000000L); // truncates to int.MinValue
        PsdFixture.I16(layerInfo, 1);
        PsdFixture.I64(layerInfo, 2); // harmless second channel, never actually reached safely

        layerInfo.Write("8BIM"u8);
        layerInfo.Write("norm"u8);
        layerInfo.WriteByte(255);
        layerInfo.WriteByte(0);
        layerInfo.WriteByte(0);
        layerInfo.WriteByte(0);

        var extra = new MemoryStream();
        PsdFixture.I32(extra, 0); // maskLength
        PsdFixture.I32(extra, 0); // blending ranges
        var name = "A"u8.ToArray();
        extra.WriteByte((byte)name.Length);
        extra.Write(name);
        var written = 1 + name.Length;
        for (var pad = (4 - written % 4) % 4; pad > 0; pad--) extra.WriteByte(0);

        PsdFixture.I32(layerInfo, (int)extra.Length);
        extra.WriteTo(layerInfo);

        // Channel[0]'s real bytes: just the 2-byte "compression" field the
        // parser reads before it discovers the declared length is bogus.
        layerInfo.Write([0, 0]);
        // Channel[1]'s bytes are never legitimately reached; a couple of bytes
        // are present only so the file isn't truncated for unrelated reasons.
        layerInfo.Write([0, 0, 0, 0]);
        if (layerInfo.Length % 2 != 0) layerInfo.WriteByte(0);

        var layerAndMask = new MemoryStream();
        PsdFixture.I64(layerAndMask, layerInfo.Length); // layer info length: wide in a PSB
        layerInfo.WriteTo(layerAndMask);
        PsdFixture.I32(layerAndMask, 0); // global layer mask info (always narrow)

        PsdFixture.I64(ms, layerAndMask.Length); // layer & mask section length: wide in a PSB
        layerAndMask.WriteTo(ms);

        PsdFixture.U16(ms, 0); // composite: raw
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < w * h; i++)
                ms.WriteByte(0);

        return ms.ToArray();
    }

    private static byte[] BuildHostilePsbWithRleChannel()
    {
        const int w = 4, h = 2; // two scanlines, so row 1 is reached with a corrupted cursor

        // The RLE payload for one channel: a row-length table only. Row 0
        // claims int32.MaxValue compressed bytes; row 1 claims 10. Neither
        // needs to actually be present -- the crash happens while computing
        // where row 1 starts, before any of its bytes are read.
        var rle = new MemoryStream();
        PsdFixture.I32(rle, int.MaxValue); // row 0 length: forces the overflow
        PsdFixture.I32(rle, 10);           // row 1 length: arbitrary, never reached safely
        var rleBytes = rle.ToArray();

        byte[] Chan()
        {
            var s = new MemoryStream();
            PsdFixture.U16(s, 1); // compression = RLE
            s.Write(rleBytes);
            return s.ToArray();
        }
        var channel = Chan();

        var ms = new MemoryStream();
        ms.Write("8BPS"u8);
        PsdFixture.U16(ms, 2); // PSB
        ms.Write(new byte[6]);
        PsdFixture.U16(ms, 3);
        PsdFixture.I32(ms, h);
        PsdFixture.I32(ms, w);
        PsdFixture.U16(ms, 8);
        PsdFixture.U16(ms, 3); // RGB

        PsdFixture.I32(ms, 0);
        PsdFixture.I32(ms, 0);

        var layerInfo = new MemoryStream();
        PsdFixture.I16(layerInfo, 1);

        PsdFixture.I32(layerInfo, 0); PsdFixture.I32(layerInfo, 0);
        PsdFixture.I32(layerInfo, h); PsdFixture.I32(layerInfo, w);
        PsdFixture.U16(layerInfo, 1); // one channel: red only
        PsdFixture.I16(layerInfo, 0);
        PsdFixture.I64(layerInfo, channel.Length);

        layerInfo.Write("8BIM"u8);
        layerInfo.Write("norm"u8);
        layerInfo.WriteByte(255);
        layerInfo.WriteByte(0);
        layerInfo.WriteByte(0);
        layerInfo.WriteByte(0);

        var extra = new MemoryStream();
        PsdFixture.I32(extra, 0);
        PsdFixture.I32(extra, 0);
        var name = "A"u8.ToArray();
        extra.WriteByte((byte)name.Length);
        extra.Write(name);
        var written = 1 + name.Length;
        for (var pad = (4 - written % 4) % 4; pad > 0; pad--) extra.WriteByte(0);

        PsdFixture.I32(layerInfo, (int)extra.Length);
        extra.WriteTo(layerInfo);

        layerInfo.Write(channel);
        if (layerInfo.Length % 2 != 0) layerInfo.WriteByte(0);

        var layerAndMask = new MemoryStream();
        PsdFixture.I64(layerAndMask, layerInfo.Length);
        layerInfo.WriteTo(layerAndMask);
        PsdFixture.I32(layerAndMask, 0);

        PsdFixture.I64(ms, layerAndMask.Length);
        layerAndMask.WriteTo(ms);

        PsdFixture.U16(ms, 0); // composite: raw
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < w * h; i++)
                ms.WriteByte(0);

        return ms.ToArray();
    }
}
