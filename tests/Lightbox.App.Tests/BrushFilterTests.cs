using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// What a search box and a row of tag chips leave visible. Pure rules, tested
/// as rules — none of them are reachable through a flyout.
/// </summary>
public class BrushFilterTests
{
    private static BrushPreset Brush(string name, params string[] tags) =>
        new() { Name = name, Tags = tags.Length == 0 ? null : [.. tags] };

    private static readonly BrushPreset Liner = Brush("Fine liner", "inking", "lines");
    private static readonly BrushPreset Wash = Brush("Wet wash", "painting");
    private static readonly BrushPreset Plain = Brush("Pencil");

    private static IReadOnlySet<string> Tags(params string[] tags) =>
        new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

    private static List<BrushPreset> Apply(string? search, params string[] tags) =>
        BrushFilter.Apply([Liner, Wash, Plain], search, Tags(tags));

    [Fact]
    public void NoFilterShowsEverythingInTheOrderItCame()
    {
        Assert.Equal(["Fine liner", "Wet wash", "Pencil"], Apply(null).Select(p => p.Name));
        Assert.Equal(3, Apply("   ").Count);
    }

    [Fact]
    public void SearchMatchesPartOfANameAndIgnoresCase()
    {
        Assert.Equal(["Fine liner"], Apply("LINE").Select(p => p.Name));
        Assert.Equal(["Wet wash"], Apply("wash").Select(p => p.Name));
    }

    [Fact]
    public void SearchMatchesATagAsWellAsAName()
    {
        // Somebody typing "inking" is looking for the brushes they filed under
        // it, and having to know whether they typed it into a name or a tag is
        // exactly the bookkeeping the search box is there to remove.
        Assert.Equal(["Fine liner"], Apply("inking").Select(p => p.Name));
        Assert.Equal(["Wet wash"], Apply("painting").Select(p => p.Name));
    }

    [Fact]
    public void TwoTagsMeanEitherNotBoth()
    {
        // A brush filed under both is rare, so an AND would hand back an empty
        // list nearly every time two chips were lit — which reads as the filter
        // being broken rather than as the question being impossible.
        Assert.Equal(
            ["Fine liner", "Wet wash"],
            BrushFilter.Apply([Liner, Wash, Plain], null, Tags("inking", "painting")).Select(p => p.Name));
    }

    [Fact]
    public void SearchAndTagsNarrowTogether()
    {
        // Typing inside a chosen tag stays inside it. The other direction —
        // search widening past the tag — would make the chips decorative.
        Assert.Empty(Apply("wash", "inking"));
        Assert.Equal(["Fine liner"], Apply("liner", "inking").Select(p => p.Name));
    }

    [Fact]
    public void AnUntaggedBrushIsHiddenByAnyTagFilterAndFoundByName()
    {
        Assert.DoesNotContain(Apply(null, "inking"), p => p.Name == "Pencil");
        Assert.Contains(Apply("penc"), p => p.Name == "Pencil");
    }

    [Fact]
    public void TagMatchingIgnoresCaseAndSurroundingSpace()
    {
        // Tags are free text an artist typed, so "Inking" and " inking " are
        // the same shelf.
        var scruffy = Brush("Scruffy", "  Inking ");

        Assert.Single(BrushFilter.Apply([scruffy], null, Tags("INKING")));
        Assert.Single(BrushFilter.Apply([scruffy], "inking", Tags()));
    }

    [Fact]
    public void NothingMatchingIsAnEmptyListRatherThanEverything()
    {
        // The failure mode worth naming: a filter that falls back to "show all"
        // when it matches nothing looks like it ignored what you typed.
        Assert.Empty(Apply("nonexistent"));
    }
}
