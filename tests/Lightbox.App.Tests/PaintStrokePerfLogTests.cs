using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// What a stroke writes to the performance log, for the lab's paint-stroke
/// scenario: the cost of each pointer batch on the UI thread, each live pass
/// and how far behind the pen it landed, and the pen-up commit. Plus the one
/// hook the scenario needs to choose a brush without a mouse.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it applies presets, and the log
/// is process-wide, so it is started and stopped inside each test.
/// </remarks>
[Collection("BrushState")]
public sealed class PaintStrokePerfLogTests : BrushStateIsolated
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"lightbox-paintlog-{Guid.NewGuid():N}")).FullName;

    public override void Dispose()
    {
        PerfLog.StopForTests();
        Directory.Delete(_dir, recursive: true);
        base.Dispose();
    }

    private static MainViewModel Vm(string presetId)
    {
        var vm = new MainViewModel(null);
        vm.LivePostRunner = work => { work(); return Task.CompletedTask; };
        vm.NewDocument(new NewDocumentSettings("Log", 240, 160, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == presetId));
        vm.BrushSize = 28;
        return vm;
    }

    private static void Draw(MainViewModel vm)
    {
        vm.BeginStroke(40, 80, 0.9);
        for (var x = 50; x <= 200; x += 10)
        {
            vm.MoveStroke(x, 80, 0.9);
            Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    private List<JsonElement> Record(Action<MainViewModel> draw, string presetId)
    {
        var vm = Vm(presetId);
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.jsonl");
        PerfLog.StartForTests(path);
        try
        {
            draw(vm);
        }
        finally
        {
            PerfLog.StopForTests();
        }
        return File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement).ToList();
    }

    private static List<string> Events(List<JsonElement> lines) =>
        lines.Select(l => l.GetProperty("ev").GetString()!).ToList();

    [AvaloniaFact]
    public void AStrokeWithAMediumLogsItsBatchesItsPassesHowFarBehindTheyLandedAndItsCommit()
    {
        var lines = Record(Draw, "builtin-watercolor-wet");
        var events = Events(lines);

        Assert.Contains("stroke.move", events);
        Assert.Contains("live.pass", events);
        Assert.Contains("live.behind", events);
        Assert.Contains("stroke.commit", events);

        // The pass says how much of the mark it read, which is the number B313
        // changed and B331 is about.
        var pass = lines.First(l => l.GetProperty("ev").GetString() == "live.pass");
        Assert.Contains("mark=", pass.GetProperty("d").GetString());
        Assert.True(pass.GetProperty("ms").GetDouble() > 0, "a live pass has a duration");

        // Behind is a count of points, never negative: the pen cannot be
        // behind the mark.
        var behind = lines.First(l => l.GetProperty("ev").GetString() == "live.behind");
        Assert.True(behind.GetProperty("v").GetInt64() >= 0);

        // One commit, at the end, after every batch.
        Assert.Single(events, "stroke.commit");
        Assert.True(events.LastIndexOf("stroke.move") < events.IndexOf("stroke.commit"));
    }

    [AvaloniaFact]
    public void AnInkStrokeLogsItsBatchesAndCommitAndNoLivePass()
    {
        var events = Events(Record(Draw, "builtin-ink"));

        Assert.Contains("stroke.move", events);
        Assert.Single(events, "stroke.commit");
        Assert.DoesNotContain("live.pass", events);
        Assert.DoesNotContain("live.behind", events);
    }

    /// <summary>
    /// <c>LIGHTBOX_BRUSH=&lt;preset id&gt;[:&lt;size&gt;]</c> puts a brush in hand
    /// at launch, so a scenario can choose one without clicking the picker.
    /// Only in a throwaway profile: the preset store persists what is in hand
    /// at exit, and the September harness left nine built-in brushes at size
    /// 70 in the owner's real profile by exactly this route.
    /// </summary>
    [AvaloniaFact]
    public void TheBrushOverridePutsThePresetInHandAtTheSizeAsked()
    {
        var vm = new MainViewModel(null);
        var path = Path.Combine(_dir, "brush.jsonl");
        PerfLog.StartForTests(path);
        var applied = vm.ApplyBrushOverride("builtin-oil:80", profileIsThrowaway: true);
        PerfLog.StopForTests();

        Assert.True(applied);
        Assert.Equal("builtin-oil", vm.SelectedBrushPreset?.Id);
        Assert.Equal(80, vm.BrushSize);
        // The run's record says which brush it measured — the old harness's
        // rule, learned when unverified rows all turned out to be Ink.
        var line = JsonDocument.Parse(File.ReadAllLines(path).Single()).RootElement;
        Assert.Equal("brush", line.GetProperty("ev").GetString());
        Assert.Contains("builtin-oil", line.GetProperty("d").GetString());
        Assert.Contains("80", line.GetProperty("d").GetString());
    }

    [AvaloniaFact]
    public void TheBrushOverrideIsRefusedOutsideAThrowawayProfile()
    {
        var vm = new MainViewModel(null);
        var before = vm.SelectedBrushPreset?.Id;
        var size = vm.BrushSize;

        Assert.False(vm.ApplyBrushOverride("builtin-oil:80", profileIsThrowaway: false));
        Assert.Equal(before, vm.SelectedBrushPreset?.Id);
        Assert.Equal(size, vm.BrushSize);
    }

    [AvaloniaFact]
    public void TheBrushOverrideIgnoresAnUnknownPresetAndANonsenseSize()
    {
        var vm = new MainViewModel(null);
        Assert.False(vm.ApplyBrushOverride("builtin-no-such-brush", profileIsThrowaway: true));
        Assert.False(vm.ApplyBrushOverride("", profileIsThrowaway: true));
        Assert.False(vm.ApplyBrushOverride(null, profileIsThrowaway: true));

        // A size that cannot be read leaves the preset's own.
        Assert.True(vm.ApplyBrushOverride("builtin-ink:huge", profileIsThrowaway: true));
        Assert.Equal("builtin-ink", vm.SelectedBrushPreset?.Id);
        Assert.Equal(5, vm.BrushSize);
    }
}
