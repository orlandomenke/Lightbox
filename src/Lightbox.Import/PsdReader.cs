using SkiaSharp;

namespace Lightbox.Import;

/// <summary>
/// Reads Photoshop documents (.psd) and their large-document variant (.psb)
/// into layers of RGBA pixels.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is supported:</b> RGB and Grayscale, 8 and 16 bits per channel, PSD
/// and PSB, raw / RLE / ZIP channel compression, layer name, visibility,
/// opacity, blend mode (the set <see cref="PsdBlend"/> names), locking, and
/// folders. A file with no layer section at all — a flattened PSD — is read from
/// its composite.
/// </para>
/// <para>
/// <b>What is refused, and why refusing is the behaviour:</b> masks, clipping
/// masks, adjustment and fill layers, text, smart objects and layer effects all
/// change what the pixels beneath them look like. Lightbox has no model for any
/// of them, so importing one means putting a drawing on screen that is not the
/// drawing the artist saved, with nothing to say so. The alternative considered
/// was to take Photoshop's own flattened composite for those layers, which
/// always looks right and silently discards the layer stack; the decision
/// (2026-08-24) was to refuse and name the reason instead. The cost is real and
/// was accepted knowingly: a great many production PSDs have an adjustment layer
/// or a mask somewhere and will not open until it is flattened.
/// </para>
/// <para>
/// <b>Nothing here throws an exception the format did not earn.</b> Malformed
/// bytes raise <see cref="FormatException"/>; a well-formed file we decline
/// raises <see cref="PsdUnsupportedException"/> carrying every reason at once.
/// </para>
/// </remarks>
public static class PsdReader
{
    /// <summary>Photoshop's own ceiling: 30,000 px per side for PSD, 300,000 for PSB.</summary>
    private const int MaxSidePsd = 30000;
    private const int MaxSidePsb = 300000;

    /// <summary>
    /// A ceiling on the pixels one file may decode in total, so a corrupt or
    /// hostile header cannot ask for an allocation that takes the application
    /// down. 512 megapixels is four times the largest canvas Photoshop's own PSD
    /// limit allows.
    /// </summary>
    private const long MaxPixels = 512L * 1024 * 1024;

    /// <summary>
    /// How much bigger than the canvas a single layer's own bounds may be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A total budget alone is not a bound, and this is the lesson of the case
    /// that got past one: layer bounds are independent of the canvas, so a 4×4
    /// document can declare four 10,000×7,000 layers — 280 megapixels, under any
    /// generous total — from an 800 KB file. Raising the total to catch that
    /// would refuse legitimate large paintings instead.
    /// </para>
    /// <para>
    /// The ratio is the honest bound, because it prices the thing that is
    /// actually suspicious. Content past the canvas edge is ordinary in
    /// Photoshop, and it is ordinary by a margin — a layer twice the canvas is
    /// unremarkable, one four billion times it is an amplification attack. Four
    /// is generous for the former and nowhere near the latter.
    /// </para>
    /// </remarks>
    private const long MaxLayerAreaOverCanvas = 4;

    /// <summary>
    /// A floor for the ratio above, so a small canvas is not a straitjacket. A
    /// 64×64 icon document may still hold a layer of a few megapixels.
    /// </summary>
    private const long MinLayerAreaAllowance = 4L * 1024 * 1024;

    private const int ModeGrayscale = 1;
    private const int ModeRgb = 3;

    public static PsdImage Read(byte[] data)
    {
        var cursor = new PsdCursor(data);
        if (cursor.Ascii(4) != "8BPS") throw new FormatException("PSD: not a Photoshop file (no 8BPS signature).");

        var version = cursor.U16();
        if (version is not (1 or 2))
            throw new FormatException($"PSD: unsupported version {version}.");
        var wide = version == 2;

        cursor.Skip(6); // reserved, must be zero
        var channels = cursor.U16();
        var height = cursor.I32();
        var width = cursor.I32();
        var depth = cursor.U16();
        var colorMode = cursor.U16();

        var maxSide = wide ? MaxSidePsb : MaxSidePsd;
        if (width <= 0 || height <= 0 || width > maxSide || height > maxSide)
            throw new FormatException($"PSD: implausible canvas {width}×{height}.");
        if ((long)width * height > MaxPixels)
            throw new FormatException($"PSD: canvas {width}×{height} is larger than Lightbox will decode.");

        var refusals = new List<PsdUnsupported>();
        var notes = new List<string>();
        CheckDocument(depth, colorMode, refusals, notes);

        cursor.Section(wide: false, out _); // colour mode data: palettes we refuse anyway
        cursor.Section(wide: false, out _); // image resources: thumbnails, guides, print setup

        var layerAndMask = cursor.Section(wide, out _);
        var records = ReadLayerSection(layerAndMask, wide, depth, refusals);

        // Refuse before decoding a single channel: the reasons all come from the
        // records, and decoding megabytes of pixels for a file we will decline is
        // work an artist waits through for nothing.
        if (refusals.Count > 0) throw new PsdUnsupportedException(refusals);

        var layers = new List<PsdLayer>(records.Count);
        try
        {
            // Budgeted across the whole file, not just per layer. A per-layer cap
            // alone is no cap: layer bounds are independent of the canvas, so a
            // 4x4 document can declare a dozen 10,000x7,000 layers, each under any
            // per-layer ceiling, and ask for gigabytes from an 800 KB file.
            var budget = new PixelBudget(width, height);
            foreach (var record in records)
            {
                layers.Add(BuildLayer(record, depth, colorMode, budget));
            }

            SKBitmap? composite = null;
            if (layers.TrueForAll(l => l.Pixels is null))
            {
                // A flattened PSD, or one whose only entries are folder markers.
                // The composite is then the whole picture and the only pixels.
                composite = ReadComposite(cursor, width, height, depth, colorMode, channels);
            }

            return new PsdImage(width, height, layers, composite) { Notes = notes };
        }
        catch
        {
            foreach (var layer in layers) layer.Pixels?.Dispose();
            throw;
        }
    }

    private static void CheckDocument(
        int depth, int colorMode, List<PsdUnsupported> refusals, List<string> notes)
    {
        if (colorMode is not (ModeRgb or ModeGrayscale))
        {
            refusals.Add(new PsdUnsupported(
                $"A {ColorModeName(colorMode)} document",
                null,
                "convert it to RGB (Image ▸ Mode ▸ RGB Color) and save again"));
        }

        switch (depth)
        {
            case 8:
                break;
            case 16:
                // Lossy and visually identical: Lightbox's document model is
                // 8-bit RGBA throughout, so this is the same conversion
                // Photoshop performs on Image ▸ Mode ▸ 8 Bits/Channel. Recorded
                // as a note rather than done silently — the artist should know
                // their 16-bit file came down, even though nothing on screen
                // will show it.
                notes.Add("16 bits per channel reduced to 8 — Lightbox paints in 8-bit RGBA.");
                break;
            default:
                refusals.Add(new PsdUnsupported(
                    depth == 32 ? "32 bits per channel (HDR)" : $"{depth} bits per channel",
                    null,
                    "convert it to 8 bits (Image ▸ Mode ▸ 8 Bits/Channel) and save again"));
                break;
        }
    }

    private static string ColorModeName(int mode) => mode switch
    {
        0 => "bitmap-mode",
        2 => "indexed-colour",
        4 => "CMYK",
        7 => "multichannel",
        8 => "duotone",
        9 => "Lab",
        _ => $"colour-mode-{mode}",
    };

    // ---- the layer and mask information section --------------------------------

    /// <summary>
    /// Walk the layer section: records first, then the channel bytes each names.
    /// </summary>
    /// <remarks>
    /// A 16- or 32-bit file leaves the ordinary layer-info length at zero and
    /// puts the whole stack in an <c>Lr16</c>/<c>Lr32</c> tagged block further
    /// down the same section. Missing that reads a 16-bit PSD as having no layers
    /// at all — which looks exactly like a flattened file rather than like a bug.
    /// </remarks>
    private static List<LayerRecord> ReadLayerSection(
        PsdCursor section, bool wide, int depth, List<PsdUnsupported> refusals)
    {
        if (section.Remaining == 0) return [];

        var layerInfoLength = section.Size(wide);
        if (layerInfoLength > 0)
        {
            var info = section.Bounded(layerInfoLength);
            return ReadLayerInfo(info, wide, depth, refusals);
        }

        section.Section(wide: false, out _); // global layer mask info
        while (section.Remaining >= 12)
        {
            var signature = section.Ascii(4);
            if (signature is not ("8BIM" or "8B64")) break;
            var key = section.Ascii(4);
            var length = section.Size(wide && IsWideLengthKey(key));
            if (!section.Has(length)) break;

            if (key is "Lr16" or "Lr32")
            {
                var info = section.Bounded(length);
                return ReadLayerInfo(info, wide, depth, refusals);
            }
            section.Skip(length);
            AlignToSignature(section);
        }
        return [];
    }

    private static List<LayerRecord> ReadLayerInfo(
        PsdCursor info, bool wide, int depth, List<PsdUnsupported> refusals)
    {
        // A negative count is Photoshop's flag that the merged result carries a
        // transparency channel; the magnitude is still the number of layers.
        int declared = info.I16();
        var count = declared == short.MinValue ? 0 : Math.Abs(declared);
        var records = new List<LayerRecord>(Math.Min(count, 4096));

        for (var i = 0; i < count; i++)
        {
            if (info.Remaining < 4) break;
            records.Add(ReadLayerRecord(info, wide, i, refusals));
        }

        // Channel bytes follow every record, in the same order.
        foreach (var record in records)
        {
            foreach (var channel in record.Channels)
            {
                if (!info.Has(2)) return records;
                var start = info.Pos;
                var compression = info.U16();
                var payload = channel.Length - 2;
                if (payload < 0 || !info.Has(payload))
                {
                    // The declared length is wrong. Skip what is left rather than
                    // seeking by it: `start + (int)channel.Length` truncates a
                    // crafted 64-bit PSB length to a negative offset, and a
                    // negative cursor makes every later bounds check vacuous.
                    info.Pos = info.End;
                    return records;
                }
                channel.Compression = compression;
                channel.DataStart = info.Pos;
                channel.DataLength = (int)payload;
                info.Skip(payload);
            }
            record.Depth = depth;
            record.Wide = wide;
            record.Data = info.Data;
        }
        return records;
    }

    private static LayerRecord ReadLayerRecord(
        PsdCursor info, bool wide, int index, List<PsdUnsupported> refusals)
    {
        var record = new LayerRecord
        {
            Top = info.I32(),
            Left = info.I32(),
            Bottom = info.I32(),
            Right = info.I32(),
        };

        var channelCount = info.U16();
        for (var c = 0; c < channelCount; c++)
        {
            var id = info.I16();
            var length = info.Size(wide);
            record.Channels.Add(new ChannelRef { Id = id, Length = length });
        }

        var signature = info.Ascii(4);
        if (signature != "8BIM")
            throw new FormatException($"PSD: layer {index} has blend signature \"{signature}\".");
        record.BlendKey = info.Ascii(4);
        record.Opacity = info.U8() / 255.0;
        var clipping = info.U8();
        var flags = info.U8();
        info.Skip(1); // filler

        record.Visible = (flags & 0x02) == 0;
        record.Locked = (flags & 0x01) != 0;

        var extraLength = info.I32();
        var extra = info.Bounded(extraLength);
        info.Skip(extraLength);

        var maskLength = extra.I32();
        if (maskLength > 0) record.HasMask = true;
        extra.Skip(Math.Min(maskLength, extra.Remaining));

        // A mask is announced two ways and both have to be believed. The extra
        // data above declares its size; the channel table declares the channel
        // that carries it (-2 user mask, -3 real mask). Trusting only the first
        // let a layer with genuine mask pixels and `maskLength = 0` import as a
        // plain opaque layer, silently dropping everything the mask hid — the
        // failure refusing exists to prevent, arrived at from the other side.
        foreach (var channel in record.Channels)
        {
            if (channel.Id is -2 or -3) record.HasMask = true;
        }

        var blendingRanges = extra.I32();
        extra.Skip(Math.Min(blendingRanges, extra.Remaining));

        record.Name = extra.PascalString(align: 4);
        ReadAdditionalInfo(extra, wide, record);

        record.Name = record.UnicodeName ?? record.Name;
        if (record.Name.Length == 0) record.Name = $"Layer {index + 1}";

        // Clipping is only a refusal on a layer that carries pixels; a folder
        // marker's clipping byte is meaningless and Photoshop writes it anyway.
        if (clipping == 1 && !record.IsGroupMarker)
        {
            refusals.Add(new PsdUnsupported(
                "A clipping mask", record.Name,
                "release it (Layer ▸ Release Clipping Mask) or merge the clipped layers together"));
        }
        if (record.HasMask)
        {
            refusals.Add(new PsdUnsupported(
                "A layer mask", record.Name,
                "apply it (Layer ▸ Layer Mask ▸ Apply)"));
        }
        foreach (var (feature, remedy) in record.UnsupportedFeatures)
        {
            refusals.Add(new PsdUnsupported(feature, record.Name, remedy));
        }
        if (!PsdBlend.IsSupported(record.BlendKey))
        {
            refusals.Add(new PsdUnsupported(
                $"The {PsdBlend.Describe(record.BlendKey)} blend mode", record.Name,
                "set the layer to a mode Lightbox shares, or merge it down"));
        }

        // A folder header carries the folder's own blend mode and opacity, and
        // Photoshop composites such a folder as one unit before blending it. A
        // Lightbox folder never does — its members stay ordinary layers in the
        // scene and compositing order is unchanged — so an isolated group would
        // render as something else entirely. Pass-through and plain Normal at full
        // opacity are the two that mean "no isolation", which is every folder
        // nobody has deliberately changed.
        if (record.SectionType is 1 or 2
            && (record.BlendKey is not ("pass" or "norm") || record.Opacity < 0.999))
        {
            refusals.Add(new PsdUnsupported(
                "A layer folder that blends as a group", record.Name,
                "set the folder to Pass Through at 100%, or merge it into one layer"));
        }
        return record;
    }

    /// <summary>
    /// Walk a layer's tagged blocks: the Unicode name, the folder brackets, and
    /// every marker that means "this layer is more than pixels".
    /// </summary>
    private static void ReadAdditionalInfo(PsdCursor extra, bool wide, LayerRecord record)
    {
        while (extra.Remaining >= 12)
        {
            var signature = extra.Ascii(4);
            if (signature is not ("8BIM" or "8B64")) break;
            var key = extra.Ascii(4);
            var length = extra.Size(wide && IsWideLengthKey(key));
            if (length < 0 || !extra.Has(length)) break;

            var block = extra.Bounded(length);
            switch (key)
            {
                case "luni":
                    record.UnicodeName = ReadUnicodeName(block);
                    break;
                case "lsct":
                    record.SectionType = block.Remaining >= 4 ? block.I32() : 0;
                    break;
                case "lspf":
                    // Protection flags: any of them makes the layer locked here,
                    // because Lightbox has one lock where Photoshop has three.
                    if (block.Remaining >= 4 && block.I32() != 0) record.Locked = true;
                    break;
                default:
                    if (PsdFeatures.Unsupported.TryGetValue(key, out var feature))
                        record.UnsupportedFeatures.Add(feature);
                    break;
            }

            extra.Skip(length);
            AlignToSignature(extra);
        }
    }

    private static string? ReadUnicodeName(PsdCursor block)
    {
        if (block.Remaining < 4) return null;
        var chars = block.I32();
        if (chars < 0 || !block.Has((long)chars * 2)) return null;
        var text = System.Text.Encoding.BigEndianUnicode.GetString(block.Data, block.Pos, chars * 2);
        return text.TrimEnd('\0');
    }

    /// <summary>
    /// Nudge past a tagged block's padding to the next signature.
    /// </summary>
    /// <remarks>
    /// Photoshop pads tagged-block data, and readers disagree about whether the
    /// pad is to 2 or to 4 — a disagreement that costs the whole rest of the
    /// block list if it is guessed wrong. Sniffing for the next <c>8BIM</c>
    /// instead is right under either convention, and stops after three bytes so
    /// it can never wander through real data.
    /// </remarks>
    private static void AlignToSignature(PsdCursor cursor)
    {
        for (var i = 0; i < 3; i++)
        {
            var peek = cursor.PeekAscii4();
            if (peek is "8BIM" or "8B64" || peek.Length < 4) return;
            cursor.Skip(1);
        }
    }

    /// <summary>Keys whose length field widens to 8 bytes in a PSB.</summary>
    private static bool IsWideLengthKey(string key) => key is
        "LMsk" or "Lr16" or "Lr32" or "Layr" or "Mt16" or "Mt32" or "Mtrn"
        or "Alph" or "FMsk" or "lnk2" or "FEid" or "FXid" or "PxSD" or "cinf";

    // ---- channels to pixels ----------------------------------------------------

    private static PsdLayer BuildLayer(LayerRecord record, int depth, int colorMode, PixelBudget budget)
    {
        var role = record.SectionType switch
        {
            1 => PsdLayerRole.GroupOpen,
            2 => PsdLayerRole.GroupClosed,
            3 => PsdLayerRole.GroupEnd,
            _ => PsdLayerRole.Raster,
        };

        var bitmap = role is PsdLayerRole.Raster
            ? DecodePixels(record, depth, colorMode, budget)
            : null;

        return new PsdLayer(
            record.Name, record.Left, record.Top, record.Right, record.Bottom,
            record.BlendKey, record.Opacity, record.Visible, record.Locked, role, bitmap);
    }

    /// <summary>
    /// Assemble a layer's channels into premultiplied RGBA at the layer's own size.
    /// </summary>
    private static SKBitmap? DecodePixels(LayerRecord record, int depth, int colorMode, PixelBudget budget)
    {
        var width = record.Right - record.Left;
        var height = record.Bottom - record.Top;
        if (width <= 0 || height <= 0) return null;

        budget.Take(record.Name, width, height);

        var samples = width * height;
        byte[]? red = null, green = null, blue = null, alpha = null;
        foreach (var channel in record.Channels)
        {
            // Mask channels (-2, -3) are skipped by their declared length; a file
            // that has them was refused before we got here.
            if (channel.Id is < -1 or > 2) continue;
            var plane = DecodeChannel(record, channel, width, height, depth);
            if (plane is null) continue;
            switch (channel.Id)
            {
                case -1: alpha = plane; break;
                case 0: red = plane; break;
                case 1: green = plane; break;
                case 2: blue = plane; break;
            }
        }

        if (colorMode == ModeGrayscale) { green = red; blue = red; }
        if (red is null && green is null && blue is null && alpha is null) return null;

        // Unpremultiplied, because that is exactly what Photoshop stores and a
        // reader's job is fidelity. The multiply into premultiplied space belongs
        // where these pixels are drawn onto a document surface — Skia does it
        // there, from the alpha type, more accurately than doing it twice here
        // would. Reading a channel back out of this bitmap therefore returns the
        // byte the file held, which is what makes the reader testable.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var pixels = new byte[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            var o = i * 4;
            pixels[o + 0] = red is null ? (byte)0 : red[i];
            pixels[o + 1] = green is null ? (byte)0 : green[i];
            pixels[o + 2] = blue is null ? (byte)0 : blue[i];
            pixels[o + 3] = alpha is null ? (byte)255 : alpha[i];
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private static byte[]? DecodeChannel(LayerRecord record, ChannelRef channel, int width, int height, int depth)
    {
        if (channel.DataStart < 0) return null;
        var bytesPerSample = depth / 8;
        var rowBytes = width * bytesPerSample;
        var data = record.Data!;
        var limit = channel.DataStart + channel.DataLength;

        var raw = channel.Compression switch
        {
            0 => Slice(data, channel.DataStart, rowBytes * height, limit),
            1 => PackBits.Decode(data, channel.DataStart, rowBytes, height, limit, record.Wide),
            2 => Inflate(data, channel.DataStart, channel.DataLength, rowBytes * height),
            3 => Unpredict(Inflate(data, channel.DataStart, channel.DataLength, rowBytes * height),
                    width, height, depth),
            _ => null,
        };
        if (raw is null) return null;
        return bytesPerSample == 1 ? raw : Narrow(raw, width * height);
    }

    private static byte[]? Slice(byte[] data, int start, int length, int limit)
    {
        if (length < 0 || start + length > Math.Min(limit, data.Length)) return null;
        return data.AsSpan(start, length).ToArray();
    }

    /// <summary>zlib-wrapped deflate, which is what PSD means by "ZIP".</summary>
    private static byte[]? Inflate(byte[] data, int start, int length, int expected)
    {
        if (length <= 0 || start + length > data.Length || expected <= 0) return null;
        try
        {
            using var source = new MemoryStream(data, start, length, writable: false);
            using var zlib = new System.IO.Compression.ZLibStream(
                source, System.IO.Compression.CompressionMode.Decompress);
            var output = new byte[expected];
            var read = 0;
            while (read < expected)
            {
                var n = zlib.Read(output, read, expected - read);
                if (n <= 0) break;
                read += n;
            }
            return read == 0 ? null : output;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Undo the per-row delta filter of PSD's "ZIP with prediction".
    /// </summary>
    private static byte[]? Unpredict(byte[]? data, int width, int height, int depth)
    {
        if (data is null) return null;
        if (depth == 8)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 1; x < width; x++)
                    data[row + x] = (byte)(data[row + x] + data[row + x - 1]);
            }
            return data;
        }
        if (depth == 16)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width * 2;
                for (var x = 1; x < width; x++)
                {
                    var here = row + x * 2;
                    var prev = here - 2;
                    var sum = ((data[prev] << 8) | data[prev + 1]) + ((data[here] << 8) | data[here + 1]);
                    data[here] = (byte)(sum >> 8);
                    data[here + 1] = (byte)sum;
                }
            }
            return data;
        }
        return null;
    }

    /// <summary>16-bit big-endian samples down to 8, by keeping the high byte.</summary>
    private static byte[] Narrow(byte[] wide, int samples)
    {
        var narrow = new byte[samples];
        for (var i = 0; i < samples && i * 2 < wide.Length; i++) narrow[i] = wide[i * 2];
        return narrow;
    }

    // ---- the flattened composite ----------------------------------------------

    /// <summary>
    /// The image data section: every channel in full-canvas planes, and for RLE a
    /// single row-length table covering all of them at once.
    /// </summary>
    private static SKBitmap? ReadComposite(
        PsdCursor cursor, int width, int height, int depth, int colorMode, int channels)
    {
        if (cursor.Remaining < 2 || channels <= 0) return null;
        var compression = cursor.U16();
        var bytesPerSample = depth / 8;
        var rowBytes = width * bytesPerSample;
        var samples = width * height;
        var planes = new List<byte[]>(channels);

        if (compression == 0)
        {
            for (var c = 0; c < channels; c++)
            {
                var plane = Slice(cursor.Data, cursor.Pos, rowBytes * height, cursor.End);
                if (plane is null) break;
                cursor.Skip(rowBytes * height);
                planes.Add(plane);
            }
        }
        else if (compression == 1)
        {
            var pos = cursor.Pos;
            var lengths = PackBits.ReadRowLengths(cursor.Data, ref pos, height * channels, wide: false);
            if (lengths is null) return null;
            for (var c = 0; c < channels; c++)
            {
                var rows = lengths.AsSpan(c * height, height).ToArray();
                var plane = PackBits.DecodeRows(cursor.Data, pos, rows, rowBytes, cursor.End);
                if (plane is null) break;
                foreach (var length in rows) pos += length;
                planes.Add(plane);
            }
        }
        else
        {
            return null;
        }

        if (planes.Count == 0) return null;
        var narrowed = planes.ConvertAll(p => bytesPerSample == 1 ? p : Narrow(p, samples));

        var isGray = colorMode == ModeGrayscale;
        var red = narrowed[0];
        var green = isGray ? red : narrowed.Count > 1 ? narrowed[1] : red;
        var blue = isGray ? red : narrowed.Count > 2 ? narrowed[2] : red;
        var alphaIndex = isGray ? 1 : 3;
        var alpha = narrowed.Count > alphaIndex ? narrowed[alphaIndex] : null;

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var pixels = new byte[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            var o = i * 4;
            pixels[o + 0] = At(red, i);
            pixels[o + 1] = At(green, i);
            pixels[o + 2] = At(blue, i);
            pixels[o + 3] = alpha is null ? (byte)255 : alpha[i];
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private static byte At(byte[] plane, int i) => i < plane.Length ? plane[i] : (byte)0;

    // ---- parsing state ---------------------------------------------------------

    /// <summary>
    /// What one file is allowed to decode: a per-layer bound relative to the
    /// canvas, and a running total across every layer.
    /// </summary>
    /// <remarks>
    /// Two bounds because they catch different things. The ratio stops one layer
    /// claiming absurd bounds on a small canvas; the total stops a file made of
    /// thousands of individually reasonable layers. Either alone was got past.
    /// </remarks>
    private sealed class PixelBudget(int canvasWidth, int canvasHeight)
    {
        private readonly long _perLayer = Math.Max(
            MinLayerAreaAllowance, (long)canvasWidth * canvasHeight * MaxLayerAreaOverCanvas);

        private long _remaining = MaxPixels;

        public void Take(string layerName, int width, int height)
        {
            var area = (long)width * height;
            if (area > _perLayer)
                throw new FormatException(
                    $"PSD: layer \"{layerName}\" is {width}×{height} on a "
                    + $"{canvasWidth}×{canvasHeight} canvas — too far past the canvas to be real.");
            if (area > _remaining)
                throw new FormatException(
                    $"PSD: the layers add up to more than Lightbox will decode for one file "
                    + $"(stopped at \"{layerName}\").");
            _remaining -= area;
        }
    }

    private sealed class ChannelRef
    {
        public short Id;
        public long Length;
        public int Compression = -1;
        public int DataStart = -1;
        public int DataLength;
    }

    private sealed class LayerRecord
    {
        public int Top, Left, Bottom, Right;
        public string Name = "";
        public string? UnicodeName;
        public string BlendKey = "norm";
        public double Opacity = 1;
        public bool Visible = true;
        public bool Locked;
        public bool HasMask;
        public int SectionType;
        public int Depth = 8;
        public bool Wide;
        public byte[]? Data;
        public List<ChannelRef> Channels { get; } = [];
        public List<(string Feature, string Remedy)> UnsupportedFeatures { get; } = [];
        public bool IsGroupMarker => SectionType is 1 or 2 or 3;
    }
}
