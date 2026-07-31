using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Ai;

/// <summary>
/// Local-LLM <see cref="IAiArtist"/> over Ollama's native chat API with
/// JSON-schema-constrained outputs (<c>format</c> grammar-constrains the
/// decoder, so responses parse by construction — content quality depends on
/// the model). Reuses the exact same prompts, schemas, and clamping
/// validation as the Anthropic path.
///
/// Expectation-setting: small local models produce noticeably weaker
/// inbetweens than Claude — this path exists for offline pipeline testing.
/// </summary>
public sealed class OllamaArtist : IAiArtist
{
    public const string DefaultUrl = "http://localhost:11434";
    public const string DefaultModel = "qwen3";

    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaArtist(string? baseUrl = null, string? model = null, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl ?? DefaultUrl);
        _http.Timeout = TimeSpan.FromMinutes(10); // local generation is slow
        _model = model ?? DefaultModel;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        [JsonPropertyName("done_reason")] public string? DoneReason { get; set; }
    }

    public async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var call = await CallAsync(Prompts.InbetweenSystem, Prompts.InbetweenUser(request), StrokeSchemas.InbetweenResult, ct);
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
                .Where(f => f.Strokes.Count > 0)
                .ToList();
            if (frames.Count == 0)
                return AiResult<List<InbetweenFrameResult>>.Error(
                    "The local model returned no usable inbetween frames — try a larger model.", retryable: true);
            return AiResult<List<InbetweenFrameResult>>.Success(frames);
        }
        catch (JsonException e)
        {
            return AiResult<List<InbetweenFrameResult>>.Error(
                $"Could not parse the local model's response: {e.Message}", retryable: true);
        }
    }

    public async Task<AiResult<List<Core.Documents.Stroke>>> DrawAsync(DrawRequest request, CancellationToken ct)
    {
        var call = await CallAsync(Prompts.DrawSystem, Prompts.DrawUser(request), StrokeSchemas.DrawResult, ct);
        if (call.Outcome != AiOutcome.Success)
            return Forward<List<Core.Documents.Stroke>>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.DrawResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            var strokes = StrokeWire.FromWire(dto.Strokes, request.Scene);
            if (strokes.Count == 0)
                return AiResult<List<Core.Documents.Stroke>>.Error(
                    "The local model returned no usable strokes — try a larger model.", retryable: true);
            return AiResult<List<Core.Documents.Stroke>>.Success(strokes);
        }
        catch (JsonException e)
        {
            return AiResult<List<Core.Documents.Stroke>>.Error(
                $"Could not parse the local model's response: {e.Message}", retryable: true);
        }
    }

    private async Task<AiResult<string>> CallAsync(string system, string user, string schemaJson, CancellationToken ct)
    {
        using var schemaDoc = JsonDocument.Parse(schemaJson);
        var body = new
        {
            model = _model,
            messages = new[]
            {
                new ChatMessage { Role = "system", Content = system },
                new ChatMessage { Role = "user", Content = user },
            },
            stream = false,
            format = schemaDoc.RootElement,
            options = new { temperature = 0, num_ctx = 8192 },
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("/api/chat", body, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                var hint = (int)response.StatusCode == 404 && detail.Contains("not found")
                    ? $" Pull the model first: `ollama pull {_model}`."
                    : "";
                return AiResult<string>.Error(
                    $"Ollama returned {(int)response.StatusCode}: {Truncate(detail)}.{hint}", retryable: false);
            }
            var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
            var text = chat?.Message?.Content;
            if (string.IsNullOrWhiteSpace(text))
                return AiResult<string>.Error("Ollama returned an empty response.", retryable: true);
            return AiResult<string>.Success(text);
        }
        catch (OperationCanceledException)
        {
            return AiResult<string>.Error("Canceled.", retryable: false);
        }
        catch (HttpRequestException e)
        {
            return AiResult<string>.Error(
                $"Could not reach Ollama at {_http.BaseAddress} — is Ollama running? ({e.Message})", retryable: true);
        }
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    private static AiResult<T> Forward<T>(AiResult<string> call) => call.Outcome switch
    {
        AiOutcome.Refused => AiResult<T>.Refused(call.Message ?? "Refused."),
        AiOutcome.Truncated => AiResult<T>.Truncated(call.Message ?? "Truncated."),
        _ => AiResult<T>.Error(call.Message ?? "Unknown error.", call.Retryable),
    };
}
