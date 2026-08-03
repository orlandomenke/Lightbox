using System.Text.Json;

namespace Lightbox.Ai.Mcp;

/// <summary>
/// One JSON-RPC conversation with an MCP server.
/// </summary>
/// <remarks>
/// An interface rather than a concrete process because the transport is the
/// only part of <see cref="McpArtist"/> that cannot be exercised in a test.
/// With this seam the handshake, the tool call, the error mapping and the
/// stroke parsing are all testable against a scripted channel, and the part
/// that is left untested is the part that is only <c>Process.Start</c>.
/// </remarks>
public interface IMcpChannel : IAsyncDisposable
{
    /// <summary>Send a request and wait for its result. Throws <see cref="McpException"/> on an error reply.</summary>
    Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken ct);

    /// <summary>Send a notification — no id, no reply.</summary>
    Task NotifyAsync(string method, object? parameters, CancellationToken ct);
}

/// <summary>A protocol-level failure: an error reply, a dead server, a malformed frame.</summary>
public sealed class McpException(string message, bool retryable = false) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}
