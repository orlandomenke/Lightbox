using System.Text.RegularExpressions;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The files that are already too big may shrink and may not grow. New work goes
/// into a partial or a collaborator rather than onto the end of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The budgets are not in this file any more (Q91).</b> They live one per file
/// under <c>.claude/quality/ratchets/</c>, with the reasons each number has moved
/// beside it — read that directory's <c>README.md</c> for why a ratchet rather
/// than a plan, why line counts, and what makes raising one legitimate.
/// </para>
/// <para>
/// They moved because of how they were <i>edited</i>, not what they said. Every
/// branch touching a budgeted file had to change the number and add a paragraph
/// explaining it, all inside one 267-line C# file shared by all four budgets — so
/// two branches growing two <i>different</i> files still conflicted here. One file
/// each means they no longer do, and the reasons stay append-only so a merge keeps
/// both sides' rather than silently taking one.
/// </para>
/// <para>
/// <b>What did not move is the number being authored.</b> Three of the four
/// ceilings equal their file's exact line count, which makes an automatic
/// re-measure look obvious and would destroy the mechanism: a ceiling derived from
/// the tree can never be exceeded, so this test could never fail. The number works
/// because raising it is a conscious edit that shows up in a diff.
/// <c>ratchets.py remeasure</c> exists for resolving a merge, where "measure on the
/// merged tree" is mechanical, and is wired to no hook.
/// </para>
/// </remarks>
public class MonolithRatchetTests(ITestOutputHelper output)
{
    /// <summary>
    /// A budget that sits far above the file it guards has stopped guarding it, so
    /// the slack is capped as well as the growth.
    /// </summary>
    private const int MaxSlack = 250;

    private static readonly Regex Target = new(@"^#\s+(?<path>\S+)\s*$", RegexOptions.Multiline);
    private static readonly Regex Budget = new(@"^budget:\s*(?<lines>\d+)\s*$", RegexOptions.Multiline);

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

    private static string RatchetDir() =>
        Path.Combine(RepoRoot(), ".claude", "quality", "ratchets");

    /// <summary>Every budget, read from the directory that owns them.</summary>
    public static IEnumerable<(string Source, string Path, int Max)> Budgets()
    {
        foreach (var file in Directory.EnumerateFiles(RatchetDir(), "*.md").OrderBy(f => f))
        {
            if (Path.GetFileName(file) == "README.md")
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var target = Target.Match(text);
            var budget = Budget.Match(text);
            Assert.True(target.Success && budget.Success,
                $"{Path.GetFileName(file)} needs `# <path>` as its first line and a `budget: <n>` "
                + "line. Those two are the whole format, and the test reads nothing else.");

            yield return (Path.GetFileName(file), target.Groups["path"].Value,
                          int.Parse(budget.Groups["lines"].Value));
        }
    }

    public static TheoryData<string, string, int> BudgetCases()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var (source, path, max) in Budgets())
        {
            data.Add(source, path, max);
        }
        return data;
    }

    /// <summary>
    /// The directory being empty would make every other test here vacuously pass, so
    /// the count is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void TheBudgetsAreReadFromTheirOwnDirectory()
    {
        var budgets = Budgets().ToList();
        foreach (var (source, path, max) in budgets)
        {
            output.WriteLine($"{source}: {path} <= {max}");
        }

        Assert.True(budgets.Count >= 4,
            $"only {budgets.Count} ratchet(s) found in .claude/quality/ratchets/. A budget removed "
            + "is a file allowed to grow again, which is a decision rather than a tidy-up.");
    }

    [Theory]
    [MemberData(nameof(BudgetCases))]
    public void AFileThatIsAlreadyTooBigDoesNotGrow(string source, string relative, int max)
    {
        var full = Path.Combine(RepoRoot(), relative);
        Assert.True(File.Exists(full),
            $"{relative} has moved or been renamed — update {source} or delete it.");

        var actual = File.ReadAllLines(full).Length;
        output.WriteLine($"{relative}: {actual} lines, budget {max} ({actual - max:+#;-#;0})");

        Assert.True(
            actual <= max,
            $"""
            {relative} is {actual} lines, over its budget of {max} by {actual - max}.

            This file is one the project has decided may shrink but not grow. Put
            the new code in a partial (for a section that owns its own state) or an
            extracted collaborator (for the hub and the shared-state clusters) —
            see docs/DESIGN-mainviewmodel-decomposition.md for which applies.

            If it genuinely has to grow, raise the number in
            .claude/quality/ratchets/{source}, say why in the entry beneath it, and
            say why in the commit message.

            If you are resolving a merge, neither side's number is right on the
            merged tree — run `python3 scripts/ratchets.py remeasure`.
            """);
    }

    /// <summary>
    /// A budget left behind by an extraction is room to regrow. Lowering it in the
    /// same commit as the extraction is what keeps this passing.
    /// </summary>
    [Theory]
    [MemberData(nameof(BudgetCases))]
    public void ABudgetStaysTightAgainstTheFileItGuards(string source, string relative, int max)
    {
        var actual = File.ReadAllLines(Path.Combine(RepoRoot(), relative)).Length;
        var slack = max - actual;
        output.WriteLine($"{relative}: {actual} of {max}, slack {slack}");

        Assert.True(slack <= MaxSlack,
            $"{relative} is {actual} lines against a budget of {max} — {slack} lines of slack. An "
            + $"extraction landed and left the budget behind, so it is now room to regrow. Lower "
            + $"it to the file's current length in .claude/quality/ratchets/{source}.");
    }

    /// <summary>
    /// The reasons are the half a script cannot reproduce, and the half a merge
    /// destroys by taking one side. An entry with a number and no account of it is
    /// the state this directory exists to prevent.
    /// </summary>
    [Theory]
    [MemberData(nameof(BudgetCases))]
    public void EveryBudgetSaysWhyItIsWhatItIs(string source, string relative, int max)
    {
        var text = File.ReadAllText(Path.Combine(RatchetDir(), source));
        var reasons = text.Split('\n').Count(l => l.TrimStart().StartsWith("- "));
        output.WriteLine($"{source}: {reasons} recorded move(s) for {relative} at {max}");

        Assert.True(reasons > 0,
            $"{source} carries a budget and no reason for it. Every move of a number is a decision "
            + "somebody made, and the entry is where it survives being read a year later — a merge "
            + "that takes one side deletes the other's and leaves a file with nothing wrong in it.");
    }
}
