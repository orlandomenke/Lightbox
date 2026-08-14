using System.Text.Json;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

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

    public const string SubjectSystem =
        "You are reading a character design so an animation tool can reason " +
        "about it later. You receive the character's name and one or more " +
        "reference sheets drawn by the artist. Describe what the subject IS, " +
        "in the given JSON schema.\n" +
        "\n" +
        "- `kind` is what sort of thing this is: biped, quadruped, bird, " +
        "vehicle, prop. One lowercase word.\n" +
        "- `parts` names the pieces that move independently, the way an " +
        "animator would name them: head, torso, near-arm, far-arm, near-leg, " +
        "far-leg, tail. Short, lowercase, hyphenated.\n" +
        "- `parent` is the part a piece hangs off — a hand's parent is an arm, " +
        "an arm's parent is the torso. Leave it null for the root.\n" +
        "- `depth` is which part is normally in front when they overlap, " +
        "higher being nearer. A near arm is in front of the torso; a far arm " +
        "is behind it.\n" +
        "\n" +
        "Describe only what is true in EVERY drawing of this character. Do not " +
        "describe a pose, a position, or anything about one particular " +
        "picture — that changes frame to frame and is read separately. If the " +
        "sheets do not show enough to be sure of a part, leave it out: a short " +
        "correct answer is worth more here than a complete guess, because an " +
        "artist will be reading and correcting this.";

    public static string SubjectUser(SubjectRequest request) =>
        $"Read the character \"{request.CharacterName}\" from the attached " +
        "reference sheet(s). What is it, and what parts does it have?";

    /// <summary>
    /// The taxonomy block, rendered for the front of an inbetween request.
    /// </summary>
    /// <remarks>
    /// <b>At the front, and that is not a stylistic choice.</b> Prompt caching
    /// covers a <em>prefix</em>, and the taxonomy is the one part of an
    /// inbetween request that is identical across every frame of every
    /// animation of a character. Placed ahead of the frame data it is
    /// cacheable; placed after it, it saves nothing at all and the storage
    /// this feature exists for buys only the calls it avoids.
    /// </remarks>
    internal static string TaxonomyBlock(SubjectTaxonomy taxonomy) =>
        "The subject, true in every frame of this character:\n" +
        JsonSerializer.Serialize(new
        {
            kind = taxonomy.Kind,
            parts = taxonomy.Parts.Select(p => new { name = p.Name, parent = p.Parent, depth = p.Depth }),
        }, Compact) +
        "\nUse it to keep the inbetween on-model: keep the parts that exist, " +
        "respect which one is in front when they overlap, and draw what the " +
        "motion reveals rather than leaving a hole.\n\n";

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
        return (request.Taxonomy is { } taxonomy ? TaxonomyBlock(taxonomy) : "") +
               "Produce one inbetween frame for every t in `requestedTs` (t is the " +
               "position between keyframe A at t=0 and keyframe B at t=1):\n" +
               JsonSerializer.Serialize(payload, Compact) +
               (request.Repair is { Count: > 0 } repair ? RepairBlock(repair, request.Settled) : "");
    }

    /// <summary>
    /// What was wrong with the last answer, and the answer itself, appended for
    /// a repair re-ask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Appended rather than prepended, which is the opposite of
    /// <see cref="TaxonomyBlock"/> and for the same reason.</b> The taxonomy is
    /// identical across every frame of every animation of a character, so it
    /// belongs in the cacheable prefix. This block is different on every
    /// attempt by construction — it contains the answer being corrected — so
    /// putting it in front would move the point at which two attempts diverge
    /// from the very end of the message to the very beginning, and a repair
    /// would re-pay for the keys it just sent.
    /// </para>
    /// <para>
    /// It states the fault in Lightbox's own words rather than restating the
    /// rule that caught it. <c>InbetweenVerifier</c> writes refusals as
    /// sentences naming a stroke and a distance precisely so they can be
    /// forwarded, and rewording them here would give the model a second,
    /// vaguer description of a fault it has already been told about exactly.
    /// </para>
    /// </remarks>
    internal static string RepairBlock(
        IReadOnlyList<RefusedFrame> repair, IReadOnlyList<AcceptedFrame>? settled) =>
        "\n\nYour previous answer was checked and these frames were rejected. " +
        "Redraw only these, correcting the fault named for each. Everything " +
        "else about the request is unchanged.\n" +
        JsonSerializer.Serialize(
            repair.Select(r => new
            {
                t = r.T,
                fault = r.Fault,
                yourStrokes = r.Strokes.Select(StrokeWire.ToWire),
            }),
            Compact) +
        // The instruction and the data arrive together or neither does. An
        // earlier cut said "keep your corrections consistent with the frames
        // that were accepted" on every repair and never sent them — an
        // instruction the payload could not support, which is a request that
        // asks a model to guess and then measures whether it guessed right.
        (settled is { Count: > 0 }
            ? "\nThese frames of yours were accepted and are now fixed — they will not be " +
              "redrawn and your corrections have to sit smoothly between them, without " +
              "jumping off the path they establish:\n" +
              JsonSerializer.Serialize(
                  settled.Select(s => new { t = s.T, strokes = s.Strokes.Select(StrokeWire.ToWire) }),
                  Compact)
            : "");
}
