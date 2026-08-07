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
    /// What <see cref="Id"/> points at, when the kind alone does not say.
    /// </summary>
    /// <remarks>
    /// <b>Q30 step 3.</b> A palette id is a palette and needs no second word. A
    /// <em>reference</em> is different: workflow 1 wants a multi-view sheet —
    /// Front, Side, Back, Expressions — and workflow 3 wants one large
    /// environment document, and those are genuinely different shapes rather
    /// than one being a bigger version of the other. See
    /// <see cref="ReferenceTargets"/>.
    /// <para>
    /// Nullable and absent unless a kind needs it, so palettes and gradients
    /// carry no empty word.
    /// </para>
    /// </remarks>
    public string? Target { get; set; }

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
        ResourceReach reach = ResourceReach.Subtree,
        string? target = null)
    {
        var entry = new ScopedResource
        {
            Id = id,
            Kind = kind,
            Target = target,
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

/// <summary>What a reference points at.</summary>
/// <remarks>
/// Three, because the four workflows asked for reference art of genuinely
/// different shapes rather than one shape at different sizes. Plain strings for
/// the same reason <see cref="ScopedResource.Kind"/> is one: a fourth is a
/// caller's business, and the resolver never interprets them.
/// </remarks>
public static class ReferenceTargets
{
    /// <summary>
    /// A multi-view sheet — Front, Side, Back, Expressions — each view its own
    /// canvas. What a character sheet has always been.
    /// </summary>
    public const string Sheet = "sheet";

    /// <summary>
    /// An ordinary document in the project, drawn against as reference.
    /// Workflow 3's one large environment layout, which is not a sheet and
    /// should not be forced into being one.
    /// </summary>
    public const string Document = "document";

    /// <summary>An imported image, relative to the project root.</summary>
    public const string Image = "image";
}

/// <summary>
/// Which references a document can draw against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q30 step 3, and the character sheet's redesign is mostly this.</b> The
/// <c>ReferenceSheet</c> record was already generic — a named set of views with
/// static layer stacks, with nothing about characters in it. What was not
/// generic was <em>where a sheet could live</em>: inside one document, in
/// <c>Doc.ReferenceSheets</c>, so it could not be shared or filed. And
/// <c>Character.References</c> was a list of paths bolted to a character, so
/// workflow 3's environment document could not be one.
/// </para>
/// <para>
/// Both are scope problems rather than shape problems, which is why this is a
/// resolver and not a new record.
/// </para>
/// </remarks>
public static class ReferenceScopes
{
    /// <summary>The kind string references are declared under.</summary>
    public const string Kind = "reference";

    /// <summary>Whether this project scopes its references at all.</summary>
    /// <remarks>
    /// The migration hinge, the same one palettes use: false for every project
    /// written before Q30, which then keeps reading <c>Character.References</c>
    /// exactly as it did.
    /// </remarks>
    public static bool AnyDeclared(ProjectManifest manifest) =>
        (manifest.Resources?.Any(r => r.Kind == Kind) ?? false)
        || ProjectFolders.All(manifest).Any(f => f.Resources?.Any(r => r.Kind == Kind) ?? false);

    /// <summary>
    /// Every reference <paramref name="document"/> can draw against, nearest
    /// first, or null when the project scopes none.
    /// </summary>
    public static IReadOnlyList<ScopedResource>? VisibleTo(
        ProjectManifest manifest, DocumentRef? document)
    {
        if (!AnyDeclared(manifest) || document is null) return null;
        return ResourceScopes.Resolve(manifest, document, Kind);
    }

    /// <summary>Declare a reference on a scope.</summary>
    /// <param name="target">One of <see cref="ReferenceTargets"/>.</param>
    public static ScopedResource Declare(
        ProjectManifest manifest,
        ProjectFolder? scope,
        string id,
        string target,
        ResourceReach reach = ResourceReach.Subtree) =>
        ResourceScopes.Declare(manifest, scope, Kind, id, reach, target);

    /// <summary>Only the references of one target kind, nearest first.</summary>
    /// <remarks>
    /// The reference panel wants sheets and the canvas wants everything, so the
    /// filter belongs here rather than in three callers reimplementing it.
    /// </remarks>
    public static IReadOnlyList<ScopedResource> OfTarget(
        ProjectManifest manifest, DocumentRef? document, string target) =>
        VisibleTo(manifest, document)?
            .Where(r => string.Equals(r.Target, target, StringComparison.Ordinal))
            .ToList() ?? [];
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
