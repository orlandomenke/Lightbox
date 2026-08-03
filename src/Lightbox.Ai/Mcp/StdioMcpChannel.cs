using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lightbox.Ai.Mcp;

/// <summary>
/// An MCP server run as a child process, spoken to over stdin/stdout with
/// newline-delimited JSON-RPC — the framing the stdio transport specifies.
/// </summary>
/// <remarks>
/// Requests are serialized by a lock rather than multiplexed by id. Lightbox
/// makes one AI call at a time and each takes minutes; a correlation table
/// would buy concurrency nobody has and cost a class of bug that is hard to
/// see. Replies whose id does not match — the server's own notifications and
/// log lines — are skipped rather than treated as answers.
/// </remarks>
public sealed class StdioMcpChannel : IMcpChannel
{
    private readonly Process _process;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private int _nextId;
    private bool _disposed;

    public StdioMcpChannel(string command, string? arguments = null, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in SplitArguments(arguments)) info.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(workingDirectory)) info.WorkingDirectory = workingDirectory;

        try
        {
            _process = Process.Start(info) ?? throw new McpException($"Could not start “{command}”.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new McpException($"Could not start “{command}”: {e.Message}");
        }
        // Drained so a chatty server cannot fill its stderr pipe and hang.
        _ = _process.StandardError.ReadToEndAsync();
    }

    /// <summary>
    /// Split a command line the way a shell would, honouring double quotes.
    /// </summary>
    /// <remarks>
    /// The Configure window takes arguments as one string because that is how
    /// people have them written down. Passing that string whole to
    /// <c>ArgumentList</c> would make it a single argument, and passing it via
    /// <c>Arguments</c> would re-quote it differently on each platform.
    /// </remarks>
    internal static IReadOnlyList<string> SplitArguments(string? line)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return result;

        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in line)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (any) { result.Add(current.ToString()); current.Clear(); any = false; }
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (any) result.Add(current.ToString());
        return result;
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        await _turn.WaitAsync(ct);
        try
        {
            await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, ct);

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line is null)
                    throw new McpException(
                        $"The MCP server exited before answering “{method}”.", retryable: true);
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; } // a log line on stdout, not a frame
                using (doc)
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var replyId)) continue;   // a notification
                    if (replyId.ValueKind != JsonValueKind.Number || replyId.GetInt32() != id) continue;

                    if (root.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                        throw new McpException($"{method} failed: {message ?? error.ToString()}");
                    }
                    return root.TryGetProperty("result", out var result)
                        ? result.Clone()   // the document is about to be disposed
                        : default;
                }
            }
        }
        catch (IOException e)
        {
            throw new McpException($"Lost the MCP server: {e.Message}", retryable: true);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task NotifyAsync(string method, object? parameters, CancellationToken ct)
    {
        await _turn.WaitAsync(ct);
        try { await WriteAsync(new { jsonrpc = "2.0", method, @params = parameters }, ct); }
        catch (IOException) { /* a notification nobody hears is not worth failing a call over */ }
        finally { _turn.Release(); }
    }

    private async Task WriteAsync(object frame, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize(frame).AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        try
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or NotSupportedException)
        {
        }
        _process.Dispose();
        _turn.Dispose();
        return ValueTask.CompletedTask;
    }
}
