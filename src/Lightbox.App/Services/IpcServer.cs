using System.IO.Pipes;
using System.Text.Json;
using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>
/// Named-pipe server the MCP bridge (Lightbox.Mcp) talks to. Accepts any
/// number of sequential/parallel clients; each request line is dispatched to
/// the UI thread and answered with one response line. The pipe is per-user by
/// OS default — nothing is exposed on the network.
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly IpcDocumentApi _api;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public IpcServer(IpcDocumentApi api, string? pipeName = null)
    {
        _api = api;
        _pipeName = pipeName ?? IpcProtocol.PipeName;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string PipeName => _pipeName;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                continue;
            }

            _ = Task.Run(() => ServeClientAsync(server));
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                while (!_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (line is null) return;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    IpcProtocol.Response response;
                    try
                    {
                        var request = JsonSerializer.Deserialize<IpcProtocol.Request>(line, IpcProtocol.Json)
                                      ?? throw new JsonException("null request");
                        response = await Dispatcher.UIThread.InvokeAsync(() => _api.Handle(request));
                    }
                    catch (JsonException e)
                    {
                        response = IpcProtocol.Response.Fail($"Bad request JSON: {e.Message}");
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcProtocol.Json));
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Client went away or we're shutting down — both are fine.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            // Unblock WaitForConnectionAsync on platforms where cancellation
            // doesn't interrupt it: poke the pipe with a throwaway client.
            using var poke = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await poke.ConnectAsync(100);
        }
        catch (Exception e) when (e is IOException or TimeoutException)
        {
        }
        try
        {
            await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
        }
        _cts.Dispose();
    }
}
