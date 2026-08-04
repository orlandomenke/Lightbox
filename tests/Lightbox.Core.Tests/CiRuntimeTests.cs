using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// CI must declare the runtimes it needs, not inherit them from the runner image.
/// </summary>
/// <remarks>
/// <para>
/// B53. <c>build.yml</c> asked <c>setup-dotnet</c> for <c>10.0.x</c> and nothing else, and
/// passed — because <c>ubuntu-latest</c> preinstalls .NET 8. The 10.0 SDK is genuinely needed
/// to <em>build</em> (Avalonia 12's source generators want a newer Roslyn than the .NET 8 SDK
/// ships), but a <c>net8.0</c> test assembly will not run on a 10.0 runtime: RollForward
/// defaults to <c>Minor</c>, which does not cross a major version, and nothing here sets it.
/// So the whole solution compiled and not one test ran, on any machine where the runtime was
/// not already there.
/// </para>
/// <para>
/// <b>Why a test rather than a note.</b> A dependency satisfied by accident is invisible
/// exactly until it is not — GitHub drops .NET 8 from the image after it leaves support in
/// November 2026, at which point the workflow fails for a reason its own YAML never mentions.
/// This asserts the declaration against the TFM that needs it, so the two cannot drift, and
/// it also tells you when the requirement has <em>gone</em>: move the solution to net10.0 and
/// it says the 8.0 line can come out.
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

    [Fact]
    public void TheEightPointZeroRuntimeIsStillActuallyRequired()
    {
        // Self-clearing. The whole entry disappears if the solution moves to net10.0 — one
        // runtime, and it is the SDK's own — so this says when the extra line has become
        // clutter rather than leaving it there for someone to wonder about.
        var tfm = AppTargetFramework();

        Assert.True(
            tfm == "net8.0",
            $"the app now targets {tfm}. If that is 10.0, the 8.0.x line in build.yml is no "
            + "longer needed and B53's reasoning is spent — remove both, and this test.");
    }
}
