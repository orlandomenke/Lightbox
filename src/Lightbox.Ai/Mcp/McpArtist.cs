using System.Text;
using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.Ai.Mcp;

/// <summary>
/// An <see cref="IAiArtist"/> whose model lives behind an MCP server the user
/// supplies. Lightbox hands over the system prompt, the task and the JSON
/// schema; the server does whatever it likes and returns text.
/// </summary>
/// <remarks>
/// The point of this path is that Lightbox does not have to know what is on
/// the other side. A team can put their own agent, their own retrieval, or a
/// model with no public API behind one tool and get inbetweens out of it.
///
/// The contract is small on purpose, and it is the whole of it:
///
/// <code>
/// tools/call { name: &lt;tool&gt;, arguments: {
///   system: string,   // the role and the rules
///   prompt: string,   // the task, with the keyframes as JSON
///   schema: object    // JSON Schema the reply must match
/// }}
/// → { content: [{ type: "text", text: "&lt;json matching schema&gt;" }] }
/// </code>
///
/// Concatenating every text block rather than taking the first is deliberate:
/// servers that stream a long answer split it, and dropping the tail would
/// look like a truncated model rather than a client bug.
/// </remarks>
public sealed class McpArtist(IMcpChannel channel, string toolName) : IAiArtist, IAsyncDisposable
{
    public const string DefaultTool = "generate";

    private readonly SemaphoreSlim _handshake = new(1, 1);
    private bool _ready;

    public async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.InbetweenSystem, Prompts.InbetweenUser(request), StrokeSchemas.InbetweenResult, ct);
        return StrokeParsing.Inbetweens(call, request.Scene, $"The “{toolName}” tool");
    }

    /// <summary>Names of the tools the server offers — what the Configure window checks against.</summary>
    public async Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var result = await channel.RequestAsync("tools/list", new { }, ct);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("tools", out var tools))
            return [];
        return tools.EnumerateArray()
            .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
    }

    private async Task<AiResult<string>> CallAsync(
        string system, string user, string schemaJson, CancellationToken ct)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        try
        {
            await EnsureHandshakeAsync(ct);
            var result = await channel.RequestAsync("tools/call", new
            {
                name = toolName,
                arguments = new { system, prompt = user, schema = schema.RootElement },
            }, ct);

            var text = TextOf(result);
            if (result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("isError", out var isError)
                && isError.ValueKind == JsonValueKind.True)
            {
                return AiResult<string>.Error(
                    $"The “{toolName}” tool reported an error: {Truncate(text)}", retryable: false);
            }

            return string.IsNullOrWhiteSpace(text)
                ? AiResult<string>.Error($"The “{toolName}” tool returned no text.", retryable: true)
                : AiResult<string>.Success(text);
        }
        catch (OperationCanceledException)
        {
            return AiResult<string>.Error("Canceled.", retryable: false);
        }
        catch (McpException e)
        {
            return AiResult<string>.Error(e.Message, e.Retryable);
        }
    }

    private async Task EnsureHandshakeAsync(CancellationToken ct)
    {
        if (_ready) return;
        await _handshake.WaitAsync(ct);
        try
        {
            if (_ready) return;
            await channel.RequestAsync("initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "Lightbox", version = "1.0" },
            }, ct);
            await channel.NotifyAsync("notifications/initialized", new { }, ct);
            _ready = true;
        }
        finally
        {
            _handshake.Release();
        }
    }

    /// <summary>Every text block in a tool result, joined.</summary>
    private static string TextOf(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var text = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("text", out var t)
                && t.ValueKind == JsonValueKind.String)
            {
                text.Append(t.GetString());
            }
        }
        return text.ToString();
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "…";

    public async ValueTask DisposeAsync()
    {
        _handshake.Dispose();
        await channel.DisposeAsync();
    }
}
