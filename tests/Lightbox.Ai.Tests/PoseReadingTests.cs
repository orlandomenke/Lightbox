using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Reading the pose of the subject in two keys (Q274): two pictures in, the
/// joints of each part the model sees in both keys out, in picture pixels. The
/// model reads; it never draws.
/// </summary>
public class PoseReadingTests
{
    private static PoseRequest Request(string? note = null) => new("KEYA", "KEYB", 768, 512, note);

    private static object Joint(string name, double[] a, double[] b) => new { name, a, b };

    private static Cli.CliOutput Answer(object parts) => FakeCli.Result(new
    {
        type = "result",
        subtype = "success",
        is_error = false,
        structured_output = new { notes = "the far leg is hidden in key B", parts },
    });

    [Fact]
    public async Task ThePoseOfEachPartComesBackInBothKeys()
    {
        var cli = new FakeCli(_ => Answer(new[]
        {
            new { name = "near leg", joints = new[] { Joint("hip", [100, 50], [110, 52]), Joint("knee", [120, 200], [180, 190]), Joint("ankle", [150, 330], [160, 320]) } },
        }));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        var leg = Assert.Single(result.Value!.Pose.Parts);
        Assert.Equal("near leg", leg.Name);
        Assert.Equal(["hip", "knee", "ankle"], leg.Joints.Select(j => j.Name));
        Assert.Equal(new PosePoint(180, 190), leg.Joints[1].B);
        Assert.Equal("the far leg is hidden in key B", result.Value.Notes);
    }

    [Fact]
    public async Task BothKeysGoAsPicturesBeforeTheTask()
    {
        var cli = new FakeCli(_ => Answer(Array.Empty<object>()));

        await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        var content = JsonDocument.Parse(cli.Runs[0].Input).RootElement.GetProperty("message").GetProperty("content");
        var kinds = content.EnumerateArray().Select(b => b.GetProperty("type").GetString()).ToList();
        Assert.Equal(["image", "image", "text"], kinds);
        Assert.Equal("KEYA", content[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("KEYB", content[1].GetProperty("source").GetProperty("data").GetString());
        Assert.Contains(Prompts.PoseSystem, cli.Runs[0].Arguments);
    }

    /// <summary>
    /// A joint without two numbers ends its chain there (Q274 review): dropping
    /// it from the middle would join the joints either side into one long
    /// straight bone — a wrong guide that looks right. A chain left with fewer
    /// than two joints is no chain and goes.
    /// </summary>
    [Fact]
    public async Task AJointWithoutTwoNumbersEndsItsChainAndAPartLeftWithFewerThanTwoGoes()
    {
        var cli = new FakeCli(_ => Answer(new[]
        {
            new { name = "arm", joints = new[] { Joint("shoulder", [10, 10], [12, 10]), Joint("elbow", [50, 10], [60, 20]), Joint("wrist", [90], [95, 30]), Joint("hand", [99, 10], [99, 30]) } },
            new { name = "tail", joints = new[] { Joint("base", [1, 1], [2, 2]), Joint("tip", [], [3, 3]) } },
        }));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        var arm = Assert.Single(result.Value!.Pose.Parts);
        Assert.Equal(["shoulder", "elbow"], arm.Joints.Select(j => j.Name));
    }

    /// <summary>
    /// The schema is not enforced by every provider (Q274 review): a null where a
    /// list or a name should be is read as nothing there, never an exception
    /// that leaves the request hanging.
    /// </summary>
    [Fact]
    public async Task NullsInTheAnswerAreReadAsNothingThere()
    {
        var cli = new FakeCli(_ => FakeCli.Result(new
        {
            type = "result", subtype = "success", is_error = false,
            structured_output = new
            {
                notes = (string?)null,
                parts = new object?[]
                {
                    null,
                    new { name = "tail", joints = (object?)null },
                    new { name = "leg", joints = new object?[] { null, Joint("hip", [10, 10], [10, 10]), new { name = (string?)null, a = new[] { 10.0, 50 }, b = new[] { 20.0, 50 } } } },
                },
            },
        }));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        var leg = Assert.Single(result.Value!.Pose.Parts);
        Assert.Equal(2, leg.Joints.Count);
        Assert.Equal("", result.Value.Notes);
    }

    /// <summary>
    /// A number has to be somewhere in the pictures (Q274 review): a joint far
    /// outside them — 1e308, or a misread — is not a guess worth drawing, and
    /// a large enough one overflows when it is mapped back to the scene. A
    /// little outside is kept, for a joint hidden past the crop's edge.
    /// </summary>
    [Fact]
    public async Task AJointFarOutsideThePicturesEndsItsChain()
    {
        var cli = new FakeCli(_ => Answer(new[]
        {
            new { name = "leg", joints = new[] { Joint("hip", [100, 50], [100, 50]), Joint("knee", [-20, 200], [110, 520]), Joint("ankle", [1.7e308, 10], [150, 330]) } },
        }));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        var leg = Assert.Single(result.Value!.Pose.Parts);
        Assert.Equal(["hip", "knee"], leg.Joints.Select(j => j.Name));   // 20 and 8 outside: kept
    }

    /// <summary>
    /// Every part has a name, and only one part has each (Q274 review): the
    /// prompt's whole promise is that a name means the same part in both keys,
    /// so a nameless part, or a second part under a name already taken, cannot
    /// be trusted to be what it says.
    /// </summary>
    [Fact]
    public async Task ANamelessOrRepeatedPartIsDropped()
    {
        var cli = new FakeCli(_ => Answer(new[]
        {
            new { name = "leg", joints = new[] { Joint("hip", [1, 1], [1, 1]), Joint("knee", [2, 2], [2, 2]) } },
            new { name = "  ", joints = new[] { Joint("a", [1, 1], [1, 1]), Joint("b", [2, 2], [2, 2]) } },
            new { name = "Leg", joints = new[] { Joint("hip", [5, 5], [5, 5]), Joint("knee", [6, 6], [6, 6]) } },
        }));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        var leg = Assert.Single(result.Value!.Pose.Parts);
        Assert.Equal(new PosePoint(1, 1), leg.Joints[0].A);
    }

    [Fact]
    public async Task NoUsablePartIsARetryableError()
    {
        var cli = new FakeCli(_ => Answer(Array.Empty<object>()));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task AProviderThatCannotSeePicturesSaysSo()
    {
        var result = await ((IAiArtist)new OllamaArtist()).ReadPoseAsync(Request(), default);

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("pose", result.Message);
    }

    [Fact]
    public void TheTaskAsksForTheSamePartUnderTheSameNameAndForNoDrawing()
    {
        var user = Prompts.PoseUser(Request("the \"dog\" jumps"));

        Assert.Contains("768", user);
        Assert.Contains("same name", user);
        Assert.Contains("\"the \\\"dog\\\" jumps\"", user);   // the note is data, JSON-encoded
        Assert.Contains("do not draw", Prompts.PoseSystem);
    }

    [Fact]
    public void TheApiProviderSendsBothKeys()
    {
        var parameters = new AnthropicArtist("sk-ant-test").Parameters("system", "user", StrokeSchemas.PoseResult, ["KEYA", "KEYB"]);

        var sent = JsonSerializer.Serialize(parameters);
        Assert.True(sent.IndexOf("KEYA", StringComparison.Ordinal) < sent.IndexOf("KEYB", StringComparison.Ordinal));
    }
}
