using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The four files that are already too big may shrink and may not grow. New work
/// goes into a partial or a collaborator rather than onto the end of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a ratchet and not a plan.</b> <c>docs/DESIGN-mainviewmodel-decomposition.md</c>
/// measured <c>MainViewModel.cs</c> at 10,098 lines and proposed extracting eight
/// leaves totalling about 2,000. By the commit that merged that document the file
/// was 12,001 lines; four days later it was 13,110, across 94 commits. The plan was
/// sound and it was losing a race — the file gained more lines while the document
/// sat unstarted than the document proposed to remove. An extraction that costs a
/// branch and a full suite run per leaf cannot outrun feature work landing in the
/// same file, so the arithmetic has to be fixed from the other side first.
/// </para>
/// <para>
/// So this is deliberately the cheapest possible mechanism: no judgement about
/// whether a section belongs somewhere, no architecture, just a line count that is
/// not allowed to go up. It makes the extraction's arithmetic possible instead of
/// doing any of the extraction.
/// </para>
/// <para>
/// <b>Lowering a budget is the point.</b> When an extraction lands, the number here
/// comes down with it in the same commit, and the file can never climb back. That is
/// what makes this a ratchet rather than a cap — a cap with slack in it is a licence
/// to grow up to the slack.
/// </para>
/// <para>
/// <b>Raising one is a decision.</b> There is no escape hatch on purpose. If a
/// feature genuinely cannot land without growing one of these files, the honest move
/// is to edit the number here and say why in the commit message, which is a visible
/// line in a diff rather than an environment variable nobody reads. That is the same
/// reasoning as <c>LIGHTBOX_PUSH_TO_MAIN</c> — make the bypass cost a sentence.
/// </para>
/// <para>
/// <b>Why line counts, given the file is shallow.</b> Re-deriving the design
/// document's own coupling analysis against the current 13,110-line file reproduces
/// it almost exactly: 54% of fields are touched by exactly one section (the document
/// measured 54%), nine fields cross five or more sections (it measured nine), and the
/// shape tool is still the widest at 33 (it measured 32). The file is not getting
/// more tangled as it grows — it is getting longer at constant shallowness. Length is
/// therefore the honest thing to measure, and it is the thing that makes the file
/// unnavigable.
/// </para>
/// <para>
/// <b><c>MainWindow.axaml</c> is here despite being XAML.</b> <c>HOTSPOTS.md</c> puts
/// it at the top of the risk table — 4,188 lines, 53 commits, and no test file that
/// exercises it directly. It is the largest unguarded surface in the repository, so
/// the least it can do is stop growing.
/// </para>
/// </remarks>
public class MonolithRatchetTests(ITestOutputHelper output)
{
    /// <summary>
    /// Line ceilings, measured 2026-08-13. Lower these when an extraction lands;
    /// raising one needs a reason in the commit message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MainWindow.axaml.cs</c> came down from 5,544 to 429 when the view was split
    /// into fifteen partials, which is the ratchet behaving as designed: the budget
    /// moves with the extraction that earned it, in the same commit, and the file can
    /// never climb back. The partials are not listed here — the largest is 747 lines
    /// and none is near the size that makes a file unreadable. A budget belongs here
    /// when a file is already too big, not as a cap on every file in the project.
    /// </para>
    /// <para>
    /// <b><c>MainViewModel.cs</c> went up once, 13,110 → 13,141, and the precedent is
    /// worth stating exactly.</b> Naming Tier 0 re-marked the live-paint engine out
    /// from under a heading reading "the shape tool" and gave the render core a marker
    /// of its own. The motion itself <i>shrank</i> the file by five lines; the 38 lines
    /// of comment explaining why the old map was wrong is what took it over.
    /// </para>
    /// <para>
    /// It was raised rather than absorbed by trimming that comment, deliberately. The
    /// budget exists to stop feature code accumulating in a file nobody can read, not
    /// to price the documentation that makes it readable — and padding prose down to
    /// fit a number set the day before is the "fixing badly to keep a number down"
    /// failure the project warns about, dressed up as discipline. Raising it costs one
    /// visible line in a diff, which is the whole design.
    /// </para>
    /// <para>
    /// That is the only legitimate reason to raise one: the file got <i>more</i>
    /// legible and slightly longer. "A feature needed the room" is not on the list —
    /// that is what a partial or a collaborator is for.
    /// </para>
    /// <para>
    /// It came back down the next branch, 13,141 → 12,919, when <c>LivePaintSession</c>
    /// took 22 fields out of the class — which is the ratchet's point. A budget that goes
    /// up once for documentation and down by 222 for an extraction is doing its job; one
    /// that only ever goes up is a comment.
    /// </para>
    /// </remarks>
    public static readonly (string Path, int Max)[] Budgets =
    [
        ("src/Lightbox.App/ViewModels/MainViewModel.cs", 12878),
        ("src/Lightbox.App/Rendering/CanvasControl.cs", 5078),
        ("src/Lightbox.App/Views/MainWindow.axaml", 4188),
        ("src/Lightbox.App/Views/MainWindow.axaml.cs", 429),
    ];

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

    public static TheoryData<string, int> BudgetCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var (path, max) in Budgets)
        {
            data.Add(path, max);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(BudgetCases))]
    public void AFileThatIsAlreadyTooBigDoesNotGrow(string relative, int max)
    {
        var full = Path.Combine(RepoRoot(), relative);
        Assert.True(File.Exists(full), $"{relative} has moved or been renamed — update the budget list.");

        var actual = File.ReadAllLines(full).Length;
        output.WriteLine($"{relative}: {actual} lines, budget {max} ({actual - max:+#;-#;0})");

        Assert.True(
            actual <= max,
            $"""
            {relative} is {actual} lines, over its budget of {max} by {actual - max}.

            This file is one of the four the project has decided may shrink but not
            grow. Put the new code in a partial (for a section that owns its own
            state) or an extracted collaborator (for the hub and the shared-state
            clusters) — see docs/DESIGN-mainviewmodel-decomposition.md for which
            applies.

            If it genuinely has to grow, raise the number in
            MonolithRatchetTests.Budgets and say why in the commit message.
            """);
    }

    /// <summary>
    /// A budget that sits far above the file it guards has stopped guarding it, so
    /// the slack is capped too. Lowering a budget on extraction is what keeps this
    /// passing.
    /// </summary>
    [Fact]
    public void ABudgetStaysTightAgainstTheFileItGuards()
    {
        var stale = new List<string>();
        foreach (var (relative, max) in Budgets)
        {
            var actual = File.ReadAllLines(Path.Combine(RepoRoot(), relative)).Length;
            var slack = max - actual;
            output.WriteLine($"{relative}: {actual} of {max}, slack {slack}");
            if (slack > 250)
            {
                stale.Add($"{relative} is {actual} lines against a budget of {max} — {slack} lines of slack");
            }
        }

        Assert.True(
            stale.Count == 0,
            "A budget has been left behind by an extraction, and the slack is now room to regrow:\n  "
                + string.Join("\n  ", stale)
                + "\nLower it to the file's current length.");
    }
}
