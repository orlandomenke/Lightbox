using System.IO.Pipes;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class IpcTests
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

    // ---- direct API tests (logic) -------------------------------------------

    [AvaloniaFact]
    public void GetScene_ReportsLayersAndKeys()
    {
        var api = new IpcDocumentApi(VmWithDrawing());
        var resp = api.Handle(Req("get_scene"));
        Assert.True(resp.Ok);
        var json = resp.Payload!.Value;
        Assert.Equal(960, json.GetProperty("width").GetInt32());
        var layer = json.GetProperty("layers")[0];
        Assert.Equal("painted", layer.GetProperty("kind").GetString());
        Assert.Equal(0, layer.GetProperty("keyedFrames")[0].GetInt32());
    }

    [AvaloniaFact]
    public void GetScene_ReportsCarveAndEffectState()
    {
        // The four booleans an agent orients by (G12): false on an ordinary
        // layer, and each one flips with the layer state it names — otherwise
        // the surface reports a document that is not the one rendering.
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        var layer = vm.Doc.Scene.Layers[0];

        var before = api.Handle(Req("get_scene")).Payload!.Value.GetProperty("layers")[0];
        Assert.False(before.GetProperty("hasMask").GetBoolean());
        Assert.False(before.GetProperty("clipped").GetBoolean());
        Assert.False(before.GetProperty("isAdjustment").GetBoolean());
        Assert.False(before.GetProperty("hasEffects").GetBoolean());

        layer.Mask = new Lightbox.Core.Documents.LayerMask();
        layer.ClipToBelow = true;
        layer.Adjusts = true;
        layer.Effects = new Lightbox.Core.Effects.EffectStack
        {
            Uses = [new Lightbox.Core.Effects.EffectUse { Kind = "grade.hsl" }],
        };

        var after = api.Handle(Req("get_scene")).Payload!.Value.GetProperty("layers")[0];
        Assert.True(after.GetProperty("hasMask").GetBoolean());
        Assert.True(after.GetProperty("clipped").GetBoolean());
        Assert.True(after.GetProperty("isAdjustment").GetBoolean());
        Assert.True(after.GetProperty("hasEffects").GetBoolean());
    }

    [AvaloniaFact]
    public void GetFrameStrokes_ReturnsWireFormat()
    {
        var api = new IpcDocumentApi(VmWithDrawing());
        var resp = api.Handle(Req("get_frame_strokes", new { frameIndex = 0 }));
        Assert.True(resp.Ok);
        var strokes = resp.Payload!.Value.GetProperty("strokes");
        Assert.Equal(1, strokes.GetArrayLength());
        Assert.True(strokes[0].TryGetProperty("points", out var pts));
        Assert.True(pts.GetArrayLength() >= 2);
    }

    /// <summary>
    /// The reply is capped on the long edge, and the drawing survives it.
    /// </summary>
    /// <remarks>
    /// <b>This assertion used to read 960 — the scene's authored width — and
    /// changing it is the point of the change, not a concession to it.</b> A
    /// 960-wide frame costs an agent ~1,100 image tokens where 768 costs ~442,
    /// on every look, and a tool whose own description says it is for *seeing* a
    /// drawing does not need the pixels. The sibling <c>render_reference_view</c>
    /// stays uncapped for the opposite reason (B31): an agent asking for a view
    /// should get the view. Two defaults, two reasons, and
    /// <see cref="RenderFrame_AtLongEdgeZero_StillGivesTheAuthoredCanvas"/> is
    /// the way back to the old behaviour.
    /// </remarks>
    [AvaloniaFact]
    public void RenderFrame_IsCappedOnTheLongEdge()
    {
        var api = new IpcDocumentApi(VmWithDrawing());
        var resp = api.Handle(Req("render_frame", new { frameIndex = 0 }));
        Assert.True(resp.Ok);
        var b64 = resp.Payload!.Value.GetProperty("pngBase64").GetString()!;
        using var bmp = SKBitmap.Decode(Convert.FromBase64String(b64));
        Assert.Equal(768, bmp.Width);
        // The stroke runs (10,10)→(120,90); its midpoint lands at 0.8 scale.
        Assert.NotEqual(SKColors.White, bmp.GetPixel(52, 40));
    }

    [AvaloniaFact]
    public void RenderFrame_AtLongEdgeZero_StillGivesTheAuthoredCanvas()
    {
        var api = new IpcDocumentApi(VmWithDrawing());
        var resp = api.Handle(Req("render_frame", new { frameIndex = 0, longEdge = 0 }));
        Assert.True(resp.Ok);
        var b64 = resp.Payload!.Value.GetProperty("pngBase64").GetString()!;
        using var bmp = SKBitmap.Decode(Convert.FromBase64String(b64));
        Assert.Equal(960, bmp.Width);
        Assert.NotEqual(SKColors.White, bmp.GetPixel(65, 50)); // stroke midpoint shows
    }

    /// <summary>
    /// A cap is a ceiling, not a target: a canvas already inside it is not
    /// upscaled to meet it.
    /// </summary>
    [AvaloniaFact]
    public void RenderFrame_DoesNotUpscaleASmallCanvasToTheCap()
    {
        var vm = VmWithDrawing();
        vm.Doc.Scene.Width = 400;
        vm.Doc.Scene.Height = 300;
        var api = new IpcDocumentApi(vm);
        var resp = api.Handle(Req("render_frame", new { frameIndex = 0 }));
        Assert.True(resp.Ok);
        using var bmp = SKBitmap.Decode(
            Convert.FromBase64String(resp.Payload!.Value.GetProperty("pngBase64").GetString()!));
        Assert.Equal(400, bmp.Width);
    }

    /// <summary>
    /// The in-app render is untouched by the MCP cap — the cap lives at the one
    /// call site where "this leaves the machine" is true, which is the shape B31
    /// arrived at for reference views.
    /// </summary>
    [AvaloniaFact]
    public void TheViewModelStillRendersAFrameAtItsAuthoredSize()
    {
        using var bmp = SKBitmap.Decode(Convert.FromBase64String(VmWithDrawing().RenderFramePng(0)));
        Assert.Equal(960, bmp.Width);
    }

    [AvaloniaFact]
    public void InsertInbetweens_ValidatesAndInserts_Undoable()
    {
        var vm = VmWithDrawing();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(10, 100, 0.5);
        vm.MoveStroke(120, 180, 0.7);
        vm.EndStroke();
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("insert_inbetweens", new
        {
            aIndex = 0,
            frames = new object[]
            {
                new
                {
                    t = 0.5,
                    strokes = new object[]
                    {
                        new
                        {
                            tool = "brush", color = "#000000", size = 6.0,
                            hardness = 0.8, opacity = 1.0, label = "line",
                            points = new object[] { new { x = 10.0, y = 55.0, pressure = 0.5 } },
                        },
                    },
                },
                new { t = 1.5, strokes = Array.Empty<object>() }, // invalid t → dropped
            },
        }));

        Assert.True(resp.Ok);
        Assert.Equal(1, resp.Payload!.Value.GetProperty("inserted").GetInt32());
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        var tween = Assert.IsType<Frame>(vm.PaintLayer().Cels[1].Frame);
        Assert.Equal("line", tween.Strokes[0].Label);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void DrawStrokes_AppendsToExposedKey()
    {
        var vm = VmWithDrawing();
        var api = new IpcDocumentApi(vm);
        var resp = api.Handle(Req("draw_strokes", new
        {
            frameIndex = 0,
            strokes = new object[]
            {
                new
                {
                    tool = "brush", color = "#ff0000", size = 4.0,
                    hardness = 1.0, opacity = 1.0, label = (string?)null,
                    points = new object[] { new { x = 5.0, y = 5.0, pressure = 1.0 } },
                },
            },
        }));
        Assert.True(resp.Ok);
        var frame = vm.PaintedCel();
        Assert.Equal(2, frame.Strokes.Count);
        Assert.Equal("#ff0000", frame.Strokes[1].Color);
    }

    [AvaloniaFact]
    public void BadRequests_FailCleanly()
    {
        var api = new IpcDocumentApi(VmWithDrawing());
        Assert.False(api.Handle(Req("nope")).Ok);
        Assert.False(api.Handle(Req("render_frame", new { frameIndex = 99 })).Ok);
        Assert.False(api.Handle(Req("get_frame_strokes", new { frameIndex = 0, layerId = "bogus" })).Ok);
        Assert.False(api.Handle(Req("insert_inbetweens", new { aIndex = 0, frames = Array.Empty<object>() })).Ok);
        Assert.False(api.Handle(Req("draw_strokes")).Ok); // missing payload
    }

    // ---- full pipe round-trip ------------------------------------------------

    [AvaloniaFact]
    public async Task PipeRoundTrip_GetScene()
    {
        var vm = VmWithDrawing();
        var pipeName = $"lightbox-test-{Guid.NewGuid():N}";
        await using var server = new IpcServer(new IpcDocumentApi(vm), pipeName);

        // Client side runs off-thread (like Lightbox.Mcp would).
        var responseLine = await Task.Run(async () =>
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("""{"op":"get_scene"}""");
            return await reader.ReadLineAsync();
        });

        Assert.NotNull(responseLine);
        using var doc = JsonDocument.Parse(responseLine!);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(960, doc.RootElement.GetProperty("payload").GetProperty("width").GetInt32());
    }
}
