using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Mcp;

/// <summary>
/// Client side of the Lightbox IPC pipe. Opens a fresh connection per call —
/// simple, stateless, and immune to a stale connection after the GUI
/// restarts. Mirrors the protocol in Lightbox.App/Services/IpcProtocol.cs.
/// </summary>
public static class PipeBridge
{
    public const string PipeName = "lightbox-ipc";
    private const int ConnectTimeoutMs = 1500;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Response
    {
        public bool Ok { get; set; }
        public JsonElement? Payload { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Send one op to the running Lightbox app; returns the payload JSON.
    /// Throws <see cref="LightboxUnavailableException"/> when the app isn't
    /// running, and <see cref="LightboxOpException"/> when the app rejects
    /// the request — both carry human-readable messages for the model.
    /// </summary>
    public static async Task<JsonElement> CallAsync(string op, object? payload, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, ct);
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            throw new LightboxUnavailableException();
        }

        var request = new
        {
            op,
            payload = payload is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(payload, Json),
        };

        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));

        var line = await reader.ReadLineAsync(ct)
                   ?? throw new LightboxOpException("Lightbox closed the connection.");
        var response = JsonSerializer.Deserialize<Response>(line, Json)
                       ?? throw new LightboxOpException("Empty response from Lightbox.");
        if (!response.Ok) throw new LightboxOpException(response.Error ?? "Lightbox rejected the request.");
        return response.Payload ?? default;
    }
}

public sealed class LightboxUnavailableException() : Exception(
    "Could not reach Lightbox. Start the Lightbox application first, then try again.");

public sealed class LightboxOpException(string message) : Exception(message);
