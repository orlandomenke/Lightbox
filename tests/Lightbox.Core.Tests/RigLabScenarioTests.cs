using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The performance lab's rig scenarios are wired to things that exist (Q239).
/// </summary>
/// <remarks>
/// <para>
/// The lab runs on the owner's machine, by hand, and never in CI
/// (<c>perf/README.md</c>) — so a scenario can name a fixture nobody
/// generates or a span nobody writes and stay broken until the afternoon it
/// is needed. A scenario that measures a span the app does not emit is worse
/// than a missing one: it runs, reports the metrics it does find, and the
/// one that was the point is simply absent from the table.
/// </para>
/// <para>
/// This reads the repository rather than running the lab, the way
/// <c>PlanGateTests</c> reads the workflow: what it guards is wiring.
/// </para>
/// </remarks>
public class RigLabScenarioTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> ScenarioFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "perf", "scenarios"), "*.json").Order();

    private static JsonElement Scenario(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "perf", "scenarios", name + ".json"))).RootElement;

    /// <summary>The fixture presets <c>perf/lab.py</c> can generate, read from its <c>PRESETS</c> table.</summary>
    private static HashSet<string> Presets()
    {
        var lab = File.ReadAllText(Path.Combine(RepoRoot(), "perf", "lab.py")).ReplaceLineEndings("\n");
        var start = lab.IndexOf("PRESETS = {", StringComparison.Ordinal);
        Assert.True(start >= 0, "perf/lab.py no longer has a PRESETS table to read");
        var end = lab.IndexOf("\n}\n", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return Regex.Matches(lab[start..end], "^\\s+\"([a-z0-9-]+)\":", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
    }

    /// <summary>Every C# source file under <c>src</c>, joined — where a span's name has to appear to be written.</summary>
    private static string AppSource() => string.Join("\n",
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

    [Fact]
    public void EveryScenarioOpensAFixtureTheLabCanGenerate()
    {
        var presets = Presets();
        Assert.Contains("owner-shape", presets);
        var missing = ScenarioFiles()
            .Select(path => (Name: Path.GetFileNameWithoutExtension(path),
                Fixture: JsonDocument.Parse(File.ReadAllText(path)).RootElement.TryGetProperty("fixture", out var f)
                    ? f.GetString()! : "owner-shape"))
            .Where(s => !presets.Contains(s.Fixture))
            .Select(s => $"{s.Name} -> {s.Fixture}")
            .ToList();
        Assert.True(missing.Count == 0,
            "scenarios name fixtures perf/lab.py cannot generate: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("pose-drag")]
    [InlineData("pin-drag")]
    public void ARigScenarioMeasuresOnlySpansTheAppWrites(string name)
    {
        var scenario = Scenario(name);
        Assert.True(scenario.TryGetProperty("measures", out var measures),
            $"{name} does not say which spans it exists to measure");
        var source = AppSource();
        var unwritten = measures.EnumerateArray().Select(m => m.GetString()!)
            .Where(span => !source.Contains($"\"{span}\"", StringComparison.Ordinal))
            .ToList();
        Assert.True(unwritten.Count == 0,
            $"{name} measures spans nothing in src writes: {string.Join(", ", unwritten)}");
    }

    [Fact]
    public void TheTwoRigScenariosMakeTheSameGesture()
    {
        // They are one comparison — a bone and a pin swinging the same wrist
        // through the same arc — and that only holds while the drags match.
        static List<string> Drags(JsonElement scenario) => scenario.GetProperty("steps").EnumerateArray()
            .Where(s => s.GetProperty("do").GetString() == "drag")
            .Select(s => s.GetRawText()).ToList();

        var bone = Drags(Scenario("pose-drag"));
        var pin = Drags(Scenario("pin-drag"));
        Assert.NotEmpty(bone);
        Assert.Equal(bone, pin);
    }

    [Fact]
    public void ThePinScenarioUsesAShortcutTheConfigureWindowCanSee()
    {
        // Ctrl+Alt+K in the scenario is `armature.pinMode`. A pin mode wired
        // straight to a gesture works and is invisible to the shortcut
        // editor; registering it is also what makes the scenario's key real.
        var map = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lightbox.App", "Services", "ShortcutMap.cs"));
        Assert.Contains("\"armature.pinMode\"", map);
        Assert.Contains("ctrl+alt+k", Scenario("pin-drag").GetRawText());
    }
}
