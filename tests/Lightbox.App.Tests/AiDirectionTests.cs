using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.App.Tests;

/// <summary>
/// Q261: directions for the AI inbetweener. The button stays one click; a
/// second way in asks for a note first. Revise redraws the AI frames between
/// the extremes — or only the selected ones — with the artist's note, in one
/// undo step, and the note is kept in each frame's AI marking. The verifier
/// still decides what lands.
/// </summary>
public class AiDirectionTests
{
    /// <summary>A horizontal line at y, the shape the fixture's keys have (keys at y=10 and y=60).</summary>
    private static Stroke Seg(double y) => new()
    {
        Points = [new(10, y, 0.5), new(30, y, 0.5)],
        Brush = new BrushSettings { Size = 6, Hardness = 1 },
    };

    private static AiResult<List<InbetweenFrameResult>> Answer(params (double T, double Y)[] frames) =>
        AiResult<List<InbetweenFrameResult>>.Success(
            frames.Select(f => new InbetweenFrameResult(f.T, [Seg(f.Y)])).ToList());

    private static MainViewModel VmWithTwoKeys(FakeArtist artist, string? answer)
    {
        var vm = new MainViewModel(artist) { AskDirection = _ => Task.FromResult(answer) };
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

    /// <summary>Keys at 0 and 3, AI inbetweens at 1 and 2 (t = 1/3, 2/3).</summary>
    private static async Task<(MainViewModel Vm, FakeArtist Artist)> WithTwoAiFrames(string? note)
    {
        var artist = new FakeArtist { InbetweenResult = Answer((1.0 / 3, 21), (2.0 / 3, 49)) };
        var vm = VmWithTwoKeys(artist, note);
        vm.TweenCount = 2;
        await vm.AiInbetweenCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        Assert.NotNull(vm.PaintLayer().Cels[1].Frame!.Ai);
        return (vm, artist);
    }

    [AvaloniaFact]
    public async Task ThePlainButtonStillAsksNothing()
    {
        var asked = 0;
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, null);
        vm.AskDirection = _ => { asked++; return Task.FromResult<string?>("unused"); };
        vm.TweenCount = 1;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);
        Assert.Null(artist.LastInbetweenRequest!.Direction);
        Assert.Null(vm.PaintLayer().Cels[1].Frame!.Ai!.Note);
    }

    [AvaloniaFact]
    public async Task WithADirectionTheNoteGoesWithTheRequestAndStaysOnTheFrames()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, "Let it drop late.");
        vm.TweenCount = 1;

        await vm.AiInbetweenWithDirectionCommand.ExecuteAsync(null);

        Assert.Equal("Let it drop late.", artist.LastInbetweenRequest!.Direction);
        Assert.Equal("Let it drop late.", vm.PaintLayer().Cels[1].Frame!.Ai!.Note);
    }

    [AvaloniaFact]
    public async Task CancellingTheNoteAsksNothing()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, null);
        vm.TweenCount = 1;

        await vm.AiInbetweenWithDirectionCommand.ExecuteAsync(null);

        Assert.Equal(0, artist.InbetweenCalls);
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public async Task AnEmptyNoteIsAnOrdinaryInbetween()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, "   ");
        vm.TweenCount = 1;

        await vm.AiInbetweenWithDirectionCommand.ExecuteAsync(null);

        Assert.Null(artist.LastInbetweenRequest!.Direction);
        Assert.Null(vm.PaintLayer().Cels[1].Frame!.Ai!.Note);
    }

    [AvaloniaFact]
    public async Task ReviseRedrawsTheAiFramesBetweenTheKeysInPlace()
    {
        var (vm, artist) = await WithTwoAiFrames("More ease into B.");
        artist.InbetweenResult = Answer((1.0 / 3, 25), (2.0 / 3, 52));
        vm.CurrentFrameIndex = 1;

        await vm.AiReviseCommand.ExecuteAsync(null);

        var request = artist.LastInbetweenRequest!;
        Assert.Equal("More ease into B.", request.Direction);
        Assert.Equal([1.0 / 3, 2.0 / 3], request.Ts);
        Assert.Equal(2, request.Earlier!.Count);             // its own two drawings
        Assert.Equal(10, request.KeyframeA[0].Points[0].Y);   // the keys, not an AI frame
        Assert.Equal(60, request.KeyframeB[0].Points[0].Y);

        var layer = vm.PaintLayer();
        Assert.Equal(4, vm.Doc.Scene.FrameCount);             // replaced, not inserted
        Assert.Equal(25, layer.Cels[1].Frame!.Strokes[0].Points[0].Y);
        Assert.Equal(52, layer.Cels[2].Frame!.Strokes[0].Points[0].Y);
        Assert.Equal("More ease into B.", layer.Cels[2].Frame!.Ai!.Note);
        Assert.Equal(FrameRole.Inbetween, layer.Cels[1].Frame!.Role);
    }

    [AvaloniaFact]
    public async Task ReviseIsOneUndoStep()
    {
        var (vm, artist) = await WithTwoAiFrames("More ease into B.");
        artist.InbetweenResult = Answer((1.0 / 3, 25), (2.0 / 3, 52));
        vm.CurrentFrameIndex = 1;
        await vm.AiReviseCommand.ExecuteAsync(null);

        vm.UndoCommand.Execute(null);

        var layer = vm.PaintLayer();
        Assert.Equal(21, layer.Cels[1].Frame!.Strokes[0].Points[0].Y);
        Assert.Equal(49, layer.Cels[2].Frame!.Strokes[0].Points[0].Y);
    }

    [AvaloniaFact]
    public async Task SelectedFramesAreTheOnlyOnesRedrawnAndTheRestGoAlongAsContext()
    {
        var (vm, artist) = await WithTwoAiFrames("Tighter arc.");
        artist.InbetweenResult = Answer((2.0 / 3, 52));
        vm.CurrentFrameIndex = 2;
        vm.ToggleCelSelection(vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex).Cells.First(c => c.Index == 2));

        await vm.AiReviseCommand.ExecuteAsync(null);

        var request = artist.LastInbetweenRequest!;
        Assert.Equal([2.0 / 3], request.Ts);
        Assert.Single(request.Earlier!);
        Assert.Single(request.Fixed!);
        var layer = vm.PaintLayer();
        Assert.Equal(21, layer.Cels[1].Frame!.Strokes[0].Points[0].Y); // untouched
        Assert.Equal(52, layer.Cels[2].Frame!.Strokes[0].Points[0].Y);
    }

    [AvaloniaFact]
    public async Task ARefusedRevisionKeepsTheFrameItWasRevising()
    {
        var (vm, artist) = await WithTwoAiFrames("Swing past B.");
        // Far past key B (y=60): outside the keys, so refused however often
        // it is asked — the note asked for an overshoot, and a note cannot buy
        // one.
        artist.InbetweenResult = Answer((1.0 / 3, 160), (2.0 / 3, 170));
        vm.CurrentFrameIndex = 1;

        await vm.AiReviseCommand.ExecuteAsync(null);

        var layer = vm.PaintLayer();
        Assert.Equal(21, layer.Cels[1].Frame!.Strokes[0].Points[0].Y);
        Assert.Equal(49, layer.Cels[2].Frame!.Strokes[0].Points[0].Y);
        Assert.Contains("refused", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task ReviseWithNoAiFramesSaysSoAndAsksNothing()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, "Anything.");

        await vm.AiReviseCommand.ExecuteAsync(null);

        Assert.Equal(0, artist.InbetweenCalls);
        Assert.Contains("no AI frames", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task ReviseNeedsANote()
    {
        var (vm, artist) = await WithTwoAiFrames("   ");
        var calls = artist.InbetweenCalls;
        vm.CurrentFrameIndex = 1;

        await vm.AiReviseCommand.ExecuteAsync(null);

        Assert.Equal(calls, artist.InbetweenCalls);
    }

    [AvaloniaFact]
    public async Task FramesARevisionDoesNotRedrawKeepEverythingTheyHad()
    {
        // The sensitivity review's blocker: writing the whole span back through
        // a clone dropped each untouched frame's AI marking, anchors and the
        // rest. A frame nobody asked to change must come through identical —
        // the same object, so nothing can have been copied away.
        var (vm, artist) = await WithTwoAiFrames("Tighter arc.");
        var layer = vm.PaintLayer();
        var staying = layer.Cels[1].Frame!;
        staying.Anchors = new() { ["hand"] = new AnchorPoint(12, 21) };
        artist.InbetweenResult = Answer((2.0 / 3, 52));
        vm.CurrentFrameIndex = 2;
        vm.ToggleCelSelection(vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex).Cells.First(c => c.Index == 2));

        await vm.AiReviseCommand.ExecuteAsync(null);

        Assert.Same(staying, vm.PaintLayer().Cels[1].Frame);
        Assert.NotNull(staying.Ai);
        Assert.NotNull(staying.Anchors);
    }

    [AvaloniaFact]
    public async Task AFrameWhoseRedrawWasRefusedKeepsItsMarking()
    {
        var (vm, artist) = await WithTwoAiFrames("Swing past B.");
        var layer = vm.PaintLayer();
        var before = layer.Cels[1].Frame!;
        artist.InbetweenResult = Answer((1.0 / 3, 160), (2.0 / 3, 170));
        vm.CurrentFrameIndex = 1;

        await vm.AiReviseCommand.ExecuteAsync(null);

        Assert.Same(before, vm.PaintLayer().Cels[1].Frame);
        Assert.NotNull(before.Ai);
        Assert.True(artist.InbetweenCalls > 2, "the repair loop ran before giving up");
        Assert.Contains("frame 2", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task OnlyTheNeighboursOfARedrawnFrameGoAlongAsContext()
    {
        // Revising one frame of many must not send every other one: smoothness
        // needs the frames either side, and each extra frame is the expensive
        // kind of payload.
        var artist = new FakeArtist
        {
            InbetweenResult = Answer((0.2, 20), (0.4, 30), (0.6, 40), (0.8, 50)),
        };
        var vm = VmWithTwoKeys(artist, "Hold the hand.");
        vm.TweenEasing = Easing.Linear;
        vm.TweenCount = 4;
        await vm.AiInbetweenCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.Doc.Scene.FrameCount);
        artist.InbetweenResult = Answer((0.4, 31));
        vm.CurrentFrameIndex = 2;
        vm.ToggleCelSelection(vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex).Cells.First(c => c.Index == 2));

        await vm.AiReviseCommand.ExecuteAsync(null);

        var request = artist.LastInbetweenRequest!;
        Assert.Equal([0.4], request.Ts);
        Assert.Equal([0.2, 0.6], request.Fixed!.Select(f => f.T).ToList());
    }

    [AvaloniaFact]
    public async Task WithThePlayheadOnTheLastDrawingItSaysWhatIsMissing()
    {
        var (vm, artist) = await WithTwoAiFrames("Anything.");
        var calls = artist.InbetweenCalls;
        vm.CurrentFrameIndex = 3;

        await vm.AiReviseCommand.ExecuteAsync(null);

        Assert.Equal(calls, artist.InbetweenCalls);
        Assert.Contains("between", vm.AiStatus);
        Assert.DoesNotContain("no AI frames", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task ATimelineChangedDuringTheRequestIsNotWrittenOver()
    {
        // The span is worked out before the request goes; if the artist inserts
        // or deletes a frame while it is out, writing back by index would land
        // on the wrong drawings — possibly over a key.
        var (vm, artist) = await WithTwoAiFrames("Tighter.");
        vm.CurrentFrameIndex = 1;
        artist.InbetweenResult = Answer((1.0 / 3, 25), (2.0 / 3, 52));
        artist.OnRequest = () =>
        {
            artist.OnRequest = null;
            vm.CurrentFrameIndex = 0;
            vm.AddFrameCommand.Execute(null);
        };

        await vm.AiReviseCommand.ExecuteAsync(null);

        Assert.Contains("changed", vm.AiStatus);
        Assert.DoesNotContain(vm.PaintLayer().Cels, c => c.Frame?.Strokes is [{ Points: [{ Y: 25 }, ..] }]);
    }

    [AvaloniaFact]
    public async Task ALongNoteIsCutToAReasonableLength()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, new string('x', 5000));
        vm.TweenCount = 1;

        await vm.AiInbetweenWithDirectionCommand.ExecuteAsync(null);

        Assert.Equal(MainViewModel.MaxDirectionLength, artist.LastInbetweenRequest!.Direction!.Length);
    }

    [Fact]
    public void BothWaysInAreInTheShortcutRegistry()
    {
        // A command that is not in the map cannot be found, searched or rebound.
        var map = new ShortcutMap();
        Assert.Contains(map.Definitions, d => d.Id == "ai.inbetweenWithDirection");
        Assert.Contains(map.Definitions, d => d.Id == "ai.revise");
    }
}
