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

    [Fact]
    public async Task AJointWithoutTwoNumbersIsDroppedAndAPartLeftWithFewerThanTwoGoes()
    {
        var cli = new FakeCli(_ => Answer(new[]
        {
            new { name = "arm", joints = new[] { Joint("shoulder", [10, 10], [12, 10]), Joint("elbow", [50], [60, 20]), Joint("wrist", [90, 10], [95, 30]) } },
            new { name = "tail", joints = new[] { Joint("base", [1, 1], [2, 2]), Joint("tip", [], [3, 3]) } },
        }));

        var result = await new ClaudeCliArtist(cli, "claude").ReadPoseAsync(Request(), default);

        var arm = Assert.Single(result.Value!.Pose.Parts);
        Assert.Equal(["shoulder", "wrist"], arm.Joints.Select(j => j.Name));
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
