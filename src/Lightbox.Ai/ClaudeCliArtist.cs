using System.Text.Json;
using Lightbox.Ai.Cli;

namespace Lightbox.Ai;

/// <summary>
/// An <see cref="IAiArtist"/> that reaches Claude through the Claude Code
/// command line already installed and signed in on this machine, run headless
/// — so an artist with a Claude subscription and no API key can stay in
/// Lightbox instead of switching to another window to ask.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is used as a model, not as an agent.</b> Each request is one
/// <c>claude -p</c> launch carrying Lightbox's own system prompt in place of
/// the coding assistant's, the same JSON schema every other provider is held
/// to, and no tools. What comes back goes through the same parser and the same
/// verifier as an API reply: the rules live in Lightbox and travel with the
/// request, so they cannot differ by transport.
/// </para>
/// <para>
/// No tools, no MCP servers, no skills and safe mode are not tidiness. Stroke
/// labels and character names are document text that reaches the prompt; a
/// model with a shell and the machine's own instructions loaded would give
/// that text something to steer. The flags ask for that; <see cref="Read"/>
/// then checks the run's own start-up line and refuses the answer if anything
/// beyond the schema's own tool was offered, because a flag a future version
/// stops honouring would otherwise fail silently.
/// </para>
/// <para>
/// The task and any reference views go in on standard input as one
/// stream-json message: that is the only input form that carries images, and
/// it keeps everything that grows with a drawing off the command line.
/// </para>
/// </remarks>
public sealed class ClaudeCliArtist(ICliRunner runner, string command, string? model = null) : IAiArtist
{
    public const string DefaultCommand = "claude";

    /// <summary>
    /// The one tool the command line adds for <c>--json-schema</c>: the model
    /// answers by calling it. Anything else on the start-up line is refused.
    /// </summary>
    internal const string SchemaTool = "StructuredOutput";

    /// <summary>
    /// What the launched program's environment is changed by.
    /// </summary>
    /// <remarks>
    /// The credentials go because this provider means <i>the account Claude
    /// Code is signed in to</i>: with an API key in the environment it would
    /// quietly bill that key instead, and a key is what the Claude (Anthropic)
    /// provider is for. The base URL goes with them, so the request cannot be
    /// sent somewhere other than the provider named. The last line turns off
    /// the program's own telemetry, error reports and update checks, so the
    /// only thing an AI action sends is the request (A1).
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string?> Environment = new Dictionary<string, string?>
    {
        ["ANTHROPIC_API_KEY"] = null,
        ["ANTHROPIC_AUTH_TOKEN"] = null,
        ["ANTHROPIC_BASE_URL"] = null,
        ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
    };

    private readonly string? _model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();

    public async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.InbetweenSystem, Prompts.InbetweenUser(request), StrokeSchemas.InbetweenResult,
            request.ReferenceImages, ct);
        return StrokeParsing.Inbetweens(call, request.Scene, "Claude Code");
    }

    public async Task<AiResult<Core.Projects.SubjectTaxonomy>> ReadSubjectAsync(
        SubjectRequest request, CancellationToken ct)
    {
        var call = await CallAsync(
            Prompts.SubjectSystem, Prompts.SubjectUser(request), StrokeSchemas.SubjectResult,
            request.ReferenceImages, ct);
        return StrokeParsing.Subject(call, "Claude Code");
    }

    private async Task<AiResult<string>> CallAsync(
        string system, string user, string schemaJson, IReadOnlyList<string>? referenceImages, CancellationToken ct)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        var arguments = new List<string>
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json", "--verbose",
            "--system-prompt", system,
            "--json-schema", JsonSerializer.Serialize(schema.RootElement),
            "--tools", "",
            "--strict-mcp-config",
            "--safe-mode",
            "--disable-slash-commands",
            "--no-session-persistence",
        };
        if (_model is not null) arguments.AddRange(["--model", _model]);

        try
        {
            var output = await runner.RunAsync(
                new CliInvocation(command, arguments, Message(user, referenceImages), Environment), ct);
            return Read(output);
        }
        catch (OperationCanceledException)
        {
            return AiResult<string>.Error("Canceled.", retryable: false);
        }
        catch (CliTimeoutException e)
        {
            return AiResult<string>.Error(e.Message, retryable: false);
        }
        catch (CliException e)
        {
            return AiResult<string>.Error(
                $"{e.Message} Install Claude Code, or give the full path to it in Configure.",
                retryable: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The run's private folder could not be made — a full or locked
            // temp folder. Nothing to do with Claude Code being installed.
            return AiResult<string>.Error(
                $"Lightbox could not prepare a folder to run Claude Code in: {e.Message}", retryable: true);
        }
    }

    /// <summary>One user turn: reference views as image blocks, then the task.</summary>
    private static string Message(string user, IReadOnlyList<string>? referenceImages)
    {
        var content = new List<object>();
        foreach (var png in referenceImages ?? [])
        {
            content.Add(new
            {
                type = "image",
                source = new { type = "base64", media_type = "image/png", data = png },
            });
        }
        content.Add(new { type = "text", text = user });

        return JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content },
        }) + "\n";
    }

    /// <summary>
    /// Check the run's start-up line, find its result line, and turn that into
    /// an outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stream is one JSON object per line — a start-up line, the turns,
    /// rate-limit notices — and ends with a single <c>"type":"result"</c>. A
    /// line that is not JSON is a log line and is skipped rather than treated
    /// as a broken answer.
    /// </para>
    /// <para>
    /// <b>A cut-off reply does not say <c>max_tokens</c> on this path.</b> The
    /// schema is answered through a tool call, so a reply that runs out of room
    /// comes back as an error whose text names the output token maximum —
    /// established by forcing a 200-token cap on 2026-10-13, not assumed.
    /// </para>
    /// </remarks>
    internal static AiResult<string> Read(CliOutput output)
    {
        JsonElement? init = null;
        JsonElement? found = null;
        foreach (var raw in output.Output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var type = Text(root, "type");
                if (type == "result") found = root.Clone();
                else if (type == "system" && Text(root, "subtype") == "init" && init is null) init = root.Clone();
            }
            catch (JsonException)
            {
            }
        }

        if (init is { } start && Offered(start) is { } offered)
        {
            return AiResult<string>.Error(
                $"Claude Code offered the model more than an answer form ({offered}), so Lightbox did not "
                + "use the reply. This version of Claude Code may not honour the flags that switch tools off.",
                retryable: false);
        }

        if (found is not { } result)
        {
            // stderr, or stdout only when it is not the stream itself: the
            // start-up line names folders and a session, and says nothing an
            // artist can act on.
            var said = Truncate(!string.IsNullOrWhiteSpace(output.Error) ? output.Error
                : output.Output.TrimStart().StartsWith('{') ? "" : output.Output);
            return AiResult<string>.Error(
                $"Claude Code exited ({output.ExitCode}) without an answer"
                + (said.Length == 0 ? "." : $": {said}"),
                retryable: false);
        }

        var text = Text(result, "result") ?? "";
        var subtype = Text(result, "subtype");
        var status = result.TryGetProperty("api_error_status", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32()
            : 0;
        var isError = result.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;

        if (text.Contains("output token maximum", StringComparison.OrdinalIgnoreCase)
            || Text(result, "stop_reason") is "max_tokens" or "model_context_window_exceeded")
        {
            return AiResult<string>.Truncated(
                "The response was cut off. Try fewer inbetweens per call or simpler keyframes.");
        }

        switch (subtype)
        {
            case null or "success":
                break;
            case "error_max_structured_output_retries":
                return AiResult<string>.Error(
                    "Claude could not produce strokes in the required form after several tries.", retryable: true);
            case "error_max_turns":
                return AiResult<string>.Error(
                    "Claude Code stopped before answering (it ran out of turns).", retryable: false);
            default:
                return AiResult<string>.Error(
                    $"Claude Code stopped with “{subtype}”"
                    + (text.Length == 0 ? "." : $": {Truncate(text)}"),
                    retryable: Retryable(status, text));
        }

        if (isError)
        {
            if (status == 401 || text.Contains("/login", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
            {
                return AiResult<string>.Error(
                    "Claude Code is not signed in. Run “claude” in a terminal once and sign in, then try again.",
                    retryable: false);
            }

            return AiResult<string>.Error(
                text.Length == 0 ? "Claude Code reported an error and said nothing more."
                                 : $"Claude Code reported: {Truncate(text)}",
                retryable: Retryable(status, text));
        }

        if (Text(result, "stop_reason") == "refusal")
        {
            return AiResult<string>.Refused(
                "Claude declined this request. Adjust the drawing or use the deterministic inbetweener.");
        }

        // The validated object when the schema was honoured; the plain text
        // otherwise, which the parser then judges like any other reply.
        if (result.TryGetProperty("structured_output", out var structured)
            && structured.ValueKind == JsonValueKind.Object)
        {
            return AiResult<string>.Success(structured.GetRawText());
        }

        return string.IsNullOrWhiteSpace(text)
            ? AiResult<string>.Error("Claude Code returned no text.", retryable: true)
            : AiResult<string>.Success(text);
    }

    /// <summary>What the run offered the model beyond the schema's own tool, or null.</summary>
    private static string? Offered(JsonElement init)
    {
        var extra = new List<string>();
        if (init.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
        {
            extra.AddRange(tools.EnumerateArray()
                .Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : null)
                .Where(t => !string.IsNullOrEmpty(t) && t != SchemaTool)!);
        }
        if (init.TryGetProperty("mcp_servers", out var servers)
            && servers.ValueKind == JsonValueKind.Array && servers.GetArrayLength() > 0)
        {
            extra.Add($"{servers.GetArrayLength()} MCP server(s)");
        }
        return extra.Count == 0 ? null : string.Join(", ", extra);
    }

    /// <summary>Whether waiting could help: rate limits, overload, a network that dropped.</summary>
    private static bool Retryable(int status, string text) =>
        status is 408 or 409 or 429 || status >= 500
        || (status == 0 && (text.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("ECONN", StringComparison.Ordinal)
                            || text.Contains("timed out", StringComparison.OrdinalIgnoreCase)));

    private static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Truncate(string s)
    {
        s = s.Trim();
        return s.Length <= 200 ? s : s[..200] + "…";
    }
}
