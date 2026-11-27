using System.Buffers.Binary;

namespace Lightbox.Import.Unity;

/// <summary>
/// One sprite cut from a texture, in both conventions.
/// </summary>
/// <param name="UnityX">Left edge, pixels — the same in both conventions.</param>
/// <param name="UnityY">Bottom edge measured from the texture's <b>bottom</b>, as Unity stores it.</param>
/// <param name="Top">Top edge measured from the texture's <b>top</b>, as Lightbox measures.</param>
/// <param name="PivotX">Normalised within the rect, from the left.</param>
/// <param name="PivotY">Normalised within the rect, from the <b>bottom</b> (Unity's).</param>
public sealed record UnitySpriteSlice(
    string Name,
    long InternalId,
    int UnityX,
    int UnityY,
    int Width,
    int Height,
    int Top,
    double PivotX,
    double PivotY)
{
    /// <summary>The pivot as a pixel in texture space, top-left origin.</summary>
    public (double X, double Y) PivotPixel => (UnityX + PivotX * Width, Top + (1 - PivotY) * Height);
}

/// <summary>A texture's sprite slicing, read from its <c>.meta</c>.</summary>
/// <param name="Problems">Sprites that could not be used, each named with why.</param>
public sealed record UnityTextureMeta(
    string Guid,
    int SpriteMode,
    double PixelsPerUnit,
    int TextureWidth,
    int TextureHeight,
    IReadOnlyList<UnitySpriteSlice> Slices,
    IReadOnlyList<string> Problems)
{
    public const int ModeSingle = 1;
    public const int ModeMultiple = 2;

    public UnitySpriteSlice? Find(long internalId) => Slices.FirstOrDefault(s => s.InternalId == internalId);
}

/// <summary>Reads a <c>TextureImporter</c> <c>.meta</c> for its sprites.</summary>
public static class UnityTextureMetaReader
{
    /// <summary>Sprites per texture above this are refused (S1).</summary>
    public const int MaxSprites = 10_000;

    /// <param name="assetName">The texture's file name without extension — a Single-mode sprite's name.</param>
    /// <param name="width">The texture's pixel width, read from the image itself.</param>
    /// <param name="height">The texture's pixel height.</param>
    /// <exception cref="FormatException">Naming <paramref name="label"/>.</exception>
    public static UnityTextureMeta Read(string text, string label, string assetName, int width, int height)
    {
        var root = UnityYaml.Parse(text, label).FirstOrDefault()?.Root
            ?? throw new FormatException($"{label}: empty.");
        var guid = root["guid"]?.Text ?? "";
        if (!UnityAnimReader.IsGuid(guid)) throw new FormatException($"{label}: no guid.");
        var importer = root["TextureImporter"]
            ?? throw new FormatException($"{label}: not a texture (no TextureImporter).");

        var mode = importer["spriteMode"] is { } m && m.TryLong(out var mv) ? (int)mv : 0;
        var ppu = importer["spritePixelsToUnits"] is { } p && p.TryDouble(out var pv) && pv > 0 ? pv : 100;

        var slices = new List<UnitySpriteSlice>();
        var problems = new List<string>();

        if (mode == UnityTextureMeta.ModeSingle)
        {
            var (px, py) = Pivot(importer["alignment"], importer["spritePivot"]);
            slices.Add(new UnitySpriteSlice(assetName, UnitySpriteRef.SingleSpriteFileId,
                0, 0, width, height, 0, px, py));
        }
        else if (mode == UnityTextureMeta.ModeMultiple)
        {
            var sprites = importer.At("spriteSheet.sprites");
            if (sprites is { Kind: UnityYamlNode.NodeKind.Sequence })
            {
                if (sprites.Items.Count > MaxSprites)
                    throw new FormatException($"{label}: more than {MaxSprites} sprites.");
                var legacyIds = LegacyIds(importer["internalIDToNameTable"]);
                foreach (var s in sprites.Items)
                {
                    var name = s["name"]?.Text ?? "";
                    if (!TryRect(s["rect"], out var x, out var y, out var w, out var h))
                    {
                        problems.Add($"'{name}': its rect is not readable");
                        continue;
                    }
                    if (x < 0 || y < 0 || x + w > width || y + h > height)
                    {
                        problems.Add($"'{name}': its rect ({x}, {y}, {w}×{h}) is outside the {width}×{height} texture");
                        continue;
                    }
                    long id;
                    if (s["internalID"] is { } idNode && idNode.TryLong(out var iv) && iv != 0) id = iv;
                    else if (legacyIds.TryGetValue(name, out var lv)) id = lv;
                    else
                    {
                        problems.Add($"'{name}': no internal id, so no clip can point at it");
                        continue;
                    }
                    var (px, py) = Pivot(s["alignment"], s["pivot"]);
                    slices.Add(new UnitySpriteSlice(name, id, x, y, w, h, height - y - h, px, py));
                }
            }
        }
        return new UnityTextureMeta(guid, mode, ppu, width, height, slices, problems);
    }

    // Unity's rects are floats in the file; sprite rects are whole pixels in practice.
    private static bool TryRect(UnityYamlNode? r, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (r is null) return false;
        if (!Whole(r["x"], out x) || !Whole(r["y"], out y) || !Whole(r["width"], out w) || !Whole(r["height"], out h))
            return false;
        return w > 0 && h > 0;
    }

    private static bool Whole(UnityYamlNode? n, out int v)
    {
        v = 0;
        if (n is null || !n.TryDouble(out var d) || Math.Abs(d) > 1 << 20) return false;
        v = (int)Math.Round(d);
        return true;
    }

    /// <summary>Unity's <c>SpriteAlignment</c>, 0–8, as a normalised pivot; 9 is custom.</summary>
    private static (double X, double Y) Pivot(UnityYamlNode? alignment, UnityYamlNode? custom)
    {
        var a = alignment is not null && alignment.TryLong(out var av) ? av : 0;
        return a switch
        {
            1 => (0, 1),
            2 => (0.5, 1),
            3 => (1, 1),
            4 => (0, 0.5),
            5 => (1, 0.5),
            6 => (0, 0),
            7 => (0.5, 0),
            8 => (1, 0),
            9 when custom?["x"] is { } cx && cx.TryDouble(out var x)
                && custom["y"] is { } cy && cy.TryDouble(out var y) => (x, y),
            _ => (0.5, 0.5),
        };
    }

    // Older importers keep ids in a table: - first: {213: id} second: name
    private static Dictionary<string, long> LegacyIds(UnityYamlNode? table)
    {
        var ids = new Dictionary<string, long>();
        if (table is not { Kind: UnityYamlNode.NodeKind.Sequence }) return ids;
        foreach (var e in table.Items)
        {
            var name = e["second"]?.Text;
            if (name is null || e["first"]?["213"] is not { } idNode || !idNode.TryLong(out var id)) continue;
            ids.TryAdd(name, id);
        }
        return ids;
    }
}

/// <summary>Reads a PNG's size from its header without decoding it.</summary>
public static class PngHeader
{
    /// <summary>The largest side accepted (S1). Unity's own limit is 16384.</summary>
    public const int MaxSide = 65536;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <exception cref="FormatException">Not a PNG, truncated, or an absurd size.</exception>
    public static (int Width, int Height) ReadSize(Stream stream, string label)
    {
        Span<byte> head = stackalloc byte[24];
        var read = 0;
        while (read < head.Length)
        {
            var n = stream.Read(head[read..]);
            if (n == 0) break;
            read += n;
        }
        if (read < head.Length || !head[..8].SequenceEqual(Signature) || !head[12..16].SequenceEqual("IHDR"u8))
            throw new FormatException($"{label}: not a PNG.");
        var w = BinaryPrimitives.ReadUInt32BigEndian(head[16..]);
        var h = BinaryPrimitives.ReadUInt32BigEndian(head[20..]);
        if (w is 0 or > MaxSide || h is 0 or > MaxSide)
            throw new FormatException($"{label}: {w}×{h} is not a usable texture size.");
        return ((int)w, (int)h);
    }
}
