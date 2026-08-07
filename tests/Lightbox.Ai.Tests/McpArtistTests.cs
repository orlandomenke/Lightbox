using System.Text.Json;
using Lightbox.Ai.Mcp;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

/// <summary>A scripted MCP server: records what was asked, answers what it was told to.</summary>
internal sealed class FakeChannel(Func<string, JsonElement, JsonElement> respond) : IMcpChannel
{
    public List<(string Method, JsonElement Params)> Requests { get; } = [];
    public List<string> Notifications { get; } = [];
    public bool Disposed { get; private set; }

    private static JsonElement Wrap(object? o) =>
        JsonSerializer.SerializeToElement(o ?? new { });

    public Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken ct)
    {
        var wrapped = Wrap(parameters);
        Requests.Add((method, wrapped));
        return Task.FromResult(respond(method, wrapped));
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken ct)
    {
        Notifications.Add(method);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>A tool result carrying one text block.</summary>
    public static JsonElement Text(string text, bool isError = false) =>
        Wrap(new { content = new[] { new { type = "text", text } }, isError });
}

public class McpArtistTests
{
    private static InbetweenRequest Request() => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Label = "arm", Points = [new(0, 0, 0.5)] }],
        [new Stroke { Label = "arm", Points = [new(0, 50, 0.5)] }],
        [0.5],
        Easing.Linear);

    private const string OneFrame = """
        {"inbetweens":[{"t":0.5,"strokes":[{"tool":"brush","color":"#000000","size":6,
        "hardness":0.8,"opacity":1,"label":"arm","points":[{"x":5,"y":25,"pressure":0.5}]}]}]}
        """;

    private static JsonElement Empty() => JsonSerializer.SerializeToElement(new { });

    [Fact]
    public async Task ItHandshakesOnceThenCallsTheNamedTool()
    {
        var channel = new FakeChannel((method, _) => method switch
        {
            "tools/call" => FakeChannel.Text(OneFrame.Replace("\n", "")),
            _ => Empty(),
        });
        var artist = new McpArtist(channel, "draw_inbetweens");

        var first = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);
        var second = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Success, first.Outcome);
        Assert.Equal(AiOutcome.Success, second.Outcome);

        // Handshake once, not once per call: initialize + two tool calls.
        Assert.Equal("initialize", channel.Requests[0].Method);
        Assert.Single(channel.Requests.Where(r => r.Method == "initialize"));
        Assert.Equal("notifications/initialized", Assert.Single(channel.Notifications));
        Assert.Equal(2, channel.Requests.Count(r => r.Method == "tools/call"));
    }

    [Fact]
    public async Task TheToolIsCalledWithSystemPromptAndSchema()
    {
        var channel = new FakeChannel((method, _) =>
            method == "tools/call" ? FakeChannel.Text(OneFrame.Replace("\n", "")) : Empty());
        var artist = new McpArtist(channel, "generate");

        await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        var call = channel.Requests.Last(r => r.Method == "tools/call").Params;
        Assert.Equal("generate", call.GetProperty("name").GetString());
        var args = call.GetProperty("arguments");
        Assert.Contains("inbetweener", args.GetProperty("system").GetString());
        Assert.Contains("keyframeA", args.GetProperty("prompt").GetString());
        Assert.True(args.GetProperty("schema").GetProperty("properties")
            .TryGetProperty("inbetweens", out _));
    }

    [Fact]
    public async Task TextBlocksAreJoinedRatherThanTruncatedToTheFirst()
    {
        // A server that streams a long answer splits it; taking block zero
        // would look like the model returning malformed JSON.
        var halves = OneFrame.Replace("\n", "");
        var split = halves.Length / 2;
        var result = JsonSerializer.SerializeToElement(new
        {
            content = new[]
            {
                new { type = "text", text = halves[..split] },
                new { type = "text", text = halves[split..] },
            },
        });
        var channel = new FakeChannel((method, _) => method == "tools/call" ? result : Empty());
        var artist = new McpArtist(channel, "generate");

        Assert.Equal(AiOutcome.Success,
            (await artist.GenerateInbetweensAsync(Request(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task AToolErrorIsReportedNotParsed()
    {
        var channel = new FakeChannel((method, _) => method == "tools/call"
            ? FakeChannel.Text("the model is not loaded", isError: true)
            : Empty());
        var artist = new McpArtist(channel, "generate");

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("not loaded", result.Message);
    }

    [Fact]
    public async Task AProtocolFailureBecomesAValueRatherThanAnException()
    {
        var channel = new FakeChannel((_, _) => throw new McpException("server exited", retryable: true));
        var artist = new McpArtist(channel, "generate");

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.True(result.Retryable);
        Assert.Contains("server exited", result.Message);
    }

    [Fact]
    public async Task ListToolsReadsTheNames()
    {
        var tools = JsonSerializer.SerializeToElement(new
        {
            tools = new[] { new { name = "generate" }, new { name = "describe" } },
        });
        var channel = new FakeChannel((method, _) => method == "tools/list" ? tools : Empty());
        var artist = new McpArtist(channel, "generate");

        Assert.Equal(["generate", "describe"], await artist.ListToolsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TestingAnMcpConnectionNamesTheToolsWhenTheChosenOneIsAbsent()
    {
        // The likeliest MCP mistake is a server that runs fine and spells its
        // tool differently. "Failed" would send someone hunting the network.
        var tools = JsonSerializer.SerializeToElement(new { tools = new[] { new { name = "complete" } } });
        var channel = new FakeChannel((method, _) => method == "tools/list" ? tools : Empty());
        var artist = new McpArtist(channel, "generate");

        var names = await artist.ListToolsAsync(CancellationToken.None);

        Assert.DoesNotContain("generate", names);
        Assert.Contains("complete", names);
    }

    [Fact]
    public async Task DisposingTheArtistClosesTheChannel()
    {
        // Otherwise a Test connection leaves a child process running.
        var channel = new FakeChannel((_, _) => Empty());
        var artist = new McpArtist(channel, "generate");

        await artist.DisposeAsync();

        Assert.True(channel.Disposed);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("  ", new string[0])]
    [InlineData("-y server", new[] { "-y", "server" })]
    [InlineData("""-y "my server" --flag""", new[] { "-y", "my server", "--flag" })]
    [InlineData("""--path "/a b/c" """, new[] { "--path", "/a b/c" })]
    [InlineData("""--empty "" x""", new[] { "--empty", "", "x" })]
    public void ArgumentsSplitLikeAShellWould(string line, string[] expected)
    {
        Assert.Equal(expected, StdioMcpChannel.SplitArguments(line));
    }
}
