using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>One row in the project tree: a character, or an animation under one.</summary>
public sealed partial class ProjectRow : ObservableObject
{
    public ProjectRow(Character character)
    {
        Character = character;
        _name = character.Name;
    }

    public ProjectRow(Character owner, DocumentRef animation)
    {
        Character = owner;
        Animation = animation;
        _name = animation.Name;
    }

    public Character Character { get; }

    /// <summary>Null on a character row.</summary>
    public DocumentRef? Animation { get; }

    public bool IsCharacter => Animation is null;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>A character reads as a heading; its animations are indented under it.</summary>
    public double Indent => IsCharacter ? 0 : 14;
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

    partial void OnProjectChanged(Project? value) => Rebuild();

    private void Rebuild()
    {
        var keep = Selected?.Animation?.Id ?? Selected?.Character.Id;
        Rows.Clear();
        foreach (var character in Project?.Characters ?? [])
        {
            Rows.Add(new ProjectRow(character));
            foreach (var animation in character.Animations) Rows.Add(new ProjectRow(character, animation));
        }
        Selected = Rows.FirstOrDefault(r => (r.Animation?.Id ?? r.Character.Id) == keep);
    }

    // ---- commands -----------------------------------------------------------

    [RelayCommand]
    private void AddCharacter()
    {
        if (Project is not { } project) return;
        var character = ProjectIo.AddCharacter(project, $"Character {project.Characters.Count() + 1}");
        Rebuild();
        Selected = Rows.FirstOrDefault(r => r.IsCharacter && r.Character.Id == character.Id);
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

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Project is not { } project || Selected is not { } row) return;
        if (row.Animation is { } animation)
        {
            row.Character.Animations.RemoveAll(a => a.Id == animation.Id);
            project.Loaded.Remove(animation.Id);
            _dirty.Remove(animation.Id);
            // The file is deliberately left on disk. Removing a row from an
            // index is cheap to undo by hand; deleting an artist's drawing
            // because they clicked the wrong row is not.
            Status = $"Removed “{animation.Name}” from the project. Its file is still on disk.";
        }
        else
        {
            project.Manifest.Characters.RemoveAll(c => c.Id == row.Character.Id);
            Status = $"Removed “{row.Character.Name}”. Its folder is still on disk.";
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
        else row.Character.Name = trimmed;
        row.Name = trimmed;
        _changed();
    }
}
