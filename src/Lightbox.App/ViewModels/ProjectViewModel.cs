using System.Windows.Input;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One row in the project tree: a folder, or a document filed in one.
/// </summary>
/// <remarks>
/// <b>B114.</b> There used to be three kinds of heading — character, scene and
/// folder — because a character grouped drawings by <i>who</i> and a scene by
/// <i>when</i>. They are all folders now, and which one a folder <em>reads</em>
/// as is derived from what it carries: a reading makes it a character, a running
/// order makes it a scene, and it can be both or neither. The tree stopped
/// needing to know.
/// </remarks>
public sealed partial class ProjectRow : ObservableObject
{
    /// <summary>A folder the artist made, at <paramref name="depth"/> from the root.</summary>
    /// <remarks>
    /// <b>B86.</b> Depth is carried rather than derived, because the row does not
    /// know the manifest and asking it to would make every row hold a reference
    /// to the project so it could walk its own ancestry on each repaint.
    /// </remarks>
    public ProjectRow(ProjectFolder folder, int depth, string? duration = null)
    {
        Folder = folder;
        Depth = depth;
        Duration = duration;
        _name = folder.Name;
    }

    /// <summary>A document filed in a folder.</summary>
    public ProjectRow(ProjectFolder folder, DocumentRef document, int depth, string? duration = null)
    {
        Folder = folder;
        Animation = document;
        Depth = depth;
        Duration = duration;
        _name = document.Name;
    }

    /// <summary>A document filed nowhere — it belongs to the project itself.</summary>
    public ProjectRow(DocumentRef document, string? duration = null)
    {
        Animation = document;
        Duration = duration;
        _name = document.Name;
    }

    private ProjectRow(string name)
    {
        IsRoot = true;
        _name = name;
    }

    /// <summary>The project folder itself, as a row.</summary>
    /// <remarks>
    /// <b>B62.</b> The tree listed everything <em>in</em> the project and not the
    /// project, so there was no way to select it — and no way to see where it
    /// actually lives on disk. That is what made moving the reveal action to the
    /// selection a removal rather than a swap: the old toolbar behaviour had
    /// nowhere to go. With a row for it, "show the project folder" is "select
    /// this and reveal it", which is one rule instead of two.
    /// </remarks>
    public static ProjectRow Root(string name) => new(name);

    /// <summary>Whether this row is the project itself rather than something in it.</summary>
    public bool IsRoot { get; }

    /// <summary>
    /// The folder this row <em>is</em>, or the one a document is filed in.
    /// Null on a document that belongs to the project rather than to any
    /// folder — a background, a colour test, a one-off illustration.
    /// </summary>
    /// <remarks>
    /// <b>B85/B86, and the only container since B114.</b> A row is a container
    /// or a thing inside one, so a document's row and its folder's row both
    /// answer "which folder is this about", which is what creating and dropping
    /// into a folder both need.
    /// </remarks>
    public ProjectFolder? Folder { get; }

    /// <summary>How far this row is indented, in tree levels.</summary>
    public int Depth { get; }

    /// <summary>Null on a folder row.</summary>
    public DocumentRef? Animation { get; }

    /// <summary>
    /// How long it runs, already formatted, or null when nothing knows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null rather than "0:00". A running time that quietly reports the shots
    /// it could not measure as zero is the number somebody schedules against.
    /// </para>
    /// <para>
    /// Settable and observable so a re-read can update a row in place rather than
    /// replacing it — see <see cref="ProjectViewModel.Refresh"/> and
    /// <see cref="Describes"/>.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuration))]
    private string? _duration;

    public bool HasDuration => Duration is { Length: > 0 };

    /// <summary>
    /// Whether this row and <paramref name="other"/> stand for exactly the same
    /// thing in the same place.
    /// </summary>
    /// <remarks>
    /// <b>B61.</b> A re-read used to rebuild every row from scratch, which is
    /// correct on screen and destroys object identity — and the docker's
    /// interactions live on the row instance. A rename in progress
    /// (<see cref="IsRenaming"/>), an open context menu acting on
    /// <c>Selected</c>, a status flyout mid-click: each of them holds a row, and
    /// each of them silently addressed a discarded object the moment anything
    /// touched the project folder. Once a save arms a directory watch, that is
    /// every save.
    /// <para>
    /// The identity is the underlying objects rather than the key, because a key
    /// can outlive a role: <see cref="ProjectViewModel.Move"/> keeps a document's
    /// id and changes the character above it, so the same id becomes a differently
    /// indented row that is no longer <see cref="IsLoose"/>. Reference equality on
    /// all three says that exactly, and anything it says no to gets a fresh row.
    /// </para>
    /// </remarks>
    internal bool Describes(ProjectRow other) =>
        ReferenceEquals(Folder, other.Folder)
        && ReferenceEquals(Animation, other.Animation)
        && Depth == other.Depth
        && IsRoot == other.IsRoot;

    /// <summary>A folder the artist made, whatever it happens to read as.</summary>
    public bool IsFolder => Folder is not null && Animation is null;

    /// <summary>
    /// Whether the folder this row is has been read.
    /// </summary>
    /// <remarks>
    /// <b>Q40.</b> A facet question, not a kind. There was an <c>IsScene</c>
    /// beside this and it is gone: the docker does not decide what a folder is,
    /// because the artist already said so with its glyph.
    /// </remarks>
    public bool HasReading => IsFolder && Folder!.HasReading;

    /// <summary>Whether the folder this row is carries an authored order.</summary>
    public bool HasOrder => IsFolder && Folder!.Order is not null;

    /// <summary>A heading row — any folder.</summary>
    public bool IsHeading => Animation is null;

    /// <summary>A document with nothing above it at all.</summary>
    public bool IsLoose => Animation is not null && Folder is null;

    /// <summary>Whether a folder row is showing what is inside it.</summary>
    /// <remarks>
    /// <b>B86.</b> On the row for binding, and mirrored from the view model's own
    /// set — the set is what survives a rebuild, since a re-read that discards
    /// rows would otherwise expand everything the artist had collapsed. B61 is
    /// the same lesson from the other side.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Twisty))]
    private bool _isCollapsed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The name is being edited in place.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>
    /// How far in this row sits, in pixels.
    /// </summary>
    /// <remarks>
    /// <b>B86.</b> Was a flat "heading or not" — 0 or 14 — which is all a
    /// two-level tree needs and cannot express a folder inside a folder. Depth
    /// now drives it, and a document inside a folder sits one level in from the
    /// folder itself, which is why the +1.
    /// </remarks>
    public double Indent =>
        Folder is null ? 0 : (Depth + (Animation is null ? 0 : 1)) * 14;

    /// <summary>What the row shows in front of its name.</summary>
    /// <remarks>
    /// <b>Q38.</b> A folder's glyph is the artist's, and the fallback is the
    /// plain folder. Nothing derives it from what the folder carries: that would
    /// put the code back in the business of deciding which facet makes a folder
    /// "a character", and it would pick wrong the first time somebody makes a
    /// prop folder with a pivot.
    /// <para>
    /// 🗁 for the project itself matches the toolbar button that used to be the
    /// only way to reach the project folder — the row inherited the icon along
    /// with the job (B62).
    /// </para>
    /// </remarks>
    public string Glyph =>
        IsRoot ? "🗁"
        : Animation is null && Folder is { Icon: { Length: > 0 } chosen } ? chosen
        : IsFolder ? "🗀"
        : "▣";

    /// <summary>The chevron on a folder row, or nothing on everything else.</summary>
    public string Twisty => IsFolder ? (IsCollapsed ? "▸" : "▾") : "";

    /// <summary>
    /// The production status, mirrored from the manifest so the row can show it.
    /// </summary>
    /// <remarks>
    /// Observable and set through <c>ProjectViewModel.SetStatus</c> rather than bound
    /// two-way to the manifest: the setter has to save the project and may fire an
    /// export, and neither belongs behind a property assignment.
    /// </remarks>
    [ObservableProperty]
    private AssetStatus? _status;

    public bool HasStatus => Status is not null;

    /// <summary>
    /// The manifest lists this row and there is no file behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B61.</b> The docker is built from the manifest, so an entry survives
    /// its file being deleted from outside the application — another program, a
    /// file manager, a branch switch. The reporter's diagnosis is what makes this
    /// a flag rather than a deletion: after restarting, the files on disk were
    /// correct, so nothing is wrong with what was *written*. The manifest is
    /// simply describing a world that has moved on.
    /// </para>
    /// <para>
    /// Flagged rather than silently dropped, because the two are different
    /// claims. A row that vanishes says "you never had this"; a row marked
    /// missing says "this is in your project and I cannot find it", which is the
    /// true statement and the one an artist can act on. Removing it from the
    /// project is then a decision they make, not one a refresh makes for them.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MissingHint))]
    [NotifyPropertyChangedFor(nameof(IsOnDisk))]
    private bool _missing;

    /// <summary>
    /// This has never been written — it exists in the project and not yet on disk.
    /// </summary>
    /// <remarks>
    /// <b>B76,</b> and the distinction is the whole entry. <see cref="Missing"/>
    /// means *this was on disk and is gone*, which is alarming and worth acting
    /// on. Pending means *this has not been saved yet*, which is ordinary and
    /// worth knowing. Conflating them would either cry wolf on every new
    /// document or hide a deleted file among them.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingHint))]
    [NotifyPropertyChangedFor(nameof(IsOnDisk))]
    private bool _pending;

    /// <summary>What the row says when it has not been written yet.</summary>
    public string PendingHint => Pending ? "not saved yet" : "";

    /// <summary>
    /// Whether the row's file is actually there — false while pending and false
    /// while missing, which is what a greyed row means.
    /// </summary>
    public bool IsOnDisk => !Pending && !Missing;

    /// <summary>What the row says when its file is gone.</summary>
    public string MissingHint => Missing ? "not on disk" : "";

    public string StatusLabel => Status is { } s ? AssetStatuses.Label(s) : "";

    public string StatusColor => Status is { } s ? AssetStatuses.Color(s) : "#00000000";

    partial void OnStatusChanged(AssetStatus? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
    }

    /// <summary>What the project row is remembered by. No id can collide with it.</summary>
    /// <remarks><see cref="Ids.NewId"/> joins with underscores, so a slash is free.</remarks>
    internal const string RootKey = "/";

    /// <summary>The id this row is remembered by across a rebuild.</summary>
    /// <remarks>
    /// <b>B62 found the gap and had to close it.</b> Folder and root were both
    /// absent here, so both keyed as null — and <c>Rebuild</c> restores the
    /// selection with <c>FirstOrDefault(r =&gt; r.Key == keep)</c>, which matches
    /// the <em>first</em> null-keyed row rather than the one that was selected.
    /// Adding a root row at the top of the tree would have made that visible as a
    /// regression: select a folder, save, and the selection jumps to the project.
    /// The underlying fault was already there — with two folders it jumped to the
    /// first one — so this is a fix rather than a defence.
    /// </remarks>
    internal string? Key =>
        Animation?.Id ?? Folder?.Id ?? (IsRoot ? RootKey : null);

    /// <summary>The docker this row belongs to, for its context menu to bind to.</summary>
    /// <remarks>
    /// <para>
    /// <b>The row's own comment warned about this and the menu did it anyway.</b>
    /// A <c>MenuFlyout</c>'s items live in a popup rather than in the window's
    /// visual tree, so <c>$parent[Window].DataContext.ProjectDocker.X</c>
    /// resolves to nothing there. The flyout's actions dodged it by using
    /// <c>Click</c> handlers — but an <c>ItemsSource</c> cannot, and the two
    /// entries that had one were silently empty from the day they shipped:
    /// *Share a palette here* and *Export this as* opened onto nothing.
    /// </para>
    /// <para>
    /// A popup does inherit its target's <em>DataContext</em>, which is the row.
    /// So the lists hang off the row and reach the docker through this. Caught
    /// by <c>TheProjectRowMenuActuallyDoesSomethingWhenClicked</c> asserting the
    /// binding resolved, which is a different claim from "the command works" —
    /// the view model was right the whole time.
    /// </para>
    /// </remarks>
    public ProjectViewModel? Owner { get; internal set; }
}

/// <summary>
/// One entry in a scope submenu, carrying everything the menu item needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the entries are baked rather than templated.</b> A
/// <c>MenuFlyout</c>'s items have no DataContext of their own, so a
/// <c>$parent[Window]</c> or <c>$parent[MenuItem]</c> binding inside one is a
/// coin flip that lands wrong: *Share a palette here* and *Export this as*
/// shipped with exactly that and were empty submenus from the day they landed.
/// With label, command and parameter on the entry, every binding in the
/// template resolves against the entry itself — the one DataContext an
/// <c>ItemsSource</c> child is guaranteed to have.
/// </para>
/// <para>
/// It also moves the wiring somewhere a test can read it. A headless run cannot
/// realise a submenu's containers, so "does the click reach the command" is not
/// answerable from the view; it is answerable here.
/// </para>
/// </remarks>
public sealed record ScopeMenuEntry(string Label, ICommand Command, object? Parameter);

/// <summary>
/// One declaration on the selected scope, named for a person rather than by id.
/// </summary>
/// <param name="Label">"Palette: Knight warms" — the kind, then the thing.</param>
/// <remarks>
/// A view type rather than a model one: <see cref="ScopedResource"/> holds ids
/// because that is what survives a rename, and a menu holding ids is a menu
/// nobody can read.
/// </remarks>
public sealed record DeclarationRow(ScopedResource Resource, string Label)
{
    /// <summary>Whether this one reaches the whole project rather than its subtree.</summary>
    public bool IsPublished => Resource.ReachOrDefault == ResourceReach.Project;

    /// <summary>
    /// The label with its reach, so the toggle says what it is toggling *from*.
    /// </summary>
    /// <remarks>
    /// A checkmark would be the obvious answer and it is the wrong one here: an
    /// unticked row would read as "not shared" when it is shared with this
    /// subtree, which is the ordinary case and the whole point of scoping.
    /// </remarks>
    public string LabelWithReach => IsPublished ? $"{Label} — project-wide" : Label;
}

/// <summary>
/// The project docker's state: the open project, its characters and their
/// animations, and the handful of things you do to them.
///
/// <b>Null is the ordinary state.</b> The app is document-first — it opens on
/// an untitled document with no project and no project UI, exactly as it always
/// has, and a project comes into existence only when one is created or opened.
/// That is the camera's rule applied again: optional means absent, not
/// disabled. Someone who opened the app to draw one picture must never be shown
/// a character tree.
/// </summary>
public sealed partial class ProjectViewModel : ObservableObject, IDisposable
{
    private readonly Func<Doc> _newDocument;
    private readonly Action<DocumentRef, Doc> _open;
    private readonly Action _changed;

    public ProjectViewModel(Func<Doc> newDocument, Action<DocumentRef, Doc> open, Action changed)
    {
        _newDocument = newDocument;
        _open = open;
        _changed = changed;
        Watcher = new Services.ProjectWatcher(Refresh);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    private Project? _project;

    public bool HasProject => Project is not null;

    /// <summary>
    /// Whether there is anything here whose order the artist authored.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Was "does this project have scenes". Ordering is a folder's
    /// now and any folder may have one, so the question the buttons actually
    /// need is whether a tree exists to arrange — the alternative is hiding the
    /// only way to <em>start</em> ordering behind already having ordered
    /// something.
    /// </remarks>
    public bool CanReorder => Project is { } p && ProjectFolders.All(p.Manifest).Count > 0;

    /// <summary>Folders carrying an authored order.</summary>
    /// <remarks>
    /// <b>Q40.</b> Was <c>Scenes</c>, and it excluded folders that had been read
    /// so that a character could not accidentally be one — which is the tie-break
    /// a designation forces you to invent. There is no tie now: a folder with an
    /// order has an order, whatever else it also has.
    /// </remarks>
    public IEnumerable<ProjectFolder> Ordered =>
        Project is { } p ? ProjectFolders.All(p.Manifest).Where(f => f.Order is not null) : [];

    public string ProjectName => Project?.Name ?? "";

    public ObservableCollection<ProjectRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(NearestReading))]
    [NotifyPropertyChangedFor(nameof(SelectedFacets))]
    [NotifyPropertyChangedFor(nameof(HasFacets))]
    [NotifyPropertyChangedFor(nameof(SelectedIcon))]
    private ProjectRow? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// The subject the selection is inside, or the project's first one.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Walks up rather than reading a field, which is what makes a
    /// subfolder under a character inherit that character — the old
    /// <c>Character</c> field could not express it at all.
    /// </remarks>
    public ProjectFolder? NearestReading
    {
        get
        {
            if (Project is not { } project) return null;
            if (Selected?.Folder is { } folder)
            {
                var chain = ProjectFolders.AncestryOf(project.Manifest, folder);
                for (var i = chain.Count - 1; i >= 0; i--)
                    if (chain[i].HasReading) return chain[i];
            }
            return project.WithReading.FirstOrDefault();
        }
    }

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Documents edited since the last save, by <see cref="DocumentRef.Id"/>.</summary>
    private readonly HashSet<string> _dirty = [];

    public IReadOnlySet<string> Dirty => _dirty;

    /// <summary>
    /// Release the directory watch.
    /// </summary>
    /// <remarks>
    /// A view model with an OS handle in it owes the caller a way to give it
    /// back. Nothing in the running application needs this — the watch follows
    /// the project and the process outlives it — but a test that opens forty
    /// projects would otherwise leave forty inotify instances to a finalizer, and
    /// the platform limit is 128 per user. Cheap to offer, expensive to discover
    /// the absence of.
    /// </remarks>
    public void Dispose() => Watcher.Dispose();

    /// <summary>
    /// Notices when the project folder changes on disk and calls
    /// <see cref="Refresh"/>.
    /// </summary>
    /// <remarks>
    /// <b>B61.</b> Owned here rather than by <c>MainViewModel</c> because
    /// <see cref="Adopt"/> is the single funnel every project arrives through, so
    /// this is the one place that cannot be bypassed by a future caller. Exposed
    /// so a test can flush its debounce; nothing else should reach into it.
    /// </remarks>
    public Services.ProjectWatcher Watcher { get; }

    public void Adopt(Project? project)
    {
        Project = project;
        _dirty.Clear();
        // B76. A different project's pending folders are not this one's — and an
        // id carried across would make a saved folder read as unsaved.
        _unwrittenFolders.Clear();
        Rebuild();
    }

    partial void OnSelectedChanged(ProjectRow? value)
    {
        // The scope panel is about whatever is selected, so it has to be told.
        OnPropertyChanged(nameof(DeclarationsOnSelected));
        OnPropertyChanged(nameof(ShareScopeLabel));
        OnPropertyChanged(nameof(Declarations));
        OnPropertyChanged(nameof(HasDeclarations));
        // Whether the row can be reference at all depends on the row, so the
        // menu entry has to appear and disappear with the selection.
        OnPropertyChanged(nameof(CanShareSelectedAsReference));
    }

    // ---- export (Q30 steps 3-4) ---------------------------------------------

    /// <summary>
    /// What exporting the selection would produce, worked out before anything
    /// is written.
    /// </summary>
    /// <remarks>
    /// The plan is separate from the run so the confirmation has something to
    /// count. "2 files from 3 documents, 1 held back by status" is this read
    /// aloud, and it is the safeguard the recursion decision leans on — a wrong
    /// scope shows up as an obviously wrong number where a yes/no could not say
    /// anything at all.
    /// </remarks>
    public IReadOnlyList<ExportArtifact> PlanExport() =>
        Project is not { } project
            ? []
            : ExportPlan.For(project.Manifest, ScopeOfSelected(), PresetById);

    /// <summary>The sentence the export confirmation shows.</summary>
    /// <remarks>
    /// <b>Staleness rides along with the count</b>, rather than getting a badge
    /// of its own. It is the same question asked one moment earlier — *is this
    /// worth exporting* — and the confirmation is already where an artist stops
    /// to read a number. A separate indicator somewhere else would be a second
    /// place to look for the same answer, and the one nobody looks at.
    /// </remarks>
    public string DescribeExportPlan()
    {
        var text = ExportPlan.Describe(PlanExport());
        if (StaleExports() is { Count: > 0 } stale)
        {
            var drifted = stale.Sum(s => s.Drifted);
            text += $". {Plural(stale.Count, "artifact")} moved on since "
                + $"{(stale.Count == 1 ? "it was" : "they were")} last built "
                + $"({Plural(drifted, "document")} changed).";
        }
        return text;
    }

    /// <summary>What the project has built that its work has since moved past.</summary>
    /// <remarks>
    /// Its own property as well as part of the sentence, because the studio
    /// dashboard wants the list rather than the count — and because a caller
    /// that only wants to know *whether* anything drifted should not have to
    /// parse prose to find out.
    /// </remarks>
    public bool HasStaleExports => StaleExports().Count > 0;

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>
    /// A preset by id: the project's own first, then the built-ins.
    /// </summary>
    /// <remarks>
    /// Project before built-in, which is the same precedence every other scoped
    /// resource uses — the nearer definition wins, and a project that overrides
    /// a built-in means it.
    /// </remarks>
    public ExportPreset? PresetById(string id) =>
        Project?.Manifest.ExportPresets?.FirstOrDefault(p => p.Id == id)
        ?? ExportPreset.BuiltIns.FirstOrDefault(p => p.Id == id);

    /// <summary>Note what an artifact was built from, so it can go stale later.</summary>
    public void RecordExport(ExportArtifact artifact, string? path = null)
    {
        if (Project is not { } project) return;
        // Keyed by scope, because a scope produces one deliverable. A loose
        // document that exported on its own keys by the document instead.
        var key = artifact.Scope?.Id ?? artifact.Documents.FirstOrDefault()?.Id;
        if (key is null) return;
        (project.Manifest.ExportRecords ??= [])[key] = ExportStaleness.RecordOf(artifact, path);
        _changed();
    }

    /// <summary>
    /// The artifacts that have moved on since they were written, named.
    /// </summary>
    /// <remarks>
    /// What the studio dashboard wants, and it costs nothing extra: the record
    /// is already there and staleness is a comparison rather than a flag
    /// somebody has to remember to clear.
    /// </remarks>
    public IReadOnlyList<(ExportArtifact Artifact, int Drifted)> StaleExports()
    {
        if (Project is not { } project || project.Manifest.ExportRecords is not { } records) return [];
        var stale = new List<(ExportArtifact, int)>();
        foreach (var artifact in ExportPlan.For(project.Manifest, null, PresetById))
        {
            var key = artifact.Scope?.Id ?? artifact.Documents.FirstOrDefault()?.Id;
            if (key is null || !records.TryGetValue(key, out var record)) continue;
            var drifted = ExportStaleness.Drifted(record, artifact);
            if (drifted.Count > 0) stale.Add((artifact, drifted.Count));
        }
        return stale;
    }

    /// <summary>
    /// Where a test export writes — beside the project, never over a deliverable.
    /// </summary>
    /// <remarks>
    /// <b>A test is a different destination, not a smaller export.</b> If
    /// looking at one cycle overwrote the shipped sheet, testing would break the
    /// build, which is the opposite of what it is for. Beside the project rather
    /// than in the system temp folder so an engine watching the project tree can
    /// hot-reload it.
    /// </remarks>
    public string? TestExportFolder =>
        Project is { } project ? Path.Combine(project.Root, "test-exports") : null;

    /// <summary>
    /// One document, exported now, under the preset that governs it — ignoring
    /// grouping and the status filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are ignored deliberately. Grouping is about the deliverable and a
    /// test is not one; the status filter exists to keep work in progress out of
    /// a shipped sheet, and a test is the case where an artist explicitly wants
    /// the work in progress.
    /// </para>
    /// <para>
    /// Returns the plan rather than running it, so the caller owns the file
    /// system — the same split <c>ExportPlan</c> makes, for the same reason:
    /// this stays testable without writing anything.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One artifact resolved to the things a caller needs to write it: its
    /// documents loaded, its preset, and where it goes under a chosen folder.
    /// </summary>
    /// <param name="Name">
    /// What the file is called before its extension — the scope's name, or the
    /// document's when a loose document is its own artifact.
    /// </param>
    /// <param name="Names">
    /// Each document's name, in step with <paramref name="Documents"/>. A sheet
    /// tags its clips with these — <c>Scene.Name</c> defaults to "Scene 1" for
    /// every document, so without them three cycles arrive in an engine as three
    /// clips with the same name.
    /// </param>
    public sealed record PlannedArtifact(
        ExportArtifact Artifact, ExportPreset Preset, IReadOnlyList<Doc> Documents,
        IReadOnlyList<string> Names, string Name, string Path);

    /// <summary>
    /// Everything the selection's export would write, resolved and ready to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of <see cref="PlanExport"/>. That one answers <em>what
    /// artifacts are there</em> without touching disk, which is what the
    /// confirmation counts; this one loads the documents and works out the
    /// paths, which is what running needs. Kept apart so the count an artist
    /// agrees to is computed without reading forty files.
    /// </para>
    /// <para>
    /// <b>Empty artifacts are dropped here rather than earlier.</b> The plan
    /// names them on purpose — a scope where everything is still Draft is a
    /// legitimate state and the confirmation should say so — but writing a sheet
    /// with no frames in it is not a useful file.
    /// </para>
    /// <para>
    /// A document that cannot be read is skipped and named in
    /// <paramref name="missing"/>, because one unreadable drawing must not take
    /// the other thirty-nine with it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PlannedArtifact> ResolveExport(string folder, List<string> missing)
    {
        if (Project is not { } project) return [];

        var planned = new List<PlannedArtifact>();
        foreach (var artifact in PlanExport())
        {
            if (artifact.IsEmpty) continue;
            // An unscoped document carries an empty preset id — Q30's migration
            // path, and the state every old project is in — so it falls back to
            // the first built-in. Dropping it instead is what this did first,
            // and it made the common case export nothing at all and say nothing
            // about why.
            var preset = (string.IsNullOrEmpty(artifact.PresetId)
                ? ExportPreset.BuiltIns.FirstOrDefault()
                : PresetById(artifact.PresetId))
                ?? ExportPreset.BuiltIns.FirstOrDefault();
            if (preset is null) continue;

            var docs = new List<Doc>();
            var names = new List<string>();
            foreach (var reference in artifact.Documents)
            {
                if (ProjectIo.LoadDocument(project, reference) is { } doc)
                {
                    docs.Add(doc);
                    names.Add(reference.Name);
                }
                else missing.Add(reference.Name);
            }
            if (docs.Count == 0) continue;

            var name = artifact.Scope?.Name ?? artifact.Documents[0].Name;
            var slug = ProjectIo.Slug(name);
            // A PNG sequence writes into a folder rather than to a file, so its
            // "path" is a directory. Giving it a .png would make the sequence
            // exporter create a folder with an extension.
            var path = preset.Target == ExportTarget.PngSequence
                ? Path.Combine(folder, slug)
                : Path.Combine(folder, slug + ".png");
            planned.Add(new PlannedArtifact(artifact, preset, docs, names, name, path));
        }
        return planned;
    }

    public (DocumentRef Document, ExportPreset Preset, string Path)? PlanTestExport()
    {
        if (Project is not { } project) return null;
        if (Selected?.Animation is not { } document) return null;
        var id = ExportScopes.PresetFor(project.Manifest, document);
        var preset = id is null ? ExportPreset.BuiltIns.FirstOrDefault() : PresetById(id);
        if (preset is null || TestExportFolder is not { } folder) return null;

        // Per document whatever the preset says, because that is what a test is.
        var forTest = preset with { Grouping = ExportGrouping.PerDocument, IncludeStatuses = null };
        return (document, forTest, Path.Combine(folder, $"{ProjectIo.Slug(document.Name)}.png"));
    }

    // ---- scoped resources (Q30) ---------------------------------------------

    /// <summary>
    /// The palettes an artist can share onto the selected scope — the project's
    /// own, since those are the ones that outlive a single document.
    /// </summary>
    public IReadOnlyList<Palette> ShareablePalettes =>
        Project?.Palettes ?? (IReadOnlyList<Palette>)[];

    /// <summary>
    /// What the selected row already declares, for the panel to show and for an
    /// artist to take back.
    /// </summary>
    /// <remarks>
    /// Declarations on the row itself, not what it inherits. Showing the whole
    /// resolved chain here would make an inherited palette look removable from a
    /// place that never declared it.
    /// </remarks>
    public IReadOnlyList<ScopedResource> DeclarationsOnSelected =>
        ScopeOfSelected() is { } folder
            ? folder.Resources ?? (IReadOnlyList<ScopedResource>)[]
            : Project?.Manifest.Resources ?? (IReadOnlyList<ScopedResource>)[];

    /// <summary>
    /// Which scope the selection means: a folder row is itself, and anything
    /// else is the project.
    /// </summary>
    /// <remarks>
    /// The project row and a document row both resolve to the project rather
    /// than refusing. An artist who selected a drawing and asked to share a
    /// palette meant *around here*, and the nearest scope that can hold one is
    /// the answer — the alternative is a disabled menu item that says nothing.
    /// </remarks>
    // ---- what a folder is, in the artist's words (Q38, Q39, Q40) --------------

    /// <summary>
    /// The glyphs the picker offers, before the artist types their own.
    /// </summary>
    /// <remarks>
    /// <b>Q38.</b> A starting point, not a vocabulary. The closed-set option was
    /// rejected outright — it is a designation list wearing a different hat, and
    /// the first production need outside it is a dead end — so this exists only
    /// so the common case is one click and the feature is discoverable at all.
    /// An empty text box gives no hint that a folder can be labelled, and typing
    /// an emoji is a coin toss depending on the platform.
    /// <para>
    /// Curated, therefore opinionated, and that cost was accepted. Nothing reads
    /// these: an artist who types something else gets exactly the same treatment.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> GlyphChoices =
    [
        "🗀", "🎬", "🧍", "🐾", "🗡", "🏠", "🌳", "🚗",
        "✨", "💥", "🎨", "📐", "🖼", "📝", "🎞", "🔊",
        "🌊", "🔥", "☁", "🌙", "⭐", "🧩", "📦", "🔧",
    ];

    /// <summary>The selected folder's glyph, or the default when it has none.</summary>
    public string SelectedIcon =>
        Selected is { IsFolder: true, Folder: { Icon: { Length: > 0 } chosen } } ? chosen : "🗀";

    /// <summary>Give the selected folder a glyph, or clear it back to the default.</summary>
    /// <remarks>
    /// Clearing writes null rather than "🗀", so a folder the artist labelled and
    /// then unlabelled is byte-identical to one they never touched — the same
    /// rule the camera and the project type follow.
    /// </remarks>
    [RelayCommand]
    public void SetIcon(string? glyph)
    {
        if (Selected is not { IsFolder: true, Folder: { } folder }) return;
        var wanted = (glyph ?? "").Trim();
        var next = wanted.Length == 0 || wanted == "🗀" ? null : wanted;
        if (folder.Icon == next) return;

        folder.Icon = next;
        var keep = Selected.Key;
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Key == keep);
        OnPropertyChanged(nameof(SelectedIcon));
        Status = next is null
            ? $"“{folder.Name}” uses the plain folder glyph."
            : $"“{folder.Name}” is {next}.";
        _changed();
    }

    /// <summary>
    /// What the selected folder carries, in words, for the details panel.
    /// </summary>
    /// <remarks>
    /// <b>Q39.</b> Here rather than on every row: a tree of forty rows has to
    /// stay scannable, which is what the panel is for. The cost is recorded in
    /// the question — nothing tells you a folder holds a hand-corrected reading
    /// until you select it, and what keeps that from being a defect is
    /// <see cref="ProjectFolder.WhatClearingTheReadingDiscards"/>, which fires
    /// at the moment of the destructive act rather than in passing.
    /// <para>
    /// <b>Facets, not a kind.</b> The panel lists what is there and draws no
    /// conclusion from the combination — a folder with a reading and a pivot is
    /// a folder with a reading and a pivot, and whether that makes it a
    /// character is the artist's to say with its glyph.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> SelectedFacets
    {
        get
        {
            if (Project is not { } project) return [];
            if (Selected is not { IsFolder: true, Folder: { } folder }) return [];

            var carried = new List<string>();
            if (folder.Taxonomy is { } reading)
            {
                carried.Add(reading.Reviewed
                    ? $"reading — {reading.Kind}, {Count(reading.Parts.Count, "part")}, yours"
                    : $"reading — {reading.Kind}, {Count(reading.Parts.Count, "part")}");
            }
            if (folder.Pivot is not null) carried.Add("pivot");
            if (folder.Variants is { Count: > 0 } variants)
            {
                carried.Add(Count(variants.Count, "variant"));
            }
            if (folder.Order is { Count: > 0 } order)
            {
                carried.Add($"{Count(order.Count, "item")} arranged");
            }
            if (folder.Resources is { Count: > 0 } declared)
            {
                carried.Add($"{Count(declared.Count, "shared resource")}");
            }
            if (folder.Tags is { Count: > 0 } tags) carried.Add(Count(tags.Count, "tag"));

            var documents = ProjectFolders.DocumentsIn(project.Manifest, folder).Count;
            if (documents > 0) carried.Add(Count(documents, "document"));
            return carried;
        }
    }

    public bool HasFacets => SelectedFacets.Count > 0;

    private ProjectFolder? ScopeOfSelected() =>
        Selected is { IsFolder: true, Folder: { } folder } ? folder : null;

    /// <summary>Where a share would land, in words, for the menu to say so.</summary>
    public string ShareScopeLabel =>
        ScopeOfSelected() is { } folder ? folder.Name : Project?.Name ?? "the project";

    /// <summary>Share a palette with everything under the selected scope.</summary>
    /// <remarks>
    /// <b>Q30.</b> Declaring the first one is what switches a project from *every
    /// palette everywhere* to *scoped*, so this is the moment an old project
    /// becomes a new one — see <c>PaletteScopes.AnyDeclared</c>. Worth knowing
    /// because it is not reversible by deleting the declaration again: a project
    /// with zero declarations reads as unscoped, which is the same state it
    /// started in, so taking the last one back does restore the old behaviour.
    /// </remarks>
    /// <summary>
    /// The export presets an artist can put on the selected scope — the
    /// project's own, then the built-ins.
    /// </summary>
    public IReadOnlyList<ExportPreset> ShareableExportPresets =>
        Project is null
            ? []
            : [.. Project.Manifest.ExportPresets ?? [], .. ExportPreset.BuiltIns];

    /// <summary>Make this preset the one the selected scope exports with.</summary>
    /// <remarks>
    /// <b>The declaration is the artifact boundary</b>, so this is not only "use
    /// these settings" — it says *everything under here is one deliverable*.
    /// The status line says which, because a menu click that silently changes
    /// how many files a folder produces is the kind of thing that surprises
    /// somebody a week later.
    /// </remarks>
    [RelayCommand]
    private void SetExportPresetEntry(ExportPreset? preset)
    {
        if (Project is not { } project || preset is null) return;
        var scope = ScopeOfSelected();
        ExportScopes.SetPreset(project.Manifest, scope, preset.Id);
        var produces = preset.Grouping switch
        {
            ExportGrouping.OneArtifact => "as one file",
            ExportGrouping.PerChildFolder => "one file per folder inside it",
            _ => "one file per document",
        };
        AfterScopeChange(project, $"{ShareScopeLabel} exports {produces}.");
    }

    /// <summary>The menu binds this, because a menu item hands over the object.</summary>
    [RelayCommand]
    private void SharePaletteEntry(Palette? palette)
    {
        if (palette?.Id is { Length: > 0 } id) SharePalette(id);
    }

    public void SharePalette(string paletteId, bool projectWide = false)
    {
        if (Project is not { } project) return;
        var scope = ScopeOfSelected();
        if (Already(scope, PaletteScopes.Kind, paletteId)) return;
        ResourceScopes.Declare(
            project.Manifest, scope, PaletteScopes.Kind, paletteId,
            projectWide ? ResourceReach.Project : ResourceReach.Subtree);
        AfterScopeChange(project, $"Shared with {ShareScopeLabel}.");
    }

    /// <summary>Share a reference — a sheet, a document or an image.</summary>
    public void ShareReference(string id, string target, bool projectWide = false)
    {
        if (Project is not { } project) return;
        var scope = ScopeOfSelected();
        if (Already(scope, ReferenceScopes.Kind, id)) return;
        ReferenceScopes.Declare(
            project.Manifest, scope, id, target,
            projectWide ? ResourceReach.Project : ResourceReach.Subtree);
        AfterScopeChange(project, $"Shared with {ShareScopeLabel}.");
    }

    // ---- the other four kinds, and the two verbs every kind needs ------------
    //
    // Q30's declaration surface. The mechanism resolved six kinds and the menu
    // could declare two of them, which is the failure CLAUDE.md's "land the
    // places it shows up" table exists for: it works, nothing is red, and the
    // artist cannot reach it. Each of these is the palette pattern again — a
    // list to choose from, a command that declares the chosen one — so the
    // interesting part is where the two that are *not* shares sit.

    /// <summary>The gradients an artist can share onto the selected scope.</summary>
    public IReadOnlyList<Gradient> ShareableGradients =>
        Project is null ? [] : [.. Project.Gradients.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// The project's symbols, which a scope can narrow to.
    /// </summary>
    /// <remarks>
    /// The project's own rather than the global library's: a global symbol is
    /// the artist's, reachable from every project, and placing one adopts it
    /// into the project — where it becomes declarable like anything else. See
    /// <see cref="SymbolScopes.Kind"/> for why this one narrows where the others
    /// widen.
    /// </remarks>
    public IReadOnlyList<Symbol> ShareableSymbols =>
        Project is null
            ? []
            : [.. Project.Symbols.Values.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)];

    [RelayCommand]
    private void ShareSymbolEntry(Symbol? symbol)
    {
        if (Project is not { } project || symbol?.Id is not { Length: > 0 } id) return;
        var scope = ScopeOfSelected();
        if (Already(scope, SymbolScopes.Kind, id)) return;
        var first = !SymbolScopes.AnyDeclared(project.Manifest);
        ResourceScopes.Declare(project.Manifest, scope, SymbolScopes.Kind, id);
        // The first declaration is the one that changes what everything else
        // sees, because symbols were project-wide until it. Said out loud, since
        // the artist's next surprise would otherwise be a picker that lost most
        // of its contents with no obvious cause.
        AfterScopeChange(
            project,
            first
                ? $"{symbol.Name} shared with {ShareScopeLabel}. Symbols are now scoped: "
                  + "elsewhere in the project only what is declared there is offered."
                : $"{symbol.Name} shared with {ShareScopeLabel}.");
    }

    /// <summary>The project's own brush tips, which a scope can narrow to.</summary>
    /// <remarks>
    /// The project's, not the user library's or the built-in catalogue's: those
    /// two follow the artist rather than the project, and painting with either
    /// copies the raster into the document anyway. See <see cref="TipScopes"/>.
    /// </remarks>
    public IReadOnlyList<BrushTip> ShareableTips =>
        Project?.Manifest.Tips is not { } tips
            ? []
            : [.. tips.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];

    [RelayCommand]
    private void ShareTipEntry(BrushTip? tip)
    {
        if (Project is not { } project || tip?.Id is not { Length: > 0 } id) return;
        var scope = ScopeOfSelected();
        if (Already(scope, TipScopes.Kind, id)) return;
        var first = !TipScopes.AnyDeclared(project.Manifest);
        ResourceScopes.Declare(project.Manifest, scope, TipScopes.Kind, id);
        // Same announcement symbols make, for the same reason: this is the click
        // that turns "every tip everywhere" into "only what is declared", and a
        // picker that quietly shrinks is a bad way to learn that.
        AfterScopeChange(
            project,
            first
                ? $"{tip.Name} shared with {ShareScopeLabel}. Tips are now scoped: "
                  + "elsewhere only what is declared there is offered."
                : $"{tip.Name} shared with {ShareScopeLabel}.");
    }

    /// <summary>The guide sets an artist can share onto the selected scope.</summary>
    public IReadOnlyList<GuideSet> ShareableGuideSets =>
        Project?.Manifest.GuideSets ?? (IReadOnlyList<GuideSet>)[];

    /// <summary>The documents marked as templates, for a scope to start from.</summary>
    /// <remarks>
    /// Reads each document to find the flag, which is why this is a method's
    /// worth of work behind a property. <see cref="Templates.InProject"/> says
    /// why the flag lives in the document rather than in the manifest, and the
    /// loads are cached.
    /// </remarks>
    public IReadOnlyList<DocumentRef> ShareableTemplates =>
        Project is null ? [] : Templates.InProject(Project);

    /// <summary>The menu binds these, because a menu item hands over the object.</summary>
    [RelayCommand]
    private void ShareGradientEntry(Gradient? gradient)
    {
        if (Project is not { } project || gradient?.Id is not { Length: > 0 } id) return;
        var scope = ScopeOfSelected();
        if (Already(scope, GradientScopes.Kind, id)) return;
        ResourceScopes.Declare(project.Manifest, scope, GradientScopes.Kind, id);
        AfterScopeChange(project, $"{gradient.Name} shared with {ShareScopeLabel}.");
    }

    [RelayCommand]
    private void ShareGuideSetEntry(GuideSet? guides)
    {
        if (Project is not { } project || guides?.Id is not { Length: > 0 } id) return;
        var scope = ScopeOfSelected();
        if (Already(scope, GuideScopes.Kind, id)) return;
        ResourceScopes.Declare(project.Manifest, scope, GuideScopes.Kind, id);
        AfterScopeChange(project, $"{guides.Name} shared with {ShareScopeLabel}.");
    }

    /// <summary>Say what a new document made in this scope starts from.</summary>
    /// <remarks>
    /// Not a share, which is why it is its own menu entry rather than a fifth
    /// item under one: a scope has <em>one</em> default, so this replaces where
    /// the others accumulate. Same shape as the export preset above, and for the
    /// same reason — two declarations of a one-at-a-time kind on one folder make
    /// which-one-wins depend on insertion order.
    /// </remarks>
    [RelayCommand]
    private void SetDefaultTemplateEntry(DocumentRef? template)
    {
        if (Project is not { } project || template?.Id is not { Length: > 0 } id) return;
        TemplateScopes.SetDefault(project.Manifest, ScopeOfSelected(), id);
        AfterScopeChange(project, $"New documents in {ShareScopeLabel} start from {template.Name}.");
    }

    /// <summary>
    /// Whether the selected row is a drawing that could be reference for others.
    /// </summary>
    public bool CanShareSelectedAsReference => Selected is { IsHeading: false, Animation: not null };

    /// <summary>
    /// Share the selected <em>document</em> as reference, here or project-wide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gesture is inverted, and deliberately.</b> Every other kind picks
    /// the resource from a list hung off the folder it lands on. References
    /// cannot: workflow 3's reference is an ordinary document, so that list
    /// would be "every drawing in the project" — hundreds of entries in a
    /// context menu, and the one you want is the row you already right-clicked.
    /// So you pick the drawing and say how far it reaches.
    /// </para>
    /// <para>
    /// <b>Which is also where publishing finally has a gesture.</b>
    /// <see cref="ResourceReach.Project"/> existed in the record and in the
    /// resolver and nowhere an artist could ask for it, which made workflows 3
    /// and 4 — the environment layout everything draws against, the sword in the
    /// asset library — the two the design was written for and the two that could
    /// not be performed.
    /// </para>
    /// <para>
    /// It lands on the document's <em>own</em> folder rather than on the
    /// selection, because the selection is the document. Subtree from there
    /// means "everything filed alongside this", which is what an artist sharing
    /// a layout with its neighbours means.
    /// </para>
    /// </remarks>
    public void ShareSelectedAsReference(bool projectWide)
    {
        if (Project is not { } project || Selected?.Animation is not { } document) return;
        var scope = ProjectFolders.ById(project.Manifest, document.FolderId);
        if (Already(scope, ReferenceScopes.Kind, document.Id)) return;
        ReferenceScopes.Declare(
            project.Manifest, scope, document.Id, ReferenceTargets.Document,
            projectWide ? ResourceReach.Project : ResourceReach.Subtree);
        AfterScopeChange(
            project,
            projectWide
                ? $"{document.Name} is reference for the whole project."
                : $"{document.Name} is reference for {scope?.Name ?? project.Name}.");
    }

    /// <summary>What the selected row declares, in words, with a verb for each.</summary>
    /// <remarks>
    /// <b>The half that makes the rest safe.</b> Declaring is one click and it
    /// switches a project from *every resource everywhere* to *scoped*; without
    /// somewhere to see what a folder declares, the only way to find out was to
    /// open a drawing and notice the picker had changed.
    /// </remarks>
    public IReadOnlyList<DeclarationRow> Declarations =>
        [.. DeclarationsOnSelected.Select(r => new DeclarationRow(r, LabelFor(r)))];

    /// <summary>So the menu can hide an entry that would open onto nothing.</summary>
    public bool HasDeclarations => DeclarationsOnSelected.Count > 0;

    // ---- what the submenus actually bind to ---------------------------------
    //
    // One shape for all seven, so a new kind is a line rather than a block of
    // XAML, and so the wiring is assertable without a window. See
    // ScopeMenuEntry for why the command travels with the entry.

    private static IReadOnlyList<ScopeMenuEntry> Entries<T>(
        IEnumerable<T> source, Func<T, string> label, ICommand command) =>
        [.. source.Select(item => new ScopeMenuEntry(label(item), command, item))];

    public IReadOnlyList<ScopeMenuEntry> PaletteMenu =>
        Entries(ShareablePalettes, p => p.Name, SharePaletteEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> GradientMenu =>
        Entries(ShareableGradients, g => g.Name, ShareGradientEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> SymbolMenu =>
        Entries(ShareableSymbols, s => s.Name, ShareSymbolEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> TipMenu =>
        Entries(ShareableTips, t => t.Name, ShareTipEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> GuideSetMenu =>
        Entries(ShareableGuideSets, g => g.Name, ShareGuideSetEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> TemplateMenu =>
        Entries(ShareableTemplates, t => t.Name, SetDefaultTemplateEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> ExportPresetMenu =>
        Entries(ShareableExportPresets, p => p.Name, SetExportPresetEntryCommand);

    /// <summary>The glyph picker, as menu entries. Q38.</summary>
    /// <remarks>
    /// The plain folder is first and clears the choice rather than setting one,
    /// so "put it back" is in the same place as "change it". The free-entry half
    /// is a separate menu item, because a text box inside a flyout item is a
    /// control nobody finds.
    /// </remarks>
    public IReadOnlyList<ScopeMenuEntry> GlyphMenu =>
        [.. GlyphChoices.Select(g => new ScopeMenuEntry(g, SetIconCommand, g))];

    public IReadOnlyList<ScopeMenuEntry> UnshareMenu =>
        Entries(Declarations, d => d.Label, UnshareEntryCommand);

    public IReadOnlyList<ScopeMenuEntry> ReachMenu =>
        Entries(Declarations, d => d.LabelWithReach, PublishEntryCommand);

    /// <summary>A declaration's name rather than its id, which says nothing.</summary>
    private string LabelFor(ScopedResource resource)
    {
        var name = resource.Kind switch
        {
            PaletteScopes.Kind => Project?.Palettes.FirstOrDefault(p => p.Id == resource.Id)?.Name,
            GradientScopes.Kind => Project?.Gradients.GetValueOrDefault(resource.Id)?.Name,
            GuideScopes.Kind => Project?.Manifest.GuideSets?.FirstOrDefault(g => g.Id == resource.Id)?.Name,
            SymbolScopes.Kind => Project?.Symbols.GetValueOrDefault(resource.Id)?.Name,
            TipScopes.Kind => Project?.Manifest.Tips?.FirstOrDefault(t => t.Id == resource.Id)?.Name,
            ExportScopes.Kind => ShareableExportPresets.FirstOrDefault(p => p.Id == resource.Id)?.Name,
            _ => DocumentByIdOrNull(resource.Id)?.Name,
        };
        var kind = resource.Kind switch
        {
            PaletteScopes.Kind => "Palette",
            GradientScopes.Kind => "Gradient",
            GuideScopes.Kind => "Guides",
            SymbolScopes.Kind => "Symbol",
            TipScopes.Kind => "Brush tip",
            TemplateScopes.Kind => "Template",
            ExportScopes.Kind => "Export",
            ReferenceScopes.Kind => "Reference",
            _ => resource.Kind,
        };
        // The id when the name cannot be found: a declaration pointing at
        // something deleted must still be visible and removable, or the artist
        // is left with a resolver failure and no way to clear its cause.
        return $"{kind}: {name ?? resource.Id}";
    }

    private DocumentRef? DocumentByIdOrNull(string id) =>
        Project?.AllDocuments.FirstOrDefault(d => d.Id == id);

    [RelayCommand]
    private void UnshareEntry(DeclarationRow? row)
    {
        if (row is not null) UnshareDeclaration(row.Resource);
    }

    [RelayCommand]
    private void PublishEntry(DeclarationRow? row)
    {
        if (row is null || Project is not { } project) return;
        if (row.IsPublished) ResourceScopes.Demote(row.Resource);
        else ResourceScopes.Promote(row.Resource);
        AfterScopeChange(
            project,
            row.Resource.ReachOrDefault == ResourceReach.Project
                ? $"{row.Label} reaches the whole project."
                : $"{row.Label} reaches {ShareScopeLabel} again.");
    }

    /// <summary>Let everything in the project see this declaration, wherever it is filed.</summary>
    public void PromoteDeclaration(ScopedResource resource)
    {
        if (Project is not { } project) return;
        ResourceScopes.Promote(resource);
        AfterScopeChange(project, "Shared with the whole project.");
    }

    /// <summary>Take a declaration back off this scope.</summary>
    public void UnshareDeclaration(ScopedResource resource)
    {
        if (Project is not { } project) return;
        if (!ResourceScopes.Withdraw(project.Manifest, ScopeOfSelected(), resource)) return;
        AfterScopeChange(project, $"No longer shared with {ShareScopeLabel}.");
    }

    private bool Already(ProjectFolder? scope, string kind, string id) =>
        (scope is null ? Project?.Manifest.Resources : scope.Resources)?
            .Any(r => r.Kind == kind && r.Id == id) ?? false;

    private void AfterScopeChange(Project project, string status)
    {
        Status = status;
        OnPropertyChanged(nameof(DeclarationsOnSelected));
        // The list the menu reads is derived from the one above, so it has to be
        // raised too — a declaration that lands and does not appear under
        // "Shared here" reads as the click having done nothing.
        OnPropertyChanged(nameof(Declarations));
        OnPropertyChanged(nameof(HasDeclarations));
        // The manifest changed, so the project is unsaved — and the resolver
        // that decides which palettes a document paints from has to be re-run,
        // which is what _changed does.
        _changed();
    }

    /// <summary>Note that a document changed, so the next save writes it and only it.</summary>
    public void MarkDirty(DocumentRef reference) => _dirty.Add(reference.Id);

    /// <summary>
    /// Note that the manifest itself changed, from somewhere other than this
    /// docker — the project is unsaved and everything derived from the manifest
    /// has to be re-resolved.
    /// </summary>
    /// <remarks>
    /// The docker's own edits go through <c>AfterScopeChange</c>, which also
    /// raises the declaration properties. This is the plain half for a caller
    /// that changed something no list on this panel is showing — a character's
    /// subject reading, for one.
    /// </remarks>
    public void MarkManifestChanged() => _changed();

    /// <summary>
    /// Everything is written. Clears the dirty set — and arms the directory
    /// watch, which is the first moment it can be armed.
    /// </summary>
    /// <remarks>
    /// <b>B61, and it was a test that found this.</b> <c>Adopt</c> looks like the
    /// only place a watch is needed, and for <c>OpenProject</c> it is. But
    /// <c>ProjectIo.Create</c> builds a <c>Project</c> without creating its folder,
    /// and <c>NewProject</c> adopts before it saves — so at <c>Adopt</c> time a new
    /// project's root <em>does not exist yet</em> and there is nothing to watch. A
    /// freshly created project would have gone unwatched for its whole session,
    /// which is the same class of miss as the one B61 already is: a fix that works
    /// on the path somebody tested and not on the one they use.
    /// <para>
    /// This is the right funnel rather than a convenient one: an empty dirty set
    /// means every document is on disk, so it is exactly the condition under which
    /// the folder is there to be watched. <c>Watch</c> is idempotent on the same
    /// root, so calling it after every save costs a string comparison.
    /// </para>
    /// </remarks>
    public void MarkAllSaved()
    {
        _dirty.Clear();
        // B76. Everything pending has just been written, so nothing is.
        _unwrittenFolders.Clear();
        // And the rows have to be told. Clearing the sets changes the *answer*
        // to "is this on disk" without changing any row, so without this a saved
        // document went on saying "not saved yet" until something else forced a
        // rebuild — which is B79's shape exactly, a badge that outlives its
        // reason, and the pair of tests here caught it on the first run.
        MarkMissing();
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(HasMissing));
        Watcher.Watch(Project?.Root);
    }

    partial void OnProjectChanged(Project? value)
    {
        Rebuild();
        OnPropertyChanged(nameof(CanReorder));
        // B61. Follows the project rather than the application: closing a project
        // stops the watch, and null — the ordinary state — watches nothing at
        // all. A document-first app that never opens a project must not hold an
        // OS handle for a folder it does not have.
        Watcher.Watch(value?.Root);
    }

    private void Rebuild()
    {
        var keep = Selected?.Key;

        // B61. A row that stands for the same thing is kept rather than replaced,
        // because a re-read now happens whenever the folder changes — including on
        // every save — and the docker's interactions live on the row instance. See
        // ProjectRow.Describes for what "the same thing" means and what it cost to
        // find out.
        var previous = new List<ProjectRow>(Rows);
        Rows.Clear();

        void Add(ProjectRow fresh)
        {
            // Every row goes through here, which is why the back-reference is
            // set here rather than in six constructors that would each have to
            // remember. A kept row gets it too: it may predate this line.
            fresh.Owner = this;
            if (previous.FirstOrDefault(p => p.Describes(fresh)) is { } kept)
            {
                kept.Owner = this;
                kept.Name = fresh.Name;
                kept.Status = fresh.Status;
                kept.Duration = fresh.Duration;
                // B86, and the same trap as the three above: a kept row keeps
                // its own state, so anything the rebuild decided has to be
                // copied over or the reuse silently discards it. Collapse is
                // decided by `_collapsed`, and without this line a folder
                // toggled shut stayed open on screen while the tree below it
                // correctly vanished.
                kept.IsCollapsed = fresh.IsCollapsed;
                Rows.Add(kept);
                return;
            }
            Rows.Add(fresh);
        }

        // B62. The project itself, above everything in it. Present whenever a
        // project is — it is the one row that cannot be absent, because it is
        // the thing the panel is showing.
        if (Project is { } withRoot) Add(ProjectRow.Root(RootLabel(withRoot)));
        // B86. The folder tree first: it is the structure the artist built, and
        // a production is organised by it rather than by the two fixed axes
        // below. Absent entirely until the first folder is made, so a project
        // that never used one looks exactly as it did.
        if (Project is { } withFolders)
        {
            EmitFolders(withFolders.Manifest, parent: null, depth: 0, Add);
        }
        // B114. There are no character and scene passes any more — both were a
        // second and third container walked after the tree, and everything they
        // held is in the tree now.
        //
        // Project-level documents last, unindented: they belong to the project,
        // not under anything. Only the ones filed nowhere; the rest were emitted
        // above, under the folder they are in.
        foreach (var document in Project?.Manifest.Documents ?? [])
        {
            if (document.FolderId is not null) continue;
            Add(new ProjectRow(document, ShotTime(document)) { Status = document.Status });
        }
        MarkMissing();
        Selected = Rows.FirstOrDefault(r => r.Key == keep);
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(TotalRunningTime));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(HasMissing));
    }

    /// <summary>
    /// Re-read the project against what is on disk now.
    /// </summary>
    /// <remarks>
    /// <b>B61.</b> The docker was built once from the manifest and then held, so
    /// it went on listing folders and documents that had been deleted from disk
    /// and stayed silent about it until the application was restarted. This is
    /// the seam a directory watcher drives; it is public and parameterless on
    /// purpose so the watcher, a manual refresh and a test all use the same one
    /// path rather than three that can disagree.
    /// </remarks>
    public void Refresh() => Rebuild();

    /// <summary>
    /// What the project row is called: the folder on disk, not the manifest name.
    /// </summary>
    /// <remarks>
    /// <b>B62.</b> The docker's title already shows the project's name, so
    /// repeating it in the first row would say nothing. The reported gap was
    /// "no way to see where the project actually lives", and
    /// <c>Production.lbproj</c> answers that where <i>Production</i> does not.
    /// The full path is a right-click away on Copy path.
    /// </remarks>
    private static string RootLabel(Project project) =>
        string.IsNullOrEmpty(project.Root) ? "Project" : Path.GetFileName(project.Root.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    // ---- the folder tree (B85, B86) -------------------------------------------

    /// <summary>
    /// Which folders are collapsed, by id.
    /// </summary>
    /// <remarks>
    /// <b>B86.</b> Here rather than on the row, because rows are rebuilt — by a
    /// save, by the directory watch, by any edit — and collapse that lived on
    /// them would spring open every time the disk moved. Ids rather than folder
    /// objects for the same reason: a reload replaces the objects and keeps the
    /// ids.
    /// </remarks>
    private readonly HashSet<string> _collapsed = [];

    /// <summary>
    /// Walk the tree in display order: a folder, then what is inside it.
    /// </summary>
    /// <remarks>
    /// Recursive, and safe to be: <see cref="ProjectFolders.Move"/> refuses to
    /// make a cycle and <c>Descendants</c> tolerates one, but this walks
    /// <em>children</em> from the root, so a cycle is simply never reached from
    /// here — an orphaned loop does not render, which is the honest outcome for
    /// a folder with no path back to the project.
    /// </remarks>
    private void EmitFolders(
        ProjectManifest manifest, ProjectFolder? parent, int depth, Action<ProjectRow> add)
    {
        // B114. In the artist's order rather than alphabetically — a scene's
        // shots play in the order they were arranged, and so do a character's
        // animations once somebody arranges them. The partial ordering means an
        // unarranged folder still reads exactly as it did, by name.
        foreach (var folder in ProjectFolders.ChildrenInOrder(manifest, parent))
        {
            var collapsed = _collapsed.Contains(folder.Id);
            add(new ProjectRow(folder, depth, RunningTime(manifest, folder))
            {
                IsCollapsed = collapsed,
            });
            if (collapsed) continue;
            EmitFolders(manifest, folder, depth + 1, add);
            foreach (var document in ProjectFolders.InOrder(manifest, folder))
            {
                add(new ProjectRow(folder, document, depth, ShotTime(document))
                {
                    Status = document.Status,
                });
            }
        }
    }

    /// <summary>Show or hide what is inside a folder, and select it.</summary>
    /// <remarks>
    /// <para>
    /// <b>B108, and it refines B86 rather than reversing it.</b> B86 put the
    /// twisty on a Button because "collapsing and selecting are different
    /// intentions and one gesture cannot mean both", and that is still true of
    /// the <em>intention</em>. What it missed is the consequence: the chevron was
    /// then the one gesture that reaches into a folder and leaves
    /// <see cref="Selected"/> pointing somewhere else — or at nothing.
    /// </para>
    /// <para>
    /// Two toolbar surfaces read that selection as <em>where I am</em>: 🗁
    /// reveals <see cref="SelectedPath"/> and ＋ New files into
    /// <see cref="TargetFolder"/>. So expanding Knight and pressing either one
    /// acted on the project root — the file manager opened the project folder,
    /// and a new document landed in <c>unassigned-documents/</c>. Both were
    /// reported as separate bugs and they are this one line.
    /// </para>
    /// <para>
    /// It stayed hidden until B104 made the tree legible. With every row drawn
    /// flush left nobody used the chevron; with real indentation it is the first
    /// thing an artist reaches for.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void ToggleCollapsed(ProjectRow? row)
    {
        if (row?.Folder is not { } folder || !row.IsFolder) return;
        if (!_collapsed.Add(folder.Id)) _collapsed.Remove(folder.Id);
        Rebuild();
        // After the rebuild, because Rebuild restores the selection from the
        // *previous* one and would otherwise overwrite this.
        Selected = Rows.FirstOrDefault(r => r.IsFolder && ReferenceEquals(r.Folder, folder))
            ?? Selected;
    }

    /// <summary>
    /// A press landed on a row: that row becomes the selection.
    /// </summary>
    /// <remarks>
    /// <b>B108.</b> The window's pointer handler used to set the selection for a
    /// right-click on any row and for a left-click on a <em>document</em> only,
    /// leaving every other row kind to whatever the ListBox happened to do. That
    /// is the asymmetry the report named — "RMB show in file manager does work
    /// correctly" — and the fix is one rule for both buttons and every kind of
    /// row.
    /// <para>
    /// Here rather than in the window so it can be tested: synthetic pointer
    /// input through Xvfb is unreliable in this environment, so a selection rule
    /// that lives only in a pointer handler is a rule nothing can check.
    /// </para>
    /// </remarks>
    public void SelectFromPointer(ProjectRow? row)
    {
        if (row is null) return;
        Selected = row;
    }

    /// <summary>Whether a folder is currently collapsed. For tests and bindings.</summary>
    public bool IsCollapsed(ProjectFolder folder) => _collapsed.Contains(folder.Id);

    /// <summary>
    /// The folder a new thing should go into, given what is selected.
    /// </summary>
    /// <remarks>
    /// <b>B85.</b> The reported defect exactly: creating a document ignored
    /// where you were and dropped it in a top-level <c>documents/</c>. A
    /// selected folder is the obvious answer, and so is the folder a selected
    /// <em>document</em> sits in — "new document" next to a document means
    /// beside it, not somewhere else.
    /// </remarks>
    public ProjectFolder? TargetFolder => Selected?.Folder;

    /// <summary>Make a folder, inside the selected one if there is one.</summary>
    [RelayCommand]
    private void AddFolder(string? name = null)
    {
        if (Project is not { } project) return;
        var folder = ProjectFolders.Add(project.Manifest, Named(name, "Folder"), TargetFolder);
        // B76. In the manifest now, on disk at the next save — so the row can say
        // "not saved yet" rather than "not on disk", which would be a lie that
        // reads as a fault.
        _unwrittenFolders.Add(folder.Id);
        // Opened, so the thing just made is not hidden by its parent's state.
        _collapsed.Remove(folder.Id);
        if (TargetFolder is { } parent) _collapsed.Remove(parent.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => ReferenceEquals(r.Folder, folder) && r.IsFolder);
        _changed();
    }

    /// <summary>
    /// Move a row into a folder, or to the project root when null.
    /// </summary>
    /// <remarks>
    /// <b>B86.</b> One entry point for the drop, because a tree view offers one
    /// gesture and the model has two operations behind it — a folder reparents,
    /// a document is refiled and repathed. Returning false rather than throwing
    /// is what lets a drop that cannot happen simply not happen: dragging a
    /// folder onto its own child is an ordinary slip, not an error to report.
    /// </remarks>
    public bool MoveInto(ProjectRow? row, ProjectFolder? destination)
    {
        if (Project is not { } project || row is null) return false;

        if (row is { IsFolder: true, Folder: { } folder })
        {
            if (!ProjectFolders.Move(project.Manifest, folder, destination)) return false;
        }
        else if (row.Animation is { } document)
        {
            // B106. Where the file has to end up, worked out before anything
            // moves. PathFor reads the manifest without changing it and gives
            // the same answer after the refile as before it — it dedupes against
            // the documents already in the destination and excludes this one —
            // so the disk can be moved first and the manifest only if it worked.
            var to = ProjectFolders.PathFor(project.Manifest, document, destination);
            if (!ProjectIo.MoveInProject(project, document.Path, to))
            {
                Status = $"Could not move “{document.Name}” on disk. It is still where it was.";
                return false;
            }
            // B114. There used to be a step here that took the document out of
            // its character or scene first, "or it would be in two places". There
            // is one place now, so filing it is the whole move.
            if (!ProjectFolders.FileDocument(project.Manifest, document, destination)) return false;
            _dirty.Add(document.Id);
        }
        else
        {
            return false;
        }

        Rebuild();
        _changed();
        return true;
    }

    /// <summary>
    /// Re-read on purpose — the F5 the artist presses, and the toolbar button.
    /// </summary>
    /// <remarks>
    /// <b>The fallback the directory watch needs to have.</b>
    /// <c>Services.ProjectWatcher.Watch</c> swallows an <c>IOException</c> so a
    /// project on a network share still opens rather than refusing to; without a
    /// manual path that swallow would leave an artist with B61 and no way out at
    /// all, which is a worse bug wearing better manners. Reports what it did,
    /// because a refresh that finds nothing wrong is otherwise indistinguishable
    /// from a button that does nothing.
    /// </remarks>
    [RelayCommand]
    private void RefreshFromDisk()
    {
        if (Project is null) return;
        Refresh();
        Status = MissingCount switch
        {
            0 => "Re-read from disk — everything is where the project says.",
            1 => "Re-read from disk — 1 item is not on disk.",
            var n => $"Re-read from disk — {n} items are not on disk.",
        };
    }

    /// <summary>
    /// Set <see cref="ProjectRow.Missing"/> against the filesystem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>Exists</c> per row and no directory enumeration: the manifest
    /// already says what should be there, so this asks about exactly those paths
    /// rather than walking the tree. That keeps a refresh proportional to the
    /// project's size rather than to the disk's.
    /// </para>
    /// <para>
    /// <b>A row with unsaved changes is never marked, and that distinction is the
    /// whole difference between a useful flag and a noisy one.</b> "Not written
    /// yet" and "was written and is gone" both fail a <c>File.Exists</c> and mean
    /// opposite things: the first is every new animation between creating it and
    /// saving, which is most of a working session. <see cref="Dirty"/> already
    /// separates them — it is the set the project saves — so a dirty row is
    /// skipped and only a clean row whose file has disappeared is reported.
    /// </para>
    /// <para>
    /// The first cut of this checked whether the project root existed and assumed
    /// that meant "saved". It does not: <c>NewProject</c> creates the root
    /// immediately, so the guard never fired and every freshly added animation
    /// reported itself missing. The test that caught it is
    /// <c>AnUnsavedProjectDoesNotReportEveryRowAsMissing</c>.
    /// </para>
    /// </remarks>
    private void MarkMissing()
    {
        if (Project is not { } project || string.IsNullOrEmpty(project.Root)) return;
        if (!Directory.Exists(project.Root)) return;

        foreach (var row in Rows)
        {
            var path = PathOf(row);
            var addressable = path is not null
                && !string.Equals(path, project.Root, StringComparison.Ordinal);

            if (!addressable || File.Exists(path!) || Directory.Exists(path!))
            {
                row.Missing = false;
                row.Pending = false;
                continue;
            }

            // Not on disk, and B76 is about which of the two reasons that is.
            //
            // The test is sound because we already know the file is absent: a
            // document that was written and then edited is in `_dirty` *and* on
            // disk, so it took the branch above. Inside this one, "dirty" can
            // only mean it was never written at all. Same for a folder — the
            // manifest holds it and `ProjectIo.Save` materialises it (B83), so
            // an unsaved one has no directory yet.
            var neverWritten = row.Animation is { } animation
                ? _dirty.Contains(animation.Id)
                : row is { IsFolder: true, Folder: { } folder } && _unwrittenFolders.Contains(folder.Id);

            row.Pending = neverWritten;
            row.Missing = !neverWritten;
        }
    }

    /// <summary>
    /// Folders made since the last save, so a folder with no directory can say
    /// which kind of absent it is.
    /// </summary>
    /// <remarks>
    /// <b>B76.</b> Separate from <see cref="_dirty"/> rather than folded into it
    /// because that set is handed to <c>ProjectIo.Save</c> to decide which
    /// <em>documents</em> to write. Ids that match no document would be inert
    /// today and are exactly the kind of inert that stops being inert.
    /// </remarks>
    private readonly HashSet<string> _unwrittenFolders = [];

    /// <summary>How many rows name something that is not on disk.</summary>
    public int MissingCount => Rows.Count(r => r.Missing);

    public bool HasMissing => MissingCount > 0;

    /// <summary>
    /// How long everything in a folder runs, or null when it holds nothing.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Every folder gets one, not only a scene. A character's
    /// animations add up to a real number too, and the old model had nowhere to
    /// put it — which is the shape of most of this bug.
    /// </remarks>
    private static string? RunningTime(ProjectManifest manifest, ProjectFolder folder)
    {
        var (frames, seconds) = ProjectIo.FolderDuration(manifest, folder);
        if (frames == 0) return null;
        return seconds is { } s ? $"{Clock(s)} · {frames}f" : $"{frames}f";
    }

    private static string? ShotTime(DocumentRef shot) =>
        shot.Seconds is { } s ? $"{Clock(s)} · {shot.Frames}f"
        : shot.Frames > 0 ? $"{shot.Frames}f"
        : null;

    /// <summary>
    /// Seconds as m:ss.t — the shape a shot list is read in.
    /// </summary>
    /// <remarks>
    /// A tenth of a second is shown because shots are short: "0:02" and
    /// "0:02.4" are the same row to a scheduler and very different to an
    /// editor.
    /// </remarks>
    private static string Clock(double seconds) =>
        $"{(int)(seconds / 60)}:{seconds % 60:00.0}";

    // ---- commands -----------------------------------------------------------

    /// <summary>
    /// Create a document in the selected folder and open it.
    ///
    /// This is where new work in a project comes from. <c>File → New</c>
    /// deliberately still means "a new standalone document" — the most common
    /// action in the app must not change meaning based on which row happens to
    /// be selected.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> This was three commands — <c>AddAnimation</c> under a
    /// character, <c>AddShot</c> under a scene, <c>AddLooseDocument</c> under
    /// nothing — appending to three different lists, and only the third one's
    /// list was read by export planning and scoped resources. One command,
    /// filing into <see cref="TargetFolder"/>, is the whole fix at this layer.
    /// </remarks>
    [RelayCommand]
    private void AddDocument(string? name = null)
    {
        if (Project is not { } project) return;
        var folder = TargetFolder;

        var doc = _newDocument();
        var reference = ProjectIo.AddDocument(
            project, Named(name, DefaultDocumentName(project, folder)), doc, folder);
        _dirty.Add(reference.Id);
        if (folder is not null) _collapsed.Remove(folder.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        _open(reference, doc);
        _changed();
    }

    private static string DefaultDocumentName(Project project, ProjectFolder? folder) =>
        folder is null
            ? $"Document {project.Manifest.Documents.Count + 1}"
            : $"{folder.Name} {ProjectFolders.DocumentsIn(project.Manifest, folder).Count + 1}";

    /// <summary>
    /// What the ＋ button offers. Each one lands somewhere specific, which is
    /// the point: creating work inside the project should not be followed by
    /// a second step that files it.
    /// </summary>
    /// <param name="IsContainer">
    /// Whether this makes something other things go <em>inside</em>, rather
    /// than a drawing.
    /// </param>
    /// <remarks>
    /// <b>B63.</b> The second half of the report: the menu "does not distinguish
    /// a folder from a work file, so it reads as an undifferentiated pile". This
    /// is what the grouping is derived from, so the menu cannot disagree with
    /// what the entries actually do.
    /// </remarks>
    public sealed record NewItemKind(string Label, string Hint, bool IsContainer = false)
    {
        public override string ToString() => Label;

        /// <summary>The same glyph the row gets, so the menu predicts the tree.</summary>
        public string Glyph => IsContainer ? "🗀" : "▣";
    }

    /// <summary>B86. A folder of the artist's own, at any depth.</summary>
    public static readonly NewItemKind NewFolderItem =
        new("Folder", "A folder you name, inside the selected one", IsContainer: true);

    /// <summary>A drawing, filed wherever the selection is.</summary>
    public static readonly NewItemKind NewDocumentItem =
        new("Document", "A drawing, in the selected folder");

    /// <summary>
    /// Two entries, since B114.
    /// </summary>
    /// <remarks>
    /// There were six — Folder, Character, Scene, Animation, Shot, Document —
    /// and four of them made the same two things under different names, filed
    /// into three lists of which only one was wired up. A character is a folder
    /// you read; a scene is a folder you arrange; an animation, a shot and a
    /// loose document are one drawing filed in one place or none.
    ///
    /// <b>Nothing became unreachable.</b> Making a character is now "make a
    /// folder, put the work in it, read it" — the reading is the thing that
    /// makes it one, and there was never an honest way to declare a character
    /// before there was anything to read.
    /// </remarks>
    public IReadOnlyList<NewItemKind> NewItemKinds { get; } =
        [
            // B63. The container first and the drawing after, because the menu
            // should read as "where does it go" then "what is it".
            NewFolderItem, NewDocumentItem,
        ];

    /// <summary>Create one of <see cref="NewItemKinds"/> in the right place.</summary>
    [RelayCommand]
    public void AddItem(NewItemKind? kind) => AddItemNamed(kind, null);

    /// <summary>
    /// Ask the artist what to call it. Null means they cancelled.
    /// </summary>
    /// <remarks>
    /// <b>B65.</b> Supplied by the window, because a view model that opens its
    /// own dialogs is one no test can drive — and until this existed the
    /// ask-then-create sequence lived in <c>MainWindow</c>, which left the
    /// cancel path untestable. B65's own entry says so: "an entry that prompts
    /// and then creates nothing would pass both halves, and only a person would
    /// see it."
    ///
    /// Null when nothing is attached, and <see cref="CreateAsync"/> falls back
    /// to the suggestion — the docker is built long before the window wires
    /// anything to it, and that must not be the difference between working and
    /// throwing.
    /// </remarks>
    public Func<NewItemKind, string, Task<string?>>? AskName { get; set; }

    /// <summary>
    /// Ask the artist to type a glyph. Null means they cancelled.
    /// </summary>
    /// <remarks>
    /// <b>Q38's free-entry half.</b> Supplied by the window for the same reason
    /// <see cref="AskName"/> is: a view model that opens its own dialogs is one
    /// no test can drive, and the cancel path is the half that goes untested
    /// when the sequence lives in the window.
    /// </remarks>
    public Func<string, Task<string?>>? AskGlyph { get; set; }

    /// <summary>Ask for a glyph, then set it — or set nothing if cancelled.</summary>
    [RelayCommand]
    public async Task ChooseIconAsync()
    {
        if (Selected is not { IsFolder: true }) return;
        if (AskGlyph is null) return;
        if (await AskGlyph(SelectedIcon) is not { } typed) return;   // cancelled
        SetIcon(typed);
    }

    /// <summary>Ask for a name, then create — or create nothing if cancelled.</summary>
    /// <remarks>
    /// The ordering is the whole of B65: a name asked for <em>after</em> the
    /// file exists is a rename, which is B64.
    /// </remarks>
    public async Task CreateAsync(NewItemKind? kind)
    {
        if (kind is null) return;
        var suggested = SuggestedNameFor(kind);
        var name = AskName is null ? suggested : await AskName(kind, suggested);
        if (name is null) return;   // cancelled: nothing is written
        AddItemNamed(kind, name);
    }

    /// <summary>
    /// Create one of <see cref="NewItemKinds"/> under a name the artist chose.
    /// </summary>
    /// <param name="name">
    /// What to call it, or null to keep the numbered default.
    /// </param>
    /// <remarks>
    /// <b>B65.</b> Every creation path wrote straight to disk under
    /// <c>Character 3</c> or <c>Scene 2</c>, so a project filled with numbered
    /// items and the only way to correct one was a file manager — B64 says the
    /// docker cannot rename. Asking first is the whole fix, and it is the same
    /// ordering B66 and B78 landed on: a name is a question, not a correction.
    /// The null default keeps <see cref="AddItem"/> meaning exactly what it did,
    /// so nothing that already created an item changed behaviour.
    /// </remarks>
    public void AddItemNamed(NewItemKind? kind, string? name)
    {
        if (kind == NewFolderItem) AddFolder(name);
        else AddDocument(name);
    }

    /// <summary>
    /// What to put in the name box before the artist types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A prompt that is already most of the answer, so naming is a confirmation
    /// rather than a blank page. It is the same expression each creator falls
    /// back to, kept here so the box and the fallback cannot drift apart — a
    /// suggestion that differs from what Enter would produce is worse than no
    /// suggestion.
    /// </para>
    /// <para>
    /// <b>B107.</b> A drawing made inside a folder is offered that folder's name
    /// and a separator — <c>Knight - </c> — because the folder is what the
    /// drawing is <em>of</em>, and typing <c>walk</c> after it is the whole
    /// gesture. <c>Document 7</c> was a number nobody wanted to keep, so every
    /// document had to be renamed once by hand. The prefix is a starting point
    /// rather than a rule: the box is ordinary text and selecting it replaces it.
    /// </para>
    /// <para>
    /// <b>Folders keep their plain suggestion, on purpose.</b> Subfolders are
    /// structural — <c>locomotion</c>, <c>combat</c> — and prefixing them would
    /// compound down the tree into <c>Knight - Locomotion - Knight - Walk</c>.
    /// Scenes and shots keep their numbers because a shot's number <em>is</em>
    /// its place in the running order.
    /// </para>
    /// </remarks>
    public string SuggestedNameFor(NewItemKind? kind)
    {
        if (Project is not { } project) return "";
        // A folder had no branch at all and fell through to the animation line
        // below, so ＋ New ▸ Folder offered "Animation 0" — the exact drift the
        // remarks above warn about, in the one place nothing asserted.
        if (kind == NewFolderItem) return "Folder";
        // A drawing is named after the folder it lands in, which after B114 is
        // one rule rather than three that had to agree.
        return TargetFolder is { } folder
            ? Stem(folder.Name)
            : $"Document {project.Manifest.Documents.Count + 1}";
    }

    /// <summary>
    /// What goes between a scope's name and the rest of a derived name.
    /// </summary>
    /// <remarks>
    /// Spaced, so <c>Knight - Walk</c> reads as a phrase rather than as a slug.
    /// The path is where hyphens belong, and <c>ProjectIo.Slug</c> puts them
    /// there: this becomes <c>knight-walk.lightbox.json</c>.
    /// </remarks>
    public const string NameSeparator = " - ";

    /// <summary>A scope's name as the beginning of a name for something in it.</summary>
    private static string Stem(string scope) => $"{scope}{NameSeparator}";

    /// <summary>A chosen name, or the numbered fallback when nothing was given.</summary>
    /// <remarks>
    /// <b>B107.</b> A dangling separator is trimmed along with the whitespace,
    /// which is what keeps the promise above — the box now offers a stem, and
    /// pressing Enter on <c>Knight - </c> unchanged has to produce
    /// <c>Knight</c> rather than a document called <c>Knight -</c>. Trailing
    /// rather than anywhere: <c>Knight - walk</c> keeps its separator, because
    /// that is the name the artist finished typing.
    /// </remarks>
    private static string Named(string? name, string fallback)
    {
        var trimmed = (name ?? "").Trim().TrimEnd('-', '–', '—', ':', '·', ' ', '\t');
        return trimmed.Length == 0 ? fallback : trimmed;
    }

    // ---- the running order ----------------------------------------------------

    /// <summary>Move the selected row up or down its folder's running order.</summary>
    /// <remarks>
    /// <b>B114.</b> Was two operations on two lists — scenes within the project,
    /// shots within a scene. It is one now: something moves within the order of
    /// whatever contains it, and a document and a sub-folder are arranged by the
    /// same list. Materialising that list is what the first nudge does, so a
    /// folder nobody has arranged still writes no <c>order</c> key.
    /// </remarks>
    [RelayCommand]
    private void MoveSelectedUp() => Nudge(-1);

    [RelayCommand]
    private void MoveSelectedDown() => Nudge(+1);

    private void Nudge(int delta)
    {
        if (Project is not { } project || Selected is not { } row) return;
        var manifest = project.Manifest;

        bool moved;
        if (row is { IsFolder: true, Folder: { } folder })
        {
            var parent = ProjectFolders.ById(manifest, folder.ParentId);
            var siblings = ProjectFolders.ChildrenInOrder(manifest, parent);
            var at = IndexOfId(siblings.Select(f => f.Id), folder.Id);
            moved = ProjectFolders.MoveFolder(manifest, parent, at, at + delta);
        }
        else if (row is { Animation: { } document, Folder: { } owner })
        {
            var documents = ProjectFolders.InOrder(manifest, owner);
            var at = IndexOfId(documents.Select(d => d.Id), document.Id);
            moved = ProjectFolders.MoveDocument(manifest, owner, at, at + delta);
        }
        else
        {
            // A loose document, or the project row. Nothing contains it, so
            // there is no order it is in — said by doing nothing rather than by
            // arranging something else.
            return;
        }

        if (!moved) return;
        var keep = row.Key;
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Key == keep);
        _changed();
    }

    private static int IndexOfId(IEnumerable<string> ids, string wanted)
    {
        var at = 0;
        foreach (var id in ids)
        {
            if (id == wanted) return at;
            at++;
        }
        return -1;
    }

    /// <summary>
    /// The running time of everything arranged, or null when nothing is.
    /// </summary>
    /// <remarks>
    /// Over the folders carrying an authored order. A folder nobody arranged
    /// adds up to a number too, but it is not a running time — an artist who
    /// wants one says so by arranging it, which is the same gesture that makes
    /// the order mean anything.
    /// </remarks>
    public string? TotalRunningTime
    {
        get
        {
            if (Project is not { } project) return null;
            var scenes = Ordered.ToList();
            if (scenes.Count == 0) return null;

            var frames = 0;
            double seconds = 0;
            var known = true;
            foreach (var scene in scenes)
            {
                var (f, s) = ProjectIo.FolderDuration(project.Manifest, scene);
                frames += f;
                if (s is { } value) seconds += value;
                else known = false;
            }
            return known ? $"{Clock(seconds)} · {frames}f" : $"{frames}f";
        }
    }

    /// <summary>
    /// A document that belongs to the project rather than to a character.
    /// </summary>
    private void AddLooseDocument(string? name = null)
    {
        if (Project is not { } project) return;
        var doc = _newDocument();
        var reference = FileInProject(project, name, doc);
        _open(reference, doc);
        _changed();
    }

    /// <summary>
    /// Put a document into the project: a row of its own, filed where the artist
    /// is, marked not saved yet.
    /// </summary>
    /// <remarks>
    /// Shared by the docker's ＋ New ▸ Document and by <b>B99</b>'s File ▸ New, so
    /// the two cannot disagree about where a new drawing lands or what it looks
    /// like before it is written. The caller supplies the document because the
    /// two get theirs from different places — the docker asks for a blank one,
    /// File ▸ New has already built one from the dialog's settings.
    /// </remarks>
    private DocumentRef FileInProject(Project project, string? name, Doc doc)
    {
        var count = project.Manifest.Documents.Count + 1;
        // B85, "created where you are": into the folder the artist was in. The
        // reported defect was that creating inside a subfolder ignored the
        // location and filed the drawing at the top level.
        //
        // B114 widened it without changing it. Selecting a character used to
        // leave `TargetFolder` null, so File → New landed at the root and the
        // rule quietly did not apply there; now every container is a folder and
        // it applies everywhere, which is what the report asked for.
        var reference = ProjectIo.AddDocument(
            project, Named(name, $"Document {count}"), doc, TargetFolder);
        if (TargetFolder is { } folder) _collapsed.Remove(folder.Id);
        // In the project, not on disk — so the row says "not saved yet" rather
        // than "not on disk", and the next project save writes it (B76, B99).
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        return reference;
    }

    /// <summary>
    /// Take a document the application has just created into the project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B99.</b> File ▸ New with a project open used to make a tab and nothing
    /// else: no manifest entry, no row, <c>Source</c> null — and
    /// <c>SaveProject</c> writes only tabs that have a <c>Source</c>, so the one
    /// document with nothing on disk was also the one a project save skipped.
    /// </para>
    /// <para>
    /// <b>File ▸ New still means "a new document", not "an animation under the
    /// selected character".</b> What changes is that the project now knows about
    /// it: it is filed where the artist is, exactly as ＋ New ▸ Document is, and
    /// it appears as an ordinary unsaved row. The rule the old comment was
    /// protecting — that the most common action in the app must not change
    /// meaning based on which row is selected — still holds, because a project
    /// document is what it made before and what it makes now.
    /// </para>
    /// <para>
    /// Null when there is no project, which is the ordinary state: a
    /// document-first application must not acquire a project by making a drawing.
    /// </para>
    /// </remarks>
    public DocumentRef? AdoptNewDocument(string? name, Doc doc)
    {
        if (Project is not { } project) return null;
        var reference = FileInProject(project, name, doc);
        _changed();
        return reference;
    }

    /// <summary>
    /// A tab closed. Take its document out of the project if it was never
    /// written — true when it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B99, and the owner's rule: closing a document that was never saved
    /// removes it from the project view immediately.</b> The alternative is a row
    /// pointing at a file that does not exist and never will, which the next
    /// refresh would report as <em>not on disk</em> — the badge that means
    /// something went wrong, raised by nothing going wrong.
    /// </para>
    /// <para>
    /// <b>Never written is narrower than "not on disk", deliberately.</b> Both
    /// conditions are required: the id is still in <see cref="Dirty"/>, so no
    /// save has ever covered it, <em>and</em> there is no file at its path. A
    /// document saved and then edited is dirty and on disk, so closing it keeps
    /// its row; a document whose file was deleted from outside is not dirty, so
    /// it keeps its row too and stays flagged missing — B61's rule that a
    /// vanished file is the artist's decision to act on, not ours.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A document was written by a save the project did not run — Save As.
    /// False when it landed outside the project, having taken it out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The counterpart B99 obliged and did not have.</b> Adopting a document
    /// at creation means something has to un-adopt it when the artist gives it a
    /// home elsewhere: without this, Save As to a folder outside the project left
    /// the row in place pointing at a file that was never written there, still
    /// saying "not saved yet" — and the next project save wrote a *second* copy
    /// inside the project. That is B106's one-drawing-two-files arriving from a
    /// different direction, so it is worth being explicit that adoption and
    /// release are a pair.
    /// </para>
    /// <para>
    /// <b>Inside the project, the record follows the file rather than the other
    /// way round.</b> Save As into a folder of the project repaths the reference
    /// and renames the row to match, because a panel that says <i>Rooftop</i>
    /// while the file says <c>rooftop-v2</c> is B64's complaint exactly. It also
    /// clears the pending mark: the document is on disk now, and only the
    /// project's own save used to be able to say so.
    /// </para>
    /// </remarks>
    public bool AdoptSavedPath(DocumentRef? reference, string fullPath)
    {
        if (Project is not { } project || reference is null) return false;

        if (ProjectIo.RelativeInProject(project, fullPath) is not { } relative)
        {
            Detach(project, reference);
            Rebuild();
            Status = $"“{reference.Name}” was saved outside the project, so it is no longer in it.";
            _changed();
            return false;
        }

        reference.Path = relative;
        reference.Name = Path.GetFileName(relative).Replace(".lightbox.json", "", StringComparison.OrdinalIgnoreCase);
        // Which folder that directory is, if it is one the artist made. Null for
        // the unassigned directory and for anything the manifest does not know
        // about, which is what B83's accountability check is for.
        var directory = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
        reference.FolderId = ProjectFolders.All(project.Manifest)
            .FirstOrDefault(f => ProjectFolders.PathOf(project.Manifest, f) == directory)?.Id;
        // On disk now, so the row stops saying otherwise and the next project
        // save does not rewrite it.
        _dirty.Remove(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id) ?? Selected;
        _changed();
        return true;
    }

    public bool ForgetIfNeverWritten(DocumentRef? reference)
    {
        if (Project is not { } project || reference is null) return false;
        if (!_dirty.Contains(reference.Id)) return false;
        if (ProjectIo.ResolveInProject(project, reference.Path) is { } path
            && (File.Exists(path) || Directory.Exists(path)))
        {
            return false;
        }

        Detach(project, reference);
        Rebuild();
        Status = $"“{reference.Name}” was never saved, so it is no longer in the project.";
        _changed();
        return true;
    }

    /// <summary>
    /// Re-file a row into a folder, or into the project when
    /// <paramref name="destination"/> is null. What a drag in the tree does.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> There were two of these — one taking a <c>Character</c> and
    /// one taking a <c>ProjectFolder</c> — and the drop handler had to pick.
    /// One container, one method: this is <see cref="MoveInto"/> with the
    /// status messages a drag wants and the same-place check a drag needs.
    ///
    /// The document keeps its id, so a tab already showing it stays bound to
    /// it — rearranging the tree must not orphan the window you are drawing
    /// in. The file follows it (B106): a move is a rename on disk, so the
    /// drawing ends up in one place rather than in both.
    /// </remarks>
    public bool Move(ProjectRow row, ProjectFolder? destination)
    {
        // Dropping a row onto what it already belongs to is an ordinary slip in
        // a tree view, not something to report — and separating it here is what
        // lets everything below say "could not" and mean it.
        var here = row.IsFolder ? row.Folder!.ParentId : row.Folder?.Id;
        if (string.Equals(here, destination?.Id, StringComparison.Ordinal)) return false;

        var name = row.Animation?.Name ?? row.Folder?.Name ?? "";
        if (!MoveInto(row, destination))
        {
            // B106. The move can fail for a reason the artist can act on — a
            // file open elsewhere, a permission, a name already taken on disk —
            // and it refuses whole rather than changing the panel and not the
            // folder. Said out loud, because a drag that silently does nothing
            // is indistinguishable from one that missed.
            if (Status.Length == 0) Status = $"Could not move “{name}”. It is still where it was.";
            return false;
        }
        Status = destination is null
            ? $"Moved “{name}” to the project."
            : $"Moved “{name}” to {destination.Name}.";
        return true;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Project is not { } project || Selected is not { } row) return;
        // B62. The project row is a place, not a thing in the project, so every
        // operation that removes has to say no to it. Said out loud rather than
        // ignored: a － that does nothing is indistinguishable from a broken one.
        if (row.IsRoot)
        {
            Status = "The project itself cannot be removed. Close it from the File menu.";
            return;
        }
        if (row.Animation is { } animation)
        {
            Detach(project, animation);
            // The file is deliberately left on disk. Removing a row from an
            // index is cheap to undo by hand; deleting an artist's drawing
            // because they clicked the wrong row is not. Delete permanently is
            // the other menu item, and it says so (B87).
            Status = $"Removed “{animation.Name}” from the project. Its file is still on disk.";
        }
        else if (row is { IsFolder: true, Folder: { } folder })
        {
            // B87. Its documents come back to the project root rather than
            // disappearing with it: the artist removed a folder, not the work
            // that was in it, and a drawing with no row is a drawing that is
            // gone as far as anyone can tell.
            var orphaned = ProjectFolders.Remove(project.Manifest, folder);
            foreach (var document in orphaned)
            {
                ProjectFolders.FileDocument(project.Manifest, document, null);
                _dirty.Add(document.Id);
            }
            Status = orphaned.Count == 0
                ? $"Removed “{folder.Name}”. Its folder is still on disk."
                : $"Removed “{folder.Name}”. {Count(orphaned.Count, "document")} moved to the project root.";
        }
        else
        {
            // Nothing else can be a row. B114 removed the character and scene
            // branches this used to need — one of which was "not wired up yet"
            // and the other of which read `row.Character!` on a scene row.
            Status = "Only documents and folders can be removed from here.";
            return;
        }
        Rebuild();
        _changed();
    }

    // ---- deleting for real (B87) ------------------------------------------------

    /// <summary>
    /// Whether deleting what is selected should ask first.
    /// </summary>
    /// <remarks>
    /// <b>B87</b>, and the reporter drew the line: a folder holding anything
    /// asks, an empty one does not. Separated from the deleting so the
    /// <em>decision</em> is testable and only the dialog is manual — the same
    /// split B65 uses for the name prompt, and for the same reason.
    /// </remarks>
    public bool DeleteNeedsConfirmation
    {
        get
        {
            if (Project is not { } project || Selected is not { IsFolder: true, Folder: { } folder })
            {
                // A single document is one file and one undoable mistake; a
                // character or scene is not deletable here at all.
                return false;
            }
            var (folders, documents) = ProjectFolders.Contents(project.Manifest, folder);
            return folders.Count > 1 || documents.Count > 0;
        }
    }

    /// <summary>What the confirmation should say, so the artist knows the size of it.</summary>
    public string DeleteWarning
    {
        get
        {
            if (Project is not { } project || Selected is not { IsFolder: true, Folder: { } folder })
            {
                return Selected?.Animation is { } document
                    ? $"Delete “{document.Name}” from the project and from disk?"
                    : "Delete the selected item from the project and from disk?";
            }
            var (folders, documents) = ProjectFolders.Contents(project.Manifest, folder);
            var inside = new List<string>();
            if (folders.Count > 1) inside.Add(Count(folders.Count - 1, "folder"));
            if (documents.Count > 0) inside.Add(Count(documents.Count, "document"));
            return inside.Count == 0
                ? $"Delete the empty folder “{folder.Name}” from disk?"
                : $"Delete “{folder.Name}” and the {string.Join(" and ", inside)} inside it, "
                  + "from the project and from disk?";
        }
    }

    /// <summary>
    /// Remove what is selected from the project <b>and</b> from disk.
    /// </summary>
    /// <remarks>
    /// <b>B87.</b> The context menu only offered "remove from project", so
    /// deleting a file meant leaving Lightbox for a file manager. This is the
    /// other half, and it is a separate item rather than a modifier on the
    /// first: two operations with different consequences should not be one
    /// gesture told apart by a held key.
    ///
    /// The caller confirms first when <see cref="DeleteNeedsConfirmation"/>
    /// says so. Nothing here asks, because a view model that opens dialogs is a
    /// view model no test can drive.
    /// </remarks>
    [RelayCommand]
    public void DeleteSelectedPermanently()
    {
        if (Project is not { } project || Selected is not { } row) return;
        // B62, and the one that would have been worst: deleting the project row
        // means deleting the project folder, which is every drawing in it.
        if (row.IsRoot)
        {
            Status = "The project itself cannot be deleted from here.";
            return;
        }

        if (row.Animation is { } document)
        {
            var path = document.Path;
            Detach(project, document);
            Status = ProjectIo.DeleteInProject(project, path)
                ? $"Deleted “{document.Name}”."
                : $"Removed “{document.Name}” from the project, but its file could not be deleted.";
        }
        else if (row is { IsFolder: true, Folder: { } folder })
        {
            // The directory before the manifest: PathOf walks the parent chain,
            // and a folder already taken out of the manifest has no chain to
            // walk — it would resolve to the project root and delete the
            // project. Order is load-bearing here, not stylistic.
            var path = ProjectFolders.PathOf(project.Manifest, folder);
            var (folders, documents) = ProjectFolders.Contents(project.Manifest, folder);
            var deleted = ProjectIo.DeleteInProject(project, path);

            ProjectFolders.Remove(project.Manifest, folder);
            foreach (var inside in documents) Detach(project, inside);

            Status = deleted
                ? $"Deleted “{folder.Name}” and everything in it."
                : $"Removed “{folder.Name}” from the project, but its folder could not be deleted.";
            _ = folders;
        }
        else
        {
            Status = "Only documents and folders can be deleted from here.";
            return;
        }

        Rebuild();
        _changed();
    }

    /// <summary>Take a document out of the project without touching disk.</summary>
    /// <remarks>
    /// <b>B114.</b> Three lists to take it out of, and now one — plus any
    /// variant that pointed at it, which would otherwise be an override naming
    /// a document the project no longer has.
    /// </remarks>
    private void Detach(Project project, DocumentRef document)
    {
        project.Manifest.Documents.RemoveAll(d => d.Id == document.Id);
        foreach (var variant in ProjectFolders.All(project.Manifest)
                     .SelectMany(f => f.Variants ?? []))
        {
            variant.Overrides.Remove(document.Id);
            foreach (var (baseId, over) in variant.Overrides.ToList())
                if (over == document.Id) variant.Overrides.Remove(baseId);
        }
        project.Loaded.Remove(document.Id);
        _dirty.Remove(document.Id);
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Open the selected animation as a tab.</summary>
    public void OpenSelected()
    {
        if (Project is not { } project || Selected?.Animation is not { } reference) return;
        if (ProjectIo.LoadDocument(project, reference) is not { } doc)
        {
            Status = $"“{reference.Name}” is missing from disk.";
            return;
        }
        _open(reference, doc);
    }

    /// <summary>
    /// Rename a row, on disk as well as in the panel. False when it could not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B64.</b> This used to set a name in memory and stop, which is the
    /// more confusing half of a rename: the panel says <i>Sir Reginald</i> and
    /// the folder still says <i>knight</i>, and the artist believes the panel
    /// until they open a file manager.
    /// </para>
    /// <para>
    /// <b>The reason is part of the fix, not a nicety.</b> Renaming is the first
    /// docker operation that can fail for reasons the app does not control — a
    /// file open elsewhere, a permission, a name the tree accepts and the
    /// filesystem does not — and the artist has to be told which, because
    /// "nothing happened" is indistinguishable from "the click missed".
    /// <see cref="Status"/> carries it and the boolean lets the caller keep the
    /// edit box open.
    /// </para>
    /// </remarks>
    public bool Rename(ProjectRow row, string name)
    {
        if (Project is not { } project) return false;
        // B62. Renaming the project row means renaming the folder the
        // application currently has open, which is a different operation with
        // its own questions — not something to do behind an in-place edit box.
        if (row.IsRoot)
        {
            Status = "The project folder is renamed outside Lightbox.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (trimmed == row.Name) return true;   // not a change, not a failure

        var ok = row switch
        {
            { IsFolder: true, Folder: { } folder } => RenameFolder(project, folder, trimmed),
            { Animation: { } document } => RenameDocument(project, document, trimmed),
            _ => false,
        };
        if (!ok) return false;

        Rebuild();
        _changed();
        return true;
    }

    private bool RenameFolder(Project project, ProjectFolder folder, string name)
    {
        var was = ProjectFolders.PathOf(project.Manifest, folder);
        if (!ProjectFolders.Rename(project.Manifest, folder, name))
        {
            Status = $"There is already a folder called “{name}” here.";
            return false;
        }

        var now = ProjectFolders.PathOf(project.Manifest, folder);
        if (!ProjectIo.MoveInProject(project, was, now))
        {
            // Put the tree back. A manifest that says one thing while the disk
            // says another is worse than a refused rename, because only one of
            // those is visible.
            ProjectFolders.Rename(project.Manifest, folder, Path.GetFileName(was));
            Status = $"Could not rename the folder on disk. It is still “{folder.Name}”.";
            return false;
        }

        // Everything filed below it moved with it, so their recorded paths have
        // to follow — they are what the next save writes to.
        foreach (var (inside, _) in DocumentsUnder(project.Manifest, folder))
        {
            inside.Path = ProjectFolders.PathFor(
                project.Manifest, inside, ProjectFolders.ById(project.Manifest, inside.FolderId));
        }
        Status = $"Renamed to “{name}”.";
        return true;
    }

    private bool RenameDocument(Project project, DocumentRef document, string name)
    {
        var was = document.Path;
        document.Name = name;
        var now = document.FolderId is null && !IsUnfiled(was)
            // A character's animation or a scene's shot keeps the shape of the
            // path it already has; only the file's own name changes.
            ? RenamedLeaf(was, name)
            : ProjectFolders.PathFor(
                project.Manifest, document, ProjectFolders.ById(project.Manifest, document.FolderId));

        if (!ProjectIo.MoveInProject(project, was, now))
        {
            document.Name = Path.GetFileNameWithoutExtension(was).Replace(".lightbox", "");
            Status = $"Could not rename the file on disk. It is still “{document.Name}”.";
            return false;
        }
        document.Path = now;
        Status = $"Renamed to “{name}”.";
        return true;
    }

    /// <summary>
    /// Whether a path is in the directory that holds documents belonging to no
    /// folder.
    /// </summary>
    /// <remarks>
    /// <b>B105.</b> Both names, because the directory was renamed and a project
    /// written before that keeps its recorded paths. One name here would have
    /// made renaming a document in an old project take the character branch
    /// below and derive a path from a shape it does not have.
    /// </remarks>
    private static bool IsUnfiled(string path) =>
        path.StartsWith($"{ProjectIo.DocumentsDir}/", StringComparison.Ordinal)
        || path.StartsWith($"{ProjectIo.LegacyDocumentsDir}/", StringComparison.Ordinal);

    /// <summary>Swap the file's own name, keeping the folders above it.</summary>
    private static string RenamedLeaf(string path, string name)
    {
        var cut = path.LastIndexOf('/');
        var directory = cut < 0 ? "" : path[..(cut + 1)];
        return $"{directory}{ProjectIo.Slug(name)}.lightbox.json";
    }

    private static IEnumerable<(DocumentRef Document, ProjectFolder Folder)> DocumentsUnder(
        ProjectManifest manifest, ProjectFolder folder)
    {
        var (folders, documents) = ProjectFolders.Contents(manifest, folder);
        var byId = folders.ToDictionary(f => f.Id);
        foreach (var document in documents)
        {
            if (document.FolderId is { } id && byId.TryGetValue(id, out var owner))
            {
                yield return (document, owner);
            }
        }
    }

    // ---- production status -------------------------------------------------------

    /// <summary>
    /// Set a document's production status. Returns what it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The manifest is the record and the row is a mirror of it, so both are written
    /// here rather than binding the row two-way: setting a status saves the project and
    /// may fire an export, and neither belongs behind a property assignment.
    /// </para>
    /// <para>
    /// Returns the previous value because the caller needs it to decide whether this was
    /// a change at all — re-selecting the status something already has must not fire an
    /// export, or opening the menu to look would ship the asset.
    /// </para>
    /// <para>
    /// Nothing here touches the document. Status is production metadata about a drawing,
    /// not part of it, which is why marking something Ready does not dirty the artwork
    /// file or need it open.
    /// </para>
    /// </remarks>
    public AssetStatus? SetStatus(ProjectRow row, AssetStatus? status)
    {
        if (row.Animation is not { } reference) return null;

        var before = reference.Status;
        reference.Status = status;
        row.Status = status;

        // Saves the manifest, not the artwork — the project's save writes only the
        // documents in `_dirty`, and this did not put one there.
        if (before != status) _changed();
        return before;
    }

    // ---- where things are on disk ------------------------------------------------

    /// <summary>The project folder, or null.</summary>
    public string? RootPath => Project?.Root;

    /// <summary>
    /// Where a row lives on disk: an animation's file, a character's folder,
    /// or null when there is no project.
    /// </summary>
    /// <remarks>
    /// The path may not exist yet — an animation created a moment ago has no
    /// file until the project is saved. Callers say what to do about that;
    /// <see cref="Services.FileReveal"/> falls back to the folder it will land
    /// in, which is more use than refusing.
    /// </remarks>
    public string? PathOf(ProjectRow? row)
    {
        if (Project is not { } project) return null;
        if (row is null) return project.Root;
        if (row.Animation is { } animation)
        {
            return Path.Combine(
                project.Root, animation.Path.Replace('/', Path.DirectorySeparatorChar));
        }
        // B76. A folder has a directory of its own, and until this answered it
        // the folder rows all resolved to the project root — which meant they
        // could never be reported as pending or missing, however wrong they were.
        if (row is { IsFolder: true, Folder: { } folder })
        {
            var relative = ProjectFolders.PathOf(project.Manifest, folder);
            return Path.Combine(project.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        }
        return project.Root;
    }

    public string? SelectedPath => PathOf(Selected);

    /// <summary>Show the selected row's file or folder in the file manager.</summary>
    /// <remarks>
    /// <b>B62.</b> There was a second command beside this one, <c>RevealRoot</c>,
    /// which the toolbar button used — and it is gone rather than merely
    /// unbound. <see cref="PathOf"/> already answers the project root for a null
    /// selection and for the project row, so keeping a separate command would
    /// have left two ways to do one thing and a button whose behaviour did not
    /// follow the tree.
    /// </remarks>
    [RelayCommand]
    private void RevealSelected()
    {
        if (SelectedPath is not { } path) return;
        if (!Services.FileReveal.Reveal(path)) Status = "Could not open the file manager.";
    }

    /// <summary>
    /// Hand the selected file to whatever application the desktop associates
    /// with it — a text editor for a <c>.lightbox.json</c>, usually.
    /// </summary>
    /// <remarks>
    /// Distinct from opening it as a tab, which is what double-click and
    /// <see cref="OpenSelected"/> do. Both are worth having and neither is a
    /// substitute: one is for drawing, the other is for looking at the JSON or
    /// dragging the file somewhere.
    /// </remarks>
    [RelayCommand]
    private void OpenSelectedExternally()
    {
        if (SelectedPath is not { } path) return;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Status = "Save the project first — this file is not on disk yet.";
            return;
        }
        if (!Services.FileReveal.Open(path)) Status = "No application is registered for this file.";
    }

    /// <summary>The selected row's path, for pasting into a terminal or a bug report.</summary>
    public string CopiedPath { get; private set; } = "";

    [RelayCommand]
    private void CopySelectedPath()
    {
        if (SelectedPath is not { } path) return;
        CopiedPath = path;
        Status = path;
    }

    /// <summary>Open the selected row as a tab — the double-click, as a menu item.</summary>
    [RelayCommand]
    private void OpenSelectedRow() => OpenSelected();

    /// <summary>
    /// Copy the selected animation into the same character, art and all.
    /// </summary>
    /// <remarks>
    /// The most common thing missing from the panel: a cycle you want to
    /// vary — a walk into a limp — starts as a copy of the walk, and the
    /// alternative is exporting and re-importing it.
    /// </remarks>
    [RelayCommand]
    private void DuplicateSelected()
    {
        if (Project is not { } project || Selected is not { Animation: { } source } row) return;
        if (ProjectIo.LoadDocument(project, source) is not { } doc)
        {
            Status = $"“{source.Name}” is missing from disk.";
            return;
        }

        var copy = Lightbox.Core.Serialization.DocJson.Clone(doc);
        var reference = ProjectIo.AddDocument(project, $"{source.Name} copy", copy, row.Folder);
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        Status = $"Copied to “{reference.Name}”. Save to write it to disk.";
        _changed();
    }
}
