using System.Net;
using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Test connection, end to end against a scripted endpoint.
/// </summary>
/// <remarks>
/// The point of these is the distinction the feature exists for: a reply can
/// be perfectly well-formed JSON and still be useless. A test that only asked
/// "did it parse" would pass on every failure below — which is exactly the
/// trap a reachability ping falls into, one level up.
/// </remarks>
public class AiConnectionTesterTests
{
    private static AiConnection Complete()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Compatible };
        connection.Set("baseUrl", "https://scripted.test/v1");
        connection.Set("model", "scripted");
        return connection;
    }

    /// <summary>An OpenAI-dialect endpoint that answers from a queue in turn.</summary>
    private static OpenAiArtist Scripted(params string[] replies)
    {
        var remaining = new Queue<string>(replies);
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { content = remaining.Count > 0 ? remaining.Dequeue() : "{}" },
                        finish_reason = "stop",
                    },
                },
            })),
        });
        return new OpenAiArtist("https://scripted.test/v1", "k", "scripted", "The endpoint", handler);
    }

    private static object WireStroke(string label, (double X, double Y)[] points) => new
    {
        tool = "brush", color = "#000000", size = 4.0, hardness = 1.0, opacity = 1.0, label,
        points = points.Select(p => new { x = p.X, y = p.Y, pressure = 0.6 }).ToArray(),
    };

    private static string Tween(double t, params (double X, double Y)[] points) =>
        JsonSerializer.Serialize(new
        {
            inbetweens = new[] { new { t, strokes = new[] { WireStroke("arm", points) } } },
        });

    /// <summary>A good quick-test reply, so a thorough test can get past stage one.</summary>
    private static string GoodLine() => Tween(0.5, (10, 64), (110, 64));

    private static Task<AiConnectionCheck> Run(AiTestDepth depth, params string[] replies) =>
        AiConnectionTester.TestAsync(Complete(), depth, Scripted(replies));

    // ---- the quick test -------------------------------------------------------

    [Fact]
    public async Task AQuickTestPassesOnOneGoodLine()
    {
        var check = await Run(AiTestDepth.Quick, GoodLine());

        Assert.True(check.Ok);
        Assert.True(check.Connected);
        Assert.Contains("1 stroke", check.Message);
    }

    [Fact]
    public async Task AQuickTestMakesOneCall()
    {
        // The whole point of having two depths: quick has to stay cheap.
        // Both depths ask for an inbetween now — inbetweening is the only
        // thing the application asks a model for — so what separates them is
        // the number of calls and how hard the second one looks.
        var artist = new CountingArtist();
        await AiConnectionTester.TestAsync(Complete(), AiTestDepth.Quick, artist);

        Assert.Equal(1, artist.Inbetweens);
    }

    [Fact]
    public async Task AThoroughTestMakesTwo()
    {
        var artist = new CountingArtist();
        await AiConnectionTester.TestAsync(Complete(), AiTestDepth.Thorough, artist);

        Assert.Equal(2, artist.Inbetweens);
    }

    // ---- output that parses and is still no good ------------------------------

    [Fact]
    public async Task AStrokeWithOnePointIsUnusableRatherThanAPass()
    {
        // It parses, it is a stroke, and it would mark nothing.
        var check = await Run(AiTestDepth.Quick, Tween(0.5, (64, 64)));

        Assert.False(check.Ok);
        Assert.True(check.Connected);   // the connection is fine; the output is not
        Assert.Contains("two points", check.Message);
    }

    [Fact]
    public async Task AStrokeWithNoExtentIsUnusable()
    {
        // Two points in the same place: a dot where a line was asked for.
        // Survives both the schema and the clamp, so only a real check sees it.
        var check = await Run(AiTestDepth.Quick, Tween(0.5, (64, 64), (64, 64)));

        Assert.False(check.Ok);
        Assert.True(check.Connected);
        Assert.Contains("dot, not a line", check.Message);
    }

    [Fact]
    public async Task PointsOffTheCanvasAreClampedBeforeTheTesterSeesThem()
    {
        // Why BadStrokes has no off-canvas check: StrokeWire.FromWire already
        // pulls every point into the scene plus a margin, so nothing reaching
        // here can be off-canvas. Writing this down is cheaper than adding
        // that unreachable check a second time.
        //
        // A model that ignored the canvas entirely is still caught, and by the
        // right complaint: both points clamp onto the same corner, so what
        // arrives is a dot.
        var check = await Run(AiTestDepth.Quick, Tween(0.5, (9000, 9000), (9100, 9000)));

        Assert.False(check.Ok);
        Assert.Contains("dot, not a line", check.Message);
    }

    [Fact]
    public async Task AnEmptyReplyFails()
    {
        var check = await Run(AiTestDepth.Quick, """{"inbetweens":[]}""");

        Assert.False(check.Ok);
        Assert.Contains("no usable inbetween", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the competence check -------------------------------------------------

    [Fact]
    public async Task AThoroughTestPassesWhenTheInbetweenLandsBetweenTheKeys()
    {
        // The keys sit at y=20 and y=100, so y=60 is where it belongs.
        var check = await Run(AiTestDepth.Thorough, GoodLine(), Tween(0.5, (20, 60), (100, 60)));

        Assert.True(check.Ok);
        Assert.Contains("where it belongs", check.Message);
    }

    [Fact]
    public async Task AThoroughTestFailsWhenTheModelCopiedAKeyInstead()
    {
        // The failure a small model actually produces: well-formed, plausible,
        // and not an inbetween. No parse check can see this one.
        var check = await Run(AiTestDepth.Thorough, GoodLine(), Tween(0.5, (20, 20), (100, 20)));

        Assert.False(check.Ok);
        Assert.True(check.Connected);
        Assert.Contains("outside the two keys", check.Message);
        Assert.Contains("too small for inbetweening", check.Message);
    }

    [Fact]
    public async Task ATimingOutsideTheKeysLeavesNoUsableFrame()
    {
        var check = await Run(AiTestDepth.Thorough, GoodLine(), Tween(1.5, (20, 60), (100, 60)));

        Assert.False(check.Ok);
        Assert.Contains("no usable inbetween", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheChecksNameTheProblemRatherThanJustFailing()
    {
        Assert.Contains("nothing was drawn", AiConnectionTester.BadStrokes([])!);
        Assert.Null(AiConnectionTester.BadInbetween([Frame(0.5, (20, 60), (100, 60))]));
        Assert.Contains("not between the keys", AiConnectionTester.BadInbetween([Frame(0, (20, 60))])!);
        Assert.Contains("no frames came back", AiConnectionTester.BadInbetween([])!);
    }

    // ---- what the Configure window narrates -----------------------------------

    [Fact]
    public async Task EachStageIsReportedSoALongTestCanSayWhatItIsDoing()
    {
        var stages = new List<string>();
        await AiConnectionTester.TestAsync(
            Complete(), AiTestDepth.Thorough, new CountingArtist(), new Immediate(stages.Add));

        Assert.Contains(stages, s => s.Contains("short line"));
        Assert.Contains(stages, s => s.Contains("inbetween"));
    }

    /// <summary>Synchronous progress — <c>Progress&lt;T&gt;</c> posts, and a test cannot wait on that.</summary>
    private sealed class Immediate(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    // ---- helpers ---------------------------------------------------------------

    private static InbetweenFrameResult Frame(double t, params (double X, double Y)[] points) =>
        new(t, [Line(points)]);

    private static Stroke Line(params (double X, double Y)[] points) => new()
    {
        Label = "arm",
        Points = points.Select(p => new StrokePoint(p.X, p.Y, 0.6)).ToList(),
    };

    /// <summary>Counts the calls, so "quick stays cheap" is assertable.</summary>
    private sealed class CountingArtist : IAiArtist
    {
        public int Draws { get; private set; }
        public int Inbetweens { get; private set; }

        public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            Inbetweens++;
            return Task.FromResult(AiResult<List<InbetweenFrameResult>>.Success(
                [Frame(0.5, (20, 60), (100, 60))]));
        }

    }
}
