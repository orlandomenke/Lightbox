using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// What an agent pays to read the document over MCP.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sibling of <c>AiPayloadBudgetTests</c>, measuring a cost that one
/// cannot see.</b> <c>docs/DESIGN-ai-payload.md</c> costs an AI <i>request</i>:
/// built, sent, paid once. An MCP reply is a different thing — it lands in the
/// agent's context and is re-read on every turn for the rest of the session, so
/// a fat reply is not a one-off charge but a standing one. Nothing in the suite
/// said a word about it before this file, which is the same gap the roadmap
/// names at "the coverage gate that would make the next such gap fail a test
/// instead of needing an audit".
/// </para>
/// <para>
/// Ratios are asserted and absolutes are printed, because the absolute depends
/// on the fixture and the ratio is the thing that must not regress.
/// </para>
/// </remarks>
public class McpReadBudgetTests(ITestOutputHelper output)
{
    /// <summary>A drawing dense enough to be worth measuring: labelled strokes, many points.</summary>
    private static MainViewModel VmWithDenseDrawing(int strokes = 120, int points = 90)
    {
        var vm = new MainViewModel(null);
        var frame = (Frame)vm.PaintLayer().Cels[0].Frame!;
        frame.Strokes.Clear();
        for (var s = 0; s < strokes; s++)
        {
            var stroke = new Stroke { Label = $"stroke-{s:D3}" };
            for (var i = 0; i < points; i++)
            {
                // Spread over the canvas so the boxes differ from one another.
                stroke.Points.Add(new StrokePoint(
                    10 + (s * 7 % 900) + (i % 11), 10 + (s * 13 % 500) + (i % 7), 0.5));
            }
            frame.Strokes.Add(stroke);
        }
        return vm;
    }

    private static IpcProtocol.Request Req(string op, object? payload = null) => new()
    {
        Op = op,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    private static string Raw(IpcProtocol.Response resp)
    {
        Assert.True(resp.Ok, resp.Error);
        return resp.Payload!.Value.GetRawText();
    }

    /// <summary>
    /// The listing is the lever: it answers "what is in this drawing" for a
    /// small fraction of what the geometry costs.
    /// </summary>
    /// <remarks>
    /// <b>Measured against the whole reply, not against the strokes array.</b>
    /// An agent pays for the bytes that reach it, envelope included, and a
    /// budget that quietly excluded the envelope would flatter a change that
    /// moved cost into it.
    /// </remarks>
    [AvaloniaFact]
    public void ListingADrawingCostsAFractionOfReadingIt()
    {
        var api = new IpcDocumentApi(VmWithDenseDrawing());
        var listing = Raw(api.Handle(Req("list_frame_strokes", new { frameIndex = 0 })));
        var full = Raw(api.Handle(Req("get_frame_strokes", new { frameIndex = 0 })));

        var share = listing.Length / (double)full.Length;
        output.WriteLine(
            $"120 strokes x 90 points: listing {listing.Length / 1024.0:F1} KB (~{listing.Length / 4} tokens), "
            + $"full {full.Length / 1024.0:F1} KB (~{full.Length / 4} tokens) — listing is {share:P1} of the read");

        Assert.True(share < 0.15, $"the listing is now {share:P1} of a full read — it has stopped being the cheap path");
    }

    /// <summary>
    /// Naming the strokes you want is what makes the listing pay off — the whole
    /// point is the second call, not the first.
    /// </summary>
    [AvaloniaFact]
    public void FetchingTheStrokesYouNamedCostsAboutWhatThoseStrokesWeigh()
    {
        var vm = VmWithDenseDrawing();
        var layerId = vm.PaintLayer().Id;
        var api = new IpcDocumentApi(vm);
        var full = Raw(api.Handle(Req("get_frame_strokes", new { frameIndex = 0 })));
        var three = Raw(api.Handle(Req("get_frame_strokes", new
        {
            frameIndex = 0,
            layerId,
            labels = new[] { "stroke-000", "stroke-041", "stroke-119" },
        })));

        var share = three.Length / (double)full.Length;
        output.WriteLine(
            $"3 of 120 strokes: {three.Length / 1024.0:F1} KB against {full.Length / 1024.0:F1} KB — {share:P1}");

        // 3/120 is 2.5%; the envelope keeps it off zero.
        Assert.True(share < 0.06, $"a three-stroke fetch costs {share:P1} of the whole frame");
    }

    /// <summary>
    /// The two stroke ops number the same list, so an index from one addresses
    /// the stroke the other meant.
    /// </summary>
    /// <remarks>
    /// The round trip is the contract an agent actually relies on: it reads a
    /// listing, picks positions out of it, and asks for those. Two separate
    /// walks of the record — one filtered differently from the other — would
    /// make that silently fetch the wrong line, and nothing in the reply would
    /// say so.
    /// </remarks>
    [AvaloniaFact]
    public void AnIndexFromTheListingFetchesTheStrokeTheListingNamed()
    {
        var vm = VmWithDenseDrawing();
        var api = new IpcDocumentApi(vm);
        var listing = api.Handle(Req("list_frame_strokes", new { frameIndex = 0 }));
        Assert.True(listing.Ok);
        var entry = listing.Payload!.Value.GetProperty("strokes")[41];
        Assert.Equal(41, entry.GetProperty("index").GetInt32());
        var label = entry.GetProperty("label").GetString();

        var layerId = listing.Payload!.Value.GetProperty("layerId").GetString();
        var fetched = api.Handle(Req("get_frame_strokes", new { frameIndex = 0, layerId, indices = new[] { 41 } }));
        Assert.True(fetched.Ok);
        var strokes = fetched.Payload!.Value.GetProperty("strokes");
        Assert.Equal(1, strokes.GetArrayLength());
        Assert.Equal(label, strokes[0].GetProperty("label").GetString());
    }

    /// <summary>
    /// The listing carries a box per stroke, padded the way the transform gizmo
    /// pads one — which is what makes it answer "would my change touch this".
    /// </summary>
    [AvaloniaFact]
    public void TheListingSaysWhereEachStrokeIs()
    {
        var api = new IpcDocumentApi(VmWithDenseDrawing(strokes: 2, points: 4));
        var listing = api.Handle(Req("list_frame_strokes", new { frameIndex = 0 }));
        Assert.True(listing.Ok);
        var box = listing.Payload!.Value.GetProperty("strokes")[0].GetProperty("box");
        Assert.Equal(4, box.GetArrayLength()); // flat [x, y, w, h] — Q18 where it was aimed
        Assert.True(box[2].GetDouble() > 0, "a stroke that spans points has a width");
        Assert.Equal(2, listing.Payload!.Value.GetProperty("strokeCount").GetInt32());
    }

    /// <summary>
    /// A label that is not there is refused by name, never answered with an
    /// empty list.
    /// </summary>
    /// <remarks>
    /// An agent cannot see the drawing. An empty array back from a misspelled
    /// label reads as "that stroke is gone" and sends it redrawing something
    /// that is already there — so this follows <c>import_character</c>'s rule
    /// and names what is present, because that is what makes the retry a
    /// decision rather than a guess.
    /// </remarks>
    [AvaloniaFact]
    public void AStrokeLabelThatIsNotThereIsRefusedAndTheRealOnesAreNamed()
    {
        var vm = VmWithDenseDrawing(strokes: 3, points: 4);
        var api = new IpcDocumentApi(vm);
        var resp = api.Handle(Req("get_frame_strokes", new { frameIndex = 0, layerId = vm.PaintLayer().Id, labels = new[] { "hed-outline" } }));
        Assert.False(resp.Ok);
        Assert.Contains("hed-outline", resp.Error);
        Assert.Contains("stroke-000", resp.Error);
    }

    [AvaloniaFact]
    public void AStrokeIndexPastTheEndIsRefusedWithTheCount()
    {
        var vm = VmWithDenseDrawing(strokes: 3, points: 4);
        var api = new IpcDocumentApi(vm);
        var resp = api.Handle(Req("get_frame_strokes", new { frameIndex = 0, layerId = vm.PaintLayer().Id, indices = new[] { 7 } }));
        Assert.False(resp.Ok);
        Assert.Contains("3 strokes", resp.Error);
    }

    /// <summary>
    /// Index 0 is a real stroke, not the "nothing matched" sentinel.
    /// </summary>
    /// <remarks>
    /// <c>FirstOrDefault</c> over a list of ints answers 0 for "no match", and 0
    /// is a perfectly good stroke index — so the range check casts through
    /// <c>int?</c>. Written down as a test because the bug it guards is
    /// invisible: asking for stroke 0 would have been refused as out of range on
    /// every drawing.
    /// </remarks>
    [AvaloniaFact]
    public void AskingForStrokeZeroIsNotMistakenForAskingForNothing()
    {
        var vm = VmWithDenseDrawing(strokes: 3, points: 4);
        var api = new IpcDocumentApi(vm);
        var resp = api.Handle(Req("get_frame_strokes", new { frameIndex = 0, layerId = vm.PaintLayer().Id, indices = new[] { 0 } }));
        Assert.True(resp.Ok, resp.Error);
        Assert.Equal(1, resp.Payload!.Value.GetProperty("strokes").GetArrayLength());
    }

    /// <summary>
    /// Unfiltered still means the whole drawing — the cheap path is opt-in, and
    /// no agent that already worked loses anything.
    /// </summary>
    [AvaloniaFact]
    public void AnUnfilteredReadIsStillTheWholeDrawing()
    {
        var api = new IpcDocumentApi(VmWithDenseDrawing(strokes: 12, points: 8));
        var resp = api.Handle(Req("get_frame_strokes", new { frameIndex = 0 }));
        Assert.True(resp.Ok);
        Assert.Equal(12, resp.Payload!.Value.GetProperty("strokes").GetArrayLength());
    }

    /// <summary>
    /// Naming strokes requires naming the layer, because the active-layer
    /// default is resolved again on every call.
    /// </summary>
    /// <remarks>
    /// <b>Found by G12's ai-engineer by reproducing it, not by reading the
    /// code.</b> A listing numbers strokes against whichever layer was active
    /// then; the artist clicking a different layer before the matching fetch is
    /// an ordinary thing to do, after which the same index silently addressed a
    /// different drawing's stroke and the reply was a cheerful <c>Ok</c>. A
    /// refusal is the fix because there is no way to detect the mistake from the
    /// reply — both answers are well-formed drawings.
    /// </remarks>
    [AvaloniaFact]
    public void NamingStrokesWithoutNamingTheLayerIsRefused()
    {
        var api = new IpcDocumentApi(VmWithDenseDrawing(strokes: 3, points: 4));
        var resp = api.Handle(Req("get_frame_strokes", new { frameIndex = 0, indices = new[] { 1 } }));
        Assert.False(resp.Ok);
        Assert.Contains("layerId", resp.Error);

        // An unfiltered read still rides the active-layer default, because it
        // cannot mis-attribute anything: it asks for a drawing, not for strokes.
        Assert.True(api.Handle(Req("get_frame_strokes", new { frameIndex = 0 })).Ok);
    }

    /// <summary>
    /// The reply says which record positions came back, because a wire stroke
    /// carries no index of its own.
    /// </summary>
    /// <remarks>
    /// Strokes come back in record order and never in the order they were asked
    /// for, so an agent zipping its <c>indices</c> against the reply — the
    /// natural thing to do — would attribute each stroke to the wrong request.
    /// G12 reproduced it with <c>[4, 1]</c>. The positions ride in the envelope
    /// rather than on each stroke, so the AI request path does not pay for a
    /// field only this surface needs.
    /// </remarks>
    [AvaloniaFact]
    public void TheReplySaysWhichPositionsItIsAnswering()
    {
        var vm = VmWithDenseDrawing(strokes: 6, points: 4);
        var layerId = vm.PaintLayer().Id;
        var api = new IpcDocumentApi(vm);
        var resp = api.Handle(Req("get_frame_strokes", new
        {
            frameIndex = 0,
            layerId,
            indices = new[] { 4, 1 },
        }));
        Assert.True(resp.Ok, resp.Error);

        var positions = resp.Payload!.Value.GetProperty("indices").EnumerateArray()
            .Select(e => e.GetInt32()).ToList();
        Assert.Equal([1, 4], positions); // record order, and said so
        var strokes = resp.Payload!.Value.GetProperty("strokes");
        Assert.Equal("stroke-001", strokes[0].GetProperty("label").GetString());
        Assert.Equal("stroke-004", strokes[1].GetProperty("label").GetString());
    }

    /// <summary>
    /// What it costs an agent to <i>write</i> a frame back, which is the half no
    /// listing helps with.
    /// </summary>
    /// <remarks>
    /// <b>This test documents a limit rather than guarding a fix — B-numbered in
    /// BUGS.md, and deliberately left open.</b> <c>insert_inbetweens</c> and
    /// <c>draw_strokes</c> take full geometry, so an agent inbetweening a dense
    /// frame must <i>emit</i> every point of every stroke, three times over for
    /// three inbetweens. That is not a bill, it is a ceiling: the answer does not
    /// fit in one response, so the task cannot be completed over MCP at all.
    /// The number is printed and asserted loosely, so that a future delta
    /// encoding shows up here as a change rather than as a claim.
    /// </remarks>
    [AvaloniaFact]
    public void WritingAFrameBackCostsWhatReadingItDid_WhichIsTheCeiling()
    {
        var api = new IpcDocumentApi(VmWithDenseDrawing());
        var full = Raw(api.Handle(Req("get_frame_strokes", new { frameIndex = 0 })));

        // Three inbetweens, each a whole drawing — what the tool's contract asks for.
        var threeFrames = full.Length * 3;
        output.WriteLine(
            $"one frame {full.Length / 1024.0:F1} KB (~{full.Length / 4} tokens); "
            + $"three inbetweens ~{threeFrames / 1024.0:F1} KB (~{threeFrames / 4} output tokens) — "
            + "past any single-response limit, so this is a ceiling and not a cost");

        Assert.True(
            threeFrames / 4 > 32_000,
            "three inbetweens of a dense frame now fit inside a typical response budget — "
            + "if a delta encoding landed, close the bug this test documents");
    }
}
