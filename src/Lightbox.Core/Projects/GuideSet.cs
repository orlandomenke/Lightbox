using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>
/// A named set of guides that outlives one document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q30 step 4's other half.</b> Guides could not be scoped when gradients and
/// templates were, and the reason was not the resolver — it was that a guide
/// lived on a document and nowhere else, so there was nothing with an id for a
/// scope to point at. This is that record, and it is deliberately the smallest
/// thing that fixes it: an id, a name, and the guides themselves.
/// </para>
/// <para>
/// The roadmap's <c>[?] Character height guide</c> is what this is for. A height
/// guide declared on the knight folder, shared by every drawing under it, is a
/// guide set and a scope declaration and nothing else — there was never another
/// thing it could have been.
/// </para>
/// <para>
/// <b>Copied into a document, not resolved at render time.</b> A guide reaches
/// what an artist draws by snapping to it, so a document whose guides changed
/// because it was dragged into another folder would be the same defect scoping
/// exists to avoid. The set is a library to pull from; <see cref="Guide"/>
/// objects land in the document that uses them.
/// </para>
/// </remarks>
public sealed class GuideSet
{
    public string Id { get; set; } = Ids.NewId("guides");

    public string Name { get; set; } = "Guides";

    /// <summary>The guides themselves, in the order they were made.</summary>
    public List<Guide> Guides { get; set; } = [];
}

/// <summary>Which guide sets a document can pull from.</summary>
/// <remarks>
/// The palette pattern again, so nothing here is new except the record it points
/// at. A project that declares none resolves to null and the caller offers
/// whatever it offered before — Q30's new-projects-only migration.
/// </remarks>
public static class GuideScopes
{
    /// <summary>The kind string guide sets are declared under.</summary>
    public const string Kind = "guides";

    /// <summary>Whether this project scopes guide sets at all.</summary>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The guide-set ids this document can pull from, nearest first, or null
    /// when the project scopes none.
    /// </summary>
    public static IReadOnlyList<string>? VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return ResourceScopes.Resolve(manifest, document, Kind).Select(r => r.Id).ToList();
    }
}
