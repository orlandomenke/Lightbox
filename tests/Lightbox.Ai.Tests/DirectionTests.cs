using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Q261: an artist's own words travel with an inbetween request — as a
/// direction on a first ask, and as a revision of frames the model drew
/// before. The verifier still judges every answer; the words change what is
/// asked, never what is accepted.
/// </summary>
public class DirectionTests
{
    private static Stroke Arm(double y) => new()
    {
        Label = "arm",
        Points = [new(40, y, 0.6), new(216, y, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    private static InbetweenRequest Swing(params double[] ts) =>
        new(new SceneInfo(256, 256, 12), [Arm(40)], [Arm(216)], ts, Easing.Linear);

    [Fact]
    public void ARequestWithNoDirectionReadsExactlyAsBefore()
    {
        // Absent means absent: nothing about directions reaches a prompt that
        // has none, so every existing request costs what it did.
        var prompt = Prompts.InbetweenUser(Swing(0.5));

        Assert.DoesNotContain("direction", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("earlier", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDirectionIsSentInTheArtistsWordsAfterTheDrawing()
    {
        var prompt = Prompts.InbetweenUser(Swing(0.5) with { Direction = "Let the arm lead, \"snappy\"." });

        // JSON-encoded, so a quote in it cannot end the string it sits in.
        var encoded = JsonSerializer.Serialize("Let the arm lead, \"snappy\".", new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        Assert.Contains(encoded, prompt);
        // After the keys: the drawing is the stable prefix of a request, the
        // words are what changes between asks.
        Assert.True(prompt.IndexOf(encoded, StringComparison.Ordinal) > prompt.IndexOf("keyframeB", StringComparison.Ordinal));
        // Said plainly that the keys still bound it — the verifier will.
        Assert.Contains("between the keys", prompt);
    }

    [Fact]
    public void ARevisionCarriesTheEarlierDrawingsAndTheFramesThatStay()
    {
        var request = Swing(0.5) with
        {
            Direction = "More overlap on the hand.",
            Earlier = [new AcceptedFrame(0.5, [Arm(130)])],
            Fixed = [new AcceptedFrame(0.25, [Arm(84)]), new AcceptedFrame(0.75, [Arm(172)])],
        };

        var prompt = Prompts.InbetweenUser(request);

        Assert.Contains("130", prompt);   // the drawing being revised
        Assert.Contains("84", prompt);    // a neighbour that stays
        Assert.Contains("172", prompt);
        Assert.Contains("More overlap on the hand.", prompt);
    }

    [Fact]
    public async Task ARepairReAskKeepsTheDirectionAndTheRevisionContext()
    {
        // The first answer is refused (it is key A again), so the loop asks
        // again. The artist's words and the frames that stay must ride along
        // on that re-ask, or the second answer ignores what was asked.
        var artist = new Recorder(
            AiResult<List<InbetweenFrameResult>>.Success([new InbetweenFrameResult(0.5, [Arm(40)])]),
            AiResult<List<InbetweenFrameResult>>.Success([new InbetweenFrameResult(0.5, [Arm(128)])]));
        var request = Swing(0.5) with
        {
            Direction = "Keep it on the arc.",
            Earlier = [new AcceptedFrame(0.5, [Arm(120)])],
            Fixed = [new AcceptedFrame(0.25, [Arm(84)])],
        };

        var run = await InbetweenRepair.RunAsync(artist, request, CancellationToken.None);

        Assert.Equal(AiOutcome.Success, run.Outcome);
        Assert.Equal(2, artist.Requests.Count);
        var reAsk = artist.Requests[1];
        Assert.Equal("Keep it on the arc.", reAsk.Direction);
        Assert.NotNull(reAsk.Fixed);
        Assert.Single(reAsk.Fixed!);
        Assert.NotNull(reAsk.Repair);
    }

    [Fact]
    public async Task AReAskCarriesOnlyTheEarlierDrawingsItIsStillAskingAbout()
    {
        // Two frames revised; the first comes back good, the second is refused
        // (key A again). The re-ask is about t=0.75 alone, so the earlier drawing
        // at t=0.25 — already settled — must not ride along a second time.
        var artist = new Recorder(
            AiResult<List<InbetweenFrameResult>>.Success(
                [new InbetweenFrameResult(0.25, [Arm(84)]), new InbetweenFrameResult(0.75, [Arm(40)])]),
            AiResult<List<InbetweenFrameResult>>.Success([new InbetweenFrameResult(0.75, [Arm(172)])]));
        var request = Swing(0.25, 0.75) with
        {
            Direction = "Smoother.",
            Earlier = [new AcceptedFrame(0.25, [Arm(80)]), new AcceptedFrame(0.75, [Arm(168)])],
        };

        await InbetweenRepair.RunAsync(artist, request, CancellationToken.None);

        var reAsk = artist.Requests[1];
        Assert.Equal([0.75], reAsk.Ts);
        Assert.Equal([0.75], reAsk.Earlier!.Select(e => e.T).ToList());
    }

    [Fact]
    public void TheRevisionComesBeforeTheDirectionAndCarriesTheDrawingsAsData()
    {
        var request = Swing(0.5) with
        {
            Direction = "Smoother.",
            Earlier = [new AcceptedFrame(0.5, [Arm(130)])],
        };

        var prompt = Prompts.InbetweenUser(request);

        var revision = prompt.IndexOf("You drew these frames before", StringComparison.Ordinal);
        var direction = prompt.IndexOf("Smoother.", StringComparison.Ordinal);
        Assert.True(revision > 0 && direction > revision);
        // The earlier drawing is there as strokes, at its t.
        var json = prompt[(prompt.IndexOf('[', revision))..];
        using var parsed = JsonDocument.Parse(json[..(json.IndexOf("]\n", StringComparison.Ordinal) + 1)]
            .Split('\n')[0]);
        var first = parsed.RootElement[0];
        Assert.Equal(0.5, first.GetProperty("t").GetDouble());
        Assert.Equal(130, first.GetProperty("yourStrokes")[0].GetProperty("points")[0].GetProperty("y").GetDouble());
    }

    private sealed class Recorder(params AiResult<List<InbetweenFrameResult>>[] answers) : IAiArtist
    {
        private int _next;

        public List<InbetweenRequest> Requests { get; } = [];

        public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(answers[Math.Min(_next++, answers.Length - 1)]);
        }

        public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(SubjectRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<SubjectTaxonomy>.Refused("not under test"));
    }
}
