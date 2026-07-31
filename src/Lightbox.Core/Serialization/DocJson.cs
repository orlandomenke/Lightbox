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

    public static void Save(Doc doc, string path) => File.WriteAllText(path, Serialize(doc));

    public static Doc Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>Deep clone via JSON round-trip (used for undo snapshots).</summary>
    public static Doc Clone(Doc doc) => Deserialize(Serialize(doc));
}
