using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol — every log line must go to stderr,
// or Claude Desktop sees a corrupted stream and drops the server.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Which build this is, on the very first line of stderr.
//
// The MCP client logs stderr, so this answers "which copy of the server did
// Claude Desktop actually launch" without anyone opening a file browser — the
// question B195 turned into an afternoon, because a fix in `main` and a stale
// published exe look identical from the client's side. get_scene reports the
// same string; this is the half that survives the server failing to start at
// all, which is when a version is hardest to get and most worth having.
Console.Error.WriteLine($"Lightbox MCP server {Lightbox.Mcp.BuildInfo.Build}");

await builder.Build().RunAsync();
