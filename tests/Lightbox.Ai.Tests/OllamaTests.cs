using System.Net;
using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

/// <summary>Fake HTTP layer — captures the request, returns a scripted response.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public string? LastBody { get; private set; }
    public Uri? LastUri { get; private set; }
    public string? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return respond(request, LastBody ?? "");
    }
}

public class OllamaTests
{
    private static InbetweenRequest Request() => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Label = "arm", Points = [new(0, 0, 0.5), new(10, 0, 0.5)] }],
        [new Stroke { Label = "arm", Points = [new(0, 50, 0.5), new(10, 50, 0.5)] }],
        [0.5],
        Easing.Linear);

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content),
    };

    private static string ChatReply(string innerJson) =>
        JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = innerJson },
            done = true,
            done_reason = "stop",
        });

    [Fact]
    public async Task RequestBody_CarriesModelSchemaPromptsAndOptions()
    {
        var handler = new FakeHandler((_, _) => Json(ChatReply("""{"inbetweens":[]}""")));
        var artist = new OllamaArtist("http://localhost:11434", "qwen3", handler);

        await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.EndsWith("/api/chat", handler.LastUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("qwen3", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("object", root.GetProperty("format").GetProperty("type").GetString());
        Assert.True(root.GetProperty("format").GetProperty("properties").TryGetProperty("inbetweens", out _));
        Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetInt32());
        Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("inbetweener", messages[0].GetProperty("content").GetString());
        Assert.Contains("keyframeA", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SuccessResponse_ParsesAndValidates()
    {
        var inner = """
        {"inbetweens":[{"t":0.5,"strokes":[{"tool":"brush","color":"#000000","size":6,
        "hardness":0.8,"opacity":1,"label":"arm","points":[{"x":5,"y":25,"pressure":0.5}]}]}]}
        """.Replace("\n", "");
        var handler = new FakeHandler((_, _) => Json(ChatReply(inner)));
        var artist = new OllamaArtist(handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        var frame = Assert.Single(result.Value!);
        Assert.Equal(0.5, frame.T);
        Assert.Equal("arm", frame.Strokes[0].Label);
    }

    [Fact]
    public async Task ConnectionRefused_MapsToRetryableWithHint()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("connection refused"));
        var artist = new OllamaArtist(handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.True(result.Retryable);
        Assert.Contains("is Ollama running", result.Message);
    }

    [Fact]
    public async Task ModelNotFound_SuggestsPull()
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"model 'qwen3' not found"}"""),
        });
        var artist = new OllamaArtist(handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("ollama pull", result.Message);
    }

    [Fact]
    public async Task EmptyOrUnusableFrames_IsRetryableError()
    {
        var handler = new FakeHandler((_, _) => Json(ChatReply("""{"inbetweens":[{"t":2,"strokes":[]}]}""")));
        var artist = new OllamaArtist(handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task Draw_ParsesStrokes()
    {
        var inner =
            """{"strokes":[{"tool":"brush","color":"#112233","size":4,"hardness":1,"opacity":1,"label":"roof","points":[{"x":10,"y":10,"pressure":0.6}]}]}""";
        var handler = new FakeHandler((_, _) => Json(ChatReply(inner)));
        var artist = new OllamaArtist(handler: handler);

        var result = await artist.DrawAsync(
            new DrawRequest(new SceneInfo(100, 100, 12), "a house", []),
            CancellationToken.None);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        Assert.Equal("roof", Assert.Single(result.Value!).Label);
    }
}
