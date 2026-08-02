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

    public ProjectRow(ProjectScene scene, DocumentRef shot, string? duration)
    {
        Scene = scene;
        Animation = shot;
        Duration = duration;
        _name = shot.Name;
    }

    /// <summary>
    /// Null on a document that belongs to the project rather than to any
    /// character — a background, a colour test, a one-off illustration.
    /// </summary>
    public Character? Character { get; }

    /// <summary>The scene this row is, or the one a shot sits in.</summary>
    public ProjectScene? Scene { get; }

    /// <summary>Null on a character or scene row.</summary>
    public DocumentRef? Animation { get; }

    /// <summary>
    /// How long it runs, already formatted, or null when nothing knows.
    /// </summary>
    /// <remarks>
    /// Null rather than "0:00". A running time that quietly reports the shots
    /// it could not measure as zero is the number somebody schedules against.
    /// </remarks>
    public string? Duration { get; }

    public bool HasDuration => Duration is { Length: > 0 };

    public bool IsScene => Scene is not null && Animation is null;

    public bool IsCharacter => Animation is null && Scene is null;

    /// <summary>A heading row — a character or a scene.</summary>
    public bool IsHeading => Animation is null;

    /// <summary>A document with no character and no scene above it.</summary>
    public bool IsLoose => Animation is not null && Character is null && Scene is null;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The name is being edited in place.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>A heading reads as a heading; what is under it is indented.</summary>
    public double Indent => IsHeading || IsLoose ? 0 : 14;

    public string Glyph => IsScene ? "🎬" : IsCharacter ? "🗀" : "▣";

    /// <summary>The id this row is remembered by across a rebuild.</summary>
    internal string? Key => Animation?.Id ?? Scene?.Id ?? Character?.Id;
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
public sealed partial class ProjectViewModel : ObservableObject
{
    private readonly Func<Doc> _newDocument;
    private readonly Action<DocumentRef, Doc> _open;
    private readonly Action _changed;

    public ProjectViewModel(Func<Doc> newDocument, Action<DocumentRef, Doc> open, Action changed)
    {
        _newDocument = newDocument;
        _open = open;
        _changed = changed;
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

    public void Adopt(Project? project)
    {
        Project = project;
        _dirty.Clear();
        Rebuild();
    }

    /// <summary>Note that a document changed, so the next save writes it and only it.</summary>
    public void MarkDirty(DocumentRef reference) => _dirty.Add(reference.Id);

    public void MarkAllSaved() => _dirty.Clear();

    partial void OnProjectChanged(Project? value)
    {
        Rebuild();
        OnPropertyChanged(nameof(HasScenes));
    }

    private void Rebuild()
    {
        var keep = Selected?.Key;
        Rows.Clear();
        foreach (var character in Project?.Characters ?? [])
        {
            Rows.Add(new ProjectRow(character));
            foreach (var animation in character.Animations) Rows.Add(new ProjectRow(character, animation));
        }
        // Scenes after the characters, because the characters are what a
        // project is named after and a film's shot list is the second axis
        // rather than the first. Absent entirely when there are none.
        foreach (var scene in Project?.Scenes ?? [])
        {
            Rows.Add(new ProjectRow(scene, RunningTime(scene)));
            foreach (var shot in scene.Shots) Rows.Add(new ProjectRow(scene, shot, ShotTime(shot)));
        }
        // Project-level documents last, unindented — they belong to the
        // project, not under anything.
        foreach (var document in Project?.Manifest.Documents ?? [])
        {
            Rows.Add(new ProjectRow(null, document));
        }
        Selected = Rows.FirstOrDefault(r => r.Key == keep);
        OnPropertyChanged(nameof(HasScenes));
        OnPropertyChanged(nameof(TotalRunningTime));
    }

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
    private void AddCharacter()
    {
        if (Project is not { } project) return;
        var character = ProjectIo.AddCharacter(project, $"Character {project.Characters.Count() + 1}");
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
    private void AddAnimation()
    {
        if (Project is not { } project) return;
        var character = SelectedCharacter ?? ProjectIo.AddCharacter(project, "Character 1");

        var doc = _newDocument();
        var reference = ProjectIo.AddAnimation(
            project, character, $"Animation {character.Animations.Count}", doc);
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
    public sealed record NewItemKind(string Label, string Hint)
    {
        public override string ToString() => Label;
    }

    public static readonly NewItemKind NewAnimation =
        new("Animation", "A drawing sequence under the selected character");

    public static readonly NewItemKind NewCharacterItem =
        new("Character", "A new character, with its own animations and palette");

    public static readonly NewItemKind NewLooseDocument =
        new("Document", "Belongs to the project, not to any character");

    public static readonly NewItemKind NewSceneItem =
        new("Scene", "A run of shots — the film's second axis, alongside the characters");

    public static readonly NewItemKind NewShotItem =
        new("Shot", "A drawing under the selected scene");

    public IReadOnlyList<NewItemKind> NewItemKinds { get; } =
        [NewAnimation, NewCharacterItem, NewSceneItem, NewShotItem, NewLooseDocument];

    /// <summary>Create one of <see cref="NewItemKinds"/> in the right place.</summary>
    [RelayCommand]
    public void AddItem(NewItemKind? kind)
    {
        if (kind == NewCharacterItem) AddCharacter();
        else if (kind == NewSceneItem) AddScene();
        else if (kind == NewShotItem) AddShot();
        else if (kind == NewLooseDocument) AddLooseDocument();
        else AddAnimation();
    }

    // ---- scenes -----------------------------------------------------------------

    /// <summary>The scene to add a shot to: the selected row's, or the last one.</summary>
    public ProjectScene? SelectedScene => Selected?.Scene ?? Project?.Scenes.LastOrDefault();

    [RelayCommand]
    private void AddScene()
    {
        if (Project is not { } project) return;
        var scene = ProjectIo.AddScene(project, $"Scene {project.Scenes.Count + 1}");
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.IsScene && r.Scene!.Id == scene.Id);
        _changed();
    }

    /// <summary>
    /// A shot under the selected scene, creating the first scene if there is
    /// none — the same bargain adding an animation with no character makes.
    /// </summary>
    [RelayCommand]
    private void AddShot()
    {
        if (Project is not { } project) return;
        var scene = SelectedScene ?? ProjectIo.AddScene(project, "Scene 1");

        var doc = _newDocument();
        var reference = ProjectIo.AddShot(project, scene, $"Shot {scene.Shots.Count + 1}", doc);
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
    private void AddLooseDocument()
    {
        if (Project is not { } project) return;
        var doc = _newDocument();
        var count = project.Manifest.Documents.Count + 1;
        var reference = ProjectIo.AddDocument(project, $"Document {count}", doc);
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
    /// in. The file on disk stays where it is until the next save writes it to
    /// the new path; the old one is left alone, for the same reason removing a
    /// row leaves it alone.
    /// </remarks>
    public bool Move(ProjectRow row, Character? destination)
    {
        if (Project is not { } project || row.Animation is not { } reference) return false;
        if (!ProjectIo.MoveDocument(project, reference, destination)) return false;
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
        if (row.Animation is { } animation)
        {
            row.Character?.Animations.RemoveAll(a => a.Id == animation.Id);
            project.Manifest.Documents.RemoveAll(d => d.Id == animation.Id);
            project.Loaded.Remove(animation.Id);
            _dirty.Remove(animation.Id);
            // The file is deliberately left on disk. Removing a row from an
            // index is cheap to undo by hand; deleting an artist's drawing
            // because they clicked the wrong row is not.
            Status = $"Removed “{animation.Name}” from the project. Its file is still on disk.";
        }
        else
        {
            project.Manifest.Characters.RemoveAll(c => c.Id == row.Character!.Id);
            Status = $"Removed “{row.Character!.Name}”. Its folder is still on disk.";
        }
        Rebuild();
        _changed();
    }

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

    /// <summary>Rename the selected row's character or animation in place.</summary>
    public void Rename(ProjectRow row, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        if (row.Animation is { } animation) animation.Name = trimmed;
        else row.Character!.Name = trimmed;
        row.Name = trimmed;
        _changed();
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
        return row.Character is { } character
            ? Path.Combine(project.Root, "characters", character.Slug)
            : project.Root;
    }

    public string? SelectedPath => PathOf(Selected);

    /// <summary>Show the project folder in the desktop's file manager.</summary>
    [RelayCommand]
    private void RevealRoot()
    {
        if (RootPath is not { } root) return;
        if (!Services.FileReveal.Reveal(root)) Status = "Could not open the file manager.";
    }

    /// <summary>Show the selected row's file or folder in the file manager.</summary>
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
