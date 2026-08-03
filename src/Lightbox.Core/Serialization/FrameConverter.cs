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
                WriteShared(writer, v, options);
                writer.WritePropertyName("strokes");
                JsonSerializer.Serialize(writer, v.Strokes, options);
                break;
            case PaintedFrame p:
                writer.WriteString("kind", "painted");
                writer.WriteString("id", p.Id);
                WriteShared(writer, p, options);
                writer.WriteString("pngBase64", p.PngBase64);
                writer.WritePropertyName("strokes");
                JsonSerializer.Serialize(writer, p.Strokes, options);
                // Only when something is placed. This writer names every
                // property it emits, so an absent key here is the whole of the
                // "a document that never places a symbol serializes exactly as
                // it did before symbols existed" promise.
                if (p.HasPlacements)
                {
                    writer.WritePropertyName("placements");
                    JsonSerializer.Serialize(writer, p.Placements, options);
                }
                break;
            default:
                throw new JsonException($"Unknown frame type {value.GetType().Name}.");
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Everything declared on <see cref="Frame"/> itself, for both kinds.
    /// </summary>
    /// <remarks>
    /// <b>This writer names every property it emits, so a field added to the base
    /// class is silently dropped until it is named here.</b> That is the price of
    /// the explicit writer — which is worth paying, because naming each key is
    /// also the whole of the "a document that never uses X serializes exactly as
    /// it did before X existed" promise. It cost one round-trip test to learn, and
    /// this method exists so the next base-class field is added in one place
    /// rather than two.
    /// </remarks>
    private static void WriteShared(Utf8JsonWriter writer, Frame frame, JsonSerializerOptions options)
    {
        writer.WritePropertyName("role");
        JsonSerializer.Serialize(writer, frame.Role, options);

        // Only when something is anchored, so an unanchored document writes no key.
        if (frame.Anchors is { Count: > 0 })
        {
            writer.WritePropertyName("anchors");
            JsonSerializer.Serialize(writer, frame.Anchors, options);
        }

        // Same rule for collision rectangles: a document that never needs a hitbox
        // writes no shape key.
        if (frame.Shapes is { Count: > 0 })
        {
            writer.WritePropertyName("shapes");
            JsonSerializer.Serialize(writer, frame.Shapes, options);
        }
    }
}
