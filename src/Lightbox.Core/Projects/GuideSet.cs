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

    /// <summary>
    /// The paper these guides were authored on, so a pull onto paper of
    /// another size can land them where they belong. Null — and absent from
    /// the file — on a set saved before sets remembered it.
    /// </summary>
    /// <remarks>
    /// <b>Absence is the migration.</b> A set with no canvas pulls verbatim in
    /// document pixels, which is exactly what every set did before, so an old
    /// project keeps behaving the way its author left it.
    /// </remarks>
    public GuideSetCanvas? Canvas { get; set; }
}

/// <summary>
/// The paper a <see cref="GuideSet"/> was authored on: what a pull measures
/// fractions against.
/// </summary>
/// <remarks>
/// Shaped like <see cref="Scene"/>'s own four numbers, origin included, because
/// it is a copy of them and a reader should not have to work that out. The
/// origin carries its null for <see cref="Scene.OriginX"/>'s reason — a set
/// saved on paper nobody grew writes no origin keys.
/// </remarks>
public sealed class GuideSetCanvas
{
    public int Width { get; set; }

    public int Height { get; set; }

    /// <inheritdoc cref="Scene.OriginX"/>
    public int? OriginX { get; set; }

    /// <inheritdoc cref="Scene.OriginY"/>
    public int? OriginY { get; set; }

    /// <inheritdoc cref="Scene.Left"/>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Left => OriginX ?? 0;

    /// <inheritdoc cref="Scene.Top"/>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Top => OriginY ?? 0;

    /// <summary>Paper with an area — a set authored on nothing cannot be fitted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUsable => Width > 0 && Height > 0;

    /// <summary>The paper a scene is offering right now.</summary>
    public static GuideSetCanvas Of(Scene scene) => new()
    {
        Width = scene.Width,
        Height = scene.Height,
        OriginX = scene.OriginX,
        OriginY = scene.OriginY,
    };

    public GuideSetCanvas Clone() => (GuideSetCanvas)MemberwiseClone();
}

/// <summary>
/// Carries a guide set onto paper of another size — Q181's first half.
/// </summary>
/// <remarks>
/// <para>
/// <b>Positions travel as fractions; sizes travel by one uniform factor, and
/// that factor comes from height.</b> Height, because the thing being kept is
/// a character's height in frame: a six-head chart filling 70% of a 4K scene
/// fills 70% of a 1080p one, and the artist's proportions survive the move
/// between files. Where the aspect matches — the ordinary case, and the one
/// the owner asked for — the two rules agree exactly and the whole set is a
/// plain uniform scale about the paper's corner.
/// </para>
/// <para>
/// <b>Uniform is not fussiness.</b> Scaling x by the width ratio and y by the
/// height ratio would land a height scale correctly and quietly corrupt three
/// other kinds on the way: every <see cref="GuideKind.Line"/> would tilt, an
/// <see cref="GuideKind.Isometric"/> would stop being isometric, and a
/// <see cref="GuideKind.Grid"/> would stop being square. One factor, taken
/// from the axis whose meaning is being preserved.
/// </para>
/// <para>
/// <b>Nothing here touches a stroke</b>, so invariant 7 is not in play: this
/// scales an aid the artist snaps to, and a snapped stroke has already
/// recorded the snapped point. Fitting a set can never move a line that was
/// drawn yesterday.
/// </para>
/// </remarks>
public static class GuideSetFit
{
    /// <summary>
    /// Copies of <paramref name="set"/>'s guides, placed for the paper
    /// <paramref name="onto"/> describes.
    /// </summary>
    /// <remarks>
    /// Ids are the caller's business — a pull wants fresh ones, and deciding
    /// that here would make this untestable without an id scheme.
    /// </remarks>
    public static List<Guide> Onto(GuideSet set, GuideSetCanvas onto)
    {
        var copies = set.Guides.Select(g => g.Clone()).ToList();
        if (set.Canvas is not { IsUsable: true } from || !onto.IsUsable) return copies;

        // The one factor, and the two fractions. See the remarks for why they
        // are not the same number.
        var scale = (double)onto.Height / from.Height;
        foreach (var guide in copies)
        {
            var fx = (guide.X - from.Left) / (double)from.Width;
            var fy = (guide.Y - from.Top) / (double)from.Height;
            guide.X = onto.Left + fx * onto.Width;
            guide.Y = onto.Top + fy * onto.Height;
            // A grid's pitch and a height scale's head. Angle and division
            // count are dimensionless and must not move: "six heads" is six
            // heads on any paper, which is the point of the exercise.
            guide.Spacing *= scale;
        }
        return copies;
    }
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
