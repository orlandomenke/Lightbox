using Lightbox.Core.Projects;

namespace Lightbox.Core.Documents;

/// <summary>
/// Whether the brush belongs to the tool or to the drawing.
/// </summary>
public enum BrushScope
{
    /// <summary>
    /// One brush for the application, carried between documents and sessions.
    /// What Photoshop and Krita do, and what a fast-moving job wants: a
    /// storyboard artist picking up the same pencil for panel forty as for
    /// panel one should not have to re-pick it.
    /// </summary>
    Global,

    /// <summary>
    /// The project remembers the brush its documents are painted with, and
    /// hands it to every one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case this exists for is a break between sessions. Coming back to a
    /// comic or a set of game assets after a fortnight, the question is not
    /// "what brush do I like" but "what brush is <i>this</i> drawn with" — and
    /// on work where the character of the stroke is part of the style,
    /// guessing wrong is visible in the result.
    /// </para>
    /// <para>
    /// The project rather than the document, because the answer has to reach
    /// the pages that do not exist yet. Page one of a comic remembering its
    /// own brush leaves page eleven starting from whatever you last used on
    /// something else, which is the same problem one file later. Pillar 1
    /// already says it: a character's animations share one palette, one brush
    /// set, one set of references.
    /// </para>
    /// <para>
    /// With no project open there is nothing to hold it, so this reads as
    /// <see cref="Global"/>.
    /// </para>
    /// </remarks>
    PerProject,
}

/// <summary>What a project type implies about where the brush lives.</summary>
public static class BrushScopeDefaults
{
    /// <summary>
    /// The scope a project type gets when the artist has not chosen one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split is about whether a body of work has a house style. A comic, a
    /// set of game assets, an illustration series — the mark is part of what
    /// the work looks like, and every new page should start from it. A
    /// storyboard is drawn to be read and thrown away, and an animator moving
    /// through one character's cycles is already carrying one pencil; those
    /// keep the brush on the tool.
    /// </para>
    /// <para>
    /// A default, not a rule: <c>AppSettings.BrushMemory</c> overrides it, and
    /// with no project open there is no type to ask, so it is
    /// <see cref="BrushScope.Global"/> — the behaviour the application has
    /// always had.
    /// </para>
    /// </remarks>
    public static BrushScope For(ProjectType? type) => type switch
    {
        ProjectType.Illustration => BrushScope.PerProject,
        ProjectType.Comic => BrushScope.PerProject,
        ProjectType.GameArt => BrushScope.PerProject,
        ProjectType.AssetLibrary => BrushScope.PerProject,
        ProjectType.Animation => BrushScope.Global,
        ProjectType.Storyboard => BrushScope.Global,
        _ => BrushScope.Global,
    };
}
