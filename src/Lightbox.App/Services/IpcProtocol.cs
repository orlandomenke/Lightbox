using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.App.Services;

/// <summary>
/// The line-delimited JSON protocol between the GUI (pipe server) and the
/// MCP bridge (pipe client). One request line → one response line.
/// Shared shape; the MCP project keeps its own mirror of these records so it
/// doesn't need a reference to the App.
/// </summary>
public static class IpcProtocol
{
    public const string PipeName = "lightbox-ipc";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed class Request
    {
        public string Op { get; set; } = "";
        public JsonElement? Payload { get; set; }
    }

    public sealed class Response
    {
        public bool Ok { get; set; }
        public JsonElement? Payload { get; set; }
        public string? Error { get; set; }

        public static Response Success(object? payload) => new()
        {
            Ok = true,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, Json),
        };

        public static Response Fail(string error) => new() { Ok = false, Error = error };
    }
}
