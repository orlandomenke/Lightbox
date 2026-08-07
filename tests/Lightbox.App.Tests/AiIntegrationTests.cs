using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>Scripted fake artist — no network.</summary>
internal sealed class FakeArtist : IAiArtist
{
    public AiResult<List<InbetweenFrameResult>>? InbetweenResult { get; set; }
    public InbetweenRequest? LastInbetweenRequest { get; private set; }

    public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        LastInbetweenRequest = request;
        return Task.FromResult(InbetweenResult
            ?? AiResult<List<InbetweenFrameResult>>.Error("unscripted", false));
    }
}

public class AiIntegrationTests
{
    private static Stroke Dot(double x, double y) => new()
    {
        Points = [new(x, y, 0.5)],
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
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success(
            [
                new InbetweenFrameResult(0.5, [Dot(20, 35)]),
                new InbetweenFrameResult(0.25, [Dot(15, 22)]),
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
        var f1 = Assert.IsType<PaintedFrame>(layer.Cels[1].Frame);
        var f2 = Assert.IsType<PaintedFrame>(layer.Cels[2].Frame);
        Assert.Equal(15, f1.Strokes[0].Points[0].X); // t=0.25 first
        Assert.Equal(20, f2.Strokes[0].Points[0].X); // t=0.5 second
        Assert.Contains("2", vm.AiStatus);
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
    /// The AI assists an artist; it does not draw instead of one, so there is
    /// no way to ask a model for a drawing from nothing. Everything it produces
    /// starts from something the artist authored — here, two keyframes.
    /// </summary>
    /// <remarks>
    /// Written as reflection over the interface rather than as a missing
    /// button, because the button was the symptom. <c>IAiArtist</c> carried a
    /// <c>DrawAsync</c> from M2 and the prompt box followed from it; a test
    /// that only checked the view would pass on a build where the capability
    /// was one binding away from returning.
    /// </remarks>
    [Fact]
    public void TheArtistInterfaceOffersInbetweeningAndNothingElse()
    {
        var methods = typeof(IAiArtist).GetMethods().Select(m => m.Name).ToList();

        Assert.Equal([nameof(IAiArtist.GenerateInbetweensAsync)], methods);
        Assert.Null(typeof(MainViewModel).GetProperty("AiDrawCommand"));
        Assert.Null(typeof(MainViewModel).GetProperty("AiPrompt"));
    }
}
