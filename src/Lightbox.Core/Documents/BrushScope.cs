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
    /// Each document remembers the brush it was last painted with, saved in
    /// the file, and hands it back when reopened.
    /// </summary>
    /// <remarks>
    /// The case this exists for is a break between sessions. Coming back to a
    /// comic page or a game asset after a fortnight, the question is not "what
    /// brush do I like" but "what brush is <i>this</i> drawn with" — and on
    /// work where the character of the stroke is part of the style, guessing
    /// wrong is visible in the result. The document already knows: every
    /// stroke carries its own settings (invariant 4). This only puts the
    /// answer back in the tool bar.
    /// </remarks>
    PerDocument,
}

/// <summary>What a project type implies about where the brush lives.</summary>
public static class BrushScopeDefaults
{
    /// <summary>
    /// The scope a project type gets when the artist has not chosen one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split is about how long a document stays open in one head. Work you
    /// come back to — an illustration, a comic page, a game asset — is
    /// per-document, because the gap between sittings is where the brush gets
    /// forgotten. Work you move through in one pass — a storyboard, the
    /// animations of one character — is global, because you are switching
    /// documents constantly and want the same pencil in each.
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
        ProjectType.Illustration => BrushScope.PerDocument,
        ProjectType.Comic => BrushScope.PerDocument,
        ProjectType.GameArt => BrushScope.PerDocument,
        ProjectType.AssetLibrary => BrushScope.PerDocument,
        ProjectType.Animation => BrushScope.Global,
        ProjectType.Storyboard => BrushScope.Global,
        _ => BrushScope.Global,
    };
}
