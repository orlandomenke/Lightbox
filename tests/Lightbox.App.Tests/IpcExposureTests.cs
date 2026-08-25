using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The exposure sheet, over MCP: an agent can author timing and not merely
/// read it.
/// </summary>
/// <remarks>
/// <para>
/// <c>get_scene</c> has reported <c>keyedFrames</c> since the surface existed
/// and nothing could make one, so an agent could draw on a frame and could not
/// time anything — on an application whose unit of work is a sequence, that is
/// the half that matters. These four ops close it.
/// </para>
/// <para>
/// What is asserted here is mostly <i>refusal</i>, because the interesting
/// failures are all quiet ones: a timing op that silently does nothing, a role
/// typo that lands a key, an agent claiming authorship of the artist's drawing.
/// An agent cannot see the timeline, so a success that changed nothing is worse
/// for it than an error.
/// </para>
/// </remarks>
public class IpcExposureTests(ITestOutputHelper output)
{
    private static MainViewModel VmWithDrawing()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(120, 90, 0.7);
        vm.EndStroke();
        return vm;
    }

    private static IpcProtocol.Request Req(string op, object? payload = null) => new()
    {
        Op = op,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    [AvaloniaFact]
    public void SetKey_MakesADrawingOnAHoldAndIsOneUndoStep()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        // A real hold, not AddFrame — that inserts a cel with an empty drawing
        // in it, which is a key. Only extend_exposure makes a bare cel.
        Assert.True(api.Handle(Req("extend_exposure", new { frameIndex = 0 })).Ok);
        var layer = vm.PaintLayer();
        Assert.Null(layer.Cels[1].Frame);

        var resp = api.Handle(Req("set_key", new { frameIndex = 1 }));

        Assert.True(resp.Ok);
        Assert.True(resp.Payload!.Value.GetProperty("created").GetBoolean());
        Assert.NotNull(layer.Cels[1].Frame);
        Assert.Equal(FrameRole.Key, layer.Cels[1].Frame!.Role);

        // One step, not two — the provenance stamp rides inside the same edit.
        vm.UndoCommand.Execute(null);
        Assert.Null(vm.PaintLayer().Cels[1].Frame);
    }

    [AvaloniaFact]
    public void ACreatedKeyIsTheAgentsAndAReMarkedOneStaysTheArtists()
    {
        // Q31's line, at its narrowest. A frame the agent brought into
        // existence is the agent's; a frame it only re-labelled is a timing
        // edit on somebody else's drawing, and claiming it would put an ai key
        // into a document whose art nobody generated.
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        Assert.True(api.Handle(Req("extend_exposure", new { frameIndex = 0 })).Ok);

        Assert.True(api.Handle(Req("set_key", new { frameIndex = 1 })).Ok);
        var made = vm.PaintLayer().Cels[1].Frame!;
        Assert.NotNull(made.Ai);
        Assert.Equal("MCP agent", made.Ai!.Provider);

        // Frame 0 is the artist's stroke from the fixture.
        var artists = vm.PaintLayer().Cels[0].Frame!;
        Assert.Null(artists.Ai);
        Assert.True(api.Handle(Req("set_key", new { frameIndex = 0, role = "breakdown" })).Ok);
        Assert.Equal(FrameRole.Breakdown, artists.Role);
        Assert.Null(artists.Ai);

        output.WriteLine($"created frame ai={made.Ai?.Provider}, re-marked ai={artists.Ai?.Provider ?? "(absent)"}");
    }

    [AvaloniaFact]
    public void SetKey_PastTheEndGrowsTheTimeline()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        var before = vm.Doc.Scene.FrameCount;

        var resp = api.Handle(Req("set_key", new { frameIndex = before + 3 }));

        Assert.True(resp.Ok);
        Assert.Equal(before + 4, vm.Doc.Scene.FrameCount);
        Assert.Equal(before + 4, resp.Payload!.Value.GetProperty("frameCount").GetInt32());
        Assert.NotNull(vm.PaintLayer().Cels[before + 3].Frame);
    }

    [AvaloniaFact]
    public void AnUnknownRoleFailsRatherThanQuietlyMakingAKey()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        Assert.True(api.Handle(Req("extend_exposure", new { frameIndex = 0 })).Ok);

        var resp = api.Handle(Req("set_key", new { frameIndex = 1, role = "breakdwn" }));

        Assert.False(resp.Ok);
        Assert.Contains("breakdwn", resp.Error);
        Assert.Null(vm.PaintLayer().Cels[1].Frame);   // and nothing happened
    }

    [AvaloniaFact]
    public void ExtendExposure_HoldsTheDrawingOneFrameLonger()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        var cels = vm.PaintLayer().Cels.Count;

        var resp = api.Handle(Req("extend_exposure", new { frameIndex = 0 }));

        Assert.True(resp.Ok);
        Assert.Equal(cels + 1, vm.PaintLayer().Cels.Count);
        Assert.Null(vm.PaintLayer().Cels[1].Frame);          // the new hold
        Assert.NotNull(vm.PaintLayer().Cels[0].Frame);        // drawing kept

        vm.UndoCommand.Execute(null);
        Assert.Equal(cels, vm.PaintLayer().Cels.Count);
    }

    [AvaloniaFact]
    public void ReduceExposure_RefusesRatherThanSilentlyDoingNothing()
    {
        // The editor's own rule is that a drawing is never removed, so on an
        // unheld frame there is nothing to do. Reporting that as success would
        // let an agent retiming a run believe it had shortened something.
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);

        var refused = api.Handle(Req("reduce_exposure", new { frameIndex = 0 }));
        Assert.False(refused.Ok);
        Assert.Contains("not held", refused.Error);

        // Give frame 0 a hold, and the same call now works.
        Assert.True(api.Handle(Req("extend_exposure", new { frameIndex = 0 })).Ok);
        var cels = vm.PaintLayer().Cels.Count;
        Assert.True(api.Handle(Req("reduce_exposure", new { frameIndex = 0 })).Ok);
        Assert.Equal(cels - 1, vm.PaintLayer().Cels.Count);
    }

    [AvaloniaFact]
    public void SetExposureStep_PutsARangeOnTwosAndStaysThereWhenRepeated()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        // Three drawings on consecutive frames.
        Assert.True(api.Handle(Req("set_key", new { frameIndex = 1 })).Ok);
        Assert.True(api.Handle(Req("set_key", new { frameIndex = 2 })).Ok);
        var drawings = vm.PaintLayer().Cels.Count(c => c.Frame is not null);

        var first = api.Handle(Req("set_exposure_step", new { from = 0, to = 2, step = 2 }));
        Assert.True(first.Ok);
        Assert.Equal(3, first.Payload!.Value.GetProperty("grew").GetInt32());
        Assert.Equal(drawings, vm.PaintLayer().Cels.Count(c => c.Frame is not null));
        Assert.Null(vm.PaintLayer().Cels[1].Frame);   // hold behind drawing 0

        // Absorbed, not multiplied: on 2s twice is still on 2s.
        var again = api.Handle(Req("set_exposure_step", new { from = 0, to = 5, step = 2 }));
        Assert.True(again.Ok);
        Assert.Equal(0, again.Payload!.Value.GetProperty("grew").GetInt32());
        Assert.Equal(drawings, vm.PaintLayer().Cels.Count(c => c.Frame is not null));

        output.WriteLine($"{drawings} drawings, grew {first.Payload!.Value.GetProperty("grew").GetInt32()} "
                       + $"then {again.Payload!.Value.GetProperty("grew").GetInt32()}");
    }

    [AvaloniaFact]
    public void NoTimingOpEverRemovesADrawing()
    {
        // The boundary this branch deliberately stops at: every op here adds or
        // re-times, none discards. `ReduceToStep` is the destructive one and is
        // not exposed, because a destructive agent op wants the explicit-flag
        // treatment `import_character` has.
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        Assert.True(api.Handle(Req("set_key", new { frameIndex = 1 })).Ok);
        Assert.True(api.Handle(Req("set_key", new { frameIndex = 2 })).Ok);
        var drawings = vm.PaintLayer().Cels.Count(c => c.Frame is not null);

        foreach (var call in new[]
        {
            Req("extend_exposure", new { frameIndex = 0 }),
            Req("reduce_exposure", new { frameIndex = 0 }),
            Req("set_exposure_step", new { from = 0, to = 4, step = 3 }),
            Req("set_key", new { frameIndex = 0, role = "inbetween" }),
        })
        {
            api.Handle(call);
            Assert.Equal(drawings, vm.PaintLayer().Cels.Count(c => c.Frame is not null));
        }
    }

    [AvaloniaFact]
    public void ALockedLayerRefusesEveryTimingOp()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        var layer = vm.PaintLayer();
        Assert.True(api.Handle(Req("extend_exposure", new { frameIndex = 0 })).Ok);
        layer.Locked = true;

        foreach (var call in new[]
        {
            Req("set_key", new { frameIndex = 6 }),
            Req("extend_exposure", new { frameIndex = 0 }),
            Req("reduce_exposure", new { frameIndex = 0 }),
            Req("set_exposure_step", new { from = 0, to = 1, step = 2 }),
        })
        {
            var resp = api.Handle(call);
            Assert.False(resp.Ok);
            Assert.Contains("cannot be edited", resp.Error);
        }
    }

    [AvaloniaFact]
    public void BadTimingRequestsFailCleanly()
    {
        var api = new IpcDocumentApi(VmWithDrawing());
        Assert.False(api.Handle(Req("set_key", new { frameIndex = -1 })).Ok);
        Assert.False(api.Handle(Req("extend_exposure", new { frameIndex = -1 })).Ok);
        Assert.False(api.Handle(Req("set_exposure_step", new { from = 0, to = 1, step = 0 })).Ok);
        Assert.False(api.Handle(Req("set_exposure_step", new { from = -1, to = 1, step = 2 })).Ok);
        Assert.False(api.Handle(Req("set_key", new { frameIndex = 0, layerId = "bogus" })).Ok);
    }
}
