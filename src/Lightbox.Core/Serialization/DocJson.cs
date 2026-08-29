using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Serialization;

/// <summary>
/// Save/load of Lightbox documents. camelCase, enums as camelCase strings,
/// so the format reads naturally to humans and LLMs alike — and, on disk,
/// inside a gzip container, which keeps that formatting without paying for it
/// (Q65: 6.4× on a 400-stroke painting). <see cref="Load"/> sniffs the
/// container, so every plain-JSON document written before the change loads
/// unchanged, forever.
/// </summary>
public static class DocJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions(indented: true);

    /// <summary>Compact variant for wire payloads (AI requests).</summary>
    public static readonly JsonSerializerOptions Compact = CreateOptions(indented: false);

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.Converters.Add(new FrameConverter());
        o.Converters.Add(new SymbolConverter());
        return o;
    }

    public static string Serialize(Doc doc) => JsonSerializer.Serialize(doc, Options);

    public static Doc Deserialize(string json) =>
        JsonSerializer.Deserialize<Doc>(json, Options)
        ?? throw new JsonException("Document deserialized to null.");

    /// <summary>Write the document as gzip-compressed JSON, atomically.</summary>
    /// <remarks>
    /// <para>
    /// The container is gzip; the content is exactly what <see cref="Serialize"/>
    /// produces — indented, camelCase, readable the moment it is gunzipped. Q65
    /// measured the alternatives before choosing this: a 400-stroke painting is
    /// 9.8 MB as plain indented JSON and 1.5 MB gzipped, while every deeper
    /// saving on the list was either worth ~0.1% after compression
    /// (deduplicating brush blocks) or a determinism break (merging strokes).
    /// Compressing the container is the whole of the cheap win, so it is the
    /// only thing this does.
    /// </para>
    /// <para>
    /// <see cref="CompressionLevel.Fastest"/> rather than Optimal, because a
    /// save can happen on the UI thread when the artist presses Ctrl+S and
    /// Q60's constraint — saving must not stall — outranks the last slice of
    /// ratio. <c>DocJsonCompressionTests</c> prints the achieved sizes so the
    /// trade stays visible rather than asserted.
    /// </para>
    /// <para>
    /// Serialized straight into the stream rather than through
    /// <see cref="Serialize"/>: the indented string for a large painting is
    /// ~10 MB of UTF-16 that would exist only to be compressed and discarded.
    /// </para>
    /// <para>
    /// Atomic for the same reason <see cref="WriteAtomic"/> is: temp file, then
    /// move, so the file on disk is always the previous document or the
    /// complete new one.
    /// </para>
    /// </remarks>
    public static void Save(Doc doc, string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            JsonSerializer.Serialize(gzip, doc, Options);
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Write to a temporary file and move it into place.
    /// </summary>
    /// <remarks>
    /// A plain <c>WriteAllText</c> truncates the target before it writes, so a
    /// crash, a full disk or a killed process mid-save leaves a half-written
    /// document where the artist's work used to be. The move is atomic on every
    /// platform we ship to, so the file on disk is always either the previous
    /// version or the complete new one — never a prefix of it.
    ///
    /// Shared with <c>ProjectIo</c> rather than duplicated: a project writes
    /// many files per save, and the one that is not safe is the one that
    /// eventually eats a scene.
    /// </remarks>
    public static void WriteAtomic(string path, string text)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Read a document, gzipped or plain, by sniffing the container.</summary>
    /// <remarks>
    /// Every document written before the container was compressed is plain JSON
    /// and must load forever — the file's own first two bytes decide, not its
    /// extension and not its age, so both kinds keep the same
    /// <c>.lightbox.json</c> name and nothing that stores a path had to change.
    /// </remarks>
    public static Doc Load(string path)
    {
        using var file = File.OpenRead(path);
        var isGzip = file.ReadByte() == 0x1f && file.ReadByte() == 0x8b;
        file.Position = 0;
        if (isGzip)
        {
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<Doc>(gzip, Options)
                ?? throw new JsonException("Document deserialized to null.");
        }
        using var reader = new StreamReader(file);
        return Deserialize(reader.ReadToEnd());
    }

    /// <summary>Deep clone via JSON round-trip.</summary>
    public static Doc Clone(Doc doc) => FromSnapshot(ToSnapshot(doc));

    /// <summary>
    /// Freeze a document into compact UTF-8 bytes — <see cref="Clone"/>'s write
    /// half.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compact, and UTF-8 bytes rather than a string: the serializer writes
    /// UTF-8 natively, so asking for a <see cref="string"/> transcodes the whole
    /// document into UTF-16 on the way out and back again on the way in, for a
    /// buffer that only ever goes to a parser. Going through <see cref="Serialize"/>
    /// instead would also mean <c>WriteIndented</c> — 13.6 MB of pretty-printed
    /// UTF-16 on a 5 000-stroke painting, none of it ever read by a human.
    /// </para>
    /// <para>
    /// These briefly held undo snapshots (B142's first fix froze the document
    /// here and thawed it lazily on the first Ctrl+Z); the undo path now takes
    /// <see cref="Doc.Clone"/>, which is cheaper than this method alone. What
    /// remains behind this pair is <see cref="Clone"/> — the round trip for
    /// callers where a copy must also prove it survives serialization.
    /// </para>
    /// </remarks>
    public static byte[] ToSnapshot(Doc doc) => JsonSerializer.SerializeToUtf8Bytes(doc, Compact);

    /// <summary>Rebuild a document from <see cref="ToSnapshot"/>'s bytes.</summary>
    public static Doc FromSnapshot(byte[] bytes) =>
        JsonSerializer.Deserialize<Doc>(bytes, Compact)
        ?? throw new JsonException("Document snapshot deserialized to null.");

    /// <summary>Deep clone of any part of a document, through the same converters.</summary>
    /// <remarks>
    /// The point of routing this through <see cref="Options"/> rather than
    /// hand-writing a copy per type: a <see cref="Layer"/> holds cels holding
    /// polymorphic frames, which only <see cref="FrameConverter"/> knows how to
    /// rebuild — and a field added to any of these is copied without anybody
    /// remembering to update a clone method.
    /// </remarks>
    public static T CloneValue<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
