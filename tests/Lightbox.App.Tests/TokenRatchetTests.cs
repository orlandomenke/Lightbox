using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The token ratchet (docs/DESIGN-tokens.md, Q203). Sizes, gaps and type sizes
/// are named in Styles/Tokens.axaml; what is still written as a number in a
/// view or style is counted against .claude/quality/ratchets/literals.json,
/// and a count may fall but never rise.
/// </summary>
/// <remarks>
/// <para>
/// The look only holds together when every inset has one answer — the gallery
/// showed that the moment its rows were put on one grid. A literal added today
/// is the next "this dropdown sits on the edge while the rows have margins".
/// </para>
/// <para>
/// <b>Lowering a budget is part of the change that earns it.</b> Moving a view's
/// margins onto tokens and leaving its budget where it was would let the next
/// change spend the difference on new literals; the slack cap below makes the
/// stale number fail rather than wait.
/// </para>
/// </remarks>
public sealed class TokenRatchetTests(ITestOutputHelper output)
{
    /// <summary>How far a budget may sit above its file before it stops guarding it.</summary>
    private const int MaxSlack = 0;

    private const string Props =
        "FontSize|Spacing|Margin|Padding|CornerRadius|Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight";

    /// <summary>
    /// A number written for one of the properties, in any of the spellings XAML
    /// accepts — either quote, spaces round the equals, and a Setter with its
    /// Value before or after its Property. The adversary review found each of
    /// those slipping past the first version. scripts/literals.py carries the
    /// same pattern; the two must count the same thing.
    /// </summary>
    private static readonly Regex Literal = new(
        $@"(?<![\w.:])(?:{Props})\s*=\s*[""']-?[0-9][0-9,.\-]*[""']" +
        $@"|Property\s*=\s*[""'](?:{Props})[""']\s+Value\s*=\s*[""']-?[0-9][0-9,.\-]*[""']" +
        $@"|Value\s*=\s*[""']-?[0-9][0-9,.\-]*[""']\s+Property\s*=\s*[""'](?:{Props})[""']");

    /// <summary>The token file itself is where numbers belong: exempt by exact path.</summary>
    private const string TokenFile = "src/Lightbox.App/Styles/Tokens.axaml";

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Every XAML file the app ships, by repo-relative path with forward slashes.</summary>
    public static IEnumerable<string> Files()
    {
        var root = Root();
        var app = Path.Combine(root, "src", "Lightbox.App");
        return Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => f != TokenFile)
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    public static int Count(string relative) =>
        Literal.Matches(File.ReadAllText(Path.Combine(Root(), relative))).Count;

    private static Dictionary<string, int> Budgets() =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(
            Path.Combine(Root(), ".claude", "quality", "ratchets", "literals.json")))!;

    [Fact]
    public void NoFileHoldsMoreLiteralsThanItsBudget()
    {
        var budgets = Budgets();
        var over = new List<string>();
        var stale = new List<string>();
        foreach (var file in Files())
        {
            var count = Count(file);
            var budget = budgets.GetValueOrDefault(file, 0);
            if (count > budget) over.Add($"{file}: {count} literals, budget {budget}");
            else if (budget - count > MaxSlack) stale.Add($"{file}: {count} literals, budget {budget} — lower it to {count}");
        }
        output.WriteLine($"{Files().Sum(Count)} literals left across {Files().Count()} files");
        Assert.True(over.Count == 0,
            "a size, gap or type size was written as a number — name a token from Styles/Tokens.axaml "
            + "(docs/DESIGN-tokens.md):\n  " + string.Join("\n  ", over));
        Assert.True(stale.Count == 0,
            "literals were moved onto tokens — lower .claude/quality/ratchets/literals.json in the same "
            + "change, or the difference is free to spend:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void EveryBudgetNamesAFileThatExists()
    {
        var files = Files().ToHashSet();
        var missing = Budgets().Keys.Where(k => !files.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "budgets for files that no longer exist:\n  " + string.Join("\n  ", missing));
    }

    [Theory]
    [InlineData("FontSize", "10|11|12|15|18")]
    [InlineData("Spacing", "2|4|6|8|10|12|14|16")]
    public void AValueOnTheScaleIsAlwaysItsToken(string property, string scale)
    {
        // The mechanical half of step 1: these values have exactly one token
        // each, so a literal of one of them is never a judgement call. Any
        // spelling counts — "12", '12', 12.0, a Setter either way round.
        var value = $@"[""'](?:{scale})(?:\.0+)?[""']";
        var pattern = new Regex(
            $@"(?<![\w.:]){property}\s*=\s*{value}" +
            $@"|Property\s*=\s*[""']{property}[""']\s+Value\s*=\s*{value}" +
            $@"|Value\s*=\s*{value}\s+Property\s*=\s*[""']{property}[""']");
        var offenders = Files()
            .SelectMany(f => pattern.Matches(File.ReadAllText(Path.Combine(Root(), f))).Select(m => $"{f}: {m.Value}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"{property} on the scale written as a number — use its token:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void ABrushOnAControlIsADynamicResource()
    {
        // What lets the theme switch while the app runs (Q203). A static
        // reference resolves once, at load, and would keep the old theme's colour.
        // The files that DEFINE resources are exempt: a brush built from a colour
        // inside a resource dictionary is not on a control.
        string[] definers = ["Palette.axaml", "Theme.axaml", "Brand.axaml", "Icons.axaml", "AppResources.axaml"];
        var offenders = Files()
            .Where(f => !definers.Contains(Path.GetFileName(f)))
            // Icon geometries are named for what they draw (IconBrush is the brush
            // TOOL), not brushes: a static reference is right for them, and the
            // first conversion swept one up — the adversary review caught it.
            .SelectMany(f => Regex.Matches(File.ReadAllText(Path.Combine(Root(), f)), @"\{StaticResource (?!Icon)\w+Brush\}")
                .Select(m => $"{f}: {m.Value}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "a brush read with StaticResource — use DynamicResource so a theme can change it:\n  "
            + string.Join("\n  ", offenders));
    }
}
