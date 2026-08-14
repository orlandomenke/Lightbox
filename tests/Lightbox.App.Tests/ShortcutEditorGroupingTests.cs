using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The shortcut editor groups by where a binding applies (Q82).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closed.</b> The list was grouped by category, so the two
/// commands bound to <c>I</c> — "Color picker tool" under Tools and "Insert
/// keyframe at playhead" under Timeline — appeared as the same key bound twice
/// with nothing saying that one of them only answers over the timeline. The
/// feature worked and the one screen an artist would consult to understand it
/// described something else.
/// </para>
/// <para>
/// <b>What it cost, kept honest here.</b> "Everywhere" is now much the largest
/// group and commands an artist thinks of together are split when they differ in
/// scope. <see cref="TheWidestGroupIsStillOrderedByCategory"/> is the mitigation
/// under test rather than asserted in a comment.
/// </para>
/// </remarks>
public class ShortcutEditorGroupingTests(ITestOutputHelper output)
{
    private static ConfigureWindow OnShortcutsPage() =>
        new(new ShortcutMap(), new MainViewModel(artist: null));

    private static IReadOnlyList<ShortcutGroup> Groups(ConfigureWindow window) =>
        (IReadOnlyList<ShortcutGroup>)window.FindControl<ItemsControl>("GroupsHost")!.ItemsSource!;

    [AvaloniaFact]
    public void TheListIsGroupedByWhereABindingAppliesRatherThanByWhatItDoes()
    {
        var groups = Groups(OnShortcutsPage());
        var names = groups.Select(g => g.Name).ToList();
        output.WriteLine(string.Join(" | ", names));

        Assert.Contains("Everywhere", names);
        Assert.Contains("Canvas", names);
        Assert.Contains("Timeline", names);
        Assert.Contains("Layers", names);

        // The old headings were categories. "Tools" was the biggest of them and
        // is the one that would still be here if the grouping had not changed.
        Assert.DoesNotContain("Tools", names);
    }

    /// <summary>
    /// Widest first, so the list reads top to bottom as the order the resolver
    /// actually walks: general, then canvas, then the panels.
    /// </summary>
    [AvaloniaFact]
    public void TheGroupsReadInTheOrderTheFallbackChainWalks()
    {
        var names = Groups(OnShortcutsPage()).Select(g => g.Name).ToList();

        Assert.Equal("Everywhere", names[0]);
        Assert.Equal("Canvas", names[1]);
        Assert.True(names.Count > 2, "the panel groups are missing entirely");
    }

    /// <summary>
    /// The two commands on <c>I</c> land under headings that say which is which
    /// — the case that named this file.
    /// </summary>
    [AvaloniaFact]
    public void TheTwoCommandsOnIAreFiledUnderTheirOwnScopes()
    {
        var groups = Groups(OnShortcutsPage());

        var picker = groups.Single(g => g.Rows.Any(r => r.Definition.Id == "canvas.pickColor"));
        var insert = groups.Single(g => g.Rows.Any(r => r.Definition.Id == "timeline.insertKey"));

        Assert.Equal("Everywhere", picker.Name);
        Assert.Equal("Timeline", insert.Name);
        output.WriteLine($"I: {picker.Name} / {insert.Name}");
    }

    /// <summary>
    /// Every heading states its rule instead of leaving it to be inferred, which
    /// is the reason grouping by scope is worth its cost at all.
    /// </summary>
    [AvaloniaFact]
    public void EveryHeadingSaysWhatItMeans()
    {
        foreach (var group in Groups(OnShortcutsPage()))
        {
            Assert.False(string.IsNullOrWhiteSpace(group.Hint), $"{group.Name} has no hint");
            output.WriteLine($"{group.Name}: {group.Hint}");
        }
    }

    /// <summary>
    /// Category no longer groups, so it orders — without it the widest group is
    /// an undifferentiated wall, which is the cost this mitigates.
    /// </summary>
    [AvaloniaFact]
    public void TheWidestGroupIsStillOrderedByCategory()
    {
        var widest = Groups(OnShortcutsPage()).OrderByDescending(g => g.Rows.Count).First();
        var categories = widest.Rows.Select(r => r.Definition.Category).ToList();

        Assert.Equal(categories.OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase), categories);
        output.WriteLine($"{widest.Name}: {string.Join(", ", categories.Distinct())}");
    }

    /// <summary>
    /// The scope became a heading, so it has to be searchable — otherwise typing
    /// the name of a group is the one query that empties the list.
    /// </summary>
    [AvaloniaFact]
    public void SearchingForAPanelNameFindsThatPanelsBindings()
    {
        var window = OnShortcutsPage();
        window.FindControl<TextBox>("SearchBox")!.Text = "timeline";

        var groups = Groups(window);
        Assert.Contains(groups, g => g.Name == "Timeline");
        Assert.Contains(groups.SelectMany(g => g.Rows), r => r.Definition.Id == "timeline.insertKey");
    }
}
