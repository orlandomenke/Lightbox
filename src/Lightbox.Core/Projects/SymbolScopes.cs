using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Projects;

/// <summary>Where a symbol is kept, and therefore who can reach it.</summary>
public enum SymbolScope
{
    /// <summary>
    /// In the project. Shared by everything under it and by nothing outside it.
    /// </summary>
    Project,

    /// <summary>
    /// In the artist's own library, beside their brushes. Available with no
    /// project open and in every project they make.
    /// </summary>
    Global,
}

/// <summary>
/// Global and project symbols, and moving between them.
/// </summary>
/// <remarks>
/// <para>
/// Three scopes, and it is the split <c>TipStore</c> already settled for brush
/// tips: <b>global</b> is the artist's own library, <b>project</b> is
/// <see cref="Project.Symbols"/>, and <b>document</b> is <see cref="Doc.Symbols"/>
/// — the flattening target, so an exported file renders with no library installed.
/// </para>
/// <para>
/// <b>The decision this rests on, because it costs something.</b> A symbol is a
/// live link by id, which is the whole of <c>DESIGN-symbols.md</c> and the
/// difference from a template. But the project is what re-renders: a
/// <c>.lbproj</c> that reached into an application folder to draw would lose art
/// the moment it moved to another machine. So <b>placing a global symbol copies it
/// into the project</b>, under the same id — exactly what <c>TipStore</c> does
/// when a tip is first painted with. The library is a source to choose from, not
/// a live dependency.
/// </para>
/// <para>
/// The consequences, stated rather than discovered: a project keeps rendering
/// with the library gone, and editing a global symbol does <em>not</em> reach back
/// into projects that already placed it. Rolling a fix forward is
/// <see cref="Pull"/> — the same direction as <c>Templates.Apply</c>, the document
/// reaching out, never the library reaching in.
/// </para>
/// </remarks>
public static class SymbolScopes
{
    /// <summary>
    /// Copy a global symbol into the project so placements resolve from the
    /// project alone. Returns the project's copy.
    /// </summary>
    /// <remarks>
    /// <b>The id is kept.</b> That is what makes a placement made today still
    /// resolve, and what lets <see cref="Pull"/> match a project copy to the
    /// library entry it came from. Idempotent: adopting twice is adopting once,
    /// so placing the same library symbol on ten frames copies it once.
    /// </remarks>
    public static Symbol Adopt(Project project, Symbol global)
    {
        if (project.Symbols.TryGetValue(global.Id, out var already)) return already;
        var copy = DocJson.CloneValue(global);
        project.Symbols[copy.Id] = copy;
        return copy;
    }

    /// <summary>
    /// Copy a project symbol up into the library, keeping its id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One direction only. There is no demote, and the reason is that it would
    /// not mean anything: a global symbol that has been placed is already in that
    /// project, so "make this project-only" is just deleting it from the library,
    /// which is its own action and reads as one.
    /// </para>
    /// <para>
    /// Overwrites an existing library entry with the same id, because that is
    /// what promoting an edited symbol means — the alternative is a second entry
    /// with the same name and no way to tell which is which.
    /// </para>
    /// </remarks>
    public static Symbol Promote(IDictionary<string, Symbol> library, Symbol projectSymbol)
    {
        var copy = DocJson.CloneValue(projectSymbol);
        library[copy.Id] = copy;
        return copy;
    }

    /// <summary>One project symbol whose library original has moved on.</summary>
    /// <param name="Mine">The project's copy, as it is now.</param>
    /// <param name="Theirs">The library entry it came from.</param>
    public sealed record Stale(Symbol Mine, Symbol Theirs)
    {
        public string Name => Theirs.Name;

        public int FromVersion => Mine.Version;

        public int ToVersion => Theirs.Version;
    }

    /// <summary>
    /// Project symbols the library has a newer version of.
    /// </summary>
    /// <remarks>
    /// Compared by <see cref="Symbol.Version"/> rather than by content: the
    /// version is already bumped on every edit for the placement-staleness
    /// report, and re-using it means one definition of "changed" in the
    /// application instead of two that can disagree. A library entry that is
    /// *older* than the project's copy is not offered — pulling would undo the
    /// artist's own work, which is never what "update" means.
    /// </remarks>
    public static List<Stale> Outdated(Project project, IReadOnlyDictionary<string, Symbol> library)
    {
        var stale = new List<Stale>();
        foreach (var (id, mine) in project.Symbols)
        {
            if (library.TryGetValue(id, out var theirs) && theirs.Version > mine.Version)
            {
                stale.Add(new Stale(mine, theirs));
            }
        }
        return stale.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Refresh one project symbol from the library. False when there is nothing
    /// newer to take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pull, and the direction is the safety property: the project reaches
    /// out when the artist says so. Nothing in the library can reach into a
    /// project, so editing a symbol in your own library can never change a
    /// finished shot.
    /// </para>
    /// <para>
    /// Placements are untouched and do not need touching — they reference the
    /// symbol by id, so replacing what that id resolves to is the whole of the
    /// update. That is the property that made symbols worth having.
    /// </para>
    /// </remarks>
    public static bool Pull(Project project, IReadOnlyDictionary<string, Symbol> library, string id)
    {
        if (!project.Symbols.TryGetValue(id, out var mine)) return false;
        if (!library.TryGetValue(id, out var theirs) || theirs.Version <= mine.Version) return false;
        project.Symbols[id] = DocJson.CloneValue(theirs);
        return true;
    }

    // ---- the tree axis (Q30 step 5) -----------------------------------------

    /// <summary>The kind string a symbol is declared under.</summary>
    /// <remarks>
    /// <b>Last of the five on purpose, because it narrows rather than widens.</b>
    /// Every other kind Q30 scoped was going from *nothing* to *somewhere*; a
    /// symbol is already project-wide, so adding folder depth can only take
    /// something away. That is why the rule below is not "resolve the chain" but
    /// "resolve the chain <em>if the artist asked for one</em>" — an existing
    /// project means every symbol everywhere, and it has to keep meaning that.
    /// </remarks>
    public const string Kind = "symbol";

    /// <summary>Whether this project scopes its symbols at all.</summary>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The symbol ids this document can place, or null when the project scopes
    /// none and every symbol applies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than "all of them", so a caller can tell *unscoped* from
    /// *scoped to everything* without counting. Every other kind does the same,
    /// and here it is load-bearing rather than tidy: a project that has never
    /// declared a symbol scope must behave exactly as it did, and a null is the
    /// only answer that cannot be mistaken for a filter that happened to admit
    /// everything.
    /// </para>
    /// <para>
    /// <b>This is the picker, not the renderer.</b> A placement resolves by id
    /// through <c>SymbolRegistry</c> and always will — moving a document into a
    /// folder that does not declare a symbol it already places must not change
    /// what it draws, or invariant 1 goes. What narrows is what an artist is
    /// <em>offered</em>. <c>TheProjectRendersWithTheLibraryGone</c> is the test
    /// that says so and it needed no change for this.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string>? VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return [.. ResourceScopes.Resolve(manifest, document, Kind).Select(r => r.Id)];
    }

    /// <summary>
    /// Whether a document may place this symbol — true when nothing is scoped.
    /// </summary>
    public static bool CanPlace(ProjectManifest manifest, DocumentRef? document, string symbolId) =>
        VisibleTo(manifest, document) is not { } allowed || allowed.Contains(symbolId);
}
