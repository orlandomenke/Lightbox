using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai;

public sealed record SceneInfo(int Width, int Height, int Fps);

public sealed record InbetweenRequest(
    SceneInfo Scene,
    IReadOnlyList<Stroke> KeyframeA,
    IReadOnlyList<Stroke> KeyframeB,
    IReadOnlyList<double> Ts,
    Easing Easing,
    IReadOnlyList<string>? ReferenceImages = null);

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
}
