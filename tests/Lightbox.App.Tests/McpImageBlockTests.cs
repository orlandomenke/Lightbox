using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B192: the seam between the app's <c>render_frame</c> payload and the image
/// block the MCP client is handed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IpcTests.RenderFrame_ReturnsDecodablePng"/> already proves the app
/// half — the payload's <c>pngBase64</c> is a real, decodable PNG — and it kept
/// proving it for as long as the tool was broken, because the defect was one
/// assignment further along. So these tests deliberately do not stop where that
/// one does: each takes the payload the app really produced, wraps it the way
/// the tool really does, <b>serializes the block</b>, and decodes the string a
/// client would read. Anything short of serializing hides the bug, since
/// <c>ImageContentBlock.Data</c> holds bytes either way and only the writer
/// reveals which bytes were meant.
/// </para>
/// <para>
/// Every test here fails on the pre-fix code with a
/// <see cref="FormatException"/> out of <c>Convert.FromBase64String</c> — which
/// was the reported symptom, arriving from the client's side of the wire.
/// </para>
/// </remarks>
public class McpImageBlockTests
{
    private static IpcProtocol.Request Req(string op, object? payload = null) => new()
    {
        Op = op,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    private static JsonElement Answer(MainViewModel vm, string op, object? payload = null)
    {
        var response = new IpcDocumentApi(vm).Handle(Req(op, payload));
        Assert.True(response.Ok, response.Error);
        return response.Payload!.Value;
    }

    /// <summary>
    /// The block as it leaves the server: the JSON string in <c>data</c>, and
    /// the bytes that string base64-decodes to.
    /// </summary>
    private static (string Data, byte[] Png) OnTheWire(ImageContentBlock block)
    {
        var json = JsonSerializer.Serialize<ContentBlock>(block, McpJsonUtilities.DefaultOptions);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data").GetString()!;
        return (data, Convert.FromBase64String(data));
    }

    [AvaloniaFact]
    public void TheRenderedFrameTravelsAsBase64TextNotAsRawPngBytes()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(120, 90, 0.7);
        vm.EndStroke();

        var payload = Answer(vm, "render_frame", new { frameIndex = 0 });
        var expected = payload.GetProperty("pngBase64").GetString()!;

        var block = LightboxTools.ImageBlock(payload);

        // The SDK's contract, and the sentence the fix turns on: Data is the
        // UTF-8 bytes of the base64 *text*. Handing it the decoded image put
        // 0x89 'P' 'N' 'G' into a JSON string and lost it to U+FFFD.
        Assert.Equal(expected, Encoding.UTF8.GetString(block.Data.Span));

        var (data, png) = OnTheWire(block);
        Assert.Equal(expected, data);

        using var bitmap = SKBitmap.Decode(png);
        // Derived from the cap rather than written out, because the size is
        // incidental to what this test guards: the seam is base64 text against
        // raw bytes, and it would fail identically at any dimensions. Asserting
        // the literal 960 here coupled a B192 regression test to a size contract
        // it has nothing to do with, and duly broke when the cap landed.
        Assert.Equal(IpcDocumentApi.RenderedFrameLongEdge, bitmap.Width);
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(52, 40)); // the stroke survived the trip
    }

    /// <summary>
    /// The reported repro, and the one that says what kind of bug this was: a
    /// document nobody has drawn on fails exactly as a full one does. Nothing
    /// in the path depended on the pixels — a PNG's first byte is 0x89, which
    /// is not valid UTF-8 whatever follows it — so neither canvas size nor
    /// content ever had anything to do with it.
    /// </summary>
    [AvaloniaFact]
    public void ABlankFrameMakesTheSameTripAndIsStillAPng()
    {
        var vm = new MainViewModel(null);

        var payload = Answer(vm, "render_frame", new { frameIndex = 0 });
        var (_, png) = OnTheWire(LightboxTools.ImageBlock(payload));

        using var bitmap = SKBitmap.Decode(png);
        // The cap scales, it does not crop: the frame keeps its aspect.
        var scale = IpcDocumentApi.RenderedFrameLongEdge / (double)vm.Doc.Scene.Width;
        Assert.Equal(IpcDocumentApi.RenderedFrameLongEdge, bitmap.Width);
        Assert.Equal((int)Math.Round(vm.Doc.Scene.Height * scale), bitmap.Height);
    }

    /// <summary>
    /// A capped render says so, and a full-size one says nothing.
    /// </summary>
    /// <remarks>
    /// <b>The condition under which Q178's cap was allowed to stand.</b>
    /// G12's art-director measured that 768 keeps a frame's pose and loses 84%
    /// of the fine dark pixels on a 1080p face — eyebrows and eyes go entirely
    /// at 4K — so an agent checking its own inbetween would see a browless head
    /// whether it drew one or not. The cap was kept and the silence was not: the
    /// reply now says what fraction of the canvas the picture is. The absent
    /// half matters as much as the present one, because a note on every render
    /// is a note nobody reads by the time it counts.
    /// </remarks>
    [AvaloniaFact]
    public void ACappedRenderSaysHowMuchOfTheCanvasYouAreSeeing()
    {
        var vm = new MainViewModel(null);

        var capped = LightboxTools.RenderFrame_ForTests(Answer(vm, "render_frame", new { frameIndex = 0 }));
        var note = Assert.IsType<TextContentBlock>(Assert.Single(capped.OfType<TextContentBlock>()));
        Assert.Contains("768", note.Text);
        Assert.Contains($"{vm.Doc.Scene.Width}×{vm.Doc.Scene.Height}", note.Text);
        Assert.Contains("longEdge 0", note.Text);
        Assert.Single(capped.OfType<ImageContentBlock>());

        var full = LightboxTools.RenderFrame_ForTests(
            Answer(vm, "render_frame", new { frameIndex = 0, longEdge = 0 }));
        Assert.Empty(full.OfType<TextContentBlock>());
        Assert.Single(full.OfType<ImageContentBlock>());
    }

    /// <summary>
    /// <c>render_reference_view</c> had the identical line and the identical
    /// bug; both tools now share one conversion, and this is what keeps them
    /// sharing it.
    /// </summary>
    [AvaloniaFact]
    public void TheReferenceViewImageGoesThroughTheSameConversion()
    {
        var vm = VmLayers.PaperVm();
        vm.AddReferenceSheet();
        vm.AddReferenceView(vm.ReferenceSheetsView[0]);
        vm.ActiveTab = vm.Tabs[0];

        var views = Answer(vm, "list_reference_views");
        var viewId = views.GetProperty("sheets")[0].GetProperty("views")[0].GetProperty("id").GetString();

        var payload = Answer(vm, "render_reference_view", new { viewId });
        var expected = payload.GetProperty("pngBase64").GetString()!;

        var (data, png) = OnTheWire(LightboxTools.ImageBlock(payload));
        Assert.Equal(expected, data);
        Assert.NotNull(SKBitmap.Decode(png));
    }

    /// <summary>
    /// The old code got its validation by accident — it decoded the string, so
    /// a bad one threw. Passing the text through means saying so on purpose,
    /// and saying it as a message the model can act on rather than as a
    /// <see cref="FormatException"/> from inside the tool.
    /// </summary>
    [Fact]
    public void AMissingOrCorruptImagePayloadFailsWithSomethingReadable()
    {
        static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

        Assert.Contains("No image", Assert.Throws<LightboxOpException>(
            () => LightboxTools.ImageBlock(Json("{}"))).Message);
        Assert.Contains("No image", Assert.Throws<LightboxOpException>(
            () => LightboxTools.ImageBlock(Json("""{"pngBase64":""}"""))).Message);
        Assert.Contains("No image", Assert.Throws<LightboxOpException>(
            () => LightboxTools.ImageBlock(Json("""{"pngBase64":null}"""))).Message);
        Assert.Contains("base64", Assert.Throws<LightboxOpException>(
            () => LightboxTools.ImageBlock(Json("""{"pngBase64":"not base64 at all!"}"""))).Message);
    }
}
