using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Serialization;

/// <summary>
/// Serializes the Frame hierarchy with a "kind" discriminator:
///   { "kind": "vector",  "id": ..., "strokes": [...] }
///   { "kind": "painted", "id": ..., "pngBase64": "...", "strokes": [...] }
/// Hand-rolled (rather than [JsonPolymorphic]) so the discriminator can sit
/// anywhere in the object — LLM-produced JSON doesn't guarantee key order.
/// </summary>
public sealed class FrameConverter : JsonConverter<Frame>
{
    public override Frame? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("kind", out var kindProp))
            throw new JsonException("Frame object is missing its \"kind\" discriminator.");

        var kind = kindProp.GetString();
        var json = root.GetRawText();
        return kind switch
        {
            "vector" => JsonSerializer.Deserialize<VectorFrame>(json, options),
            "painted" => JsonSerializer.Deserialize<PaintedFrame>(json, options),
            _ => throw new JsonException($"Unknown frame kind \"{kind}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Frame value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case VectorFrame v:
                writer.WriteString("kind", "vector");
                writer.WriteString("id", v.Id);
                writer.WritePropertyName("strokes");
                JsonSerializer.Serialize(writer, v.Strokes, options);
                break;
            case PaintedFrame p:
                writer.WriteString("kind", "painted");
                writer.WriteString("id", p.Id);
                writer.WriteString("pngBase64", p.PngBase64);
                writer.WritePropertyName("strokes");
                JsonSerializer.Serialize(writer, p.Strokes, options);
                break;
            default:
                throw new JsonException($"Unknown frame type {value.GetType().Name}.");
        }
        writer.WriteEndObject();
    }
}
