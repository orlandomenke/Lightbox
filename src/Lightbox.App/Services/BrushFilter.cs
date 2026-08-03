namespace Lightbox.App.Services;

/// <summary>
/// Which brushes a search box and a set of tag chips leave visible.
/// </summary>
/// <remarks>
/// A pure function rather than a method on the window, because it is the part
/// with rules in it — the AND between search and tags, the OR between the tags
/// themselves, the fact that typing matches a tag as well as a name — and none
/// of those are testable through a flyout.
/// </remarks>
public static class BrushFilter
{
    /// <summary>
    /// Does this brush survive the filter?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Search and tags narrow together, so typing inside a chosen tag stays
    /// inside it.
    /// </para>
    /// <para>
    /// The tags themselves are an <b>OR</b>. Picking "inking" and "roughs"
    /// means either, because a brush filed under both is rare and an AND would
    /// hand back an empty list almost every time two chips were lit — which
    /// reads as the filter being broken rather than as the artist having asked
    /// for something impossible.
    /// </para>
    /// </remarks>
    public static bool Matches(BrushPreset preset, string? search, IReadOnlySet<string> tags)
    {
        var needle = (search ?? "").Trim();

        var found = needle.Length == 0
            || preset.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || preset.NormalisedTags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase));

        var filed = tags.Count == 0 || preset.NormalisedTags.Any(tags.Contains);

        return found && filed;
    }

    /// <summary>The visible brushes, in the order the picker was given them.</summary>
    public static List<BrushPreset> Apply(
        IEnumerable<BrushPreset> presets, string? search, IReadOnlySet<string> tags) =>
        presets.Where(p => Matches(p, search, tags)).ToList();
}
