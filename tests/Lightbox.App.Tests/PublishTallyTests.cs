using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// B178's instrument: who publishes during playback, counted per caller.
/// These prove the instrument — its counting, its gating, and the report's
/// wording — and are deliberately not B178's evidence anchors: the entry
/// closes on a capture from the owner's machine, not on the counter working.
/// The same split <c>StrokeToScreenTests</c> already holds for B189.
/// </summary>
[Collection("BrushState")]
public sealed class PublishTallyTests(ITestOutputHelper output) : BrushStateIsolated
{
    [AvaloniaFact]
    public void OnlyPublishesWhileTheClockRunsAreTallied()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;

        // Paused: however much publishing happens, the table stays empty —
        // pointer publishes are legitimate and would bury the tick's surplus.
        vm.PublishSnapshot();
        vm.PublishSnapshot();
        Assert.Equal(0, vm.ReportPublishTally.Total);

        vm.TogglePlaybackCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        vm.PublishSnapshot();
        vm.PublishSnapshot();
        vm.PublishSnapshot();
        vm.TogglePlaybackCommand.Execute(null);

        var rows = vm.ReportPublishTally.Snapshot();
        output.WriteLine(string.Join(", ", rows.Select(r => $"{r.Caller}={r.Count}")));
        Assert.True(vm.ReportPublishTally.Total >= 3);
        // CallerMemberName stamps the member that made the call — here, this
        // test — which is the property that makes the table greppable.
        Assert.Contains(rows, r => r.Caller == nameof(OnlyPublishesWhileTheClockRunsAreTallied));
    }

    [Fact]
    public void TheReportNamesTheBusiestCallerAndJudgesAgainstTheTicks()
    {
        using var report = new RenderReportTests(output).Setup();

        // B178's own field numbers: 757 publishes over 339 ticks, ~2.2 per
        // tick. The tick's own publishes are the baseline; the surplus caller
        // must be visible above it.
        var tally = new PublishTally();
        for (var i = 0; i < 339; i++) tally.Note("OnPlaybackTick");
        for (var i = 0; i < 418; i++) tally.Note("RefreshThumbnails");

        var path = RenderReport.WriteStartup(RenderReportTests.Facts(
            pacing: new PlaybackClock.Pacing(339, 0, 0, 0, 0, 0),
            publishesByCaller: tally));
        var text = File.ReadAllText(path!);

        Assert.Contains("who publishes during playback (B178)", text);
        Assert.Contains("publishes while playing   757, from 2 caller(s)", text);
        // Busiest first, so the surplus caller is the first row a reader sees.
        Assert.True(
            text.IndexOf("RefreshThumbnails", StringComparison.Ordinal)
            < text.IndexOf("OnPlaybackTick", StringComparison.Ordinal),
            "the table is not sorted busiest-first");
        Assert.Contains("2.23 publishes per tick", text);
    }

    [Fact]
    public void AQuietCaptureSaysPlayFirstInsteadOfJudging()
    {
        using var report = new RenderReportTests(output).Setup();

        var path = RenderReport.WriteStartup(RenderReportTests.Facts(
            publishesByCaller: new PublishTally()));
        var text = File.ReadAllText(path!);

        Assert.Contains("who publishes during playback (B178)", text);
        Assert.Contains("none yet — this needs the scene PLAYED first", text);
    }
}
