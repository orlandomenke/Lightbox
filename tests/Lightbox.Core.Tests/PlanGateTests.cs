using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// CI runs only the tests a change can reach, split across legs — and the rules
/// deciding that live in exactly one place.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-09-12: the serial suite costs 742 s, of which
/// <c>Lightbox.App.Tests</c> is 557 s. Q189 records why that makes selection
/// the weakest of the three available levers and sharding the strongest.
/// <c>scripts/testplan.py</c> owns both, and has its own selftest.
/// </para>
/// <para>
/// <b>What this class guards is the wiring, because the wiring is the part a
/// refactor loses silently</b> — the same argument <c>CiTestCountTests</c>
/// makes for the count guard. A second copy of the path rules in YAML would
/// pass every selftest the script has and still decide differently; a leg added
/// without its slice check would prove nothing; a test project nobody's plan
/// mentions would simply stop running, and nothing would be red.
/// </para>
/// <para>
/// The failure this exists to prevent has a shape and a precedent. PR #95: a
/// README-only change skipped the only test reading the README, and reported
/// <c>skipped</c> beside two green ticks. Nothing was wrong and nothing had
/// run. Every assertion below is a way that could happen again.
/// </para>
/// </remarks>
public class PlanGateTests(ITestOutputHelper output)
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

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");

    /// <summary>The workflow with its comments stripped — what the runner acts on.</summary>
    /// <remarks>
    /// Only for absence checks. A comment explaining why a pattern was removed
    /// necessarily names it, so a raw search for the retired thing finds the
    /// explanation and reports it as the thing being explained.
    /// </remarks>
    private static string WorkflowDirectives() =>
        string.Join("\n", Workflow().Split('\n')
            .Select(line =>
            {
                var hash = line.IndexOf('#');
                return hash < 0 ? line : line[..hash];
            }));

    private static (int Code, string Said) Plan(string arguments)
    {
        var info = new ProcessStartInfo("python3", $"scripts/testplan.py {arguments}")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info);
        Assert.NotNull(process);
        var said = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        return (process.ExitCode, said);
    }

    // -----------------------------------------------------------------------
    // One set of rules, in the place that has a selftest
    // -----------------------------------------------------------------------

    [Fact]
    public void TheWorkflowAsksThePlannerRatherThanClassifyingPathsItself()
    {
        var directives = WorkflowDirectives();
        Assert.Contains("testplan.py plan", directives);

        // The retired shape: a grep of path patterns living in YAML. It had no
        // selftest, no way to ask it a question, and it was a second opinion
        // about what counts as documentation kept where nobody looks.
        Assert.DoesNotContain("docs/|", directives);
        Assert.DoesNotContain("grep -qvE", directives);
        output.WriteLine("build.yml classifies no paths of its own");
    }

    [Fact]
    public void TheMatrixComesFromThePlan()
    {
        var directives = WorkflowDirectives();
        Assert.Matches(new Regex(@"matrix:\s*\n\s*leg:\s*\$\{\{\s*fromJSON\(needs\.changes\.outputs\.plan\)"), directives);

        // Each leg has to work out its own slice, because the plan deliberately
        // does not carry one: the complement filter alone is ~25 KB.
        Assert.Contains("testplan.py filter", directives);
        output.WriteLine("the test matrix is the planner's output");
    }

    [Fact]
    public void ThePlannerAndTheCountGuardAreBothExercisedByCi()
    {
        var directives = WorkflowDirectives();

        // A planner with no selftest is the one component here that can fail by
        // being too quiet.
        Assert.Contains("testplan.py selftest", directives);

        // And the half a selftest cannot cover: that the rules still describe
        // the tests as they are now.
        Assert.Contains("testplan.py audit", directives);
        output.WriteLine("CI runs both the planner's selftest and its audit");
    }

    // -----------------------------------------------------------------------
    // Nothing may become unreachable
    // -----------------------------------------------------------------------

    [Fact]
    public void EveryTestProjectIsSomethingThePlanCanSelect()
    {
        var (code, said) = Plan("plan --json");
        Assert.True(code == 0, said);

        // Test legs only. A full plan also carries `build` legs for the projects
        // no test reaches — `tools/Lightbox.Bench` — and those are not test
        // projects and have no csproj under tests/.
        var planned = JsonDocument.Parse(said).RootElement.EnumerateArray()
            .Where(leg => leg.GetProperty("kind").GetString() == "test")
            .Select(leg => leg.GetProperty("project").GetString()!)
            .ToHashSet();

        var onDisk = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "tests"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet();

        output.WriteLine($"on disk: {string.Join(", ", onDisk.OrderBy(n => n))}");
        output.WriteLine($"planned: {string.Join(", ", planned.OrderBy(n => n))}");

        Assert.True(onDisk.SetEquals(planned),
            "a test project exists that a full plan never names, so nothing would ever run it: "
            + string.Join(", ", onDisk.Except(planned))
            + " — every csproj under tests/ has to be reachable from scripts/testplan.py");
    }

    /// <summary>
    /// A project no test suite reaches is still compiled by CI.
    /// </summary>
    /// <remarks>
    /// <c>dotnet test Lightbox.sln</c> built the whole solution as a side
    /// effect, so a project with no tests was kept compiling by the suite for
    /// free. <c>dotnet test &lt;csproj&gt;</c> builds only that project's closure,
    /// so per-project legs lose that: anything outside every test's closure
    /// stops being built at all, and a compile error in it reaches main with
    /// every check green.
    /// <para>
    /// <c>tools/Lightbox.Bench</c> is that project today. It is <em>derived</em>
    /// rather than named, so the next one is covered the day it is added — and
    /// this asserts the derivation still finds it, because a plan that quietly
    /// stopped emitting compile legs would look exactly like a faster CI.
    /// </para>
    /// </remarks>
    [Fact]
    public void AProjectNoTestReachesIsStillCompiled()
    {
        var (code, said) = Plan("plan --json");
        Assert.True(code == 0, said);

        var compiled = JsonDocument.Parse(said).RootElement.EnumerateArray()
            .Where(leg => leg.GetProperty("kind").GetString() == "build")
            .Select(leg => leg.GetProperty("project").GetString()!)
            .ToHashSet();
        output.WriteLine($"compile-only legs: {string.Join(", ", compiled)}");

        var solution = File.ReadAllText(Path.Combine(RepoRoot(), "Lightbox.sln"));
        var inSolution = Regex.Matches(solution, @"""([A-Za-z0-9_.]+)"",\s*""[^""]+\.csproj""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var tested = JsonDocument.Parse(said).RootElement.EnumerateArray()
            .Where(leg => leg.GetProperty("kind").GetString() == "test")
            .Select(leg => leg.GetProperty("project").GetString()!)
            .ToHashSet();

        // Whatever is in the solution is either run by a test leg, pulled in as
        // a reference of one, or compiled on its own. Nothing may be in none of
        // the three — which is what this asserts by naming the one that is not
        // covered by a test today.
        Assert.True(inSolution.Contains("Lightbox.Bench"),
            "Lightbox.Bench left the solution — if it was deleted, delete this test with it");
        Assert.True(compiled.Contains("Lightbox.Bench"),
            "a full plan no longer compiles Lightbox.Bench, which no test suite references. "
            + "`dotnet test Lightbox.sln` used to build it as a side effect and per-project legs "
            + "do not, so a compile error in it would reach main with every check green.");
        Assert.DoesNotContain("Lightbox.Bench", tested);
    }

    /// <summary>
    /// Every leg the plan emits can produce the filter the workflow will ask it for.
    /// </summary>
    /// <remarks>
    /// The workflow runs <c>testplan.py filter &lt;project&gt; &lt;shard&gt;</c> in each leg. A
    /// plan naming a leg that command refuses is a red run at best, and this is
    /// cheaper than finding out on a runner.
    /// </remarks>
    [Fact]
    public void EveryLegInThePlanCanAskForItsOwnSlice()
    {
        var (code, said) = Plan("plan --json");
        Assert.True(code == 0, said);

        foreach (var leg in JsonDocument.Parse(said).RootElement.EnumerateArray())
        {
            var project = leg.GetProperty("project").GetString()!;
            var shard = leg.GetProperty("shard").GetInt32();
            var (filterCode, filter) = Plan($"filter {project} {shard}");
            Assert.True(filterCode == 0, $"{project} shard {shard}: {filter}");
            output.WriteLine($"{leg.GetProperty("name").GetString()}: {filter.Trim().Length} chars of filter");
        }
    }

    // -----------------------------------------------------------------------
    // The fan-in, which is what a branch rule should require
    // -----------------------------------------------------------------------

    /// <summary>
    /// A matrix reports one check per leg, and those names move with the shard
    /// counts — so there has to be one check that does not.
    /// </summary>
    /// <remarks>
    /// <c>always()</c> is the load-bearing half. Without it, a failed or skipped
    /// <c>test</c> would skip this job too, and a skipped required check reads
    /// as a green tick nobody earned — which is PR #95's failure wearing the
    /// branch-protection hat.
    /// </remarks>
    [Fact]
    public void OneCheckSpeaksForTheWholeMatrix()
    {
        var yaml = Workflow();
        var jobsAt = yaml.IndexOf("\njobs:\n", StringComparison.Ordinal);
        Assert.True(jobsAt >= 0);
        var body = yaml[(jobsAt + "\njobs:\n".Length)..];

        var starts = Regex.Matches(body, @"^  (?<name>[a-z][a-z0-9-]*):[ \t]*$", RegexOptions.Multiline)
            .Select(m => (Name: m.Groups["name"].Value, m.Index))
            .ToList();
        Assert.Contains(starts, s => s.Name == "suite");

        var index = starts.FindIndex(s => s.Name == "suite");
        var end = index + 1 < starts.Count ? starts[index + 1].Index : body.Length;
        var block = body[starts[index].Index..end];
        output.WriteLine(block);

        Assert.Contains("needs:", block);
        Assert.Contains("test", block);
        Assert.True(block.Contains("always()", StringComparison.Ordinal),
            "the fan-in job does not use always(), so a failed or skipped matrix would skip it "
            + "too — and a skipped required check is a tick nobody earned");

        // The subtlest way this job could lie: a failed planner leaves its
        // outputs empty, which reads exactly like "this change reaches no
        // tests". Without checking the planner's own result, the one check a
        // branch rule requires would go green precisely when nothing was
        // planned at all.
        // Followed from the value to the comparison, rather than searched for by
        // name. Two weaker versions of this assertion were written first and a
        // mutant walked through both: searching for `needs.changes.result`
        // passes when the value is read into a variable and never tested, and
        // searching for `!= "success"` passes on the *matrix* result's
        // comparison, which is a different check entirely. So: find the shell
        // variable the planner's result is assigned to, then require that
        // variable to be the one compared.
        var assigned = Regex.Match(block, @"(?<var>[A-Z_]+)='\$\{\{\s*needs\.changes\.result\s*\}\}'");
        Assert.True(assigned.Success,
            "the fan-in job never reads needs.changes.result, so a failed `changes` job would "
            + "leave empty outputs that read as 'nothing to test'");

        var planner = assigned.Groups["var"].Value;
        output.WriteLine($"planner result is held in ${planner}");
        Assert.True(
            Regex.IsMatch(block, $@"""\${planner}""\s*!=\s*""success"""),
            $"the fan-in job reads the planner's result into ${planner} and never tests it, so a "
            + "failed `changes` job would leave empty outputs that read as 'nothing to test' and "
            + "the required check would pass having run nothing");
    }

    // -----------------------------------------------------------------------
    // The rules themselves still hold
    // -----------------------------------------------------------------------

    /// <summary>
    /// The planner's own selftest and audit, run from the suite as well as from CI.
    /// </summary>
    /// <remarks>
    /// CI runs these as their own steps, which is where they belong — but this
    /// repository's gates all rest on "the four suites are green", and a rule
    /// set that only a workflow checks is one a local run cannot see. Both are
    /// a second or two.
    /// </remarks>
    [Fact]
    public void ThePlannersOwnRulesPass()
    {
        var (code, said) = Plan("selftest");
        output.WriteLine(said.TrimEnd());
        Assert.Equal(0, code);
    }

    [Fact]
    public void EveryRepoPathTheTestsReadIsAccountedFor()
    {
        var (code, said) = Plan("audit");
        output.WriteLine(said.TrimEnd());
        Assert.True(code == 0,
            "a test reads a repository path that the plan classifies as something CI would "
            + "skip. That is PR #95 again: the change most likely to break the assertion is "
            + "the one change that would never run it.\n" + said);
    }

    /// <summary>
    /// The default for an unclassified path is everything, and it stays that way.
    /// </summary>
    /// <remarks>
    /// This is the property the whole scheme is safe under. A new kind of file —
    /// a lock file, a config, a generator's output — must run the suite until
    /// somebody decides otherwise, because the alternative is a file that
    /// silently tests nothing.
    /// </remarks>
    [Fact]
    public void APathNobodyWroteARuleForRunsEverything()
    {
        var (code, said) = Plan("explain some/newly/invented/thing.toml");
        Assert.True(code == 0, said);
        output.WriteLine(said.TrimEnd());

        Assert.Contains("NO RULE", said);
        foreach (var project in new[]
                 {
                     "Lightbox.Core.Tests", "Lightbox.Raster.Tests",
                     "Lightbox.Ai.Tests", "Lightbox.App.Tests",
                 })
        {
            Assert.True(said.Contains(project, StringComparison.Ordinal),
                $"an unclassified path did not select {project} — the fail-safe default is gone");
        }
    }

    /// <summary>
    /// A change under <c>src/Lightbox.Core</c> still runs all four suites.
    /// </summary>
    /// <remarks>
    /// Everything references Core, so this is the case where selection must buy
    /// nothing. It is asserted because a selector that gets this wrong looks
    /// like a much faster CI.
    /// </remarks>
    [Fact]
    public void AChangeToTheCoreModelStillRunsEverything()
    {
        var (code, said) = Plan("explain src/Lightbox.Core/Documents/Doc.cs");
        Assert.True(code == 0, said);
        output.WriteLine(said.TrimEnd());

        foreach (var project in new[]
                 {
                     "Lightbox.Core.Tests", "Lightbox.Raster.Tests",
                     "Lightbox.Ai.Tests", "Lightbox.App.Tests",
                 })
        {
            Assert.True(said.Contains(project, StringComparison.Ordinal),
                $"a change to the document model did not select {project}, and every project "
                + "in this solution references Lightbox.Core");
        }
    }
}
