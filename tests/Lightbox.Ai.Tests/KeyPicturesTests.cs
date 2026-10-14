using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

/// <summary>
/// The keys as pictures (2026-10-14). Sent only as stroke coordinates, a model
/// pairs stroke 5 with stroke 5 — which is a different line whenever the keys
/// were drawn with different strokes, as an artist draws them and as a trace
/// does. With the pictures it can see which line is which part of the subject.
/// </summary>
public class KeyPicturesTests
{
    private static InbetweenRequest Request(IReadOnlyList<KeyPicture>? pictures = null, IReadOnlyList<string>? sheets = null) => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Points = [new(0, 0, 0.5), new(10, 0, 0.5)] }],
        [new Stroke { Points = [new(0, 50, 0.5), new(10, 50, 0.5)] }],
        [0.5],
        Easing.Linear,
        sheets,
        KeyPictures: pictures);

    private static readonly KeyPicture[] Keys =
    [
        new("Key A, this layer: scene x 100–900, y 50–500, at 0.5 scale.", "KEYA"),
        new("Key B, this layer: scene x 100–900, y 50–500, at 0.5 scale.", "KEYB"),
    ];

    private static Lightbox.Ai.Cli.CliOutput Answer() => FakeCli.Result(new
    {
        type = "result",
        subtype = "success",
        is_error = false,
        structured_output = new
        {
            inbetweens = new[]
            {
                new { t = 0.5, strokes = new[] { new { tool = "brush", color = "#000000", size = 4.0, hardness = 1.0, opacity = 1.0, label = (string?)null, points = new[] { new { x = 0.0, y = 25.0, pressure = 0.5 }, new { x = 10.0, y = 25.0, pressure = 0.5 } } } } },
            },
        },
    });

    [Fact]
    public async Task EachKeyPictureArrivesWithItsCaptionBeforeTheSheetsAndTheTask()
    {
        var cli = new FakeCli(_ => Answer());

        await new ClaudeCliArtist(cli, "claude").GenerateInbetweensAsync(Request(Keys, ["SHEET"]), default);

        var content = JsonDocument.Parse(cli.Runs[0].Input).RootElement.GetProperty("message").GetProperty("content");
        var kinds = content.EnumerateArray().Select(b => b.GetProperty("type").GetString()).ToList();
        Assert.Equal(["text", "image", "text", "image", "image", "text"], kinds);
        Assert.Equal(Keys[0].Caption, content[0].GetProperty("text").GetString());
        Assert.Equal("KEYA", content[1].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("KEYB", content[3].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("SHEET", content[4].GetProperty("source").GetProperty("data").GetString());
    }

    [Fact]
    public void TheApiProviderSendsThemTheSameWay()
    {
        var parameters = new AnthropicArtist("sk-ant-test").Parameters(
            "system", "user", StrokeSchemas.InbetweenResult, Prompts.Images(Request(Keys, ["SHEET"])));

        var sent = JsonSerializer.Serialize(parameters);
        Assert.Contains("KEYA", sent);
        Assert.Contains("SHEET", sent);
        Assert.Contains(JsonSerializer.Serialize(Keys[1].Caption).Trim('"'), sent);
        Assert.True(sent.IndexOf("KEYA", StringComparison.Ordinal) < sent.IndexOf("SHEET", StringComparison.Ordinal),
            "the keys come before the character sheet");
    }

    [Fact]
    public void TheTaskSaysThePicturesAreThereOnlyWhenTheyAre()
    {
        Assert.Contains("pictures", Prompts.InbetweenUser(Request(Keys)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pictures", Prompts.InbetweenUser(Request()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheModelIsNoLongerToldToKeepTheKeysStrokeForStroke()
    {
        // The instruction that made it pair stroke 5 with stroke 5.
        Assert.DoesNotContain("keep stroke count", Prompts.InbetweenSystem);
        Assert.Contains("one to one", Prompts.InbetweenSystem);
        // Labels still bind a part to its stroke where the artist named them.
        Assert.Contains("label", Prompts.InbetweenSystem);
    }

    [Fact]
    public void ARequestWithNoPicturesSendsWhatItSentBefore()
    {
        var images = Prompts.Images(Request(sheets: ["SHEET"]));

        var only = Assert.Single(images);
        Assert.Null(only.Caption);
        Assert.Equal("SHEET", only.PngBase64);
    }
}
