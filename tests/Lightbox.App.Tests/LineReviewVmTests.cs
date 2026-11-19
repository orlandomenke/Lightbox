using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The AI line review (Q296–Q299) through the app and the pipe: a model names
/// fixes, the engine makes them, and they wait as a preview — never on a key,
/// never without a provider, never uncited.
/// </summary>
[Collection("BrushState")]
public sealed class LineReviewVmTests : BrushStateIsolated
{
    /// <summary>A reviewer that answers with the fixes it is given and remembers what it was sent.</summary>
    private sealed class Reviewer(params ReviewFix[] fixes) : IAiArtist
    {
        public LineReviewRequest? Last { get; private set; }
        public int Calls { get; private set; }

        public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(InbetweenRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<List<InbetweenFrameResult>>.Error("unscripted", false));

        public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(SubjectRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<SubjectTaxonomy>.Error("unscripted", false));

        public Task<AiResult<List<ReviewFix>>> ReviewLinesAsync(LineReviewRequest request, CancellationToken ct)
        {
            Last = request;
            Calls++;
            return Task.FromResult(AiResult<List<ReviewFix>>.Success([.. fixes]));
        }
    }

    private static void Draw(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        var n = (int)Math.Ceiling(Math.Abs(y1 - y0) + Math.Abs(x1 - x0));
        vm.BeginStroke(x0, y0, 0.5);
        for (var i = 1; i <= n; i++) vm.MoveStroke(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, 0.5);
        vm.EndStroke();
    }

    private static readonly ReviewFix TrimTheKnee =
        new(ReviewFixKind.Trim, 1, false, 2, false, ["A1", "B1"], "The knee runs on past the shin; both keys meet them at one point.");

    /// <summary>
    /// Two keys, a knee meeting a shin at one point, and an inbetween between
    /// them whose knee runs 30 px down past the top of the shin.
    /// </summary>
    private static MainViewModel KneeInbetween(IAiArtist? artist)
    {
        var vm = new MainViewModel(artist) { SmoothStrokes = false };
        Draw(vm, 90, 0, 90, 100);
        Draw(vm, 90, 100, 90, 300);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 110, 0, 110, 100);
        Draw(vm, 110, 100, 110, 300);
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.TweenEasing = Easing.Linear;
        vm.InsertInbetweensCommand.Execute(null);
        var inbetween = vm.PaintLayer().Cels[1].Frame!;
        var knee = inbetween.Strokes[0];
        var shin = inbetween.Strokes[1];
        knee.Points = [.. Enumerable.Range(0, 131).Select(i => new StrokePoint(100, i, 0.5))];
        shin.Points = [.. Enumerable.Range(0, 201).Select(i => new StrokePoint(104, 100 + i, 0.5))];
        vm.CurrentFrameIndex = 1;
        return vm;
    }

    private static List<StrokePoint> KneeOf(MainViewModel vm) => [.. vm.PaintLayer().Cels[1].Frame!.Strokes[0].Points];

    [AvaloniaFact]
    public async Task AReviewsFixesWaitAsAPreviewAndApplyAsOneStep()
    {
        var vm = KneeInbetween(new Reviewer(TrimTheKnee));
        var before = KneeOf(vm);

        await vm.ReviewLinesWithAiCommand.ExecuteAsync(null);

        Assert.True(vm.IsCleanupPending);
        Assert.Equal(before, KneeOf(vm));
        Assert.Contains("AI review", vm.CleanupSummary);
        Assert.Contains("runs on past the shin", vm.CleanupSummary);
        Assert.True(vm.TryApplyCleanup());
        Assert.InRange(KneeOf(vm)[^1].Y, 99, 101);
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, KneeOf(vm));
    }

    [AvaloniaFact]
    public async Task TheRequestCarriesTheNumberedPictureAndListing()
    {
        var reviewer = new Reviewer();
        var vm = KneeInbetween(reviewer);

        await vm.ReviewLinesWithAiCommand.ExecuteAsync(null);

        Assert.NotNull(reviewer.Last!.Picture);
        Assert.Contains("L1:", reviewer.Last.Listing);
        Assert.Contains("A2:", reviewer.Last.Listing);
        Assert.Matches("runs alongside it for", reviewer.Last.Listing);
        Assert.Contains("found no mistakes", vm.AiStatus);
        Assert.False(vm.IsCleanupPending);
    }

    [AvaloniaFact]
    public async Task AKeyIsNeverSentToBeFixed()
    {
        var reviewer = new Reviewer(TrimTheKnee);
        var vm = KneeInbetween(reviewer);
        vm.CurrentFrameIndex = 0;

        await vm.ReviewLinesWithAiCommand.ExecuteAsync(null);

        Assert.Equal(0, reviewer.Calls);
        Assert.Contains("for inbetweens", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task WithoutAProviderItSaysSoAndSendsNothing()
    {
        var vm = KneeInbetween(artist: null);

        await vm.ReviewLinesWithAiCommand.ExecuteAsync(null);

        Assert.False(vm.IsCleanupPending);
        Assert.Contains("needs a provider", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task AFixCitingNoKeyLineIsNotMade()
    {
        var vm = KneeInbetween(new Reviewer(TrimTheKnee with { Cites = [] }));

        await vm.ReviewLinesWithAiCommand.ExecuteAsync(null);

        Assert.False(vm.IsCleanupPending);
        Assert.Contains("none could be made", vm.AiStatus);
        Assert.Contains("Q298", vm.AiStatus);
    }

    [AvaloniaFact]
    public void NewInbetweensAreNotSentForReviewUnlessTheArtistAsks()
    {
        var reviewer = new Reviewer();
        var vm = KneeInbetween(reviewer);

        Assert.False(vm.ReviewNewInbetweensWithAi);
        Assert.Equal(0, reviewer.Calls);
    }

    [AvaloniaFact]
    public void WhenAskedANewInbetweenIsReviewedAndTheFindingsWaitAsAPreview()
    {
        var reviewer = new Reviewer(new ReviewFix(ReviewFixKind.Join, 1, false, 2, true, ["A1"], "The pieces should meet."));
        var vm = new MainViewModel(reviewer) { SmoothStrokes = false, ReviewNewInbetweensWithAi = true };
        Draw(vm, 90, 0, 90, 100);
        Draw(vm, 90, 110, 90, 300);   // a gap the keys leave too: the scripted reviewer is wrong, and only previews
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 110, 0, 110, 100);
        Draw(vm, 110, 110, 110, 300);
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        var keyStrokes = vm.PaintLayer().Cels[0].Frame!.Strokes.Select(s => s.Points.ToList()).ToList();

        vm.InsertInbetweensCommand.Execute(null);

        Assert.Equal(1, reviewer.Calls);
        Assert.True(vm.IsCleanupPending);
        Assert.Equal(keyStrokes, vm.PaintLayer().Cels[0].Frame!.Strokes.Select(s => s.Points.ToList()).ToList());
    }

    // ---- over the pipe --------------------------------------------------------

    private static IpcProtocol.Request Req(string op, object payload) => new()
    {
        Op = op,
        Payload = JsonSerializer.SerializeToElement(payload, IpcProtocol.Json),
    };

    [AvaloniaFact]
    public void AnAgentReadsTheSheetAndItsFixesWaitAsAPreview()
    {
        var vm = KneeInbetween(artist: null);
        var api = new IpcDocumentApi(vm);
        var before = KneeOf(vm);

        var sheet = api.Handle(Req("render_line_review", new { frameIndex = 1 }));
        Assert.True(sheet.Ok, sheet.Error);
        var token = sheet.Payload!.Value.GetProperty("sheet").GetString();
        Assert.False(string.IsNullOrEmpty(sheet.Payload.Value.GetProperty("pngBase64").GetString()));
        var fixes = JsonSerializer.SerializeToElement(new[]
        {
            new { kind = "trim", line = 1, end = "end", other = (int?)2, otherEnd = "start", cites = new[] { "A1" }, reason = "past the joint" },
        });

        var resp = api.Handle(Req("fix_lines", new { frameIndex = 1, sheet = token, fixes }));

        Assert.True(resp.Ok, resp.Error);
        Assert.Single(resp.Payload!.Value.GetProperty("made").EnumerateArray());
        Assert.True(vm.IsCleanupPending);
        Assert.Equal(before, KneeOf(vm));
    }

    [AvaloniaFact]
    public void FixesNamedOnAnOutOfDateSheetAreRefused()
    {
        var vm = KneeInbetween(artist: null);
        var api = new IpcDocumentApi(vm);
        var token = api.Handle(Req("render_line_review", new { frameIndex = 1 })).Payload!.Value.GetProperty("sheet").GetString();
        vm.PaintLayer().Cels[1].Frame!.Strokes.RemoveAt(1);
        var fixes = JsonSerializer.SerializeToElement(new[] { new { kind = "remove", line = 1, end = "end", other = (int?)null, otherEnd = "end", cites = new[] { "A1" }, reason = "x" } });

        var resp = api.Handle(Req("fix_lines", new { frameIndex = 1, sheet = token, fixes }));

        Assert.False(resp.Ok);
        Assert.Contains("changed since its sheet was read", resp.Error);
    }

    [AvaloniaFact]
    public void AKeyCannotBeReadForReview()
    {
        var api = new IpcDocumentApi(KneeInbetween(artist: null));

        var resp = api.Handle(Req("render_line_review", new { frameIndex = 0 }));

        Assert.False(resp.Ok);
        Assert.Contains("for inbetweens", resp.Error);
    }

    [AvaloniaFact]
    public void TheCommandsAreRegistered()
    {
        var ids = new ShortcutMap().Definitions.Select(d => d.Id).ToList();

        Assert.Contains("ai.reviewLines", ids);
        Assert.Contains("ai.reviewNewInbetweens", ids);
    }
}
