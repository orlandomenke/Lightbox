using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The application's own version number — the one on the download, in the
/// executable's properties and in every crash report.
/// </summary>
/// <remarks>
/// <para>
/// Not to be confused with <c>VersionHistoryManager</c> and
/// <c>VersioningTests</c>, which version a <em>document's assets</em>. This is
/// the build number, and it lives in exactly one place:
/// <c>Directory.Build.props</c>. <c>release.yml</c> reads it from there and
/// hands it to <c>dotnet publish</c>; a tag overrides it with its own name.
/// </para>
/// <para>
/// <b>What these guard.</b> Every project in the solution had no version at
/// all, so the SDK stamped its default and every build of every commit said
/// <c>1.0.0</c>. Two ways that comes back: the property is dropped or renamed,
/// and the binary silently goes back to lying; or a publish step is added to
/// the workflow and nobody threads the version into it, so one of the two
/// executables in the bundle disagrees with the other.
/// </para>
/// </remarks>
public class ReleaseVersionTests(ITestOutputHelper output)
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

    private static string BuildProps() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));

    private static string ReleaseWorkflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"))
            .ReplaceLineEndings("\n");

    /// <summary>
    /// The workflow's steps, comments stripped — what the runner actually acts on.
    /// </summary>
    /// <remarks>
    /// The comments in this file discuss the commands they sit above, so a
    /// search of the raw text finds the explanation of a step as well as the
    /// step. That is not hypothetical: the first version of
    /// <see cref="EveryPublishInTheReleaseWorkflowIsStamped"/> counted three
    /// publishes in a workflow that has two, because a comment mentions the
    /// command by name.
    /// </remarks>
    private static List<string> ReleaseSteps()
    {
        var directives = string.Join("\n", ReleaseWorkflow().Split('\n').Select(line =>
        {
            // Only whole-line comments: `${GITHUB_REF_NAME#v}` has a `#` in it
            // and cutting there would silently rewrite the script.
            var trimmed = line.TrimStart();
            return trimmed.StartsWith('#') ? "" : line;
        }));

        return Regex
            .Split(directives, @"\n(?=\s*- (name|uses):)")
            .Where(s => s.Trim().Length > 0)
            .ToList();
    }

    /// <summary>The declared base version, or null if nothing declares one.</summary>
    private static string? DeclaredPrefix() =>
        Regex.Match(BuildProps(), @"<VersionPrefix>(?<v>[^<]+)</VersionPrefix>") is { Success: true } m
            ? m.Groups["v"].Value
            : null;

    [Fact]
    public void TheSolutionDeclaresOneVersionAndItIsSemver()
    {
        var prefix = DeclaredPrefix();
        output.WriteLine($"Directory.Build.props declares {prefix ?? "<nothing>"}");

        Assert.NotNull(prefix);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), prefix!);

        // One declaration, not one per project. A second copy anywhere is a
        // version that can drift from the one the workflow reads.
        var strays = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "tests"), "*.csproj", SearchOption.AllDirectories))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<Version(Prefix)?>"))
            .ToList();
        Assert.True(
            strays.Count == 0,
            "these projects declare their own version, which can drift from the one "
            + $"release.yml publishes with: {string.Join(", ", strays)}");
    }

    /// <summary>
    /// The declared number actually reaches the compiled assembly.
    /// </summary>
    /// <remarks>
    /// The half a YAML check cannot reach. <c>Directory.Build.props</c> only
    /// applies to projects underneath it, so a project moved out of the tree —
    /// or a property misspelled — leaves the default in place and nothing else
    /// notices.
    /// </remarks>
    [Fact]
    public void TheDeclaredVersionIsWhatTheBinaryCarries()
    {
        var stamped = typeof(Lightbox.Core.Documents.Doc).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        output.WriteLine($"Lightbox.Core is stamped {stamped ?? "<nothing>"}");

        Assert.NotNull(stamped);
        Assert.StartsWith(DeclaredPrefix()!, stamped!, StringComparison.Ordinal);

        // 1.0.0 is the SDK's default and the thing this exists to have replaced.
        Assert.False(
            stamped!.StartsWith("1.0.0", StringComparison.Ordinal),
            "the assembly still carries the SDK's default version — Directory.Build.props "
            + "is not reaching it");
    }

    [Fact]
    public void EveryPublishInTheReleaseWorkflowIsStamped()
    {
        var publishes = ReleaseSteps().Where(s => s.Contains("dotnet publish")).ToList();

        output.WriteLine($"{publishes.Count} publish step(s) in release.yml");
        Assert.Equal(2, publishes.Count); // the app and the MCP server beside it

        foreach (var step in publishes)
        {
            Assert.Contains("-p:Version=", step);
            // Read from the one derivation step rather than written out again:
            // a literal here would be a version that only moves when somebody
            // remembers this file.
            Assert.Contains("steps.version.outputs.version", step);
        }
    }

    /// <summary>
    /// The file you download and the executable inside it say the same number.
    /// </summary>
    /// <remarks>
    /// The bundle used to be named from the tag or from branch-and-sha, on its
    /// own axis entirely — which was fine while the executable carried no
    /// version to contradict. Now that it does, two independently-derived names
    /// is exactly the bug worth preventing.
    /// </remarks>
    [Fact]
    public void TheBundleIsNamedFromTheSameVersionItWasBuiltWith()
    {
        var naming = ReleaseSteps().SingleOrDefault(s => s.Contains("zip=Lightbox-win-x64-"));
        Assert.True(naming is not null, "release.yml no longer has a step that names the bundle");

        Assert.Contains("steps.version.outputs.version", naming!);
    }

    /// <summary>
    /// A build nobody tagged is a prerelease of the version it precedes.
    /// </summary>
    /// <remarks>
    /// Without a suffix, a bundle built off a branch would claim to be the
    /// release itself — so a bug report naming "0.1.0" could mean the release or
    /// any of the untagged builds before it, which is the exact ambiguity the
    /// whole change exists to remove.
    /// </remarks>
    [Fact]
    public void AnUntaggedBuildSortsBeforeTheReleaseItPrecedes()
    {
        var yaml = ReleaseWorkflow();
        Assert.Contains("-alpha.${GITHUB_RUN_NUMBER}", yaml);
        // And a tag drops the `v`: `v0.2.0` is a ref name, `0.2.0` is a version.
        Assert.Contains("${GITHUB_REF_NAME#v}", yaml);
    }
}
