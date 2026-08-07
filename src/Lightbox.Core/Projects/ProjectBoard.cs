namespace Lightbox.Core.Projects;

/// <summary>
/// What the project window asks a project, and what it changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Model code, for the reason Q29 gave about the tree.</b> The docker and the
/// window are two surfaces onto one project, so anything either of them needs to
/// <em>know</em> lives here rather than in whichever view model happened to want
/// it first — that is how a second implementation gets written by somebody who
/// cannot change the first.
/// </para>
/// <para>
/// <b>None of this reaches a pixel.</b> Tags, status and assignment are
/// production metadata about a drawing rather than part of it: changing one
/// touches no stroke, needs no document open, and cannot alter what re-renders.
/// Invariant 1 is not in play anywhere in this file, which is also why Q44 could
/// answer "no undo" — setting a status back is the same gesture as setting it.
/// </para>
/// </remarks>
public static class ProjectBoard
{
    // ---- tags ------------------------------------------------------------------

    /// <summary>
    /// Every tag in use anywhere in the project, sorted, without duplicates.
    /// </summary>
    /// <remarks>
    /// <b>The vocabulary is derived rather than declared.</b> Typing a new tag
    /// adds it by using it, and the offered list is whatever the project already
    /// says. A declared vocabulary is a registry somebody maintains and a wall in
    /// front of the first word that is not in it — the same argument that took
    /// the New menu from six entries to two.
    /// <para>
    /// Folders and documents share one vocabulary on purpose: <c>rough</c> means
    /// the same thing wherever it is put, and two lists would be two spellings.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> TagsInUse(ProjectManifest manifest)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in ProjectFolders.All(manifest))
        {
            foreach (var tag in folder.Tags ?? []) Add(found, tag);
        }
        foreach (var document in manifest.Documents)
        {
            foreach (var tag in document.Tags ?? []) Add(found, tag);
        }
        return [.. found];

        static void Add(SortedSet<string> into, string tag)
        {
            var trimmed = tag.Trim();
            if (trimmed.Length > 0) into.Add(trimmed);
        }
    }

    /// <summary>Put a tag on a document. False when it was already there.</summary>
    /// <remarks>
    /// Case-insensitive, because <c>Rough</c> and <c>rough</c> being two tags is
    /// the spreadsheet problem this window exists to remove.
    /// </remarks>
    public static bool Tag(DocumentRef document, string tag)
    {
        var wanted = tag.Trim();
        if (wanted.Length == 0) return false;
        document.Tags ??= [];
        if (document.Tags.Contains(wanted, StringComparer.OrdinalIgnoreCase)) return false;
        document.Tags.Add(wanted);
        return true;
    }

    /// <summary>Take a tag off a document. False when it was not there.</summary>
    /// <remarks>
    /// Empties back to null, so a document tagged and then untagged is
    /// byte-identical to one nobody touched — absent unless used, in both
    /// directions.
    /// </remarks>
    public static bool Untag(DocumentRef document, string tag)
    {
        if (document.Tags is not { } tags) return false;
        var removed = tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        if (tags.Count == 0) document.Tags = null;
        return true;
    }

    /// <summary>Put a tag on a folder. False when it was already there.</summary>
    public static bool Tag(ProjectFolder folder, string tag)
    {
        var wanted = tag.Trim();
        if (wanted.Length == 0) return false;
        folder.Tags ??= [];
        if (folder.Tags.Contains(wanted, StringComparer.OrdinalIgnoreCase)) return false;
        folder.Tags.Add(wanted);
        return true;
    }

    /// <summary>Take a tag off a folder. False when it was not there.</summary>
    public static bool Untag(ProjectFolder folder, string tag)
    {
        if (folder.Tags is not { } tags) return false;
        var removed = tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        if (tags.Count == 0) folder.Tags = null;
        return true;
    }

    /// <summary>
    /// Every tag that reaches a document: its own, and every folder above it.
    /// </summary>
    /// <remarks>
    /// <b>Tags inherit down the tree</b>, the way scoped resources do, and for
    /// the reason Q31 raised: tagging <c>characters/</c> once is how "every
    /// character animation" becomes expressible without listing them. A document
    /// cannot decline an inherited tag — the same thing
    /// <c>DESIGN-scoped-resources.md</c> deliberately leaves unsolved for
    /// resources, and inventing a negation before somebody needs one has been
    /// wrong every time.
    /// </remarks>
    public static IReadOnlyList<string> TagsReaching(ProjectManifest manifest, DocumentRef document)
    {
        var found = new List<string>(document.Tags ?? []);
        if (ProjectFolders.ById(manifest, document.FolderId) is { } folder)
        {
            // AncestryOf runs root-first; nearest-first keeps the document's own
            // tags in front, which is the order every other resolution uses.
            var chain = ProjectFolders.AncestryOf(manifest, folder);
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var tag in chain[i].Tags ?? [])
                {
                    if (!found.Contains(tag, StringComparer.OrdinalIgnoreCase)) found.Add(tag);
                }
            }
        }
        return found;
    }

    // ---- people ----------------------------------------------------------------

    /// <summary>The people on this project, or an empty list when nobody is.</summary>
    public static IReadOnlyList<Person> People(ProjectManifest manifest) => manifest.People ?? [];

    public static Person? PersonById(ProjectManifest manifest, string? id) =>
        id is null ? null : People(manifest).FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Add somebody, or return the person already named that.
    /// </summary>
    /// <remarks>
    /// Returning the existing one rather than making a second is the whole point
    /// of Q43's registry: two spellings of one person is the spreadsheet problem,
    /// and adding "Ana" twice is the commonest way to create one.
    /// </remarks>
    public static Person AddPerson(ProjectManifest manifest, string name)
    {
        var wanted = name.Trim();
        manifest.People ??= [];
        if (manifest.People.FirstOrDefault(
                p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            return existing;
        }
        var person = new Person { Name = wanted };
        manifest.People.Add(person);
        return person;
    }

    /// <summary>How many documents name this person, for a confirmation to read.</summary>
    /// <remarks>
    /// <b>Q35's pattern, pointed at people.</b> Removing somebody unassigns
    /// however many documents named them, and the gesture says which — never a
    /// bare "are you sure".
    /// </remarks>
    public static int AssignedCount(ProjectManifest manifest, Person person) =>
        manifest.Documents.Count(d => d.AssigneeId == person.Id);

    /// <summary>
    /// Take somebody off the project, unassigning whatever named them.
    /// </summary>
    /// <remarks>
    /// The documents are unassigned rather than left pointing at a person who is
    /// gone. That is the opposite of the palette path's dangling-id answer, and
    /// deliberately: a palette id that resolves to nothing is a drawing that
    /// still paints, while an assignee that resolves to nothing is a row saying
    /// somebody is working on it who is not.
    /// </remarks>
    public static int RemovePerson(ProjectManifest manifest, Person person)
    {
        if (manifest.People is not { } people) return 0;
        var unassigned = 0;
        foreach (var document in manifest.Documents.Where(d => d.AssigneeId == person.Id))
        {
            document.AssigneeId = null;
            unassigned++;
        }
        people.RemoveAll(p => p.Id == person.Id);
        if (people.Count == 0) manifest.People = null;
        return unassigned;
    }

    /// <summary>Point a document at somebody, or at nobody.</summary>
    public static void Assign(DocumentRef document, Person? person) =>
        document.AssigneeId = person?.Id;

    /// <summary>Everything this person is on.</summary>
    public static IReadOnlyList<DocumentRef> AssignedTo(ProjectManifest manifest, Person person) =>
        [.. manifest.Documents.Where(d => d.AssigneeId == person.Id)];

    // ---- the summary the window's footer reads ------------------------------------

    /// <summary>
    /// What the project holds and what is wrong with it.
    /// </summary>
    /// <remarks>
    /// <b>One place counting, because three would drift.</b> The footer, the
    /// status view and <c>ExportPlan.Describe</c> all say how many documents
    /// there are, and the verification for this feature makes them agree by
    /// having one of them do the counting.
    /// </remarks>
    /// <param name="Documents">Every document in the project.</param>
    /// <param name="ByStatus">
    /// How many are at each status. A status nobody has used is absent rather
    /// than zero, so the footer lists what is true instead of the whole enum.
    /// </param>
    /// <param name="Unset">How many nobody has given a status.</param>
    /// <param name="Unassigned">How many nobody is on.</param>
    public readonly record struct BoardSummary(
        int Documents,
        IReadOnlyDictionary<AssetStatus, int> ByStatus,
        int Unset,
        int Unassigned);

    public static BoardSummary Summarise(ProjectManifest manifest)
    {
        var byStatus = new Dictionary<AssetStatus, int>();
        var unset = 0;
        var unassigned = 0;
        foreach (var document in manifest.Documents)
        {
            if (document.Status is { } status)
            {
                byStatus[status] = byStatus.GetValueOrDefault(status) + 1;
            }
            else
            {
                unset++;
            }
            if (document.AssigneeId is null) unassigned++;
        }
        return new BoardSummary(manifest.Documents.Count, byStatus, unset, unassigned);
    }

    // ---- what a scope can be given ---------------------------------------------

    /// <summary>Something declarable, named for a person rather than by id.</summary>
    public readonly record struct Offer(string Id, string Name);

    /// <summary>
    /// The kinds a scope can declare, in the order a surface should list them.
    /// </summary>
    /// <remarks>
    /// Named here rather than discovered from what is already declared: a column
    /// that appears only once something exists in it is a column an artist
    /// cannot use to declare the first one.
    /// </remarks>
    public static readonly IReadOnlyList<string> Kinds =
    [
        PaletteScopes.Kind, GradientScopes.Kind, ReferenceScopes.Kind, GuideScopes.Kind,
        TemplateScopes.Kind, ExportScopes.Kind, SymbolScopes.Kind, TipScopes.Kind,
    ];

    /// <summary>
    /// What a scope could be given, of one kind.
    /// </summary>
    /// <remarks>
    /// <b>One place, because there were about to be two.</b> The docker has seven
    /// <c>ShareableX</c> properties and the project window needs the same seven
    /// lists; a second copy is a second thing to update when a ninth kind
    /// arrives. Model code for the reason Q29 gave about the tree.
    /// <para>
    /// <c>reference</c> is deliberately absent: a reference binds to a
    /// <em>target</em> as well as an id (<see cref="ReferenceTargets"/>), so
    /// offering it as a flat list would produce a declaration that names a sheet
    /// and not what to do with it. It stays a surface that knows about targets.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Offer> Offers(Project project, string kind)
    {
        var manifest = project.Manifest;
        return kind switch
        {
            PaletteScopes.Kind => [.. Named(project.Palettes.Select(p => new Offer(p.Id, p.Name)))],
            GradientScopes.Kind => [.. Named(project.Gradients.Values.Select(g => new Offer(g.Id, g.Name)))],
            SymbolScopes.Kind => [.. Named(project.Symbols.Values.Select(s => new Offer(s.Id, s.Name)))],
            TipScopes.Kind => [.. Named((manifest.Tips ?? []).Select(t => new Offer(t.Id, t.Name)))],
            GuideScopes.Kind => [.. Named((manifest.GuideSets ?? []).Select(g => new Offer(g.Id, g.Name)))],
            // The project's own first, then the built-ins — the order the docker
            // already offers them in, so the two surfaces agree.
            ExportScopes.Kind =>
            [
                .. (manifest.ExportPresets ?? []).Select(p => new Offer(p.Id, p.Name)),
                .. ExportPreset.BuiltIns.Select(p => new Offer(p.Id, p.Name)),
            ],
            TemplateScopes.Kind => [.. Templates.InProject(project).Select(d => new Offer(d.Id, d.Name))],
            _ => [],
        };

        static IEnumerable<Offer> Named(IEnumerable<Offer> offers) =>
            offers.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>What an id resolves to, for a chip to read as a name.</summary>
    /// <remarks>
    /// Falls back to the id rather than to nothing: a declaration naming
    /// something deleted is a real state — the palette path has had it since
    /// Q30 — and an empty chip would hide it rather than showing it as broken.
    /// </remarks>
    public static string NameOf(Project project, string kind, string id) =>
        Offers(project, kind).FirstOrDefault(o => o.Id == id) is { Name.Length: > 0 } found
            ? found.Name
            : id;

    /// <summary>The documents at one status, in the order the project lists them.</summary>
    /// <remarks>
    /// Null means <em>nobody has said</em>, which stays distinct from Design —
    /// the distinction <see cref="DocumentRef.Status"/> exists to keep, and the
    /// status board shows it as its own column rather than folding it into the
    /// first real one.
    /// </remarks>
    public static IReadOnlyList<DocumentRef> At(ProjectManifest manifest, AssetStatus? status) =>
        [.. manifest.Documents.Where(d => d.Status == status)];
}
