using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Mcp;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// An agent suggesting line matches over MCP: <c>render_match_pieces</c> shows
/// both keys with every piece numbered, <c>set_line_matches</c> takes the
/// numbers back and saves them as the artist's clicks would be saved.
/// </summary>
/// <remarks>
/// Most of what is asserted is refusal. The names an agent sends are only
/// meaningful against the picture it read, so a key that changed in between, a
/// name that does not exist, or a gap whose matches may be the artist's must
/// change nothing and say why.
/// </remarks>
[Collection("BrushState")]
public sealed class IpcLineMatchTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Draw(MainViewModel vm, params (double X, double Y)[] points)
    {
        vm.BeginStroke(points[0].X, points[0].Y, 0.5);
        foreach (var p in points.Skip(1)) vm.MoveStroke(p.X, p.Y, 0.5);
        vm.EndStroke();
    }

    /// <summary>
    /// Key A: an L as one stroke, cut at its corner into A1 (the upright) and A2
    /// (the foot). Key B: the same L 40 to the right as two strokes, the foot
    /// first (B1) and backwards, then the upright (B2). One inbetween, drawn the
    /// scrambled way.
    /// </summary>
    private static MainViewModel OnTheInbetween()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, (100, 100), (100, 200), (100, 300), (150, 300), (200, 300));
        vm.AddFrameCommand.Execute(null);
        Draw(vm, (240, 300), (190, 300), (146, 300));
        Draw(vm, (140, 294), (140, 200), (140, 100));
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.TweenEasing = Easing.Linear;
        vm.InsertInbetweensCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        return vm;
    }

    private static double OffTheHalfwayL(Frame frame)
    {
        StrokePoint[] l = [new(120, 100, 0), new(120, 300, 0), new(220, 300, 0)];
        return frame.Strokes.SelectMany(s => s.Points).Max(p =>
            Math.Min(GeometryOps.DistToSegment(p, l[0], l[1]), GeometryOps.DistToSegment(p, l[1], l[2])));
    }

    private static IpcProtocol.Request Req(string op, object? payload = null) => new()
    {
        Op = op,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    private static string CutOf(IpcDocumentApi api) =>
        api.Handle(Req("render_match_pieces", new { frameIndex = 1 })).Payload!.Value.GetProperty("cut").GetString()!;

    private static readonly object[] RightMatches =
    [
        new { a = new[] { "A1" }, b = new[] { "B2" } },
        new { a = new[] { "A2" }, b = new[] { "B1" } },
    ];

    [AvaloniaFact]
    public void ThePictureNumbersEveryPieceOfBothKeys()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("render_match_pieces", new { frameIndex = 1 }));

        Assert.True(resp.Ok, resp.Error);
        var payload = resp.Payload!.Value;
        var ids = payload.GetProperty("pieces").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Equal(["A1", "A2", "B1", "B2"], ids);
        Assert.Equal(0, payload.GetProperty("keyA").GetInt32());
        Assert.Equal(2, payload.GetProperty("keyB").GetInt32());
        Assert.Empty(payload.GetProperty("matches").EnumerateArray());
        using var image = SKBitmap.Decode(Convert.FromBase64String(payload.GetProperty("pngBase64").GetString()!));
        Assert.NotNull(image);
        // Two panels side by side, each capped at the panel size.
        Assert.True(image.Width > image.Height, $"{image.Width}x{image.Height}");
        Assert.True(image.Width <= 2 * 640 + 12);
        output.WriteLine($"picture {image.Width}x{image.Height}, pieces {string.Join(",", ids)}");
    }

    [AvaloniaFact]
    public void MatchesByNameAreSavedAndRedrawTheInbetweenInOneUndoStep()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        var scrambled = OffTheHalfwayL(vm.PaintLayer().Cels[1].Frame!);

        var resp = api.Handle(Req("set_line_matches", new { frameIndex = 1, cut = CutOf(api), matches = RightMatches }));

        Assert.True(resp.Ok, resp.Error);
        Assert.Equal(2, resp.Payload!.Value.GetProperty("saved").GetInt32());
        Assert.Equal(1, resp.Payload!.Value.GetProperty("redrawn").GetInt32());
        var matched = OffTheHalfwayL(vm.PaintLayer().Cels[1].Frame!);
        output.WriteLine($"off the halfway L: scrambled {scrambled:0.0}px, matched {matched:0.0}px");
        Assert.True(matched < 3, $"{matched:0.0}px");
        Assert.Equal(2, vm.PaintLayer().Cels[0].Frame!.Matches!.Count);

        // The picture now reports them by the same names.
        var read = api.Handle(Req("render_match_pieces", new { frameIndex = 1 })).Payload!.Value.GetProperty("matches");
        Assert.Equal("B2", read[0].GetProperty("b")[0].GetString());

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
        Assert.Equal(scrambled, OffTheHalfwayL(vm.PaintLayer().Cels[1].Frame!), 3);
    }

    [AvaloniaFact]
    public void AKeyChangedSinceThePictureIsRefused()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        var cut = CutOf(api);
        vm.CurrentFrameIndex = 0;
        Draw(vm, (300, 100), (300, 200));

        var resp = api.Handle(Req("set_line_matches", new { frameIndex = 1, cut, matches = RightMatches }));

        Assert.False(resp.Ok);
        Assert.Contains("not the ones you read", resp.Error);
        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
    }

    [AvaloniaTheory]
    [InlineData("A9", "B1", "is not a piece of key A")]
    [InlineData("B1", "B2", "is not a piece of key A")]
    [InlineData("A1", "A2", "is not a piece of key B")]
    public void ANameThatIsNotAPieceOfThatKeyIsRefused(string a, string b, string error)
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("set_line_matches", new
        {
            frameIndex = 1, cut = CutOf(api), matches = new[] { new { a = new[] { a }, b = new[] { b } } },
        }));

        Assert.False(resp.Ok);
        Assert.Contains(error, resp.Error);
        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Matches);
    }

    [AvaloniaFact]
    public void APieceInTwoMatchesIsRefused()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("set_line_matches", new
        {
            frameIndex = 1, cut = CutOf(api),
            matches = new[] { new { a = new[] { "A1" }, b = new[] { "B2" } }, new { a = new[] { "A1" }, b = new[] { "B1" } } },
        }));

        Assert.False(resp.Ok);
        Assert.Contains("more than one match", resp.Error);
    }

    [AvaloniaFact]
    public void AnAgentsMatchesAreMarkedAsItsAndAnArtistsFileStaysUnchanged()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);

        Assert.True(api.Handle(Req("set_line_matches", new { frameIndex = 1, cut = CutOf(api), matches = RightMatches })).Ok);

        Assert.All(vm.PaintLayer().Cels[0].Frame!.Matches!, m => Assert.Equal("MCP agent", m.SuggestedBy));
        var read = api.Handle(Req("render_match_pieces", new { frameIndex = 1 })).Payload!.Value.GetProperty("matches");
        Assert.Equal("agent", read[0].GetProperty("by").GetString());

        // Accepted, they are the artist's, and the file carries no key for it.
        vm.AcceptSuggestedMatchesCommand.Execute(null);
        Assert.All(vm.PaintLayer().Cels[0].Frame!.Matches!, m => Assert.Null(m.SuggestedBy));
        Assert.DoesNotContain("\"suggestedBy\"", Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void AnAgentsNextAnswerReplacesItsOwnSuggestions()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        var cut = CutOf(api);
        Assert.True(api.Handle(Req("set_line_matches", new { frameIndex = 1, cut, matches = RightMatches })).Ok);

        var again = api.Handle(Req("set_line_matches", new
        {
            frameIndex = 1, cut, matches = new[] { new { a = new[] { "A1" }, b = new[] { "B1" } } },
        }));

        Assert.True(again.Ok, again.Error);
        var match = Assert.Single(vm.PaintLayer().Cels[0].Frame!.Matches!);
        Assert.Equal("MCP agent", match.SuggestedBy);
    }

    [AvaloniaFact]
    public void TheArtistsMatchesAreKeptAndTheirPiecesCannotBeNamed()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        var cut = CutOf(api);
        Assert.True(api.Handle(Req("set_line_matches", new { frameIndex = 1, cut, matches = RightMatches })).Ok);
        // The artist accepts A1 -> B2 by clicking its line (the upright of key A).
        vm.LineMatchMode = true;
        vm.LineMatchPress(100, 150, shift: false, reach: 6);
        vm.LineMatchMode = false;
        var artists = vm.PaintLayer().Cels[0].Frame!.Matches!.Single(m => m.SuggestedBy is null);

        var refused = api.Handle(Req("set_line_matches", new
        {
            frameIndex = 1, cut, matches = new[] { new { a = new[] { "A1" }, b = new[] { "B1" } } },
        }));
        Assert.False(refused.Ok);
        Assert.Contains("the artist made", refused.Error);

        var other = api.Handle(Req("set_line_matches", new
        {
            frameIndex = 1, cut, matches = new[] { new { a = new[] { "A2" }, b = new[] { "B1" } } },
        }));
        Assert.True(other.Ok, other.Error);
        var all = vm.PaintLayer().Cels[0].Frame!.Matches!;
        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.SuggestedBy is null && m.A.SequenceEqual(artists.A) && m.B.SequenceEqual(artists.B));
        Assert.Empty(resp_unmatched(other));

        static IEnumerable<string?> resp_unmatched(IpcProtocol.Response r) =>
            r.Payload!.Value.GetProperty("unmatched").EnumerateArray().Select(e => e.GetString());
    }

    [AvaloniaFact]
    public void AClickOnASuggestedLineAcceptsItsMatchAsItIsAndShowsItFull()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        Assert.True(api.Handle(Req("set_line_matches", new { frameIndex = 1, cut = CutOf(api), matches = RightMatches })).Ok);
        vm.LineMatchMode = true;
        Assert.All(vm.LineMatchView!.Lines, l => Assert.True(l.Suggested));

        vm.LineMatchPress(100, 150, shift: false, reach: 6);

        var matches = vm.PaintLayer().Cels[0].Frame!.Matches!;
        Assert.Equal(2, matches.Count);
        var accepted = matches.Single(m => m.SuggestedBy is null);
        Assert.Single(accepted.A);
        Assert.Single(accepted.B);
        Assert.Equal(2, vm.LineMatchView!.Lines.Count(l => !l.Suggested));
        vm.UndoCommand.Execute(null);
        Assert.All(vm.PaintLayer().Cels[0].Frame!.Matches!, m => Assert.NotNull(m.SuggestedBy));
    }

    [AvaloniaFact]
    public void AnEmptyAnswerIsRefusedAndClearsNothing()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        var cut = CutOf(api);
        Assert.True(api.Handle(Req("set_line_matches", new { frameIndex = 1, cut, matches = RightMatches })).Ok);

        var resp = api.Handle(Req("set_line_matches", new { frameIndex = 1, cut, matches = Array.Empty<object>() }));

        Assert.False(resp.Ok);
        Assert.Equal(2, vm.PaintLayer().Cels[0].Frame!.Matches!.Count);
    }

    [AvaloniaFact]
    public void TheReplyNamesThePiecesLeftOut()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("set_line_matches", new
        {
            frameIndex = 1, cut = CutOf(api), matches = new[] { new { a = new[] { "A1" }, b = new[] { "B2" } } },
        }));

        Assert.True(resp.Ok, resp.Error);
        var left = resp.Payload!.Value.GetProperty("unmatched").EnumerateArray().Select(e => e.GetString()).Order().ToList();
        Assert.Equal(["A2", "B1"], left);
    }

    [Fact]
    public void TheCutTokenSeesAnInteriorPointMoveAndIsTheSameEveryTime()
    {
        // The guard on a write: an interior nudge keeps a stroke's id, its
        // point count and its first, middle and last points — everything the
        // cache's fingerprint samples — and can still move where it is cut.
        static List<LinePiece> Cut(double bend) =>
            LinePieces.Cut([new Stroke
            {
                Id = "s",
                Points = [new(0, 0, 0.5), new(30, bend, 0.5), new(60, 0, 0.5), new(90, 0, 0.5), new(120, 0, 0.5)],
            }]);

        var token = MainViewModel.CutToken(Cut(0), Cut(0));

        Assert.Equal(token, MainViewModel.CutToken(Cut(0), Cut(0)));
        Assert.NotEqual(token, MainViewModel.CutToken(Cut(4), Cut(0)));
        // A fixed value: SHA-256 is not seeded per process, so a restart of
        // Lightbox does not refuse a cut that has not changed.
        output.WriteLine($"token {token}");
        Assert.Matches("^[0-9a-f]{24}$", token);
    }

    [AvaloniaFact]
    public void AFrameWithNoGapIsRefused()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, (100, 100), (200, 200));
        var api = new IpcDocumentApi(vm);

        var resp = api.Handle(Req("render_match_pieces", new { frameIndex = 0 }));

        Assert.False(resp.Ok);
        Assert.Contains("No gap", resp.Error);
    }

    [AvaloniaFact]
    public void TheMcpTextBlockCarriesEverythingButTheImage()
    {
        var vm = OnTheInbetween();
        var api = new IpcDocumentApi(vm);
        var payload = api.Handle(Req("render_match_pieces", new { frameIndex = 1 })).Payload!.Value;

        var text = LightboxTools.WithoutImage(payload);

        Assert.DoesNotContain("pngBase64", text);
        Assert.Contains("\"cut\"", text);
        Assert.Contains("\"A1\"", text);
        Assert.NotNull(LightboxTools.ImageBlock(payload));
    }
}
