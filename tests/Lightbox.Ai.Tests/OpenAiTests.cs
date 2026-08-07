using System.Net;
using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

/// <summary>
/// The OpenAI dialect, which is also how GPT, OpenRouter and every
/// self-hosted endpoint are reached. Everything is asserted through a fake
/// HTTP layer, so the request shape is checked rather than assumed.
/// </summary>
public class OpenAiTests
{
    private static InbetweenRequest Request() => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Label = "arm", Points = [new(0, 0, 0.5), new(10, 0, 0.5)] }],
        [new Stroke { Label = "arm", Points = [new(0, 50, 0.5), new(10, 50, 0.5)] }],
        [0.5],
        Easing.Linear);

    private static HttpResponseMessage Ok(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static string Completion(string innerJson, string finish = "stop") =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = innerJson }, finish_reason = finish },
            },
        });

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = new { message } });

    [Fact]
    public async Task RequestCarriesModelSchemaAndBearerToken()
    {
        var handler = new FakeHandler((_, _) => Ok(Completion("""{"inbetweens":[]}""")));
        var artist = new OpenAiArtist("https://api.example.test/v1", "sk-test", "gpt-5", handler: handler);

        await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        // The version segment must survive: base "…/v1" + "chat/completions".
        Assert.Equal("/v1/chat/completions", handler.LastUri!.AbsolutePath);
        Assert.Equal("Bearer sk-test", handler.LastAuthorization);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("gpt-5", root.GetProperty("model").GetString());

        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schema = format.GetProperty("json_schema");
        Assert.True(schema.GetProperty("strict").GetBoolean());
        Assert.True(schema.GetProperty("schema").GetProperty("properties")
            .TryGetProperty("inbetweens", out _));

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("inbetweener", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ABaseUrlWithATrailingSlashResolvesTheSameWay()
    {
        // The Uri class drops the last segment without a trailing slash, so
        // "…/v1" and "…/v1/" must be normalised to the same thing.
        foreach (var url in new[] { "https://api.example.test/v1", "https://api.example.test/v1/" })
        {
            var handler = new FakeHandler((_, _) => Ok(Completion("""{"inbetweens":[]}""")));
            var artist = new OpenAiArtist(url, "k", "m", handler: handler);
            await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);
            Assert.Equal("/v1/chat/completions", handler.LastUri!.AbsolutePath);
        }
    }

    [Fact]
    public async Task NoKeyMeansNoAuthorizationHeader()
    {
        // A local endpoint usually authenticates nothing, and sending
        // "Bearer " would be rejected by some of them.
        var handler = new FakeHandler((_, _) => Ok(Completion("""{"inbetweens":[]}""")));
        var artist = new OpenAiArtist("http://localhost:1234/v1", apiKey: null, model: "local", handler: handler);

        await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Null(handler.LastAuthorization);
    }

    [Fact]
    public async Task ReferenceImagesRideAlongAsDataUrisBeforeTheTask()
    {
        var handler = new FakeHandler((_, _) => Ok(Completion("""{"inbetweens":[]}""")));
        var artist = new OpenAiArtist("https://x.test/v1", "k", "m", handler: handler);
        var request = Request() with { ReferenceImages = ["AAAA"] };

        await artist.GenerateInbetweensAsync(request, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var parts = body.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal("image_url", parts[0].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,AAAA",
            parts[0].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("text", parts[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SuccessParsesIntoStrokes()
    {
        var inner = """
        {"inbetweens":[{"t":0.5,"strokes":[{"tool":"brush","color":"#000000","size":6,
        "hardness":0.8,"opacity":1,"label":"arm","points":[{"x":5,"y":25,"pressure":0.5}]}]}]}
        """.Replace("\n", "");
        var handler = new FakeHandler((_, _) => Ok(Completion(inner)));
        var artist = new OpenAiArtist("https://x.test/v1", "k", "m", handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        Assert.Equal("arm", Assert.Single(result.Value!).Strokes[0].Label);
    }

    [Theory]
    [InlineData(401, "rejected the API key")]
    [InlineData(404, "no model called")]
    [InlineData(429, "Rate limited")]
    [InlineData(503, "server-side problem")]
    public async Task StatusCodesBecomeSentencesSomebodyCanAct(int status, string expected)
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(Error("nope")),
        });
        var artist = new OpenAiArtist("https://x.test/v1", "k", "gpt-nine", "OpenAI", handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains(expected, result.Message);
        // Retryable only where retrying could actually help.
        Assert.Equal(status is 429 or 503, result.Retryable);
    }

    [Fact]
    public async Task ARefusalIsARefusalRatherThanAnError()
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { refusal = "I can't help with that." }, finish_reason = "stop" },
            },
        });
        var handler = new FakeHandler((_, _) => Ok(body));
        var artist = new OpenAiArtist("https://x.test/v1", "k", "m", handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task HittingTheOutputLimitIsTruncationRatherThanAParseFailure()
    {
        var handler = new FakeHandler((_, _) => Ok(Completion("""{"inbetweens":[""", "length")));
        var artist = new OpenAiArtist("https://x.test/v1", "k", "m", handler: handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Truncated, result.Outcome);
    }

    [Fact]
    public async Task SomethingThatIsNotAChatCompletionSaysSo()
    {
        // Pointing the endpoint at the wrong path is a common mistake, and
        // "not a chat completion" is a far better clue than a parse error.
        var handler = new FakeHandler((_, _) => Ok("<html>hello</html>"));
        var artist = new OpenAiArtist("https://x.test/v1", "k", "m", "OpenAI", handler);

        var result = await artist.GenerateInbetweensAsync(Request(), CancellationToken.None);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("not a chat completion", result.Message);
    }
}
