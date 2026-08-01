using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>Settings collected by the File → New dialog.</summary>
public sealed record NewDocumentSettings(
    string Name,
    int Width,
    int Height,
    int Fps,
    int Ppi,
    string BackgroundColor,
    bool TransparentBackground);

public enum DocumentTabKind
{
    Animation,
    Reference,
}

/// <summary>
/// One open document: its editor (which owns the undo history, so switching
/// tabs never loses it), where it was saved, and whether it has unsaved
/// changes (shown as a • in the tab strip).
///
/// A Reference tab edits a character-sheet view of its <see cref="Owner"/>
/// animation document: its editor wraps a scene that shares the view's layer
/// list, so edits land in the owning document (and dirty the owner, never
/// this tab).
/// </summary>
public sealed partial class DocumentTab : ObservableObject
{
    internal DocumentTab(DocumentEditor editor, string title)
    {
        Editor = editor;
        _title = title;
    }

    public DocumentTabKind Kind { get; init; } = DocumentTabKind.Animation;

    /// <summary>The animation tab whose document owns this reference view.</summary>
    public DocumentTab? Owner { get; init; }

    /// <summary>The character-sheet view this tab edits (Reference tabs only).</summary>
    public ReferenceView? View { get; init; }

    internal DocumentEditor Editor { get; set; }

    public Doc Doc => Editor.Doc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isActive;

    public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

    public string? FilePath { get; set; }

    /// <summary>
    /// The project slot this tab came from, or null for a standalone document.
    /// Alongside <see cref="FilePath"/> rather than instead of it: a project
    /// animation is saved by the project, a loose file by its own path, and
    /// Save has to know which it is looking at.
    /// </summary>
    public Lightbox.Core.Projects.DocumentRef? Source { get; set; }

    /// <summary>Playhead/layer selection remembered while another tab is active.</summary>
    internal int SavedFrameIndex;

    internal int SavedLayerIndex;
}
