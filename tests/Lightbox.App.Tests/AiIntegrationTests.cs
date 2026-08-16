using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>Scripted fake artist — no network.</summary>
internal sealed class FakeArtist : IAiArtist
{
    public AiResult<List<InbetweenFrameResult>>? InbetweenResult { get; set; }

    /// <summary>
    /// One answer per call, for tests about the repair loop. When it runs dry
    /// — or is never set — <see cref="InbetweenResult"/> answers instead, which
    /// is what makes "this model never improves" the default rather than
    /// something each test has to spell out.
    /// </summary>
    public Queue<AiResult<List<InbetweenFrameResult>>> InbetweenAnswers { get; } = new();

    public AiResult<SubjectTaxonomy>? SubjectResult { get; set; }
    public InbetweenRequest? LastInbetweenRequest { get; private set; }
    public int InbetweenCalls { get; private set; }
    public SubjectRequest? LastSubjectRequest { get; private set; }

    public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        LastInbetweenRequest = request;
        InbetweenCalls++;
        if (InbetweenAnswers.Count > 0) return Task.FromResult(InbetweenAnswers.Dequeue());
        return Task.FromResult(InbetweenResult
            ?? AiResult<List<InbetweenFrameResult>>.Error("unscripted", false));
    }

    public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(
        SubjectRequest request, CancellationToken ct)
    {
        LastSubjectRequest = request;
        return Task.FromResult(SubjectResult ?? AiResult<SubjectTaxonomy>.Error("unscripted", false));
    }
}

public class AiIntegrationTests
{
    private static Stroke Dot(double x, double y) => new()
    {
        Points = [new(x, y, 0.5)],
        Brush = new BrushSettings { Size = 6, Hardness = 1 },
    };

    /// <summary>
    /// A horizontal line at <paramref name="y"/>, shaped like the keys the
    /// fixture draws — so the verifier can match it and judge betweenness.
    /// The keys sit at y=10 and y=60.
    /// </summary>
    private static Stroke Seg(double y) => new()
    {
        Points = [new(10, y, 0.5), new(30, y, 0.5)],
        Brush = new BrushSettings { Size = 6, Hardness = 1 },
    };

    private static MainViewModel VmWithTwoKeys(FakeArtist artist)
    {
        var vm = new MainViewModel(artist);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 10, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(10, 60, 0.5);
        vm.MoveStroke(30, 60, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    [AvaloniaFact]
    public void NoArtist_DisablesAi()
    {
        var vm = new MainViewModel(null);
        Assert.False(vm.IsAiAvailable);
        Assert.False(vm.CanUseAi);
        Assert.NotEmpty(vm.AiUnavailableHint);
    }

    [AvaloniaFact]
    public async Task AiInbetween_InsertsFramesThroughSharedPath()
    {
        // The eased expectations for ts [1/3, 2/3] between y=10 and y=60 are
        // y≈21 and y≈49; these answers sit on them, so the verifier passes
        // both. Returned out of order on purpose.
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
            [
                new InbetweenFrameResult(2.0 / 3, [Seg(49)]),
                new InbetweenFrameResult(1.0 / 3, [Seg(21)]),
            ]),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 2;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        // Request carried scene + both keys + evenly spaced ts.
        Assert.NotNull(artist.LastInbetweenRequest);
        Assert.Equal([1.0 / 3, 2.0 / 3], artist.LastInbetweenRequest!.Ts);
        Assert.Single(artist.LastInbetweenRequest.KeyframeA);

        // Frames inserted sorted by t.
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        var layer = vm.PaintLayer();
        var f1 = Assert.IsType<Frame>(layer.Cels[1].Frame);
        var f2 = Assert.IsType<Frame>(layer.Cels[2].Frame);
        Assert.Equal(21, f1.Strokes[0].Points[0].Y); // t=1/3 first
        Assert.Equal(49, f2.Strokes[0].Points[0].Y); // t=2/3 second
        Assert.Contains("2", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task ARubbishAnswerInsertsNothingAndSaysWhy()
    {
        // Q32: the AI never inserts a frame it cannot defend, and a refusal
        // and a silent no-op are different outcomes — the document is
        // untouched AND the status names which t was refused and why.
        var artist = new FakeArtist
        {
            // Key A handed back as the "inbetween": well-formed, plausible,
            // and not between the keys.
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
                [new InbetweenFrameResult(0.5, [Seg(10)])]),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 1;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Doc.Scene.FrameCount); // nothing inserted
        Assert.Contains("Nothing was inserted", vm.AiStatus);
        Assert.Contains("frame 1 of 1 was refused", vm.AiStatus);
        Assert.Contains("did not stay between the keys", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task ARefusedFrameKeepsItsSlotAsAHold()
    {
        // Refusal is per frame: the ones that passed are inserted, each at its
        // own t's slot. The refused middle slot stays a hold — the surviving
        // frames must not slide onto somebody else's timing.
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
            [
                new InbetweenFrameResult(0.25, [Seg(16)]),   // eased expectation ≈16.3
                new InbetweenFrameResult(0.50, [Seg(10)]),   // key A again — refused
                new InbetweenFrameResult(0.75, [Seg(54)]),   // eased expectation ≈53.8
            ]),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 3;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Doc.Scene.FrameCount);
        var layer = vm.PaintLayer();
        Assert.Equal(16, layer.Cels[1].Frame!.Strokes[0].Points[0].Y);
        Assert.Null(layer.Cels[2].Frame); // the refused slot holds
        Assert.Equal(54, layer.Cels[3].Frame!.Strokes[0].Points[0].Y);
        Assert.Contains("Inserted 2", vm.AiStatus);
        Assert.Contains("frame 2 of 3 was refused", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task AnInsertedAiFrameCarriesItsProvenance()
    {
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
                [new InbetweenFrameResult(0.5, [Seg(35)])]),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 1;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        var frame = vm.PaintLayer().Cels[1].Frame!;
        Assert.NotNull(frame.Ai); // Q31: the frame records that AI drew it
        // Absent unless it took more than one ask, so a model that gets it
        // right first time writes exactly what it wrote before repair existed.
        Assert.Null(frame.Ai!.Attempts);
        Assert.DoesNotContain("\"attempts\"", DocJson.Serialize(vm.Doc));
    }

    /// <summary>
    /// Phase 3: a refused frame is asked again with the fault named, and the
    /// corrected drawing lands with a record of what it cost.
    /// </summary>
    [AvaloniaFact]
    public async Task ARefusedFrameIsAskedAgainAndTheFrameItProducesSaysSo()
    {
        var artist = new FakeArtist();
        // Key A handed back as the inbetween — refused — then a real one.
        artist.InbetweenAnswers.Enqueue(AiResult<List<InbetweenFrameResult>>.Success(
            [new InbetweenFrameResult(0.5, [Seg(10)])]));
        artist.InbetweenAnswers.Enqueue(AiResult<List<InbetweenFrameResult>>.Success(
            [new InbetweenFrameResult(0.5, [Seg(35)])]));
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 1;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal(2, artist.InbetweenCalls);
        var frame = vm.PaintLayer().Cels[1].Frame!;
        Assert.Equal(35, frame.Strokes[0].Points[0].Y);
        Assert.Equal(2, frame.Ai!.Attempts);
        Assert.Contains("Inserted 1", vm.AiStatus);
        Assert.Contains("needed a second ask", vm.AiStatus);

        // The re-ask carried the fault and the drawing that earned it, rather
        // than being the same request sent twice.
        var repair = Assert.Single(artist.LastInbetweenRequest!.Repair!);
        Assert.Contains("did not stay between the keys", repair.Fault);
        Assert.Equal(10, repair.Strokes[0].Points[0].Y);
    }

    /// <summary>
    /// Bounded (Q85), and the status says what the empty result cost — three
    /// calls and one call are very different bills for the same nothing.
    /// </summary>
    [AvaloniaFact]
    public async Task AModelThatKeepsFailingStopsAfterThreeAsksAndSaysHowMany()
    {
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
                [new InbetweenFrameResult(0.5, [Seg(10)])]),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 1;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal(1 + InbetweenRepair.MaxReasks, artist.InbetweenCalls);
        Assert.Equal(2, vm.Doc.Scene.FrameCount); // still nothing inserted
        Assert.Contains("Nothing was inserted after 3 attempts", vm.AiStatus);
    }

    [AvaloniaFact]
    public void AnMcpInsertedInbetweenCarriesProvenance()
    {
        // Q31's other door: an agent working the document over MCP is AI
        // touching a frame, whatever model drives it.
        var vm = VmWithTwoKeys(new FakeArtist());
        var layer = vm.PaintLayer();

        var inserted = vm.InsertExternalInbetweens(layer.Id, 0, [[Seg(35)]]);

        Assert.Equal(1, inserted);
        var frame = layer.Cels[1].Frame!;
        Assert.Equal("MCP agent", frame.Ai!.Provider);
    }

    [AvaloniaFact]
    public void AnMcpAppendMarksTheFrame_WithoutClobberingAnEarlierProvider()
    {
        var vm = VmWithTwoKeys(new FakeArtist());
        var layer = vm.PaintLayer();

        vm.AppendExternalStrokes(layer.Id, 0, [Seg(20)]);
        var frame = layer.Cels[0].Frame!;
        Assert.Equal("MCP agent", frame.Ai!.Provider);

        // A second AI touch keeps the first record: ??= is deliberate, so the
        // provider that originally drew here is not rewritten by a later edit.
        frame.Ai = new AiProvenance("Claude");
        vm.AppendExternalStrokes(layer.Id, 0, [Seg(25)]);
        Assert.Equal("Claude", frame.Ai!.Provider);
    }

    [AvaloniaFact]
    public void ADeterministicInbetweenCarriesNoProvenance()
    {
        // The other half of Q31: absent unless AI touched it. The free engine
        // is not an AI, and its frames must not claim one drew them.
        var vm = VmWithTwoKeys(new FakeArtist());

        vm.InsertInbetweensCommand.Execute(null);

        var frame = vm.PaintLayer().Cels[1].Frame!;
        Assert.NotEmpty(frame.Strokes);
        Assert.Null(frame.Ai);
    }

    [AvaloniaFact]
    public async Task AiInbetween_AChartOnTheExtremeBecomesTheRequestsTs()
    {
        // The extreme's timing chart (Q58) is the ts, and the easing sent
        // with it is Linear — the rungs are already eased by the artist.
        // This is the contract that keeps both producers of inbetweens
        // landing the same timing; a future edit that quietly reintroduces
        // TweenEasing on either side is what this test exists to catch.
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Error("stop after the request", false),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 3;   // and the chart must win over it
        vm.TweenEasing = Lightbox.Core.Inbetween.Easing.EaseInOut;
        vm.SetChartAt(new FrameCell(0) { LayerIndex = vm.ActiveLayerIndex }, [0.2, 0.9]);

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        var request = artist.LastInbetweenRequest!;
        Assert.Equal([0.2, 0.9], request.Ts);
        Assert.Equal(Lightbox.Core.Inbetween.Easing.Linear, request.Easing);
    }

    [AvaloniaFact]
    public async Task AiInbetween_WithoutAChartTheBarStillDecides()
    {
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Error("stop after the request", false),
        };
        var vm = VmWithTwoKeys(artist);
        vm.TweenCount = 2;
        vm.TweenEasing = Lightbox.Core.Inbetween.Easing.EaseIn;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        var request = artist.LastInbetweenRequest!;
        Assert.Equal(2, request.Ts.Count);
        Assert.Equal(1.0 / 3, request.Ts[0], 6);
        Assert.Equal(2.0 / 3, request.Ts[1], 6);
        Assert.Equal(Lightbox.Core.Inbetween.Easing.EaseIn, request.Easing);
    }

    [AvaloniaFact]
    public async Task AiInbetween_RefusalSurfacesMessage_NoMutation()
    {
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Refused("Claude declined this request."),
        };
        var vm = VmWithTwoKeys(artist);

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Doc.Scene.FrameCount); // nothing inserted
        Assert.Contains("declined", vm.AiStatus);
        Assert.False(vm.AiBusy);
    }

    [AvaloniaFact]
    public async Task AiInbetween_WithoutSecondKey_AsksForOne()
    {
        var artist = new FakeArtist();
        var vm = new MainViewModel(artist);
        await vm.AiInbetweenCommand.ExecuteAsync(null);
        Assert.Null(artist.LastInbetweenRequest); // no call made
        Assert.Contains("keyframe", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task AiInbetween_RefusesALockedLayer()
    {
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
                [new InbetweenFrameResult(0.5, [Dot(20, 35)])]),
        };
        var vm = VmWithTwoKeys(artist);
        vm.LayerRows[0].Locked = true;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Null(artist.LastInbetweenRequest); // never reached the artist
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        Assert.Contains("locked", vm.AiStatus);
    }

    /// <summary>
    /// The AI assists an artist; it does not draw instead of one. Every method
    /// on the artist interface must start from something the artist authored —
    /// two keyframes for an inbetween, a character sheet for a reading — and
    /// this list is the place that says so.
    /// </summary>
    /// <remarks>
    /// Written as reflection over the interface rather than as a missing
    /// button, because the button was the symptom. <c>IAiArtist</c> carried a
    /// <c>DrawAsync</c> from M2 and the prompt box followed from it; a test
    /// that only checked the view would pass on a build where the capability
    /// was one binding away from returning.
    ///
    /// Adding a name here is therefore a decision, not a formality: it is the
    /// moment to ask what the new call starts from. If the answer is "whatever
    /// somebody types", it does not belong in this application.
    /// </remarks>
    [Fact]
    public void EveryArtistMethodStartsFromSomethingTheArtistDrew()
    {
        var methods = typeof(IAiArtist).GetMethods().Select(m => m.Name).Order().ToList();

        Assert.Equal(
            [nameof(IAiArtist.GenerateInbetweensAsync), nameof(IAiArtist.ReadSubjectAsync)],
            methods);
        Assert.Null(typeof(MainViewModel).GetProperty("AiDrawCommand"));
        Assert.Null(typeof(MainViewModel).GetProperty("AiPrompt"));
    }
}
