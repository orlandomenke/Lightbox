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
    /// <summary>What the provider catalogue offers as the default, and what a
    /// caller that names no model gets.</summary>
    public const string Model = "claude-opus-5";

    private const int MaxTokens = 32000;

    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicArtist(string apiKey, string? model = null)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _model = string.IsNullOrWhiteSpace(model) ? Model : model.Trim();
    }

    public async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.InbetweenSystem,
            Prompts.InbetweenUser(request),
            StrokeSchemas.InbetweenResult,
            request.ReferenceImages,
            ct);
        return StrokeParsing.Inbetweens(call, request.Scene, "The model");
    }

    public async Task<AiResult<Core.Projects.SubjectTaxonomy>> ReadSubjectAsync(
        SubjectRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.SubjectSystem,
            Prompts.SubjectUser(request),
            StrokeSchemas.SubjectResult,
            request.ReferenceImages,
            ct);
        return StrokeParsing.Subject(call, "The model");
    }

    // ---- shared call path ----------------------------------------------------

    /// <summary>Run one structured-output request; returns the raw JSON text.</summary>
    private async Task<AiResult<string>> CallAsync(
        string system, string user, string schemaJson, IReadOnlyList<string>? referenceImages, CancellationToken ct)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schemaJson)
                     ?? throw new InvalidOperationException("Bad schema constant.");

        // Character-sheet views ride along as image blocks before the task text.
        var content = new List<ContentBlockParam>();
        foreach (var png in referenceImages ?? [])
        {
            content.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource { Data = png, MediaType = "image/png" },
            });
        }
        content.Add(new TextBlockParam { Text = user });

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = MaxTokens,
            System = new List<TextBlockParam> { new() { Text = system } },
            Messages = [new() { Role = Role.User, Content = content }],
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
}
