using System.Text.Json;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The app-side half of the performance lab (Q209): the action log, and the one
/// profile folder a run can point somewhere else.
/// </summary>
public sealed class PerfLabHookTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"lightbox-perflab-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        PerfLog.StopForTests();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Off, a span is a default struct and nothing is written anywhere.</summary>
    [Fact]
    public void AnOffLogRecordsNothingAndCostsNoFile()
    {
        Assert.False(PerfLog.On);
        using (PerfLog.Begin("undo")) { }
        PerfLog.Mark("playhead", "3");
        Assert.Empty(Directory.GetFiles(_dir));
    }

    /// <summary>
    /// Each line is one JSON object naming the action, when it began and how
    /// long it took — what the lab overlaps against stalls and the screen.
    /// </summary>
    [Fact]
    public void ASpanIsOneJsonLineWithItsStartAndDuration()
    {
        var path = Path.Combine(_dir, "perf.jsonl");
        PerfLog.StartForTests(path);
        using (PerfLog.Begin("transform.commit", "detail"))
        {
            Thread.Sleep(15);
        }
        PerfLog.Mark("ready");
        PerfLog.StopForTests();

        var lines = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("transform.commit", lines[0].GetProperty("ev").GetString());
        Assert.Equal("detail", lines[0].GetProperty("d").GetString());
        Assert.True(lines[0].GetProperty("ms").GetDouble() >= 10, "the span did not measure its own duration");
        Assert.True(lines[1].GetProperty("t").GetDouble() >= lines[0].GetProperty("t").GetDouble());
        Assert.Equal(0, lines[1].GetProperty("ms").GetDouble());
    }

    /// <summary>
    /// A duration measured elsewhere — the pen-to-screen chain clocks itself —
    /// is written as if it had been a span: its start is backdated by its own
    /// length, so the lab can overlap it with stalls like any other action.
    /// </summary>
    [Fact]
    public void ATakenDurationIsBackdatedToWhenItStarted()
    {
        var path = Path.Combine(_dir, "perf.jsonl");
        PerfLog.StartForTests(path);
        PerfLog.Mark("before");
        PerfLog.Took("pen.screen", 40);
        PerfLog.StopForTests();

        var lines = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(2, lines.Count);
        var before = lines[0].GetProperty("t").GetDouble();
        var taken = lines[1];
        Assert.Equal("pen.screen", taken.GetProperty("ev").GetString());
        Assert.Equal(40, taken.GetProperty("ms").GetDouble());
        Assert.True(taken.GetProperty("t").GetDouble() < before,
            "a taken duration starts before it was written, by its own length");
    }

    /// <summary>
    /// A count is a moment with a value — how many points the pen is ahead of
    /// the mark — and it travels in its own field so the lab never reads a
    /// count of points as a number of milliseconds.
    /// </summary>
    [Fact]
    public void ACountIsOneLineWithItsValueInItsOwnField()
    {
        var path = Path.Combine(_dir, "perf.jsonl");
        PerfLog.StartForTests(path);
        PerfLog.Count("live.behind", 12);
        PerfLog.StopForTests();

        var line = JsonDocument.Parse(File.ReadAllLines(path).Single()).RootElement;
        Assert.Equal("live.behind", line.GetProperty("ev").GetString());
        Assert.Equal(0, line.GetProperty("ms").GetDouble());
        Assert.Equal(12, line.GetProperty("v").GetInt64());
    }

    /// <summary>
    /// The artist's number. Every drawn frame that carried fresh ink logs how
    /// long its oldest event took to reach the screen, and how long its newest
    /// did — the render report's pen→screen and tip→screen, per frame rather
    /// than as a session mean, so a paint scenario can take their median and
    /// their worst.
    /// </summary>
    [Fact]
    public void ADrawnFrameLogsHowLongItsInkTookToReachTheScreen()
    {
        var path = Path.Combine(_dir, "perf.jsonl");
        PerfLog.StartForTests(path);
        var chain = new Lightbox.App.Rendering.StrokeToScreen();
        chain.Stamped(Lightbox.App.Rendering.StrokeToScreen.EventArrived());
        chain.Published(7);
        chain.Rendered(7);
        // A frame with no ink waiting records nothing: playback and thumbnails
        // stay out of a paint scenario's numbers.
        chain.Published(8);
        chain.Rendered(8);
        PerfLog.StopForTests();

        var events = File.ReadAllLines(path)
            .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("ev").GetString()).ToList();
        Assert.Equal(["pen.screen", "tip.screen"], events);
    }

    /// <summary>
    /// Every store derives from the one profile root, so moving it moves them
    /// all — settings, the key file, workspaces, autosave. The September
    /// harness proved what a store with its own path does to a real profile.
    /// </summary>
    [Fact]
    public void EveryStoreDerivesFromTheOneProfileRoot()
    {
        var root = Lightbox.Core.ProfileFolder.Root;
        Assert.Equal(Path.Combine(root, "settings.json"), Lightbox.Ai.ApiKeyProvider.SettingsPath);
        Assert.Equal(Path.Combine(root, "autosave.lightbox.json"), AutosaveService.AutosavePath);

        // AppSettings.Path and WorkspaceStore.Path are redirected by this test
        // assembly itself, so they cannot be read back here. What actually went
        // wrong in September was a store building its own %APPDATA% path, and
        // that is checked at the source: only ProfileFolder may name the folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("ProfileFolder.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("SpecialFolder.ApplicationData", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(dir.FullName, f))
            .ToList();
        Assert.True(offenders.Count == 0,
            "these build their own profile path instead of using ProfileFolder.Root: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The override is honoured only as an absolute local path: a relative one
    /// would split the profile as the current directory moved, and a network one
    /// would put settings and autosave copies where others can read them.
    /// </summary>
    [Fact]
    public void OnlyAnAbsoluteLocalOverrideIsHonoured()
    {
        var previous = Environment.GetEnvironmentVariable("LIGHTBOX_PROFILE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_PROFILE_DIR", _dir);
            Assert.Equal(Path.GetFullPath(_dir), Lightbox.Core.ProfileFolder.Resolve());

            Environment.SetEnvironmentVariable("LIGHTBOX_PROFILE_DIR", @"relative\profile");
            Assert.DoesNotContain("relative", Lightbox.Core.ProfileFolder.Resolve());

            // Fully qualified, so it is refused for being a network path and not
            // merely for being relative.
            // Windows only: on Linux (CI) a backslash is not a separator, so the
            // string is merely relative — refused above, but not this rule's case.
            if (OperatingSystem.IsWindows())
            {
                Environment.SetEnvironmentVariable("LIGHTBOX_PROFILE_DIR", @"\\server\share\profile");
                Assert.True(Path.IsPathFullyQualified(@"\\server\share\profile"));
                Assert.DoesNotContain("server", Lightbox.Core.ProfileFolder.Resolve());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_PROFILE_DIR", previous);
        }
    }

    /// <summary>The log appends only to a log file, never to a document.</summary>
    [Fact]
    public void TheLogRefusesToAppendToADocument()
    {
        Assert.True(PerfLog.IsLogPath(@"C:\runs\a.jsonl"));
        Assert.False(PerfLog.IsLogPath(@"C:\work\drawing.lightbox.json"));
    }
}
