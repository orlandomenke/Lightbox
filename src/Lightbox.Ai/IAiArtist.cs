using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai;

public sealed record SceneInfo(int Width, int Height, int Fps);

public sealed record InbetweenRequest(
    SceneInfo Scene,
    IReadOnlyList<Stroke> KeyframeA,
    IReadOnlyList<Stroke> KeyframeB,
    IReadOnlyList<double> Ts,
    Easing Easing);

/// <summary>One generated inbetween: its timing parameter and its strokes.</summary>
public sealed record InbetweenFrameResult(double T, List<Stroke> Strokes);

public sealed record DrawRequest(
    SceneInfo Scene,
    string Prompt,
    IReadOnlyList<Stroke> ExistingStrokes);

/// <summary>
/// The AI drawing counterpart of the deterministic engines. Implementations
/// return stroke lists in the document's own model, so AI output flows
/// through the exact same brush re-render and timeline insertion paths as
/// everything else. Implementations never throw — failures come back as
/// <see cref="AiResult{T}"/> values.
/// </summary>
public interface IAiArtist
{
    Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct);

    Task<AiResult<List<Stroke>>> DrawAsync(DrawRequest request, CancellationToken ct);
}
