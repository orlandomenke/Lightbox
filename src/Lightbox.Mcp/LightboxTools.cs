using System.Buffers.Text;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [McpServerTool(Name = "import_character"), Description(
        "Import a character from an asset-library project into the open project: " +
        "its animations, its palette and its variants. Importing copies rather " +
        "than links. Importing the same character again merges: copies that came " +
        "from the library take its newer version, animations the library gained " +
        "are added, and documents made locally are never touched. Copies the " +
        "artist has edited since import are KEPT and reported under keptEdited " +
        "unless replaceEdited is true — replacing discards the artist's edits, " +
        "so pass it only when that is explicitly wanted. \"library\" is a path " +
        "to a library project or a folder holding several; omit it to search " +
        "the library folders the artist has configured.")]
    public static Task<string> ImportCharacter(
        string character, string? library = null, bool replaceEdited = false,
        CancellationToken ct = default)
        => Text("import_character", new { library, character, replaceEdited }, ct);

    [McpServerTool(Name = "get_scene"), Description(
        "Get the Lightbox scene: canvas size, fps, frame count, current frame, " +
        "and the layers (id, name, kind, visibility, which frames are keyed, " +
        "and the carve/effect state: hasMask, clipped, isAdjustment, " +
        "hasEffects — render_frame already shows all of them applied). " +
        "Call this first to orient yourself. It also reports which builds you " +
        "are talking to: appBuild is the running Lightbox, mcpBuild is this " +
        "server. If they disagree, or mcpBuild is missing entirely, this server " +
        "is an older published copy than the application — republish it and " +
        "restart the MCP client before trusting a bug to be fixed.")]
    public static async Task<string> GetScene(CancellationToken ct)
    {
        var result = await PipeBridge.CallAsync("get_scene", null, ct);
        return WithBuild(result);
    }

    /// <summary>
    /// Add this server's own build to a payload the app produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The app supplies <c>appBuild</c> and this adds <c>mcpBuild</c>, which
    /// makes the pair say something neither could alone. The asymmetry is
    /// deliberate and is the whole design: a server too old to have this method
    /// still forwards the app's key verbatim, so <b>a missing <c>mcpBuild</c> is
    /// itself the answer</b> — that server predates the stamp and is certainly
    /// stale. A version that can only report itself when it is new enough to
    /// report anything would be useless for exactly the case it exists for.
    /// </para>
    /// <para>
    /// Failure here is silent by design. A payload that is not an object, or a
    /// shape that changes later, must not cost the caller the scene it asked
    /// for — a diagnostic that can break the tool it annotates is a worse bug
    /// than the one it was added to diagnose.
    /// </para>
    /// </remarks>
    private static string WithBuild(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return Raw(payload);
        try
        {
            var node = JsonNode.Parse(payload.GetRawText())?.AsObject();
            if (node is null) return Raw(payload);
            node["mcpBuild"] = BuildInfo.Build;
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return Raw(payload);
        }
    }

    private static string Raw(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Undefined ? "{}" : payload.GetRawText();

    /// <summary>Test seam: <see cref="WithBuild"/> without a running app on the pipe.</summary>
    internal static string WithBuildForTests(JsonElement payload) => WithBuild(payload);

    /// <summary>
    /// Test seam: the blocks <see cref="RenderFrame"/> would return for a
    /// payload the app really produced, without a running app on the pipe.
    /// </summary>
    internal static IEnumerable<ContentBlock> RenderFrame_ForTests(JsonElement payload) =>
        ReductionNote(payload) is { } note ? [note, ImageBlock(payload)] : [ImageBlock(payload)];

    [McpServerTool(Name = "list_frame_strokes"), Description(
        "List what a drawing contains without its geometry: one line per stroke " +
        "with index, label, colour, pointCount and box [x,y,w,h]. About a " +
        "twelfth the size of get_frame_strokes — read this first, then fetch " +
        "geometry only for the strokes you will actually change. A box says " +
        "where a stroke is, never what it does: a straight line and a curve " +
        "between the same endpoints list almost identically, and strokes an " +
        "artist drew carry no label at all. Fetch the geometry before treating " +
        "a stroke as one you can carry over unchanged.")]
    public static Task<string> ListFrameStrokes(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null) =>
        Text("list_frame_strokes", new { frameIndex, layerId }, ct);

    [McpServerTool(Name = "get_frame_strokes"), Description(
        "Get stroke geometry from the drawing exposed at a timeline frame (JSON, " +
        "same stroke format used for drawing). Returns every stroke unless you " +
        "pass labels or indices, which fetch just those (indices are the ones " +
        "list_frame_strokes reports). A whole dense frame can run to tens of " +
        "thousands of tokens, so prefer naming what you need. Also returns " +
        "keyIndex — the frame the drawing is actually keyed on.")]
    public static Task<string> GetFrameStrokes(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer, required with labels/indices")] string? layerId = null,
        [Description("Stroke labels to fetch; omit for all")] string[]? labels = null,
        [Description("Stroke indices from list_frame_strokes; omit for all")] int[]? indices = null) =>
        Text("get_frame_strokes", new { frameIndex, layerId, labels, indices }, ct);

    [McpServerTool(Name = "render_frame"), Description(
        "Render a timeline frame to an image so you can SEE the drawing. " +
        "Use this to inspect keyframes before inbetweening and to check your " +
        "own results after inserting frames. Capped at 768px on the long edge: " +
        "enough for pose, timing and staging, and NOT enough for fine marks — " +
        "eyebrows, eyes and small hands thin out or vanish at the cap, more so " +
        "the larger the canvas. Pass longEdge 0 for the authored size whenever " +
        "you are judging detail that small, or a smaller number for a cheap look.")]
    public static async Task<IEnumerable<ContentBlock>> RenderFrame(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("Long-edge cap in px; omit for 768, 0 for authored size")] int? longEdge = null)
    {
        var result = await PipeBridge.CallAsync("render_frame", new { frameIndex, longEdge }, ct);
        var image = ImageBlock(result);
        return ReductionNote(result) is { } note ? [note, image] : [image];
    }

    /// <summary>
    /// A line saying the picture is smaller than the canvas, when it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Absent at full size, on purpose.</b> Saying "1.00×" on every
    /// unreduced render is the noise this surface is trying to remove, and it
    /// would train a reader to skip the line in the one case it matters.
    /// </para>
    /// <para>
    /// It exists because G12's art-director measured what the cap costs and the
    /// answer was not "a bit softer": at 768 a 1080p frame keeps its pose and
    /// loses 84% of the fine dark pixels on a face, and a 4K frame loses
    /// eyebrows and eyes entirely. The cap survives that finding because it is
    /// right for what the tool is mostly for — pose, timing, staging. What did
    /// not survive is the reduction being <em>silent</em>: an agent checking its
    /// own inbetween would see a browless head whether it had drawn one or not,
    /// and would have no way to know it was looking at a fifth of the picture.
    /// </para>
    /// </remarks>
    private static TextContentBlock? ReductionNote(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("scale", out var s)
            || s.GetDouble() >= 1.0)
        {
            return null;
        }
        static int Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
        return new TextContentBlock
        {
            Text = $"Shown at {Int(result, "width")}×{Int(result, "height")}, "
                   + $"{s.GetDouble():0.##}× of the {Int(result, "sceneWidth")}×{Int(result, "sceneHeight")} canvas. "
                   + "Pose, timing and staging are reliable at this size; fine marks — eyebrows, eyes, "
                   + "small hands — are thinned or gone. Re-render with longEdge 0 before judging those.",
        };
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

    [McpServerTool(Name = "set_key"), Description(
        "Make a timeline frame a key on a layer, creating an empty drawing if " +
        "that frame is currently a hold, or re-marking the role of the drawing " +
        "already there. Roles are key, breakdown or inbetween. A frameIndex " +
        "past the end of the timeline extends it. Use this to lay out an " +
        "exposure sheet before drawing into it; draw_strokes then fills a key " +
        "in. Returns created:true when the drawing is new. One undo step.")]
    public static Task<string> SetKey(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("key (default), breakdown or inbetween")] string? role = null,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null) =>
        Text("set_key", new { frameIndex, role, layerId }, ct);

    [McpServerTool(Name = "extend_exposure"), Description(
        "Hold the drawing exposed at a frame one frame longer, on this layer " +
        "only — the rest of the layer shifts right, other layers are untouched " +
        "(classic X-sheet behaviour). Use it to lengthen a single hold; use " +
        "set_exposure_step to re-time a whole range at once. One undo step.")]
    public static Task<string> ExtendExposure(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null) =>
        Text("extend_exposure", new { frameIndex, layerId }, ct);

    [McpServerTool(Name = "reduce_exposure"), Description(
        "Shorten the exposure at a frame by one, removing the hold directly " +
        "after it and pulling the layer left. A drawing is never removed, so " +
        "this fails rather than doing nothing when the next frame is itself a " +
        "key. One undo step.")]
    public static Task<string> ReduceExposure(
        [Description("Timeline frame index, 0-based")] int frameIndex,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null) =>
        Text("reduce_exposure", new { frameIndex, layerId }, ct);

    [McpServerTool(Name = "set_exposure_step"), Description(
        "Re-time a range of frames so every drawing in it is held for `step` " +
        "frames — step 2 is what an animator means by \"animating on 2s\". The " +
        "range gets longer and no drawing is lost; holds already inside it are " +
        "absorbed rather than multiplied, so applying step 2 twice stays on 2s " +
        "rather than landing on 4s. Returns the frames the range grew by. One " +
        "undo step.")]
    public static Task<string> SetExposureStep(
        [Description("First frame of the range, 0-based")] int from,
        [Description("Last frame of the range, 0-based")] int to,
        [Description("Frames to hold each drawing for; 2 = on 2s")] int step,
        CancellationToken ct,
        [Description("Layer id from get_scene; omit for the active layer")] string? layerId = null) =>
        Text("set_exposure_step", new { from, to, step, layerId }, ct);

    [McpServerTool(Name = "list_reference_views"), Description(
        "List the document's character sheets and their views (id, name, size). " +
        "Character sheets are reference art of the subject (front/side/…) that " +
        "lives outside the timeline — the authoritative design to keep frames " +
        "on-model and to draw parts of the subject that motion reveals.")]
    public static Task<string> ListReferenceViews(CancellationToken ct) =>
        Text("list_reference_views", null, ct);

    [McpServerTool(Name = "render_reference_view"), Description(
        "Render one character-sheet view to an image so you can SEE the " +
        "subject's design. Use it before drawing or inbetweening a character " +
        "so your strokes stay on-model.")]
    public static async Task<ImageContentBlock> RenderReferenceView(
        [Description("View id from list_reference_views")] string viewId,
        CancellationToken ct)
    {
        var result = await PipeBridge.CallAsync("render_reference_view", new { viewId }, ct);
        return ImageBlock(result);
    }

    /// <summary>
    /// Wrap a payload's <c>pngBase64</c> as the image block the client sees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ImageContentBlock.Data"/> is <b>the UTF-8 bytes of the base64
    /// text</b>, not the decoded image — the SDK writes those bytes straight
    /// into the JSON string, and exposes the decoded form separately as
    /// <c>DecodedData</c>. The name reads like the opposite, which is how this
    /// went wrong: both tools used to hand it
    /// <c>Convert.FromBase64String(b64)</c>, so raw PNG bytes were transcoded
    /// as UTF-8 onto the wire and every byte that is not valid UTF-8 — starting
    /// with the <c>0x89</c> in the PNG signature itself — became U+FFFD. The
    /// client then failed to base64-decode the result.
    /// </para>
    /// <para>
    /// It failed on every frame of every document, blank or not, at any canvas
    /// size, because nothing about it depends on the pixels: the first byte is
    /// already unrepresentable. Passing the text through untouched is the whole
    /// fix; the validity check keeps the guard the old decode gave by accident,
    /// without allocating a second copy of a 4K frame to get it.
    /// </para>
    /// </remarks>
    internal static ImageContentBlock ImageBlock(JsonElement result)
    {
        var b64 = result.ValueKind == JsonValueKind.Object
                  && result.TryGetProperty("pngBase64", out var png)
            ? png.GetString()
            : null;
        if (string.IsNullOrEmpty(b64))
            throw new LightboxOpException("No image returned.");
        if (!Base64.IsValid(b64.AsSpan()))
            throw new LightboxOpException("Lightbox returned an image that is not valid base64.");
        return new ImageContentBlock { Data = Encoding.UTF8.GetBytes(b64), MimeType = "image/png" };
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
