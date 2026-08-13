using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
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

/// <summary>One declaration, with the scope it sits on so a chip can take it back.</summary>
/// <remarks>
/// The scope travels with the entry because "undeclare this" is one gesture and
/// the chip is what an artist clicks. Exactly one of the three is set.
/// </remarks>
public sealed record Declaration(
    ScopedResource Resource, string Name, ProjectFolder? Folder, DocumentRef? Document, bool OnProject)
{
    /// <summary>The kind's own face, so a chip is recognisable before it is read.</summary>
    public string Glyph => AssetKinds.GlyphOf(Resource.Kind);

    public string Designation => AssetKinds.LabelOf(Resource.Kind);

    /// <summary>Whether this one reaches the whole project rather than its subtree.</summary>
    public bool IsPublished => Resource.ReachOrDefault == ResourceReach.Project;

    /// <summary>
    /// Whether reach is a question here at all. A document declaration has
    /// nothing below it, so publishing from one is the folder's job.
    /// </summary>
    public bool CanReach => Document is null;

    public string ReachGlyph => IsPublished ? "⤓" : "⤒";

    public string ReachHint => IsPublished
        ? "Reaches the whole project — take it back to this subtree"
        : "Reaches this subtree — publish it project-wide";
}

/// <summary>One cell of the Assets table: what a scope declares of one kind.</summary>
public sealed record AssetCell(string Kind, IReadOnlyList<Declaration> Declared)
{
    public int Count => Declared.Count;

    public bool Any => Declared.Count > 0;

    /// <summary>The names, so the cell reads rather than counting.</summary>
    public string Text => string.Join(", ", Declared.Select(d => d.Name));
}

/// <summary>One row of the Assets table: a scope, and what it declares.</summary>
/// <remarks>
/// A class rather than a record since the hierarchy became draggable here too:
/// the drop indicator lives on the row, and a record cannot raise a change.
/// </remarks>
public sealed partial class AssetScope(
    string name, int depth, IReadOnlyList<AssetCell> cells,
    ProjectFolder? folder, DocumentRef? document, bool isProject) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Zero for the project, then folder depth, then documents.</summary>
    public int Depth { get; } = depth;

    public IReadOnlyList<AssetCell> Cells { get; } = cells;

    public ProjectFolder? Folder { get; } = folder;

    public DocumentRef? Document { get; } = document;

    public bool IsProject { get; } = isProject;

    public double Indent => Depth * 16;

    public bool DeclaresNothing => Cells.All(c => !c.Any);

    /// <summary>Every declaration on this scope, flattened, for the row to list.</summary>
    public IReadOnlyList<Declaration> All => [.. Cells.SelectMany(c => c.Declared)];

    /// <summary>Where a drag would land, for the row to show — see BoardRow's pair.</summary>
    [ObservableProperty]
    private bool _dropAbove;

    [ObservableProperty]
    private bool _dropInto;
}

/// <summary>One entry of the "give this scope something" menu.</summary>
public sealed record OfferChoice(AssetScope Scope, string Kind, string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One asset in the project's library: a reference sheet, a palette, a
/// gradient, a brush tip or a symbol, wearing its kind.
/// </summary>
/// <remarks>
/// The library lists what the project <em>has</em>, where the Assets table
/// lists what each scope <em>declares</em> — the library is what an artist
/// drags onto the table to connect the two. Designation and glyph come from
/// <see cref="AssetKinds"/> and are automatic, so an asset is recognisable as
/// what it is wherever it lands.
/// </remarks>
public sealed record AssetEntry(string Kind, string Id, string Name)
{
    public string Glyph => AssetKinds.GlyphOf(Kind);

    public string Designation => AssetKinds.LabelOf(Kind);
}

/// <summary>One place a sheet can be filed: a folder, or the project when null.</summary>
public sealed record SheetHomeChoice(ProjectFolder? Folder, string Label)
{
    public override string ToString() => Label;
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
        _name = folder.Name;
    }

    public BoardRow(DocumentRef document, ProjectFolder? folder, int depth)
    {
        Document = document;
        Folder = folder;
        Depth = depth;
        _name = document.Name;
    }

    /// <summary>A character sheet, filed in a folder or project-wide when none.</summary>
    /// <remarks>
    /// <b>Q25 re-answered.</b> The row exists so the window can do the one
    /// thing the docker cannot: re-assign the sheet to another folder. It
    /// never takes a status, an assignee or a tag — a sheet is reference art,
    /// not a deliverable — and every bulk command guards on that.
    /// </remarks>
    public BoardRow(SheetRef sheet, ProjectFolder? folder, int depth)
    {
        Sheet = sheet;
        Folder = folder;
        Depth = depth;
        _name = sheet.Name;
    }

    /// <summary>The folder this row is, or the one a document is filed in.</summary>
    public ProjectFolder? Folder { get; }

    /// <summary>The document this row is, or null on a folder row.</summary>
    public DocumentRef? Document { get; }

    /// <summary>The character sheet this row is, or null on every other row.</summary>
    public SheetRef? Sheet { get; }

    public int Depth { get; }

    public bool IsFolder => Document is null && Sheet is null;

    public bool IsSheet => Sheet is not null;

    public double Indent => Depth * 16;

    public string Glyph =>
        Document is { IsTemplate: true } ? AssetKinds.GlyphOf(TemplateScopes.Kind)
        : Document is not null ? "▣"
        : Sheet is not null ? AssetKinds.GlyphOf(ReferenceScopes.Kind)
        : Folder is { Icon: { Length: > 0 } chosen } ? chosen
        : "🗀";

    /// <summary>What kind of asset this row is, in a word — empty on the rest.</summary>
    /// <remarks>
    /// From <see cref="AssetKinds"/>, never authored, so the docker and this
    /// window call a sheet — and a template — the same thing.
    /// </remarks>
    public string Designation =>
        Sheet is not null ? AssetKinds.LabelOf(ReferenceScopes.Kind)
        : Document is { IsTemplate: true } ? AssetKinds.LabelOf(TemplateScopes.Kind)
        : "";

    public bool HasDesignation => Designation.Length > 0;

    /// <summary>
    /// Observable and two-way bound while renaming; the rebuild every edit
    /// causes re-reads it from the model, so it cannot drift.
    /// </summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>The name is being edited in place — double-click starts it.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    // ---- where a drag would land, for the row to show -------------------------
    //
    // Set by the window's drag-over handling and rendered as a line above the
    // row (about to be placed before it) or a tint (about to be filed inside
    // it). On the row because that is the one DataContext the template has.

    [ObservableProperty]
    private bool _dropAbove;

    [ObservableProperty]
    private bool _dropInto;

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
        Sheet is not null ? [] : (Document?.Tags ?? Folder?.Tags ?? []).ToList();

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

    /// <summary>
    /// Rename the project — name and folder, the docker's root-row rename with
    /// this window's prompt-first idiom.
    /// </summary>
    [RelayCommand]
    public async Task RenameProjectAsync()
    {
        if (AskName is null) return;
        var name = await AskName("project", _project.Name);
        if (name is null || string.IsNullOrWhiteSpace(name)) return;
        switch (ProjectIo.RenameProject(_project, name))
        {
            case ProjectIo.RenameOutcome.NameTaken:
                Status = $"There is already something called “{name.Trim()}” beside the project.";
                return;
            case ProjectIo.RenameOutcome.DiskRefused:
                Status = "Could not rename the project folder on disk — something has it open.";
                return;
        }
        OnPropertyChanged(nameof(Title));
        Status = $"The project is now “{_project.Name}”.";
        _changed();   // the docker re-points its watcher from this
        RequestSave?.Invoke();
        Rebuild();
    }

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
        OnPropertyChanged(nameof(HasSheetSelection));
        OnPropertyChanged(nameof(SelectionLabel));
        RefreshFacetEditor();
    }

    /// <summary>Everything the facet editor shows, after the selection moved.</summary>
    private void RefreshFacetEditor()
    {
        OnPropertyChanged(nameof(EditingFolder));
        OnPropertyChanged(nameof(IsEditingFolder));
        OnPropertyChanged(nameof(EditingFolderName));
        OnPropertyChanged(nameof(FolderNotes));
        OnPropertyChanged(nameof(FolderHasPivot));
        OnPropertyChanged(nameof(FolderHasReading));
        OnPropertyChanged(nameof(FolderReadingLabel));
        OnPropertyChanged(nameof(WhatClearingCosts));
        OnPropertyChanged(nameof(FolderVariants));
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
        OnPropertyChanged(nameof(HasSheetSelection));
        OnPropertyChanged(nameof(SelectionLabel));
        RefreshFacetEditor();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(TagsInUse));
        OnPropertyChanged(nameof(People));
        OnPropertyChanged(nameof(Assets));
        OnPropertyChanged(nameof(OfferChoices));
        OnPropertyChanged(nameof(Library));
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(ExportRows));
        OnPropertyChanged(nameof(ExportSummary));
    }

    private static string Key(BoardRow row) => row.Document?.Id ?? row.Sheet?.Id ?? row.Folder?.Id ?? "";

    private void Emit(ProjectFolder? parent, int depth)
    {
        foreach (var folder in ProjectFolders.ChildrenInOrder(Manifest, parent))
        {
            var before = Rows.Count;
            var row = Build(folder, depth);
            Rows.Add(row);

            Emit(folder, depth + 1);
            // Sheets above the drawings that consult them — the docker's order.
            // A tag or assignee filter hides them: neither is a thing a sheet has.
            if (TagFilter is null && AssigneeFilter is null)
            {
                foreach (var sheet in ProjectSheets.In(Manifest, folder))
                {
                    Rows.Add(new BoardRow(sheet, folder, depth + 1));
                }
            }
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
        if (TagFilter is null && AssigneeFilter is null)
        {
            foreach (var sheet in ProjectSheets.In(Manifest, null))
            {
                Rows.Add(new BoardRow(sheet, folder: null, 0));
            }
        }
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
            var sheets = Selected.Count(r => r.Sheet is not null);
            var folders = Selected.Count - documents - sheets;
            var parts = new List<string>();
            if (folders > 0) parts.Add(Count(folders, "folder"));
            if (documents > 0) parts.Add(Count(documents, "document"));
            if (sheets > 0) parts.Add(Count(sheets, "sheet"));
            return string.Join(", ", parts).ToUpperInvariant();
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
        var kept = 0;
        foreach (var document in documents)
        {
            var was = document.Status;
            document.Status = status;
            if (CaptureMilestone(document, was)) kept++;
        }
        Done(documents.Count, status is { } s ? $"marked {AssetStatuses.Label(s)}" : "status cleared");
        if (kept > 0) Status += $" {Count(kept, "milestone version")} kept.";
    }

    /// <summary>
    /// A promotion to Review or Ready keeps a version of the file as it
    /// stands, tagged with the milestone — "which bytes were the Ready ones"
    /// is the question a studio asks after somebody keeps drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is on disk is what is versioned. A document open and unsaved in a
    /// tab versions its last save — status is set from this window, normally
    /// between sessions, and reaching into the editor from here would couple
    /// the two the way nothing else in this window does.
    /// </para>
    /// <para>
    /// Failure leaves the status set and says so in the status line rather
    /// than throwing: the artist asked for a status change and got one; the
    /// missing version is a degradation, not a reason to refuse the edit.
    /// </para>
    /// </remarks>
    private bool CaptureMilestone(DocumentRef document, AssetStatus? was)
    {
        var now = document.Status;
        if (now is not (AssetStatus.Review or AssetStatus.Ready) || was == now) return false;
        if (!File.Exists(Path.Combine(_project.Root, document.Path))) return false;
        try
        {
            ProjectVersions.SaveVersion(
                _project, document.Id, document.Path,
                $"Marked {AssetStatuses.Label(now.Value)}", milestone: now);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Status set, but no version could be kept: {ex.Message}";
            return false;
        }
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
                : row is { IsFolder: true, Folder: { } f } && ProjectBoard.Tag(f, tag);
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
                : row is { IsFolder: true, Folder: { } f } && ProjectBoard.Untag(f, tag);
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

    // ---- re-assigning sheets (Q25 re-answered) -----------------------------------

    /// <summary>The sheets in the selection — what the re-file gesture acts on.</summary>
    private List<SheetRef> SelectedSheets =>
        [.. Selected.Select(r => r.Sheet).OfType<SheetRef>()];

    public bool HasSheetSelection => Selected.Any(r => r.Sheet is not null);

    /// <summary>
    /// Where a sheet can be filed: the project itself, then every folder.
    /// </summary>
    /// <remarks>
    /// Labelled by path rather than by name, because "combat" says nothing when
    /// the knight and the goblin both have one.
    /// </remarks>
    public IReadOnlyList<SheetHomeChoice> SheetHomeChoices =>
    [
        new SheetHomeChoice(null, $"{_project.Name} (everything sees it)"),
        .. ProjectFolders.All(Manifest)
            .OrderBy(f => ProjectFolders.PathOf(Manifest, f), StringComparer.OrdinalIgnoreCase)
            .Select(f => new SheetHomeChoice(f, ProjectFolders.PathOf(Manifest, f))),
    ];

    /// <summary>Picking one refiles, rather than needing a second Apply click.</summary>
    [ObservableProperty]
    private SheetHomeChoice? _sheetHomeToApply;

    partial void OnSheetHomeToApplyChanged(SheetHomeChoice? value)
    {
        if (value is null) return;
        FileSelectedSheets(value);
        SheetHomeToApply = null;
    }

    /// <summary>File every selected sheet in the chosen folder — disk first (B106).</summary>
    [RelayCommand]
    public void FileSelectedSheets(SheetHomeChoice? choice)
    {
        if (choice is null) return;
        var moved = 0;
        foreach (var sheet in SelectedSheets)
        {
            if (ProjectSheets.Refile(_project, sheet, choice.Folder)) moved++;
        }
        Done(moved, choice.Folder is null
            ? "filed on the project — every document sees them"
            : $"filed in {choice.Folder.Name}");
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

    // ---- creating structure from the window ---------------------------------------

    /// <summary>
    /// Makes the blank document a new entry starts as. Supplied by the owner
    /// so a document made here matches one made anywhere else — size, fps,
    /// paper — rather than a second definition of "blank".
    /// </summary>
    public Func<Doc>? NewDocument { get; set; }

    /// <summary>
    /// Called with each document made here, before the save. The docker owns
    /// the dirty set the project save reads, so a document it was never told
    /// about is a document no save writes.
    /// </summary>
    public Action<DocumentRef>? DocumentCreated { get; set; }

    /// <summary>
    /// Saves the project. Supplied by the owner so what is made here lands on
    /// disk at once — the docker's pending badge is for work mid-drawing, and
    /// this window is used between drawings, where "created" should mean
    /// "exists".
    /// </summary>
    public Action? RequestSave { get; set; }

    /// <summary>
    /// Ask the artist what to call it: the kind in words, and a suggestion.
    /// Null means they cancelled. Supplied by the window, for B65's reason —
    /// a view model that opens its own dialogs is one no test can drive, and
    /// the cancel path is the half that goes untested otherwise.
    /// </summary>
    public Func<string, string, Task<string?>>? AskName { get; set; }

    /// <summary>
    /// The folder a new thing goes into: the selected folder, or the folder a
    /// selected document sits in — B85's rule, unchanged from the docker.
    /// </summary>
    public ProjectFolder? TargetFolder => Selected.FirstOrDefault()?.Folder;

    /// <summary>Ask for a name, then create — or create nothing if cancelled.</summary>
    [RelayCommand]
    public async Task CreateFolderAsync()
    {
        var name = AskName is null ? "Folder" : await AskName("folder", "Folder");
        if (name is null) return;   // cancelled: nothing is written
        AddFolder(name);
    }

    [RelayCommand]
    public async Task CreateDocumentAsync()
    {
        // The same stem the docker offers (B107): the folder is what the
        // drawing is of, and typing "walk" after "Knight - " is the gesture.
        var suggested = TargetFolder is { } folder
            ? $"{folder.Name}{ProjectViewModel.NameSeparator}"
            : $"Document {Manifest.Documents.Count + 1}";
        var name = AskName is null ? suggested : await AskName("document", suggested);
        if (name is null) return;   // cancelled: nothing is written
        AddDocument(name);
    }

    /// <summary>Make a folder where the selection is, and write it to disk.</summary>
    public ProjectFolder AddFolder(string? name)
    {
        var parent = TargetFolder;
        var folder = ProjectFolders.Add(Manifest, ProjectViewModel.Named(name, "Folder"), parent);
        _changed();
        RequestSave?.Invoke();
        Rebuild();
        SetSelection(Rows.Where(r => r.IsFolder && ReferenceEquals(r.Folder, folder)));
        Status = parent is null
            ? $"“{folder.Name}” added to the project{OnDisk}."
            : $"“{folder.Name}” added inside “{parent.Name}”{OnDisk}.";
        return folder;
    }

    /// <summary>Make a document where the selection is, and write it to disk.</summary>
    /// <remarks>
    /// The save is what gives it its status: a new document becomes Draft on
    /// its first write (<c>ProjectIo.Save</c>), so a row made here arrives in
    /// the pipeline rather than outside it.
    /// </remarks>
    public DocumentRef AddDocument(string? name)
    {
        var folder = TargetFolder;
        var doc = NewDocument?.Invoke() ?? DocumentFactory.CreateDoc();
        var reference = ProjectIo.AddDocument(
            _project,
            ProjectViewModel.Named(name, $"Document {Manifest.Documents.Count + 1}"),
            doc, folder);
        DocumentCreated?.Invoke(reference);
        _changed();
        RequestSave?.Invoke();
        Rebuild();
        SetSelection(Rows.Where(r => ReferenceEquals(r.Document, reference)));
        Status = folder is null
            ? $"“{reference.Name}” added to the project{OnDisk}."
            : $"“{reference.Name}” added in “{folder.Name}”{OnDisk}.";
        return reference;
    }

    /// <summary>How the creation message ends, honestly.</summary>
    /// <remarks>
    /// Without a save hook nothing was written, and saying "written to disk"
    /// anyway would be the docker's pending badge contradicted by a sentence.
    /// </remarks>
    private string OnDisk => RequestSave is null ? " — save the project to write it" : " and written to disk";

    // ---- rearranging the hierarchy (both tabs drag through here) -------------------

    /// <summary>
    /// Move something to a folder — or to the project root when null —
    /// optionally placing it before a sibling in the running order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One method behind both trees' drags, the way the docker's
    /// <c>MoveInto</c> is behind its own: a folder reparents
    /// (<see cref="ProjectFolders.Move"/>, which refuses cycles), a document
    /// refiles disk-first (<see cref="ProjectIo.RefileDocument"/>), a sheet
    /// refiles through its own registry.
    /// </para>
    /// <para>
    /// <paramref name="beforeId"/> is the row the drop's line pointed at.
    /// Ordering is a folder's <c>Order</c> list, so it only applies inside a
    /// folder — the root has no record to arrange — and only between things
    /// of the same kind, because folders and documents are arranged by
    /// different readers of that one list.
    /// </para>
    /// </remarks>
    public bool MoveTo(BoardRow dragged, ProjectFolder? destination, string? beforeId = null)
    {
        if (dragged is { IsFolder: true, Folder: { } folder })
        {
            // Dropping something onto where it already is, with no place
            // asked for, is an ordinary slip in a tree — not a move and not
            // an error.
            if (folder.ParentId == destination?.Id && beforeId is null) return false;
            if (folder.ParentId != destination?.Id
                && !ProjectIo.MoveFolder(_project, folder, destination))
            {
                Status = $"“{folder.Name}” cannot go there.";
                return false;
            }
            if (beforeId is not null && destination is not null)
            {
                var children = ProjectFolders.ChildrenInOrder(Manifest, destination);
                Reorder(
                    IndexOf(children.Select(f => f.Id), folder.Id),
                    IndexOf(children.Select(f => f.Id), beforeId),
                    (from, to) => ProjectFolders.MoveFolder(Manifest, destination, from, to));
            }
            Moved(folder.Name, destination);
            return true;
        }
        if (dragged.Sheet is { } sheet)
        {
            if (sheet.FolderId == destination?.Id) return false;
            if (!ProjectSheets.Refile(_project, sheet, destination))
            {
                Status = $"Could not move “{sheet.Name}”. It is still where it was.";
                return false;
            }
            Moved(sheet.Name, destination);
            return true;
        }
        if (dragged.Document is { } document)
        {
            if (document.FolderId == destination?.Id && beforeId is null) return false;
            if (document.FolderId != destination?.Id
                && !ProjectIo.RefileDocument(_project, document, destination))
            {
                Status = $"Could not move “{document.Name}” on disk. It is still where it was.";
                return false;
            }
            if (beforeId is not null && destination is not null)
            {
                var siblings = ProjectFolders.InOrder(Manifest, destination);
                Reorder(
                    IndexOf(siblings.Select(d => d.Id), document.Id),
                    IndexOf(siblings.Select(d => d.Id), beforeId),
                    (from, to) => ProjectFolders.MoveDocument(Manifest, destination, from, to));
            }
            Moved(document.Name, destination);
            return true;
        }
        return false;
    }

    /// <summary>The Assets tab's rows go through the same move.</summary>
    public bool MoveTo(AssetScope dragged, ProjectFolder? destination, string? beforeId = null)
    {
        if (dragged.IsProject) return false;
        var row = dragged.Folder is { } folder
            ? new BoardRow(folder, 0)
            : dragged.Document is { } document
                ? new BoardRow(document, null, 0)
                : null;
        return row is not null && MoveTo(row, destination, beforeId);
    }

    /// <summary>
    /// Place <paramref name="dragged"/> before <paramref name="target"/> in
    /// the container that holds the target — what dropping on the line means.
    /// </summary>
    public bool MoveBefore(BoardRow dragged, BoardRow target)
    {
        if (ReferenceEquals(dragged, target)) return false;
        if (target is { IsFolder: true, Folder: { } folder })
        {
            return MoveTo(dragged, ProjectFolders.ById(Manifest, folder.ParentId), folder.Id);
        }
        if (target.Document is { } document)
        {
            return MoveTo(dragged, ProjectFolders.ById(Manifest, document.FolderId), document.Id);
        }
        if (target.Sheet is { } sheet)
        {
            // Sheets sit in a fixed band of their folder, so "before the
            // sheet" can only mean "into the folder that holds it".
            return MoveTo(dragged, ProjectFolders.ById(Manifest, sheet.FolderId));
        }
        return false;
    }

    /// <summary>The Assets tab's version of the same placement.</summary>
    public bool MoveBefore(AssetScope dragged, AssetScope target)
    {
        if (dragged.IsProject) return false;
        if (target.IsProject) return MoveTo(dragged, null);
        if (target.Folder is { } folder && target.Document is null)
        {
            return MoveTo(dragged, ProjectFolders.ById(Manifest, folder.ParentId), folder.Id);
        }
        if (target.Document is { } document)
        {
            return MoveTo(dragged, ProjectFolders.ById(Manifest, document.FolderId), document.Id);
        }
        return false;
    }

    private static void Reorder(int from, int before, Func<int, int, bool> move)
    {
        if (from < 0 || before < 0 || from == before) return;
        // Taking the item out first shifts everything after it up one.
        move(from, from < before ? before - 1 : before);
    }

    private static int IndexOf(IEnumerable<string> ids, string wanted)
    {
        var at = 0;
        foreach (var id in ids)
        {
            if (id == wanted) return at;
            at++;
        }
        return -1;
    }

    private void Moved(string name, ProjectFolder? destination)
    {
        Status = destination is null
            ? $"Moved “{name}” to the project."
            : $"Moved “{name}” to “{destination.Name}”.";
        Rebuild();
        _changed();
        RequestSave?.Invoke();
    }

    // ---- removing and deleting (the docker's two operations, here too) -------------
    //
    // Remove takes something out of the index and leaves the disk alone —
    // cheap to undo by hand. Delete is the other decision, said out loud with
    // its own confirmation. Never one gesture told apart by a held key (B87).

    /// <summary>Take a row out of the project. The disk is not touched.</summary>
    public bool RemoveFromProject(BoardRow row)
    {
        if (row is { IsFolder: true, Folder: { } folder })
        {
            // B87: the artist removed a folder, not the work in it — its
            // documents come back to the project root.
            var orphaned = ProjectFolders.Remove(Manifest, folder);
            foreach (var document in orphaned)
            {
                ProjectFolders.FileDocument(Manifest, document, null);
            }
            Status = orphaned.Count == 0
                ? $"Removed “{folder.Name}”. Its folder is still on disk."
                : $"Removed “{folder.Name}”. {orphaned.Count} document{(orphaned.Count == 1 ? "" : "s")} moved to the project root.";
        }
        else if (row.Document is { } document)
        {
            ProjectIo.DetachDocument(_project, document);
            Status = $"Removed “{document.Name}” from the project. Its file is still on disk.";
        }
        else if (row.Sheet is { } sheet)
        {
            ProjectSheets.Remove(_project, sheet);
            Status = $"Removed “{sheet.Name}” from the project. Its file is still on disk.";
        }
        else
        {
            return false;
        }
        AfterRemoval();
        return true;
    }

    /// <summary>Whether deleting this row should ask first — B87's line.</summary>
    public bool DeleteNeedsConfirmation(BoardRow row)
    {
        if (row is not { IsFolder: true, Folder: { } folder }) return false;
        var (folders, documents) = ProjectFolders.Contents(Manifest, folder);
        return folders.Count > 1 || documents.Count > 0;
    }

    /// <summary>What the confirmation should say, so the artist knows the size of it.</summary>
    public string DeleteWarning(BoardRow row)
    {
        if (row is { IsFolder: true, Folder: { } folder })
        {
            var (folders, documents) = ProjectFolders.Contents(Manifest, folder);
            var inside = new List<string>();
            if (folders.Count > 1) inside.Add($"{folders.Count - 1} folder{(folders.Count == 2 ? "" : "s")}");
            if (documents.Count > 0) inside.Add($"{documents.Count} document{(documents.Count == 1 ? "" : "s")}");
            return inside.Count == 0
                ? $"Delete the empty folder “{folder.Name}” from disk?"
                : $"Delete “{folder.Name}” and the {string.Join(" and ", inside)} inside it, "
                  + "from the project and from disk?";
        }
        return $"Delete “{row.Name}” from the project and from disk?";
    }

    /// <summary>
    /// Remove a row from the project <b>and</b> delete it from disk. The
    /// caller confirms first when <see cref="DeleteNeedsConfirmation"/> says
    /// so — nothing here asks, because a view model that opens dialogs is a
    /// view model no test can drive.
    /// </summary>
    public bool DeleteFromDisk(BoardRow row)
    {
        if (row is { IsFolder: true, Folder: { } folder })
        {
            // The directory before the manifest: PathOf walks the parent
            // chain, and a folder already out of the manifest resolves to the
            // project root — which is every drawing. Order is load-bearing.
            var path = ProjectFolders.PathOf(Manifest, folder);
            var (_, documents) = ProjectFolders.Contents(Manifest, folder);
            var deleted = ProjectIo.DeleteInProject(_project, path);
            ProjectFolders.Remove(Manifest, folder);
            foreach (var inside in documents) ProjectIo.DetachDocument(_project, inside);
            Status = deleted
                ? $"Deleted “{folder.Name}” and everything in it."
                : $"Removed “{folder.Name}” from the project, but its folder could not be deleted.";
        }
        else if (row.Document is { } document)
        {
            var path = document.Path;
            ProjectIo.DetachDocument(_project, document);
            Status = ProjectIo.DeleteInProject(_project, path)
                ? $"Deleted “{document.Name}”."
                : $"Removed “{document.Name}” from the project, but its file could not be deleted.";
        }
        else if (row.Sheet is { } sheet)
        {
            var path = sheet.Path;
            ProjectSheets.Remove(_project, sheet);
            Status = ProjectIo.DeleteInProject(_project, path)
                ? $"Deleted “{sheet.Name}”."
                : $"Removed “{sheet.Name}” from the project, but its file could not be deleted.";
        }
        else
        {
            return false;
        }
        AfterRemoval();
        return true;
    }

    /// <summary>The Assets tab's rows are the same hierarchy; same operations.</summary>
    public BoardRow? AsRow(AssetScope scope) =>
        scope.Folder is { } folder ? new BoardRow(folder, 0)
        : scope.Document is { } document ? new BoardRow(document, null, 0)
        : null;

    /// <summary>What deleting a library asset means, said before it happens.</summary>
    public string DeleteAssetWarning(AssetEntry asset) => asset.Kind switch
    {
        ReferenceScopes.Kind => $"Delete the reference sheet “{asset.Name}” from the project and from disk?",
        TemplateScopes.Kind => $"Delete the template “{asset.Name}” from the project and from disk?",
        _ => $"Delete {Core.Projects.AssetKinds.LabelOf(asset.Kind).ToLowerInvariant()} “{asset.Name}” from the project? "
             + "Everywhere it is shared stops offering it.",
    };

    /// <summary>
    /// Delete an asset from the library — and with it, every declaration of
    /// it, or the scopes would offer a thing the project no longer has.
    /// </summary>
    /// <remarks>
    /// Sheets and templates are files and go through the row operations;
    /// palettes, gradients and tips live in the project's aggregate resource
    /// files, which the save rewrites without them. Symbols are the one
    /// refusal: instances in documents reference them by id, and the Symbols
    /// panel owns the delete that knows about instances.
    /// </remarks>
    public bool DeleteAsset(AssetEntry? asset)
    {
        if (asset is null) return false;
        switch (asset.Kind)
        {
            case ReferenceScopes.Kind:
                var sheet = (Manifest.Sheets ?? []).FirstOrDefault(s => s.Id == asset.Id);
                return sheet is not null && DeleteFromDisk(new BoardRow(sheet, null, 0));
            case TemplateScopes.Kind:
                var document = Manifest.Documents.FirstOrDefault(d => d.Id == asset.Id);
                if (document is null) return false;
                ResourceScopes.Retract(Manifest, TemplateScopes.Kind, asset.Id);
                return DeleteFromDisk(new BoardRow(document, null, 0));
            case PaletteScopes.Kind:
                _project.Palettes.RemoveAll(p => p.Id == asset.Id);
                break;
            case GradientScopes.Kind:
                _project.Gradients.Remove(asset.Id);
                break;
            case TipScopes.Kind:
                Manifest.Tips?.RemoveAll(t => t.Id == asset.Id);
                break;
            default:
                Status = "Symbols are deleted from the Symbols panel, which knows about their instances.";
                return false;
        }
        ResourceScopes.Retract(Manifest, asset.Kind, asset.Id);
        Status = $"Deleted {Core.Projects.AssetKinds.LabelOf(asset.Kind).ToLowerInvariant()} “{asset.Name}” from the project.";
        AfterRemoval();
        return true;
    }

    private void AfterRemoval()
    {
        Rebuild();
        _changed();
        RequestSave?.Invoke();
    }

    // ---- renaming in place ---------------------------------------------------------

    /// <summary>
    /// Rename a row, on disk as well as in the panel — what committing the
    /// double-click edit does. False when it could not, with the reason said.
    /// </summary>
    /// <remarks>
    /// The mechanics are <see cref="ProjectIo.RenameFolder"/> and
    /// <see cref="ProjectIo.RenameDocument"/>, shared with the docker; only
    /// the wording here is this window's. A failed or empty edit rebuilds, so
    /// the shown name falls back to the model's rather than keeping the text
    /// nobody accepted.
    /// </remarks>
    public bool Rename(BoardRow row, string? name)
    {
        row.IsRenaming = false;
        if (string.IsNullOrWhiteSpace(name))
        {
            Rebuild();
            return false;
        }
        var trimmed = name.Trim();
        var current = row.Document?.Name ?? row.Folder?.Name ?? "";
        if (trimmed == current) return true;   // not a change, not a failure

        bool renamed;
        if (row is { IsFolder: true, Folder: { } folder })
        {
            renamed = ProjectIo.RenameFolder(_project, folder, trimmed) switch
            {
                ProjectIo.RenameOutcome.NameTaken => Refused(
                    $"There is already a folder called “{trimmed}” here."),
                ProjectIo.RenameOutcome.DiskRefused => Refused(
                    $"Could not rename the folder on disk. It is still “{folder.Name}”."),
                _ => true,
            };
        }
        else if (row.Document is { } document)
        {
            renamed = ProjectIo.RenameDocument(_project, document, trimmed) switch
            {
                ProjectIo.RenameOutcome.DiskRefused => Refused(
                    $"Could not rename the file on disk. It is still “{document.Name}”."),
                _ => true,
            };
        }
        else
        {
            // A sheet renames from the Reference sheets panel, where its
            // views live.
            renamed = Refused("Sheets are renamed from the Reference sheets panel.");
        }

        if (renamed)
        {
            Status = $"Renamed to “{trimmed}”.";
            _changed();
            RequestSave?.Invoke();
        }
        Rebuild();
        return renamed;
    }

    private bool Refused(string why)
    {
        Status = why;
        return false;
    }

    // ---- editing a folder's facets (closes Q39's cost) --------------------------------

    /// <summary>
    /// The folder the facet editor is about, or null when the selection is not
    /// one folder.
    /// </summary>
    /// <remarks>
    /// <b>Exactly one.</b> Notes, a pivot and a reading are per-folder things
    /// with per-folder values, and applying one across a multi-selection would
    /// mean deciding what "the notes" of nine folders are. Tags and status are
    /// the bulk operations; these are not.
    /// </remarks>
    public ProjectFolder? EditingFolder =>
        Selected.Count == 1 && Selected[0] is { IsFolder: true, Folder: { } folder } ? folder : null;

    public bool IsEditingFolder => EditingFolder is not null;

    public string EditingFolderName => EditingFolder?.Name ?? "";

    /// <summary>
    /// What this folder is, in the artist's words. Written straight through.
    /// </summary>
    /// <remarks>
    /// Q44's rule holds here too — notes are metadata, nothing is destructive,
    /// and typing is its own undo. Empties back to null so a folder whose notes
    /// were typed and then cleared writes no key.
    /// </remarks>
    public string FolderNotes
    {
        get => EditingFolder?.Notes ?? "";
        set
        {
            if (EditingFolder is not { } folder) return;
            var wanted = value.Trim();
            var next = wanted.Length == 0 ? null : wanted;
            if (folder.Notes == next) return;
            folder.Notes = next;
            OnPropertyChanged();
            Touched($"Notes on “{folder.Name}”.");
        }
    }

    public bool FolderHasPivot => EditingFolder?.Pivot is not null;

    /// <summary>
    /// Give the folder a pivot, or take it away.
    /// </summary>
    /// <remarks>
    /// A default pivot rather than a picker: the pivot's own placement is a
    /// canvas gesture, and what this decides is only whether the folder
    /// <em>has</em> one — which is the thing asset export asks and the thing an
    /// artist could not previously see or change from here.
    /// </remarks>
    [RelayCommand]
    public void TogglePivot()
    {
        if (EditingFolder is not { } folder) return;
        folder.Pivot = folder.Pivot is null ? new Lightbox.Core.Documents.Pivot() : null;
        OnPropertyChanged(nameof(FolderHasPivot));
        Touched(folder.Pivot is null
            ? $"“{folder.Name}” has no pivot. Its frames register on the canvas."
            : $"“{folder.Name}” has a pivot. Asset export registers its frames on it.");
    }

    /// <summary>Whether this folder has been read, for the editor to offer clearing it.</summary>
    public bool FolderHasReading => EditingFolder?.HasReading ?? false;

    public string FolderReadingLabel =>
        EditingFolder?.Taxonomy is not { } reading
            ? ""
            : reading.Reviewed
                ? $"{reading.Kind}, {Count(reading.Parts.Count, "part")} — corrected by you"
                : $"{reading.Kind}, {Count(reading.Parts.Count, "part")}";

    /// <summary>
    /// What clearing this folder's reading would discard, as a sentence.
    /// </summary>
    /// <remarks>
    /// <b>Q35's condition, and Q39 leans on it.</b> Under the old model "delete
    /// character" was explicitly destructive; under the derived one, clearing a
    /// reading quietly takes the pivot, the variants and a hand-corrected
    /// taxonomy with it. The facet list lives behind a click (Q39), so this
    /// sentence is where an artist is told — at the moment of the act rather
    /// than in passing.
    /// </remarks>
    public string WhatClearingCosts =>
        EditingFolder?.WhatClearingTheReadingDiscards() is not { Count: > 0 } lost
            ? ""
            : $"Clearing this discards {string.Join(", ", lost)}.";

    /// <summary>
    /// Clear the reading. Guarded by <see cref="WhatClearingCosts"/> in the view.
    /// </summary>
    /// <remarks>
    /// It clears the reading and nothing else — the pivot and the variants stay
    /// where they are. The warning says what <em>stops meaning anything</em>
    /// rather than what gets deleted, which is the honest version: a pivot on a
    /// folder nothing has read is still a pivot, it just no longer describes a
    /// subject.
    /// </remarks>
    [RelayCommand]
    public void ClearReading()
    {
        if (EditingFolder is not { } folder || folder.Taxonomy is null) return;
        folder.Taxonomy = null;
        Touched($"“{folder.Name}” has no reading. Read it again from the Project panel.");
    }

    /// <summary>Mark the reading as yours, so a re-read will not overwrite it.</summary>
    /// <remarks>
    /// <b>The flag shipped in PR #48 with nothing that could set it</b>, and the
    /// refusal message said "clear it first" about a control that did not exist.
    /// This is that control.
    /// </remarks>
    [RelayCommand]
    public void MarkReadingReviewed()
    {
        if (EditingFolder?.Taxonomy is not { } reading) return;
        reading.Reviewed = !reading.Reviewed;
        OnPropertyChanged(nameof(FolderReadingLabel));
        OnPropertyChanged(nameof(WhatClearingCosts));
        Touched(reading.Reviewed
            ? $"The reading of “{EditingFolder!.Name}” is yours. A re-read will refuse rather than overwrite it."
            : $"The reading of “{EditingFolder!.Name}” is the model's again.");
    }

    /// <summary>The folder's variants, for the editor to list.</summary>
    public IReadOnlyList<SubjectVariant> FolderVariants => EditingFolder?.Variants ?? [];

    [ObservableProperty]
    private string _newVariantName = "";

    [RelayCommand]
    public void AddVariant()
    {
        if (EditingFolder is not { } folder) return;
        var name = NewVariantName.Trim();
        if (name.Length == 0) return;
        var variant = ProjectIo.AddVariant(_project, folder, name);
        NewVariantName = "";
        Touched(variant.PaletteId is null
            ? $"“{name}” added. It has no palette of its own — this folder shares none yet."
            : $"“{name}” added, with its own copy of the palette.");
    }

    /// <summary>Remove a variant, and the documents it replaced go back to shared.</summary>
    /// <remarks>
    /// The override documents are left in the project rather than deleted: a
    /// variant is an arrangement, and removing one must not be the fastest way
    /// to delete the art made for it. They become ordinary documents in the
    /// folder, which is what the docker will then show.
    /// </remarks>
    [RelayCommand]
    public void RemoveVariant(SubjectVariant? variant)
    {
        if (EditingFolder is not { } folder || variant is null) return;
        if (folder.Variants is not { } variants) return;
        var kept = variant.Overrides.Count;
        variants.Remove(variant);
        if (variants.Count == 0) folder.Variants = null;
        _project.ActiveVariant.Remove(folder.Id);
        Touched(kept == 0
            ? $"“{variant.Name}” removed."
            : $"“{variant.Name}” removed. {Count(kept, "drawing")} it replaced stay in the folder.");
    }

    private void Touched(string said)
    {
        Status = said;
        Rebuild();
        _changed();
    }

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
        var was = document.Status;
        document.Status = move.Status;
        var kept = CaptureMilestone(document, was);
        Status = move.Status is { } s
            ? $"“{document.Name}” is {AssetStatuses.Label(s)}.{(kept ? " A milestone version was kept." : "")}"
            : $"“{document.Name}” has no status.";
        Rebuild();
        _changed();
    }

    // ---- the export tab ------------------------------------------------------------------

    /// <summary>One artifact an export would write.</summary>
    /// <param name="Scope">The folder whose subtree becomes this file, or the project.</param>
    public sealed record ExportRow(
        string Scope, string Preset, IReadOnlyList<string> Documents,
        int ExcludedCount, bool IsEmpty)
    {
        public string Contents => string.Join(", ", Documents);

        public int Count => Documents.Count;

        public bool HasExcluded => ExcludedCount > 0;

        public string Excluded => $"{ExcludedCount} held back by status";
    }

    /// <summary>
    /// What exporting the whole project would produce, standing still.
    /// </summary>
    /// <remarks>
    /// <b>`ExportPlan` already produced this and only a confirmation dialog read
    /// it.</b> A plan you can only see in the half-second before it runs is a
    /// plan nobody checks, and the count is exactly the thing worth checking —
    /// it is how you find out that most of a scope is held back by status before
    /// wondering why the sheet is half empty.
    /// <para>
    /// It ran nowhere until the owner asked (2026-08-13) — "read-only, the
    /// export window's job" was the original call, and what changed it is that
    /// this tab is where the grouped artifacts live: a plan you can read but
    /// must leave the window to run is a detour. Two surfaces cannot disagree
    /// about what export means because they share the docker's resolution and
    /// the one runner (<see cref="RunExportToAsync"/>).
    /// </para>
    /// </remarks>
    public IReadOnlyList<ExportRow> ExportRows
    {
        get
        {
            var artifacts = ExportPlan.For(Manifest, selection: null, PresetById);
            return
            [
                .. artifacts.Select(a => new ExportRow(
                    a.Scope?.Name ?? _project.Name,
                    PresetById(a.PresetId)?.Name ?? a.PresetId,
                    [.. a.Documents.Select(d => d.Name)],
                    a.Excluded.Count,
                    a.IsEmpty)),
            ];
        }
    }

    /// <summary>The sentence the export confirmation reads, shown here instead.</summary>
    public string ExportSummary =>
        ExportPlan.Describe(ExportPlan.For(Manifest, selection: null, PresetById));

    /// <summary>
    /// The docker's plan resolution, injected so both surfaces run the one
    /// export the one way — loading, preset fallback and pathing included.
    /// </summary>
    public Func<string, List<string>, IReadOnlyList<ProjectViewModel.PlannedArtifact>>? ResolveExport { get; set; }

    /// <summary>Success bookkeeping, the docker's — a failed artifact stays stale.</summary>
    public Action<ExportArtifact, string?>? RecordExport { get; set; }

    /// <summary>
    /// Run the whole plan into a folder — the view picks the folder, this does
    /// the rest, off-thread per artifact like the docker's own export.
    /// </summary>
    public async Task<int> RunExportToAsync(string destination)
    {
        if (ResolveExport is null) return 0;
        var missing = new List<string>();
        var planned = ResolveExport(destination, missing);
        if (planned.Count == 0)
        {
            Status = missing.Count > 0
                ? $"Nothing exported — {string.Join(", ", missing)} could not be read."
                : "Nothing to export: every document was held back.";
            return 0;
        }

        var written = 0;
        var failed = new List<string>();
        foreach (var item in planned)
        {
            try
            {
                var run = await Task.Run(
                    () => Services.ExportRunner.Run(item.Documents, item.Preset, item.Path, item.Names));
                // No files means the runner refused, and said why.
                if (run.Files.Count == 0)
                {
                    failed.Add($"{item.Name}: {run.Summary}");
                    continue;
                }
                written++;
                RecordExport?.Invoke(item.Artifact, item.Path);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Name}: {ex.Message}");
            }
        }
        Status = failed.Count == 0
            ? $"Exported {written} artifact(s) → {destination}."
            : $"Exported {written} artifact(s); {failed.Count} refused or failed — "
              + string.Join("; ", failed);
        _changed();
        Rebuild();
        return written;
    }

    private ExportPreset? PresetById(string id) =>
        (Manifest.ExportPresets ?? []).FirstOrDefault(p => p.Id == id)
        ?? ExportPreset.BuiltIns.FirstOrDefault(p => p.Id == id);

    // ---- the assets tab ----------------------------------------------------------------

    /// <summary>The kinds a scope can declare, in the order the Assets tab lists them.</summary>
    /// <remarks>
    /// <c>ProjectBoard.Kinds</c> rather than a second list here — the docker has
    /// seven `ShareableX` properties and this tab needs the same seven, and a
    /// copy is a second thing to update when a ninth kind arrives.
    /// </remarks>
    public static IReadOnlyList<string> AssetKinds => ProjectBoard.Kinds;

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
                new(_project.Name, 0, Cells(Manifest.Resources, null, null, true),
                    null, null, isProject: true),
            };
            foreach (var row in Rows)
            {
                // A sheet is an asset, not a scope. Before this guard a sheet
                // row fell into the folder branch below and put its folder in
                // the table twice — a second "Knight" row whose chips edited
                // the same folder as the first.
                if (row.Sheet is not null) continue;
                scopes.Add(row.Document is { } document
                    ? new AssetScope(
                        document.Name, row.Depth + 1,
                        Cells(document.Resources, null, document, false),
                        null, document, isProject: false)
                    : new AssetScope(
                        row.Folder!.Name, row.Depth + 1,
                        Cells(row.Folder!.Resources, row.Folder, null, false),
                        row.Folder, null, isProject: false));
            }
            return scopes;
        }
    }

    private IReadOnlyList<AssetCell> Cells(
        List<ScopedResource>? declared, ProjectFolder? folder, DocumentRef? document, bool onProject) =>
        [.. AssetKinds.Select(k => new AssetCell(
            k,
            [.. (declared ?? []).Where(r => r.Kind == k).Select(
                r => new Declaration(
                    r, ProjectBoard.NameOf(_project, k, r.Id), folder, document, onProject))]))];

    /// <summary>
    /// The scope the Assets tab is about to give something to.
    /// </summary>
    /// <remarks>
    /// Its own selection rather than the Structure tab's, because the two tabs
    /// list different things — the Assets table has a row for the project, which
    /// the tree does not.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssetScope))]
    [NotifyPropertyChangedFor(nameof(AssetScopeLabel))]
    [NotifyPropertyChangedFor(nameof(OfferChoices))]
    private AssetScope? _selectedScope;

    public bool HasAssetScope => SelectedScope is not null;

    public string AssetScopeLabel => SelectedScope?.Name ?? "";

    /// <summary>
    /// Everything the selected scope could be given, across every kind.
    /// </summary>
    /// <remarks>
    /// One flat list rather than a menu per kind, because the artist knows what
    /// they want to share and not which of eight words the application files it
    /// under. The kind is on the label so the answer is still legible.
    /// <para>
    /// <c>reference</c> is absent because sheets share by being <em>filed</em>
    /// — drag one from the library onto a folder. The declaration kind that
    /// used to be offered here was write-only and is retired (B133).
    /// </para>
    /// </remarks>
    public IReadOnlyList<OfferChoice> OfferChoices
    {
        get
        {
            if (SelectedScope is not { } scope) return [];
            var already = scope.All.Select(d => d.Resource).ToHashSet();
            var choices = new List<OfferChoice>();
            foreach (var kind in AssetKinds)
            {
                // A template default is about the documents made *under* a
                // scope, and a document scope has none — TemplateScopes reads
                // the folder chain only, so an offer here would write an entry
                // nothing resolves.
                if (kind == TemplateScopes.Kind && scope.Document is not null) continue;
                foreach (var offer in ProjectBoard.Offers(_project, kind))
                {
                    Offer(kind, offer.Id, offer.Name);
                }
            }
            return choices;

            void Offer(string kind, string id, string name)
            {
                if (already.Any(r => r.Kind == kind && r.Id == id)) return;
                choices.Add(new OfferChoice(
                    scope, kind, id, $"{Core.Projects.AssetKinds.LabelOf(kind)} · {name}"));
            }
        }
    }

    /// <summary>
    /// Picking one shares it, rather than needing a second Apply click.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the bulk status picker: undoing is by doing the
    /// opposite, and that only stays cheap if doing it is one gesture.
    /// </remarks>
    [ObservableProperty]
    private OfferChoice? _offerToDeclare;

    partial void OnOfferToDeclareChanged(OfferChoice? value)
    {
        if (value is null) return;
        DeclareOnScope(value);
        OfferToDeclare = null;
    }

    /// <summary>Give the selected scope one of the things it could have.</summary>
    /// <remarks>
    /// <b>Declaring the first of a kind changes what everything else sees.</b>
    /// The narrowing kinds read "everything applies" until something is declared
    /// — `AnyDeclared` is the switch — so this is the click that turns every
    /// palette everywhere into only what is declared. Said out loud, because a
    /// picker that quietly shrinks is a bad way to learn it.
    /// </remarks>
    [RelayCommand]
    public void DeclareOnScope(OfferChoice? choice)
    {
        if (choice is null) return;

        // The one-at-a-time kinds replace rather than accumulate — a scope
        // starts new documents from one template and exports one way, and two
        // declarations of either would make which-one-wins depend on
        // insertion order. The docker's menu went through SetDefault and
        // SetPreset for exactly this reason; now that this tab is the whole
        // declaration surface, so does it.
        if (choice.Kind == TemplateScopes.Kind && choice.Scope.Document is null)
        {
            SetTemplateDefault(
                choice.Scope, choice.Id, ProjectBoard.NameOf(_project, choice.Kind, choice.Id));
            return;
        }
        if (choice.Kind == ExportScopes.Kind && choice.Scope.Document is null)
        {
            ExportScopes.SetPreset(Manifest, choice.Scope.Folder, choice.Id);
            Status = $"{ProjectBoard.NameOf(_project, choice.Kind, choice.Id)} is how "
                + $"{choice.Scope.Name} exports — and where one file ends.";
            AfterAssetChange(choice.Scope);
            return;
        }

        var first = !AnyDeclaredOf(choice.Kind);

        if (choice.Scope.Document is { } document)
        {
            // A document exports one way at a time too; its own list keeps the
            // same replace rule the folder path gets from SetPreset.
            if (choice.Kind == ExportScopes.Kind)
            {
                document.Resources?.RemoveAll(r => r.Kind == ExportScopes.Kind);
            }
            ResourceScopes.DeclareOn(document, choice.Kind, choice.Id);
        }
        else
        {
            ResourceScopes.Declare(
                Manifest, choice.Scope.Folder, choice.Kind, choice.Id);
        }

        var name = ProjectBoard.NameOf(_project, choice.Kind, choice.Id);
        Status = first
            ? $"{name} shared with {choice.Scope.Name} — {Feeds(choice.Scope)}. {choice.Kind} is now scoped — "
              + "elsewhere only what is declared there is offered."
            : $"{name} shared with {choice.Scope.Name} — {Feeds(choice.Scope)}.";
        AfterAssetChange(choice.Scope);
    }

    /// <summary>
    /// What a declaration on this scope reaches, said so the artist is told
    /// rather than left to infer it from the resolution rules.
    /// </summary>
    private static string Feeds(AssetScope scope) =>
        scope.Document is not null ? $"it feeds only “{scope.Name}”"
        : scope.IsProject ? "it feeds every document in the project"
        : $"it feeds every document under “{scope.Name}”";

    /// <summary>
    /// Make a template the scope's default, saying what it displaced.
    /// </summary>
    /// <remarks>
    /// <c>TemplateScopes.SetDefault</c> replaces silently, which is correct
    /// and worth a sentence: a click that quietly un-decides an earlier
    /// decision is the kind of thing somebody notices a week later.
    /// </remarks>
    private void SetTemplateDefault(AssetScope scope, string id, string name)
    {
        var was = (scope.Folder is { } folder ? folder.Resources : Manifest.Resources)?
            .FirstOrDefault(r => r.Kind == TemplateScopes.Kind);
        TemplateScopes.SetDefault(Manifest, scope.Folder, id);
        var replaced = was is not null && was.Id != id
            ? $" It replaces {ProjectBoard.NameOf(_project, TemplateScopes.Kind, was.Id)} — "
              + "a scope starts new documents from one template."
            : "";
        Status = scope.IsProject
            ? $"New documents in the project start from “{name}”.{replaced}"
            : $"New documents in “{scope.Name}” start from “{name}”.{replaced}";
        AfterAssetChange(scope);
    }

    /// <summary>Take one declaration back.</summary>
    [RelayCommand]
    public void UndeclareOnScope(Declaration? declaration)
    {
        if (declaration is null) return;
        var removed = declaration.Document is { } document
            ? ResourceScopes.Undeclare(document, declaration.Resource)
            : ResourceScopes.Undeclare(Manifest, declaration.Folder, declaration.Resource);
        if (!removed) return;

        // The other half of the sentence above: taking back the last declaration
        // of a kind puts the project back to "everything applies".
        Status = AnyDeclaredOf(declaration.Resource.Kind)
            ? $"{declaration.Name} is no longer shared here."
            : $"{declaration.Name} is no longer shared here. Nothing scopes "
              + $"{declaration.Resource.Kind} now, so everything applies again.";
        AfterAssetChange(null);
    }

    /// <summary>
    /// Whether anything anywhere declares this kind — the narrowing switch.
    /// </summary>
    /// <remarks>
    /// Walks all three tiers rather than calling one kind's <c>AnyDeclared</c>,
    /// because each kind has its own and this has to answer for eight. Document
    /// declarations count, which the per-kind helpers predate.
    /// </remarks>
    private bool AnyDeclaredOf(string kind) =>
        (Manifest.Resources?.Any(r => r.Kind == kind) ?? false)
        || ProjectFolders.All(Manifest).Any(f => f.Resources?.Any(r => r.Kind == kind) ?? false)
        || Manifest.Documents.Any(d => d.Resources?.Any(r => r.Kind == kind) ?? false);

    /// <summary>Rebuild and keep the Assets tab pointing where it was.</summary>
    private void AfterAssetChange(AssetScope? keep)
    {
        var name = keep?.Name ?? SelectedScope?.Name;
        Rebuild();
        SelectedScope = name is null ? null : Assets.FirstOrDefault(s => s.Name == name);
        _changed();
    }

    // ---- the asset library ---------------------------------------------------------

    /// <summary>
    /// The kinds the library lists — the assets an artist recognises as
    /// <em>things</em>: a sheet, a palette, a gradient, a brush tip, a symbol,
    /// a template.
    /// </summary>
    /// <remarks>
    /// Templates are here by the owner's call (2026-08-13): a template is a
    /// document wearing a flag, but an artist reaches for it as a thing — so
    /// it wears the kind like the rest and drags like the rest, with the one
    /// difference that dropping it <em>replaces</em> the scope's default
    /// rather than accumulating. Guides and export presets stay out — those
    /// are settings, and a library that lists settings beside artwork stops
    /// reading as a library; the share picker above still declares them.
    /// </remarks>
    private static readonly IReadOnlyList<string> LibraryKinds =
        [PaletteScopes.Kind, GradientScopes.Kind, TipScopes.Kind, SymbolScopes.Kind, TemplateScopes.Kind];

    /// <summary>
    /// Every asset the project has, each wearing its designation and glyph —
    /// what an artist drags onto a scope to feed it.
    /// </summary>
    /// <remarks>
    /// Sheets first because reference art is what everything else is drawn
    /// against, matching the order the tree already lists them in.
    /// </remarks>
    public IReadOnlyList<AssetEntry> Library
    {
        get
        {
            var entries = new List<AssetEntry>();
            foreach (var sheet in Manifest.Sheets ?? [])
            {
                entries.Add(new AssetEntry(ReferenceScopes.Kind, sheet.Id, sheet.Name));
            }
            foreach (var kind in LibraryKinds)
            {
                if (kind == TemplateScopes.Kind)
                {
                    // From the manifest hint, not Templates.InProject: the
                    // library rebuilds on every edit, and InProject reads
                    // every document to find the flag — the one cost the
                    // folder layout exists to avoid. The hint refreshes at
                    // save, which is also when the library is re-read.
                    foreach (var document in Manifest.Documents.Where(d => d.IsTemplate == true))
                    {
                        entries.Add(new AssetEntry(kind, document.Id, document.Name));
                    }
                    continue;
                }
                foreach (var offer in ProjectBoard.Offers(_project, kind))
                {
                    entries.Add(new AssetEntry(kind, offer.Id, offer.Name));
                }
            }
            return entries;
        }
    }

    public bool HasLibrary => Library.Count > 0;

    /// <summary>
    /// An asset landed on a scope — what a drag from the library does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two operations behind one gesture, and the asset decides which.</b> A
    /// sheet is <em>filed</em>: `ProjectSheets.VisibleTo` already feeds a
    /// folder's sheets to every document under it, so moving the entry is the
    /// whole assignment and no declaration is written (writing one instead
    /// would be B133 — an entry nothing reads). Everything else is
    /// <em>declared</em> on the scope, which is what the resolvers read.
    /// </para>
    /// <para>
    /// Either way the status line says what the folder now feeds, because the
    /// inheritance is the point of the gesture and it is invisible from the
    /// row itself.
    /// </para>
    /// </remarks>
    public void DropOnScope(AssetEntry? asset, AssetScope? scope)
    {
        if (asset is null || scope is null) return;

        if (asset.Kind == ReferenceScopes.Kind
            && ProjectSheets.FindRef(Manifest, asset.Id) is { } sheet)
        {
            if (scope.Document is not null)
            {
                Status = "A sheet is filed on a folder or on the project, "
                    + "so every drawing under it can consult it.";
                return;
            }
            if (!ProjectSheets.Refile(_project, sheet, scope.Folder))
            {
                Status = $"Could not move “{sheet.Name}”. It is still where it was.";
                return;
            }
            Status = scope.Folder is null
                ? $"“{sheet.Name}” is filed on the project — every document sees it."
                : $"“{sheet.Name}” is filed in “{scope.Folder.Name}” — it feeds every document under it.";
            AfterAssetChange(scope);
            return;
        }

        if (scope.All.Any(d => d.Resource.Kind == asset.Kind && d.Resource.Id == asset.Id))
        {
            Status = $"“{asset.Name}” is already shared with {scope.Name}.";
            return;
        }

        if (asset.Kind == TemplateScopes.Kind)
        {
            if (scope.Document is not null)
            {
                Status = "A template is the default for new documents, "
                    + "so it goes on a folder or on the project.";
                return;
            }
            SetTemplateDefault(scope, asset.Id, asset.Name);
            return;
        }

        var first = !AnyDeclaredOf(asset.Kind);
        if (scope.Document is { } document)
        {
            ResourceScopes.DeclareOn(document, asset.Kind, asset.Id);
        }
        else
        {
            ResourceScopes.Declare(Manifest, scope.Folder, asset.Kind, asset.Id);
        }
        Status = first
            ? $"{asset.Designation} “{asset.Name}” shared with {scope.Name} — {Feeds(scope)}. "
              + $"{asset.Kind} is now scoped — elsewhere only what is declared there is offered."
            : $"{asset.Designation} “{asset.Name}” shared with {scope.Name} — {Feeds(scope)}.";
        AfterAssetChange(scope);
    }

    // ---- creating assets from the Assets tab -----------------------------------
    //
    // The tab's right-click is "make an asset", where Structure's is "make
    // structure" — same prompt-first rules (B65), and a new asset made with a
    // scope row under the pointer is shared there at once, because that is
    // what making it *there* means.

    /// <summary>The folder the Assets tab's selection means, for a new asset.</summary>
    private ProjectFolder? AssetTargetFolder =>
        SelectedScope?.Folder
        ?? (SelectedScope?.Document is { } document
            ? ProjectFolders.ById(Manifest, document.FolderId)
            : null);

    /// <summary>A blank reference sheet, filed where the selection is.</summary>
    [RelayCommand]
    public async Task CreateSheetAsync()
    {
        var name = AskName is null ? "Character" : await AskName("reference sheet", "Character");
        if (name is null) return;   // cancelled: nothing is written
        var folder = AssetTargetFolder;
        var sheet = ProjectSheets.Add(_project, ProjectViewModel.Named(name, "Character"), folder);
        _changed();
        RequestSave?.Invoke();
        Rebuild();
        Status = folder is null
            ? $"“{sheet.Name}” added — every document sees it."
            : $"“{sheet.Name}” added — it feeds every document under “{folder.Name}”.";
    }

    [RelayCommand]
    public async Task CreatePaletteAsync()
    {
        var name = AskName is null ? "Palette" : await AskName("palette", "Palette");
        if (name is null) return;
        var palette = new Palette { Name = ProjectViewModel.Named(name, "Palette") };
        _project.Palettes.Add(palette);
        DeclareNewlyCreated(PaletteScopes.Kind, palette.Id, palette.Name);
    }

    [RelayCommand]
    public async Task CreateGradientAsync()
    {
        var name = AskName is null ? "Gradient" : await AskName("gradient", "Gradient");
        if (name is null) return;
        var gradient = new Gradient { Name = ProjectViewModel.Named(name, "Gradient") };
        _project.Gradients[gradient.Id] = gradient;
        DeclareNewlyCreated(GradientScopes.Kind, gradient.Id, gradient.Name);
    }

    /// <summary>A blank document already wearing the template flag.</summary>
    /// <remarks>
    /// Making one from nothing, where File ▸ Use as template marks work
    /// already done — both end in the same flag, and this one exists so
    /// setting up a project's templates does not require drawing first.
    /// </remarks>
    [RelayCommand]
    public async Task CreateTemplateAsync()
    {
        var name = AskName is null ? "Template" : await AskName("template", "Template");
        if (name is null) return;
        var doc = NewDocument?.Invoke() ?? DocumentFactory.CreateDoc();
        Templates.SetTemplate(doc, true);
        var reference = ProjectIo.AddDocument(
            _project, ProjectViewModel.Named(name, "Template"), doc, AssetTargetFolder);
        DocumentCreated?.Invoke(reference);
        _changed();
        RequestSave?.Invoke();
        Rebuild();
        Status = $"“{reference.Name}” is a template. New from template… offers it; "
            + "drop it on a folder here to make it that folder's default.";
    }

    /// <summary>Share a just-made asset where it was made, and say what that did.</summary>
    private void DeclareNewlyCreated(string kind, string id, string name)
    {
        var designation = Core.Projects.AssetKinds.LabelOf(kind);
        if (AssetTargetFolder is not { } folder)
        {
            Status = $"{designation} “{name}” added to the project.";
        }
        else
        {
            var first = !AnyDeclaredOf(kind);
            ResourceScopes.Declare(Manifest, folder, kind, id);
            Status = first
                ? $"{designation} “{name}” added and shared with “{folder.Name}” — it feeds every "
                  + $"document under it. {kind} is now scoped — elsewhere only what is declared "
                  + "there is offered."
                : $"{designation} “{name}” added and shared with “{folder.Name}” — it feeds every "
                  + "document under it.";
        }
        _changed();
        RequestSave?.Invoke();
        Rebuild();
    }

    /// <summary>
    /// Flip a declaration between its subtree and the whole project — the
    /// docker's Reach menu, as one click on the chip it is about.
    /// </summary>
    [RelayCommand]
    public void ToggleReach(Declaration? declaration)
    {
        if (declaration is null || !declaration.CanReach) return;
        if (declaration.IsPublished) ResourceScopes.Demote(declaration.Resource);
        else ResourceScopes.Promote(declaration.Resource);
        Status = declaration.Resource.ReachOrDefault == ResourceReach.Project
            ? $"{declaration.Name} reaches the whole project."
            : $"{declaration.Name} reaches its own subtree again.";
        AfterAssetChange(null);
    }
}
