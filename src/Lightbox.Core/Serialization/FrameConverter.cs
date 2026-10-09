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

    /// <remarks>
    /// <b>Read straight off the reader, once.</b> It used to parse each drawing
    /// into a <c>JsonDocument</c>, then copy every field back out as a UTF-16
    /// string and parse that again: 4.5 s of a 30-layer, 200-drawing document's
    /// open (2026-10-09, <c>FrameReadCostTests</c>). Key order still does not
    /// matter, and a key this build does not know is skipped.
    /// </remarks>
    public override Frame? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"A frame is an object, not {reader.TokenType}.");

        var frame = new Frame();
        string? id = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                // Absent is correct for anything this build wrote; present must
                // still be one of the two it used to write.
                case "kind":
                    var kind = reader.GetString();
                    if (kind is null || !KnownKinds.Contains(kind))
                        throw new JsonException($"Unknown frame kind \"{kind}\".");
                    break;
                case "id":
                    id = reader.GetString();
                    break;
                case "role":
                    frame.Role = JsonSerializer.Deserialize<FrameRole>(ref reader, options);
                    break;
                // Empty and absent mean the same thing — no baseline — so an old
                // file's `"pngBase64": ""` normalises to null on the way in.
                // Without this, every pre-merge document would round-trip back
                // out with the key it was supposed to lose.
                case "pngBase64":
                    if (reader.GetString() is { Length: > 0 } baseline) frame.PngBase64 = baseline;
                    break;
                case "strokes":
                    frame.Strokes = JsonSerializer.Deserialize<List<Stroke>>(ref reader, options) ?? [];
                    break;
                case "anchors":
                    frame.Anchors = JsonSerializer.Deserialize<Dictionary<string, AnchorPoint>>(ref reader, options);
                    break;
                case "shapes":
                    frame.Shapes = JsonSerializer.Deserialize<Dictionary<string, ShapeBox>>(ref reader, options);
                    break;
                case "placements":
                    frame.Placements = JsonSerializer.Deserialize<List<SymbolPlacement>>(ref reader, options);
                    break;
                case "ai":
                    frame.Ai = JsonSerializer.Deserialize<AiProvenance>(ref reader, options);
                    break;
                case "chart":
                    frame.Chart = JsonSerializer.Deserialize<List<double>>(ref reader, options);
                    break;
                case "correctives":
                    frame.Correctives = JsonSerializer.Deserialize<List<Corrective>>(ref reader, options);
                    break;
                // Derived pixels, so a checkpoint that will not parse is dropped
                // rather than thrown on: the drawing still renders, from the
                // record, exactly as it would have. B137's rule, applied to the
                // one field in the document that is allowed to be missing. Held
                // as a document first, so a bad one cannot leave the reader
                // half way through it.
                case "checkpoint":
                    using (var checkpoint = JsonDocument.ParseValue(ref reader))
                    {
                        try
                        {
                            frame.Checkpoint = checkpoint.RootElement.Deserialize<StrokeCheckpoint>(options);
                        }
                        catch (JsonException)
                        {
                            frame.Checkpoint = null;
                        }
                    }
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        frame.Id = id ?? Ids.NewId("f");
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

        if (value.Ai is not null)
        {
            writer.WritePropertyName("ai");
            JsonSerializer.Serialize(writer, value.Ai, options);
        }

        if (value.Chart is { Count: > 0 })
        {
            writer.WritePropertyName("chart");
            JsonSerializer.Serialize(writer, value.Chart, options);
        }

        if (value.HasCorrectives)
        {
            writer.WritePropertyName("correctives");
            JsonSerializer.Serialize(writer, value.Correctives, options);
        }

        if (value.Checkpoint is { IsUsable: true } snapshot)
        {
            writer.WritePropertyName("checkpoint");
            JsonSerializer.Serialize(writer, snapshot, options);
        }

        writer.WriteEndObject();
    }
}
