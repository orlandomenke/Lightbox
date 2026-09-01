using System.Text.RegularExpressions;

namespace Lightbox.Core.Tests;

/// <summary>
/// B269 and B281: a full-solution test run has been seen executing hundreds
/// fewer App tests than exist and still reporting green — 2 889 of a discovered
/// 3 498 with exit 0, and, in the killed-host variant, `Passed!` printed one
/// line after xUnit's own `Catastrophic failure`. The guard is
/// <c>scripts/testcount.py</c>: after the CI run writes TRX logs, it compares
/// the names each assembly reported against what discovery finds on the same
/// binaries, and reds the job on any test that went missing.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class pins is the wiring, because the wiring is the part a
/// refactor loses silently.</b> The comparison itself is proven by
/// <c>testcount.py selftest</c>, which CI runs as its own step — but a workflow
/// edit that drops the trx logger or the verify step would leave both selftest
/// and comparison intact and the guard watching nothing. Reading the workflow
/// from a test is <c>CiDraftGateTests</c>' pattern, for the same reason it
/// works there: build.yml is the one place the promise is either kept or not.
/// </para>
/// <para>
/// This test is B269's evidence anchor, so deleting the guard from CI reopens
/// the bug — which is the ledger's contract working as designed.
/// </para>
/// </remarks>
public class CiTestCountTests
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

    [Fact]
    public void FullSuiteExecutionCountIsAsserted()
    {
        var yaml = File.ReadAllText(
                Path.Combine(RepoRoot(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");

        // The run has to leave the per-test record the guard reads. Asserted as
        // "the dotnet test line carries a trx logger" rather than an exact
        // command, so flags can move around it.
        Assert.True(
            Regex.IsMatch(yaml, @"dotnet test Lightbox\.sln.*--logger\s+""?trx"),
            "build.yml's test run writes no trx logs, so the executed-count guard "
            + "has nothing to read — a run that dies mid-suite would go back to "
            + "printing Passed! unchallenged (B269)");

        // And the guard has to actually run against them.
        Assert.True(
            yaml.Contains("testcount.py verify", StringComparison.Ordinal),
            "build.yml never runs scripts/testcount.py verify, so nothing compares "
            + "what the suite reported against what discovery finds (B269)");

        // The comparison's own selftest, so a broken guard cannot guard.
        Assert.True(
            yaml.Contains("testcount.py selftest", StringComparison.Ordinal),
            "build.yml never runs the count guard's selftest — branchstate and "
            + "bugs both run theirs, and a guard nobody exercises is decoration");

        Assert.True(
            File.Exists(Path.Combine(RepoRoot(), "scripts", "testcount.py")),
            "scripts/testcount.py is gone while build.yml still names it");
    }
}
