using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One row in the project tree: a character, a scene, or a document under one
/// of them.
/// </summary>
/// <remarks>
/// Two kinds of heading, on purpose. A character groups drawings by <i>who</i>
/// and a scene by <i>when</i>, and they cross — one scene holds several
/// characters, one character appears in several scenes — so neither can be a
/// folder inside the other and the tree shows both.
/// </remarks>
public sealed partial class ProjectRow : ObservableObject
{
    public ProjectRow(Character character)
    {
        Character = character;
        _name = character.Name;
    }

    public ProjectRow(ProjectScene scene, string? duration)
    {
        Scene = scene;
        Duration = duration;
        _name = scene.Name;
    }

    public ProjectRow(Character? owner, DocumentRef animation)
    {
        Character = owner;
        Animation = animation;
        _name = animation.Name;
    }

    /// <summary>A folder the artist made, at <paramref name="depth"/> from the root.</summary>
    /// <remarks>
    /// <b>B86.</b> Depth is carried rather than derived, because the row does not
    /// know the manifest and asking it to would make every row hold a reference
    /// to the project so it could walk its own ancestry on each repaint.
    /// </remarks>
    public ProjectRow(ProjectFolder folder, int depth)
    {
        Folder = folder;
        Depth = depth;
        _name = folder.Name;
    }

    /// <summary>A document filed in a folder.</summary>
    public ProjectRow(ProjectFolder folder, DocumentRef document, int depth)
    {
        Folder = folder;
        Animation = document;
        Depth = depth;
        _name = document.Name;
    }

    public ProjectRow(ProjectScene scene, DocumentRef shot, string? duration)
    {
        Scene = scene;
        Animation = shot;
        Duration = duration;
        _name = shot.Name;
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
    /// Null on a document that belongs to the project rather than to any
    /// character — a background, a colour test, a one-off illustration.
    /// </summary>
    public Character? Character { get; }

    /// <summary>The scene this row is, or the one a shot sits in.</summary>
    public ProjectScene? Scene { get; }

    /// <summary>
    /// The folder this row <em>is</em>, or the one a document is filed in.
    /// </summary>
    /// <remarks>
    /// <b>B85/B86.</b> Reads the same way <see cref="Character"/> does — a row
    /// is a container or a thing inside one — so a document's row and its
    /// folder's row both answer "which folder is this about", which is what
    /// creating and dropping into a folder both need.
    /// </remarks>
    public ProjectFolder? Folder { get; }

    /// <summary>How far this row is indented, in tree levels.</summary>
    public int Depth { get; }

    /// <summary>Null on a character or scene row.</summary>
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
        ReferenceEquals(Character, other.Character)
        && ReferenceEquals(Scene, other.Scene)
        && ReferenceEquals(Folder, other.Folder)
        && ReferenceEquals(Animation, other.Animation)
        && Depth == other.Depth
        && IsRoot == other.IsRoot;

    public bool IsScene => Scene is not null && Animation is null;

    /// <summary>A folder the artist made, rather than a character or a scene.</summary>
    public bool IsFolder => Folder is not null && Animation is null;

    public bool IsCharacter => Animation is null && Scene is null && Folder is null && !IsRoot;

    /// <summary>A heading row — a character, a scene or a folder.</summary>
    public bool IsHeading => Animation is null;

    /// <summary>A document with nothing above it at all.</summary>
    public bool IsLoose =>
        Animation is not null && Character is null && Scene is null && Folder is null;

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
    public double Indent => Folder is not null
        ? (Depth + (Animation is null ? 0 : 1)) * 14
        : IsHeading || IsLoose ? 0 : 14;

    // 🗁 is the open folder, and it matches the toolbar button that used to be
    // the only way to reach the project folder — the row inherited the icon
    // along with the job (B62).
    public string Glyph => IsRoot ? "🗁" : IsScene ? "🎬" : IsFolder ? "🗀" : IsCharacter ? "🗀" : "▣";

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
        Animation?.Id ?? Scene?.Id ?? Character?.Id ?? Folder?.Id ?? (IsRoot ? RootKey : null);
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
    /// Whether this project has a running order to reorder.
    /// </summary>
    /// <remarks>
    /// The order of a character's animations is alphabetical housekeeping; the
    /// order of a film's shots is the film. So the reorder buttons are absent
    /// until there are scenes, rather than sitting there doing nothing useful.
    /// </remarks>
    public bool HasScenes => Project?.HasScenes ?? false;

    public string ProjectName => Project?.Name ?? "";

    public ObservableCollection<ProjectRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedCharacter))]
    private ProjectRow? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>The character to add work under: the selected row's, or the first.</summary>
    public Character? SelectedCharacter => Selected?.Character ?? Project?.Characters.FirstOrDefault();

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
    public string DescribeExportPlan() => ExportPlan.Describe(PlanExport());

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
        var scope = ScopeOfSelected();
        var list = scope is null ? project.Manifest.Resources : scope.Resources;
        if (list is null || !list.Remove(resource)) return;
        // Emptied rather than left as an empty list, so a scope that declares
        // nothing writes no key — the same rule the camera follows.
        if (list.Count == 0)
        {
            if (scope is null) project.Manifest.Resources = null;
            else scope.Resources = null;
        }
        AfterScopeChange(project, $"No longer shared with {ShareScopeLabel}.");
    }

    private bool Already(ProjectFolder? scope, string kind, string id) =>
        (scope is null ? Project?.Manifest.Resources : scope.Resources)?
            .Any(r => r.Kind == kind && r.Id == id) ?? false;

    private void AfterScopeChange(Project project, string status)
    {
        Status = status;
        OnPropertyChanged(nameof(DeclarationsOnSelected));
        // The manifest changed, so the project is unsaved — and the resolver
        // that decides which palettes a document paints from has to be re-run,
        // which is what _changed does.
        _changed();
    }

    /// <summary>Note that a document changed, so the next save writes it and only it.</summary>
    public void MarkDirty(DocumentRef reference) => _dirty.Add(reference.Id);

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
        OnPropertyChanged(nameof(HasScenes));
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
            if (previous.FirstOrDefault(p => p.Describes(fresh)) is { } kept)
            {
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
        foreach (var character in Project?.Characters ?? [])
        {
            Add(new ProjectRow(character));
            foreach (var animation in character.Animations)
                Add(new ProjectRow(character, animation) { Status = animation.Status });
        }
        // Scenes after the characters, because the characters are what a
        // project is named after and a film's shot list is the second axis
        // rather than the first. Absent entirely when there are none.
        foreach (var scene in Project?.Scenes ?? [])
        {
            Add(new ProjectRow(scene, RunningTime(scene)));
            foreach (var shot in scene.Shots)
                Add(new ProjectRow(scene, shot, ShotTime(shot)) { Status = shot.Status });
        }
        // Project-level documents last, unindented — they belong to the
        // project, not under anything. B85: only the ones that are not filed in
        // a folder; the rest were emitted above, under the folder they are in.
        foreach (var document in Project?.Manifest.Documents ?? [])
        {
            if (document.FolderId is not null) continue;
            Add(new ProjectRow(null, document) { Status = document.Status });
        }
        MarkMissing();
        Selected = Rows.FirstOrDefault(r => r.Key == keep);
        OnPropertyChanged(nameof(HasScenes));
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
        foreach (var folder in ProjectFolders.ChildrenOf(manifest, parent).OrderBy(f => f.Name))
        {
            var collapsed = _collapsed.Contains(folder.Id);
            add(new ProjectRow(folder, depth) { IsCollapsed = collapsed });
            if (collapsed) continue;
            EmitFolders(manifest, folder, depth + 1, add);
            foreach (var document in ProjectFolders.DocumentsIn(manifest, folder))
            {
                add(new ProjectRow(folder, document, depth) { Status = document.Status });
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
            // A character's animation or a scene's shot has to leave that first,
            // or it would be in two places: the folder tree and the character.
            // Its own disk move is a no-op by now — the file is already at `to`,
            // and MoveInProject treats "nothing there to move" as a success.
            if (row.Character is not null || row.Scene is not null)
            {
                if (!ProjectIo.MoveDocument(project, document, null)) return false;
            }
            if (!ProjectFolders.FileDocument(project.Manifest, document, destination)) return false;
            _dirty.Add(document.Id);
        }
        else
        {
            // A character or a scene is not in the folder tree yet — Q30.
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

    private static string? RunningTime(ProjectScene scene)
    {
        var (frames, seconds) = ProjectIo.SceneDuration(scene);
        if (scene.Shots.Count == 0) return null;
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

    [RelayCommand]
    private void AddCharacter(string? name = null)
    {
        if (Project is not { } project) return;
        var character = ProjectIo.AddCharacter(
            project, Named(name, $"Character {project.Characters.Count() + 1}"));
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.IsCharacter && r.Character!.Id == character.Id);
        _changed();
    }

    /// <summary>
    /// Create an animation under the selected character and open it.
    ///
    /// This is where new work in a project comes from. <c>File → New</c>
    /// deliberately still means "a new standalone document" — the most common
    /// action in the app must not change meaning based on which row happens to
    /// be selected.
    /// </summary>
    [RelayCommand]
    private void AddAnimation(string? name = null)
    {
        if (Project is not { } project) return;
        var character = SelectedCharacter ?? ProjectIo.AddCharacter(project, "Character 1");

        var doc = _newDocument();
        var reference = ProjectIo.AddAnimation(
            project, character, Named(name, $"Animation {character.Animations.Count}"), doc);
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        _open(reference, doc);
        _changed();
    }

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

    public static readonly NewItemKind NewAnimation =
        new("Animation", "A drawing sequence under the selected character");

    public static readonly NewItemKind NewCharacterItem =
        new("Character", "A new character, with its own animations and palette", IsContainer: true);

    public static readonly NewItemKind NewLooseDocument =
        new("Document", "Belongs to the project, not to any character");

    /// <summary>B86. A folder of the artist's own, at any depth.</summary>
    public static readonly NewItemKind NewFolderItem =
        new("Folder", "A folder you name, inside the selected one", IsContainer: true);

    public static readonly NewItemKind NewSceneItem =
        new("Scene", "A run of shots — the film's second axis, alongside the characters", IsContainer: true);

    public static readonly NewItemKind NewShotItem =
        new("Shot", "A drawing under the selected scene");

    public IReadOnlyList<NewItemKind> NewItemKinds { get; } =
        [
            // B63. Containers first and drawings after, because the menu should
            // read as "where does it go" then "what is it" rather than as a pile.
            NewFolderItem, NewCharacterItem, NewSceneItem,
            NewAnimation, NewShotItem, NewLooseDocument,
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
        if (kind == NewCharacterItem) AddCharacter(name);
        else if (kind == NewFolderItem) AddFolder(name);
        else if (kind == NewSceneItem) AddScene(name);
        else if (kind == NewShotItem) AddShot(name);
        else if (kind == NewLooseDocument) AddLooseDocument(name);
        else AddAnimation(name);
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
        if (kind == NewCharacterItem) return $"Character {project.Characters.Count() + 1}";
        if (kind == NewSceneItem) return $"Scene {project.Scenes.Count + 1}";
        if (kind == NewShotItem) return $"Shot {(SelectedScene?.Shots.Count ?? 0) + 1}";
        // A folder had no branch at all and fell through to the animation line
        // below, so ＋ New ▸ Folder offered "Animation 0" — the exact drift the
        // remarks above warn about, in the one place nothing asserted.
        if (kind == NewFolderItem) return "Folder";
        if (kind == NewLooseDocument)
        {
            return TargetFolder is { } folder
                ? Stem(folder.Name)
                : $"Document {project.Manifest.Documents.Count + 1}";
        }
        // An animation lands under a character, which is the scope it is named
        // after — Q30 answered that a character *is* a folder carrying character
        // data, so the same rule applies to both and this is not a second one.
        return SelectedCharacter is { } character
            ? Stem(character.Name)
            : $"Animation {SelectedCharacter?.Animations.Count() ?? 0}";
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

    // ---- scenes -----------------------------------------------------------------

    /// <summary>The scene to add a shot to: the selected row's, or the last one.</summary>
    public ProjectScene? SelectedScene => Selected?.Scene ?? Project?.Scenes.LastOrDefault();

    [RelayCommand]
    private void AddScene(string? name = null)
    {
        if (Project is not { } project) return;
        var scene = ProjectIo.AddScene(project, Named(name, $"Scene {project.Scenes.Count + 1}"));
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.IsScene && r.Scene!.Id == scene.Id);
        _changed();
    }

    /// <summary>
    /// A shot under the selected scene, creating the first scene if there is
    /// none — the same bargain adding an animation with no character makes.
    /// </summary>
    [RelayCommand]
    private void AddShot(string? name = null)
    {
        if (Project is not { } project) return;
        var scene = SelectedScene ?? ProjectIo.AddScene(project, "Scene 1");

        var doc = _newDocument();
        var reference = ProjectIo.AddShot(project, scene, Named(name, $"Shot {scene.Shots.Count + 1}"), doc);
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        _open(reference, doc);
        _changed();
    }

    /// <summary>
    /// Delete the selected scene. Its shots become loose documents.
    /// </summary>
    /// <remarks>
    /// Reorganising a film must not be the fastest way to delete it, so the
    /// drawings survive — the same rule deleting a palette folder follows. The
    /// files on disk are never touched either way.
    /// </remarks>
    [RelayCommand]
    private void RemoveScene()
    {
        if (Project is not { } project || Selected?.Scene is not { } scene) return;
        if (Selected is not { IsScene: true }) return;
        ProjectIo.RemoveScene(project, scene);
        Rebuild();
        _changed();
    }

    /// <summary>Move the selected scene or shot up or down the running order.</summary>
    [RelayCommand]
    private void MoveSelectedUp() => Nudge(-1);

    [RelayCommand]
    private void MoveSelectedDown() => Nudge(+1);

    private void Nudge(int delta)
    {
        if (Project is not { } project || Selected is not { Scene: { } scene } row) return;
        var moved = row.IsScene
            ? ProjectIo.MoveScene(project, project.Scenes.ToList().IndexOf(scene), Index(project, scene) + delta)
            : ProjectIo.MoveShot(scene, scene.Shots.IndexOf(row.Animation!), scene.Shots.IndexOf(row.Animation!) + delta);
        if (!moved) return;
        var keep = row.Key;
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Key == keep);
        _changed();
    }

    private static int Index(Project project, ProjectScene scene) =>
        project.Scenes.ToList().IndexOf(scene);

    /// <summary>The whole film's running time, or null when a shot is unmeasured.</summary>
    public string? TotalRunningTime
    {
        get
        {
            if (Project is not { HasScenes: true } project) return null;
            var frames = 0;
            double seconds = 0;
            var known = true;
            foreach (var scene in project.Scenes)
            {
                var (f, s) = ProjectIo.SceneDuration(scene);
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
        var count = project.Manifest.Documents.Count + 1;
        var reference = ProjectIo.AddDocument(project, Named(name, $"Document {count}"), doc);
        // B85. Into the folder you were in. ProjectIo.AddDocument still puts it
        // at `documents/`, which is right when nothing is selected and was the
        // whole of the bug when something was — creating a document inside a
        // folder ignored the folder and filed it at the top level.
        if (TargetFolder is { } folder)
        {
            ProjectFolders.FileDocument(project.Manifest, reference, folder);
            _collapsed.Remove(folder.Id);
        }
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        _open(reference, doc);
        _changed();
    }

    /// <summary>
    /// Re-file a document under another character, or under the project when
    /// <paramref name="destination"/> is null. What a drag in the tree does.
    /// </summary>
    /// <remarks>
    /// The document keeps its id, so a tab already showing it stays bound to
    /// it — rearranging the tree must not orphan the window you are drawing
    /// in. The file follows it (B106): a move is a rename on disk, so the
    /// drawing ends up in one place rather than in both.
    /// </remarks>
    public bool Move(ProjectRow row, Character? destination)
    {
        if (Project is not { } project || row.Animation is not { } reference) return false;
        // Dropping a row onto what it already belongs to is an ordinary slip in
        // a tree view, not something to report — and separating it here is what
        // lets everything below say "could not" and mean it.
        if (ReferenceEquals(row.Character, destination)) return false;
        if (!ProjectIo.MoveDocument(project, reference, destination))
        {
            // B106. The move can now fail for a reason the artist can act on —
            // a file open elsewhere, a permission, a name already taken on disk
            // — and it refuses whole rather than changing the panel and not the
            // folder. Said out loud, because a drag that silently does nothing
            // is indistinguishable from one that missed.
            Status = $"Could not move “{reference.Name}”. It is still where it was.";
            return false;
        }
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        Status = destination is null
            ? $"Moved “{reference.Name}” to the project."
            : $"Moved “{reference.Name}” to {destination.Name}.";
        _changed();
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
        else if (row.Character is { } character)
        {
            project.Manifest.Characters.RemoveAll(c => c.Id == character.Id);
            Status = $"Removed “{character.Name}”. Its folder is still on disk.";
        }
        else
        {
            // A scene row. Guarded rather than crashed on: the old code read
            // `row.Character!` for anything that was not a document, which was
            // a null reference the moment a scene was selected and became far
            // easier to reach when folders arrived.
            Status = "Removing a scene from the docker is not wired up yet.";
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
    private void Detach(Project project, DocumentRef document)
    {
        foreach (var character in project.Manifest.Characters)
        {
            character.Animations.RemoveAll(a => a.Id == document.Id);
        }
        foreach (var scene in project.Manifest.Scenes ?? [])
        {
            scene.Shots.RemoveAll(s => s.Id == document.Id);
        }
        project.Manifest.Documents.RemoveAll(d => d.Id == document.Id);
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
            { Character: { } character } => Rename(character, trimmed),
            { Scene: { } scene } => Rename(scene, trimmed),
            _ => false,
        };
        if (!ok) return false;

        Rebuild();
        _changed();
        return true;
    }

    private bool Rename(Character character, string name)
    {
        // The character's folder is `characters/<slug>`, and moving it would
        // repath every animation under it — Q30's territory rather than this
        // bug's. The displayed name changes and the folder keeps its slug,
        // which is what every version until now also did.
        character.Name = name;
        Status = $"Renamed to “{name}”.";
        return true;
    }

    private bool Rename(ProjectScene scene, string name)
    {
        scene.Name = name;
        Status = $"Renamed to “{name}”.";
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
        return row.Character is { } character
            ? Path.Combine(project.Root, "characters", character.Slug)
            : project.Root;
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
        var reference = row.Character is { } owner
            ? ProjectIo.AddAnimation(project, owner, $"{source.Name} copy", copy)
            : ProjectIo.AddDocument(project, $"{source.Name} copy", copy);
        _dirty.Add(reference.Id);
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.Animation?.Id == reference.Id);
        Status = $"Copied to “{reference.Name}”. Save to write it to disk.";
        _changed();
    }
}
