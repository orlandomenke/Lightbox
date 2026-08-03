using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Serialization;

/// <summary>
/// Save/load of Lightbox documents. camelCase, enums as camelCase strings,
/// so the on-disk format reads naturally to humans and LLMs alike.
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
        return o;
    }

    public static string Serialize(Doc doc) => JsonSerializer.Serialize(doc, Options);

    public static Doc Deserialize(string json) =>
        JsonSerializer.Deserialize<Doc>(json, Options)
        ?? throw new JsonException("Document deserialized to null.");

    public static void Save(Doc doc, string path) => WriteAtomic(path, Serialize(doc));

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

    public static Doc Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>Deep clone via JSON round-trip (used for undo snapshots).</summary>
    public static Doc Clone(Doc doc) => Deserialize(Serialize(doc));

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
