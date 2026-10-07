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
    /// <c>LIGHTBOX_PROFILE_DIR</c> moves every store at once — settings, the key
    /// file, brushes, symbols, logs — so a measuring run cannot touch the
    /// artist's own profile. The September harness proved that it could.
    /// </summary>
    [Fact]
    public void TheProfileOverrideMovesEveryStore()
    {
        var previous = Environment.GetEnvironmentVariable("LIGHTBOX_PROFILE_DIR");
        Environment.SetEnvironmentVariable("LIGHTBOX_PROFILE_DIR", _dir);
        try
        {
            Assert.Equal(Path.GetFullPath(_dir), Lightbox.Core.ProfileFolder.Root);
            Assert.StartsWith(Path.GetFullPath(_dir), Lightbox.Ai.ApiKeyProvider.SettingsPath);
            Assert.StartsWith(Path.GetFullPath(_dir), AutosaveService.AutosavePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_PROFILE_DIR", previous);
        }
        Assert.DoesNotContain(_dir, Lightbox.Core.ProfileFolder.Root);
    }
}
