namespace Lightbox.Core.Projects;

/// <summary>How far a resource declared at a scope can be seen.</summary>
/// <remarks>
/// Two values, deliberately, and not an access-control system. Q30 settled that
/// resources hang on scopes and accumulate down the tree; the four workflows
/// behind <c>docs/DESIGN-scoped-resources.md</c> then showed that a cascade
/// alone cannot express *"this environment reference lives in `environments/`
/// and every character needs it"* — that is a sideways reach, and the folder it
/// lives in is not an ancestor of anything that wants it.
/// </remarks>
public enum ResourceReach
{
    /// <summary>
    /// Visible to documents at or below the declaring scope. The default, and
    /// the one that writes no key.
    /// </summary>
    Subtree,

    /// <summary>
    /// Visible everywhere in the project, wherever it is filed. The sword in the
    /// asset library that characters, environments and props all place.
    /// </summary>
    Project,
}

/// <summary>
/// One resource declared at a scope: which resource, of what kind, and how far
/// it reaches.
/// </summary>
/// <remarks>
/// <para>
/// A pointer rather than the resource itself. The palette, sheet or gradient
/// lives where it always did; this says <em>who can see it</em>, which is the
/// part Q30 changed.
/// </para>
/// <para>
/// <b>Not what rendering reads.</b> Scoped resources are a library to choose
/// from — resolution happens when an artist picks, and the choice is captured
/// into the stroke record. If a frame resolved this at render time, moving a
/// document between folders would change its pixels, which breaks invariant 1
/// and invariant 4 together. The codebase already states the rule twice, in
/// <c>DeletingFromTheLibraryCannotChangeADrawing</c> and
/// <c>TheProjectRendersWithTheLibraryGone</c>.
/// </para>
/// </remarks>
public sealed class ScopedResource
{
    /// <summary>The resource this points at — a palette id, a sheet id, and so on.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// What kind of thing it is: <c>palette</c>, <c>reference</c>,
    /// <c>gradient</c>, <c>export</c>. A plain string rather than an enum so a
    /// new kind is a caller's business rather than a change here — the resolver
    /// never interprets it, it only groups by it.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// How far it reaches, or null for the default. Nullable so the ordinary
    /// case writes no key — the camera's rule.
    /// </summary>
    public ResourceReach? Reach { get; set; }

    /// <summary>The reach to actually use. Derived; never serialized.</summary>
    /// <remarks>
    /// <c>[JsonIgnore]</c> because a convenience getter beside a nullable field
    /// is still a property, and without it every declaration would write
    /// <c>"reachOrDefault": "subtree"</c> — reintroducing under a second name
    /// the key that making <see cref="Reach"/> nullable exists to remove.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public ResourceReach ReachOrDefault => Reach ?? ResourceReach.Subtree;
}

/// <summary>
/// Which resources a document can see, and in what order they win.
/// </summary>
/// <remarks>
/// <para>
/// The mechanism Q30 asked for. Modelled on <c>SymbolScopes</c>, which already
/// resolves a two-tier chain — this widens the idea rather than inventing it.
/// </para>
/// <para>
/// <b>The chain is four tiers</b>, and only the middle two live here:
/// <c>user library → project → folder path → document</c>. Brush tips and
/// symbols already ship the user↔project half; Q30 adds folder depth. Designing
/// only the tree axis would have stranded the two features that already have
/// the other one.
/// </para>
/// </remarks>
public static class ResourceScopes
{
    /// <summary>
    /// Every declaration of <paramref name="kind"/> that <paramref name="document"/>
    /// can see, nearest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Walk up, do not name a level.</b> The knight's palette is declared on
    /// the knight whether its animations sit directly underneath or two folders
    /// down — the artist's gesture is the same and resolution has to be too.
    /// That is also what makes the tree safe to rearrange: splitting a folder in
    /// two adds a level and changes nothing, which is exactly where the
    /// rejected *explicit per document* option would have broken.
    /// </para>
    /// <para>
    /// <b>Nearest wins ties, and locality beats publication.</b> Two
    /// declarations of the same id resolve to the nearer one, and a subtree
    /// declaration close to the document beats a project-wide one — so a
    /// knight's own red overrides the studio red without anything being
    /// unpublished.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ScopedResource> Resolve(
        ProjectManifest manifest, DocumentRef document, string kind)
    {
        var folder = ProjectFolders.ById(manifest, document.FolderId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<ScopedResource>();

        void Take(IEnumerable<ScopedResource>? declared)
        {
            if (declared is null) return;
            foreach (var r in declared)
            {
                if (!string.Equals(r.Kind, kind, StringComparison.Ordinal)) continue;
                if (seen.Add(r.Id)) found.Add(r);
            }
        }

        // 1. Up the tree, nearest first. AncestryOf runs root-first, so reverse.
        if (folder is not null)
        {
            var chain = ProjectFolders.AncestryOf(manifest, folder);
            for (var i = chain.Count - 1; i >= 0; i--) Take(chain[i].Resources);
        }

        // 2. The project itself — the scope above every folder.
        Take(manifest.Resources);

        // 3. Anything published from elsewhere in the tree. Last, so it loses
        //    every tie to something nearer, which is the locality rule.
        foreach (var other in ProjectFolders.All(manifest))
        {
            if (other.Resources is null) continue;
            Take(other.Resources.Where(r => r.ReachOrDefault == ResourceReach.Project));
        }

        return found;
    }

    /// <summary>
    /// The one that wins for <paramref name="kind"/>, or null if nothing is
    /// declared.
    /// </summary>
    /// <remarks>
    /// For the resources where an artist has one at a time — the palette a
    /// document paints from, the export configuration it uses. Resources that
    /// accumulate as a set, like references, want <see cref="Resolve"/>.
    /// </remarks>
    public static ScopedResource? Nearest(
        ProjectManifest manifest, DocumentRef document, string kind) =>
        Resolve(manifest, document, kind).FirstOrDefault();

    /// <summary>Declare a resource on a folder, or on the project when null.</summary>
    public static ScopedResource Declare(
        ProjectManifest manifest,
        ProjectFolder? scope,
        string kind,
        string id,
        ResourceReach reach = ResourceReach.Subtree)
    {
        var entry = new ScopedResource
        {
            Id = id,
            Kind = kind,
            // Absent unless it is the interesting value, so an ordinary
            // declaration adds no key to the file.
            Reach = reach == ResourceReach.Subtree ? null : reach,
        };
        if (scope is null) (manifest.Resources ??= []).Add(entry);
        else (scope.Resources ??= []).Add(entry);
        return entry;
    }

    /// <summary>
    /// Move a declaration up to the project so everything can see it — the
    /// gesture symbols already call <em>promoting</em>.
    /// </summary>
    /// <remarks>
    /// Reuses the word rather than inventing one: <c>PromotingCopiesUpAndKeepsTheId</c>
    /// is the existing behaviour for symbols, and an artist who has learned it
    /// there should not meet a second name for it here.
    /// </remarks>
    public static void Promote(ScopedResource resource) =>
        resource.Reach = ResourceReach.Project;
}
