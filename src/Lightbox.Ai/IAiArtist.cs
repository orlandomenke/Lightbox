using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

namespace Lightbox.Ai;

public sealed record SceneInfo(int Width, int Height, int Fps);

public sealed record InbetweenRequest(
    SceneInfo Scene,
    IReadOnlyList<Stroke> KeyframeA,
    IReadOnlyList<Stroke> KeyframeB,
    IReadOnlyList<double> Ts,
    Easing Easing,
    IReadOnlyList<string>? ReferenceImages = null,
    SubjectTaxonomy? Taxonomy = null,
    IReadOnlyList<RefusedFrame>? Repair = null,
    IReadOnlyList<AcceptedFrame>? Settled = null);

/// <summary>
/// A frame from an earlier attempt that passed and is now fixed — context for
/// a repair, never something to redraw.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sent only when a refusal needs it, because it is the expensive kind of
/// context.</b> Most faults name a stroke and a distance the model can act on
/// with the keys and its own rejected drawing alone.
/// <see cref="InbetweenFault.Incoherent"/> cannot: <i>“it jitters against the
/// frames beside it”</i> is defined entirely against frames the re-ask would
/// otherwise not carry, so a model told that and shown nothing can only guess.
/// It is the one fault in the set that fails the *would an inbetweener know
/// what to change* test.
/// </para>
/// <para>
/// So these ride along on a coherence repair and on nothing else. Sending them
/// always would put roughly another whole frame's strokes on every re-ask for
/// faults that had no use for them — see <c>DESIGN-ai-payload.md</c>, where
/// strokes are the token cost and there is no cheaper honest substitute for
/// showing a drawing.
/// </para>
/// </remarks>
public sealed record AcceptedFrame(double T, IReadOnlyList<Stroke> Strokes);

/// <summary>
/// A frame the verifier refused, handed back to the model so the next ask is a
/// correction rather than another roll of the dice.
/// </summary>
/// <remarks>
/// <para>
/// <b>The strokes are the reason this record exists.</b> The fault already
/// reads like a sentence a person could act on — <i>“the ‘near-arm’ did not
/// stay between the keys — it sits 60px from where the motion puts it”</i> —
/// but it names a stroke in a drawing the model can no longer see, and a model
/// asked to fix a stroke it cannot see has to redraw the frame from the keys
/// again. Sending its own rejected answer back turns the re-ask into an edit.
/// </para>
/// <para>
/// It costs roughly one extra frame's strokes beside the two keys, on the
/// repair call only — measured against <c>DESIGN-ai-payload.md</c>'s rule that
/// strokes are what tokens are made of. That is the price of the choice and it
/// is paid only by runs that were about to lose the frame entirely.
/// </para>
/// </remarks>
public sealed record RefusedFrame(double T, IReadOnlyList<Stroke> Strokes, string Fault);

/// <summary>
/// Ask what a character is, from the sheets the artist drew of it.
/// </summary>
/// <remarks>
/// The taxonomy half of a subject reading — once per character, not once per
/// frame. That distinction is the whole economic argument: a 24-frame cycle
/// pays for this once instead of twenty-four times, and the second animation
/// of the same character pays nothing.
/// </remarks>
public sealed record SubjectRequest(
    string CharacterName,
    IReadOnlyList<string> ReferenceImages);

/// <summary>One generated inbetween: its timing parameter and its strokes.</summary>
public sealed record InbetweenFrameResult(double T, List<Stroke> Strokes);

/// <summary>
/// The AI counterpart of the deterministic inbetweener. Implementations
/// return stroke lists in the document's own model, so AI output flows
/// through the exact same brush re-render and timeline insertion paths as
/// everything else. Implementations never throw — failures come back as
/// <see cref="AiResult{T}"/> values.
/// </summary>
/// <remarks>
/// One method, deliberately. This interface briefly had a second —
/// <c>DrawAsync</c>, a text prompt in and a drawing out — and it was removed
/// rather than left unused, because an interface is a statement about what the
/// application is for. Lightbox assists an artist; it does not draw instead of
/// one. Everything the AI produces has to start from something the artist
/// authored — two keyframes here, and pencils or a pose in the features that
/// follow. A prompt field starts from nothing, and a control that is present
/// makes a promise whether or not anyone presses it.
/// </remarks>
public interface IAiArtist
{
    Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct);

    /// <summary>
    /// Read what a character is, from the sheets drawn of it. Once per
    /// character; the answer is stored on the character and edited by hand.
    /// </summary>
    Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(
        SubjectRequest request, CancellationToken ct);
}
