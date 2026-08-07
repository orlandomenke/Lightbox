using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// A person, and what removing them would cost, for a row to bind to.
/// </summary>
/// <remarks>
/// A view type wrapping <see cref="Person"/> rather than the model type itself,
/// for the reason <c>ScopeMenuEntry</c> already gives: the model holds an id
/// because that is what survives a rename, and a list holding ids is a list
/// nobody can read. It also carries the sentence Q35's pattern wants — "Ana is
/// on 4 documents, which will be unassigned" — computed once rather than by a
/// row reaching back into the manifest.
/// </remarks>
public sealed record PersonChoice(Person Person, string Cost)
{
    /// <summary>
    /// Two-way, so editing the box renames the person everywhere at once —
    /// which is the whole reason Q43 chose a registry over a typed name.
    /// </summary>
    public string Name
    {
        get => Person.Name;
        set => Person.Name = value;
    }

    public override string ToString() => Person.Name;
}

/// <summary>One status a bulk edit can apply, including "no status".</summary>
/// <remarks>
/// A wrapper because the list has to contain <c>null</c> as a real choice —
/// "nobody has said" is distinct from Design and clearing a status is a thing an
/// artist does — and a <c>ComboBox</c> cannot tell a null item from no selection.
/// </remarks>
public sealed record StatusChoice(AssetStatus? Status, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One column of the status board.</summary>
/// <param name="Status">Null is "nobody has said", which is its own column.</param>
public sealed record StatusColumn(
    AssetStatus? Status, string Label, string Color, IReadOnlyList<BoardRow> Rows)
{
    public int Count => Rows.Count;

    public bool IsEmpty => Rows.Count == 0;
}

/// <summary>One cell of the Assets table: what a scope declares of one kind.</summary>
public sealed record AssetCell(string Kind, IReadOnlyList<ScopedResource> Declared)
{
    public int Count => Declared.Count;

    public bool Any => Declared.Count > 0;

    /// <summary>The ids, so the cell says what rather than how many.</summary>
    public string Text => string.Join(", ", Declared.Select(d => d.Id));
}

/// <summary>One row of the Assets table: a scope, and what it declares.</summary>
/// <param name="Depth">Zero for the project, then folder depth, then documents.</param>
public sealed record AssetScope(string Name, int Depth, IReadOnlyList<AssetCell> Cells)
{
    public double Indent => Depth * 16;

    public bool DeclaresNothing => Cells.All(c => !c.Any);
}

/// <summary>
/// One line in the project window's Structure tab — a folder or a document,
/// with the columns the docker has no width for.
/// </summary>
/// <remarks>
/// <b>A second row type, and deliberately not <see cref="ProjectRow"/>.</b> That
/// one carries a docker's worth of interaction state — renaming, collapse,
/// pending, missing — and reusing it here would make every one of those the
/// window's problem too. What the two share is the <em>traversal</em>, and that
/// lives in <c>ProjectFolders</c> where Q29 put it.
/// </remarks>
public sealed partial class BoardRow : ObservableObject
{
    public BoardRow(ProjectFolder folder, int depth)
    {
        Folder = folder;
        Depth = depth;
    }

    public BoardRow(DocumentRef document, ProjectFolder? folder, int depth)
    {
        Document = document;
        Folder = folder;
        Depth = depth;
    }

    /// <summary>The folder this row is, or the one a document is filed in.</summary>
    public ProjectFolder? Folder { get; }

    /// <summary>The document this row is, or null on a folder row.</summary>
    public DocumentRef? Document { get; }

    public int Depth { get; }

    public bool IsFolder => Document is null;

    public double Indent => Depth * 16;

    public string Glyph =>
        Document is not null ? "▣"
        : Folder is { Icon: { Length: > 0 } chosen } ? chosen
        : "🗀";

    public string Name => Document?.Name ?? Folder?.Name ?? "";

    /// <summary>Every tag on this row, and on the folders above a document.</summary>
    /// <remarks>
    /// Inherited tags are shown rather than hidden, because *why does this
    /// appear when I filter by "hero"* is otherwise unanswerable from the row.
    /// Set once at build time — the window rebuilds on every edit, so a live
    /// property would recompute an ancestry walk per repaint for no gain.
    /// </remarks>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>What is set on this row itself, so an edit knows what it removes.</summary>
    public IReadOnlyList<string> OwnTags =>
        (Document?.Tags ?? Folder?.Tags ?? []).ToList();

    public AssetStatus? Status => Document?.Status;

    public string StatusLabel => Status is { } s ? AssetStatuses.Label(s) : "";

    public string StatusColor => Status is { } s ? AssetStatuses.Color(s) : "#00000000";

    public bool HasStatus => Status is not null;

    /// <summary>Who is on it, resolved through the project's people.</summary>
    public Person? Assignee { get; init; }

    public string AssigneeName => Assignee?.Name ?? "";

    public bool HasAssignee => Assignee is not null;

    /// <summary>How long it runs, already formatted, or empty when nothing knows.</summary>
    public string Length { get; init; } = "";

    public bool HasLength => Length.Length > 0;

    /// <summary>What a folder carries, for the column that reads it at a glance.</summary>
    /// <remarks>
    /// Q39 kept this out of the docker's rows because a tree of forty has to
    /// stay scannable at 200 pixels. This window is the place it fits, and the
    /// window is where a facet is edited — which is what closes Q39's cost.
    /// </remarks>
    public string Carries { get; init; } = "";
}

/// <summary>
/// The project window: what you do between drawings.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q29's other surface, and Q41 put it in its own window.</b> The docker does
/// what you do while drawing — find it, open it, move it, rename it. This does
/// bulk operations, tagging, assignment and status across a production, none of
/// which fits in 200 pixels beside a canvas.
/// </para>
/// <para>
/// <b>It does not own the tree.</b> Every traversal is <c>ProjectFolders</c> and
/// every question about the project is <c>ProjectBoard</c>; Q29 answered that in
/// advance precisely so this class could not grow a second implementation.
/// </para>
/// <para>
/// <b>No undo, by Q44.</b> Status, tags and assignment are manifest metadata —
/// changing one touches no stroke and needs no document open — so setting one
/// back is the same gesture as setting it. Nothing here is destructive, and
/// nothing here reaches a pixel.
/// </para>
/// </remarks>
public sealed partial class ProjectWindowViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action _changed;

    /// <param name="changed">
    /// Called after anything is edited, so the owner can mark the project unsaved
    /// and the docker can re-read. Supplied rather than reached for, so a test
    /// can drive this window with no window at all.
    /// </param>
    public ProjectWindowViewModel(Project project, Action? changed = null)
    {
        _project = project;
        _changed = changed ?? (() => { });
        Rebuild();
    }

    public Project Project => _project;

    private ProjectManifest Manifest => _project.Manifest;

    public string Title => $"{_project.Name} — project";

    // ---- structure ----------------------------------------------------------------

    public ObservableCollection<BoardRow> Rows { get; } = [];

    /// <summary>
    /// The rows the next bulk edit acts on.
    /// </summary>
    /// <remarks>
    /// <b>The difference from the docker in one property.</b> The docker has no
    /// multi-select on purpose — a bulk operation is what you do between
    /// drawings rather than during one — and every command below reads this
    /// rather than a single selection.
    /// </remarks>
    public ObservableCollection<BoardRow> Selected { get; } = [];

    public bool HasSelection => Selected.Count > 0;

    /// <summary>
    /// Replace the selection — what the list's <c>SelectionChanged</c> hands over.
    /// </summary>
    /// <remarks>
    /// A method rather than a bound <c>SelectedItems</c>, because Avalonia
    /// exposes that as a non-generic <c>IList</c> and binding it would put a
    /// cast in front of every read here. This also keeps the bulk commands
    /// drivable by a test that never made a control.
    /// </remarks>
    public void SetSelection(IEnumerable<BoardRow> rows)
    {
        Selected.Clear();
        foreach (var row in rows) Selected.Add(row);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionLabel));
    }

    /// <summary>Only the documents in the selection — a folder has no status.</summary>
    private List<DocumentRef> SelectedDocuments =>
        [.. Selected.Select(r => r.Document).OfType<DocumentRef>()];

    /// <summary>
    /// Filter the tree to rows carrying a tag, or show everything when null.
    /// </summary>
    /// <remarks>
    /// A folder is kept when anything under it matches, or the match would be an
    /// orphaned document with no path back to the root — which reads as the tree
    /// being broken rather than as a filter being on.
    /// </remarks>
    [ObservableProperty]
    private string? _tagFilter;

    partial void OnTagFilterChanged(string? value) => Rebuild();

    [ObservableProperty]
    private PersonChoice? _assigneeFilter;

    partial void OnAssigneeFilterChanged(PersonChoice? value) => Rebuild();

    [RelayCommand]
    private void ClearTagFilter() => TagFilter = null;

    [RelayCommand]
    private void ClearAssigneeFilter() => AssigneeFilter = null;

    public void Rebuild()
    {
        var keep = Selected.Select(Key).ToHashSet(StringComparer.Ordinal);
        Rows.Clear();
        Emit(parent: null, depth: 0);

        Selected.Clear();
        foreach (var row in Rows.Where(r => keep.Contains(Key(r)))) Selected.Add(row);

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(TagsInUse));
        OnPropertyChanged(nameof(People));
        OnPropertyChanged(nameof(Assets));
        OnPropertyChanged(nameof(Columns));
    }

    private static string Key(BoardRow row) => row.Document?.Id ?? row.Folder?.Id ?? "";

    private void Emit(ProjectFolder? parent, int depth)
    {
        foreach (var folder in ProjectFolders.ChildrenInOrder(Manifest, parent))
        {
            var before = Rows.Count;
            var row = Build(folder, depth);
            Rows.Add(row);

            Emit(folder, depth + 1);
            foreach (var document in ProjectFolders.InOrder(Manifest, folder))
            {
                if (Build(document, folder, depth + 1) is { } kept) Rows.Add(kept);
            }

            // A folder survives a filter only if it or something under it did.
            // Removing it after the fact rather than looking ahead keeps the walk
            // one pass, and the tree small enough that a second pass would be
            // the cheaper-looking mistake.
            if (Rows.Count == before + 1 && !Matches(folder, document: null)) Rows.RemoveAt(before);
        }

        if (parent is not null) return;
        foreach (var loose in Manifest.Documents.Where(d => d.FolderId is null))
        {
            if (Build(loose, null, 0) is { } kept) Rows.Add(kept);
        }
    }

    private BoardRow Build(ProjectFolder folder, int depth) =>
        new(folder, depth)
        {
            Tags = folder.Tags ?? [],
            Length = Duration(ProjectIo.FolderDuration(Manifest, folder)),
            Carries = string.Join(" · ", Facets(folder)),
        };

    private BoardRow? Build(DocumentRef document, ProjectFolder? folder, int depth)
    {
        if (!Matches(folder, document)) return null;
        return new BoardRow(document, folder, depth)
        {
            Tags = ProjectBoard.TagsReaching(Manifest, document),
            Assignee = ProjectBoard.PersonById(Manifest, document.AssigneeId),
            Length = Duration((document.Frames, document.Seconds)),
        };
    }

    private bool Matches(ProjectFolder? folder, DocumentRef? document)
    {
        if (TagFilter is { Length: > 0 } tag)
        {
            var carried = document is not null
                ? ProjectBoard.TagsReaching(Manifest, document)
                : (IReadOnlyList<string>)(folder?.Tags ?? []);
            if (!carried.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (AssigneeFilter is { } choice)
        {
            // A folder is not assigned to anybody, so an assignee filter is a
            // question about documents and folders survive only by holding one.
            if (document is null) return false;
            if (document.AssigneeId != choice.Person.Id) return false;
        }
        return true;
    }

    private static IEnumerable<string> Facets(ProjectFolder folder)
    {
        if (folder.Taxonomy is { } reading)
        {
            yield return reading.Reviewed ? $"reading ({reading.Kind}, yours)" : $"reading ({reading.Kind})";
        }
        if (folder.Pivot is not null) yield return "pivot";
        if (folder.Variants is { Count: > 0 } v) yield return Count(v.Count, "variant");
        if (folder.Order is { Count: > 0 } o) yield return $"{o.Count} arranged";
        if (folder.Resources is { Count: > 0 } r) yield return Count(r.Count, "resource");
    }

    private static string Duration((int Frames, double? Seconds) of) =>
        of.Frames == 0 ? ""
        : of.Seconds is { } s ? $"{(int)(s / 60)}:{s % 60:00.0} · {of.Frames}f"
        : $"{of.Frames}f";

    // ---- bulk edits (Q44: no undo, and nothing destructive) ---------------------------

    [ObservableProperty]
    private string _status = "";

    /// <summary>What the bulk bar is about to act on.</summary>
    public string SelectionLabel
    {
        get
        {
            var documents = SelectedDocuments.Count;
            var folders = Selected.Count - documents;
            if (folders == 0) return Count(documents, "document").ToUpperInvariant();
            if (documents == 0) return Count(folders, "folder").ToUpperInvariant();
            return $"{Count(folders, "folder")}, {Count(documents, "document")}".ToUpperInvariant();
        }
    }

    /// <summary>The six statuses and "no status", for the bulk picker.</summary>
    public IReadOnlyList<StatusChoice> StatusChoices =>
    [
        .. AssetStatuses.InOrder.Select(s => new StatusChoice(s, AssetStatuses.Label(s))),
        new StatusChoice(null, "— no status —"),
    ];

    /// <summary>
    /// Picking one applies it, rather than needing a second Apply click.
    /// </summary>
    /// <remarks>
    /// Q44 chose no undo on the grounds that nothing here is destructive and
    /// setting a value back is the same gesture — which is only true if setting
    /// one is a single gesture. An Apply button would make the cheap path two
    /// clicks and the correction two more.
    /// </remarks>
    [ObservableProperty]
    private StatusChoice? _statusToApply;

    partial void OnStatusToApplyChanged(StatusChoice? value)
    {
        if (value is null) return;
        SetStatus(value.Status);
        StatusToApply = null;
    }

    [ObservableProperty]
    private PersonChoice? _personToApply;

    partial void OnPersonToApplyChanged(PersonChoice? value)
    {
        if (value is null) return;
        AssignSelection(value);
        PersonToApply = null;
    }

    /// <summary>What the Tag and Untag buttons act with.</summary>
    [ObservableProperty]
    private string _tagToApply = "";

    /// <summary>Set the status of every selected document.</summary>
    [RelayCommand]
    public void SetStatus(AssetStatus? status)
    {
        var documents = SelectedDocuments;
        if (documents.Count == 0) return;
        foreach (var document in documents) document.Status = status;
        Done(documents.Count, status is { } s ? $"marked {AssetStatuses.Label(s)}" : "status cleared");
    }

    /// <summary>Put a tag on everything selected — folders and documents alike.</summary>
    [RelayCommand]
    public void TagSelection(string? tag)
    {
        if (tag is not { Length: > 0 }) return;
        var touched = 0;
        foreach (var row in Selected.ToList())
        {
            var added = row.Document is { } d
                ? ProjectBoard.Tag(d, tag)
                : row.Folder is { } f && ProjectBoard.Tag(f, tag);
            if (added) touched++;
        }
        Done(touched, $"tagged “{tag.Trim()}”");
    }

    /// <summary>Take a tag off everything selected.</summary>
    [RelayCommand]
    public void UntagSelection(string? tag)
    {
        if (tag is not { Length: > 0 }) return;
        var touched = 0;
        foreach (var row in Selected.ToList())
        {
            var removed = row.Document is { } d
                ? ProjectBoard.Untag(d, tag)
                : row.Folder is { } f && ProjectBoard.Untag(f, tag);
            if (removed) touched++;
        }
        Done(touched, $"untagged “{tag.Trim()}”");
    }

    /// <summary>Point every selected document at somebody, or at nobody.</summary>
    [RelayCommand]
    public void AssignSelection(PersonChoice? choice)
    {
        var documents = SelectedDocuments;
        if (documents.Count == 0) return;
        var person = choice?.Person is { Id.Length: > 0 } p ? p : null;
        foreach (var document in documents) ProjectBoard.Assign(document, person);
        Done(documents.Count, person is null ? "unassigned" : $"assigned to {person.Name}");
    }

    private void Done(int count, string what)
    {
        // Said out loud, because Q44 chose no undo: a bulk edit that changes
        // nine rows silently is one nobody can check, and checking is the whole
        // substitute for taking it back.
        Status = count == 0 ? "Nothing changed." : $"{Count(count, "row")} {what}.";
        Rebuild();
        _changed();
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    // ---- people --------------------------------------------------------------------

    public IReadOnlyList<PersonChoice> People =>
        [.. ProjectBoard.People(Manifest).Select(p => new PersonChoice(p, WhatRemovingCosts(p)))];

    /// <summary>The people, plus "nobody" — what a bulk assign offers.</summary>
    /// <remarks>
    /// Unassigning has to be in the same list as assigning, or taking somebody
    /// off nine rows is nine right-clicks.
    /// </remarks>
    public IReadOnlyList<PersonChoice> AssignChoices =>
        [new PersonChoice(NobodyInParticular, ""), .. People];

    /// <summary>
    /// A sentinel meaning "nobody", so the assign list can contain the absence.
    /// </summary>
    /// <remarks>
    /// Never added to <c>manifest.People</c> and never written: it exists so a
    /// <c>ComboBox</c> can offer clearing, which it cannot do with a null item.
    /// </remarks>
    internal static readonly Person NobodyInParticular = new() { Id = "", Name = "— nobody —" };

    [ObservableProperty]
    private string _newPersonName = "";

    [RelayCommand]
    public void AddPerson()
    {
        var name = NewPersonName.Trim();
        if (name.Length == 0) return;
        var person = ProjectBoard.AddPerson(Manifest, name);
        NewPersonName = "";
        Status = $"{person.Name} is on the project.";
        Rebuild();
        _changed();
    }

    /// <summary>What removing somebody would cost, for the confirmation to say.</summary>
    /// <remarks>
    /// Q35's pattern: the gesture names what goes rather than asking a bare "are
    /// you sure". Separated from the removing so the <em>decision</em> is
    /// testable and only the dialog is manual.
    /// </remarks>
    public string WhatRemovingCosts(Person person)
    {
        var count = ProjectBoard.AssignedCount(Manifest, person);
        return count == 0
            ? $"{person.Name} is on nothing."
            : $"{person.Name} is on {Count(count, "document")}, which will be unassigned.";
    }

    [RelayCommand]
    public void RemovePerson(PersonChoice? choice)
    {
        if (choice?.Person is not { } person) return;
        var unassigned = ProjectBoard.RemovePerson(Manifest, person);
        if (AssigneeFilter?.Person.Id == person.Id) AssigneeFilter = null;
        Status = unassigned == 0
            ? $"{person.Name} is off the project."
            : $"{person.Name} is off the project. {Count(unassigned, "document")} unassigned.";
        Rebuild();
        _changed();
    }

    // ---- the footer -----------------------------------------------------------------

    public IReadOnlyList<string> TagsInUse => ProjectBoard.TagsInUse(Manifest);

    /// <summary>
    /// What the project holds and what is wrong with it.
    /// </summary>
    /// <remarks>
    /// One place counting. The footer, the status columns and
    /// <c>ExportPlan.Describe</c> all say how many documents there are, and
    /// three places counting separately is three places to drift.
    /// </remarks>
    public string Summary
    {
        get
        {
            var s = ProjectBoard.Summarise(Manifest);
            var parts = new List<string> { Count(s.Documents, "document") };
            foreach (var status in AssetStatuses.InOrder)
            {
                if (s.ByStatus.TryGetValue(status, out var n)) parts.Add($"{n} {AssetStatuses.Label(status)}");
            }
            if (s.Unset > 0) parts.Add($"{s.Unset} with no status");
            if (s.Unassigned > 0) parts.Add($"{s.Unassigned} unassigned");
            return string.Join(" · ", parts);
        }
    }

    // ---- the status board ------------------------------------------------------------

    /// <summary>
    /// The six statuses and the unset column, each holding its documents.
    /// </summary>
    /// <remarks>
    /// Built from the rows rather than from the manifest, so a tag or assignee
    /// filter narrows the board too — otherwise filtering the tree and then
    /// looking at the board would show two different projects.
    /// </remarks>
    public IReadOnlyList<StatusColumn> Columns
    {
        get
        {
            var documents = Rows.Where(r => r.Document is not null).ToList();
            var columns = new List<StatusColumn>();
            foreach (var status in AssetStatuses.InOrder)
            {
                columns.Add(new StatusColumn(
                    status,
                    AssetStatuses.Label(status),
                    AssetStatuses.Color(status),
                    [.. documents.Where(r => r.Status == status)]));
            }
            // Last, and its own column: "nobody has said" is not Design, and
            // folding it in would invent a status for every imported file.
            columns.Add(new StatusColumn(
                null, "No status", "#00000000", [.. documents.Where(r => r.Status is null)]));
            return columns;
        }
    }

    /// <summary>Move one row to a status — what a drag between columns does.</summary>
    [RelayCommand]
    public void MoveToStatus((BoardRow Row, AssetStatus? Status) move)
    {
        if (move.Row.Document is not { } document) return;
        if (document.Status == move.Status) return;
        document.Status = move.Status;
        Status = move.Status is { } s
            ? $"“{document.Name}” is {AssetStatuses.Label(s)}."
            : $"“{document.Name}” has no status.";
        Rebuild();
        _changed();
    }

    // ---- the assets tab ----------------------------------------------------------------

    /// <summary>
    /// The kinds a scope can declare, in the order the Assets tab lists them.
    /// </summary>
    /// <remarks>
    /// Named here rather than discovered, because a column that appears only
    /// once something is declared is a column an artist cannot use to declare
    /// the first one. The strings are the kinds `ResourceScopes` resolves.
    /// </remarks>
    public static readonly IReadOnlyList<string> AssetKinds =
    [
        PaletteScopes.Kind, GradientScopes.Kind, ReferenceScopes.Kind, GuideScopes.Kind,
        TemplateScopes.Kind, ExportScopes.Kind, SymbolScopes.Kind, TipScopes.Kind,
    ];

    /// <summary>
    /// Every scope in the project, with what it declares — the three levels at once.
    /// </summary>
    /// <remarks>
    /// <b>This is the tab the row menu cannot be.</b> A context menu declares on
    /// one scope at a time and shows nothing about the others, so *why is this
    /// drawing painting from the studio palette* is answerable only by
    /// reasoning. Here the project, the folders and a document are rows of one
    /// table and the answer is visible.
    /// </remarks>
    public IReadOnlyList<AssetScope> Assets
    {
        get
        {
            var scopes = new List<AssetScope>
            {
                new(_project.Name, 0, Cells(Manifest.Resources)),
            };
            foreach (var row in Rows)
            {
                scopes.Add(row.Document is { } document
                    ? new AssetScope(document.Name, row.Depth + 1, Cells(document.Resources))
                    : new AssetScope(row.Folder!.Name, row.Depth + 1, Cells(row.Folder!.Resources)));
            }
            return scopes;
        }
    }

    private static IReadOnlyList<AssetCell> Cells(List<ScopedResource>? declared) =>
        [.. AssetKinds.Select(k => new AssetCell(
            k, [.. (declared ?? []).Where(r => r.Kind == k)]))];
}
