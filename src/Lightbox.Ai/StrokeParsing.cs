using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.Ai;

/// <summary>
/// Turning a model's JSON text into strokes, once.
/// </summary>
/// <remarks>
/// Every artist differs only in how it gets the text — auth, endpoint,
/// framing. What it does with the text is identical, and was copied per
/// artist until there were four of them. The <paramref name="noun"/> and
/// <paramref name="hint"/> parameters keep the one thing that legitimately
/// varied: a local model that returns nothing wants "try a larger model",
/// and a hosted one does not.
/// </remarks>
internal static class StrokeParsing
{
    public static AiResult<List<InbetweenFrameResult>> Inbetweens(
        AiResult<string> call, SceneInfo scene, string noun, string hint = "")
    {
        if (call.Outcome != AiOutcome.Success) return Forward<List<InbetweenFrameResult>>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.InbetweenResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            var frames = dto.Inbetweens
                .Where(f => f.T is > 0 and < 1)
                .OrderBy(f => f.T)
                .Select(f => new InbetweenFrameResult(f.T, StrokeWire.FromWire(f.Strokes, scene)))
                .Where(f => f.Strokes.Count > 0)
                .ToList();
            if (frames.Count == 0)
                return AiResult<List<InbetweenFrameResult>>.Error(
                    $"{noun} returned no usable inbetween frames.{hint}", retryable: true);
            return AiResult<List<InbetweenFrameResult>>.Success(frames);
        }
        catch (JsonException e)
        {
            return AiResult<List<InbetweenFrameResult>>.Error(
                $"Could not parse the response: {e.Message}", retryable: true);
        }
    }

    public static AiResult<List<Stroke>> Strokes(
        AiResult<string> call, SceneInfo scene, string noun, string hint = "")
    {
        if (call.Outcome != AiOutcome.Success) return Forward<List<Stroke>>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.DrawResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            var strokes = StrokeWire.FromWire(dto.Strokes, scene);
            if (strokes.Count == 0)
                return AiResult<List<Stroke>>.Error(
                    $"{noun} returned no usable strokes.{hint}", retryable: true);
            return AiResult<List<Stroke>>.Success(strokes);
        }
        catch (JsonException e)
        {
            return AiResult<List<Stroke>>.Error(
                $"Could not parse the response: {e.Message}", retryable: true);
        }
    }

    public static AiResult<T> Forward<T>(AiResult<string> call) => call.Outcome switch
    {
        AiOutcome.Refused => AiResult<T>.Refused(call.Message ?? "Refused."),
        AiOutcome.Truncated => AiResult<T>.Truncated(call.Message ?? "Truncated."),
        _ => AiResult<T>.Error(call.Message ?? "Unknown error.", call.Retryable),
    };
}
