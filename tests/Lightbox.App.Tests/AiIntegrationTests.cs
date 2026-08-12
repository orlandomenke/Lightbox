using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>Scripted fake artist — no network.</summary>
internal sealed class FakeArtist : IAiArtist
{
    public AiResult<List<InbetweenFrameResult>>? InbetweenResult { get; set; }
    public AiResult<SubjectTaxonomy>? SubjectResult { get; set; }
    public InbetweenRequest? LastInbetweenRequest { get; private set; }
    public SubjectRequest? LastSubjectRequest { get; private set; }

    public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        LastInbetweenRequest = request;
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
