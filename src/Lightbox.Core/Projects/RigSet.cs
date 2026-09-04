using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>
/// A named skeleton that outlives one document: the human, the dog, the
/// goblin, kept so the next drawing can be built against them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q181.</b> <see cref="GuideSet"/>'s shape, for <see cref="GuideSet"/>'s
/// reason — an id, a name and the thing itself — and the same rule about
/// copies: the set is a library, so re-proportioning a bone in a drawing
/// afterwards must not silently redraw every character that pulled from it.
/// </para>
/// <para>
/// <b>What makes it more than a saved rig is <see cref="Heads"/>.</b> A guide
/// set travels as a fraction of the canvas because a guide's job is framing; a
/// rig travels in <em>head units</em> because a rig's job is proportion. The
/// human at 7.5 heads and the goblin at 4.5 keep their relationship on any
/// paper at any resolution, because both are measured in the same unit and the
/// document's own height scale converts it back into pixels. That is the whole
/// idea, and the rest is arithmetic.
/// </para>
/// </remarks>
public sealed class RigSet
{
    public string Id { get; set; } = Ids.NewId("rig");

    public string Name { get; set; } = "Rig";

    /// <summary>The skeleton itself, in its bind pose.</summary>
    public Armature Armature { get; set; } = new();

    /// <summary>
    /// The paper it was authored on, so a pull can fall back to the guide-set
    /// rule when there is no height scale to measure against.
    /// </summary>
    public AuthoredCanvas? Canvas { get; set; }

    /// <summary>
    /// How many head units tall it stood when it was saved, or null — and
    /// absent from the file — when there was no height scale to measure it
    /// against.
    /// </summary>
    /// <remarks>
    /// <b>Null is a real state, not a missing value.</b> A rig saved on a
    /// document with no character height scale has no head count anybody
    /// measured, and inventing one from the canvas would be a guess dressed as
    /// a proportion — so the pull falls back to the canvas rule and says so.
    /// </remarks>
    public double? Heads { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public RigSet Clone()
    {
        var copy = (RigSet)MemberwiseClone();
        copy.Armature = Armature.Clone();
        copy.Canvas = Canvas?.Clone();
        return copy;
    }
}

/// <summary>Which rig sets a document can pull from.</summary>
/// <remarks>
/// <see cref="GuideScopes"/> with one word changed. The palette pattern again,
/// so nothing here is new except the record it points at.
/// </remarks>
public static class RigScopes
{
    /// <summary>The kind string rig sets are declared under.</summary>
    public const string Kind = "rigs";

    /// <summary>Whether this project scopes rig sets at all.</summary>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The rig-set ids this document can pull from, nearest first, or null
    /// when the project scopes none.
    /// </summary>
    public static IReadOnlyList<string>? VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return ResourceScopes.Resolve(manifest, document, Kind).Select(r => r.Id).ToList();
    }
}
