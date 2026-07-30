using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Lightbox.Ai;

/// <summary>
/// Claude-backed <see cref="IAiArtist"/>. Uses the Messages API with
/// structured outputs so responses are guaranteed to parse against the
/// stroke schema, and streaming so long stroke payloads don't hit HTTP
/// timeouts. Thinking is left at the model default.
///
/// All failure modes — refusals, truncation, rate limits, auth, network —
/// come back as <see cref="AiResult{T}"/> values, never exceptions.
/// </summary>
public sealed class AnthropicArtist : IAiArtist
{
    public const string Model = "claude-opus-5";
    private const int MaxTokens = 32000;

    private readonly AnthropicClient _client;

    public AnthropicArtist(string apiKey)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
    }

    public async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.InbetweenSystem,
            Prompts.InbetweenUser(request),
            StrokeSchemas.InbetweenResult,
            ct);
        if (call.Outcome != AiOutcome.Success)
            return Forward<List<InbetweenFrameResult>>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.InbetweenResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            var frames = dto.Inbetweens
                .Where(f => f.T is > 0 and < 1)
                .OrderBy(f => f.T)
                .Select(f => new InbetweenFrameResult(f.T, StrokeWire.FromWire(f.Strokes, request.Scene)))
                .ToList();
            if (frames.Count == 0)
                return AiResult<List<InbetweenFrameResult>>.Error(
                    "The model returned no usable inbetween frames.", retryable: true);
            return AiResult<List<InbetweenFrameResult>>.Success(frames);
        }
        catch (JsonException e)
        {
            return AiResult<List<InbetweenFrameResult>>.Error(
                $"Could not parse the model's response: {e.Message}", retryable: true);
        }
    }

    public async Task<AiResult<List<Core.Documents.Stroke>>> DrawAsync(
        DrawRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.DrawSystem,
            Prompts.DrawUser(request),
            StrokeSchemas.DrawResult,
            ct);
        if (call.Outcome != AiOutcome.Success)
            return Forward<List<Core.Documents.Stroke>>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.DrawResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            var strokes = StrokeWire.FromWire(dto.Strokes, request.Scene);
            if (strokes.Count == 0)
                return AiResult<List<Core.Documents.Stroke>>.Error(
                    "The model returned no usable strokes.", retryable: true);
            return AiResult<List<Core.Documents.Stroke>>.Success(strokes);
        }
        catch (JsonException e)
        {
            return AiResult<List<Core.Documents.Stroke>>.Error(
                $"Could not parse the model's response: {e.Message}", retryable: true);
        }
    }

    // ---- shared call path ----------------------------------------------------

    /// <summary>Run one structured-output request; returns the raw JSON text.</summary>
    private async Task<AiResult<string>> CallAsync(
        string system, string user, string schemaJson, CancellationToken ct)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schemaJson)
                     ?? throw new InvalidOperationException("Bad schema constant.");

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            System = new List<TextBlockParam> { new() { Text = system } },
            Messages = [new() { Role = Role.User, Content = user }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = schema },
            },
        };

        try
        {
            var text = new StringBuilder();
            string? stopReason = null;
            await foreach (var ev in _client.Messages.CreateStreaming(parameters, cancellationToken: ct))
            {
                if (ev.TryPickContentBlockDelta(out var blockDelta)
                    && blockDelta.Delta.TryPickText(out var textDelta))
                {
                    text.Append(textDelta.Text);
                }
                else if (ev.TryPickDelta(out var messageDelta))
                {
                    stopReason = messageDelta.Delta.StopReason?.ToString();
                }
            }

            return stopReason?.ToLowerInvariant() switch
            {
                "refusal" => AiResult<string>.Refused(
                    "Claude declined this request. Adjust the drawing or use the deterministic inbetweener."),
                "max_tokens" => AiResult<string>.Truncated(
                    "The response was cut off. Try fewer inbetweens per call or simpler keyframes."),
                _ => AiResult<string>.Success(text.ToString()),
            };
        }
        catch (OperationCanceledException)
        {
            return AiResult<string>.Error("Canceled.", retryable: false);
        }
        catch (AnthropicUnauthorizedException)
        {
            return AiResult<string>.Error(
                "The API key was rejected. Check the ANTHROPIC_API_KEY environment variable or your settings.",
                retryable: false);
        }
        catch (AnthropicRateLimitException)
        {
            return AiResult<string>.Error("Rate limited — wait a moment and retry.", retryable: true);
        }
        catch (Anthropic5xxException)
        {
            return AiResult<string>.Error("The API had a server-side problem — retry shortly.", retryable: true);
        }
        catch (AnthropicApiException e)
        {
            return AiResult<string>.Error($"API error: {e.Message}", retryable: false);
        }
        catch (AnthropicIOException e)
        {
            return AiResult<string>.Error($"Network error: {e.Message}", retryable: true);
        }
    }

    private static AiResult<T> Forward<T>(AiResult<string> call) => call.Outcome switch
    {
        AiOutcome.Refused => AiResult<T>.Refused(call.Message ?? "Refused."),
        AiOutcome.Truncated => AiResult<T>.Truncated(call.Message ?? "Truncated."),
        _ => AiResult<T>.Error(call.Message ?? "Unknown error.", call.Retryable),
    };
}
