using System.Text.Json;
using Lightbox.Ai.Cli;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

/// <summary>A scripted command line: records what was launched, answers what it was told to.</summary>
internal sealed class FakeCli(Func<CliInvocation, CliOutput> respond) : ICliRunner
{
    public List<CliInvocation> Runs { get; } = [];

    public Task<CliOutput> RunAsync(CliInvocation invocation, CancellationToken ct)
    {
        Runs.Add(invocation);
        return Task.FromResult(respond(invocation));
    }

    /// <summary>What a headless run prints: an init line, noise, then the result line.</summary>
    public static CliOutput Result(object result) => new(0,
        "{\"type\":\"system\",\"subtype\":\"init\"}\n"
        + "not json at all\n"
        + JsonSerializer.Serialize(result) + "\n",
        "");
}

public class ClaudeCliArtistTests
{
    private static InbetweenRequest Request(IReadOnlyList<string>? images = null) => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Label = "arm", Points = [new(0, 0, 0.5)] }],
        [new Stroke { Label = "arm", Points = [new(0, 50, 0.5)] }],
        [0.5],
        Easing.Linear,
        images);

    private const string OneFrame = """
        {"inbetweens":[{"t":0.5,"strokes":[{"tool":"brush","color":"#000000","size":6,
        "hardness":0.8,"opacity":1,"label":"arm","points":[{"x":5,"y":25,"pressure":0.5}]}]}]}
        """;

    private static CliOutput Good() => FakeCli.Result(new
    {
        type = "result",
        subtype = "success",
        is_error = false,
        stop_reason = "tool_use",
        result = "",
        structured_output = JsonSerializer.Deserialize<JsonElement>(OneFrame),
    });

    private static string After(CliInvocation run, string flag)
    {
        var at = run.Arguments.ToList().IndexOf(flag);
        Assert.True(at >= 0, $"{flag} was not passed");
        return run.Arguments[at + 1];
    }

    [Fact]
    public async Task TheCallCarriesLightboxsOwnSystemPromptAndSchema()
    {
        var cli = new FakeCli(_ => Good());
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        var run = Assert.Single(cli.Runs);
        Assert.Equal("claude", run.Command);
        Assert.Contains("-p", run.Arguments);
        // Replaced, not appended to: the coding assistant's own prompt has no
        // business beside an inbetweener's.
        Assert.Equal(Prompts.InbetweenSystem, After(run, "--system-prompt"));
        Assert.DoesNotContain("--append-system-prompt", run.Arguments);
        Assert.Equal(
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(StrokeSchemas.InbetweenResult)),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(After(run, "--json-schema"))));
    }

    [Fact]
    public async Task TheModelIsGivenNoToolsAndNoneOfTheMachinesOwnConfiguration()
    {
        var cli = new FakeCli(_ => Good());
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        // A drawing's labels reach the prompt (S4). With no tools, no MCP
        // servers, no hooks and no CLAUDE.md there is nothing for text in a
        // drawing to steer, and nothing of the machine's rides along (A2).
        var run = cli.Runs[0];
        Assert.Equal("", After(run, "--tools"));
        Assert.Contains("--strict-mcp-config", run.Arguments);
        Assert.Contains("--safe-mode", run.Arguments);
        Assert.Contains("--disable-slash-commands", run.Arguments);
        Assert.Contains("--no-session-persistence", run.Arguments);
        Assert.DoesNotContain("--dangerously-skip-permissions", run.Arguments);
    }

    [Fact]
    public async Task TheTaskGoesInOnStandardInputNotOnTheCommandLine()
    {
        var cli = new FakeCli(_ => Good());
        var request = Request();
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(request, default);

        var run = cli.Runs[0];
        Assert.Equal("stream-json", After(run, "--input-format"));
        var task = Prompts.InbetweenUser(request);
        Assert.DoesNotContain(run.Arguments, a => a.Contains(task, StringComparison.Ordinal));

        // One line: the stream framing is newline-delimited.
        Assert.Single(run.Input.TrimEnd('\n').Split('\n'));
        var message = JsonDocument.Parse(run.Input).RootElement;
        Assert.Equal("user", message.GetProperty("type").GetString());
        var content = message.GetProperty("message").GetProperty("content");
        Assert.True(content.GetArrayLength() == 1, "the task is the only block when there are no reference views");
        Assert.Equal(task, content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ReferenceViewsRideAlongAsImagesBeforeTheTask()
    {
        var cli = new FakeCli(_ => Good());
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(["AAAA", "BBBB"]), default);

        var content = JsonDocument.Parse(cli.Runs[0].Input).RootElement
            .GetProperty("message").GetProperty("content");
        Assert.Equal(3, content.GetArrayLength());
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        Assert.Equal("image/png", content[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("AAAA", content[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("BBBB", content[1].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("text", content[2].GetProperty("type").GetString());
    }

    [Fact]
    public async Task TheStructuredOutputBecomesFrames()
    {
        var cli = new FakeCli(_ => Good());
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        var frame = Assert.Single(result.Value!);
        Assert.Equal(0.5, frame.T);
        Assert.Equal("arm", Assert.Single(frame.Strokes).Label);
    }

    [Fact]
    public async Task TheResultTextIsReadWhenThereIsNoStructuredOutput()
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = false, result = OneFrame,
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task ARefusalIsARefusal()
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = false, stop_reason = "refusal", result = "",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task ACutOffReplyIsTruncatedInTheShapeTheCliReallySendsIt()
    {
        // Recorded by forcing a 200-token cap: on the schema path the reply is
        // a tool call, so there is no max_tokens stop — the result is an error
        // whose text names the maximum, with stop_reason "stop_sequence".
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = true, stop_reason = "stop_sequence",
            result = "API Error: Claude's response exceeded the 200 output token maximum. To configure "
                     + "this behavior, set the CLAUDE_CODE_MAX_OUTPUT_TOKENS environment variable.",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Truncated, result.Outcome);
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("model_context_window_exceeded")]
    public async Task AStopForLackOfRoomIsTruncated(string stopReason)
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = false, stop_reason = stopReason, result = "{\"inbet",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Truncated, result.Outcome);
    }

    [Fact]
    public async Task CancellingARequestComesBackAsAValue()
    {
        var runner = new ThrowingCli(new OperationCanceledException());
        var result = await new ClaudeCliArtist(runner, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Equal("Canceled.", result.Message);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public async Task AFileSystemFailureIsAValueRatherThanACrash(string kind)
    {
        // A full or unwritable temp folder fails before the program starts —
        // and an IAiArtist never throws, whatever the reason.
        Exception error = kind == "io"
            ? new DirectoryNotFoundException("Could not find a part of the path 'Z:\\nope'.")
            : new UnauthorizedAccessException("Access to the path is denied.");
        var result = await new ClaudeCliArtist(new ThrowingCli(error), "claude")
            .GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.DoesNotContain("Install", result.Message);
    }

    [Theory]
    [InlineData("error_max_structured_output_retries", false, true)]
    [InlineData("error_max_structured_output_retries", true, true)]
    [InlineData("error_max_turns", false, false)]
    [InlineData("error_max_turns", true, false)]
    [InlineData("error_during_execution", false, false)]
    [InlineData("error_during_execution", true, false)]
    public async Task ARunThatDidNotSucceedIsAnErrorWhateverIsErrorSays(
        string subtype, bool isError, bool retryable)
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype, is_error = isError, stop_reason = "tool_use", result = "",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Equal(retryable, result.Retryable);
        Assert.DoesNotContain("returned no text", result.Message);
    }

    [Fact]
    public async Task AnErrorWithNothingToSaySaysThat()
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = true, result = "",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.DoesNotMatch(@":\s*$", result.Message!);
    }

    [Fact]
    public async Task AnAuthenticationWordInSomeOtherErrorIsNotTakenForSigningOut()
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = true, api_error_status = 400,
            result = "API Error: the request mentions authentication but is malformed",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.DoesNotContain("sign in", result.Message);
    }

    [Theory]
    [InlineData("""["StructuredOutput"]""", "[]", true)]
    [InlineData("""["StructuredOutput","Bash"]""", "[]", false)]
    [InlineData("""["StructuredOutput"]""", """[{"name":"lightbox","status":"connected"}]""", false)]
    public async Task AnAnswerIsRefusedIfTheRunOfferedTheModelAnythingButTheAnswerForm(
        string tools, string servers, bool used)
    {
        // The flags ask for no tools; the start-up line is where the run says
        // what it actually offered. A version that stopped honouring a flag
        // would otherwise hand a hostile stroke label a shell, silently.
        var good = Good();
        var cli = new FakeCli(_ => good with
        {
            Output = $"{{\"type\":\"system\",\"subtype\":\"init\",\"tools\":{tools},\"mcp_servers\":{servers}}}\n"
                     + good.Output,
        });
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(used ? AiOutcome.Success : AiOutcome.Error, result.Outcome);
        if (!used) Assert.False(result.Retryable);
    }

    [Fact]
    public async Task TheLastResultLineCountsAndWindowsLineEndingsAreFine()
    {
        var first = JsonSerializer.Serialize(new
        {
            type = "result", subtype = "success", is_error = true, result = "an earlier attempt",
        });
        var last = JsonSerializer.Serialize(new
        {
            type = "result", subtype = "success", is_error = false, result = OneFrame.ReplaceLineEndings(""),
        });
        var noise = JsonSerializer.Serialize(new { type = "assistant", result = "not the answer" });
        var cli = new FakeCli(_ => new CliOutput(0, $"{first}\r\n{noise}\r\n{last}\r\n", ""));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task ARunWithNoAnswerDoesNotEchoItsStartUpLine()
    {
        // The start-up line names folders and a session id; it tells an artist
        // nothing they can act on.
        var cli = new FakeCli(_ => new CliOutput(1,
            "{\"type\":\"system\",\"subtype\":\"init\",\"cwd\":\"C:\\\\Users\\\\someone\"}\n", ""));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.DoesNotContain("someone", result.Message);
    }

    [Fact]
    public async Task TheLaunchedProgramGetsNoKeyNoGatewayAndNoTrafficOfItsOwn()
    {
        var cli = new FakeCli(_ => Good());
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        var env = cli.Runs[0].Environment!;
        Assert.True(env.ContainsKey("ANTHROPIC_API_KEY") && env["ANTHROPIC_API_KEY"] is null);
        Assert.True(env.ContainsKey("ANTHROPIC_AUTH_TOKEN") && env["ANTHROPIC_AUTH_TOKEN"] is null);
        Assert.True(env.ContainsKey("ANTHROPIC_BASE_URL") && env["ANTHROPIC_BASE_URL"] is null);
        Assert.Equal("1", env["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"]);
    }

    [Fact]
    public async Task NotBeingSignedInSaysHowToSignIn()
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = true,
            result = "Not logged in · Please run /login",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.False(result.Retryable);
        Assert.Contains("sign in", result.Message);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(529, true)]
    [InlineData(400, false)]
    public async Task AnErrorFromTheServiceIsRetryableOnlyWhenWaitingCouldHelp(int status, bool retryable)
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = true,
            api_error_status = status, result = "API Error: something went wrong",
        }));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Equal(retryable, result.Retryable);
        Assert.Contains("something went wrong", result.Message);
    }

    [Fact]
    public async Task ExitingWithoutAResultIsReportedNotParsed()
    {
        var cli = new FakeCli(_ => new CliOutput(1, "", "error: unknown option '--json-schema'"));
        var result = await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("unknown option", result.Message);
    }

    [Fact]
    public async Task ACommandThatCannotStartIsAValueRatherThanAnException()
    {
        var missing = "definitely-not-an-executable-" + Guid.NewGuid().ToString("N");
        var result = await new ClaudeCliArtist(new ProcessCliRunner(), missing)
            .GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.False(result.Retryable);
        Assert.Contains(missing, result.Message);
    }

    [Fact]
    public async Task AModelIsNamedOnlyWhenTheArtistChoseOne()
    {
        var cli = new FakeCli(_ => Good());
        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(), default);
        await new ClaudeCliArtist(cli, "claude", " opus ").GenerateInbetweensAsync(Request(), default);

        Assert.DoesNotContain("--model", cli.Runs[0].Arguments);
        Assert.Equal("opus", After(cli.Runs[1], "--model"));
    }

    [Fact]
    public async Task TheCommandLineStaysWellInsideWhatWindowsAllows()
    {
        // CreateProcess takes 32,767 characters. The system prompt and the
        // schema are the only large things on it — the strokes go on stdin —
        // so this guards the day a prompt grows rather than a drawing.
        var cli = new FakeCli(_ => Good());
        var artist = new ClaudeCliArtist(cli, "claude", "claude-opus-5");
        await artist.GenerateInbetweensAsync(Request(), default);
        await artist.ReadSubjectAsync(new SubjectRequest("Mira", ["AAAA"]), default);

        Assert.Equal(2, cli.Runs.Count);
        foreach (var run in cli.Runs)
        {
            var length = run.Arguments.Sum(a => a.Length + 3);
            Assert.True(length < 16000, $"command line is {length} characters");
        }
    }

    [Fact]
    public async Task TheRunnerHandsInputOverOnStandardInputAndReadsTheReply()
    {
        // sort exists on Windows and on every Unix, and echoes one line back.
        var output = await new ProcessCliRunner().RunAsync(
            new CliInvocation("sort", [], "héllo strokes\n"), default);

        Assert.Equal(0, output.ExitCode);
        Assert.Equal("héllo strokes", output.Output.Trim());
    }

    /// <summary>A child that sits for half a minute, on either platform.</summary>
    private static CliInvocation Sleeper() => OperatingSystem.IsWindows()
        ? new CliInvocation("ping", ["-n", "30", "127.0.0.1"], "")
        : new CliInvocation("sleep", ["30"], "");

    [Fact]
    public async Task CancellingMidRunStopsTheChildAndComesBackPromptly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessCliRunner().RunAsync(Sleeper(), cts.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task ARunPastItsTimeIsStoppedAndSaysSo()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<CliTimeoutException>(
            () => new ProcessCliRunner(TimeSpan.FromMilliseconds(500)).RunAsync(Sleeper(), default));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.Contains("did not answer", e.Message);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    public async Task AStoppedRunLeavesNothingRunning(string how)
    {
        // Returning promptly is not the same as stopping the program: a
        // runner that walked away would return just as fast and leave a
        // half-minute process behind, one per press.
        var started = new List<int>();
        var runner = new ProcessCliRunner(how == "timeout" ? TimeSpan.FromMilliseconds(500) : null)
        {
            Started = started.Add,
        };
        using var cts = new CancellationTokenSource(how == "cancel" ? 500 : 60_000);
        var stopped = false;
        try { await runner.RunAsync(Sleeper(), cts.Token); }
        catch (Exception e) when (e is OperationCanceledException or CliTimeoutException) { stopped = true; }
        Assert.True(stopped, "the run finished on its own instead of being stopped");
        var pid = Assert.Single(started);

        System.Diagnostics.Process? left = null;
        try { left = System.Diagnostics.Process.GetProcessById(pid); }
        catch (ArgumentException) { }
        using (left)
        {
            Assert.True(left is null || left.WaitForExit(2000), $"process {pid} is still running");
        }
    }

    [Fact]
    public async Task ATimeoutReachesTheArtistAsAnErrorThatIsNotAboutInstalling()
    {
        var runner = new ThrowingCli(new CliTimeoutException("“claude” did not answer within 10 minutes and was stopped."));
        var result = await new ClaudeCliArtist(runner, "claude").GenerateInbetweensAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.DoesNotContain("Install", result.Message);
    }

    [Theory]
    [InlineData("claude.cmd")]
    [InlineData(@"C:\tools\claude.BAT")]
    public async Task ABatchFileIsRefusedBeforeAnythingStarts(string command)
    {
        var e = await Assert.ThrowsAsync<CliException>(
            () => new ProcessCliRunner().RunAsync(new CliInvocation(command, ["-p"], ""), default));

        Assert.Contains("batch file", e.Message);
    }

    [Fact]
    public async Task AMegabyteOnStandardInputDoesNotJamThePipes()
    {
        // Images are why the request goes on stdin; this is the size they come at.
        var lines = Enumerable.Range(0, 20000).Select(i => $"{i:D8}-" + new string('x', 56)).ToList();
        var output = await new ProcessCliRunner().RunAsync(
            new CliInvocation("sort", [], string.Join("\n", lines) + "\n"), default);

        Assert.Equal(0, output.ExitCode);
        Assert.Equal(lines.Count, output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task EachRunHasAPrivateFolderThatIsGoneAfterwards()
    {
        var invocation = OperatingSystem.IsWindows()
            ? new CliInvocation("cmd", ["/c", "cd"], "")
            : new CliInvocation("pwd", [], "");
        var output = await new ProcessCliRunner().RunAsync(invocation, default);

        var folder = output.Output.Trim();
        Assert.Contains("lightbox-cli-", folder);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task TheEnvironmentChangesAreApplied()
    {
        Environment.SetEnvironmentVariable("LIGHTBOX_CLI_TEST_REMOVED", "present");
        try
        {
            var change = new Dictionary<string, string?>
            {
                ["LIGHTBOX_CLI_TEST_REMOVED"] = null,
                ["LIGHTBOX_CLI_TEST_ADDED"] = "added",
            };
            var invocation = OperatingSystem.IsWindows()
                ? new CliInvocation("cmd", ["/c", "set"], "", change)
                : new CliInvocation("env", [], "", change);
            var output = await new ProcessCliRunner().RunAsync(invocation, default);

            Assert.Contains("LIGHTBOX_CLI_TEST_ADDED=added", output.Output);
            Assert.DoesNotContain("LIGHTBOX_CLI_TEST_REMOVED", output.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_CLI_TEST_REMOVED", null);
        }
    }
}

/// <summary>A runner that fails the way a real one can, without a process.</summary>
internal sealed class ThrowingCli(Exception error) : ICliRunner
{
    public Task<CliOutput> RunAsync(CliInvocation invocation, CancellationToken ct) => Task.FromException<CliOutput>(error);
}

/// <summary>The catalogue entry and the factory case.</summary>
[Collection("AiSettingsFile")]
public class ClaudeCliProviderTests
{
    [Fact]
    public void ItNeedsNoKeyAndIsCompleteOutOfTheBox()
    {
        var provider = AiProviders.Find(AiProviders.ClaudeCli)!;
        Assert.Equal(AiTransport.Cli, provider.Transport);
        Assert.DoesNotContain(provider.Fields, f => f.Kind == AiFieldKind.Secret);

        var connection = new AiConnection { ProviderId = AiProviders.ClaudeCli };
        Assert.True(connection.IsComplete);
        Assert.Equal("claude", connection.Value("command"));
        // The same model as the API route unless the artist names another:
        // Claude Code's own default follows the account and can be lighter.
        Assert.Equal(AnthropicArtist.Model, connection.Value("model"));
    }

    [Fact]
    public void TheFactoryBuildsItWithoutLaunchingAnything()
    {
        // Unlike the MCP path nothing starts until a request is made, so a
        // machine without Claude Code still starts Lightbox; Test connection
        // is where the person finds out.
        var connection = new AiConnection { ProviderId = AiProviders.ClaudeCli };
        connection.Set("command", "definitely-not-an-executable-" + Guid.NewGuid().ToString("N"));

        Assert.IsType<ClaudeCliArtist>(AiArtistFactory.Create(connection));
    }

    [Fact]
    public void ItIsNotTheDefault()
    {
        Assert.Equal(AiProviders.Anthropic, AiProviders.DefaultId);
    }
}
