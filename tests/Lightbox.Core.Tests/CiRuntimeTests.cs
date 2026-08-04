using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// CI must declare the runtimes it needs, not inherit them from the runner image.
/// </summary>
/// <remarks>
/// <para>
/// B53, now retired by the net10.0 upgrade rather than merely satisfied. <c>build.yml</c> once
/// asked <c>setup-dotnet</c> for <c>10.0.x</c> and nothing else, and passed — because
/// <c>ubuntu-latest</c> preinstalls .NET 8. The 10.0 SDK was genuinely needed to <em>build</em>
/// (Avalonia 12's source generators want a newer Roslyn than the .NET 8 SDK ships), but a
/// <c>net8.0</c> test assembly will not run on a 10.0 runtime: RollForward defaults to
/// <c>Minor</c>, which does not cross a major version. So the whole solution compiled and not
/// one test ran, on any machine where the runtime was not already there.
/// </para>
/// <para>
/// <b>Why the tests survive the fix.</b> The solution is net10.0 throughout now, so the SDK
/// carries its own runtime and the two-runtime problem is gone by construction. What is still
/// worth asserting is that the declaration tracks the TFM: these derive the runtime line from
/// <c>Lightbox.App.csproj</c> rather than hard-coding it, so a future TFM move that leaves
/// <c>build.yml</c> behind fails here instead of on a runner.
/// </para>
/// </remarks>
public class CiRuntimeTests(ITestOutputHelper output)
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
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "build.yml"));

    /// <summary>
    /// The workflow with its comments stripped — what the runner actually acts on.
    /// </summary>
    /// <remarks>
    /// Only for asserting that something is <em>absent</em>. A comment explaining why a
    /// version was removed necessarily names it, so a raw-text search for the retired line
    /// finds the explanation and reports it as the thing it is explaining. Presence checks
    /// can stay on the raw text; absence checks cannot.
    /// </remarks>
    private static string WorkflowDirectives() =>
        string.Join(
            "\n",
            Workflow()
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line =>
                {
                    var hash = line.IndexOf('#');
                    return hash < 0 ? line : line[..hash];
                }));

    /// <summary>The framework the app and its tests are built for, read from the project.</summary>
    private static string AppTargetFramework()
    {
        var csproj = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lightbox.App", "Lightbox.App.csproj"));
        var m = Regex.Match(csproj, @"<TargetFramework>(?<tfm>net[\d.]+)</TargetFramework>");
        Assert.True(m.Success, "could not read a TargetFramework from Lightbox.App.csproj");
        return m.Groups["tfm"].Value;
    }

    /// <summary>"net8.0" → "8.0.x", which is what setup-dotnet wants.</summary>
    private static string RuntimeLineFor(string tfm) => tfm.Replace("net", "") + ".x";

    [Fact]
    public void TheJobThatRunsTestsAsksForTheRuntimeThoseTestsNeed()
    {
        var tfm = AppTargetFramework();
        var wanted = RuntimeLineFor(tfm);
        var yaml = Workflow();

        // The `test` job is the only one that executes a built assembly; publish merely
        // compiles, and a self-contained publish carries its runtime as a NuGet pack rather
        // than needing one installed. So the requirement belongs to this job specifically.
        var testJob = yaml[yaml.IndexOf("  test:", StringComparison.Ordinal)..];
        testJob = testJob[..testJob.IndexOf("\n  changes:", StringComparison.Ordinal)];

        output.WriteLine($"app targets {tfm}, so CI must offer {wanted}");
        Assert.Contains("dotnet test", testJob);
        Assert.True(
            testJob.Contains(wanted, StringComparison.Ordinal),
            $"the test job runs {tfm} assemblies but never asks setup-dotnet for {wanted} — "
            + "it is relying on the runner image having it, which is a dependency that "
            + "disappears without warning");
    }

    [Fact]
    public void TheSdkIsStillNamedToo()
    {
        // The other half, and it must not be lost while fixing the first: the 10.0 SDK is what
        // compiles this at all. Naming only the 8.0 runtime would trade one broken CI for
        // another, and the failure would be a source-generator error rather than a missing
        // framework — less obviously about the SDK, not more.
        Assert.Contains("10.0.x", Workflow());
    }

    /// <summary>
    /// The 8.0 runtime is gone from <c>build.yml</c>, and stays gone.
    /// </summary>
    /// <remarks>
    /// This replaces <c>TheEightPointZeroRuntimeIsStillActuallyRequired</c>, which asserted
    /// the opposite and was written to fail the moment the solution moved to net10.0 — it
    /// did exactly that, and told whoever read it to remove the extra line and the test with
    /// it. What is worth keeping is the other direction: a stray <c>8.0.x</c> re-added to a
    /// solution that no longer has a net8.0 assembly anywhere installs a runtime nothing
    /// runs on, which is slower CI that looks like nothing at all.
    /// </remarks>
    [Fact]
    public void TheRetiredEightPointZeroRuntimeHasNotComeBack()
    {
        var tfm = AppTargetFramework();
        Assert.Equal("net10.0", tfm);
        Assert.DoesNotContain("8.0.x", WorkflowDirectives());
    }
}
