using System.Text.Json;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai;

/// <summary>
/// Prompt builders — pure functions so they are unit-testable without a
/// network. The user payload is compact JSON in the same shape the model
/// must produce, which measurably improves schema adherence.
/// </summary>
public static class Prompts
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
    };

    public const string InbetweenSystem =
        "You are a professional animation inbetweener working inside a drawing " +
        "application. You receive two keyframes of a hand-drawn animation as " +
        "stroke JSON plus scene metadata, and produce the requested inbetween " +
        "frames in the given JSON schema.\n" +
        "\n" +
        "Principles of good inbetweening you must apply:\n" +
        "- Preserve the drawing's structure: keep stroke count and stroke roles " +
        "consistent with the keyframes. When strokes carry a `label`, keep the " +
        "same label on the corresponding inbetween stroke.\n" +
        "- Follow arcs, not straight lines: when the motion between the keys " +
        "implies rotation or a swing (limbs, heads, pendulums, tails), move " +
        "points along curved trajectories.\n" +
        "- Respect volume: shapes may squash and stretch but should read as the " +
        "same object at every t.\n" +
        "- Respect the requested easing when placing each t's drawing between " +
        "the keys.\n" +
        "- Coordinates are in scene pixels with the origin at the top-left. " +
        "Keep drawings inside the scene bounds.\n" +
        "- Match each keyframe stroke's color, size, hardness, and opacity in " +
        "your inbetween strokes unless the two keys differ, in which case " +
        "blend them.\n" +
        "- If character-sheet reference images are attached, they are the " +
        "authoritative design of the subject: keep every inbetween on-model " +
        "with them, and use them to draw parts of the subject that motion " +
        "reveals (e.g. a limb swinging away exposing the body behind it).";

    public static string InbetweenUser(InbetweenRequest request)
    {
        var payload = new
        {
            scene = new { width = request.Scene.Width, height = request.Scene.Height, fps = request.Scene.Fps },
            easing = request.Easing.ToString().ToLowerInvariant(),
            requestedTs = request.Ts,
            keyframeA = new { strokes = request.KeyframeA.Select(StrokeWire.ToWire) },
            keyframeB = new { strokes = request.KeyframeB.Select(StrokeWire.ToWire) },
        };
        return "Produce one inbetween frame for every t in `requestedTs` (t is the " +
               "position between keyframe A at t=0 and keyframe B at t=1):\n" +
               JsonSerializer.Serialize(payload, Compact);
    }
}
