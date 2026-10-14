using Lightbox.Ai.Cli;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Q262: inbetweening runs at low effort by default, and an artist can raise
/// it. Measured on real drawings 2026-10-13: thinking was 60–90% of a request's
/// output tokens, low effort was 1.4–7× faster and up to 5× cheaper than the
/// model's default on four layers, and the frames were indistinguishable — what
/// limited quality was what the model was shown, not how long it thought.
/// </summary>
[Collection("AiSettingsFile")]
public class AiEffortTests
{
    private static InbetweenRequest Request() => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Points = [new(0, 0, 0.5)] }],
        [new Stroke { Points = [new(0, 50, 0.5)] }],
        [0.5],
        Easing.Linear);

    private sealed class Recording : ICliRunner
    {
        public List<CliInvocation> Runs { get; } = [];

        public Task<CliOutput> RunAsync(CliInvocation invocation, CancellationToken ct)
        {
            Runs.Add(invocation);
            return Task.FromResult(new CliOutput(1, "", "stopped by the test"));
        }
    }

    private static string? After(CliInvocation run, string flag)
    {
        var at = run.Arguments.ToList().IndexOf(flag);
        return at < 0 ? null : run.Arguments[at + 1];
    }

    [Theory]
    [InlineData(AiProviders.Anthropic)]
    [InlineData(AiProviders.ClaudeCli)]
    public void BothClaudeRoutesDefaultToLowEffort(string providerId)
    {
        var connection = new AiConnection { ProviderId = providerId };

        Assert.Equal("low", connection.Value("effort"));
        Assert.False(connection.Provider.Fields.First(f => f.Id == "effort").Required);
    }

    [Fact]
    public async Task ClaudeCodeIsToldTheEffort()
    {
        var cli = new Recording();
        await new ClaudeCliArtist(cli, "claude", null, "medium").GenerateInbetweensAsync(Request(), default);

        Assert.Equal("medium", After(cli.Runs[0], "--effort"));
    }

    [Fact]
    public async Task WithNoEffortNamedClaudeCodeRunsAtLow()
    {
        // The artist class has the same default as the catalogue, so a caller
        // that builds one directly — a test, the connection tester — does not
        // quietly get the slow setting.
        var cli = new Recording();
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal("low", After(cli.Runs[0], "--effort"));
    }

    [Theory]
    [InlineData(" High ", "high")]
    [InlineData("MAX", "max")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("Model", "model")]   // send nothing: the model's own default
    [InlineData("turbo", "low")]     // not a level: the default, not an error mid-request
    [InlineData("", "low")]
    [InlineData(null, "low")]
    public void AnEffortIsOneOfTheLevelsOrTheDefault(string? typed, string expected)
    {
        Assert.Equal(expected, AiEffort.Normalize(typed));
    }

    [Fact]
    public async Task EffortIsPassedOnceAndAfterTheModel()
    {
        var cli = new Recording();
        await new ClaudeCliArtist(cli, "claude", "claude-opus-5", "high").GenerateInbetweensAsync(Request(), default);

        var args = cli.Runs[0].Arguments.ToList();
        Assert.Single(args, a => a == "--effort");
        Assert.True(args.IndexOf("--effort") > args.IndexOf("--model"));
    }

    [Fact]
    public async Task TheModelChoiceSendsClaudeCodeNoEffortAtAll()
    {
        // For a model or a Claude Code that does not take the setting: the one
        // way to say "leave it out", since an empty field means the default.
        var cli = new Recording();
        await new ClaudeCliArtist(cli, "claude", null, "model").GenerateInbetweensAsync(Request(), default);

        Assert.DoesNotContain("--effort", cli.Runs[0].Arguments);
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("high", "high")]
    public void TheApiRequestCarriesTheEffort(string effort, string onTheWire)
    {
        // The property alone would pass on an artist that never used it.
        var parameters = new AnthropicArtist("sk-ant-test", null, effort)
            .Parameters("system", "user", StrokeSchemas.InbetweenResult, null);

        Assert.Equal(onTheWire, parameters.OutputConfig!.Effort!.Raw());
        Assert.NotNull(parameters.OutputConfig.Format);
    }

    [Fact]
    public void TheModelChoiceSendsTheApiNoEffortAtAll()
    {
        var parameters = new AnthropicArtist("sk-ant-test", null, "model")
            .Parameters("system", "user", StrokeSchemas.InbetweenResult, null);

        Assert.Null(parameters.OutputConfig!.Effort);
        Assert.NotNull(parameters.OutputConfig.Format);
    }

    [Theory]
    [InlineData("output_config.effort: this model does not support effort")]
    [InlineData("error: unknown option '--effort'")]
    public void ARejectedEffortSaysWhatToChange(string said)
    {
        var message = AiEffort.Rejection(said, "low");

        Assert.NotNull(message);
        Assert.Contains("Effort", message);
        Assert.Contains("model", message);
    }

    [Fact]
    public void AnErrorAboutSomethingElseIsNotBlamedOnEffort()
    {
        Assert.Null(AiEffort.Rejection("rate limit exceeded", "low"));
    }

    [Fact]
    public async Task AnOlderClaudeCodeThatDoesNotKnowTheFlagIsToldPlainly()
    {
        var cli = new Refusing("error: unknown option '--effort'");
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("Effort", result.Message);
    }

    private sealed class Refusing(string stderr) : ICliRunner
    {
        public Task<CliOutput> RunAsync(CliInvocation invocation, CancellationToken ct) =>
            Task.FromResult(new CliOutput(1, "", stderr));
    }

    [Fact]
    public void TheFactoryHandsTheEffortToClaudeCode()
    {
        var connection = new AiConnection { ProviderId = AiProviders.ClaudeCli };
        connection.Set("effort", "high");

        var artist = Assert.IsType<ClaudeCliArtist>(AiArtistFactory.Create(connection));

        Assert.Equal("high", artist.Effort);
    }

    [Fact]
    public void TheFactoryHandsTheEffortToTheApi()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Anthropic };
        connection.Set("apiKey", "sk-ant-test");
        connection.Set("effort", "medium");

        var artist = Assert.IsType<AnthropicArtist>(AiArtistFactory.Create(connection));

        Assert.Equal("medium", artist.Effort);
    }
}
