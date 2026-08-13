using System.Text.RegularExpressions;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The state left in <c>MainViewModel.cs</c> after the split has to stay shared, and
/// the shared block may shrink and may not grow.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <c>MonolithRatchetTests</c>.</b> That one caps lines, and
/// the split took the hub from 13,628 of them to under 700 — so as a guard on this file
/// it has almost nothing left to do. The thing that can still rot is not the length, it
/// is the <i>rule</i>: a section's own state travels into the partial that owns it, and
/// only state used by more than one section stays in the hub. A field parked here
/// because it was easier than deciding where it belongs re-couples the class without
/// adding a line anybody notices.
/// </para>
/// <para>
/// <b>It is derived rather than asserted</b>, in the same spirit as the ledgers: the
/// test reads the field declarations out of the hub and the usages out of the eighteen
/// partials, so it cannot be satisfied by editing a list. Four fields had in fact
/// drifted the wrong way and were only found by measuring —
/// <c>_untitledCounter</c> (used only by <c>Documents</c>), <c>_snapshotQueued</c> and
/// <c>_stabilizer</c> (only by <c>Painting</c>), and <c>_featureDefaults</c>, which was
/// used by nothing at all: <c>Diagnostics.cs</c> constructs its own.
/// </para>
/// <para>
/// <b>Hub-internal state is not a violation.</b> The frame caches, the prewarmer and the
/// tile fallbacks are read by one partial each and heavily by the hub's own invalidation
/// funnel, which is where they belong — moving them would split the funnel in two. So
/// the rule is "used by the hub, or by two or more partials", which is the honest
/// statement of what the split promised rather than a stricter one that would be wrong.
/// </para>
/// </remarks>
public class SharedStateRatchetTests(ITestOutputHelper output)
{
    /// <summary>
    /// Fields below the shared-state marker, measured 2026-08-13. Lower it when a field
    /// leaves; raising it means a new field really is used by several sections, which is
    /// worth a sentence in the commit message.
    /// </summary>
    public const int SharedStateFields = 21;

    private const string Marker = "---- state shared across sections";

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

    private static string ViewModels() =>
        Path.Combine(RepoRoot(), "src", "Lightbox.App", "ViewModels");

    private static string HubPath() => Path.Combine(ViewModels(), "MainViewModel.cs");

    /// <summary>
    /// Private field declarations on <c>MainViewModel</c> itself, with the name a
    /// <c>[ObservableProperty]</c> field is actually read through.
    /// </summary>
    /// <remarks>
    /// The hub also declares <c>FrameCell</c>, whose backing fields would otherwise read
    /// as unused state, so the scan starts at the view model's own declaration. A
    /// generated property is what every other file sees, so an observable field is
    /// searched for under both names or it would look lonely to everybody.
    /// </remarks>
    private static List<(string Field, string? Property, int Line)> HubFields(out string[] lines)
    {
        var all = File.ReadAllLines(HubPath());
        var from = Array.FindIndex(all, l => l.Contains("class MainViewModel"));
        Assert.True(from >= 0, "MainViewModel's own declaration has moved.");
        lines = all;

        var decl = new Regex(@"^\s+private\s+(?:readonly\s+)?[\w<>?,\[\]\. ]+?\s+(_\w+)\s*(?:=|;)");
        var found = new List<(string, string?, int)>();
        var observable = false;
        for (var i = from; i < all.Length; i++)
        {
            var line = all[i];
            if (line.Contains("[ObservableProperty]"))
            {
                observable = true;
                continue;
            }
            var m = decl.Match(line);
            if (m.Success)
            {
                var field = m.Groups[1].Value;
                found.Add((field, observable ? char.ToUpperInvariant(field[1]) + field[2..] : null, i));
                observable = false;
                continue;
            }
            var t = line.Trim();
            if (t.Length > 0 && !t.StartsWith("//") && !t.StartsWith('[')) observable = false;
        }
        return found;
    }

    /// <summary>
    /// State used by exactly one section belongs in that section's partial. This is the
    /// split's own rule, checked against the code rather than trusted.
    /// </summary>
    [Fact]
    public void NoFieldLeftInTheHubBelongsToASingleSection()
    {
        var fields = HubFields(out var hubLines);
        var partials = Directory
            .GetFiles(ViewModels(), "MainViewModel.*.cs")
            .ToDictionary(
                p => Path.GetFileNameWithoutExtension(p).Replace("MainViewModel.", ""),
                File.ReadAllText);

        // The declarations themselves are not uses, so the hub is searched without them.
        var declared = fields.Select(f => f.Line).ToHashSet();
        var hubBody = string.Join('\n', hubLines.Where((_, i) => !declared.Contains(i)));

        var misplaced = new List<string>();
        foreach (var (field, property, _) in fields)
        {
            var names = property is null ? field : $"{field}|{property}";
            var uses = new Regex($@"\b(?:{names})\b");
            var sections = partials.Where(p => uses.IsMatch(p.Value)).Select(p => p.Key).ToList();
            var inHub = uses.IsMatch(hubBody);

            if (sections.Count >= 2 || inHub) continue;
            misplaced.Add(sections.Count == 0
                ? $"{field} is used by nothing — delete it"
                : $"{field} is used only by MainViewModel.{sections[0]}.cs — move it there");
        }

        output.WriteLine($"{fields.Count} fields declared in the hub, {partials.Count} partials, "
                         + $"{misplaced.Count} misplaced");
        Assert.True(
            misplaced.Count == 0,
            """
            State that one section owns has been left in the hub:

            """ + string.Join("\n  ", misplaced) + """


            The split's rule (Q78) is that a section's own state travels with it and only
            shared state stays in MainViewModel.cs. See
            docs/DESIGN-mainviewmodel-decomposition.md.
            """);
    }

    /// <summary>
    /// The shared block is the honest measure of how coupled the class still is, so it
    /// ratchets the same way the line counts do.
    /// </summary>
    [Fact]
    public void TheSharedStateBlockDoesNotGrow()
    {
        var lines = File.ReadAllLines(HubPath());
        var from = Array.FindIndex(lines, l => l.Contains(Marker));
        Assert.True(from >= 0, $"The '{Marker}' marker has gone — the block it delimits is what this counts.");

        var decl = new Regex(@"^\s+private\s+(?:readonly\s+)?[\w<>?,\[\]\. ]+?\s+(_\w+)\s*(?:=|;)");
        var names = lines.Skip(from).Select(l => decl.Match(l)).Where(m => m.Success)
            .Select(m => m.Groups[1].Value).ToList();

        output.WriteLine($"{names.Count} shared fields against a budget of {SharedStateFields}: "
                         + string.Join(", ", names));

        Assert.True(
            names.Count <= SharedStateFields,
            $"""
            The shared-state block is {names.Count} fields, over its budget of {SharedStateFields}.

            A new field here is a new coupling between sections. Before raising the
            number, check whether the state belongs in one partial (move it) or wants a
            collaborator of its own — LivePaintSession, PublishState, BrushWorkingSet and
            TransformSession are all fields that turned out to be one object.
            """);
    }
}
