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

    // A `Target` property lived here for the retired reference declarations —
    // the one kind whose id needed a second word. B133: nothing ever read it.
    // Old files carrying "target" keys still load; an unknown key is ignored.

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
        ProjectManifest manifest, DocumentRef document, string kind) =>
        Resolve(manifest, document, kind, user: null);

    /// <summary>
    /// The whole four-tier chain: <b>document → folder path → project → user</b>,
    /// nearest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two ends this class always described and never had.</b> Its own
    /// remarks say the chain is four deep; only the middle two were built, so
    /// "this one drawing paints from that palette" had no target and an artist's
    /// own library was reachable by one kind (<c>TipStore.Available</c>) and no
    /// mechanism.
    /// </para>
    /// <para>
    /// The precedence falls out of the order rather than being decided: a
    /// project <em>can</em> override an artist's default because the user tier
    /// is widest, and a document beats its folder because it is narrowest.
    /// Nothing new had to be invented for either sentence.
    /// </para>
    /// <para>
    /// <paramref name="user"/> is null for every caller that does not have a
    /// user library to offer, which is most of them — and a null user tier
    /// resolves exactly what <see cref="Resolve(ProjectManifest, DocumentRef, string)"/>
    /// always did.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ScopedResource> Resolve(
        ProjectManifest manifest,
        DocumentRef document,
        string kind,
        IReadOnlyList<ScopedResource>? user)
    {
        var found = new List<ScopedResource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 0. The document itself, nearest of all.
        Collect(document.Resources, kind, seen, found);

        foreach (var r in ResolveAt(manifest, ProjectFolders.ById(manifest, document.FolderId), kind))
        {
            if (seen.Add(r.Id)) found.Add(r);
        }

        // 4. The artist's own library, widest, so a project overrides it.
        Collect(user, kind, seen, found);
        return found;
    }

    private static void Collect(
        IEnumerable<ScopedResource>? declared,
        string kind,
        HashSet<string> seen,
        List<ScopedResource> into)
    {
        if (declared is null) return;
        foreach (var r in declared)
        {
            if (!string.Equals(r.Kind, kind, StringComparison.Ordinal)) continue;
            if (seen.Add(r.Id)) into.Add(r);
        }
    }

    /// <summary>
    /// The same walk, starting from a folder rather than from a document in one.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Once a character is a folder, things are asked about the
    /// folder itself — <em>what palette does this subject paint with</em> — with
    /// no document in hand to walk up from. Splitting the walk out is what stops
    /// that question needing a fake <see cref="DocumentRef"/> to answer.
    /// A null folder resolves the project tier and below, which is what a
    /// document at the root sees.
    /// </remarks>
    public static IReadOnlyList<ScopedResource> ResolveAt(
        ProjectManifest manifest, ProjectFolder? folder, string kind)
    {
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

    /// <summary>The one that wins for a document, with the artist's library behind it.</summary>
    public static ScopedResource? Nearest(
        ProjectManifest manifest,
        DocumentRef document,
        string kind,
        IReadOnlyList<ScopedResource>? user) =>
        Resolve(manifest, document, kind, user).FirstOrDefault();

    /// <summary>The one that wins for a folder. <see cref="ResolveAt"/>'s pair.</summary>
    public static ScopedResource? NearestAt(
        ProjectManifest manifest, ProjectFolder? folder, string kind) =>
        ResolveAt(manifest, folder, kind).FirstOrDefault();

    /// <summary>
    /// Take a declaration back off whatever it was declared on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One method for all three scopes, because "undeclare this" is one gesture
    /// and the caller holding the entry already knows nothing about where it
    /// came from. Empties back to null so a scope that declared something and
    /// then took it back is byte-identical to one that never did.
    /// </para>
    /// <para>
    /// <b>Deleting a declaration cannot change a drawing.</b> A scoped resource
    /// is a library to choose from; the choice is captured into the stroke
    /// record when an artist picks. <c>DeletingFromTheLibraryCannotChangeADrawing</c>
    /// is the standing guard and it applies here unchanged.
    /// </para>
    /// <para>
    /// Worth knowing for the narrowing kinds: taking back the <em>last</em>
    /// declaration of a kind puts the project back to "everything applies",
    /// because <c>AnyDeclared</c> is what switches scoping on. That is a real
    /// change of behaviour from one click, and a caller near an artist should
    /// say so.
    /// </para>
    /// </remarks>
    public static bool Undeclare(
        ProjectManifest manifest, ProjectFolder? scope, ScopedResource resource)
    {
        var declared = scope is null ? manifest.Resources : scope.Resources;
        if (declared is null || !declared.Remove(resource)) return false;
        if (declared.Count == 0)
        {
            if (scope is null) manifest.Resources = null;
            else scope.Resources = null;
        }
        return true;
    }

    /// <summary>Take a declaration back off a document.</summary>
    public static bool Undeclare(DocumentRef document, ScopedResource resource)
    {
        if (document.Resources is not { } declared || !declared.Remove(resource)) return false;
        if (declared.Count == 0) document.Resources = null;
        return true;
    }

    /// <summary>
    /// Remove every declaration of one asset, at every scope — what deleting
    /// the asset itself requires, or its declarations would name a thing the
    /// project no longer has.
    /// </summary>
    /// <remarks>
    /// The empty-list-to-null collapse matches <see cref="Undeclare"/>: a
    /// scope declaring nothing writes no key, which is the serialization rule
    /// optional things follow everywhere here.
    /// </remarks>
    public static void Retract(ProjectManifest manifest, string kind, string id) =>
        Retract(manifest, r => r.Kind == kind && r.Id == id);

    /// <summary>Remove every declaration of a whole kind — B133's load-time prune.</summary>
    public static void Retract(ProjectManifest manifest, string kind) =>
        Retract(manifest, r => r.Kind == kind);

    private static void Retract(ProjectManifest manifest, Predicate<ScopedResource> Match)
    {
        manifest.Resources?.RemoveAll(Match);
        if (manifest.Resources is { Count: 0 }) manifest.Resources = null;
        foreach (var folder in ProjectFolders.All(manifest))
        {
            folder.Resources?.RemoveAll(Match);
            if (folder.Resources is { Count: 0 }) folder.Resources = null;
        }
        foreach (var document in manifest.Documents)
        {
            document.Resources?.RemoveAll(Match);
            if (document.Resources is { Count: 0 }) document.Resources = null;
        }
    }

    /// <summary>Declare a resource on one document — the narrowest scope.</summary>
    /// <remarks>
    /// A separate method rather than an overload taking a nullable document,
    /// because "declare on nothing" already means the project and a second null
    /// with a different meaning is how a call site quietly does the wrong thing.
    /// </remarks>
    public static ScopedResource DeclareOn(
        DocumentRef document,
        string kind,
        string id)
    {
        var entry = new ScopedResource
        {
            Id = id,
            Kind = kind,
            // Reach is meaningless on a document — there is nothing below it to
            // subtree into, and publishing project-wide from one drawing is the
            // folder's job. Left null, so it writes no key.
        };
        (document.Resources ??= []).Add(entry);
        return entry;
    }

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

    /// <summary>Bring a published declaration back to its own subtree.</summary>
    /// <remarks>
    /// Writes null rather than <see cref="ResourceReach.Subtree"/>, so demoting
    /// leaves the record exactly as an ordinary declaration would have written
    /// it. Setting the enum's default value explicitly would serialize a
    /// <c>reach</c> key that says nothing — *optional means absent*, and a
    /// round trip through publish-and-unpublish must not be visible in the file.
    /// </remarks>
    public static void Demote(ScopedResource resource) => resource.Reach = null;

    /// <summary>
    /// Take a declaration back off the scope that made it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart <see cref="Declare"/> shipped without, and the omission
    /// was not harmless: a project becomes *scoped* the moment it declares its
    /// first resource — see <c>PaletteScopes.AnyDeclared</c> — so with no way
    /// back, one mistaken click changed how every document in the project
    /// resolved, permanently, from the artist's side.
    /// </para>
    /// <para>
    /// Removing the last declaration of a kind returns the project to unscoped,
    /// which is the same state it started in. That is the migration rule read
    /// backwards and it falls out rather than being special-cased.
    /// </para>
    /// </remarks>
    /// <returns>Whether the declaration was there to remove.</returns>
    public static bool Withdraw(
        ProjectManifest manifest, ProjectFolder? scope, ScopedResource resource)
    {
        var list = scope is null ? manifest.Resources : scope.Resources;
        if (list is null || !list.Remove(resource)) return false;
        // An empty list and no list mean the same thing and only one of them
        // writes a key.
        if (list.Count == 0)
        {
            if (scope is null) manifest.Resources = null;
            else scope.Resources = null;
        }
        return true;
    }
}

/// <summary>
/// Which of a project's palettes a document paints from.
/// </summary>
/// <remarks>
/// <para>
/// Q30, step 2. Until now every palette in a project was registered for every
/// document — <c>RegisterResources</c> concatenated <c>project.Palettes</c>
/// wholesale — so "shared palette across a character's animations" was true only
/// because nothing was scoped at all. That reads as working right up until a
/// project has two characters, and then the goblin's reds are in the knight's
/// picker.
/// </para>
/// <para>
/// <b>Old projects keep the old behaviour, which is Q30's migration answer
/// working rather than a special case.</b> A project that declares no palette
/// scopes gets everything, exactly as before; one that declares any is taken at
/// its word. That is *read both, write the new one* at the only place a reader
/// can tell them apart.
/// </para>
/// </remarks>
public static class PaletteScopes
{
    /// <summary>The kind string palettes are declared under.</summary>
    public const string Kind = "palette";

    /// <summary>Whether this project scopes its palettes at all.</summary>
    /// <remarks>
    /// The migration hinge. False for every project written before Q30, and for
    /// any project whose artist has never declared a scope — both of which mean
    /// "you have not told me, so do what you always did".
    /// </remarks>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The palette ids <paramref name="document"/> can paint from, nearest
    /// first, or null when the project scopes nothing and every palette applies.
    /// </summary>
    /// <remarks>
    /// Null rather than "all of them" on purpose: the caller has the palette
    /// list and this does not, and a null says *do not filter* without this
    /// having to know what it would have been filtering.
    /// </remarks>
    public static IReadOnlyList<string>? VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest)) return null;
        // A document the project does not know about — a loose file opened
        // alongside it — is not filtered either. It has no place in the tree to
        // resolve from, and hiding every swatch from it would be a worse answer
        // than showing too many.
        if (document is null) return null;
        return ResourceScopes.Resolve(manifest, document, Kind).Select(r => r.Id).ToList();
    }
}

/// <summary>
/// The kind key sheets are labelled with, and the ghost of a system.
/// </summary>
/// <remarks>
/// <para>
/// <b>B133, retired on the owner's call (2026-08-13).</b> This class carried
/// Q30's reference <em>declarations</em> — Declare, VisibleTo, OfTarget, a
/// target vocabulary of sheet/document/image — and measurement showed the
/// whole of it was write-only: every other scoped kind was declared and
/// consumed, references were declared and read by nothing. Meanwhile the
/// mechanism that actually delivers scoped reference art shipped beside it:
/// a sheet <em>filed on a folder</em> (<see cref="ProjectSheets.VisibleTo"/>)
/// reaches every document below, and the Reference sheets panel consumes it.
/// The choice was wire a consumer or retire the parallel system, and retire
/// won — a second route to the same promise, kept alive by nothing but its
/// producers, is where the next B133 comes from.
/// </para>
/// <para>
/// The kind string stays: it is how <see cref="AssetKinds"/> labels a sheet
/// (▤ Reference) on every surface. Declarations of this kind in old files are
/// pruned when the project loads.
/// </para>
/// </remarks>
public static class ReferenceScopes
{
    /// <summary>The kind key sheets wear as assets.</summary>
    public const string Kind = "reference";
}

/// <summary>
/// Which gradients a document can reach, and which template a new document in a
/// scope starts from.
/// </summary>
/// <remarks>
/// <para>
/// Q30 step 4. Both are the palette pattern applied again, which is the point —
/// once the resolver exists, a resource joins it by naming a kind rather than by
/// growing machinery of its own.
/// </para>
/// <para>
/// <b>Two of step 4's four could not join, and the reason is the same for
/// both.</b> Guides and export configuration have no project-level record to
/// point at — a guide lives on a document and nowhere else, and export settings
/// are chosen per export. Scoping needs something with an id that outlives one
/// document, so those two want that record built first and are not a line of
/// resolver away. Recorded here rather than in a commit message because the next
/// person to try will otherwise rediscover it.
/// </para>
/// </remarks>
public static class GradientScopes
{
    /// <summary>The kind string gradients are declared under.</summary>
    public const string Kind = "gradient";

    /// <summary>Whether this project scopes its gradients at all.</summary>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The gradient ids this document can use, or null when the project scopes
    /// none and every gradient applies.
    /// </summary>
    public static IReadOnlyList<string>? VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return ResourceScopes.Resolve(manifest, document, Kind).Select(r => r.Id).ToList();
    }
}

/// <summary>Which template a new document in a scope starts from.</summary>
/// <remarks>
/// <para>
/// Q30 step 4, and the cheapest of them because the machinery already shipped: a
/// template is an ordinary document with a flag, so a declaration points at a
/// <see cref="DocumentRef"/> id like anything else. What was missing was
/// somewhere to say *which* one a folder starts from — workflow 1's
/// <c>locomotion</c> folder wanting new animations to already know what they are.
/// </para>
/// <para>
/// Nearest wins, and only the nearest is asked for: a document starts from one
/// template or none, so accumulating them would be offering a choice nobody made.
/// </para>
/// </remarks>
public static class TemplateScopes
{
    /// <summary>The kind string a scope's default template is declared under.</summary>
    public const string Kind = "template";

    /// <summary>
    /// The template a new document in this scope should start from, or null.
    /// </summary>
    /// <param name="folder">
    /// The scope a new document is being made in — the selection, rather than an
    /// existing document, because the document does not exist yet.
    /// </param>
    public static string? DefaultFor(ProjectManifest manifest, ProjectFolder? folder)
    {
        // A stand-in document at the target scope, so the same walk answers for
        // a document that has not been created yet.
        var probe = new DocumentRef { FolderId = folder?.Id };
        return ResourceScopes.Resolve(manifest, probe, Kind).FirstOrDefault()?.Id;
    }

    /// <summary>Say that new documents in this scope start from this template.</summary>
    /// <remarks>
    /// Replaces rather than adds. A scope has one default, and two declarations
    /// of the same kind on one folder would make which-one-wins depend on
    /// insertion order — the kind of thing that reads as random.
    /// </remarks>
    public static void SetDefault(ProjectManifest manifest, ProjectFolder? scope, string templateId)
    {
        var list = scope is null ? manifest.Resources : scope.Resources;
        list?.RemoveAll(r => r.Kind == Kind);
        ResourceScopes.Declare(manifest, scope, Kind, templateId);
    }
}

/// <summary>Which export preset applies to a document, and where artifacts split.</summary>
/// <remarks>
/// <para>
/// Q30 for export. <see cref="ResourceScopes.Nearest"/> rather than
/// <c>Resolve</c>, because a document exports one way at a time — accumulating
/// presets would be offering a choice nobody made.
/// </para>
/// <para>
/// <b>The declaration is the artifact boundary</b>, which is what makes this
/// more than settings lookup: the folder that declares a preset is the folder
/// whose subtree becomes one deliverable, so nearest-wins gives the grouping for
/// free. See the design doc for why that beat a separate boundary declaration.
/// </para>
/// </remarks>
public static class ExportScopes
{
    /// <summary>The kind string export presets are declared under.</summary>
    public const string Kind = "export";

    /// <summary>Whether this project scopes export at all.</summary>
    /// <remarks>
    /// The migration hinge. False for every project written before this, which
    /// then keeps using the user's preset store exactly as it does today.
    /// </remarks>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The preset id this document exports with, or null when the project
    /// declares none.
    /// </summary>
    public static string? PresetFor(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return ResourceScopes.Nearest(manifest, document, Kind)?.Id;
    }

    /// <summary>
    /// The folder whose declaration governs this document — the artifact it
    /// belongs to. Null means the project's own scope, or none at all.
    /// </summary>
    /// <remarks>
    /// Answering *which artifact does this document belong to* is what makes a
    /// status change able to say what it invalidated. Without it, "one animation
    /// reached Ready" has nothing to rebuild.
    /// </remarks>
    public static ProjectFolder? BoundaryFor(ProjectManifest manifest, DocumentRef? document)
    {
        if (document is null) return null;
        var folder = ProjectFolders.ById(manifest, document.FolderId);
        if (folder is null) return null;
        var chain = ProjectFolders.AncestryOf(manifest, folder);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Resources?.Any(r => r.Kind == Kind) ?? false) return chain[i];
        }
        return null;
    }

    /// <summary>Declare an export preset on a scope.</summary>
    /// <remarks>Replaces: a scope exports one way, so two would be ambiguous.</remarks>
    public static void SetPreset(ProjectManifest manifest, ProjectFolder? scope, string presetId)
    {
        var list = scope is null ? manifest.Resources : scope.Resources;
        list?.RemoveAll(r => r.Kind == Kind);
        ResourceScopes.Declare(manifest, scope, Kind, presetId);
    }
}

/// <summary>Which of a project's brush tips a document is offered.</summary>
/// <remarks>
/// <para>
/// <b>The last thing Q30's design doc promised, and it sat outside the five
/// steps by its own admission</b> — *"Pillar 0's tip library already says
/// scoped like palettes, so it joins whenever step 1 exists."* Step 1 existed
/// for some time before anyone came back for this, which is the ordinary way a
/// line like that goes unhonoured: nothing is broken, so nothing asks.
/// </para>
/// <para>
/// It behaves like symbols rather than like palettes, and the distinction is
/// the one that matters when adding a kind: a tip is already offered to every
/// document, so declaring one <b>narrows</b>. Null means unscoped and therefore
/// *all of them*, which is what every project in existence means.
/// </para>
/// <para>
/// <b>The user library and the built-in catalogue are never narrowed.</b> A
/// scope governs the project's own tips; the artist's library follows them
/// between projects and the built-ins are always there. Painting with either
/// copies the raster into the document — <c>TipStore.AdoptInto</c>, the same
/// trade symbols make — so a scope has nothing to say about them.
/// </para>
/// </remarks>
public static class TipScopes
{
    /// <summary>The kind string a brush tip is declared under.</summary>
    public const string Kind = "tip";

    /// <summary>Whether this project scopes its tips at all.</summary>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// The tip ids this document is offered, or null when nothing is scoped.
    /// </summary>
    public static IReadOnlyList<string>? VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return [.. ResourceScopes.Resolve(manifest, document, Kind).Select(r => r.Id)];
    }

    /// <summary>Whether a document is offered this tip — true when nothing is scoped.</summary>
    public static bool CanUse(ProjectManifest manifest, DocumentRef? document, string tipId) =>
        VisibleTo(manifest, document) is not { } allowed || allowed.Contains(tipId);
}
