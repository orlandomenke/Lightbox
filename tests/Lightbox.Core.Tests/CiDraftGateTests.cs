using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// A draft pull request builds nothing, and the rule holds for every job rather
/// than the ones somebody remembered.
/// </summary>
/// <remarks>
/// <para>
/// <c>pull_request</c> fires on every push to an open PR, which is the expensive
/// trigger: a branch merged with main twice and re-indexed costs a full suite
/// each time. The answer is to draft the PR while that is happening — but the
/// mechanism is easy to half-implement, and both halves fail quietly.
/// </para>
/// <para>
/// <b>Forgetting a job</b> means it builds on drafts while its neighbours do not,
/// so the saving silently stops applying the next time a job is added. <b>Forgetting
/// <c>ready_for_review</c></b> is worse: the default event types are
/// <c>opened</c>, <c>synchronize</c> and <c>reopened</c>, none of which fire when
/// a draft is marked ready — so a PR opened as a draft and then readied would
/// never build at all, and would sit with a green tick it never earned.
/// </para>
/// <para>
/// Asserted against the workflow text because that is the only artefact; there is
/// no way to run Actions' expression evaluator here. These are cheap and they
/// catch the two mistakes that are otherwise invisible until a build that should
/// have happened did not.
/// </para>
/// </remarks>
public class CiDraftGateTests(ITestOutputHelper output)
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

    private static string WorkflowPath() =>
        Path.Combine(RepoRoot(), ".github", "workflows", "build.yml");

    /// <summary>Every job in build.yml, as name → its block of YAML.</summary>
    private static Dictionary<string, string> Jobs()
    {
        var yaml = File.ReadAllText(WorkflowPath()).ReplaceLineEndings("\n");
        var jobsAt = yaml.IndexOf("\njobs:\n", StringComparison.Ordinal);
        Assert.True(jobsAt >= 0, "build.yml has no jobs: block");
        var body = yaml[(jobsAt + "\njobs:\n".Length)..];

        // Job names are the only keys at exactly two spaces of indent, and a
        // comment line can sit at that indent too — hence the letter check.
        var starts = Regex.Matches(body, @"^  (?<name>[a-z][a-z0-9-]*):[ \t]*$", RegexOptions.Multiline)
            .Select(m => (m.Groups["name"].Value, m.Index))
            .ToList();
        Assert.NotEmpty(starts);

        var jobs = new Dictionary<string, string>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : body.Length;
            jobs[starts[i].Item1] = body[starts[i].Index..end];
        }
        return jobs;
    }

    [Fact]
    public void EveryJobIsSwitchedOffForADraftPullRequest()
    {
        var jobs = Jobs();
        var missing = new List<string>();

        foreach (var (name, block) in jobs)
        {
            // Both halves matter. `draft == false` alone would compare an empty
            // string on a push event and skip the merge build on main, so the
            // event-name guard has to be there too.
            var hasDraftCheck = block.Contains("github.event.pull_request.draft == false", StringComparison.Ordinal);
            var hasEventGuard = block.Contains("github.event_name != 'pull_request'", StringComparison.Ordinal);
            output.WriteLine($"{name,-18} draft-check {hasDraftCheck,-5} event-guard {hasEventGuard}");
            if (!hasDraftCheck || !hasEventGuard) missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            "these jobs would still build on a draft pull request: " + string.Join(", ", missing)
            + " — add `if: github.event_name != 'pull_request' "
            + "|| github.event.pull_request.draft == false`, and see the `on:` block for why "
            + "the first half cannot be dropped");
    }

    /// <summary>
    /// The trap: without this type, marking a draft ready fires nothing and the
    /// PR is mergeable having never been built.
    /// </summary>
    [Fact]
    public void MarkingADraftReadyTriggersABuild()
    {
        var yaml = File.ReadAllText(WorkflowPath()).ReplaceLineEndings("\n");
        var prBlock = Regex.Match(yaml, @"^  pull_request:\s*$(?<body>(?:\n(?:    .*)?)*)", RegexOptions.Multiline);
        Assert.True(prBlock.Success, "build.yml has no pull_request trigger");

        var body = prBlock.Groups["body"].Value;
        output.WriteLine("pull_request trigger:" + body);

        Assert.True(
            body.Contains("ready_for_review", StringComparison.Ordinal),
            "the pull_request trigger lists explicit types but not ready_for_review, so a PR "
            + "opened as a draft and later marked ready would never build — the default types "
            + "(opened, synchronize, reopened) do not include it");
    }

    /// <summary>
    /// A merge to the default branch must still build, which is the thing the
    /// draft gate is most likely to break by accident.
    /// </summary>
    [Fact]
    public void AMergeToMainStillBuilds()
    {
        var yaml = File.ReadAllText(WorkflowPath()).ReplaceLineEndings("\n");
        var pushTrigger = Regex.Match(
            yaml, @"^  push:\s*\n    branches: \[(?<branches>[^\]]*)\]", RegexOptions.Multiline);
        Assert.True(pushTrigger.Success, "build.yml no longer builds on a push at all");
        output.WriteLine($"push branches: {pushTrigger.Groups["branches"].Value}");
        Assert.Contains("main", pushTrigger.Groups["branches"].Value);

        // Nothing may gate a job on being a pull_request, or a push to main would
        // be excluded along with the drafts.
        foreach (var (name, block) in Jobs())
        {
            Assert.DoesNotContain("github.event_name == 'pull_request'", block);
            output.WriteLine($"{name,-18} does not require a pull_request event");
        }
    }
}
