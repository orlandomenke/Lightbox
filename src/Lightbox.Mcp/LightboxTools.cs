using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lightbox.Mcp;

/// <summary>
/// The MCP tools Claude sees. Pure forwarding: every call goes over the pipe
/// to the running Lightbox app, which validates and applies it through the
/// same paths as in-app editing (so everything is undoable there).
///
/// Stroke JSON format (used by get_frame_strokes, insert_inbetweens,
/// draw_strokes): a stroke is
///   { "tool": "brush"|"eraser", "color": "#rrggbb", "size": number,
///     "hardness": 0..1, "opacity": 0..1, "label": string|null,
///     "points": [{ "x": number, "y": number, "pressure": 0..1 }, ...] }
/// Coordinates are scene pixels, origin top-left.
/// </summary>
[McpServerToolType]
public static class LightboxTools
{
    private static async Task<string> Text(string op, object? payload, CancellationToken ct)
    {
        var result = await PipeBridge.CallAsync(op, payload, ct);
        return result.ValueKind == JsonValueKind.Undefined ? "{}" : result.GetRawText();
    }

    [McpServerTool(Name = "get_scene"), Description(
        "Get the Lightbox scene: canvas size, fps, frame count, current frame, " +
        "and the layers (id, name, kind, visibility, which frames are keyed). " +
        "Call this first to orient yourself.")]
    public static Task<string> GetScene(CancellationToken ct) =>
        Text("get_scene", null, ct);

    [McpServerTool(Name = "get_frame_strokes"), Description(
        "Get the strokes of the drawing exposed at a timeline frame (JSON, " +
        "same stroke format used for drawing). Optional layerId; defaults to " +
        "the active layer. Also returns keyIndex — the frame the drawing is " +
        "actually keyed on.")]
    public static Task<string> GetFrameStrokes(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null) =>
        Text("get_frame_strokes", new { frameIndex, layerId }, ct);

    [McpServerTool(Name = "render_frame"), Description(
        "Render a timeline frame to an image so you can SEE the drawing. " +
        "Use this to inspect keyframes before inbetweening and to check your " +
        "own results after inserting frames.")]
    public static async Task<ImageContentBlock> RenderFrame(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct)
    {
        var result = await PipeBridge.CallAsync("render_frame", new { frameIndex }, ct);
        var b64 = result.GetProperty("pngBase64").GetString()
                  ?? throw new LightboxOpException("No image returned.");
        return new ImageContentBlock { Data = Convert.FromBase64String(b64), MimeType = "image/png" };
    }

    [McpServerTool(Name = "insert_inbetweens"), Description(
        "Insert inbetween frames between keyframe aIndex and the next keyframe. " +
        "framesJson is a JSON array like " +
        "[{\"t\":0.5,\"strokes\":[...stroke objects...]}], with 0<t<1 being the " +
        "position between key A (t=0) and key B (t=1). Draw each inbetween as " +
        "a full drawing in the stroke format; preserve stroke labels across " +
        "frames; follow arcs, not straight lines. The insertion is one undo " +
        "step in Lightbox.")]
    public static async Task<string> InsertInbetweens(
        [Description("Frame index of keyframe A (must be keyed)")] int aIndex,
        [Description("JSON array of {t, strokes[]} inbetween frames")] string framesJson,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null)
    {
        var frames = ParseJsonArg(framesJson, "framesJson");
        return await Text("insert_inbetweens", new { aIndex, layerId, frames }, ct);
    }

    [McpServerTool(Name = "draw_strokes"), Description(
        "Append strokes to the drawing exposed at a timeline frame. " +
        "strokesJson is a JSON array of stroke objects (see stroke format). " +
        "Give each stroke a short semantic label (e.g. \"head-outline\") so " +
        "drawings can be inbetweened later. One undo step in Lightbox.")]
    public static async Task<string> DrawStrokes(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        [Description("JSON array of stroke objects")] string strokesJson,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null)
    {
        var strokes = ParseJsonArg(strokesJson, "strokesJson");
        return await Text("draw_strokes", new { frameIndex, layerId, strokes }, ct);
    }

    private static JsonElement ParseJsonArg(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new LightboxOpException($"{name} must be a JSON array.");
            return doc.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new LightboxOpException($"{name} is not valid JSON: {e.Message}");
        }
    }
}
