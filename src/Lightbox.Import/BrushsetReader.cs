using System.IO.Compression;
using System.Xml;
using SkiaSharp;

namespace Lightbox.Import;

/// <summary>
/// Procreate brushes: a <c>.brushset</c> holding many, or a <c>.brush</c> holding one.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the file is</b> (<c>docs/DESIGN-procreate-import.md</c> has the whole
/// account and where it was learned). A zip. A brush is a folder — or, in a
/// <c>.brush</c>, the archive's root — holding <c>Brush.archive</c>, its settings as
/// a keyed binary property list, and optionally <c>Shape.png</c>, a greyscale image
/// of the tip in which white is paint. A set adds <c>brushset.plist</c>, an XML
/// property list naming the set and listing the folders in the artist's order.
/// </para>
/// <para>
/// <b>What comes across</b> is what <see cref="ImportedBrush"/> carries for every
/// format: the name, the tip, a size, a spacing and an opacity. A brush whose shape
/// is one of Procreate's own is stored as a reference to an image inside the app,
/// which is not in the file and is not ours to ship, so that brush arrives with no
/// tip and paints round. Grain, pressure and tilt response, taper and the wet-mix
/// values are read by nothing here.
/// </para>
/// <para>
/// <b>The numbers are a reading, not a calibration.</b> The format has no published
/// specification and nothing here was measured against Procreate itself. Size and
/// opacity are stored in the app's own units; <see cref="PixelsPerSizeUnit"/> is
/// this importer's choice of scale, made so the ten brushes of the first set an
/// artist handed over land between a pencil and an airbrush, and it is the first
/// thing to change when somebody compares the two side by side.
/// </para>
/// <para>
/// <b>Untrusted bytes (SENSITIVITY S1).</b> Nothing is written to disk, so an entry
/// name can never become a path. Every entry is inflated through a counter that
/// stops at a limit set by what that entry is for, whatever length the archive
/// declares; a shape's dimensions are read from its header before a pixel is
/// decoded; and a brush that cannot be read costs that brush and not the set.
/// </para>
/// </remarks>
public static class BrushsetReader
{
    /// <summary>The longest side a tip is kept at. Procreate's own shapes are 2048.</summary>
    public const int MaxTipSide = 1024;

    /// <summary>
    /// Diameter in pixels of a brush whose size works out to one Procreate unit.
    /// </summary>
    /// <remarks>See the type's remarks: chosen, not measured.</remarks>
    public const double PixelsPerSizeUnit = 100;

    private const int MaxEntries = 4096;
    private const int MaxBrushes = 1024;
    /// <summary>A real settings file is about three kilobytes.</summary>
    private const int MaxArchiveBytes = 1024 * 1024;

    /// <summary>How deep a manifest's XML may nest. A real one is four deep.</summary>
    private const int MaxManifestDepth = 8;

    /// <summary>
    /// How many characters of encoded tip one file may produce in all.
    /// </summary>
    /// <remarks>
    /// A tip is kept as base64 in the preset store, and one small archive can name a
    /// thousand brushes that each carry a megabyte of noise. Past this the remaining
    /// brushes arrive without their shapes. The owner's ten-brush set comes to 230 KB.
    /// Counted in characters, which are two bytes each in memory: 64 MB held.
    /// </remarks>
    internal const long DefaultTipBudget = 32L * 1024 * 1024;

    /// <summary>
    /// How many bytes of shape one file may have inflated in all, read or refused.
    /// </summary>
    /// <remarks>
    /// The tip budget counts what was produced. A shape that is decoded and then
    /// fails produces nothing and still costs the time, so the attempts are counted too.
    /// </remarks>
    private const long ShapeInflationBudget = 256L * 1024 * 1024;
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxShapeBytes = 32 * 1024 * 1024;
    private const int MaxShapeSide = 4096;

    /// <summary>Read every brush in a <c>.brushset</c> or <c>.brush</c>.</summary>
    /// <param name="fileName">Names a brush that does not name itself.</param>
    /// <exception cref="FormatException">The file is not one, or holds no readable brush.</exception>
    public static List<ImportedBrush> Read(string fileName, byte[] bytes) =>
        Read(fileName, bytes, DefaultTipBudget);

    internal static List<ImportedBrush> Read(string fileName, byte[] bytes, long tipBudget)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new FormatException("Not a Procreate brush file: it is not a zip archive.");
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries)
            {
                throw new FormatException("Not a Procreate brush file: it holds more entries than a brush set does.");
            }

            // Folder -> its entries. "" is the root, which is where a .brush keeps its one.
            var folders = new List<string>();
            var archives = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            var shapes = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            ZipArchiveEntry? manifest = null;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var slash = name.LastIndexOf('/');
                var folder = slash < 0 ? "" : name[..slash];
                var leaf = name[(slash + 1)..];
                // A brush folder is one plain name. Anything deeper, absolute or
                // climbing is not a brush, whatever it is called.
                if (folder.Contains('/') || folder is "." or ".." || folder.Contains(':')) continue;

                if (leaf == "Brush.archive" && archives.TryAdd(folder, entry)) folders.Add(folder);
                else if (leaf == "Shape.png") shapes.TryAdd(folder, entry);
                else if (folder.Length == 0 && leaf == "brushset.plist") manifest ??= entry;
            }

            var fallbackName = Path.GetFileNameWithoutExtension(fileName);
            var brushes = new List<ImportedBrush>();
            var shapeBytesLeft = ShapeInflationBudget;
            foreach (var folder in InTheArtistsOrder(folders, manifest).Take(MaxBrushes))
            {
                try
                {
                    shapes.TryGetValue(folder, out var shape);
                    if (tipBudget <= 0 || shapeBytesLeft <= 0) shape = null;
                    shapeBytesLeft -= shape?.Length ?? 0;
                    var brush = ReadBrush(archives[folder], shape, fallbackName);
                    tipBudget -= brush.TipPngBase64?.Length ?? 0;
                    brushes.Add(brush);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Partial import stays partial: this brush is lost, the set is not.
                    // Whatever it threw — the zip reader and the image decoder each
                    // have their own exceptions, and none of them is a reason to lose
                    // the brushes either side of this one.
                }
            }

            if (brushes.Count == 0)
            {
                throw new FormatException(folders.Count == 0
                    ? "Not a Procreate brush file: there is no Brush.archive in it."
                    : "None of the brushes in this file could be read.");
            }
            return brushes;
        }
    }

    private static ImportedBrush ReadBrush(ZipArchiveEntry archive, ZipArchiveEntry? shape, string fallbackName)
    {
        var settings = KeyedArchive.Root(Inflate(archive, MaxArchiveBytes));

        var name = settings.String("name");
        var size = settings.Number("paintSize", 0.5) * settings.Number("maxSize", 1) * PixelsPerSizeUnit;
        var spacing = settings.Number("plotSpacing", 0.1);
        var opacity = settings.Number("maxOpacity", 1);

        string? tip = null;
        if (shape is not null)
        {
            try
            {
                tip = Tip(Inflate(shape, MaxShapeBytes), inverted: settings.Flag("shapeInverted"));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A brush with an unreadable shape is still a brush; it paints round.
            }
        }

        return new ImportedBrush(
            string.IsNullOrWhiteSpace(name) ? fallbackName : name.Trim(),
            tip,
            SizePx: Clamp(size, 1, 500, otherwise: 24),
            // Procreate goes far denser than this engine does; its floor is ours.
            Spacing: Clamp(spacing, 0.02, 2, otherwise: 0.1),
            Opacity: Clamp(opacity, 0.01, 1, otherwise: 1));
    }

    /// <summary>A value held inside a range, or a default when it is not a number at all.</summary>
    private static double Clamp(double value, double min, double max, double otherwise) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : otherwise;

    /// <summary>
    /// The folders in the order the set's manifest lists them, then any it does not mention.
    /// </summary>
    private static IEnumerable<string> InTheArtistsOrder(List<string> folders, ZipArchiveEntry? manifest)
    {
        if (manifest is null) return folders;
        List<string> listed;
        try
        {
            listed = ListedFolders(Inflate(manifest, MaxManifestBytes));
        }
        catch (Exception e) when (e is XmlException or FormatException or InvalidDataException or IOException)
        {
            return folders;
        }

        var known = new HashSet<string>(folders, StringComparer.Ordinal);
        var ordered = listed.Where(known.Remove).ToList();
        ordered.AddRange(folders.Where(known.Contains));
        return ordered;
    }

    /// <summary>
    /// The strings of the manifest's <c>brushes</c> array, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An XML property list: <c>plist / dict / key</c> then the value, here an
    /// <c>array</c> of <c>string</c>. Read forwards with a reader, and refused past
    /// <see cref="MaxManifestDepth"/>.
    /// </para>
    /// <para>
    /// <b>Not through a document tree.</b> The first cut loaded an <c>XDocument</c>
    /// and asked elements for their <c>Value</c>, which walks an element's children
    /// by recursion: a manifest of forty thousand nested elements, 1.7 KB zipped,
    /// overflowed the stack, and a stack overflow cannot be caught — the application
    /// simply goes, with whatever was unsaved (sensitivity-guardian, in review).
    /// </para>
    /// <para>The DOCTYPE is skipped and never fetched: no resolver.</para>
    /// </remarks>
    private static List<string> ListedFolders(byte[] xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = MaxManifestBytes,
            IgnoreWhitespace = true,
            IgnoreComments = true,
        };
        using var reader = XmlReader.Create(new MemoryStream(xml), settings);
        var listed = new List<string>();
        var afterBrushesKey = false;
        var inArray = false;
        var arrayDepth = 0;
        while (reader.Read())
        {
            if (reader.Depth > MaxManifestDepth) throw new FormatException("The manifest nests deeper than one does.");
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (inArray && reader.Depth == arrayDepth) break;
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (inArray)
            {
                // A string directly inside the array, holding text and nothing else.
                if (reader.Depth == arrayDepth + 1 && reader.Name == "string" && !reader.IsEmptyElement
                    && reader.Read() && reader.NodeType == XmlNodeType.Text)
                {
                    listed.Add(reader.Value);
                }
            }
            else if (afterBrushesKey)
            {
                if (reader.Name != "array" || reader.IsEmptyElement) break;
                inArray = true;
                arrayDepth = reader.Depth;
            }
            else if (reader.Name == "key" && !reader.IsEmptyElement
                     && reader.Read() && reader.NodeType == XmlNodeType.Text && reader.Value == "brushes")
            {
                afterBrushesKey = true;
            }
        }
        return listed;
    }

    /// <summary>
    /// Inflate one entry, stopping at <paramref name="limit"/> bytes.
    /// </summary>
    /// <remarks>
    /// The entry's declared length is not consulted to decide how much to read — an
    /// archive may say anything — only to refuse early when it already admits to
    /// being too big.
    /// </remarks>
    private static byte[] Inflate(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new FormatException($"{entry.Name} is larger than a brush's would be.");
        using var source = entry.Open();
        return ReadBounded(source, limit, entry.Name);
    }

    /// <summary>Read a stream to its end, refusing it once it passes <paramref name="limit"/> bytes.</summary>
    internal static byte[] ReadBounded(Stream source, int limit, string what)
    {
        using var into = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (into.Length + read > limit) throw new FormatException($"{what} is larger than a brush's would be.");
            into.Write(buffer, 0, read);
        }
        return into.ToArray();
    }

    /// <summary>
    /// A shape image as a tip: white RGBA whose alpha is the shape's brightness.
    /// </summary>
    private static string Tip(byte[] png, bool inverted)
    {
        using var data = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(data) ?? throw new FormatException("The shape is not an image.");
        // Shape.png is a PNG. Bytes that are something else under that name would
        // otherwise reach every decoder the image library ships.
        if (codec.EncodedFormat != SKEncodedImageFormat.Png) throw new FormatException("The shape is not a PNG.");
        // From the header, before any pixel exists.
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width is <= 0 or > MaxShapeSide || height is <= 0 or > MaxShapeSide)
        {
            throw new FormatException("The shape is larger than a tip can be.");
        }

        using var grey = Brightness(codec, width, height);

        var scale = Math.Min(1.0, (double)MaxTipSide / Math.Max(width, height));
        var (w, h) = (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        using var sized = scale < 1
            ? grey.Resize(new SKImageInfo(w, h, SKColorType.Gray8), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
              ?? throw new FormatException("The shape could not be resized.")
            : null;
        var source = sized ?? grey;

        using var tip = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var from = source.GetPixelSpan();
        var to = tip.GetPixelSpan();
        var (fromStride, toStride) = (source.RowBytes, tip.RowBytes);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var value = from[y * fromStride + x];
                var o = y * toStride + x * 4;
                to[o] = to[o + 1] = to[o + 2] = 255;
                to[o + 3] = inverted ? (byte)(255 - value) : value;
            }
        }
        using var image = SKImage.FromBitmap(tip);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new FormatException("Could not encode brush tip.");
        return Convert.ToBase64String(encoded.AsSpan());
    }

    /// <summary>
    /// The image as one byte a pixel: how much paint is there.
    /// </summary>
    /// <remarks>
    /// Procreate writes shapes as plain greyscale, which decodes straight to this. A
    /// shape drawn on transparency, or saved in colour, cannot be decoded to grey
    /// directly, so it goes through RGBA and is weighed there: its brightness, times
    /// how visible the pixel is. That costs four bytes a pixel for a moment — 64 MB at
    /// the 4096 px limit — which is the largest thing this reader ever holds.
    /// </remarks>
    private static SKBitmap Brightness(SKCodec codec, int width, int height)
    {
        var grey = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8));
        try
        {
            if (codec.Info.ColorType == SKColorType.Gray8 && codec.Info.AlphaType == SKAlphaType.Opaque)
            {
                if (codec.GetPixels(grey.Info, grey.GetPixels()) != SKCodecResult.Success)
                {
                    throw new FormatException("The shape could not be decoded.");
                }
                return grey;
            }

            using var colour = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (codec.GetPixels(colour.Info, colour.GetPixels()) != SKCodecResult.Success)
            {
                throw new FormatException("The shape could not be decoded.");
            }
            var from = colour.GetPixelSpan();
            var to = grey.GetPixelSpan();
            var (fromStride, toStride) = (colour.RowBytes, grey.RowBytes);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * fromStride + x * 4;
                    // Rec. 601 weights, the usual reading of "how bright".
                    var luma = (from[i] * 299 + from[i + 1] * 587 + from[i + 2] * 114) / 1000;
                    to[y * toStride + x] = (byte)(luma * from[i + 3] / 255);
                }
            }
            return grey;
        }
        catch
        {
            grey.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The root object of a keyed archive, with its references followed.
    /// </summary>
    /// <remarks>
    /// A keyed archive is a property list of four entries: an object table
    /// (<c>$objects</c>), and <c>$top</c> naming which of them is the root. The root
    /// is a dictionary whose numbers and flags sit inline and whose strings are
    /// references back into the table, entry 0 being the string <c>$null</c>.
    /// </remarks>
    private sealed class KeyedArchive
    {
        private readonly List<object?> _objects;
        private readonly Dictionary<string, object?> _root;

        private KeyedArchive(List<object?> objects, Dictionary<string, object?> root)
        {
            _objects = objects;
            _root = root;
        }

        public static KeyedArchive Root(byte[] bytes)
        {
            if (BinaryPlist.Read(bytes) is not Dictionary<string, object?> top
                || top.GetValueOrDefault("$objects") is not List<object?> objects
                || top.GetValueOrDefault("$top") is not Dictionary<string, object?> tops
                || tops.GetValueOrDefault("root") is not PlistUid rootId
                || rootId.Index < 0 || rootId.Index >= objects.Count
                || objects[rootId.Index] is not Dictionary<string, object?> root)
            {
                throw new FormatException("The brush's settings are not a keyed archive.");
            }
            return new KeyedArchive(objects, root);
        }

        private object? Value(string key)
        {
            var value = _root.GetValueOrDefault(key);
            if (value is PlistUid id) value = id.Index >= 0 && id.Index < _objects.Count ? _objects[id.Index] : null;
            return value;
        }

        public string? String(string key) => Value(key) is string s && s != "$null" ? s : null;

        public double Number(string key, double otherwise) => Value(key) switch
        {
            double d => d,
            float f => f,
            long l => l,
            _ => otherwise,
        };

        public bool Flag(string key) => Value(key) is true;
    }
}
