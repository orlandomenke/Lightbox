using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Serialization;

/// <summary>
/// Reads a symbol written either way, and writes the narrower one whenever it
/// still says everything.
/// </summary>
/// <remarks>
/// <para>
/// <b>A symbol used to be a flat list of frames</b> — the time axis, with no
/// layer axis at all (Q171). It is now a stack of layers, each with its own
/// cels. Every symbol on disc today is the degenerate case of that: one layer,
/// carrying nothing but its drawings.
/// </para>
/// <para>
/// So this reads <c>frames</c> or <c>layers</c>, and <b>writes <c>frames</c>
/// unless there is actually a stack</b>. Every project in existence round-trips
/// byte-identically, and a <c>layers</c> key appears in a file exactly when
/// somebody has put a second layer in a symbol — which is the rule the camera,
/// the palettes and every optional block in this model already follow:
/// <i>absent unless used</i>.
/// </para>
/// <para>
/// <b>Two shapes on read and one on write is the deliberate half.</b> The
/// alternative — always write <c>layers</c> — is simpler code and would rewrite
/// every symbol file in every project the first time it was saved, for a feature
/// nobody in that project had used. <c>FrameConverter</c> made the same call
/// about <c>kind</c> and it is the same reasoning.
/// </para>
/// </remarks>
public sealed class SymbolConverter : JsonConverter<Symbol>
{
    public override Symbol Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var symbol = new Symbol();

        if (root.TryGetProperty("id", out var id) && id.GetString() is { } sid) symbol.Id = sid;
        if (root.TryGetProperty("name", out var name) && name.GetString() is { } sname) symbol.Name = sname;
        if (root.TryGetProperty("kind", out var kind)
            && Enum.TryParse<SymbolKind>(kind.GetString(), ignoreCase: true, out var parsed))
        {
            symbol.Kind = parsed;
        }
        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            symbol.Tags = [.. tags.EnumerateArray().Select(t => t.GetString()).OfType<string>()];
        }
        if (root.TryGetProperty("fps", out var fps) && fps.TryGetInt32(out var f)) symbol.Fps = f;
        if (root.TryGetProperty("pivotX", out var px) && px.TryGetDouble(out var x)) symbol.PivotX = x;
        if (root.TryGetProperty("pivotY", out var py) && py.TryGetDouble(out var y)) symbol.PivotY = y;
        if (root.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver)) symbol.Version = ver;

        // The stack, if this file has one.
        if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            symbol.Layers = layers.Deserialize<List<Layer>>(options) ?? [];
        }
        // The old shape: a flat frame list, which is one layer's worth of cels.
        // Read into exactly that, so an old symbol and a one-layer new one are
        // the same object in memory and cannot render differently.
        else if (root.TryGetProperty("frames", out var frames) && frames.ValueKind == JsonValueKind.Array)
        {
            var read = frames.Deserialize<List<Frame>>(options) ?? [];
            symbol.Layers = Symbol.Flat(symbol.Name, read);
        }

        return symbol;
    }

    public override void Write(Utf8JsonWriter writer, Symbol value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WritePropertyName("kind");
        JsonSerializer.Serialize(writer, value.Kind, options);
        if (value.Tags.Count > 0)
        {
            writer.WritePropertyName("tags");
            JsonSerializer.Serialize(writer, value.Tags, options);
        }
        writer.WriteNumber("fps", value.Fps);
        if (value.PivotX != 0) writer.WriteNumber("pivotX", value.PivotX);
        if (value.PivotY != 0) writer.WriteNumber("pivotY", value.PivotY);
        writer.WriteNumber("version", value.Version);

        if (IsFlat(value, out var only))
        {
            // The shape every existing file has. Written whenever it still says
            // everything, so a project that never made a stack never grows a key.
            writer.WritePropertyName("frames");
            writer.WriteStartArray();
            foreach (var cel in only!.Cels) JsonSerializer.Serialize(writer, cel.Frame, options);
            writer.WriteEndArray();
        }
        else
        {
            writer.WritePropertyName("layers");
            JsonSerializer.Serialize(writer, value.Layers, options);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Whether this symbol is still sayable as a flat frame list.
    /// </summary>
    /// <remarks>
    /// <b>One layer is not enough on its own.</b> A single layer that has been
    /// renamed, hidden, given an opacity or a blend mode carries intent the old
    /// shape has nowhere to put, and writing it flat would drop that silently on
    /// the next save. So the test is one layer <em>at its defaults</em>, and
    /// anything else — including a hold, which is a cel with no drawing — gets
    /// the stack.
    /// </remarks>
    private static bool IsFlat(Symbol symbol, out Layer? only)
    {
        only = null;
        if (symbol.Layers.Count != 1) return false;
        var layer = symbol.Layers[0];
        if (!layer.Visible || layer.Locked || layer.AlphaLocked || layer.IsBackground) return false;
        if (layer.Opacity != 1 || layer.BlendMode != LayerBlendMode.Normal) return false;
        if (layer.Mask is not null || layer.Effects is not null) return false;
        if (layer.OmitFromExport is not null || layer.ClipToBelow is not null || layer.Adjusts is not null) return false;
        if (layer.Depth is not null || layer.BoneId is not null || layer.SimId is not null) return false;
        if (layer.GroupId is not null || layer.LinkId is not null) return false;
        // A hold is a cel with no drawing, and a flat list cannot say "nothing
        // here" without also saying "the animation is shorter".
        if (layer.Cels.Any(c => c.Frame is null)) return false;
        only = layer;
        return true;
    }
}
