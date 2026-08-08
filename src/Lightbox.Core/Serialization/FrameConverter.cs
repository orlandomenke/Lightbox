using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Serialization;

/// <summary>
/// Reads and writes a <see cref="Frame"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than <c>[JsonPolymorphic]</c>, originally so the
/// discriminator could sit anywhere in the object — LLM-produced JSON does not
/// guarantee key order. It stays hand-rolled for a second reason that has
/// outlived the first: <b>this writer names every property it emits</b>, and that
/// is the whole of the "a document that never uses X serializes exactly as it did
/// before X existed" promise.
/// </para>
/// <para>
/// <b>The "kind" discriminator is read and ignored, and never written.</b> There
/// were two frame classes — <c>"vector"</c> and <c>"painted"</c> — and the split
/// was fiction: the vector one was the painted one minus its pixel baseline and
/// minus placements. They are one class now (see <see cref="Frame"/>), so there is
/// nothing left to discriminate. Read still accepts both spellings, because every
/// file written before the merge carries one, and an unknown value still throws:
/// <c>"kind": "hologram"</c> is a corrupt document, not a frame with no baseline.
/// </para>
/// <para>
/// <b>What that changes on disc, stated because it is a real format change.</b> A
/// document saved by this build no longer carries <c>"kind"</c>, and carries
/// <c>"pngBase64"</c> only when the drawing actually has imported pixels. Older
/// builds cannot read it. Every older file still opens here.
/// </para>
/// </remarks>
public sealed class FrameConverter : JsonConverter<Frame>
{
    /// <summary>The spellings the discriminator ever had.</summary>
    /// <remarks>
    /// Kept as a set rather than dropped entirely so an unknown kind is still an
    /// error. Ignoring the field outright would silently accept a corrupt or
    /// future-format document as an empty frame, which is the failure mode this
    /// converter has thrown on since it was written.
    /// </remarks>
    private static readonly HashSet<string> KnownKinds = ["vector", "painted"];

    public override Frame? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Absent is correct for anything this build wrote; present must still be
        // one of the two it used to write.
        if (root.TryGetProperty("kind", out var kindProp))
        {
            var kind = kindProp.GetString();
            if (kind is null || !KnownKinds.Contains(kind))
                throw new JsonException($"Unknown frame kind \"{kind}\".");
        }

        var frame = new Frame
        {
            Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? Ids.NewId("f") : Ids.NewId("f"),
        };

        if (root.TryGetProperty("role", out var role))
            frame.Role = JsonSerializer.Deserialize<FrameRole>(role.GetRawText(), options);

        // Empty and absent mean the same thing — no baseline — so an old file's
        // `"pngBase64": ""` normalises to null on the way in. Without this, every
        // pre-merge document would round-trip back out with the key it was
        // supposed to lose.
        if (root.TryGetProperty("pngBase64", out var png) && png.GetString() is { Length: > 0 } baseline)
            frame.PngBase64 = baseline;

        if (root.TryGetProperty("strokes", out var strokes))
            frame.Strokes = JsonSerializer.Deserialize<List<Stroke>>(strokes.GetRawText(), options) ?? [];

        if (root.TryGetProperty("anchors", out var anchors))
            frame.Anchors = JsonSerializer.Deserialize<Dictionary<string, AnchorPoint>>(anchors.GetRawText(), options);

        if (root.TryGetProperty("shapes", out var shapes))
            frame.Shapes = JsonSerializer.Deserialize<Dictionary<string, ShapeBox>>(shapes.GetRawText(), options);

        if (root.TryGetProperty("placements", out var placements))
            frame.Placements = JsonSerializer.Deserialize<List<SymbolPlacement>>(placements.GetRawText(), options);

        return frame;
    }

    public override void Write(Utf8JsonWriter writer, Frame value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);

        writer.WritePropertyName("role");
        JsonSerializer.Serialize(writer, value.Role, options);

        // Every optional block below is written only when used. A frame that was
        // never imported into, never anchored, never given a hitbox and never
        // placed on writes four fewer keys than one that was — which is what makes
        // an unused feature cost nothing in the file as well as at render time.
        if (value.HasBaseline)
            writer.WriteString("pngBase64", value.PngBase64);

        writer.WritePropertyName("strokes");
        JsonSerializer.Serialize(writer, value.Strokes, options);

        if (value.Anchors is { Count: > 0 })
        {
            writer.WritePropertyName("anchors");
            JsonSerializer.Serialize(writer, value.Anchors, options);
        }

        if (value.Shapes is { Count: > 0 })
        {
            writer.WritePropertyName("shapes");
            JsonSerializer.Serialize(writer, value.Shapes, options);
        }

        if (value.HasPlacements)
        {
            writer.WritePropertyName("placements");
            JsonSerializer.Serialize(writer, value.Placements, options);
        }

        writer.WriteEndObject();
    }
}
