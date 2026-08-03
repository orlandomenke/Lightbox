using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Ai;

/// <summary>
/// The OpenAI chat-completions dialect, which is the lingua franca: GPT
/// itself, OpenRouter, LM Studio, vLLM, llama.cpp's server, and most gateways
/// people put in front of a model of their own all answer it.
/// </summary>
/// <remarks>
/// One class rather than one per vendor, because the only differences are the
/// base URL, the key and the model name — exactly the three things the
/// provider catalogue already describes. Structured output goes through
/// <c>response_format: json_schema</c> with <c>strict</c>; services that
/// ignore it still usually return the right shape, and if they do not the
/// parse failure says so rather than producing nonsense strokes.
/// </remarks>
public sealed class OpenAiArtist : IAiArtist
{
    public const string OpenAiUrl = "https://api.openai.com/v1";
    public const string OpenRouterUrl = "https://openrouter.ai/api/v1";
    public const string DefaultModel = "gpt-5";

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _label;

    /// <param name="label">
    /// What to call this service in an error message. "OpenAI" and "OpenRouter"
    /// are worth distinguishing when a key is rejected.
    /// </param>
    public OpenAiArtist(
        string? baseUrl = null,
        string? apiKey = null,
        string? model = null,
        string label = "The service",
        HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        // A trailing slash matters: without one, Uri resolution drops the last
        // path segment, so ".../v1" + "chat/completions" would lose the "v1".
        var root = (string.IsNullOrWhiteSpace(baseUrl) ? OpenAiUrl : baseUrl.Trim()).TrimEnd('/');
        _http.BaseAddress = new Uri(root + "/");
        _http.Timeout = TimeSpan.FromMinutes(10);
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _label = label;
    }

    public async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.InbetweenSystem, Prompts.InbetweenUser(request),
            "inbetween_result", StrokeSchemas.InbetweenResult, request.ReferenceImages, ct);
        return StrokeParsing.Inbetweens(call, request.Scene, _label);
    }

    public async Task<AiResult<List<Core.Documents.Stroke>>> DrawAsync(
        DrawRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.DrawSystem, Prompts.DrawUser(request),
            "draw_result", StrokeSchemas.DrawResult, request.ReferenceImages, ct);
        return StrokeParsing.Strokes(call, request.Scene, _label);
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("refusal")] public string? Refusal { get; set; }
    }

    private sealed class Completion
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private async Task<AiResult<string>> CallAsync(
        string system, string user, string schemaName, string schemaJson,
        IReadOnlyList<string>? referenceImages, CancellationToken ct)
    {
        using var schemaDoc = JsonDocument.Parse(schemaJson);

        // Reference views ride along as data URIs before the task text, in the
        // same order the Anthropic path uses, so the prompts stay comparable.
        var parts = new List<object>();
        foreach (var png in referenceImages ?? [])
        {
            parts.Add(new
            {
                type = "image_url",
                image_url = new { url = "data:image/png;base64," + png },
            });
        }
        parts.Add(new { type = "text", text = user });

        var body = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = parts },
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new { name = schemaName, strict = true, schema = schemaDoc.RootElement },
            },
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("chat/completions", body, ct);
            if (!response.IsSuccessStatusCode) return Failure(response.StatusCode, await Detail(response, ct));

            var completion = await response.Content.ReadFromJsonAsync<Completion>(cancellationToken: ct);
            var choice = completion?.Choices?.FirstOrDefault();
            if (choice?.Message?.Refusal is { Length: > 0 } refusal)
                return AiResult<string>.Refused($"{_label} declined this request: {refusal}");
            if (choice?.FinishReason == "length")
                return AiResult<string>.Truncated(
                    "The response was cut off. Try fewer inbetweens per call or simpler keyframes.");

            var text = choice?.Message?.Content;
            return string.IsNullOrWhiteSpace(text)
                ? AiResult<string>.Error($"{_label} returned an empty response.", retryable: true)
                : AiResult<string>.Success(text);
        }
        catch (OperationCanceledException)
        {
            return AiResult<string>.Error("Canceled.", retryable: false);
        }
        catch (HttpRequestException e)
        {
            return AiResult<string>.Error(
                $"Could not reach {_http.BaseAddress} ({e.Message}).", retryable: true);
        }
        catch (JsonException e)
        {
            return AiResult<string>.Error(
                $"{_label} replied with something that is not a chat completion: {e.Message}", retryable: false);
        }
    }

    private static async Task<string> Detail(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            // The useful sentence is nested; dig it out rather than dumping the envelope.
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? body;
                if (error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString() ?? body;
            }
            return Truncate(body);
        }
        catch (Exception e) when (e is JsonException or IOException or OperationCanceledException)
        {
            return response.ReasonPhrase ?? "no detail";
        }
    }

    private AiResult<string> Failure(System.Net.HttpStatusCode status, string detail) => (int)status switch
    {
        401 or 403 => AiResult<string>.Error(
            $"{_label} rejected the API key: {Truncate(detail)}", retryable: false),
        404 => AiResult<string>.Error(
            $"{_label} has no model called “{_model}”, or the endpoint path is wrong: {Truncate(detail)}",
            retryable: false),
        429 => AiResult<string>.Error("Rate limited — wait a moment and retry.", retryable: true),
        >= 500 => AiResult<string>.Error(
            $"{_label} had a server-side problem — retry shortly: {Truncate(detail)}", retryable: true),
        _ => AiResult<string>.Error($"{_label} returned {(int)status}: {Truncate(detail)}", retryable: false),
    };

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";
}
